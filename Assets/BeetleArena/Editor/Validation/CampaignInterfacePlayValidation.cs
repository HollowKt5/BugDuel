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
using Unity.Collections;
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
    const string InputKey=Key+".inputRecovery";
    [Serializable] sealed class InputRecovery
    {
        public int[] deviceIds;
        public int settingsId;
        public string token,settingsPath,settingsJson;
    }
    public const string Output="Artifacts/InterfaceRework";
    static CampaignInterfacePlayValidation()
    {
        string storage=SessionState.GetString(Key,"");if(storage.Length>0)CampaignApp.StorageOverride=storage;
        EditorApplication.playModeStateChanged+=OnPlay;
        AssemblyReloadEvents.beforeAssemblyReload+=()=>RestoreInput(false);
        string pendingRecovery=SessionState.GetString(InputKey,"");
        if(pendingRecovery.Length>0)EditorApplication.delayCall+=()=>
        {
            if(SessionState.GetString(InputKey,"")!=pendingRecovery)return;
            RestoreInput(!EditorApplication.isPlaying);
            if(EditorApplication.isPlaying&&SessionState.GetString(Key,"").Length>0)
            {File.AppendAllText(Output+"/Play.txt","INTERRUPTED by script reload; physical input restored.\n");EditorApplication.isPlaying=false;}
        };
        EditorApplication.update+=()=>{if(SessionState.GetString(Key,"").Length>0&&EditorApplication.isPlaying)EditorApplication.QueuePlayerLoopUpdate();};
    }
    [MenuItem("Tools/甲壳虫斗/验证/界面与画面/菜单仓库育种交互", false, 611)]
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before isolated UI validation.");
        RestoreInput(true);
        Directory.CreateDirectory(Output);string storage=Path.GetFullPath(Output+"/Save-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(storage);File.WriteAllText(Output+"/Play.txt","RUNNING\n"+storage+"\n");
        SessionState.SetString(Key+".previousStorage",CampaignApp.StorageOverride??"");SessionState.SetString(Key,storage);CampaignApp.StorageOverride=storage;
        SessionState.SetString(Key+".start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Campaign/CampaignMainMenu.unity");
        var game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));game.Show();game.Focus();EditorApplication.isPaused=false;EditorApplication.isPlaying=true;
    }
    static void OnPlay(PlayModeStateChange state)
    {
        if(state==PlayModeStateChange.ExitingPlayMode)RestoreInput(false);
        if(state==PlayModeStateChange.EnteredEditMode)
        {
            RestoreInput(false);string pendingRecovery=SessionState.GetString(InputKey,"");
            if(pendingRecovery.Length>0)EditorApplication.delayCall+=()=>
            {if(SessionState.GetString(InputKey,"")==pendingRecovery)RestoreInput(true);};
        }
        if(SessionState.GetString(Key,"").Length==0)return;
        if(state==PlayModeStateChange.EnteredPlayMode)
        {var go=new GameObject("Interface acceptance");UnityEngine.Object.DontDestroyOnLoad(go);go.AddComponent<CampaignInterfaceDriver>();}
        if(state!=PlayModeStateChange.EnteredEditMode)return;
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key+".start",""));
        string previous=SessionState.GetString(Key+".previousStorage","");CampaignApp.StorageOverride=previous.Length>0?previous:null;
        SessionState.EraseString(Key);SessionState.EraseString(Key+".previousStorage");SessionState.EraseString(Key+".start");
    }

    internal static void SuspendInput()
    {
        RestoreInput(true);
        var settings=InputSystem.settings;
        var devices=InputSystem.devices.Where(d=>d.enabled&&(d is Mouse||d is Keyboard||d is Gamepad)).ToArray();
        var recovery=new InputRecovery{token=Guid.NewGuid().ToString("N"),deviceIds=devices.Select(d=>d.deviceId).ToArray(),settingsId=settings.GetInstanceID(),settingsPath=AssetDatabase.GetAssetPath(settings),settingsJson=JsonUtility.ToJson(settings)};
        // Persist before touching any device: domain reloads recreate InputDevice objects.
        SessionState.SetString(InputKey,JsonUtility.ToJson(recovery));
        foreach(var device in devices)InputSystem.DisableDevice(device,keepSendingEvents:true);
    }
    internal static void RestoreInput(bool forget)
    {
        foreach(var driver in Resources.FindObjectsOfTypeAll<CampaignInterfaceDriver>())driver.DetachInputIsolation();
        string saved=SessionState.GetString(InputKey,"");if(saved.Length==0)return;
        var recovery=JsonUtility.FromJson<InputRecovery>(saved);
        foreach(int id in recovery.deviceIds)
        {var device=InputSystem.GetDeviceById(id);if(device!=null)InputSystem.EnableDevice(device);}
        RemoveTestDevices();
        var original=EditorUtility.InstanceIDToObject(recovery.settingsId) as InputSettings;
        if(!original&&!string.IsNullOrEmpty(recovery.settingsPath))original=AssetDatabase.LoadAssetAtPath<InputSettings>(recovery.settingsPath);
        if(!original){original=ScriptableObject.CreateInstance<InputSettings>();JsonUtility.FromJsonOverwrite(recovery.settingsJson,original);}
        var current=InputSystem.settings;InputSystem.settings=original;
        if(current!=original&&current&&current.name=="Interface acceptance settings")UnityEngine.Object.DestroyImmediate(current);
        MakePhysicalDevicesCurrent();
        if(forget)SessionState.EraseString(InputKey);
    }
    static void RemoveTestDevices()
    {
        foreach(var device in InputSystem.devices.ToArray())
            if(!device.native&&device.name.StartsWith("Interface acceptance ",StringComparison.Ordinal))InputSystem.RemoveDevice(device);
    }
    static void MakePhysicalDevicesCurrent()
    {
        InputSystem.devices.OfType<Mouse>().FirstOrDefault(d=>d.native&&d.enabled)?.MakeCurrent();
        InputSystem.devices.OfType<Keyboard>().FirstOrDefault(d=>d.native&&d.enabled)?.MakeCurrent();
    }
    public static string RecoverPhysicalInput()
    {
        if(SessionState.GetString(Key,"").Length>0)throw new InvalidOperationException("Stop the interface acceptance run before recovering input.");
        RestoreInput(true);RemoveTestDevices();
        foreach(var device in InputSystem.devices)
            if(device.native&&(device is Mouse||device is Keyboard))InputSystem.EnableDevice(device);
        MakePhysicalDevicesCurrent();return "Physical mouse and keyboard restored; stale interface test devices removed.";
    }
}

public sealed class CampaignInterfaceDriver:MonoBehaviour
{
    const string Output=CampaignInterfacePlayValidation.Output;
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    CampaignApp app;Mouse mouse;Keyboard keyboard;InputSettings previousInput,testInput;bool background,cleaned,previousDark;
    readonly List<string> errors=new List<string>();
    DisplayPreferencesSnapshot displayPreferences;
    CampaignView View=>FindFirstObjectByType<CampaignView>();
    IEnumerable<Button> Buttons=>FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b=>b.gameObject.activeInHierarchy);
    void Start()
    {
        background=Application.runInBackground;previousDark=CampaignRetroTheme.Dark;Application.runInBackground=true;Application.logMessageReceived+=Log;
        CampaignInterfacePlayValidation.SuspendInput();
        previousInput=InputSystem.settings;testInput=Instantiate(previousInput);testInput.name="Interface acceptance settings";InputSystem.settings=testInput;
        displayPreferences=new DisplayPreferencesSnapshot();BeetleDisplayPreferences.UseSceneDefault("CampaignGreenhouse");
        testInput.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        testInput.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        mouse=InputSystem.AddDevice<Mouse>("Interface acceptance mouse");keyboard=InputSystem.AddDevice<Keyboard>("Interface acceptance keyboard");
        InputSystem.onAfterUpdate+=MakeTestDevicesCurrent;MakeTestDevicesCurrent();
        StartCoroutine(Guard(Run()));
    }
    void MakeTestDevicesCurrent()
    {
        if(mouse!=null&&InputSystem.GetDeviceById(mouse.deviceId)==mouse&&Mouse.current!=mouse)mouse.MakeCurrent();
        if(keyboard!=null&&InputSystem.GetDeviceById(keyboard.deviceId)==keyboard&&Keyboard.current!=keyboard)keyboard.MakeCurrent();
    }
    internal void DetachInputIsolation()=>InputSystem.onAfterUpdate-=MakeTestDevicesCurrent;
    void Log(string text,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(text+"\n"+trace);}
    void OnApplicationFocus(bool focused)
    {
        if(!Directory.Exists(Output))return;
        var grab=View?View.BeetleGrab:null;
        File.AppendAllText(Output+"/Play.txt",$"FOCUS event={focused} frame={Time.frameCount} time={Time.unscaledTime:F3} applicationFocused={Application.isFocused} editorWindow={EditorWindow.focusedWindow?.GetType().Name} charging={grab&&grab.IsCharging} holding={grab&&grab.IsHolding} mouse={Mouse.current?.name} mousePressed={Mouse.current?.leftButton.isPressed}\n");
    }
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
    IEnumerator FocusWorldInput(string operation)
    {
        var game=Resources.FindObjectsOfTypeAll<EditorWindow>().First(window=>window.GetType()==Type.GetType("UnityEditor.GameView,UnityEditor"));
        game.Focus();yield return Wait(()=>Application.isFocused,"GameView focus before "+operation);
        Check(Mouse.current==mouse&&Keyboard.current==keyboard,"isolated test devices are current before "+operation);
    }
    IEnumerator Click(string name)=>Click(Buttons.First(b=>(b.name==name||b.GetComponentsInChildren<TMP_Text>().Any(t=>t.text==name))&&b.IsInteractable()));
    IEnumerator Click(Button button)
    {
        yield return null;
        foreach(var scroll in button.GetComponentsInParent<ScrollRect>())
        {
            Canvas.ForceUpdateCanvases();if(scroll.content.rect.height<=scroll.viewport.rect.height)continue;
            var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport,button.transform);
            scroll.StopMovement();scroll.verticalNormalizedPosition=Mathf.Clamp01(scroll.verticalNormalizedPosition-(scroll.viewport.rect.center.y-bounds.center.y)/(scroll.content.rect.height-scroll.viewport.rect.height));
        }
        Canvas.ForceUpdateCanvases();yield return null;
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
        yield return FocusWorldInput("beetle information click");
        yield return null;
        var world=View.backdrop;var specimen=FindObjectsByType<BeetleArena.Campaign.CampaignSpecimen>(FindObjectsSortMode.None).First(s=>s.selected);
        var body=specimen.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).OrderByDescending(r=>r.bounds.size.sqrMagnitude).First();
        var uv=world.sceneCamera.WorldToViewportPoint(body.bounds.center);var output=world.output.rectTransform;
        var point=RectTransformUtility.WorldToScreenPoint(null,output.TransformPoint(new Vector3(output.rect.xMin+output.rect.width*uv.x,output.rect.yMin+output.rect.height*uv.y,0)));
        InputSystem.QueueStateEvent(mouse,new MouseState{position=point});yield return null;
        LogGrabState("before observed beetle mouse down",View.BeetleGrab,point);
        InputSystem.QueueStateEvent(mouse,new MouseState{position=point}.WithButton(MouseButton.Left));yield return null;yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState{position=point});yield return new WaitForSecondsRealtime(.3f);
        LogGrabState("before observed beetle click assertion",View.BeetleGrab,point);
        Check(View.GreenhouseInfoOpen&&!app.Modal&&!world.UiFocused,"clicking the rendered beetle opens a clear-background nonmodal care window");
    }
    IEnumerator Run()
    {
        app=CampaignApp.Ensure();CampaignLocalization.Select(0);yield return Page(CampaignPage.MainMenu);
        var game=Resources.FindObjectsOfTypeAll<EditorWindow>().First(window=>window.GetType()==Type.GetType("UnityEditor.GameView,UnityEditor"));
        game.Focus();yield return Wait(()=>Application.isFocused,"GameView focus after entering Play mode");
        CheckDitherPixelRendering();
        Check(!Buttons.Any(b=>b.name=="设置")&&View.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="甲壳虫斗"),"menu branding and no settings button");
        yield return Capture("01-MainMenu");
        yield return Key(UnityEngine.InputSystem.Key.Escape);Check(app.Modal,"Escape opens settings on menu");
        yield return Click("完成");yield return new WaitForSecondsRealtime(.4f);
        Check(!app.Modal&&EventSystem.current.currentSelectedGameObject==null,"mouse closure leaves no latched action selection");
        yield return Click("开始 / 存档");yield return Page(CampaignPage.Saves);
        yield return Capture("01b-Saves");
        var savesWindow=Named("SavesWindow");
        CheckInside(Named("PageNotice"),savesWindow,"save status stays inside the window");
        for(int i=0;i<4;i++)CheckInside(Named("SaveSlot"+i),savesWindow,"save slot "+i+" stays inside its window");
        Check(!savesWindow.GetComponentsInChildren<RawImage>().Any(),"empty slots do not display placeholder screenshots");
        Check(app.NewGame(0,"验收玩家"),"create isolated journey");yield return Page(CampaignPage.Greenhouse);
        Check(app.Execute(s=>{s.State.coins=50000;s.ConfigureClock(86400,1,true);for(int i=0;i<3;i++)s.State.inventory.Add(new CampaignInventoryStack{itemId=CampaignItems.Food(i),quantity=5+i});s.Beetle(s.State.selectedId).championships=1;}),"prepare test stock and champion");yield return Idle();
        Check(!Buttons.Any(b=>b.name=="存档"||b.name=="设置"||b.name.Contains("保存当前")),"greenhouse exposes neither saves nor settings buttons");
        Check(Named("Player currency").IsChildOf(Named("Page")),"currency belongs to the left greenhouse UI");
        Check(Mathf.Abs(Named("Page").rect.width/Named("CampaignCanvas").rect.width-1f/6)<.001f,"main navigation uses one sixth of the canvas");
        Check(!View.GreenhouseInfoOpen&&!Buttons.Any(b=>b.name=="建造"),"care details are hidden until requested");
        Check(!Buttons.Any(b=>b.name=="虫箱"||b.name=="照料"),"redundant terrarium and care navigation entries are removed");
        Check(Named("ControlHint").gameObject.activeInHierarchy,"first visit explains how to inspect a beetle");
        var chineseLabel=Buttons.First(b=>b.name=="市场").GetComponentInChildren<TMP_Text>();chineseLabel.ForceMeshUpdate();
        var chineseFont=chineseLabel.textInfo.characterInfo.First(c=>c.character=='市').fontAsset;
        Check(chineseFont.faceInfo.familyName=="LiSu"&&chineseFont.faceInfo.scale==1&&chineseFont.faceInfo.pointSize==40&&chineseFont.atlasPadding>=4&&chineseFont.atlasTextures.All(t=>t.mipmapCount==1),"Rendered Chinese glyphs use LiSu SDF with normalized scale, padding and unmipped atlases");
        Check(chineseLabel.fontSizeMax==21,"Chinese navigation retains its authored size");
        Check(Named("Player currency").anchorMin==Vector2.zero,"currency is docked at the bottom of the sidebar");
        var dateText=Buttons.First(b=>b.name=="日历与时间").GetComponentInChildren<TMP_Text>().text;
        Check(dateText.Contains("年")&&dateText.Contains("月")&&dateText.Contains("日")&&dateText.Contains(":"),"greenhouse retains full date and 24-hour time");
        var dateLabel=Buttons.First(b=>b.name=="日历与时间").GetComponentInChildren<TMP_Text>();dateLabel.ForceMeshUpdate();
        Check(!dateText.Contains("\n")&&dateLabel.textInfo.lineCount==1&&!dateLabel.isTextOverflowing&&!dateLabel.isTextTruncated,"greenhouse date, time and speed fit on one line without truncation");
        double dayLength=app.Session.State.clock.realSecondsPerDay;
        yield return Click("日历与时间");yield return new WaitForSecondsRealtime(.3f);
        Check(!View.GetComponentsInChildren<TMP_InputField>().Any()&&!Buttons.Any(b=>b.name=="暂停"||b.name=="继续"),"calendar offers speed selection only");
        yield return Click("2×");yield return Click("保存并返回");yield return Idle();
        Check(app.Session.State.clock.speed==2&&app.Session.State.clock.realSecondsPerDay==dayLength,"speed selection commits without changing the day-length baseline");
        Check(app.Execute(s=>s.ConfigureClock(dayLength,1,true)),"restore isolated paused clock");yield return Idle();
        yield return CheckSeasonThemes();
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
        var builder=View.GetComponent<CampaignConstructionController>();
        var animalCells=GameObject.Find("CareConstructionAnimalCells");
        Check(builder.AnimalOccupiedCells.Count>1&&animalCells&&animalCells.GetComponent<MeshFilter>().sharedMesh.vertexCount==builder.AnimalOccupiedCells.Count*8,"every cell occupied by the beetle receives a full red X");
        Check(!View.BeetleGrab.TryBeginHold(),"beetles cannot be picked up while placing objects");
        yield return Capture("02c-Greenhouse-Construction");
        yield return Key(UnityEngine.InputSystem.Key.Escape);Check(!app.Constructing&&View.GreenhouseInfoOpen,"Escape leaves construction before closing the inspector");
        yield return Key(UnityEngine.InputSystem.Key.Escape);Check(!View.GreenhouseInfoOpen&&!app.Modal,"Escape closes the floating inspector first");
        yield return CheckFeedingDish();
        yield return CheckBeetleHandling();
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
        foreach(var target in new[]{("交易市场","TradingBBS","06d-Market-Trading"),("拍卖","PublicAuctions","06e-Market-Auctions"),("出售","InventorySales","06f-Market-Sell")})
        {
            yield return Click(target.Item1);yield return new WaitForSecondsRealtime(.3f);
            var panel=Named(target.Item2);CheckInside(panel,Named("MarketWindow"),target.Item2+" stays inside the market");
            Check(Mathf.Abs(panel.rect.width-(Named("MarketWindow").rect.width-6))<1,target.Item2+" fills both sides evenly");yield return Capture(target.Item3);
        }
        yield return Click("商店");yield return new WaitForSecondsRealtime(.3f);
        yield return Click("食物与养殖补给");yield return new WaitForSecondsRealtime(.3f);
        if(CampaignRetroTheme.Dark){yield return Click("明暗");yield return new WaitForSecondsRealtime(.3f);}
        yield return Capture("06b-Market-Supplies-Light");
        yield return Click("明暗");yield return new WaitForSecondsRealtime(.3f);yield return Capture("06c-Market-Supplies-Dark");
        yield return Click("明暗");yield return new WaitForSecondsRealtime(.3f);
        yield return Click("主题 ▾");yield return new WaitForSecondsRealtime(.4f);
        CheckInside(Named("ThemeDropdown"),Named("MarketWindow"),"theme dropdown stays within the centered market menu");
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
        var englishClock=Buttons.First(b=>b.name=="日历与时间").GetComponentInChildren<TMP_Text>();englishClock.ForceMeshUpdate();
        Check(englishClock.textInfo.lineCount==1&&!englishClock.isTextTruncated,"English date, time and speed fit on one line");
        yield return Click("仓库");yield return new WaitForSecondsRealtime(.3f);yield return Click("成虫食物  ›");yield return Capture("09d-Warehouse-English");
        CampaignRetroTheme.SetMode(true);typeof(CampaignView).GetMethod("QueueRefresh",Private).Invoke(View,null);yield return Capture("09e-Warehouse-English-Dark");
        app.Go(CampaignPage.MainMenu);yield return Page(CampaignPage.MainMenu);yield return Capture("10-MainMenu-English");
        Check(View.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="Betles Duel"),"English game title retains requested spelling");
        Check(errors.Count==0,"no runtime errors during interface workflow");
    }
    IEnumerator CheckSeasonThemes()
    {
        var clock=app.Session.State.clock;double originalDays=clock.elapsedDays;bool originalPaused=clock.paused,originalDark=CampaignRetroTheme.Dark;
        string[] seasons={"Spring","Summer","Autumn","Winter"};
        bool Same(Color a,Color b)=>Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b)+Mathf.Abs(a.a-b.a)<.012f;
        double Luminance(Color color)
        {
            double Channel(double value)=>value<=.04045?value/12.92:Math.Pow((value+.055)/1.055,2.4);
            return .2126*Channel(color.r)+.7152*Channel(color.g)+.0722*Channel(color.b);
        }
        double Contrast(Color a,Color b)
        {double x=Luminance(a),y=Luminance(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);}
        Check(!app.Busy&&clock.paused&&!app.CalendarRunning,"season previews run in the isolated paused session without transactions");
        try
        {
            clock.paused=true;
            for(int season=0;season<4;season++)for(int mode=0;mode<2;mode++)
            {
                // Months contain 30 days; preview Jan/Apr/Jul/Oct without advancing campaign business.
                clock.elapsedDays=season*90d+.5;CampaignRetroTheme.SetMode(mode==1);
                typeof(CampaignView).GetMethod("QueueRefresh",Private).Invoke(View,null);yield return null;yield return null;
                string palette=seasons[season]+(mode==0?"-Light":"-Dark");
                Check(CampaignRetroTheme.Season==season&&clock.Month==season*3+1,"calendar selects "+palette);
                var title=Named("GreenhouseTitle");var button=Buttons.First(b=>b.name=="市场");
                Check(Same(Named("GreenhousePanel").GetComponent<Image>().color,CampaignRetroTheme.Window)&&Same(button.GetComponent<Image>().color,CampaignRetroTheme.Button)&&!Same(CampaignRetroTheme.Window,CampaignRetroTheme.Button),palette+" separates window and button surfaces");
                Check(Same(title.GetComponentInChildren<TMP_Text>().color,CampaignRetroTheme.TitleInk)&&Same(button.colors.highlightedColor,CampaignRetroTheme.HoverTint),palette+" applies title ink and seasonal control tint");
                using(var mesh=new VertexHelper())
                {
                    title.GetComponent<RetroBevel>().ModifyMesh(mesh);Check(mesh.currentVertCount==20,palette+" title keeps its fill and four bevel edges");
                    var top=UIVertex.simpleVert;var bottom=top;var highlight=top;var shadow=top;
                    mesh.PopulateUIVertex(ref bottom,0);mesh.PopulateUIVertex(ref top,1);mesh.PopulateUIVertex(ref highlight,4);mesh.PopulateUIVertex(ref shadow,12);
                    Check(Same(top.color,CampaignRetroTheme.TitleTop)&&Same(bottom.color,CampaignRetroTheme.TitleBottom)&&Same(highlight.color,CampaignRetroTheme.TitleHighlight)&&Same(shadow.color,CampaignRetroTheme.TitleShadow)&&Mathf.Abs(title.rect.yMax-highlight.position.y-1)<.001f,palette+" renders the seasonal gradient and one-pixel title bevel");
                }
                Check(Contrast(CampaignRetroTheme.TitleInk,CampaignRetroTheme.TitleTop)>=4.5&&Contrast(CampaignRetroTheme.TitleInk,CampaignRetroTheme.TitleBottom)>=4.5&&Contrast(CampaignRetroTheme.SelectionInk,CampaignRetroTheme.Selection)>=4.5&&Contrast(CampaignRetroTheme.Ink,CampaignRetroTheme.Button)>=4.5,palette+" title, selected item and button text retain readable contrast");
                CheckInside(title,Named("Page"),palette+" title remains within the sidebar");
                CheckInside(button.GetComponent<RectTransform>(),Named("Page"),palette+" navigation remains within the sidebar");
                yield return Capture("00-Theme-"+palette);
            }
        }
        finally
        {
            clock.elapsedDays=originalDays;clock.paused=originalPaused;CampaignRetroTheme.SetMode(originalDark);
            typeof(CampaignView).GetMethod("QueueRefresh",Private).Invoke(View,null);
        }
    }
    IEnumerator Capture(string name)
    {
        yield return new WaitForSecondsRealtime(.5f);
        var game=Resources.FindObjectsOfTypeAll<EditorWindow>().First(window=>window.GetType()==Type.GetType("UnityEditor.GameView,UnityEditor"));game.Repaint();yield return new WaitForEndOfFrame();SaveFrame(name);
    }
    void CheckInside(RectTransform child,RectTransform parent,string message)
    {
        var corners=new Vector3[4];child.GetWorldCorners(corners);
        Check(corners.All(c=>{var p=parent.InverseTransformPoint(c);return p.x>=parent.rect.xMin-1&&p.x<=parent.rect.xMax+1&&p.y>=parent.rect.yMin-1&&p.y<=parent.rect.yMax+1;}),message);
    }
    IEnumerator ClickDish()
    {
        yield return FocusWorldInput("feeding dish click");
        var specimen=FindObjectsByType<BeetleArena.Campaign.CampaignSpecimen>(FindObjectsSortMode.None).First(s=>s.selected);
        Check(specimen.FeedingDish,"selected adult has an interactive feeding dish");
        var world=View.backdrop;var output=world.output.rectTransform;var builder=View.GetComponent<CampaignConstructionController>();var grab=View.BeetleGrab;
        var dish=specimen.FeedingDish.GetComponent<MeshFilter>();Check(dish&&dish.sharedMesh,"dish exposes the visible mesh used by its click fixture");
        var geometry=new Dictionary<Mesh,(Vector3[] vertices,int[] triangles)>();
        var occluders=specimen.GetComponentsInChildren<MeshFilter>().Where(f=>f.sharedMesh&&f.GetComponent<Renderer>() is Renderer r&&r.enabled&&r.gameObject.activeInHierarchy).ToArray();
        foreach(var filter in occluders.Append(dish))
            if(!geometry.ContainsKey(filter.sharedMesh))geometry.Add(filter.sharedMesh,ReadMeshGeometry(filter.sharedMesh));
        Vector2 ScreenPoint(Vector3 position)
        {
            var uv=world.sceneCamera.WorldToViewportPoint(position);
            return RectTransformUtility.WorldToScreenPoint(null,output.TransformPoint(new Vector3(output.rect.xMin+output.rect.width*uv.x,output.rect.yMin+output.rect.height*uv.y,0)));
        }
        bool VisibleDishPoint(Vector2 screen)
        {
            if(!builder.TryPointerRay(screen,out var ray)||grab.HitTarget(ray)!=CarePointerAction.FeedingDish)return false;
            var uiHits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=screen},uiHits);
            if(uiHits.Count>0)return false;
            var mesh=geometry[dish.sharedMesh];
            if(!MeshHit(ray,dish.transform,mesh.vertices,mesh.triangles,out float distance))return false;
            foreach(var blocker in occluders)
            {
                if(!blocker.GetComponent<Renderer>().bounds.IntersectRay(ray,out float boundsDistance)||boundsDistance>=distance)continue;
                mesh=geometry[blocker.sharedMesh];
                if(MeshHit(ray,blocker.transform,mesh.vertices,mesh.triangles,out float near)&&near<distance-.002f)return false;
            }
            return true;
        }
        var center=ScreenPoint(specimen.FeedingDish.bounds.center);
        var centerTarget=builder.TryPointerRay(center,out var centerRay)?grab.HitTarget(centerRay):CarePointerAction.None;
        var surface=geometry[dish.sharedMesh];Vector2 point=default;bool found=false;float best=float.PositiveInfinity;
        for(int i=0;i<surface.triangles.Length;i+=3)
        {
            var local=(surface.vertices[surface.triangles[i]]+surface.vertices[surface.triangles[i+1]]+surface.vertices[surface.triangles[i+2]])/3;
            if(local.y<dish.sharedMesh.bounds.center.y)continue;
            var candidate=ScreenPoint(dish.transform.TransformPoint(local));float score=(candidate-center).sqrMagnitude;
            if(score>=best||!VisibleDishPoint(candidate)||!VisibleDishPoint(candidate+Vector2.right*2)||!VisibleDishPoint(candidate-Vector2.right*2)||!VisibleDishPoint(candidate+Vector2.up*2)||!VisibleDishPoint(candidate-Vector2.up*2))continue;
            found=true;point=candidate;best=score;
        }
        File.AppendAllText(Output+"/Play.txt",$"DISH center={center} target={centerTarget} visible={VisibleDishPoint(center)} candidate={point} found={found} uiFocused={world.UiFocused} focused={Application.isFocused}\n");
        Check(found,"dish has a visible surface point with FeedingDish hit priority and no UI or beetle mesh occlusion");
        InputSystem.QueueStateEvent(mouse,new MouseState{position=point});yield return null;
        Check(Application.isFocused&&!app.Busy&&!app.Modal&&!app.Constructing&&!world.UiFocused,"dish click begins with a focused and interactive greenhouse");
        Check(VisibleDishPoint(point),"dish click precondition still holds immediately before mouse down");
        InputSystem.QueueStateEvent(mouse,new MouseState{position=point}.WithButton(MouseButton.Left));yield return null;yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState{position=point});yield return new WaitForSecondsRealtime(.3f);
        Check(View.FeedingDishOpen&&!View.GreenhouseInfoOpen&&!app.Modal,"clicking the dish opens an undimmed operation window");
    }
    static (Vector3[] vertices,int[] triangles) ReadMeshGeometry(Mesh mesh)
    {
        using(var snapshot=MeshUtility.AcquireReadOnlyMeshData(mesh))
        {
            var data=snapshot[0];var triangles=new List<int>();
            using(var vertices=new NativeArray<Vector3>(data.vertexCount,Allocator.Temp,NativeArrayOptions.UninitializedMemory))
            {
                data.GetVertices(vertices);
                for(int submesh=0;submesh<data.subMeshCount;submesh++)
                {
                    var descriptor=data.GetSubMesh(submesh);if(descriptor.topology!=MeshTopology.Triangles)continue;
                    using(var indices=new NativeArray<int>(descriptor.indexCount,Allocator.Temp,NativeArrayOptions.UninitializedMemory))
                    {data.GetIndices(indices,submesh,true);triangles.AddRange(indices.ToArray());}
                }
                return (vertices.ToArray(),triangles.ToArray());
            }
        }
    }
    static bool MeshHit(Ray ray,Transform transform,Vector3[] vertices,int[] triangles,out float distance)
    {
        var local=new Ray(transform.InverseTransformPoint(ray.origin),transform.InverseTransformVector(ray.direction));distance=float.PositiveInfinity;
        for(int i=0;i<triangles.Length;i+=3)
        {
            var a=vertices[triangles[i]];var edge1=vertices[triangles[i+1]]-a;var edge2=vertices[triangles[i+2]]-a;
            var cross=Vector3.Cross(local.direction,edge2);float determinant=Vector3.Dot(edge1,cross);if(Mathf.Abs(determinant)<.0000001f)continue;
            float inverse=1/determinant;var offset=local.origin-a;float u=Vector3.Dot(offset,cross)*inverse;if(u<0||u>1)continue;
            var q=Vector3.Cross(offset,edge1);float v=Vector3.Dot(local.direction,q)*inverse;if(v<0||u+v>1)continue;
            float t=Vector3.Dot(edge2,q)*inverse;if(t<0)continue;
            distance=Mathf.Min(distance,Vector3.Distance(ray.origin,transform.TransformPoint(local.GetPoint(t))));
        }
        return float.IsFinite(distance);
    }
    IEnumerator CheckFeedingDish()
    {
        var specimen=FindObjectsByType<BeetleArena.Campaign.CampaignSpecimen>(FindObjectsSortMode.None).First(s=>s.selected);
        var antenna=specimen.rig.antennae[0];var rotation=antenna.localRotation;var position=specimen.transform.position;
        float seconds=Time.unscaledTime;yield return new WaitForSecondsRealtime(1.1f);
        Check(Time.unscaledTime>seconds+1&&Quaternion.Angle(rotation,antenna.localRotation)>.1f&&Vector3.Distance(position,specimen.transform.position)<.0001f,"paused husbandry advances sensory animation while holding position");
        yield return ClickDish();yield return Capture("02d-Feeding-Dish");
        CheckInside(Named("FeedingDishWindow"),Named("CampaignCanvas"),"feeding window fits the canvas");
        int stock=app.Session.InventoryCount(CampaignItems.Food(0));double nutrition=app.Session.Beetle(app.Session.State.selectedId).nutrition;
        yield return Click(Buttons.Where(b=>b.name=="放入食物"&&b.IsInteractable()).OrderByDescending(b=>b.transform.position.y).First());yield return Idle();
        Check(app.Session.InventoryCount(CampaignItems.Food(0))==stock-1&&app.Session.Beetle(app.Session.State.selectedId).meal!=null,"dish placement consumes one real inventory item");
        yield return Click("清空食盆");yield return Click("取消");
        Check(app.Session.Beetle(app.Session.State.selectedId).meal!=null,"canceling dish clearing preserves the meal");
        yield return Click("清空食盆");yield return Click("确定");yield return Idle();
        Check(app.Session.Beetle(app.Session.State.selectedId).meal==null&&app.Session.InventoryCount(CampaignItems.Food(0))==stock-1&&app.Session.Beetle(app.Session.State.selectedId).nutrition==nutrition,"discarding food neither refunds inventory nor grants nutrition");
        Check(app.Execute(s=>{var b=s.Beetle(s.State.selectedId);s.Feed(b.id,0);s.CompleteMeal(b.id,b.meal.id);}),"prepare real completed meal residue");yield return Idle();
        yield return Capture("02e-Feeding-Residue");yield return Click("清理残渣");yield return Idle();
        Check(!app.ReadSlot(0).state.beetles.First(b=>b.id==app.Session.State.selectedId).foodResidue,"residue clearing commits to the save");
        yield return Key(UnityEngine.InputSystem.Key.Escape);Check(!View.FeedingDishOpen&&!app.Modal,"Escape closes the dish before opening settings");
        Check((app.Preferences.learnedControls&3)==3,"completed beetle and dish instructions are remembered");
        var preferences=JsonUtility.FromJson<CampaignPreferences>(File.ReadAllText(Path.Combine(app.SaveDirectory,"settings.json")));
        Check((preferences.learnedControls&3)==3,"learned controls persist across sessions");
    }
    void LogGrabState(string phase,CampaignBeetleGrabController grab,Vector2 screen)
    {
        var builder=View.GetComponent<CampaignConstructionController>();var selected=app.Session?.State.beetles.Find(b=>b.id==app.Session.State.selectedId);
        var type=typeof(CampaignBeetleGrabController);object Field(string name)=>type.GetField(name,Private)?.GetValue(grab);
        var canRead=type.GetProperty("CanRead",Private)?.GetValue(grab);
        var canLift=type.GetMethod("CanLift",Private)?.Invoke(grab,new object[]{selected});
        bool projects=builder.TryPointerRay(screen,out var ray);var current=Mouse.current;var ring=grab.ChargeIndicator;
        var canvas=ring?ring.canvas:null;
        var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=screen},hits);
        File.AppendAllText(Output+"/Play.txt",$"GRAB {phase} frame={Time.frameCount} time={Time.unscaledTime:F3} focused={Application.isFocused} screen={screen} projects={projects} HitTarget={(projects?grab.HitTarget(ray):CarePointerAction.None)} HitBack={projects&&grab.HitBack(ray)} uiHits=[{string.Join(",",hits.Select(h=>h.gameObject.name))}]\n"+
            $"  mouse={current?.name} device={current?.deviceId} enabled={current?.enabled} native={current?.native} testDevice={mouse.deviceId} sameDevice={current==mouse} position={current?.position.ReadValue()} pressed={current?.leftButton.isPressed} down={current?.leftButton.wasPressedThisFrame} up={current?.leftButton.wasReleasedThisFrame}\n"+
            $"  CanRead={canRead} CanLift={canLift} enabled={grab.enabled} active={grab.gameObject.activeInHierarchy} pending={Field("pending")} canLift={Field("canLift")} pressedTarget={Field("pressedTarget")} pressedBeetle={Field("pressedBeetle")} pressPoint={Field("pressPoint")} pressedAt={Field("pressedAt")} IsCharging={grab.IsCharging} HoldProgress={grab.HoldProgress:F3} IsHolding={grab.IsHolding} IsManipulating={grab.IsManipulating}\n"+
            $"  indicator={(ring?ring.name:"null")} activeSelf={ring&&ring.gameObject.activeSelf} activeHierarchy={ring&&ring.gameObject.activeInHierarchy} enabled={ring&&ring.enabled} canvas={(canvas?canvas.name:"null")} canvasEnabled={canvas&&canvas.enabled} canvasActive={canvas&&canvas.gameObject.activeInHierarchy} parent={(ring?ring.transform.parent?.name:"null")}\n"+
            $"  Busy={app.Busy} Modal={app.Modal} Constructing={app.Constructing} UiFocused={View.backdrop.UiFocused} NeedsPlayerName={app.NeedsPlayerName} selectedId={app.Session?.State.selectedId} selected={selected?.id} adult={selected!=null&&selected.Adult(app.Session.State.week)} overturned={selected?.overturned} dead={selected?.dead} listed={selected?.listed}\n");
    }
    IEnumerator CheckBeetleHandling()
    {
        yield return FocusWorldInput("beetle handling");
        var animal=FindObjectsByType<BeetleArena.Campaign.CampaignSpecimen>(FindObjectsSortMode.None).First(s=>s.selected);
        var grab=View.BeetleGrab;var world=View.backdrop;var output=world.output.rectTransform;
        Vector2 ScreenPoint(Vector3 position)
        {
            var uv=world.sceneCamera.WorldToViewportPoint(position);
            return RectTransformUtility.WorldToScreenPoint(null,output.TransformPoint(new Vector3(output.rect.xMin+output.rect.width*uv.x,output.rect.yMin+output.rect.height*uv.y,0)));
        }
        Vector2 BackPoint()
        {
            foreach(var shell in animal.rig.shellRenderers)
            {
                if(!shell||!shell.enabled||shell.transform.IsChildOf(animal.rig.headPivot))continue;
                var point=shell.bounds.center+Vector3.up*shell.bounds.extents.y*.8f;
                var screen=ScreenPoint(point);
                if(!View.GetComponent<CampaignConstructionController>().TryPointerRay(screen,out var ray))continue;
                var target=grab.HitTarget(ray);bool hitBack=grab.HitBack(ray);
                var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=screen},hits);
                File.AppendAllText(Output+"/Play.txt",$"GRAB candidate shell={shell.name} screen={screen} HitTarget={target} HitBack={hitBack} uiHits=[{string.Join(",",hits.Select(h=>h.gameObject.name))}]\n");
                if(target==CarePointerAction.Beetle&&hitBack&&hits.Count==0)return screen;
            }
            throw new InvalidOperationException("No visible back point for grab interaction");
        }
        bool IndicatorVisible()=>grab.ChargeIndicator&&grab.ChargeIndicator.gameObject.activeInHierarchy&&grab.ChargeIndicator.enabled;
        void CheckChargeIndicator()
        {
            var ring=grab.ChargeIndicator;
            Check(grab.IsCharging&&IndicatorVisible()&&grab.HoldProgress>.3f&&grab.HoldProgress<.8f,"half of the hold duration shows an incomplete long-press progress ring");
            Check(!ring.raycastTarget&&!ring.maskable&&ring.GetComponent<CanvasGroup>() is CanvasGroup group&&!group.blocksRaycasts&&!group.interactable,"charge indicator never intercepts pointer interaction");
            Canvas.ForceUpdateCanvases();var mesh=ring.canvasRenderer.GetMesh();var center=ring.rectTransform.rect.center;
            Check(mesh&&mesh.vertexCount>0&&mesh.vertices.All(v=>Vector2.Distance(v,center)>.01f)&&mesh.colors32.Length==mesh.vertexCount&&mesh.colors32.All(c=>c.r==255&&c.g==255&&c.b==255),"charge indicator renders a white hollow ring rather than a filled disk");
            var ringPoint=RectTransformUtility.WorldToScreenPoint(null,ring.rectTransform.TransformPoint(ring.rectTransform.rect.center));
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=ringPoint},hits);
            Check(!hits.Any(h=>h.gameObject.transform.IsChildOf(ring.transform)),"progress ring is absent from UI raycast results");
        }
        var motor=animal.Care.Find(app.Session.State.selectedId).Motor;
        var original=motor.Position;var back=BackPoint();
        var builder=View.GetComponent<CampaignConstructionController>();
        Check(CampaignBeetleGrabController.HoldSeconds==.7f,"pickup requires a full 0.7-second long press");
        var uiRect=Buttons.First(b=>b.name=="市场").GetComponent<RectTransform>();
        var uiPoint=RectTransformUtility.WorldToScreenPoint(null,uiRect.TransformPoint(uiRect.rect.center));
        int pickupCount=grab.PickupCount;
        InputSystem.QueueStateEvent(mouse,new MouseState{position=back});yield return null;
        LogGrabState("before initial press",grab,back);
        InputSystem.QueueStateEvent(mouse,new MouseState{position=back}.WithButton(MouseButton.Left));yield return new WaitForSecondsRealtime(CampaignBeetleGrabController.HoldSeconds*.25f);
        LogGrabState("after initial quarter-duration press",grab,back);
        Check(grab.IsCharging&&!grab.IsHolding&&IndicatorVisible(),"a partial hold displays progress without picking up the beetle");
        InputSystem.QueueStateEvent(mouse,new MouseState{position=back});yield return null;yield return null;
        Check(!grab.IsCharging&&!IndicatorVisible()&&!grab.IsManipulating&&grab.PickupCount==pickupCount&&View.GreenhouseInfoOpen,"early release hides progress and preserves the short-click information window");
        yield return Key(UnityEngine.InputSystem.Key.Escape);
        back=BackPoint();InputSystem.QueueStateEvent(mouse,new MouseState{position=back});yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState{position=back}.WithButton(MouseButton.Left));yield return new WaitForSecondsRealtime(CampaignBeetleGrabController.HoldSeconds*.25f);
        Check(grab.IsCharging&&IndicatorVisible(),"charging starts before the pointer enters UI");
        InputSystem.QueueStateEvent(mouse,new MouseState{position=uiPoint}.WithButton(MouseButton.Left));yield return null;yield return null;
        Check(!grab.IsCharging&&!IndicatorVisible()&&!grab.IsManipulating&&grab.PickupCount==pickupCount,"entering UI cancels the pending hold and hides progress");
        InputSystem.QueueStateEvent(mouse,new MouseState{position=uiPoint});yield return null;yield return null;
        Check(View.page==CampaignPage.Greenhouse&&!View.GreenhouseInfoOpen&&!app.Modal,"releasing a canceled partial hold over UI does not click that control");
        back=BackPoint();
        Check(builder.TryPointerRay(back,out var pressedRay),"initial grab point projects into the terrarium");
        Vector2 moved=original;bool movedOffRay=false;var originalWorld=animal.transform.position;
        try
        {
            for(int radius=1;radius<=12&&!movedOffRay;radius++)for(int direction=0;direction<8;direction++)
            {
                float angle=direction*Mathf.PI/4;
                var candidate=original+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius*.3f;
                if(!motor.Area.IsClear(candidate)||!motor.Area.SegmentClear(original,candidate))continue;
                animal.transform.position=originalWorld+animal.Enclosure.ToWorld(candidate)-animal.Enclosure.ToWorld(original);
                if(grab.HitBack(pressedRay))continue;
                moved=candidate;movedOffRay=true;break;
            }
        }
        finally{animal.transform.position=originalWorld;}
        Check(movedOffRay,"a legal moving target can leave the original pointer ray during a long press");
        InputSystem.QueueStateEvent(mouse,new MouseState{position=back});yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState{position=back}.WithButton(MouseButton.Left));yield return null;
        Check(!grab.IsHolding,"short initial press waits for the long-press threshold");
        motor.PlaceAt(moved);animal.transform.position=originalWorld+animal.Enclosure.ToWorld(moved)-animal.Enclosure.ToWorld(original);
        Check(!grab.HitBack(pressedRay),"the same beetle moves off the pressed back point before the hold threshold");
        yield return new WaitForSecondsRealtime(CampaignBeetleGrabController.HoldSeconds*.5f);
        LogGrabState("before half-duration assertion",grab,back);
        Check(!grab.IsHolding&&!app.HandlingBeetle&&grab.PickupCount==pickupCount,"half of the hold duration is insufficient to lift the beetle");CheckChargeIndicator();
        yield return new WaitForEndOfFrame();LogGrabState("before charging screenshot",grab,back);
        SaveFrame("02f-Beetle-Charging");LogGrabState("after charging screenshot",grab,back);
        yield return new WaitForSecondsRealtime(CampaignBeetleGrabController.HoldSeconds*.5f+.15f);
        LogGrabState("before completed 0.7-second assertion",grab,back);
        Check(grab.IsHolding&&!View.GreenhouseInfoOpen&&!grab.IsCharging&&!IndicatorVisible(),"0.7-second long press retains the originally hit beetle and hides the completed ring");
        Check(app.HandlingBeetle&&!app.CalendarRunning,"calendar pauses during handling to prevent meals or accidents while held");
        yield return new WaitForSecondsRealtime(.35f);
        Check(grab.StruggleWeight==0,"holding the pointer still does not trigger struggle");
        InputSystem.QueueStateEvent(mouse,new MouseState{position=back+new Vector2(8,0)}.WithButton(MouseButton.Left));
        yield return Wait(()=>grab.StruggleWeight>.1f,"pointer movement triggers held struggle");yield return Capture("02f-Beetle-Held");
        yield return new WaitForSecondsRealtime(.45f);
        Check(grab.StruggleWeight==0,"struggle settles when the held pointer stops moving");
        var desired=RectTransformUtility.WorldToScreenPoint(null,output.TransformPoint(new Vector3(output.rect.xMin+output.rect.width*.93f,output.rect.yMin+output.rect.height*.84f,0)));
        InputSystem.QueueStateEvent(mouse,new MouseState{position=desired}.WithButton(MouseButton.Left));yield return new WaitForSecondsRealtime(.3f);
        var dropTarget=grab.GroundPoint;
        Check(Vector2.Distance(dropTarget,moved)>.05f,"dragging chooses a new drop point before release");
        InputSystem.QueueStateEvent(mouse,new MouseState{position=desired});yield return null;
        Check(!grab.IsHolding&&grab.IsManipulating&&!IndicatorVisible(),"release begins landing with the progress ring hidden");
        InputSystem.QueueStateEvent(mouse,new MouseState{position=uiPoint});yield return new WaitForSecondsRealtime(.8f);
        Check(Vector2.Distance(motor.Position,dropTarget)<.001f,"moving the released pointer into UI preserves the new landing position");
        Check(!grab.IsManipulating&&animal.Care.HeldId==null&&Vector2.Distance(motor.Position,motor.Area.KeepInside(motor.Position))<.001f,"release corrects the target and returns control on a legal landing spot");
        Check(!app.HandlingBeetle,"landing clears the temporary calendar pause");
        Check(Vector2.Distance(original,motor.Position)>.05f,"dragging relocates the beetle");
        var landing=motor.Position;back=BackPoint();
        InputSystem.QueueStateEvent(mouse,new MouseState{position=back});yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState{position=back}.WithButton(MouseButton.Left));yield return new WaitForSecondsRealtime(CampaignBeetleGrabController.HoldSeconds+.15f);
        Check(grab.IsHolding,"beetle can be picked up again after landing");yield return Key(UnityEngine.InputSystem.Key.Escape);
        InputSystem.QueueStateEvent(mouse,new MouseState{position=back});yield return null;
        Check(!grab.IsManipulating&&!app.Modal&&!app.HandlingBeetle&&animal.Care.HeldId==null&&Vector2.Distance(landing,motor.Position)<.001f,"Escape cancels pickup at the safe origin without opening settings or leaving care paused");
        back=BackPoint();InputSystem.QueueStateEvent(mouse,new MouseState{position=back});yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState{position=back}.WithButton(MouseButton.Left));yield return new WaitForSecondsRealtime(CampaignBeetleGrabController.HoldSeconds+.15f);
        Check(grab.IsHolding,"pickup starts before pointer enters UI");
        InputSystem.QueueStateEvent(mouse,new MouseState{position=uiPoint}.WithButton(MouseButton.Left));yield return null;yield return null;
        Check(!grab.IsManipulating&&!app.HandlingBeetle&&!IndicatorVisible()&&animal.Care.HeldId==null&&Vector2.Distance(landing,motor.Position)<.001f,"entering UI while holding cancels safely, hides progress and restores care control");
        InputSystem.QueueStateEvent(mouse,new MouseState{position=uiPoint});yield return null;
        Check(View.page==CampaignPage.Greenhouse&&!app.Modal,"releasing a grab over UI does not click the hovered control");
        back=BackPoint();InputSystem.QueueStateEvent(mouse,new MouseState{position=back});yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState{position=back}.WithButton(MouseButton.Left));yield return new WaitForSecondsRealtime(CampaignBeetleGrabController.HoldSeconds+.15f);
        Check(grab.IsHolding,"pickup starts before focus-loss cancellation");
        typeof(CampaignBeetleGrabController).GetMethod("OnApplicationFocus",Private).Invoke(grab,new object[]{false});
        Check(!grab.IsManipulating&&!app.HandlingBeetle&&animal.Care.HeldId==null&&Vector2.Distance(landing,motor.Position)<.001f,"focus loss restores the safe origin and clears all held state");
        InputSystem.QueueStateEvent(mouse,new MouseState{position=back});yield return null;
        Check((app.Preferences.learnedControls&32)!=0,"successful pickup dismisses its instruction permanently");

        var state=app.Session.State;var selected=app.Session.Beetle(state.selectedId);
        var savedBattle=state.battle;var savedAuction=state.auction;bool savedListed=selected.listed;
        string originalDomain=JsonUtility.ToJson(state);
        Check(state.clock.paused&&!app.Busy,"economic reservation pickup fixtures run without clock advancement or transactions");
        try
        {
            foreach(string reservation in new[]{"pending battle","pending auction","market listing"})
            {
                state.battle=null;state.auction=null;selected.listed=false;
                if(reservation=="pending battle")
                {
                    var opponent=CampaignSnapshots.Copy(selected);opponent.id="grab-regression-opponent";
                    var tier=app.Session.Catalog.Tier(selected.length);
                    state.battle=new PendingBattle {id="grab-regression-battle",beetleId=selected.id,opponent=opponent,seed=31415,tier=tier.tier,fee=tier.fee};
                }
                else if(reservation=="pending auction")
                    state.auction=new AuctionState {beetleId=selected.id,rights=true,round=1,rounds=5,bid=selected.value,finalBid=selected.value,bidder="Grab regression"};
                else selected.listed=true;
                app.Session.Validate();string before=JsonUtility.ToJson(state);
                yield return null;
                back=BackPoint();InputSystem.QueueStateEvent(mouse,new MouseState{position=back});yield return null;
                LogGrabState("before "+reservation+" pickup",grab,back);
                InputSystem.QueueStateEvent(mouse,new MouseState{position=back}.WithButton(MouseButton.Left));yield return new WaitForSecondsRealtime(CampaignBeetleGrabController.HoldSeconds+.15f);
                LogGrabState("after "+reservation+" pickup",grab,back);
                Check(grab.IsHolding&&!grab.IsCharging&&!IndicatorVisible()&&!View.GreenhouseInfoOpen,"completed pointer hold lifts a beetle reserved by "+reservation);
                Check(!app.Busy&&ReferenceEquals(app.Session.State,state)&&JsonUtility.ToJson(app.Session.State)==before,"pickup preserves the entire domain JSON during "+reservation);
                InputSystem.QueueStateEvent(mouse,new MouseState{position=back});yield return new WaitForSecondsRealtime(.8f);
                Check(!grab.IsManipulating&&!app.HandlingBeetle&&animal.Care.HeldId==null&&motor.Area.IsClear(motor.Position)&&Vector2.Distance(motor.Position,motor.Area.KeepInside(motor.Position))<.001f,"pointer release lands safely and restores care after "+reservation);
                Check(!app.Busy&&ReferenceEquals(app.Session.State,state)&&JsonUtility.ToJson(app.Session.State)==before,"release preserves the entire domain JSON during "+reservation);
            }
        }
        finally
        {
            grab.Cancel();state.battle=savedBattle;state.auction=savedAuction;selected.listed=savedListed;
            if(mouse!=null&&InputSystem.GetDeviceById(mouse.deviceId)==mouse)InputSystem.QueueStateEvent(mouse,new MouseState{position=mouse.position.ReadValue()});
        }
        Check(JsonUtility.ToJson(app.Session.State)==originalDomain,"temporary battle, auction and listing fixtures restore the original domain state");
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
        var game=Resources.FindObjectsOfTypeAll<EditorWindow>().First(window=>window.GetType()==Type.GetType("UnityEditor.GameView,UnityEditor"));RenderTexture target=null;
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
        DetachInputIsolation();
        CampaignInterfacePlayValidation.RestoreInput(false);Application.runInBackground=background;
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
