using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BeetleArena;
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

[InitializeOnLoad]
public static class CampaignInterfacePlayValidation
{
    const string Key="BugDuel.InterfaceAcceptance";
    public const string Output="Artifacts/InterfaceRework";
    static CampaignInterfacePlayValidation()
    {
        string storage=SessionState.GetString(Key,"");if(storage.Length>0)CampaignApp.StorageOverride=storage;
        EditorApplication.playModeStateChanged+=OnPlay;
        EditorApplication.update+=()=>{if(SessionState.GetString(Key,"").Length>0&&EditorApplication.isPlaying)EditorApplication.QueuePlayerLoopUpdate();};
    }
    [MenuItem("Tools/甲壳虫斗/验证/界面与画面/菜单仓库育种交互", false, 611)]
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before isolated UI validation.");
        Directory.CreateDirectory(Output);string storage=Path.GetFullPath(Output+"/Save-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(storage);File.WriteAllText(Output+"/Play.txt","RUNNING\n"+storage+"\n");
        SessionState.SetString(Key+".previousStorage",CampaignApp.StorageOverride??"");SessionState.SetString(Key,storage);CampaignApp.StorageOverride=storage;
        SessionState.SetString(Key+".start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Campaign/CampaignMainMenu.unity");
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Show();EditorApplication.isPlaying=true;
    }
    static void OnPlay(PlayModeStateChange state)
    {
        if(SessionState.GetString(Key,"").Length==0)return;
        if(state==PlayModeStateChange.EnteredPlayMode)
        {var go=new GameObject("Interface acceptance");UnityEngine.Object.DontDestroyOnLoad(go);go.AddComponent<CampaignInterfaceDriver>();}
        if(state!=PlayModeStateChange.EnteredEditMode)return;
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key+".start",""));
        string previous=SessionState.GetString(Key+".previousStorage","");CampaignApp.StorageOverride=previous.Length>0?previous:null;
        SessionState.EraseString(Key);SessionState.EraseString(Key+".previousStorage");SessionState.EraseString(Key+".start");
    }
}

public sealed class CampaignInterfaceDriver:MonoBehaviour
{
    const string Output=CampaignInterfacePlayValidation.Output;
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    CampaignApp app;Mouse mouse;Keyboard keyboard;InputSettings previousInput,testInput;bool background,cleaned,previousDark;
    readonly List<string> errors=new List<string>();
    readonly List<InputDevice> suspendedDevices=new List<InputDevice>();
    DisplayPreferencesSnapshot displayPreferences;
    CampaignView View=>FindFirstObjectByType<CampaignView>();
    IEnumerable<Button> Buttons=>FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b=>b.gameObject.activeInHierarchy);
    void Start()
    {
        background=Application.runInBackground;previousDark=CampaignRetroTheme.Dark;Application.runInBackground=true;Application.logMessageReceived+=Log;
        previousInput=InputSystem.settings;testInput=Instantiate(previousInput);InputSystem.settings=testInput;
        displayPreferences=new DisplayPreferencesSnapshot();BeetleDisplayPreferences.UseSceneDefault("CampaignGreenhouse");
        testInput.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        testInput.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        // Isolate only Unity's input devices; desktop mouse/keyboard remain usable.
        foreach(var device in InputSystem.devices.ToArray())
            if(device.enabled&&(device is Mouse||device is Keyboard||device is Gamepad))
            {suspendedDevices.Add(device);InputSystem.DisableDevice(device);}
        mouse=InputSystem.AddDevice<Mouse>("Interface acceptance mouse");keyboard=InputSystem.AddDevice<Keyboard>("Interface acceptance keyboard");
        StartCoroutine(Guard(Run()));
    }
    void Log(string text,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(text+"\n"+trace);}
    void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);File.AppendAllText(Output+"/Play.txt","PASS "+message+"\n");}
    IEnumerator Guard(IEnumerator routine)
    {
        var stack=new Stack<IEnumerator>();stack.Push(routine);
        while(stack.Count>0)
        {
            bool moved=false;object current=null;Exception error=null;
            try{moved=stack.Peek().MoveNext();if(moved)current=stack.Peek().Current;}catch(Exception e){error=e;}
            if(error!=null){Finish(error);yield break;}
            if(!moved){(stack.Pop() as IDisposable)?.Dispose();continue;}
            if(current is IEnumerator nested)stack.Push(nested);else yield return current;
        }
        Finish(null);
    }
    IEnumerator Wait(Func<bool> condition,string message)
    {
        float end=Time.realtimeSinceStartup+35;
        while(!condition()){if(Time.realtimeSinceStartup>end)throw new TimeoutException(message+": "+app?.Message);yield return null;}
        yield return null;yield return null;
    }
    IEnumerator Idle()=>Wait(()=>app&&!app.Busy,"commit");
    IEnumerator Page(CampaignPage page)=>Wait(()=>app&&!app.Busy&&View&&View.page==page,"page "+page);
    IEnumerator Click(string name)=>Click(Buttons.First(b=>(b.name==name||b.GetComponentsInChildren<TMP_Text>().Any(t=>t.text==name))&&b.IsInteractable()));
    IEnumerator Click(Button button)
    {
        yield return new WaitForEndOfFrame();
        foreach(var scroll in button.GetComponentsInParent<ScrollRect>())
        {
            Canvas.ForceUpdateCanvases();if(scroll.content.rect.height<=scroll.viewport.rect.height)continue;
            var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport,button.transform);
            scroll.StopMovement();scroll.verticalNormalizedPosition=Mathf.Clamp01(scroll.verticalNormalizedPosition-(scroll.viewport.rect.center.y-bounds.center.y)/(scroll.content.rect.height-scroll.viewport.rect.height));
        }
        Canvas.ForceUpdateCanvases();yield return new WaitForEndOfFrame();
        var rect=button.GetComponent<RectTransform>();var point=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
        var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
        Check(hits.Count>0&&hits[0].gameObject.GetComponentInParent<Button>()==button,"pointer reaches "+button.name);
        bool clicked=false;string buttonName=button.name;button.onClick.AddListener(()=>clicked=true);
        InputSystem.QueueStateEvent(mouse,new MouseState{position=point});yield return null;yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState{position=point}.WithButton(MouseButton.Left));yield return null;yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState{position=point});yield return null;yield return null;
        Check(clicked,"mouse click dispatched to "+buttonName);
    }
    IEnumerator Key(Key key)
    {
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return null;yield return null;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;yield return new WaitForSecondsRealtime(.4f);
    }
    RectTransform Named(string name)=>View.GetComponentsInChildren<RectTransform>(true).First(t=>t.name==name);
    IEnumerator ClickObservedBeetle()
    {
        yield return new WaitForEndOfFrame();
        var world=View.backdrop;var specimen=FindObjectsByType<BeetleArena.Campaign.CampaignSpecimen>(FindObjectsSortMode.None).First(s=>s.selected);
        var body=specimen.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).OrderByDescending(r=>r.bounds.size.sqrMagnitude).First();
        var uv=world.sceneCamera.WorldToViewportPoint(body.bounds.center);var output=world.output.rectTransform;
        var point=RectTransformUtility.WorldToScreenPoint(null,output.TransformPoint(new Vector3(output.rect.xMin+output.rect.width*uv.x,output.rect.yMin+output.rect.height*uv.y,0)));
        InputSystem.QueueStateEvent(mouse,new MouseState{position=point});yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState{position=point}.WithButton(MouseButton.Left));yield return null;yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState{position=point});yield return new WaitForSecondsRealtime(.3f);
        Check(View.GreenhouseInfoOpen&&!app.Modal&&!world.UiFocused,"clicking the rendered beetle opens a clear-background nonmodal care window");
    }
    IEnumerator Run()
    {
        app=CampaignApp.Ensure();CampaignLocalization.Select(0);yield return Page(CampaignPage.MainMenu);
        CheckDitherPixelRendering();
        Check(!Buttons.Any(b=>b.name=="设置")&&View.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="甲壳虫斗"),"menu branding and no settings button");
        yield return Capture("01-MainMenu");
        yield return Key(UnityEngine.InputSystem.Key.Escape);Check(app.Modal,"Escape opens settings on menu");
        yield return Click("完成");yield return new WaitForSecondsRealtime(.4f);
        Check(!app.Modal&&EventSystem.current.currentSelectedGameObject==null,"mouse closure leaves no latched action selection");
        yield return Click("开始 / 存档");yield return Page(CampaignPage.Saves);
        Check(app.NewGame(0,"验收玩家"),"create isolated journey");yield return Page(CampaignPage.Greenhouse);
        Check(app.Execute(s=>{s.State.coins=50000;s.ConfigureClock(86400,1,true);for(int i=0;i<3;i++)s.State.inventory.Add(new CampaignInventoryStack{itemId=CampaignItems.Food(i),quantity=5+i});s.Beetle(s.State.selectedId).championships=1;}),"prepare test stock and champion");yield return Idle();
        Check(!Buttons.Any(b=>b.name=="存档"||b.name=="设置"||b.name.Contains("保存当前")),"greenhouse exposes neither saves nor settings buttons");
        Check(Named("Player currency").IsChildOf(Named("Page")),"currency belongs to the left greenhouse UI");
        Check(Mathf.Abs(Named("Page").rect.width/Named("CampaignCanvas").rect.width-1f/6)<.001f,"main navigation uses one sixth of the canvas");
        Check(!View.GreenhouseInfoOpen&&!Buttons.Any(b=>b.name=="建造"),"care details are hidden until requested");
        var chineseLabel=Buttons.First(b=>b.name=="市场").GetComponentInChildren<TMP_Text>();chineseLabel.ForceMeshUpdate();
        var chineseFont=chineseLabel.textInfo.characterInfo.First(c=>c.character=='市').fontAsset;
        Check(chineseFont.faceInfo.familyName=="LiSu"&&chineseFont.faceInfo.scale==1&&chineseFont.faceInfo.pointSize==40&&chineseFont.atlasPadding>=4&&chineseFont.atlasTextures.All(t=>t.mipmapCount==1),"Rendered Chinese glyphs use LiSu SDF with normalized scale, padding and unmipped atlases");
        Check(chineseLabel.fontSizeMax==21,"Chinese navigation retains its authored size");
        Check(Named("Player currency").anchorMin==Vector2.zero,"currency is docked at the bottom of the sidebar");
        var dateText=Buttons.First(b=>b.name=="日历与时间").GetComponentInChildren<TMP_Text>().text;
        Check(dateText.Contains("年")&&dateText.Contains("月")&&dateText.Contains("日")&&dateText.Contains(":"),"greenhouse retains full date and 24-hour time");
        double dayLength=app.Session.State.clock.realSecondsPerDay;
        yield return Click("日历与时间");yield return new WaitForSecondsRealtime(.3f);
        Check(!View.GetComponentsInChildren<TMP_InputField>().Any()&&!Buttons.Any(b=>b.name=="暂停"||b.name=="继续"),"calendar offers speed selection only");
        yield return Click("2×");yield return Click("保存并返回");yield return Idle();
        Check(app.Session.State.clock.speed==2&&app.Session.State.clock.realSecondsPerDay==dayLength,"speed selection commits without changing the day-length baseline");
        Check(app.Execute(s=>s.ConfigureClock(dayLength,1,true)),"restore isolated paused clock");yield return Idle();
        yield return Capture("02-Greenhouse");
        CheckMailBadge("1");
        foreach(int unread in new[]{99,100})
        {
            Check(app.Execute(s=>{s.State.mail.RemoveAll(m=>m.id.StartsWith("badge-check-"));for(int i=s.UnreadMail;i<unread;i++)s.State.mail.Add(new CampaignMail{id="badge-check-"+i,senderId="keeper",subject="Badge layout",body="",week=s.State.week});}),"prepare isolated unread count "+unread);yield return Idle();
            yield return Capture("02a-Mail-"+unread);CheckMailBadge(unread>99?"99+":unread.ToString());
        }
        Check(app.Execute(s=>s.State.mail.RemoveAll(m=>m.id.StartsWith("badge-check-"))),"remove badge fixtures");yield return Idle();
        yield return ClickObservedBeetle();yield return Capture("02b-Greenhouse-Info");
        yield return Click("管理");Check(Buttons.Any(b=>b.name=="帮工一周 +150 G"),"management remains available in the floating window");
        yield return Click("建造");yield return new WaitForSecondsRealtime(.3f);Check(app.Constructing,"construction remains available in the floating window");
        yield return Capture("02c-Greenhouse-Construction");
        yield return Key(UnityEngine.InputSystem.Key.Escape);Check(!app.Constructing&&View.GreenhouseInfoOpen,"Escape leaves construction before closing the inspector");
        yield return Key(UnityEngine.InputSystem.Key.Escape);Check(!View.GreenhouseInfoOpen&&!app.Modal,"Escape closes the floating inspector first");
        yield return Click(Named("Sidebar toggle").GetComponent<Button>());yield return new WaitForSecondsRealtime(.4f);Check(View.SidebarCollapsed&&View.SidebarProgress>.99f,"DOTween collapses greenhouse controls");
        Check(View.backdrop.Viewport.x<.001f&&Named("Sidebar toggle").anchoredPosition.y<40,"collapsed handle stays at the bottom left and the world fills the canvas");
        yield return Capture("03-Greenhouse-Collapsed");
        yield return Click(Named("Sidebar toggle").GetComponent<Button>());yield return new WaitForSecondsRealtime(.4f);Check(!View.SidebarCollapsed&&View.SidebarProgress<.01f,"DOTween expands greenhouse controls");
        yield return Click("仓库");yield return new WaitForSecondsRealtime(.5f);
        Check(View.WarehouseOpen&&!app.Modal&&!Named("WarehouseDetails").gameObject.activeSelf,"warehouse level two stays hidden until a category is chosen, without dimming the scene");yield return Capture("04-Warehouse-Categories");
        yield return Click("成虫食物  ›");yield return new WaitForSecondsRealtime(.4f);
        Check(Named("WarehouseDetails").gameObject.activeSelf&&Named("WarehouseDetails").GetComponent<CanvasGroup>().alpha>.99f,"category reveals its stock with DOTween");yield return Capture("05-Warehouse-Expanded");CheckMailBadge("1");
        Check(Mathf.Abs(View.backdrop.Viewport.x-1f/3)<.001f,"two warehouse columns reserve exactly one third of the canvas");
        Check(!Named("WarehouseItems").GetComponent<ScrollRect>().horizontal,"warehouse grid cannot scroll horizontally");
        int foodBefore=app.Session.InventoryCount(CampaignItems.Food(0));
        yield return Click(Buttons.First(b=>b.name=="Warehouse item · "+CampaignItems.Name(CampaignItems.Food(0))));
        yield return Capture("05a-Warehouse-Item");yield return Click("投喂");yield return Idle();
        Check(View.WarehouseOpen&&!app.Modal&&app.Session.InventoryCount(CampaignItems.Food(0))==foodBefore-1,"card actions preserve real inventory transactions and return to the warehouse");
        yield return Key(UnityEngine.InputSystem.Key.Escape);Check(View.WarehouseOpen&&!Named("WarehouseDetails").gameObject.activeSelf,"Escape retracts warehouse level two first");
        yield return Key(UnityEngine.InputSystem.Key.Escape);Check(!View.WarehouseOpen&&!app.Modal,"Escape returns to the main sidebar");
        yield return Click("仓库");yield return new WaitForSecondsRealtime(.25f);yield return Click("成虫食物  ›");yield return new WaitForSecondsRealtime(.3f);
        yield return Click("成虫食物  ›");yield return new WaitForSecondsRealtime(.3f);Check(View.WarehouseCategory<0&&!Named("WarehouseDetails").gameObject.activeSelf,"reselecting a category retracts its details");
        yield return Click("成虫食物  ›");yield return new WaitForSecondsRealtime(.3f);yield return Click(Named("Sidebar toggle").GetComponent<Button>());yield return new WaitForSecondsRealtime(.35f);
        yield return Click(Named("Sidebar toggle").GetComponent<Button>());yield return new WaitForSecondsRealtime(.35f);
        Check(!View.WarehouseOpen&&!View.SidebarCollapsed&&Mathf.Abs(View.backdrop.Viewport.x-1f/6)<.001f,"reopening a collapsed warehouse returns to the main navigation");
        yield return Key(UnityEngine.InputSystem.Key.DownArrow);Check(!RetroUiNavigation.PointerMode&&EventSystem.current.currentSelectedGameObject,"keyboard navigation still obtains focus");
        yield return Key(UnityEngine.InputSystem.Key.Escape);Check(app.Modal,"Escape opens settings in greenhouse");yield return Click("画面");yield return new WaitForSecondsRealtime(.3f);
        Check(BeetleDisplayPreferences.PixelSize==2,"dither defaults to level two");
        foreach(int level in new[]{3,1,2})
        {
            yield return Click("FilterPixelSize");yield return new WaitForSecondsRealtime(.2f);
            var pixelButton=Buttons.First(b=>b.name=="FilterPixelSize");
            Check(BeetleDisplayPreferences.PixelSize==level&&pixelButton.GetComponentInChildren<TMP_Text>().text==level+" 档","pixel control cycles only through labeled levels: "+level);
            Check(Mathf.Approximately(View.backdrop.output.material.GetFloat("_PixelSize"),BeetleDisplayPreferences.DitherPixelScale(level)),"rendered pixel scale matches level "+level);
            if(level==3)yield return Capture("05c-Pixel-Level-3");
        }
        yield return Click("完成");yield return new WaitForSecondsRealtime(.5f);
        Check(EventSystem.current.currentSelectedGameObject==null,"switching from keyboard to mouse clears button selection on close");
        yield return Click("标本室");yield return Page(CampaignPage.SpecimenRoom);yield return Capture("05b-SpecimenRoom");
        yield return Click("返回温室");yield return Page(CampaignPage.Greenhouse);
        yield return Click("市场");yield return Page(CampaignPage.Market);
        Check(Vector2.Distance(Named("MarketWindow").rect.size,Named("CampaignCanvas").rect.size)<1,"market fills the whole canvas");yield return Capture("06-Market");
        yield return Click("食物与养殖补给");yield return new WaitForSecondsRealtime(.3f);
        if(CampaignRetroTheme.Dark){yield return Click("明暗");yield return new WaitForSecondsRealtime(.3f);}
        yield return Capture("06b-Market-Supplies-Light");
        yield return Click("明暗");yield return new WaitForSecondsRealtime(.3f);yield return Capture("06c-Market-Supplies-Dark");
        yield return Click("明暗");yield return new WaitForSecondsRealtime(.3f);
        yield return Click("主题 ▾");yield return new WaitForSecondsRealtime(.4f);
        Check(Named("ThemeDropdown").anchoredPosition.x>=294,"theme menu follows its relocated button");
        yield return Key(UnityEngine.InputSystem.Key.Escape);Check(!app.Modal,"Escape dismisses the market dropdown before opening settings");
        yield return Click("拍卖");yield return null;yield return null;yield return Click("冠军拍卖");yield return null;yield return Capture("07-Champion-Auctions");
        Check(View.page==CampaignPage.Market&&Buttons.Any(b=>b.name=="只拍卖一次首配权"),"champion auction is available inside market auctions");
        yield return Click("温室");yield return Page(CampaignPage.Greenhouse);yield return Click("育种中心");yield return Page(CampaignPage.Breeding);
        Check(View.GetComponentsInChildren<TMP_Text>().Any(t=>t.text.Contains("最终失败率")),"breeding offers show final failure percentage before payment");yield return Capture("08-Breeding");
        int count=app.Session.State.beetles.Count;long funds=app.Session.State.coins;string father=app.Session.State.beetles.First(b=>!b.female).id;
        var offer=app.Session.BreedingOffer(father,0);yield return Click(Buttons.Where(b=>b.name=="选择配种"&&b.IsInteractable()).OrderByDescending(b=>b.transform.position.y).First());
        Check(View.GetComponentsInChildren<TMP_Text>().Any(t=>t.text.Contains("失败不退费")),"payment confirmation discloses non-refundable random failure");
        yield return Click("确定");yield return Idle();Check(app.Session.State.coins==funds-offer.Fee&&app.ReadSlot(0).state.coins==app.Session.State.coins,"breeding payment committed to disk exactly once");
        Check(app.Session.State.beetles.Count==count||app.Session.State.beetles.Count==count+1,"service yields zero or one egg");
        CampaignLocalization.Select(1);yield return null;yield return null;yield return Capture("09-Breeding-English");
        Check(CampaignLocalization.Font.faceInfo.familyName=="Tahoma","English locale uses Tahoma");
        app.Go(CampaignPage.Market);yield return Page(CampaignPage.Market);yield return Capture("09b-Market-English");
        app.Go(CampaignPage.Greenhouse);yield return Page(CampaignPage.Greenhouse);
        for(int i=0;i<2;i++)
        {
            CampaignLocalization.Select(0);yield return null;yield return null;Check(Buttons.First(b=>b.name=="市场").GetComponentInChildren<TMP_Text>().fontSizeMax==21,"Chinese size restores after locale switch "+i);
            CampaignLocalization.Select(1);yield return null;yield return null;Check(Mathf.Abs(Buttons.First(b=>b.name=="市场").GetComponentInChildren<TMP_Text>().fontSizeMax-18.9f)<.01f,"English uses 90% size without accumulating reductions "+i);
        }
        yield return Capture("09c-Greenhouse-English");CheckMailBadge("1");
        yield return Click("仓库");yield return new WaitForSecondsRealtime(.3f);yield return Click("成虫食物  ›");yield return Capture("09d-Warehouse-English");
        CampaignRetroTheme.SetMode(true);typeof(CampaignView).GetMethod("QueueRefresh",Private).Invoke(View,null);yield return Capture("09e-Warehouse-English-Dark");
        app.Go(CampaignPage.MainMenu);yield return Page(CampaignPage.MainMenu);yield return Capture("10-MainMenu-English");
        Check(View.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="Betles Duel"),"English game title retains requested spelling");
        Check(errors.Count==0,"no runtime errors during interface workflow");
    }
    IEnumerator Capture(string name)
    {
        yield return new WaitForSecondsRealtime(.5f);
        var game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));game.Repaint();yield return new WaitForEndOfFrame();SaveFrame(name);
    }
    void CheckMailBadge(string expected)
    {
        var badge=Named("UnreadMailBadge");var text=badge.GetComponentInChildren<TMP_Text>();var currency=Named("Player currency");
        var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(currency.parent,badge);
        Check(badge.GetComponent<RetroRoundBadge>()&&Mathf.Abs(badge.rect.width-badge.rect.height)<.01f,"unread badge is circular for "+expected);
        Check(text.text==expected&&!text.isTextOverflowing,"unread count fits inside circle: "+expected);
        Check(badge.parent.parent==currency.parent&&badge.parent.GetSiblingIndex()>currency.GetSiblingIndex(),"mail badge renders above currency");
        Check(bounds.max.x<=currency.anchoredPosition.x,"unread circle does not overlap currency");
        var corners=new Vector3[4];badge.GetWorldCorners(corners);Check(corners[0].x>=0&&corners[2].y<=Screen.height,"unread circle stays inside the screen");
        Check(!badge.GetComponent<Graphic>().raycastTarget&&!text.raycastTarget,"unread badge leaves mailbox input available");
    }
    void CheckDitherPixelRendering()
    {
        const int width=120,height=10;
        var source=new Texture2D(width,height,TextureFormat.RGBA32,false,true){filterMode=FilterMode.Point};
        var colors=new Color[width*height];for(int y=0;y<height;y++)for(int x=0;x<width;x++)colors[y*width+x]=new Color(x/(width-1f),x/(width-1f),x/(width-1f),1);
        source.SetPixels(colors);source.Apply();
        var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
        var capture=new Texture2D(width,height,TextureFormat.RGBA32,false,true);var previous=RenderTexture.active;
        try
        {
            foreach(string shaderName in new[]{"BeetleCombatDither","BeetleDenseDither"})
            {
                var shader=Resources.Load<Shader>(shaderName);Check(shader&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader),shaderName+" compiles on the active GPU");
                var material=new Material(shader);
                try
                {
                    Check(material.GetFloat("_PixelSize")==2,shaderName+" shader defaults to level two");material.SetFloat("_DitherStrength",0);
                    for(int level=1;level<=3;level++)
                    {
                        material.SetFloat("_PixelSize",BeetleDisplayPreferences.DitherPixelScale(level));Graphics.Blit(source,target,material);
                        RenderTexture.active=target;capture.ReadPixels(new Rect(0,0,width,height),0,0);capture.Apply();
                        var row=capture.GetPixels32();int bands=1;for(int x=1;x<width;x++)if(!row[x].Equals(row[x-1]))bands++;
                        int expected=level==1?120:level==2?60:48;
                        Check(bands==expected,shaderName+" level "+level+" produces "+expected+" pixel cells across 120px");
                    }
                }
                finally{Destroy(material);}
            }
        }
        finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Destroy(source);Destroy(capture);}
    }
    void SaveFrame(string name)
    {
        var game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));RenderTexture target=null;
        for(var type=game.GetType();type!=null&&!target;type=type.BaseType)
        {var field=type.GetField("m_TargetTexture",Private|BindingFlags.DeclaredOnly)??type.GetField("m_RenderTexture",Private|BindingFlags.DeclaredOnly);if(field!=null)target=field.GetValue(game) as RenderTexture;}
        if(!target)throw new InvalidOperationException("No Game View texture");
        var old=RenderTexture.active;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try
        {
            RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
            if(SystemInfo.graphicsUVStartsAtTop){var pixels=image.GetPixels32();var upright=new Color32[pixels.Length];for(int y=0;y<image.height;y++)Array.Copy(pixels,y*image.width,upright,(image.height-1-y)*image.width,image.width);image.SetPixels32(upright);image.Apply();}
            File.WriteAllBytes(Output+"/"+name+".png",image.EncodeToPNG());
        }
        finally{RenderTexture.active=old;Destroy(image);}
    }
    void Cleanup()
    {
        if(cleaned)return;cleaned=true;Application.logMessageReceived-=Log;
        if(mouse!=null)InputSystem.RemoveDevice(mouse);if(keyboard!=null)InputSystem.RemoveDevice(keyboard);
        foreach(var device in suspendedDevices)if(device.added)InputSystem.EnableDevice(device);
        if(previousInput)InputSystem.settings=previousInput;if(testInput)Destroy(testInput);Application.runInBackground=background;
        displayPreferences?.Dispose();
        if(CampaignRetroTheme.Dark!=previousDark)CampaignRetroTheme.SetMode(previousDark);
    }
    void OnDestroy()=>Cleanup();
    void Finish(Exception error)
    {
        if(error!=null)try{SaveFrame("Failure");}catch(Exception){}
        Cleanup();File.AppendAllText(Output+"/Play.txt",(error==null?"COMPLETE PASS":"FAILED "+error)+"\n"+string.Join("\n",errors));EditorApplication.isPlaying=false;
    }
}
