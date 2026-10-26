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
using UI=UnityEngine.UI;

[InitializeOnLoad]
public static class CampaignEndToEndValidation
{
    const string Key="BugDuel.EndToEndValidation";
    public const string Output="Artifacts/EndToEnd";
    static CampaignEndToEndValidation()
    {
        string path=SessionState.GetString(Key,"");if(path.Length>0)CampaignApp.StorageOverride=path;
        EditorApplication.playModeStateChanged+=OnPlay;
        EditorApplication.update+=()=>{if(SessionState.GetString(Key,"").Length>0&&EditorApplication.isPlaying)EditorApplication.QueuePlayerLoopUpdate();};
    }
    [MenuItem("Tools/甲壳虫斗/验证/战役流程/完整战役流程", false, 560)]
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before isolated campaign validation.");
        Directory.CreateDirectory(Output);string path=Path.GetFullPath(Output+"/Save-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(path);File.WriteAllText(Output+"/Result.txt","RUNNING\nIsolated saves: "+path+"\n");
        SessionState.SetString(Key+".storage",CampaignApp.StorageOverride??"");SessionState.SetString(Key,path);CampaignApp.StorageOverride=path;
        SessionState.SetString(Key+".start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Campaign/CampaignMainMenu.unity");
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Show();EditorApplication.isPlaying=true;
    }
    static void OnPlay(PlayModeStateChange change)
    {
        if(SessionState.GetString(Key,"").Length==0)return;
        if(change==PlayModeStateChange.EnteredPlayMode)
        {var go=new GameObject("Complete campaign acceptance");UnityEngine.Object.DontDestroyOnLoad(go);go.AddComponent<CampaignEndToEndDriver>();}
        if(change!=PlayModeStateChange.EnteredEditMode)return;
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key+".start",""));
        string storage=SessionState.GetString(Key+".storage","");CampaignApp.StorageOverride=storage.Length==0?null:storage;
        SessionState.EraseString(Key);SessionState.EraseString(Key+".start");SessionState.EraseString(Key+".storage");
    }
}

public sealed class CampaignEndToEndDriver:MonoBehaviour
{
    const string Output=CampaignEndToEndValidation.Output;
    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    CampaignApp app;Mouse mouse;Keyboard keyboard;InputSettings originalInput,testInput;
    bool originalBackground,cleaned;int originalLanguage;
    readonly List<string> errors=new List<string>();
    CampaignView View=>FindFirstObjectByType<CampaignView>();
    CampaignLeagueBridge Bridge=>FindFirstObjectByType<CampaignLeagueBridge>();
    RectTransform Modal=>View.GetComponentsInChildren<RectTransform>().FirstOrDefault(rect=>rect.name=="Modal");
    IEnumerable<UI.Button> Buttons=>FindObjectsByType<UI.Button>(FindObjectsSortMode.None).Where(button=>button.gameObject.activeInHierarchy);
    void Start()
    {
        originalBackground=Application.runInBackground;Application.runInBackground=true;originalLanguage=CampaignLocalization.English?1:0;
        Application.logMessageReceived+=Log;originalInput=InputSystem.settings;testInput=Instantiate(originalInput);InputSystem.settings=testInput;
        testInput.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        testInput.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        mouse=InputSystem.AddDevice<Mouse>("Campaign acceptance mouse");keyboard=InputSystem.AddDevice<Keyboard>("Campaign acceptance keyboard");
        StartCoroutine(Guard(Run()));
    }
    void Log(string message,string trace,LogType type)
    {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+trace);}
    IEnumerator Guard(IEnumerator routine)
    {
        var stack=new Stack<IEnumerator>();stack.Push(routine);
        while(stack.Count>0)
        {
            bool moved=false;object current=null;Exception failure=null;
            try{moved=stack.Peek().MoveNext();if(moved)current=stack.Peek().Current;}catch(Exception error){failure=error;}
            if(failure!=null){while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();Finish(failure);yield break;}
            if(!moved){(stack.Pop() as IDisposable)?.Dispose();continue;}
            if(current is IEnumerator nested)stack.Push(nested);else yield return current;
        }
        Finish(null);
    }
    void Check(bool value,string message)
    {if(!value)throw new InvalidOperationException(message+" | "+app?.Message);File.AppendAllText(Output+"/Result.txt","PASS "+message+"\n");}
    IEnumerator Wait(Func<bool> condition,string label,float seconds=45)
    {
        float until=Time.realtimeSinceStartup+seconds;
        while(!condition()){if(Time.realtimeSinceStartup>until)throw new TimeoutException(label+" | "+app?.Message);yield return null;}
        yield return null;yield return null;
    }
    IEnumerator Idle()=>Wait(()=>app&&!app.Busy,"durable commit");
    IEnumerator OnPage(CampaignPage page)=>Wait(()=>app&&!app.Busy&&View&&View.page==page&&SceneManager.GetActiveScene().name==CampaignApp.SceneName(page),page.ToString());
    UI.Button Button(string text)=>Buttons.First(button=>button.IsInteractable()&&(button.name==text||button.GetComponentsInChildren<TMP_Text>().Any(label=>label.text==text)));
    Vector2 ScreenPoint(RectTransform rect)
    {
        var canvas=rect.GetComponentInParent<Canvas>();var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
        return RectTransformUtility.WorldToScreenPoint(camera,rect.TransformPoint(rect.rect.center));
    }
    IEnumerator ClickAt(Vector2 point)
    {
        InputSystem.QueueStateEvent(mouse,new MouseState {position=point});yield return null;yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState {position=point}.WithButton(MouseButton.Left));yield return null;yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState {position=point});yield return null;
    }
    IEnumerator Click(UI.Button button)
    {
        // Newly built modal graphics need one canvas pass before GraphicRaycaster sees them.
        yield return new WaitForEndOfFrame();
        if(!button||!button.IsInteractable())throw new InvalidOperationException("Button is unavailable");
        foreach(var scroll in button.GetComponentsInParent<UI.ScrollRect>())
        {
            if(!scroll.vertical||scroll.content.rect.height<=scroll.viewport.rect.height)continue;
            var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport,button.transform);
            float offset=scroll.viewport.rect.center.y-bounds.center.y;
            scroll.StopMovement();scroll.verticalNormalizedPosition=Mathf.Clamp01(scroll.verticalNormalizedPosition-offset/(scroll.content.rect.height-scroll.viewport.rect.height));
            Canvas.ForceUpdateCanvases();
        }
        yield return new WaitForEndOfFrame();
        Vector2 point=ScreenPoint(button.GetComponent<RectTransform>());var hits=new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
        if(hits.Count==0||hits[0].gameObject.GetComponentInParent<UI.Button>()!=button)
            throw new InvalidOperationException("Button is occluded: "+button.name+" at "+point+"; hits="+string.Join(", ",hits.Select(hit=>hit.gameObject.name)));
        yield return ClickAt(point);
    }
    IEnumerator Click(string text)=>Click(Button(text));
    IEnumerator KeyPress(Key key)
    {
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return null;yield return null;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;yield return null;
    }
    UI.Button SlotButton(int slot,string name)=>Buttons.Where(button=>button.name==name&&button.IsInteractable()).OrderBy(button=>ScreenPoint(button.GetComponent<RectTransform>()).x).ElementAt(slot);
    void Tick(CampaignLeagueBridge bridge,float seconds)=>typeof(CampaignLeagueBridge).GetMethod("Tick",Private).Invoke(bridge,new object[]{seconds});
    void PauseApplication(bool paused)=>typeof(CampaignApp).GetMethod("OnApplicationPause",Private).Invoke(app,new object[]{paused});
    Texture2D GameFrame()
    {
        var game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));RenderTexture target=null;
        for(Type type=game.GetType();type!=null&&!target;type=type.BaseType)
        {
            var field=type.GetField("m_TargetTexture",Private|BindingFlags.DeclaredOnly)??type.GetField("m_RenderTexture",Private|BindingFlags.DeclaredOnly);
            if(field!=null)target=field.GetValue(game) as RenderTexture;
        }
        if(!target)throw new InvalidOperationException("Game View has no composed render texture");
        var previous=RenderTexture.active;var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try{RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();return texture;}
        finally{RenderTexture.active=previous;}
    }
    void SaveFrame(string name)
    {
        var frame=GameFrame();
        try
        {
            var pixels=frame.GetPixels32();
            if(SystemInfo.graphicsUVStartsAtTop)
            {var upright=new Color32[pixels.Length];for(int y=0;y<frame.height;y++)Array.Copy(pixels,y*frame.width,upright,(frame.height-1-y)*frame.width,frame.width);frame.SetPixels32(upright);frame.Apply();}
            File.WriteAllBytes(Output+"/"+name+".png",frame.EncodeToPNG());
        }
        finally{Destroy(frame);}
    }
    IEnumerator Capture(string name)
    {
        var game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));game.Focus();game.Repaint();
        yield return new WaitForEndOfFrame();SaveFrame(name);
    }
    IEnumerator Run()
    {
        app=CampaignApp.Ensure();CampaignLocalization.Select(0);yield return OnPage(CampaignPage.MainMenu);yield return Wait(()=>!CampaignLocalization.English,"Chinese acceptance labels");
        Check(app.Session==null&&Path.GetFullPath(app.SaveDirectory)==Path.GetFullPath(CampaignApp.StorageOverride),"fresh MainMenu uses isolated saves and has no active journey");
        yield return Click("开始 / 存档");yield return OnPage(CampaignPage.Saves);
        yield return Click(SlotButton(0,"新建存档"));yield return Wait(()=>app.Modal&&Modal&&Modal.GetComponentInChildren<TMP_InputField>(),"new journey naming");
        Modal.GetComponentInChildren<TMP_InputField>().text="流程验收 Ada";yield return Click("开始旅程");yield return OnPage(CampaignPage.Greenhouse);
        Check(app.ReadSlot(0).state.playerName=="流程验收 Ada"&&!app.NeedsPlayerName,"real new-game naming durably creates the journey before entering the greenhouse");
        string id=app.Session.State.selectedId,food=CampaignItems.Food(0);
        // Fixture changes only starting funds, healthy adults and a stable husbandry rate.
        // Match winners and all rewards below are produced by the actual combat controller.
        Check(app.Execute(s=>
        {
            s.State.coins=20000;s.ConfigureClock(86400,1,false);s.State.random=1987;
            foreach(var b in s.State.beetles){b.stage=CampaignLifeStage.ActiveAdult;b.stageAgeDays=b.adultAgeDays=0;b.dead=b.overturned=false;b.hearts=b.maxHearts;b.nutrition=55;b.meal=null;b.hasMeal=false;}
            s.Beetle(id).nutrition=25;
        }),"prepare the isolated funds and healthy-adult fixture");yield return Idle();
        app.Preferences.battleSpeed=1;
        long funds=app.Session.State.coins;int stock=app.Session.InventoryCount(food);
        yield return Click("市场");yield return OnPage(CampaignPage.Market);
        yield return Click(Buttons.Single(button=>button.name.EndsWith("食物与养殖补给",StringComparison.Ordinal)));yield return null;yield return null;
        var foodCard=View.GetComponentsInChildren<RectTransform>().Single(rect=>rect.name=="GoodsCard"&&rect.GetComponentsInChildren<TMP_Text>().Any(label=>label.text==CampaignItems.Name(food)));
        yield return Click(foodCard.GetComponentInChildren<UI.Button>());yield return Wait(()=>app.Modal&&Modal,"supply order dialog");
        Modal.GetComponentInChildren<TMP_InputField>().text="3";yield return Click("确认购买");yield return Idle();
        var order=app.Session.State.mail.Last(mail=>mail.Pending&&mail.attachments.Any(attachment=>attachment.itemId==food));string mailId=order.id;
        Check(app.Session.State.coins==funds-3*app.Session.SupplyBuyPrice(food)&&app.Session.InventoryCount(food)==stock&&order.attachments.Single().quantity==3,
            "market quantity purchase charges once and delivers three supplies to mail, not directly to inventory");
        yield return Click("邮箱");yield return null;yield return null;
        yield return Click(Buttons.Where(button=>button.name=="查看 · 有附件").OrderByDescending(button=>ScreenPoint(button.GetComponent<RectTransform>()).y).First());yield return Idle();
        yield return Click("领取全部附件");yield return Idle();
        Check(app.Session.State.mail.Single(mail=>mail.id==mailId).claimed&&app.Session.InventoryCount(food)==stock+3&&app.ReadSlot(0).state.mail.Single(mail=>mail.id==mailId).claimed,
            "opening the actual delivery and claiming attachments durably moves the order into the warehouse");
        yield return KeyPress(Key.Escape);yield return Wait(()=>!app.Modal,"mailbox close");yield return Click("温室");yield return OnPage(CampaignPage.Greenhouse);
        yield return Click("照料");yield return null;yield return null;
        yield return Click(Buttons.Single(button=>button.name.StartsWith("红薯 ×",StringComparison.Ordinal)));yield return Idle();
        Check(app.Session.InventoryCount(food)==stock+2&&app.Session.Beetle(id).meal!=null,"greenhouse feed button consumes one claimed supply and places a meal");
        yield return Wait(()=>!app.Busy&&app.Session.Beetle(id).meal==null,"actual greenhouse feeding",150);
        Check(app.Session.Beetle(id).nutrition>=59&&app.ReadSlot(0).state.beetles.Single(beetle=>beetle.id==id).meal==null,"actual feeding behavior completes and saves its nutrition recovery");
        yield return Capture("01-Greenhouse-Fed");

        funds=app.Session.State.coins;long fee=app.Session.Catalog.Tier(app.Session.Beetle(id).length).fee;
        yield return Click("联赛");yield return OnPage(CampaignPage.League);
        yield return Click(Buttons.Where(button=>button.name.StartsWith("报名  ",StringComparison.Ordinal)).OrderByDescending(button=>ScreenPoint(button.GetComponent<RectTransform>()).y).First());yield return OnPage(CampaignPage.Arena);
        var bridge=Bridge;var controller=bridge.controller;var reservation=app.Session.State.battle;string matchId=reservation.id;int seed=reservation.seed;
        Check(controller.Simulation!=null&&controller.Simulation.MatchId==matchId&&app.Session.State.coins==funds-fee&&app.ReadSlot(0).state.battle.id==matchId,
            "league button saves and charges one reservation before binding the actual combat simulation");
        double battleDay=app.Session.State.clock.elapsedDays;
        Check(!app.Manage(CampaignPage.Arena,"NextWeek")&&!app.Manage(CampaignPage.Arena,"Help")&&!app.CalendarRunning,"arena blocks husbandry shortcuts and freezes the campaign calendar");
        bridge.enabled=false;float clock=controller.RoundClock,motion=controller.Simulation.MotionTime;Tick(bridge,.1f);
        Check(Mathf.Abs(controller.RoundClock-clock-.1f)<.001f&&controller.Simulation.MotionTime-motion<=.061f,"10 FPS frame advances the full round deadline while physics catch-up remains capped");
        clock=controller.RoundClock;motion=controller.Simulation.MotionTime;Tick(bridge,2f);
        Check(Mathf.Abs(controller.RoundClock-clock-2f)<.001f&&controller.Simulation.MotionTime-motion<=.061f,"a two-second stall cannot extend the real round deadline");bridge.enabled=true;
        yield return Click("倍速 1×");yield return null;yield return null;yield return Click("倍速 2×");yield return null;yield return null;
        Check(bridge.Speed==4,"actual arena speed buttons reach four-times play speed");
        yield return KeyPress(Key.Escape);yield return Wait(()=>app.Modal,"arena exit confirmation");clock=controller.RoundClock;motion=controller.Simulation.MotionTime;
        Tick(bridge,10);yield return new WaitForSecondsRealtime(.2f);
        Check(controller.RoundClock==clock&&controller.Simulation.MotionTime==motion&&app.Session.State.clock.elapsedDays==battleDay,"a visible arena modal freezes both round time and simulation without advancing husbandry");
        yield return Capture("02-Arena-Paused");yield return Click("完成");yield return Wait(()=>!app.Modal,"resume arena");
        yield return Wait(()=>controller.RoundClock>clock,"round clock resumes after closing its modal");
        Check(app.SaveSlot(1)&&app.ReadSlot(1).state.battle.id==matchId,"mid-match manual snapshot preserves the original reservation and seed");
        yield return KeyPress(Key.Escape);yield return Wait(()=>app.Modal,"leave arena settings");yield return Click("返回主菜单");yield return OnPage(CampaignPage.MainMenu);
        yield return Click("开始 / 存档");yield return OnPage(CampaignPage.Saves);yield return Click(SlotButton(1,"读取"));yield return OnPage(CampaignPage.Greenhouse);
        Check(app.Session.State.battle.id==matchId&&app.Session.State.battle.seed==seed&&app.Session.State.coins==funds-fee&&!app.CalendarRunning,
            "reload preserves the pending match without charging again or advancing the campaign calendar");
        yield return Click("联赛");yield return OnPage(CampaignPage.League);yield return Click("重返赛场");yield return OnPage(CampaignPage.Arena);
        bridge=Bridge;controller=bridge.controller;
        Check(controller.Simulation.MatchId==matchId&&controller.Simulation.Seed==seed&&controller.RoundClock<2&&app.Session.State.coins==funds-fee,
            "re-entering explicitly restarts the same seeded match from the beginning with no second entry fee");
        yield return Click("倍速 1×");yield return null;yield return null;yield return Click("倍速 2×");yield return null;yield return null;
        long beforeResult=app.Session.State.coins;int beforeWins=app.Session.Beetle(id).wins;
        // Hold only the isolated temp file. Combat still runs normally to its own result.
        using(var locked=new FileStream(app.SlotPath(0)+".tmp",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
        {
            yield return Wait(()=>bridge.SettlementPending&&!app.Busy&&app.Message.Contains("保存失败"),"real match result and deliberately failed settlement write",120);
            // Stop only the automatic retry timer while inspecting the failure; the
            // actual retry button below still calls the production durable path.
            bridge.enabled=false;
            yield return Wait(()=>Buttons.Any(button=>button.name=="重试保存"),"pending-settlement feedback");
            Check(controller.Simulation.Result!=null&&!bridge.IsFinished&&app.Session.State.battle.id==matchId&&app.Session.State.coins==beforeResult&&app.Session.Beetle(id).wins==beforeWins,
                "a real result with a failed save keeps the reservation and rewards unchanged and offers a visible retry");
            yield return Capture("03-Settlement-Save-Failure");
        }
        var retry=Button("重试保存");bool pauseFlushed=false;
        UnityEngine.Events.UnityAction pauseDuringCommit=()=>{pauseFlushed=app.Busy;PauseApplication(true);};
        retry.onClick.AddListener(pauseDuringCommit);
        try{yield return Click(retry);yield return Wait(()=>bridge.IsFinished&&!app.Busy,"retry and pause-flush settlement");}
        finally{if(retry)retry.onClick.RemoveListener(pauseDuringCommit);PauseApplication(false);if(bridge)bridge.enabled=true;}
        long settled=app.Session.State.coins;int wins=app.Session.Beetle(id).wins;var outcome=controller.Simulation.Result;
        long reward=outcome.Winner==0?app.Session.Catalog.Tier(app.Session.Beetle(id).length).win:0;
        Check(pauseFlushed&&app.Session.State.battle==null&&settled==beforeResult+reward&&wins==beforeWins+(outcome.Winner==0?1:0)&&app.ReadSlot(0).state.battle==null,
            "manual retry plus application pause completes the durable callback and settles the actual outcome exactly once");
        Check(!app.Execute(s=>s.SettleBattle(matchId,true))&&app.Session.State.coins==settled&&app.Session.Beetle(id).wins==wins,"duplicate settlement cannot award money or wins again");
        yield return Capture("04-Real-Result");
        Check(app.SaveSlot(2),"save the completed match for a fresh load check");long currencySequence=app.CurrencySequence;
        app.LoadSlot(2);yield return OnPage(CampaignPage.Greenhouse);
        Check(app.Session.State.battle==null&&app.Session.State.coins==settled&&app.Session.Beetle(id).wins==wins&&app.CurrencySequence==currencySequence,
            "loading a completed match preserves one reward and emits no fresh currency delta");

        // This isolated workflow shortens development only; production durations remain unchanged.
        var originalLifeRules=app.Session.State.lifeRules.Copy();
        Check(app.Execute(s=>
        {
            s.ConfigureClock(86400,1,true);
            s.ConfigureLifeRules(new CampaignLifeRules {eggDays=1,larva1Days=1,larva2Days=1,larva3Days=3,prepupaDays=1,pupaDays=1,dormantDays=1,adultDays=180});
            foreach(var b in s.State.beetles){b.hearts=b.maxHearts;b.nutrition=100;b.overturned=false;}
            s.State.selectedId=id;s.State.random=0x12345678;
        }),"prepare healthy parents and a short development cycle only in the isolated workflow");yield return Idle();
        int residents=app.Session.State.beetles.Count;funds=app.Session.State.coins;
        var breedingOffer=app.Session.BreedingOffer(id,0);string motherId=breedingOffer.Mother.id;
        yield return Click("育种中心");yield return OnPage(CampaignPage.Breeding);
        yield return Click(Buttons.Where(button=>button.name=="选择配种").OrderByDescending(button=>ScreenPoint(button.GetComponent<RectTransform>()).y).First());
        // Freeze the random fixture inside the confirmation modal; background care can
        // consume the shared RNG while travelling from the greenhouse to the center.
        Check(app.Execute(s=>s.State.random=0x12345678),"seed the isolated successful-breeding branch while care is paused");yield return Idle();
        yield return Click("确定");yield return Idle();
        string childId=app.Session.State.selectedId;var child=app.Session.Beetle(childId);
        Check(childId!=id&&child.stage==CampaignLifeStage.Egg&&child.father==id&&child.mother==motherId&&app.Session.State.beetles.Count==residents+1&&app.Session.State.coins==funds-breedingOffer.Fee&&
            app.ReadSlot(0).state.beetles.Single(b=>b.id==childId).stage==CampaignLifeStage.Egg,
            "center breeding charges the quoted fee once and durably produces an owned egg with both parent IDs");
        yield return Click("温室");yield return OnPage(CampaignPage.Greenhouse);funds=app.Session.State.coins;
        yield return Click("选用营养与温控方案");yield return Idle();
        Check(app.Session.Beetle(childId).feed==1&&app.Session.Beetle(childId).climate==1&&app.Session.State.coins==funds-app.Session.Catalog.feeds[1].cost,
            "the egg care panel saves the chosen nutrition and temperature plan with its exact fee");
        yield return Capture("04a-Breeding");

        yield return Click("捕虫队");yield return OnPage(CampaignPage.Catching);funds=app.Session.State.coins;
        string[] existingIds=app.Session.State.beetles.Select(b=>b.id).ToArray();int departureWeek=app.Session.State.week;
        yield return Click("派遣捕虫队");yield return Idle();var expedition=app.Session.State.expeditions.Single();
        Check(expedition.habitat=="oak"&&expedition.count==2&&app.Session.Reserved==2&&app.Session.State.coins==funds-app.Session.Catalog.Catch("oak").cost&&
            app.Session.State.beetles.Count==existingIds.Length&&app.ReadSlot(0).state.expeditions.Single().id==expedition.id,
            "the catching page charges the oak expedition and saves two reserved slots before any beetles arrive");
        yield return KeyPress(Key.N);yield return Idle();
        Check(app.Session.State.week==departureWeek&&app.Session.State.expeditions.Count==1&&app.Controls.FindAction("NextWeek",false)==null,
            "the removed N shortcut cannot skip days or complete an expedition");
        // Only this editor fixture shortens the real-time day; the player UI exposes
        // speed alone. Arrivals and growth below use the production calendar loop.
        Check(app.Execute(s=>s.ConfigureClock(10,8,false)),"prepare a short real-time day only for the natural-arrival fixture");yield return Idle();
        yield return Wait(()=>!app.Busy&&app.Session.State.week>departureWeek,"natural expedition return",60);
        string[] caughtIds=app.Session.State.beetles.Where(b=>!existingIds.Contains(b.id)).Select(b=>b.id).ToArray();
        Check(app.Session.State.week==departureWeek+1&&app.Session.Reserved==0&&app.Session.State.expeditions.Count==0&&caughtIds.Length==2&&
            app.Session.State.beetles.Count==existingIds.Length+2&&caughtIds.All(caught=>app.Session.Beetle(caught).Adult(app.Session.State.week))&&!app.Session.Beetle(childId).Adult(app.Session.State.week),
            "natural calendar progression returns two caught adults into reserved greenhouse slots while the younger brood still develops");
        yield return Capture("04b-Catching-Returned");yield return Click("温室");yield return OnPage(CampaignPage.Greenhouse);
        yield return Wait(()=>!app.Busy&&app.Session.State.week>=departureWeek+2,"natural brood development",60);
        Check(app.Session.Beetle(childId).Adult(app.Session.State.week)&&app.Session.State.beetles.Count==existingIds.Length+2&&app.Session.State.expeditions.Count==0,
            "natural time completes the explicit short lifecycle without delivering the expedition twice");
        Check(app.Execute(s=>{s.ConfigureLifeRules(originalLifeRules);s.ConfigureClock(86400,1,true);}),"restore the slow editor fixture clock and production lifecycle rules after natural progression");yield return Idle();
        var familySave=app.ReadSlot(0);
        Check(familySave.state.beetles.Single(b=>b.id==childId).stage==CampaignLifeStage.ActiveAdult&&familySave.state.beetles.Single(b=>b.id==childId).father==id&&
            caughtIds.All(caught=>familySave.state.beetles.Count(b=>b.id==caught)==1)&&familySave.state.expeditions.Count==0&&familySave.state.lifeRules.larva3Days==originalLifeRules.larva3Days,
            "the saved family, caught individuals and restored development rules survive the actual JSON repository");

        var originalCatalog=JsonUtility.FromJson<CampaignCatalog>(JsonUtility.ToJson(app.Session.Catalog));string originalHabitat=app.Session.Beetle(id).habitat;
        var updatedCatalog=JsonUtility.FromJson<CampaignCatalog>(JsonUtility.ToJson(originalCatalog));updatedCatalog.revision++;
        updatedCatalog.housing=updatedCatalog.housing.Concat(new[]{new HousingRule("acceptance-habitat","Acceptance Habitat",25,70,.5,0)}).ToArray();
        updatedCatalog.species=updatedCatalog.species.Concat(new[]{new SpeciesRule("acceptance-beetle","Acceptance Beetle",50,200,.3)}).ToArray();
        try
        {
            File.WriteAllText(app.ContentPath,JsonUtility.ToJson(updatedCatalog));app.ReloadContent();yield return Idle();
            Check(app.Session.Catalog.revision==updatedCatalog.revision&&app.Session.Catalog.Species("acceptance-beetle")!=null&&app.ReadSlot(0).catalog.revision==updatedCatalog.revision,
                "the production hot-reload interface durably accepts a higher revision with new species and habitat definitions");
            Check(app.Execute(s=>s.House(id,"acceptance-habitat")),"occupy the newly loaded habitat for its reference check");yield return Idle();
            var occupiedSession=app.Session;var invalidCatalog=JsonUtility.FromJson<CampaignCatalog>(JsonUtility.ToJson(updatedCatalog));invalidCatalog.revision++;
            invalidCatalog.housing=invalidCatalog.housing.Where(h=>h.id!="acceptance-habitat").ToArray();
            File.WriteAllText(app.ContentPath,JsonUtility.ToJson(invalidCatalog));app.ReloadContent();yield return Idle();
            Check(app.Session==occupiedSession&&app.Session.Catalog.revision==updatedCatalog.revision&&app.Session.Beetle(id).habitat=="acceptance-habitat"&&
                app.ReadSlot(0).catalog.revision==updatedCatalog.revision,
                "removing an occupied habitat is rejected without replacing live state or its durable catalog snapshot");
            Check(app.Execute(s=>s.House(id,originalHabitat)),"return the beetle to its original habitat before restoring definitions");yield return Idle();
            originalCatalog.revision=updatedCatalog.revision+1;File.WriteAllText(app.ContentPath,JsonUtility.ToJson(originalCatalog));app.ReloadContent();yield return Idle();
            Check(app.Session.Catalog.revision==originalCatalog.revision&&!app.Session.Catalog.species.Any(s=>s.id=="acceptance-beetle")&&
                !app.Session.Catalog.housing.Any(h=>h.id=="acceptance-habitat")&&app.ReadSlot(0).catalog.revision==originalCatalog.revision,
                "a final valid reload restores the original definitions and saves their new revision");
        }
        finally { File.WriteAllText(app.ContentPath,JsonUtility.ToJson(app.Session.Catalog)); }

        // The actual result is never rewritten. A single honor bit enables this independent sales branch.
        Check(app.Execute(s=>{var b=s.Beetle(id);b.championships|=1;b.hearts=b.maxHearts;b.nutrition=100;b.overturned=false;s.State.selectedId=id;s.ConfigureClock(86400,1,true);}),"prepare one champion honor for the independent auction branch");yield return Idle();
        yield return Click("市场");yield return OnPage(CampaignPage.Market);yield return Click("拍卖");yield return null;yield return null;yield return Click("冠军拍卖");yield return null;yield return null;yield return Click("只拍卖一次首配权");yield return Idle();
        Check(app.Session.State.auction!=null&&app.ReadSlot(0).state.auction!=null,"champion auction entry creates a durable sale while keeping its beetle");
        while(app.Session.State.auction.round<app.Session.State.auction.rounds){yield return Click("下一轮举牌");yield return Idle();}
        long bid=app.Session.State.auction.bid;funds=app.Session.State.coins;yield return Click("落槌成交");yield return Idle();
        Check(app.Session.State.auction==null&&app.Session.Beetle(id).rightsSold&&app.Session.State.coins==funds+bid&&app.Session.State.hall.Last().rights,
            "all champion bidding rounds close once, pay the final bid and retain the sold-rights champion");
        Check(!app.Execute(s=>s.CloseAuction())&&app.Session.State.coins==funds+bid,"champion auction also rejects a second close");yield return Capture("05-Champion-Auction");
        yield return Click("温室");yield return OnPage(CampaignPage.Greenhouse);
        Check(app.Execute(s=>{var b=s.Beetle(id);b.dead=true;b.hearts=0;b.overturned=false;b.meal=null;b.hasMeal=false;s.State.selectedId=id;}),"prepare one deceased owned beetle for the memorial branch");yield return Idle();
        funds=app.Session.State.coins;yield return Click(Buttons.Single(button=>button.name.StartsWith("制作装框标本",StringComparison.Ordinal)));yield return Click("确定");yield return OnPage(CampaignPage.SpecimenRoom);
        string specimenId=app.Session.State.specimens.Last().id;
        Check(app.Session.State.beetles.All(b=>b.id!=id)&&app.Session.State.specimens.Last().beetle.id==id&&app.Session.State.coins==funds-CampaignSession.SpecimenPreparationCost,
            "memorial confirmation charges once, removes the deceased beetle and enters its specimen room");
        yield return Click("01");yield return Idle();yield return Click("编辑铭牌");yield return Wait(()=>app.Modal,"plaque editor");
        Modal.GetComponentsInChildren<TMP_InputField>()[0].text="流程纪念";yield return Click("保存铭牌");yield return Idle();
        Check(app.Session.State.specimens.Single(s=>s.id==specimenId).wallSlot==0&&app.Session.State.specimens.Single(s=>s.id==specimenId).labelName=="流程纪念",
            "specimen wall placement and an edited plaque commit through real buttons");yield return Capture("06-Specimen-Room");
        yield return Click("返回温室");yield return OnPage(CampaignPage.Greenhouse);Check(app.SaveSlot(3),"save the completed campaign branches");
        funds=app.Session.State.coins;currencySequence=app.CurrencySequence;app.LoadSlot(3);yield return OnPage(CampaignPage.Greenhouse);
        Check(app.Session.State.coins==funds&&app.CurrencySequence==currencySequence&&app.Session.State.battle==null&&app.Session.State.auction==null&&
            app.Session.State.specimens.Single(s=>s.id==specimenId).wallSlot==0&&app.Session.State.specimens.Single(s=>s.id==specimenId).labelName=="流程纪念"&&app.Session.State.hall.Count==1&&
            app.Session.Beetle(childId).father==id&&app.Session.Beetle(childId).Adult(app.Session.State.week)&&caughtIds.All(caught=>app.Session.State.beetles.Count(b=>b.id==caught)==1)&&app.Session.State.expeditions.Count==0,
            "final reload retains the family, caught individuals, plaque, wall slot, hall and balance without replaying arrivals or settlements");
        yield return Click("标本室");yield return OnPage(CampaignPage.SpecimenRoom);yield return Click("返回温室");yield return OnPage(CampaignPage.Greenhouse);
        Check(!app.Modal&&!app.Busy&&errors.Count==0,"final navigation remains usable with no runtime errors or exceptions");
    }
    void Cleanup()
    {
        if(cleaned)return;cleaned=true;Application.logMessageReceived-=Log;
        if(mouse!=null)InputSystem.RemoveDevice(mouse);if(keyboard!=null)InputSystem.RemoveDevice(keyboard);
        if(originalInput)InputSystem.settings=originalInput;if(testInput)Destroy(testInput);
        Application.runInBackground=originalBackground;CampaignLocalization.Select(originalLanguage);
    }
    void OnDestroy()=>Cleanup();
    void Finish(Exception failure)
    {
        if(failure!=null){try{SaveFrame("Failure");}catch(Exception capture){File.AppendAllText(Output+"/Result.txt","Screenshot unavailable: "+capture.Message+"\n");}}
        Cleanup();File.AppendAllText(Output+"/Result.txt",(failure==null?"COMPLETE PASS":"FAILED "+failure)+"\n"+string.Join("\n",errors));EditorApplication.isPlaying=false;
    }
}
