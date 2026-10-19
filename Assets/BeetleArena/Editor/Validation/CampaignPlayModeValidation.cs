using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using BeetleArena;
using BeetleArena.Campaign;
using BugDuel.Campaign;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Runs against actual campaign scenes and the existing combat driver, with disposable player data.
[InitializeOnLoad]
public static class CampaignPlayModeValidation
{
    const string Key="BugDuel.CampaignAcceptance";
    public const string Output="Artifacts/Campaign/Acceptance";
    static CampaignPlayModeValidation()
    {
        string path=SessionState.GetString(Key,"");
        if(path.Length>0)CampaignApp.StorageOverride=path;
        EditorApplication.playModeStateChanged+=OnPlay;
    }
    [MenuItem("Tools/Beetle Arena/Validate Campaign Play Flow")]
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop the current game before acceptance.");
        for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save the current scene before acceptance.");
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output+"/BackgroundFocus.txt","");
        string path=Path.GetFullPath(Output+"/Save-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        SessionState.SetString(Key,path);CampaignApp.StorageOverride=path;
        File.WriteAllText(Output+"/Result.txt","RUNNING\nIsolated saves: "+path+"\n");
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Campaign/CampaignMainMenu.unity");
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Show();
        EditorApplication.isPlaying=true;
    }
    static void OnPlay(PlayModeStateChange state)
    {
        if(SessionState.GetString(Key,"").Length==0)return;
        if(state==PlayModeStateChange.EnteredPlayMode)
        {
            var go=new GameObject("Campaign acceptance");UnityEngine.Object.DontDestroyOnLoad(go);go.AddComponent<CampaignAcceptanceDriver>();
        }
        if(state==PlayModeStateChange.EnteredEditMode) { SessionState.EraseString(Key);CampaignApp.StorageOverride=null; }
    }
}

public sealed class CampaignAcceptanceDriver : MonoBehaviour
{
    CampaignApp app;
    Keyboard keyboard;
    InputSettings originalInput,acceptanceInput;
    readonly List<string> checks=new List<string>();
    readonly List<string> errors=new List<string>();
    readonly CareContactMonitor contact=new CareContactMonitor();
    const string Output=CampaignPlayModeValidation.Output;
    void Start()
    {
        Application.runInBackground=true;
        originalInput=InputSystem.settings;acceptanceInput=Instantiate(originalInput);InputSystem.settings=acceptanceInput;
        acceptanceInput.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        acceptanceInput.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        Application.logMessageReceived+=Log;StartCoroutine(SampleContacts());StartCoroutine(Guard(Run()));
    }
    IEnumerator SampleContacts() { while(true){yield return new WaitForEndOfFrame();contact.Sample();} }
    void Log(string message,string stack,LogType type) { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert||message.Contains("character with Unicode value"))errors.Add(message+"\n"+stack); }
    IEnumerator Guard(IEnumerator routine)
    {
        var stack=new Stack<IEnumerator>();stack.Push(routine);
        while(stack.Count>0)
        {
            object current=null;bool moved=false;Exception error=null;
            try { moved=stack.Peek().MoveNext(); if(moved)current=stack.Peek().Current; }
            catch(Exception e) { error=e; }
            if(error!=null) { Finish(error);yield break; }
            if(!moved) { (stack.Pop() as IDisposable)?.Dispose();continue; }
            if(current is IEnumerator nested)stack.Push(nested);else yield return current;
        }
        Finish(null);
    }
    void Check(bool ok,string name)
    {
        if(!ok)throw new InvalidOperationException(name+"; message="+app?.Message+"; modal="+app?.Modal+"; busy="+app?.Busy);
        checks.Add("PASS "+name);File.AppendAllText(Output+"/Result.txt","PASS "+name+"\n");
    }
    IEnumerator Wait(Func<bool> condition,string name,float timeout=25,bool settle=true)
    {
        float until=Time.realtimeSinceStartup+timeout;
        while(!condition()) { if(Time.realtimeSinceStartup>until)throw new TimeoutException(name+"; "+app?.Message);yield return null; }
        if(settle) { yield return null;yield return null; }
    }
    IEnumerator Idle() => Wait(()=>app&&!app.Busy,"pending transaction/transition");
    IEnumerator Page(CampaignPage page) => Wait(()=>app&&!app.Busy&&SceneManager.GetActiveScene().name==CampaignApp.SceneName(page),"scene "+page);
    Button Button(string name) => FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b=>b.gameObject.activeInHierarchy&&b.name==name) ?? throw new InvalidOperationException("Missing button: "+name);
    void Click(string name)
    {
        var b=Button(name);if(!b.IsInteractable())throw new InvalidOperationException("Disabled button: "+name);
        Click(b);
    }
    void Click(Button b)
    {
        if(!b.IsInteractable())throw new InvalidOperationException("Disabled button: "+b.name);
        EventSystem.current.SetSelectedGameObject(b.gameObject);
        ExecuteEvents.Execute(b.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);
    }
    Transform SaveCard(int slot) => FindFirstObjectByType<CampaignView>().GetComponentsInChildren<TMP_Text>()
        .Single(t=>t.text==(slot==0?"AUTO SAVE":"JOURNAL 0"+slot)).transform.parent;
    Button CreateButton(int slot) => SaveCard(slot).GetComponentsInChildren<Button>().Single(b=>b.name=="新建存档");
    void EmptySlot(int slot)
    {
        var card=SaveCard(slot);var controls=card.GetComponentsInChildren<Button>();var button=CreateButton(slot);
        var position=card.InverseTransformPoint(button.transform.TransformPoint(((RectTransform)button.transform).rect.center));
        Check(controls.Length==1&&card.GetComponentsInChildren<RawImage>().Length==0&&
            Vector2.Distance(position,((RectTransform)card).rect.center)<.1f,"empty slot "+slot+" has one centered create button and no thumbnail/disabled actions");
    }
    void BackgroundFocus(bool focused,bool inspectPixels=false)
    {
        var view=FindFirstObjectByType<CampaignView>();var backdrop=view.backdrop;
        Check(backdrop.UiFocused==focused&&backdrop.output.texture&&
            (focused?backdrop.output.texture!=backdrop.Frame:backdrop.output.texture==backdrop.Frame),"background focus policy: "+view.page);
        var targets=Resources.FindObjectsOfTypeAll<RenderTexture>().Where(t=>t.name=="Campaign focused background"||t.name=="Campaign focus scratch").ToArray();
        Check(targets.Length==(focused?2:0)&&targets.All(t=>t.width<=320&&t.depth==0),"bounded background targets, no stale page allocations: "+view.page);
        if(!inspectPixels)return;
        float sharp=BackgroundEdges(backdrop.Frame),soft=BackgroundEdges((RenderTexture)backdrop.output.texture);
        File.AppendAllText(Output+"/BackgroundFocus.txt",view.page+": clean edges="+sharp+", blurred edges="+soft+"\n");
        Check(sharp>.0001f&&soft>0&&soft<sharp*.85f,"rendered background has reduced edge detail: "+view.page);
    }
    static float BackgroundEdges(RenderTexture source)
    {
        const int w=320,h=180;var old=RenderTexture.active;var target=RenderTexture.GetTemporary(w,h,0);Texture2D read=null;
        try
        {
            Graphics.Blit(source,target);RenderTexture.active=target;read=new Texture2D(w,h,TextureFormat.RGB24,false);
            read.ReadPixels(new Rect(0,0,w,h),0,0);read.Apply();var pixels=read.GetPixels();float total=0;
            for(int y=1;y<h;y++)for(int x=1;x<w;x++)
            {
                var a=pixels[y*w+x];var b=pixels[y*w+x-1];var c=pixels[(y-1)*w+x];
                total+=Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b)+Mathf.Abs(a.r-c.r)+Mathf.Abs(a.g-c.g)+Mathf.Abs(a.b-c.b);
            }
            return total/((w-1)*(h-1)*6);
        }
        finally { RenderTexture.active=old;RenderTexture.ReleaseTemporary(target);if(read)Destroy(read); }
    }
    IEnumerator Key(Key key)
    {
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return null;yield return null;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;yield return null;
    }
    IEnumerator Capture(string name,bool transitionFrame=false)
    {
        var game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));game.Focus();game.Repaint();
        if(!transitionFrame) { yield return null;yield return null; }
        yield return new WaitForEndOfFrame();
        // ScreenCapture reads a black backbuffer in this Editor/URP configuration.
        // Read the Game View's actual composed target (including screen-space UI) instead.
        RenderTexture target=null;
        for(Type type=game.GetType();type!=null && !target;type=type.BaseType)
        {
            var field=type.GetField("m_TargetTexture",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)
                ?? type.GetField("m_RenderTexture",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
            if(field!=null)target=field.GetValue(game) as RenderTexture;
        }
        if(!target)throw new InvalidOperationException("Game View has no composed render target");
        var old=RenderTexture.active;Texture2D texture=null;
        try
        {
            RenderTexture.active=target;texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();
            var pixels=texture.GetPixels32();
            Check(pixels.Where((p,i)=>i%41==0).Select(p=>(int)p.r+(p.g<<8)+(p.b<<16)).Distinct().Take(33).Count()>32,"rendered scene and UI: "+name);
            // Direct3D's Editor Game View target is stored top-down.
            if(SystemInfo.graphicsUVStartsAtTop)
            {
                var upright=new Color32[pixels.Length];
                for(int y=0;y<target.height;y++)Array.Copy(pixels,y*target.width,upright,(target.height-1-y)*target.width,target.width);
                texture.SetPixels32(upright);texture.Apply();
            }
            File.WriteAllBytes(Output+"/"+name+".png",texture.EncodeToPNG());
            if(transitionFrame)
            {
                var top=pixels[target.width/2];var bottom=pixels[pixels.Length-target.width/2];
                var transition=app.GetComponent<CampaignSceneTransition>();
                File.WriteAllLines(Output+"/TransitionGeometry.txt",FindObjectsByType<Image>(FindObjectsSortMode.None).Where(i=>i.canvas&&i.canvas.sortingOrder==1000)
                    .Select(i=> {
                        var corners=new Vector3[4];i.rectTransform.GetWorldCorners(corners);
                        var meshMethod=typeof(CanvasRenderer).GetMethod("GetMesh",Type.EmptyTypes);
                        var mesh=meshMethod?.Invoke(i.canvasRenderer,null) as Mesh;
                        return i.name+" enabled="+i.isActiveAndEnabled+" rect="+i.rectTransform.rect+" pos="+i.rectTransform.anchoredPosition+" color="+i.color+" canvas="+i.canvas?.sortingOrder+" alpha="+i.canvasRenderer.GetInheritedAlpha()+" culled="+i.canvasRenderer.cull+" shader="+i.materialForRendering.shader.name+
                            " canvasEnabled="+i.canvas.enabled+" scene="+i.gameObject.scene.name+" root="+i.canvas.rootCanvas.name+" corners="+string.Join(";",corners)+" canvasScale="+i.canvas.transform.lossyScale+" vertices="+(mesh?mesh.vertexCount:-1)+" materials="+i.canvasRenderer.materialCount+" renderColor="+i.canvasRenderer.GetColor()+" renderMode="+i.canvas.renderMode+" order="+i.canvas.renderOrder+" display="+i.canvas.targetDisplay;
                    }));
                File.AppendAllLines(Output+"/TransitionGeometry.txt",FindObjectsByType<Canvas>(FindObjectsSortMode.None).Select(c=>c.name+" sorting="+c.sortingOrder+" render="+c.renderOrder+" root="+c.isRootCanvas+" mode="+c.renderMode));
                if(transition.ActiveMode==CampaignTransitionMode.Television)
                    Check(top.r<4&&top.g<4&&top.b<4&&bottom.r<4&&bottom.g<4&&bottom.b<4,
                        "TV upper/lower shutters visible around the opening; progress="+transition.OpenProgress+" top="+top+" bottom="+bottom);
                else
                {
                    var overlay=FindObjectsByType<Canvas>(FindObjectsSortMode.None).Single(c=>c.name=="SceneFade");
                    float alpha=overlay.GetComponentInChildren<CanvasGroup>().alpha;
                    Check(alpha>0&&alpha<1&&overlay.transform.Find("Fade/Cover").gameObject.activeSelf&&!overlay.transform.Find("Fade/UpperShutter").gameObject.activeSelf,"menu exits through a partially transparent full-screen black cover");
                }
            }
            var clipping=FindFirstObjectByType<CampaignView>().GetComponentsInChildren<TMP_Text>()
                .Where(t=>t.isTextTruncated).Select(t=>t.text.Replace("\n"," / ")+" | box="+t.rectTransform.sizeDelta+" font="+t.fontSize).ToArray();
            File.WriteAllLines(Output+"/"+name+"-TextClipping.txt",clipping);
            Check(clipping.Length==0,"no clipped text: "+name);
        }
        finally { RenderTexture.active=old;if(texture)Destroy(texture); }
    }
    void Opaque()
    {
        var view=FindFirstObjectByType<CampaignView>();
        foreach(var image in view.GetComponentsInChildren<Image>())
        {
            if(image.name=="Viewport")continue;
            if(image.color.a!=1)throw new InvalidOperationException("Translucent surface: "+image.name);
            if(image.material.shader.name!="UI/Default")throw new InvalidOperationException("Non-UI shader: "+image.name);
        }
        Check(true,"opaque UI surfaces and standard UI material: "+view.page);
    }
    IEnumerator Run()
    {
        yield return null;app=CampaignApp.Ensure();keyboard=InputSystem.AddDevice<Keyboard>("CampaignAcceptanceKeyboard");
        Check(app.SaveDirectory.StartsWith(Path.GetFullPath(Output),StringComparison.OrdinalIgnoreCase),"isolated save directory");
        yield return Page(CampaignPage.MainMenu);Opaque();BackgroundFocus(false);yield return Capture("01-MainMenu");
        Click("Setting  /  设置");yield return null;Click("Video");yield return null;Click("电视开幕");yield return null;
        Check(app.Preferences.transitionMode==CampaignTransitionMode.BlackFade,"video setting selects black fade");
        yield return Capture("01a-TransitionSettings");Click("保存并关闭");yield return null;
        Check(JsonUtility.FromJson<CampaignPreferences>(File.ReadAllText(Path.Combine(app.SaveDirectory,"settings.json"))).transitionMode==CampaignTransitionMode.BlackFade,"transition selection saves to preferences");
        Click("Setting  /  设置");yield return null;Click("黑屏淡入淡出");yield return null;Click("保存并关闭");yield return null;
        Check(app.GetComponent<CampaignSceneTransition>().Mode==CampaignTransitionMode.Television,"video setting switches back to TV opening");
        Click("Start  /  开始");
        var transition=app.GetComponent<CampaignSceneTransition>();
        yield return Wait(()=>transition.Opening&&transition.OpenProgress>.25f,"black fade opening",25,false);
        Check(app.Busy&&transition.ActiveMode==CampaignTransitionMode.BlackFade,"menu transition uses black fade and blocks input until complete");yield return Capture("01b-BlackFade",true);
        yield return Page(CampaignPage.Saves);
        for(int slot=0;slot<4;slot++)EmptySlot(slot);
        Check(!FindObjectsByType<Button>(FindObjectsSortMode.None).Any(b=>b.name=="新建旅程"||b.name=="New journey"),"no separate new-journey entry");
        Opaque();BackgroundFocus(true,true);yield return Capture("01c-EmptySaves");
        var saveBackdrop=FindFirstObjectByType<CampaignBackdrop>();saveBackdrop.SetUiFocus(false);yield return null;yield return null;BackgroundFocus(false);
        saveBackdrop.SetUiFocus(true);yield return null;yield return null;BackgroundFocus(true);
        string newTemp=app.SlotPath(1)+".tmp";
        using(var locked=new FileStream(newTemp,FileMode.Create,FileAccess.ReadWrite,FileShare.None))
        {
            Click(CreateButton(1));yield return Idle();
            Check(app.Session==null&&!File.Exists(app.SlotPath(1))&&SceneManager.GetActiveScene().name==CampaignApp.SceneName(CampaignPage.Saves),"failed create keeps the slot empty and stays on saves");
        }
        if(File.Exists(newTemp))File.Delete(newTemp);
        Click(CreateButton(0));yield return Page(CampaignPage.Greenhouse);BackgroundFocus(false);
        Check(app.ReadSlot(0)?.state.beetles.Count==2,"empty auto slot creates a durable new game");
        app.Go(CampaignPage.Saves);yield return Page(CampaignPage.Saves);
        var original=app.Session;string priorAuto=File.ReadAllText(app.SlotPath(0));
        Click(CreateButton(1));yield return null;Click("取消");yield return null;
        Check(app.Session==original&&!File.Exists(app.SlotPath(1)),"cancel new journey preserves the current game and empty slot");
        Click(CreateButton(1));yield return null;Click("确定");yield return Page(CampaignPage.Greenhouse);
        Check(app.Session.State.beetles.Count==2,"new game through save-slot UI");
        Check(app.ReadSlot(1)?.state.selectedId==app.Session.State.selectedId&&File.ReadAllText(app.SlotPath(0))==priorAuto,"new game is durable in the chosen manual slot before subsequent autosaves");
        original=app.Session;app.NewGame(1);yield return null;
        Check(app.Session==original&&!app.Busy,"occupied slots cannot be silently replaced by create");
        var clock=FindFirstObjectByType<CampaignLocalClock>();
        Check(clock&&clock.GetComponent<TMP_Text>().text==CampaignLocalClock.Format(DateTime.Now)&&CampaignLocalClock.Format(new DateTime(2026,10,19,23,5,0))=="10月19日\n23:05","upper-right local date and 24-hour clock");
        string id=app.Session.State.selectedId;long coins=app.Session.State.coins;
        var button=Button("红薯 15 G");int identity=button.GetInstanceID();
        EventSystem.current.SetSelectedGameObject(button.gameObject);yield return null;
        var scroll=button.GetComponentInParent<ScrollRect>();float y=scroll.content.anchoredPosition.y;
        Click("红薯 15 G");yield return Idle();
        Check(app.Session.State.coins==coins-15&&app.Session.Beetle(id).meal!=null&&app.Session.Beetle(id).nutrition==85,"feeding places durable food without instant nutrition");
        yield return Wait(()=>!app.Busy&&app.Session.Beetle(id).meal==null,"actual beetle reaches and eats food");
        Check(app.Session.State.coins==coins-15 && app.Session.Beetle(id).nutrition==100,"feeding commits nutrition and cost");
        Check(Button("红薯 15 G").GetInstanceID()==identity && Mathf.Abs(scroll.content.anchoredPosition.y-y)<1,"refresh preserves controls and scroll position");
        Check(EventSystem.current.currentSelectedGameObject&&EventSystem.current.currentSelectedGameObject.GetComponent<Selectable>().IsInteractable(),"feeding refresh keeps focus on an enabled control");
        Opaque();yield return Capture("02-Greenhouse");
        yield return CampaignCarePlayChecks.Run(app,Check,name=>Capture(name));
        app.Execute(s=>{var b=s.Beetle(id);b.overturned=true;b.overturnSeconds=65;b.hearts=4;});yield return Idle();
        yield return Key(UnityEngine.InputSystem.Key.H);yield return Idle();Check(!app.Session.Beetle(id).overturned,"keyboard H rescues a struggling beetle");
        Click("更换养殖环境");yield return null;yield return null;
        var modal=FindFirstObjectByType<CampaignView>().transform.Find("CampaignCanvas/Modal");
        Check(app.Modal&&EventSystem.current.currentSelectedGameObject.transform.IsChildOf(modal),"modal captures controller focus");
        int week=app.Session.State.week;yield return Key(UnityEngine.InputSystem.Key.N);
        Check(app.Session.State.week==week&&!Button("下一周  N").IsInteractable(),"modal blocks shortcuts and background navigation");
        Opaque();yield return Capture("03-Housing");
        var housing=modal.GetComponentsInChildren<Button>().First(b=>b.name.StartsWith("安静恢复箱"));
        ExecuteEvents.Execute(housing.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);yield return Idle();
        Check(app.Session.Beetle(id).habitat=="recovery","housing choice commits");
        coins=app.Session.State.coins;var before=app.Session;
        string temp=app.SlotPath(0)+".tmp";
        using(var locked=new FileStream(temp,FileMode.Create,FileAccess.ReadWrite,FileShare.None))
        {
            Check(app.Execute(s=>s.Feed(id,0)),"accept transaction while disk failure is injected");
            Check(!app.Execute(s=>s.Feed(id,0)),"reject overlapping transaction");
            yield return Idle();
            Check(app.Session==before&&app.Session.State.coins==coins&&app.Message.Contains("未提交"),"disk failure rolls back memory, currency and items");
        }
        if(File.Exists(temp))File.Delete(temp);
        app.Execute(s=>s.Feed(id,0));yield return Idle();Check(app.Session.State.coins==coins-15,"transaction succeeds after disk recovery");
        Click("配种 100 G · 1个后代");yield return Idle();
        var child=app.Session.State.beetles.Last();string childId=child.id;
        Check(app.Session.State.beetles.Count==3&&!child.Adult(app.Session.State.week),"breeding through greenhouse UI");
        Click("暖气 2周 ×0.88");yield return null;Click("开始养成");yield return Idle();
        yield return Key(UnityEngine.InputSystem.Key.N);yield return Idle();yield return Key(UnityEngine.InputSystem.Key.N);yield return Idle();
        Check(app.Session.Beetle(childId).Adult(app.Session.State.week),"larva grows to adult across two weeks");
        app.Go(CampaignPage.Catching);yield return Page(CampaignPage.Catching);BackgroundFocus(true);Click("派遣捕虫队");yield return Idle();
        Check(app.Session.Reserved==2,"expedition reserves capacity");yield return Key(UnityEngine.InputSystem.Key.N);yield return Idle();
        Check(app.Session.State.expeditions.Count==0&&app.Session.State.beetles.Count==5,"expedition returns into reserved slots");
        app.Go(CampaignPage.Market);yield return Page(CampaignPage.Market);
        Click("树液蜜 120 G  ·  库存 0");yield return Idle();Check(app.Session.State.sap==1,"market purchase");Opaque();BackgroundFocus(true,true);yield return Capture("04-Market");
        app.Select(id);app.Go(CampaignPage.MainMenu);yield return Page(CampaignPage.MainMenu);week=app.Session.State.week;
        string saved=File.ReadAllText(app.SlotPath(0));yield return Key(UnityEngine.InputSystem.Key.N);yield return Key(UnityEngine.InputSystem.Key.H);
        Check(app.Session.State.week==week&&File.ReadAllText(app.SlotPath(0))==saved,"main menu shortcuts cannot mutate session or autosave");
        Click("Start  /  开始");yield return Page(CampaignPage.Saves);yield return Key(UnityEngine.InputSystem.Key.N);yield return Key(UnityEngine.InputSystem.Key.H);
        Check(app.Session.State.week==week&&File.ReadAllText(app.SlotPath(0))==saved,"save screen shortcuts cannot mutate session or autosave");
        EmptySlot(2);EmptySlot(3);yield return Capture("04b-PopulatedSaves");
        Check(app.SaveSlot(1),"manual slot writes");File.WriteAllText(app.SlotPath(3),"{broken");before=app.Session;app.LoadSlot(3);
        Check(app.Session==before&&!app.Busy,"corrupt save preserves current session");app.LoadSlot(1);yield return Page(CampaignPage.Greenhouse);
        Check(app.Session.State.week==week,"manual save reloads");
        var catalog=JsonUtility.FromJson<CampaignCatalog>(JsonUtility.ToJson(app.Session.Catalog));catalog.revision++;
        catalog.housing=catalog.housing.Concat(new[]{new HousingRule("acceptance","测试养殖箱",25,70,.5,0)}).ToArray();
        catalog.species=catalog.species.Concat(new[]{new SpeciesRule("acceptance-beetle","测试甲虫",50,200,.3)}).ToArray();
        File.WriteAllText(app.ContentPath,JsonUtility.ToJson(catalog));app.ReloadContent();yield return Idle();
        Check(app.Session.Catalog.revision==catalog.revision&&app.Session.Catalog.Species("acceptance-beetle")!=null,"species and housing hot update commits atomically");
        app.Execute(s=>s.House(id,"acceptance"));yield return Idle();before=app.Session;
        catalog.revision++;catalog.housing=catalog.housing.Where(h=>h.id!="acceptance").ToArray();File.WriteAllText(app.ContentPath,JsonUtility.ToJson(catalog));
        app.ReloadContent();yield return Idle();Check(app.Session==before,"hot update rejects removal of an occupied environment");
        File.WriteAllText(app.ContentPath,JsonUtility.ToJson(app.Session.Catalog));
        app.Go((CampaignPage)999);yield return null;Check(!app.Busy,"invalid transition leaves input and fade usable");
        app.Execute(s=>{s.House(id,"recovery");s.Feed(id,0);s.State.random=1987;});yield return Idle();
        yield return Wait(()=>!app.Busy&&app.Session.Beetle(id).meal==null,"pre-match meal completes");
        app.Select(id);app.Go(CampaignPage.League);yield return Page(CampaignPage.League);BackgroundFocus(true);
        var entry=FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b=>b.name.StartsWith("报名  ")).OrderBy(b=>b.transform.parent.GetSiblingIndex()).First();
        ExecuteEvents.Execute(entry.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);yield return Page(CampaignPage.Arena);
        BackgroundFocus(false);
        var bridge=FindFirstObjectByType<CampaignLeagueBridge>();Check(bridge.controller.Simulation!=null,"actual combat simulation starts from persisted reservation");
        string matchId=app.Session.State.battle.id;coins=app.Session.State.coins;
        bridge.CycleSpeed();bridge.CycleSpeed();yield return Capture("05-Arena");
        yield return Wait(()=>bridge.IsFinished,"real match completion",300);
        Check(app.Session.State.battle==null,"real combat result commits and clears reservation");
        long after=app.Session.State.coins;int wins=app.Session.Beetle(id).wins;
        Check(!app.Execute(s=>s.SettleBattle(matchId,true))&&app.Session.State.coins==after,"duplicate battle settlement is rejected");
        yield return Capture("06-Result");Check(app.SaveSlot(2),"save completed battle");app.LoadSlot(2);yield return Page(CampaignPage.Greenhouse);
        Check(app.Session.State.battle==null&&app.Session.State.coins==after&&app.Session.Beetle(id).wins==wins,"battle reward persists once across reload");
        app.Go(CampaignPage.Auction);yield return Page(CampaignPage.Auction);Opaque();BackgroundFocus(true);yield return Capture("07-Auction");
        Check(errors.Count==0,"no runtime errors or exceptions");
        contact.SaveReport();
        Check(contact.Samples>500&&contact.Failure==null,"live greenhouse foot contact, support and soil boundaries: "+contact.Failure);
    }
    void Finish(Exception failure)
    {
        contact.SaveReport();
        if(keyboard!=null)InputSystem.RemoveDevice(keyboard);
        if(originalInput)InputSystem.settings=originalInput;if(acceptanceInput)Destroy(acceptanceInput);
        Application.logMessageReceived-=Log;
        File.AppendAllText(Output+"/Result.txt",(failure==null?"COMPLETE PASS":"FAILED "+failure)+"\n"+string.Join("\n",errors));
        EditorApplication.isPlaying=false;
    }
}
