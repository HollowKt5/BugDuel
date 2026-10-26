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
public static class SpecimenTradingPlayValidation
{
    const string Key="BugDuel.TradingPlayValidation";
    public const string Output="Artifacts/Trading/Play";
    static SpecimenTradingPlayValidation()
    {
        string path=SessionState.GetString(Key,"");if(path.Length>0)CampaignApp.StorageOverride=path;
        EditorApplication.playModeStateChanged+=OnPlay;
        EditorApplication.update+=Tick;
    }
    static void Tick() { if(SessionState.GetString(Key,"").Length>0&&EditorApplication.isPlaying)EditorApplication.QueuePlayerLoopUpdate(); }
    [MenuItem("Tools/甲壳虫斗/验证/战役流程/标本交易实机流程", false, 567)]
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before the isolated acceptance run.");
        Directory.CreateDirectory(Output);
        var path=Path.GetFullPath(Output+"/Save-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var session=CampaignSession.New(CampaignCatalog.Defaults(),48117);session.RenamePlayer("验收玩家");session.State.coins=100000;
        for(int i=0;i<11;i++)
        {
            var species=session.Catalog.species[i];var beetle=session.Create(species.id,species.length,2);
            beetle.dead=true;beetle.hearts=0;session.State.beetles.Add(beetle);
            string id=session.PreserveDead(beetle.id,"2026-10-22");session.PlaceSpecimen(id,i);
            var record=session.Specimen(id);session.EditSpecimen(id,"馆藏 "+(i+1),species.name,"2026-10-22",i%3,0);
        }
        var deceased=session.State.beetles[0];deceased.dead=true;deceased.hearts=0;deceased.overturned=false;session.State.selectedId=deceased.id;
        var live=session.State.beetles[1];live.overturned=false;live.nutrition=100;live.hearts=5;live.female=false;
        session.Validate();new CampaignSaveRepository(path).Write(0,new CampaignSave {state=session.State,catalog=session.Catalog});
        File.WriteAllText(Output+"/Result.txt","RUNNING\nIsolated saves: "+path+"\n");
        SessionState.SetString(Key,path);CampaignApp.StorageOverride=path;
        SessionState.SetString(Key+".start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Campaign/CampaignMainMenu.unity");
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Show();
        EditorApplication.isPlaying=true;
    }
    static void OnPlay(PlayModeStateChange state)
    {
        if(SessionState.GetString(Key,"").Length==0)return;
        if(state==PlayModeStateChange.EnteredPlayMode)
        { var go=new GameObject("Specimen trading acceptance");UnityEngine.Object.DontDestroyOnLoad(go);go.AddComponent<SpecimenTradingPlayDriver>(); }
        if(state==PlayModeStateChange.EnteredEditMode)
        {
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key+".start",""));
            SessionState.EraseString(Key);SessionState.EraseString(Key+".start");CampaignApp.StorageOverride=null;
        }
    }
}

public sealed class SpecimenTradingPlayDriver : MonoBehaviour
{
    const string Output=SpecimenTradingPlayValidation.Output;
    CampaignApp app;
    readonly List<string> errors=new List<string>();
    DisplayPreferencesSnapshot preferences;
    Mouse mouse;
    Keyboard keyboard;
    InputSettings originalInput,testInput;
    void Start()
    {
        Application.runInBackground=true;Application.logMessageReceived+=Log;
        originalInput=InputSystem.settings;testInput=Instantiate(originalInput);InputSystem.settings=testInput;
        testInput.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        testInput.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        preferences=new DisplayPreferencesSnapshot();
        StartCoroutine(Guard(Run()));
    }
    void Log(string message,string stack,LogType type) { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+stack); }
    IEnumerator Guard(IEnumerator routine)
    {
        var stack=new Stack<IEnumerator>();stack.Push(routine);
        while(stack.Count>0)
        {
            bool moved=false;object current=null;Exception error=null;
            try { moved=stack.Peek().MoveNext();if(moved)current=stack.Peek().Current; }catch(Exception e){error=e;}
            if(error!=null){Finish(error);yield break;}
            if(!moved){(stack.Pop() as IDisposable)?.Dispose();continue;}
            if(current is IEnumerator nested)stack.Push(nested);else yield return current;
        }
        Finish(null);
    }
    void Check(bool condition,string label)
    {
        if(!condition)throw new InvalidOperationException(label+" | "+app?.Message);
        File.AppendAllText(Output+"/Result.txt","PASS "+label+"\n");
    }
    IEnumerator Wait(Func<bool> condition,string label)
    {
        float until=Time.realtimeSinceStartup+35;
        while(!condition()){if(Time.realtimeSinceStartup>until)throw new TimeoutException(label+" | "+app?.Message);yield return null;}
        yield return null;yield return null;
    }
    IEnumerator Idle()=>Wait(()=>app&&!app.Busy,"commit/transition");
    IEnumerator Page(CampaignPage page)=>Wait(()=>app&&!app.Busy&&SceneManager.GetActiveScene().name==CampaignApp.SceneName(page),"page "+page);
    CampaignView View=>FindFirstObjectByType<CampaignView>();
    void Click(string label)
    {
        var button=FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b=>b.gameObject.activeInHierarchy&&b.name==label&&b.IsInteractable());
        if(!button)throw new InvalidOperationException("Missing enabled button: "+label);
        ExecuteEvents.Execute(button.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);
    }
    void InvokeView(string method,params object[] args)=>typeof(CampaignView).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(View,args);
    IEnumerator Capture(string name)
    {
        var game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));game.Focus();game.Repaint();
        yield return new WaitForSecondsRealtime(.45f);yield return new WaitForEndOfFrame();
        RenderTexture target=null;
        for(Type type=game.GetType();type!=null&&!target;type=type.BaseType)
        {
            var field=type.GetField("m_TargetTexture",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)
                ??type.GetField("m_RenderTexture",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
            if(field!=null)target=field.GetValue(game) as RenderTexture;
        }
        if(!target)throw new InvalidOperationException("Game View has no composed render target");
        var previous=RenderTexture.active;var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try
        {
            RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();
            var pixels=texture.GetPixels32();
            Check(pixels.Where((p,i)=>i%41==0).Select(p=>(int)p.r+(p.g<<8)+(p.b<<16)).Distinct().Take(33).Count()>32,"composited scene and UI: "+name);
            if(SystemInfo.graphicsUVStartsAtTop)
            {
                var upright=new Color32[pixels.Length];
                for(int y=0;y<target.height;y++)Array.Copy(pixels,y*target.width,upright,(target.height-1-y)*target.width,target.width);
                texture.SetPixels32(upright);texture.Apply();
            }
            File.WriteAllBytes(Output+"/"+name+".png",texture.EncodeToPNG());
        }
        finally { RenderTexture.active=previous;Destroy(texture); }
    }
    IEnumerator Run()
    {
        yield return Wait(()=>CampaignApp.Instance&&!CampaignApp.Instance.Busy,"app startup");app=CampaignApp.Instance;
        app.LoadSlot(0);yield return Page(CampaignPage.Greenhouse);
        int frame=Time.frameCount;yield return new WaitForSecondsRealtime(.25f);Check(Time.frameCount>frame+2,"player loop advances");
        string namedId=app.Session.State.selectedId;
        string oldName=app.Session.Beetle(namedId).name;
        Click("命名");yield return null;
        var nameInput=FindFirstObjectByType<TMP_InputField>();
        Check(nameInput&&!nameInput.textComponent.GetComponent<CampaignLocalizedLabel>(),"name input is editable data, independent of UI translations");
        Check(!nameInput.richText&&!nameInput.textComponent.richText,"Chinese IME text and its field both use plain text");
        nameInput.text="";nameInput.caretPosition=0;
        nameInput.ProcessEvent(new Event {type=EventType.KeyDown,character='青'});
        nameInput.ProcessEvent(new Event {type=EventType.KeyDown,character='岚'});
        Check(nameInput.text=="青岚","TMP keyboard text events enter Chinese characters");
        nameInput.text="   ";Click("保存名字");yield return null;
        Check(app.Modal&&app.Session.Beetle(namedId).name==oldName,"empty name stays in dialog without changing saved identity");
        nameInput.text="  青岚 · 你  ";Click("保存名字");yield return Idle();
        Check(!app.Modal&&app.Session.Beetle(namedId).name=="青岚 · 你"&&app.Session.Beetle(namedId).customName,"name UI trims and durably saves custom name");
        Click("命名");yield return null;yield return Capture("00-NameDialog");FindFirstObjectByType<TMP_InputField>().text="未保存的名字";Click("取消");yield return null;
        Check(app.Session.Beetle(namedId).name=="青岚 · 你","cancel preserves previous name");
        CampaignLocalization.Select(1);yield return null;yield return null;
        Check(FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Any(t=>t.text.Contains("青岚 · 你")),"English UI preserves player name verbatim");
        CampaignLocalization.Select(app.Preferences.language);yield return null;yield return null;
        Click("制作装框标本 · 40 G");yield return null;Click("确定");yield return Page(CampaignPage.SpecimenRoom);
        Check(app.Session.State.specimens.Count==12&&app.Session.State.beetles.Count==1,"dead beetle UI creates specimen and releases housing");
        Check(app.Session.State.specimens.Last().labelName=="青岚 · 你","specimen inherits the player name");
        var room=FindFirstObjectByType<CampaignSpecimenRoom>();Check(room&&room.GetComponentsInChildren<TextMeshPro>().Length==12,"twelve real framed exhibits with plaques");
        string newest=app.Session.State.specimens.Last().id;
        app.Execute(s=>s.PlaceSpecimen(newest,11));yield return Idle();yield return Capture("01-SpecimenWall");
        Click("编辑铭牌");yield return null;
        var fields=FindObjectsByType<TMP_InputField>(FindObjectsSortMode.None).OrderByDescending(f=>f.transform.position.y).ToArray();
        fields[0].text="我的第一件馆藏";fields[1].text="独角仙 · 纪念";fields[2].text="2026-10-22";Click("保存铭牌");yield return Idle();
        Check(app.Session.Specimen(newest).labelName=="我的第一件馆藏"&&!app.Modal,"plaque input commits and closes on saved state");
        app.Execute(s=>s.EditSpecimen(newest,s.Specimen(newest).labelName,s.Specimen(newest).speciesLabel,s.Specimen(newest).dateLabel,1,45));yield return Idle();
        room.Focus(newest);yield return new WaitForSecondsRealtime(.5f);yield return Capture("02-SpecimenClose");
        string other=app.Session.State.specimens.First(s=>s.wallSlot==0).id;
        room.SetArrange(true);room.ResetView();yield return new WaitForSecondsRealtime(.7f);
        mouse=InputSystem.AddDevice<Mouse>("Specimen drag validation");
        var world=View.backdrop;
        Vector2 ScreenPoint(int slot)
        {
            var p=world.sceneCamera.WorldToViewportPoint(CampaignSpecimenRoom.SlotPosition(slot));
            return new Vector2((world.Viewport.x+p.x*world.Viewport.width)*Screen.width,p.y*Screen.height);
        }
        InputSystem.QueueStateEvent(mouse,new MouseState {position=ScreenPoint(11)});yield return null;yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState {position=ScreenPoint(11),buttons=1});yield return null;yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState {position=ScreenPoint(0),buttons=1});yield return null;yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState {position=ScreenPoint(0)});yield return null;yield return null;yield return Idle();
        InputSystem.RemoveDevice(mouse);mouse=null;
        Check(app.Session.Specimen(newest).wallSlot==0&&app.Session.Specimen(other).wallSlot==11,"real mouse drag swaps occupied wall positions");
        Check(app.SaveSlot(1),"gallery composition saved");app.LoadSlot(1);yield return Page(CampaignPage.Greenhouse);
        Check(app.Session.Specimen(newest).rotation==45&&app.Session.Specimen(newest).wallSlot==0,"gallery survives reload");
        Click("市场");yield return Page(CampaignPage.Market);yield return Capture("03-MarketShop");
        Click("公开拍卖 (自由竞价)");yield return null;yield return Capture("04-AuctionHall");
        var lot=app.Session.State.marketAuctions.First();string lotId=lot.id;long before=app.Session.State.coins;
        InvokeView("OpenMarketAuction",lotId);yield return null;Click("期望价 100%");yield return null;Click("确认举牌");yield return Idle();
        Check(app.Session.GetMarketAuction(lotId).playerBid==lot.expectedPrice&&app.Session.State.coins==before-lot.expectedPrice,"auction UI escrows exact custom bid");
        yield return Capture("05-AuctionBid");
        InvokeView("CloseModal");app.Execute(s=>{while(!s.GetMarketAuction(lotId).closed)s.AdvanceMarketAuction(lotId);});yield return Idle();
        Check(app.Session.GetMarketAuction(lotId).won,"highest expected-price bid is guaranteed");
        var mail=app.Session.State.mail.Last(m=>m.Pending);app.Execute(s=>s.ClaimMail(mail.id));yield return Idle();
        Check(app.Session.State.storedBeetles.Count>0,"auction delivery claims into warehouse");
        InvokeView("OpenWarehouse");yield return null;Click("命名");yield return null;
        FindFirstObjectByType<TMP_InputField>().text="Atlas 07";Click("保存名字");yield return Idle();
        var stored=app.Session.State.storedBeetles.First();string storedId=stored.id;
        Check(app.Modal&&stored.name=="Atlas 07"&&stored.customName,"warehouse naming saves and returns to warehouse");
        Click("命名");yield return null;FindFirstObjectByType<TMP_InputField>().text="Cancelled by Escape";
        keyboard=InputSystem.AddDevice<Keyboard>("Naming cancel validation");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape));yield return null;yield return null;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;yield return null;
        InputSystem.RemoveDevice(keyboard);keyboard=null;
        Check(app.Modal&&!FindFirstObjectByType<TMP_InputField>()&&app.Session.State.storedBeetles.First().name=="Atlas 07","Escape cancels naming and returns to the warehouse");
        Click("安排入住温室");yield return Idle();
        Check(app.Session.Beetle(storedId).name=="Atlas 07","warehouse housing preserves chosen name");
        InvokeView("CloseModal");
        InvokeView("OpenSettings");yield return null;Click("音频");yield return null;yield return Capture("06-VolumeSettings");
        var slider=FindObjectsByType<Slider>(FindObjectsSortMode.None).First(s=>s.name=="Slider 主音量");slider.value=.23f;
        Check(Mathf.Abs(app.Preferences.master-.23f)<.001f&&Mathf.Abs(AudioListener.volume-.23f)<.001f,"volume slider changes live channel");
        CombatDisplaySettingsValidation.Run(slider);
        Click("画面");yield return null;
        var filter=View.GetComponentsInChildren<RetroFilterDropdown>().Single(d=>!d.IsGlassQualityDropdown);filter.Trigger.onClick.Invoke();
        yield return Wait(()=>filter.IsOpen&&!filter.IsAnimating,"filter dropdown");Click("FilterOption-OrderedDither");
        yield return Wait(()=>!filter.IsOpen&&!filter.IsAnimating,"filter selection");
        yield return Capture("07-DisplaySettings");Click("完成");yield return Idle();
        var live=app.Session.State.beetles.First();app.Execute(s=>s.EnterLeague(live.id));yield return Idle();app.Go(CampaignPage.Arena);yield return Page(CampaignPage.Arena);
        BeetleDisplayPreferences.Style=BeetleDisplayStyle.OrderedDither;
        yield return new WaitForSecondsRealtime(.8f);
        var effect=FindFirstObjectByType<BeetleCombatCrt>();Check(effect&&effect.DitherActive&&!effect.CrtActive,"combat selects dither exclusively");
        yield return Capture("08-CombatDither");BeetleDisplayPreferences.Style=BeetleDisplayStyle.Crt;yield return null;yield return null;
        Check(effect.CrtActive&&!effect.DitherActive,"CRT selection replaces dither");yield return Capture("09-CombatCRT");
        Check(errors.Count==0,"no runtime errors or exceptions");
    }
    void Finish(Exception failure)
    {
        Application.logMessageReceived-=Log;
        if(mouse!=null)InputSystem.RemoveDevice(mouse);if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(originalInput)InputSystem.settings=originalInput;if(testInput)Destroy(testInput);
        preferences.Dispose();
        File.AppendAllText(Output+"/Result.txt",(failure==null?"COMPLETE PASS":"FAILED "+failure)+"\n"+string.Join("\n",errors));
        EditorApplication.isPlaying=false;
    }
}
