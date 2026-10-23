using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BeetleArena.Campaign;
using BugDuel.Campaign;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using CampaignSpecimen=BeetleArena.Campaign.CampaignSpecimen;

[InitializeOnLoad]
public static class CampaignConstructionPlayValidation
{
    const string Key="BugDuel.ConstructionPlayValidation";
    public const string Output="Artifacts/Construction/Play";
    static CampaignConstructionPlayValidation()
    {
        string path=SessionState.GetString(Key,"");if(path.Length>0)CampaignApp.StorageOverride=path;
        EditorApplication.playModeStateChanged+=OnPlay;
        EditorApplication.update+=()=>{if(SessionState.GetString(Key,"").Length>0&&EditorApplication.isPlaying)EditorApplication.QueuePlayerLoopUpdate();};
    }
    [MenuItem("Tools/Beetle Arena/Validate Construction Play Flow")]
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before the isolated construction validation.");
        Directory.CreateDirectory(Output);
        var session=CampaignSession.New(CampaignCatalog.Defaults(),81542);session.RenamePlayer("验收玩家");session.ConfigureClock(600,1,true);
        foreach(var beetle in session.State.beetles)beetle.habitat="recovery";session.Validate();
        string path=Path.GetFullPath(Output+"/Save-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        new CampaignSaveRepository(path).Write(0,new CampaignSave {state=session.State,catalog=session.Catalog});
        File.WriteAllText(Output+"/Result.txt","RUNNING\nIsolated saves: "+path+"\n");
        SessionState.SetString(Key,path);CampaignApp.StorageOverride=path;
        SessionState.SetString(Key+".start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Campaign/CampaignMainMenu.unity");
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Show();EditorApplication.isPlaying=true;
    }
    static void OnPlay(PlayModeStateChange change)
    {
        if(SessionState.GetString(Key,"").Length==0)return;
        if(change==PlayModeStateChange.EnteredPlayMode)
        {var go=new GameObject("Construction acceptance");UnityEngine.Object.DontDestroyOnLoad(go);go.AddComponent<CampaignConstructionPlayDriver>();}
        if(change==PlayModeStateChange.EnteredEditMode)
        {
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key+".start",""));
            SessionState.EraseString(Key);SessionState.EraseString(Key+".start");CampaignApp.StorageOverride=null;
        }
    }
}

public sealed class CampaignConstructionPlayDriver:MonoBehaviour
{
    const string Output=CampaignConstructionPlayValidation.Output;
    CampaignApp app;
    Mouse mouse;Keyboard keyboard;
    InputSettings originalInput,testInput;
    float originalVolume;bool originalAudioPause;
    readonly List<string> errors=new List<string>();
    CampaignView View=>FindFirstObjectByType<CampaignView>();
    CampaignConstructionController Construction=>FindFirstObjectByType<CampaignConstructionController>();
    CampaignCurrencyHud Hud=>FindFirstObjectByType<CampaignCurrencyHud>();
    CampaignCurrencyAudio CoinAudio=>app.GetComponent<CampaignCurrencyAudio>();
    void Start()
    {
        Application.runInBackground=true;Application.logMessageReceived+=Log;
        originalInput=InputSystem.settings;testInput=Instantiate(originalInput);InputSystem.settings=testInput;
        testInput.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        testInput.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        originalVolume=AudioListener.volume;originalAudioPause=AudioListener.pause;
        mouse=InputSystem.AddDevice<Mouse>("Construction validation mouse");keyboard=InputSystem.AddDevice<Keyboard>("Construction validation keyboard");
        StartCoroutine(Guard(Run()));
    }
    void Log(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+trace);}
    IEnumerator Guard(IEnumerator routine)
    {
        var stack=new Stack<IEnumerator>();stack.Push(routine);
        while(stack.Count>0)
        {
            bool moved=false;object current=null;Exception failure=null;
            try{moved=stack.Peek().MoveNext();if(moved)current=stack.Peek().Current;}catch(Exception error){failure=error;}
            if(failure!=null){Finish(failure);yield break;}
            if(!moved){(stack.Pop() as IDisposable)?.Dispose();continue;}
            if(current is IEnumerator nested)stack.Push(nested);else yield return current;
        }
        Finish(null);
    }
    void Check(bool pass,string message)
    {if(!pass)throw new InvalidOperationException(message+" | "+app?.Message+" | preview="+Construction?.PreviewReason);File.AppendAllText(Output+"/Result.txt","PASS "+message+"\n");}
    IEnumerator Wait(Func<bool> condition,string label,bool settle=true)
    {
        float until=Time.realtimeSinceStartup+40;
        while(!condition()){if(Time.realtimeSinceStartup>until)throw new TimeoutException(label+" | "+app?.Message);yield return null;}
        if(settle){yield return null;yield return null;}
    }
    IEnumerator Idle()=>Wait(()=>app&&!app.Busy,"commit");
    IEnumerator Page(CampaignPage page)=>Wait(()=>app&&!app.Busy&&SceneManager.GetActiveScene().name==CampaignApp.SceneName(page),page.ToString());
    void Click(string caption)
    {
        var button=FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b=>b.gameObject.activeInHierarchy&&b.name==caption&&b.IsInteractable());
        if(!button)throw new InvalidOperationException("Missing enabled button: "+caption);
        Click(button);
    }
    void Click(Button button)
    {
        if(!button.IsInteractable())throw new InvalidOperationException("Disabled button: "+button.name);
        EventSystem.current.SetSelectedGameObject(button.gameObject);
        ExecuteEvents.Execute(button.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);
    }
    void InvokeView(string method,params object[] args)=>typeof(CampaignView).GetMethods(BindingFlags.Instance|BindingFlags.NonPublic).First(m=>m.Name==method&&m.GetParameters().Length==args.Length).Invoke(View,args);
    IEnumerator KeyPress(Key key)
    {
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return null;yield return null;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;yield return null;
    }
    Vector2 CellScreenPoint(int x,int z)
    {
        var controller=Construction;var world=View.backdrop;
        int width=CampaignDecorations.Width(controller.PreviewItemId,controller.PreviewRotation),depth=CampaignDecorations.Depth(controller.PreviewItemId,controller.PreviewRotation);
        var point=controller.SurfaceCenter+controller.SurfaceRotation*new Vector3(((x+width*.5f)/CampaignDecorations.Columns-.5f)*controller.SurfaceSize.x,0,((z+depth*.5f)/CampaignDecorations.Rows-.5f)*controller.SurfaceSize.y);
        var viewport=world.sceneCamera.WorldToViewportPoint(point);var rect=world.output.rectTransform;var canvas=world.output.canvas;
        var uiCamera=canvas&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
        return RectTransformUtility.WorldToScreenPoint(uiCamera,rect.TransformPoint(new Vector3(rect.rect.xMin+viewport.x*rect.rect.width,rect.rect.yMin+viewport.y*rect.rect.height,0)));
    }
    IEnumerator Aim(int x,int z)
    {
        for(int frame=0;frame<4;frame++){InputSystem.QueueStateEvent(mouse,new MouseState {position=CellScreenPoint(x,z)});yield return null;}
        Construction.PreviewAtScreenPoint(mouse.position.ReadValue());
        Check(Construction.PreviewCell==new Vector2Int(x,z),"screen pointer snaps to cell "+x+", "+z);
    }
    IEnumerator MousePlace()
    {
        Vector2 point=mouse.position.ReadValue();InputSystem.QueueStateEvent(mouse,new MouseState {position=point}.WithButton(MouseButton.Left));yield return null;yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState {position=point});yield return null;
    }
    IEnumerator DragInventoryCard(string caption,int x,int z)
    {
        var button=FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.gameObject.activeInHierarchy&&b.name==caption&&b.IsInteractable());
        var source=button.GetComponent<CampaignDecorationDragSource>();var rect=button.GetComponent<RectTransform>();var canvas=button.GetComponentInParent<Canvas>();
        var uiCamera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
        Vector2 start=RectTransformUtility.WorldToScreenPoint(uiCamera,rect.TransformPoint(rect.rect.center));
        InputSystem.QueueStateEvent(mouse,new MouseState {position=start});yield return null;yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState {position=start}.WithButton(MouseButton.Left));yield return null;yield return null;
        Vector2 lifted=start+Vector2.right*(EventSystem.current.pixelDragThreshold+12);
        InputSystem.QueueStateEvent(mouse,new MouseState {position=lifted}.WithButton(MouseButton.Left));
        yield return Wait(()=>Construction.HasPreview&&CampaignDecorationDragSource.Dragging,"inventory card starts a real pointer drag");
        Vector2 target=CellScreenPoint(x,z);
        for(int frame=1;frame<=8;frame++)
        {InputSystem.QueueStateEvent(mouse,new MouseState {position=Vector2.Lerp(lifted,target,frame/8f)}.WithButton(MouseButton.Left));yield return null;}
        yield return null;
        Check(source&&source.gameObject.activeInHierarchy&&CampaignDecorationDragSource.Dragging&&Construction.PreviewValid&&Construction.PreviewCell==new Vector2Int(x,z),"inventory drag source survives selected-item UI refresh across multiple frames");
        InputSystem.QueueStateEvent(mouse,new MouseState {position=target});yield return null;yield return null;
    }
    Color GhostColor()
    {
        var ghost=GameObject.Find("CareConstructionGhost");if(!ghost)throw new InvalidOperationException("No active construction ghost");
        return ghost.GetComponentInChildren<Renderer>().sharedMaterial.GetColor("_BaseColor");
    }
    static float PartSize(CampaignDecorationVisuals visual)=>visual.Shape.GetComponentsInChildren<Renderer>().Sum(r=>r.transform.localScale.sqrMagnitude);
    bool CorrectAspect()
    {
        var backdrop=View.backdrop;float expected=Screen.width*backdrop.Viewport.width/(Screen.height*backdrop.Viewport.height);
        return Math.Abs(backdrop.sceneCamera.aspect-expected)<.025f;
    }
    IEnumerator Capture(string name,float delay=.15f)
    {
        var game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));game.Focus();game.Repaint();
        if(delay>0)yield return new WaitForSecondsRealtime(delay);yield return new WaitForEndOfFrame();
        RenderTexture target=null;
        for(Type type=game.GetType();type!=null&&!target;type=type.BaseType)
        {
            var field=type.GetField("m_TargetTexture",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)??type.GetField("m_RenderTexture",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
            if(field!=null)target=field.GetValue(game) as RenderTexture;
        }
        if(!target)throw new InvalidOperationException("Game View has no composed render texture");
        var previous=RenderTexture.active;var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try
        {
            RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();var pixels=texture.GetPixels32();
            Check(pixels.Where((p,i)=>i%47==0).Select(p=>(int)p.r+(p.g<<8)+(p.b<<16)).Distinct().Take(33).Count()>32,"composited view: "+name);
            if(SystemInfo.graphicsUVStartsAtTop)
            {var upright=new Color32[pixels.Length];for(int y=0;y<target.height;y++)Array.Copy(pixels,y*target.width,upright,(target.height-1-y)*target.width,target.width);texture.SetPixels32(upright);texture.Apply();}
            File.WriteAllBytes(Output+"/"+name+".png",texture.EncodeToPNG());
        }
        finally{RenderTexture.active=previous;Destroy(texture);}
    }
    IEnumerator Run()
    {
        yield return Wait(()=>CampaignApp.Instance&&!CampaignApp.Instance.Busy,"startup");app=CampaignApp.Instance;app.LoadSlot(0);yield return Page(CampaignPage.Greenhouse);
        app.Preferences.master=.8f;AudioListener.volume=.8f;AudioListener.pause=false;
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();yield return Wait(()=>Application.isFocused,"focused Game View");
        string first=app.Session.State.beetles[0].id,second=app.Session.State.beetles[1].id;
        Click("景观建造");yield return Wait(()=>Construction&&Construction.Editing&&Construction.CurrentBeetleId==first,"construction mode");yield return new WaitForSecondsRealtime(.6f);
        Check(FindObjectsByType<Button>(FindObjectsSortMode.None).Count(b=>b.gameObject.activeInHierarchy&&b.name.StartsWith("取出 · "))==4&&app.Session.State.inventory.Where(i=>CampaignDecorations.Known(i.itemId)).Sum(i=>i.quantity)==4,"construction warehouse shows four usable starter cards");
        Check(app.Constructing&&!app.CalendarRunning,"construction mode pauses husbandry while editing");yield return Capture("01-Warehouse");
        var specimen=FindObjectsByType<CampaignSpecimen>(FindObjectsSortMode.None).Single(s=>s.selected);
        Check(specimen.ConstructionHidden&&specimen.rig.GetComponentsInChildren<Renderer>(true).All(r=>!r.enabled||!r.gameObject.activeInHierarchy)&&specimen.enclosureFloor.enabled,"entering construction removes the animal while keeping its enclosure visible");
        Construction.BeginPlace(CampaignDecorations.Driftwood);Construction.PreviewAtCell(4,4);
        Check(!Construction.PreviewValid&&Construction.PreviewReason.StartsWith("生活空间不足")&&!Construction.Confirm()&&app.Session.InventoryCount(CampaignDecorations.Driftwood)==1,"a central log that removes real walking space is rejected without consuming inventory");
        Construction.Cancel();Check(specimen.ConstructionHidden&&Construction.Editing,"canceling one item retains the empty construction workspace until finishing");

        Click("取出 · 蕨叶");yield return Aim(1,3);
        Check(Construction.HasPreview&&Construction.PreviewValid&&GhostColor().g>GhostColor().r,"valid snapped preview is green");
        yield return KeyPress(Key.E);Check(Construction.PreviewRotation==90,"real E key rotates preview clockwise by ninety degrees");
        yield return KeyPress(Key.Q);Check(Construction.PreviewRotation==0,"real Q key rotates preview counter-clockwise by ninety degrees");
        yield return Aim(1,3);yield return Capture("02-Ghost-Green");
        yield return Aim(1,1);Check(!Construction.PreviewValid&&GhostColor().r>GhostColor().g&&!Construction.Confirm(),"circular container boundary shows a red preview and rejects placement");
        Check(app.Session.InventoryCount(CampaignDecorations.Fern)==1&&app.Session.State.decorations.Count==0,"invalid preview does not consume inventory");yield return Capture("03-Ghost-Red");
        yield return Aim(1,3);yield return MousePlace();yield return Wait(()=>!app.Busy&&app.Session.State.decorations.Count==1,"mouse placement commit",false);
        string fern=app.Session.State.decorations.Single().id;yield return Wait(()=>Construction.Bindings.ContainsKey(fern),"placed visual binding",false);
        Check(app.Session.InventoryCount(CampaignDecorations.Fern)==0&&app.Session.Decoration(first,fern).x==1&&app.Session.Decoration(first,fern).z==3,"real mouse placement commits its cell and consumes one item");
        yield return new WaitForSecondsRealtime(.2f);float building=PartSize(Construction.Bindings[fern]);yield return Capture("04-Building",0);
        yield return new WaitForSecondsRealtime(1);float built=PartSize(Construction.Bindings[fern]);
        Check(building>0&&building<built*.98f,"placed decoration grows from a partial construction at 0.2 seconds to its complete geometry");yield return Capture("05-Placed");

        Construction.Select(fern);yield return null;yield return null;
        yield return DragInventoryCard("取出 · 卵石组",9,5);
        yield return Wait(()=>!app.Busy&&app.Session.State.decorations.Any(p=>p.beetleId==first&&p.itemId==CampaignDecorations.Stones),"dragged second decoration commits on release");
        var dragged=app.Session.State.decorations.Single(p=>p.beetleId==first&&p.itemId==CampaignDecorations.Stones);
        Check(dragged.x==9&&dragged.z==5&&app.Session.InventoryCount(CampaignDecorations.Stones)==0&&app.Session.Decoration(first,fern).x==1&&!CampaignDecorationDragSource.Dragging,"dragging a second inventory card while scenery is selected places exactly one item on release");
        Construction.Select(dragged.id);Construction.ReturnSelected();yield return Idle();

        Construction.BeginPlace(CampaignDecorations.Stones);
        bool centerAllowed=false;
        for(int z=2;z<=7&&!centerAllowed;z++)for(int x=3;x<=8&&!centerAllowed;x++)centerAllowed=Construction.PreviewAtCell(x,z);
        Check(centerAllowed&&Construction.Confirm(),"free construction can occupy the former central reservation when the animal can safely return");yield return Idle();
        string central=app.Session.State.decorations.Single(p=>p.beetleId==first&&p.itemId==CampaignDecorations.Stones).id;
        Click("完成布置 · 放回甲虫");yield return Wait(()=>!Construction.Editing&&!specimen.ConstructionHidden,"animal returned after finishing");yield return null;
        var resident=specimen.Care.Find(first);
        Check(specimen.rig.GetComponentsInChildren<Renderer>().Any(r=>r.enabled)&&specimen.Enclosure.Area.IsClear(resident.Motor.Position)&&specimen.Enclosure.Area.CanWalk,"finishing restores the visible adult inside the largest continuous walkable area");
        Check(Construction.Bindings.Values.All(v=>!v.Bounds.Intersects(specimen.LifeStageDisplay.ObservationBounds)),"returned adult does not intersect saved scenery");yield return Capture("12-Animal-Returned");
        Click("景观建造");yield return Wait(()=>Construction.Editing,"construction resumed");Construction.Select(central);Construction.ReturnSelected();yield return Idle();

        Click("取出 · 卵石组");yield return Aim(1,3);Check(!Construction.PreviewValid&&GhostColor().r>GhostColor().g&&!Construction.Confirm(),"occupied cells reject a second item with a red preview");
        Construction.Cancel();Construction.BeginMove(fern);Check(Construction.PreviewValid,"move preview ignores the selected item's own footprint");
        yield return Aim(9,4);yield return KeyPress(Key.E);Construction.Cancel();
        Check(app.Session.Decoration(first,fern).x==1&&app.Session.Decoration(first,fern).z==3&&app.Session.Decoration(first,fern).rotation==0&&Construction.Bindings[fern].gameObject.activeSelf,"canceling a moved and rotated preview restores the original layout and visual");
        Construction.BeginMove(fern);yield return Aim(9,4);Check(Construction.Confirm(),"valid move accepted");yield return Idle();
        Check(app.Session.Decoration(first,fern).x==9&&app.Session.Decoration(first,fern).z==4&&app.Session.InventoryCount(CampaignDecorations.Fern)==0,"moving updates the existing placement without consuming another item");
        Construction.Select(fern);Click("放回仓库");yield return Idle();Check(app.Session.InventoryCount(CampaignDecorations.Fern)==1&&!app.Session.State.decorations.Any(p=>p.id==fern),"return button restores exactly one item to the warehouse");
        Click("取出 · 蕨叶");yield return Aim(9,4);Check(Construction.Confirm(),"reusing returned scenery accepted");yield return Idle();
        fern=app.Session.State.decorations.Single().id;Construction.Select(fern);long funds=app.Session.State.coins;long resale=app.Session.SupplySellPrice(CampaignDecorations.Fern);
        Click("卖出 +"+resale+" G");yield return Idle();Check(app.Session.State.coins==funds+resale&&app.Session.InventoryCount(CampaignDecorations.Fern)==0&&app.Session.State.decorations.Count==0,"direct sale pays forty percent and removes scenery without also returning inventory");

        Click("取出 · 苔藓垫");yield return Aim(1,3);Check(Construction.Confirm(),"first beetle layout placed");yield return Idle();string moss=app.Session.State.decorations.Single().id;
        app.Select(second);yield return Wait(()=>Construction.CurrentBeetleId==second&&Construction.Bindings.Count==0,"second beetle layout");
        Check(!Construction.Editing&&!specimen.ConstructionHidden,"switching beetles exits construction and restores the newly selected animal");Click("景观建造");yield return Wait(()=>Construction.Editing,"second enclosure construction");
        Click("取出 · 卵石组");yield return Aim(1,3);Check(Construction.PreviewValid&&Construction.Confirm(),"second beetle can use the same grid cell independently");yield return Idle();string stone=app.Session.State.decorations.Single(p=>p.beetleId==second).id;
        app.Select(first);yield return Wait(()=>Construction.CurrentBeetleId==first&&Construction.Bindings.ContainsKey(moss),"first beetle layout restored");
        Check(Construction.Bindings.Count==1&&!Construction.Bindings.ContainsKey(stone)&&app.Session.State.decorations.Count==2,"switching beetles restores only that beetle's scenery");

        View.ToggleSidebar();yield return new WaitForSecondsRealtime(.15f);
        Check(View.SidebarCollapsed&&View.SidebarAnimationActive&&View.SidebarProgress>0&&View.SidebarProgress<1,"sidebar collapse animates smoothly through an intermediate position");
        yield return Wait(()=>!View.SidebarAnimationActive,"sidebar collapse");
        Check(Math.Abs(View.backdrop.Viewport.x)<.001f&&Math.Abs(View.backdrop.Viewport.width-1)<.001f&&CorrectAspect(),"collapsed sidebar expands the world to full width with a matching camera aspect");yield return Capture("06-Sidebar-Collapsed");
        View.ToggleSidebar();yield return Wait(()=>!View.SidebarAnimationActive,"sidebar expansion");
        Check(!View.SidebarCollapsed&&Math.Abs(View.backdrop.Viewport.x-1f/3)<.001f&&CorrectAspect(),"expanded sidebar restores its one-third width and camera aspect");

        yield return Wait(()=>Hud&&Hud.ActiveFloatCount==0&&!Hud.BalanceTweenActive,"settled currency HUD");funds=app.Session.State.coins;long sequence=app.CurrencySequence;int sounds=CoinAudio.PlayedCount;
        Check(app.Execute(s=>s.State.coins+=125),"isolated reward transaction accepted");yield return Wait(()=>!app.Busy&&app.CurrencySequence==sequence+1,"reward commit",false);
        Check(app.CurrencyBalance==funds+125&&app.LastCurrencyChange.Delta==125&&Hud.TargetBalance==funds+125&&Hud.BalanceTweenActive&&Hud.ActiveFloatCount>0,"successful income starts balance interpolation and a positive floating amount");
        Check(CoinAudio.PlayedCount==sounds+1&&CoinAudio.LastDelta==125&&CoinAudio.LastPitch>1&&CoinAudio.CoinClip,"income emits one higher-pitched coin sound");yield return Capture("07-Currency-Gain",0);
        yield return Wait(()=>!Hud.BalanceTweenActive,"income number animation");Check(Math.Abs(Hud.DisplayedBalance-(funds+125))<.001,"animated currency settles exactly on the committed balance");
        sequence=app.CurrencySequence;sounds=CoinAudio.PlayedCount;funds=app.Session.State.coins;long price=CampaignDecorations.Price(CampaignDecorations.Leaf);
        Check(app.Execute(s=>s.BuyItem(CampaignDecorations.Leaf)),"landscape purchase transaction accepted");yield return Wait(()=>!app.Busy&&app.CurrencySequence==sequence+1,"purchase commit",false);
        Check(app.CurrencyBalance==funds-price&&app.LastCurrencyChange.Delta==-price&&Hud.TargetBalance==funds-price&&Hud.BalanceTweenActive&&Hud.ActiveFloatCount>0,"successful expense starts balance interpolation and a negative floating amount");
        Check(CoinAudio.PlayedCount==sounds+1&&CoinAudio.LastDelta==-price&&CoinAudio.LastPitch<1,"expense emits one lower-pitched coin sound");yield return Capture("08-Currency-Expense",0);
        yield return Wait(()=>!Hud.BalanceTweenActive&&Hud.ActiveFloatCount==0&&Hud.PendingFloats==0,"currency feedback completed");

        Check(app.SaveSlot(1),"independent layouts saved");long savedBalance=app.Session.State.coins;sequence=app.CurrencySequence;sounds=CoinAudio.PlayedCount;
        app.LoadSlot(1);yield return Page(CampaignPage.Greenhouse);yield return Wait(()=>Construction&&Construction.CurrentBeetleId==first&&Construction.Bindings.ContainsKey(moss),"restored scenery");
        specimen=FindObjectsByType<CampaignSpecimen>(FindObjectsSortMode.None).Single(s=>s.selected);
        Check(!specimen.ConstructionHidden&&!Construction.Editing,"scene reload never preserves temporary animal removal");
        Check(app.Session.State.decorations.Count==2&&app.Session.Decoration(second,stone).x==1&&app.Session.InventoryCount(CampaignDecorations.Fern)==0,"save reload retains independent layouts and sold-item inventory without repeating gifts");
        Check(app.CurrencySequence==sequence&&CoinAudio.PlayedCount==sounds&&CoinAudio.LastDelta==0&&Hud.TargetBalance==savedBalance&&Math.Abs(Hud.DisplayedBalance-savedBalance)<.001&&!Hud.BalanceTweenActive&&Hud.ActiveFloatCount==0,"save reload resets currency silently without replaying income, expense or sound");
        yield return Capture("09-Reloaded");

        app.Execute(s=>
        {
            var beetle=s.Beetle(first);beetle.stage=CampaignLifeStage.Larva3;beetle.stageAgeDays=s.StageDuration(beetle)*.35;
            beetle.totalAgeDays=beetle.stageAgeDays;beetle.adultAgeDays=0;beetle.feed=-1;
            beetle.readyWeek=s.State.week+(int)Math.Ceiling(s.RemainingDevelopmentDays(beetle)/7);
        });yield return Idle();
        yield return Wait(()=>FindFirstObjectByType<CampaignLifeStageDisplay>().CurrentStage==CampaignLifeStage.Larva3&&Construction.Bindings.ContainsKey(moss)&&Construction.Bindings[moss].transform.IsChildOf(FindFirstObjectByType<CampaignLifeStageDisplay>().StageRoot),"larval construction surface rebuilt");
        var larvalDisplay=FindFirstObjectByType<CampaignLifeStageDisplay>();
        Check(larvalDisplay.TryGetConstructionSurface(out Vector3 larvalCenter,out Vector2 larvalSize,out Quaternion larvalRotation)&&Vector3.Distance(Construction.SurfaceCenter,larvalCenter)<.001f&&Vector2.Distance(Construction.SurfaceSize,larvalSize)<.001f,"L3 scenery uses the larval vessel surface instead of the adult soil bed");
        Vector3 mossPosition=larvalCenter+larvalRotation*new Vector3((1.5f/CampaignDecorations.Columns-.5f)*larvalSize.x,.012f,(3.5f/CampaignDecorations.Rows-.5f)*larvalSize.y);
        Check(app.Session.Decoration(first,moss).x==1&&app.Session.Decoration(first,moss).z==3&&Vector3.Distance(Construction.Bindings[moss].transform.position,mossPosition)<.003f,"existing moss retains its grid cell and rests on the remapped larval surface");
        Click("景观建造");yield return new WaitForSecondsRealtime(.6f);Click("取出 · 枯木");yield return KeyPress(Key.E);yield return Aim(9,3);
        Check(specimen.ConstructionHidden&&larvalDisplay.SpecimenRoot.GetComponentsInChildren<Renderer>(true).All(r=>!r.enabled)&&larvalDisplay.StageRoot.GetComponentsInChildren<Renderer>().Any(r=>r.enabled),"larval construction removes only the animal and retains the bottle and substrate");
        Check(Construction.PreviewRotation==90&&Construction.PreviewValid&&GhostColor().g>GhostColor().r,"L3 construction preserves ninety-degree rotation and valid green grid previews");
        yield return Capture("10-Larva-Construction");Construction.Cancel();yield return null;yield return null;Click("完成布置 · 放回甲虫");yield return Wait(()=>!Construction.Editing&&!specimen.ConstructionHidden,"larva returned after finishing");yield return null;
        Check(larvalDisplay.SpecimenRoot.GetComponentsInChildren<Renderer>().Any(r=>r.enabled)&&Construction.Bindings.Values.All(v=>!v.Bounds.Intersects(larvalDisplay.ObservationBounds)),"finished larval construction restores the model at a clear substrate location");
        app.Execute(s=>{var beetle=s.Beetle(first);beetle.stage=CampaignLifeStage.ActiveAdult;beetle.stageAgeDays=beetle.adultAgeDays=0;beetle.feed=0;beetle.readyWeek=s.State.week;});yield return Idle();
        yield return Wait(()=>FindFirstObjectByType<CampaignLifeStageDisplay>().CurrentStage==CampaignLifeStage.ActiveAdult&&Construction.Bindings.ContainsKey(moss),"adult construction restored");
        Check(app.Session.State.decorations.Count==2&&app.Session.Decoration(first,moss).x==1,"returning to adulthood retains both beetles' layouts");
        Construction.SetEditing(false);Click("市场");yield return Page(CampaignPage.Market);
        Click(FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.gameObject.activeInHierarchy&&b.name.EndsWith("养殖箱景观")));yield return null;yield return null;
        Check(CampaignDecorations.All.All(item=>View.GetComponentsInChildren<TMP_Text>().Any(label=>label.text==item.name))&&View.GetComponentsInChildren<Transform>().Count(t=>t.name=="GoodsCard")==8,"landscape market category displays the eight real catalogue items");
        yield return Capture("11-Landscape-Market");
        var lampCard=View.GetComponentsInChildren<TMP_Text>().Single(label=>label.text==CampaignDecorations.Name(CampaignDecorations.Lamp)).transform.parent;
        Click(lampCard.GetComponentsInChildren<Button>().Single(b=>b.name=="查看参数档案..."));yield return null;
        Check(app.Modal&&FindFirstObjectByType<TMP_InputField>(),"landscape product card opens the quantity purchase dialog");
        funds=app.Session.State.coins;Click("确认购买");yield return Idle();
        var lampMail=app.Session.State.mail.Last(m=>m.Pending&&m.attachments.Any(a=>a.itemId==CampaignDecorations.Lamp));
        Check(app.Session.State.coins==funds-CampaignDecorations.Price(CampaignDecorations.Lamp)&&app.Session.InventoryCount(CampaignDecorations.Lamp)==0,"landscape purchase charges the catalogue price and waits in mail");
        InvokeView("OpenMailbox");InvokeView("SelectMail",lampMail.id);yield return Idle();Click("领取全部附件");yield return Idle();
        Check(app.Session.InventoryCount(CampaignDecorations.Lamp)==1,"new landscape supply is claimed into the warehouse");
        Click("打开仓库");yield return null;yield return null;
        var lampStorage=View.GetComponentsInChildren<TMP_Text>().Single(label=>label.text==CampaignDecorations.Name(CampaignDecorations.Lamp)+" ×1").transform.parent;
        Click(lampStorage.GetComponentsInChildren<Button>().Single(b=>b.name=="取出并布置"));yield return Page(CampaignPage.Greenhouse);
        yield return Wait(()=>Construction&&Construction.Editing&&Construction.HasPreview&&Construction.PreviewItemId==CampaignDecorations.Lamp,"warehouse take-out opens a greenhouse placement preview");yield return new WaitForSecondsRealtime(.6f);yield return Aim(9,3);
        Check(Construction.CurrentBeetleId==first&&Construction.PreviewValid&&app.Session.InventoryCount(CampaignDecorations.Lamp)==1,"warehouse take-out returns to the selected beetle with a valid unconsumed preview");
        Construction.Cancel();
        Check(Construction.Editing&&app.Constructing,"canceling the warehouse preview keeps construction active");
        Click("市场");yield return Page(CampaignPage.Market);
        Check(!app.Constructing,"navigating away from active construction restores the normal game mode");
        Check(errors.Count==0,"no runtime errors or exceptions");
    }
    void Finish(Exception failure)
    {
        Application.logMessageReceived-=Log;
        if(mouse!=null)InputSystem.RemoveDevice(mouse);if(keyboard!=null)InputSystem.RemoveDevice(keyboard);
        if(originalInput)InputSystem.settings=originalInput;if(testInput)Destroy(testInput);
        AudioListener.volume=originalVolume;AudioListener.pause=originalAudioPause;
        File.AppendAllText(Output+"/Result.txt",(failure==null?"COMPLETE PASS":"FAILED "+failure)+"\n"+string.Join("\n",errors));EditorApplication.isPlaying=false;
    }
}
