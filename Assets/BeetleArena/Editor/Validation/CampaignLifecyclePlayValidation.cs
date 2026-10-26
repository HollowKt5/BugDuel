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
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class CampaignLifecyclePlayValidation
{
    const string Key="BugDuel.LifecyclePlayValidation";
    public const string Output="Artifacts/Lifecycle/Play";
    static CampaignLifecyclePlayValidation()
    {
        string path=SessionState.GetString(Key,"");if(path.Length>0)CampaignApp.StorageOverride=path;
        EditorApplication.playModeStateChanged+=OnPlay;
        EditorApplication.update+=()=>{if(SessionState.GetString(Key,"").Length>0&&EditorApplication.isPlaying)EditorApplication.QueuePlayerLoopUpdate();};
    }
    [MenuItem("Tools/甲壳虫斗/验证/战役流程/生命周期实机流程", false, 565)]
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before the isolated lifecycle validation.");
        Directory.CreateDirectory(Output);
        var session=CampaignSession.New(CampaignCatalog.Defaults(),67125);session.RenamePlayer("验收玩家");session.State.coins=100000;session.State.capacity=12;
        // Legacy player settings are retained except for a manual pause on load.
        session.ConfigureClock(720,1,true);session.ConfigureLifeRules(new CampaignLifeRules {larva3Days=200});
        foreach(var adult in session.State.beetles)adult.habitat="recovery";
        session.State.clock.elapsedDays=359.999;session.State.week=52;session.RefreshMarket();
        for(int i=0;i<(int)CampaignLifeStage.ActiveAdult;i++)
        {
            var beetle=session.Create("red",48);beetle.stage=(CampaignLifeStage)i;beetle.stageAgeDays=session.StageDuration(beetle)*.35;
            beetle.totalAgeDays=beetle.stageAgeDays;beetle.feed=i<4?-1:0;session.State.beetles.Add(beetle);
            session.RenameBeetle(beetle.id,"观察 · "+CampaignSession.StageName(beetle.stage));
        }
        session.Validate();
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
        { var go=new GameObject("Lifecycle acceptance");UnityEngine.Object.DontDestroyOnLoad(go);go.AddComponent<CampaignLifecyclePlayDriver>(); }
        if(change==PlayModeStateChange.EnteredEditMode)
        {
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key+".start",""));
            SessionState.EraseString(Key);SessionState.EraseString(Key+".start");CampaignApp.StorageOverride=null;
        }
    }
}

public sealed class CampaignLifecyclePlayDriver:MonoBehaviour
{
    const string Output=CampaignLifecyclePlayValidation.Output;
    CampaignApp app;
    readonly List<string> errors=new List<string>();
    CampaignView View=>FindFirstObjectByType<CampaignView>();
    void Start(){Application.runInBackground=true;Application.logMessageReceived+=Log;StartCoroutine(Guard(Run()));}
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
    void Check(bool pass,string message){if(!pass)throw new InvalidOperationException(message+" | "+app?.Message);File.AppendAllText(Output+"/Result.txt","PASS "+message+"\n");}
    IEnumerator Wait(Func<bool> condition,string label)
    {
        float until=Time.realtimeSinceStartup+40;
        while(!condition()){if(Time.realtimeSinceStartup>until)throw new TimeoutException(label+" | "+app?.Message);yield return null;}
        yield return null;yield return null;
    }
    IEnumerator Idle()=>Wait(()=>app&&!app.Busy,"commit");
    IEnumerator Page(CampaignPage page)=>Wait(()=>app&&!app.Busy&&SceneManager.GetActiveScene().name==CampaignApp.SceneName(page),page.ToString());
    void Click(string caption)
    {
        var button=FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(b=>b.gameObject.activeInHierarchy&&b.name==caption&&b.IsInteractable());
        if(!button)throw new InvalidOperationException("Missing enabled button: "+caption);
        ExecuteEvents.Execute(button.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);
    }
    void InvokeView(string method,params object[] args)=>typeof(CampaignView).GetMethods(BindingFlags.Instance|BindingFlags.NonPublic).First(m=>m.Name==method&&m.GetParameters().Length==args.Length).Invoke(View,args);
    IEnumerator Capture(string name)
    {
        var game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));game.Focus();game.Repaint();
        yield return new WaitForSecondsRealtime(.5f);yield return new WaitForEndOfFrame();
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
            RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();
            var pixels=texture.GetPixels32();
            Check(pixels.Where((p,i)=>i%47==0).Select(p=>(int)p.r+(p.g<<8)+(p.b<<16)).Distinct().Take(33).Count()>32,"composited view: "+name);
            if(SystemInfo.graphicsUVStartsAtTop)
            {
                var upright=new Color32[pixels.Length];for(int y=0;y<target.height;y++)Array.Copy(pixels,y*target.width,upright,(target.height-1-y)*target.width,target.width);
                texture.SetPixels32(upright);texture.Apply();
            }
            File.WriteAllBytes(Output+"/"+name+".png",texture.EncodeToPNG());
        }
        finally{RenderTexture.active=previous;Destroy(texture);}
    }
    IEnumerator ThemeDropdownAnimation()
    {
        Click("主题(T) ▼");BeetleArena.BeetlePanelMotion motion=null;float deadline=Time.realtimeSinceStartup+5;
        while(!motion)
        {
            motion=View.GetComponentsInChildren<BeetleArena.BeetlePanelMotion>().FirstOrDefault(panel=>panel.name=="ThemeDropdown");
            if(Time.realtimeSinceStartup>deadline)throw new TimeoutException("theme dropdown creation");
            if(!motion)yield return null;
        }
        var opening=new List<Vector2>();
        do
        {
            opening.Add(new Vector2(motion.Panel.localScale.x,motion.Alpha));
            if(Time.realtimeSinceStartup>deadline)throw new TimeoutException("theme dropdown opening");
            yield return new WaitForEndOfFrame();
        }while(motion.IsAnimating);
        Check(opening[0].x<.999f&&opening[0].y<.99f&&opening.Any(sample=>sample.x>.945f&&sample.x<.999f)&&opening.Any(sample=>sample.y>.05f&&sample.y<.95f),
            "actual market theme button opens its dropdown with visible scale and alpha progression");
        Check(motion.Visible&&Mathf.Abs(motion.Alpha-1)<.001f&&Vector3.Distance(motion.Panel.localScale,Vector3.one)<.001f,
            "theme dropdown finishes fully opaque at its original size");
        yield return Capture("03a-ThemeDropdown");
        Click("MenuDismiss");var group=motion.GetComponent<CanvasGroup>();
        Check(!motion.Visible&&motion.IsAnimating&&motion.gameObject.activeSelf&&!group.interactable&&!group.blocksRaycasts,
            "dismissing the theme menu starts closing while immediately disabling dropdown input");
        var closing=new List<Vector2>();deadline=Time.realtimeSinceStartup+5;
        while(motion&&motion.gameObject.activeSelf)
        {
            closing.Add(new Vector2(motion.Panel.localScale.x,motion.Alpha));
            if(Time.realtimeSinceStartup>deadline)throw new TimeoutException("theme dropdown closing");
            yield return new WaitForEndOfFrame();
        }
        Check(closing.Any(sample=>sample.x<.999f&&sample.x>.94f)&&closing.Any(sample=>sample.y>.01f&&sample.y<.95f)&&(!motion||!motion.gameObject.activeSelf),
            "theme dropdown visibly shrinks and fades before becoming inactive or being removed");
    }
    IEnumerator Run()
    {
        yield return Wait(()=>CampaignApp.Instance&&!CampaignApp.Instance.Busy,"startup");app=CampaignApp.Instance;app.LoadSlot(0);yield return Page(CampaignPage.Greenhouse);
        int frame=Time.frameCount;double start=app.Session.State.clock.elapsedDays;yield return new WaitForSecondsRealtime(.8f);
        Check(Time.frameCount>frame+2&&app.Session.State.clock.elapsedDays>start,"natural calendar advances while the player loop runs");
        Check(app.Session.State.clock.Year==2&&app.Session.State.clock.Month==1&&app.Session.State.clock.Day==1,"natural clock crosses month and year boundaries");
        Check(FindFirstObjectByType<CampaignCalendarReadout>(),"calendar is visible in the greenhouse");
        Check(!app.Session.State.clock.paused&&!app.ReadSlot(0).state.clock.paused&&app.Session.State.clock.realSecondsPerDay==720&&app.Session.State.lifeRules.larva3Days==200,"loading a legacy pause durably resumes natural time while retaining the saved day baseline and stage rules");
        var buttons=FindObjectsByType<Button>(FindObjectsSortMode.None);
        Check(app.Controls.FindAction("NextWeek",false)==null&&!app.Manage(CampaignPage.Greenhouse,"NextWeek")&&
            !buttons.Any(b=>b.name.Contains("快进")||b.name=="暂停时间"||b.name=="继续时间"),"players have no manual pause, direct day skip or next-week input binding");
        Click("时间设置");yield return null;
        double paused=app.Session.State.clock.elapsedDays;string rules=JsonUtility.ToJson(app.Session.State.lifeRules);
        Check(FindObjectsByType<TMP_InputField>(FindObjectsSortMode.None).Length==0,"time settings expose speed choices without editable dates, day baselines or lifecycle durations");
        Click("32×");yield return Capture("01-TimeSettings");
        Check(app.Session.State.clock.elapsedDays==paused&&!app.CalendarRunning,"the open settings panel automatically freezes natural time");
        Click("保存并返回");yield return Idle();
        Check(!app.Modal&&!app.Session.State.clock.paused&&app.Session.State.clock.realSecondsPerDay==720&&app.Session.State.clock.speed==32&&JsonUtility.ToJson(app.Session.State.lifeRules)==rules,"time settings save only the chosen multiplier without changing the baseline or stage rules");
        start=app.Session.State.clock.elapsedDays;yield return new WaitForSecondsRealtime(.8f);
        Check(app.Session.State.clock.elapsedDays-start>.02,"selected speed accelerates natural game time");
        Click("景观建造");yield return Idle();paused=app.Session.State.clock.elapsedDays;yield return new WaitForSecondsRealtime(.6f);
        Check(app.Constructing&&!app.Session.State.clock.paused&&!app.CalendarRunning&&app.Session.State.clock.elapsedDays==paused,"construction automatically freezes time without storing a manual pause");
        Click("完成布置 · 放回甲虫");yield return Idle();start=app.Session.State.clock.elapsedDays;yield return new WaitForSecondsRealtime(.6f);
        Check(!app.Constructing&&app.CalendarRunning&&app.Session.State.clock.elapsedDays>start,"finishing construction automatically resumes natural time");
        Check(View.GetComponentsInChildren<Button>().Any(b=>b.name=="日常照料"&&!b.interactable)&&
            View.GetComponentsInChildren<ScrollRect>().Any(s=>s.name=="CareScroll"&&s.gameObject.activeInHierarchy),
            "finishing construction automatically returns to the selected daily-care tab and its visible care content");
        Check(app.Execute(s=>s.ConfigureClock(s.State.clock.realSecondsPerDay,s.State.clock.speed,true)),"freeze only the editor stage and supply fixtures after checking natural time");yield return Idle();
        string larvaId=null,eggId=null;
        foreach(CampaignLifeStage stage in Enum.GetValues(typeof(CampaignLifeStage)))
        {
            var beetle=app.Session.State.beetles.First(b=>b.stage==stage);app.Select(beetle.id);
            if(stage==CampaignLifeStage.Larva3)larvaId=beetle.id;if(stage==CampaignLifeStage.Egg)eggId=beetle.id;
            yield return new WaitForSecondsRealtime(.5f);
            var display=FindFirstObjectByType<CampaignLifeStageDisplay>();
            Check(display&&display.CurrentStage==stage&&display.ObservationBounds.size.sqrMagnitude>.01f,"visible stage representation: "+stage);
            Check(stage==CampaignLifeStage.ActiveAdult?!display.HasStageEnvironment:display.HasStageEnvironment,"correct enclosure selection: "+stage);
            yield return Capture("02-Stage-"+(int)stage+"-"+stage);
            Check(CampaignLifeStageVisualValidation.ValidateCurrent().StartsWith("PASS"),"stage geometry, ground contact and camera framing: "+stage);
            var camera=FindFirstObjectByType<CampaignObservationCamera>();camera.SetMode(CampaignObservationMode.Specimen);
            camera.ApplyOrbitInput(new Vector2(80,15),-1);yield return new WaitForSecondsRealtime(.7f);
            Check(CampaignLifeStageVisualValidation.ValidateCurrent().StartsWith("PASS"),"close observation remains framed after orbit and zoom: "+stage);
            camera.SetMode(CampaignObservationMode.Environment);camera.ResetView();
        }
        app.Select(larvaId);app.Go(CampaignPage.Market);yield return Page(CampaignPage.Market);
        yield return ThemeDropdownAnimation();
        typeof(CampaignView).GetField("marketCategory",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(View,2);InvokeView("QueueRefresh");yield return null;yield return null;
        Check(CampaignRearingItems.All.All(id=>FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Any(t=>t.text==CampaignRearingItems.Name(id))),"market displays all six functional stage supplies");
        yield return Capture("03-StageMarket");
        long coins=app.Session.State.coins;
        InvokeView("OpenMarketSupply",CampaignRearingItems.FungusBottle,false);yield return null;FindFirstObjectByType<TMP_InputField>().text="2";Click("确认购买");yield return Idle();
        Check(app.Session.State.coins==coins-2*CampaignRearingItems.Price(CampaignRearingItems.FungusBottle),"stage supplies purchase UI charges exact quantity");
        string mailId=app.Session.State.mail.Last(m=>m.Pending).id;InvokeView("OpenMailbox");InvokeView("SelectMail",mailId);yield return Idle();Click("领取全部附件");yield return Idle();
        Check(app.Session.InventoryCount(CampaignRearingItems.FungusBottle)==2,"stage supplies arrive through the mailbox");
        InvokeView("CloseModal");app.Go(CampaignPage.Greenhouse);yield return Page(CampaignPage.Greenhouse);Click("使用养殖用品");yield return Idle();
        Check(app.Session.InventoryCount(CampaignRearingItems.FungusBottle)==1&&app.Session.Beetle(larvaId).substrateNutrition==100&&app.Session.Beetle(larvaId).growthQualityBonus==.05,"stage care UI consumes inventory and applies fungus nutrition bonus");
        app.Go(CampaignPage.Market);yield return Page(CampaignPage.Market);coins=app.Session.State.coins;long sell=app.Session.SupplySellPrice(CampaignRearingItems.FungusBottle);
        InvokeView("OpenMarketSupply",CampaignRearingItems.FungusBottle,true);yield return null;Click("确认出售");yield return Idle();
        Check(app.Session.InventoryCount(CampaignRearingItems.FungusBottle)==0&&app.Session.State.coins==coins+sell,"stage supply resale removes stock and pays discounted value");
        Check(app.SaveSlot(1),"calendar and lifecycle saved");double savedDay=app.Session.State.clock.elapsedDays,loadedDay=-1;
        Action observeLoad=()=>{if(loadedDay<0)loadedDay=app.Session.State.clock.elapsedDays;};app.Changed+=observeLoad;
        try{app.LoadSlot(1);yield return Page(CampaignPage.Greenhouse);}
        finally{app.Changed-=observeLoad;}
        Check(Math.Abs(loadedDay-savedDay)<1e-9&&!app.Session.State.clock.paused&&app.Session.State.clock.realSecondsPerDay==720&&app.Session.State.clock.speed==32&&app.Session.State.lifeRules.larva3Days==200,"reload publishes the saved date without offline aging and retains pace and legacy rules with natural time enabled");
        app.Execute(s=>{var egg=s.Beetle(eggId);egg.dead=true;egg.hearts=0;s.PreserveDead(eggId,"第 2 年 01 月 01 日");});yield return Idle();
        app.Go(CampaignPage.SpecimenRoom);yield return Page(CampaignPage.SpecimenRoom);string specimen=app.Session.State.specimens.Last().id;
        app.Execute(s=>s.PlaceSpecimen(specimen,5));yield return Idle();
        Check(FindObjectsByType<CampaignLifeStageDisplay>(FindObjectsSortMode.None).Any(d=>d.CurrentStage==CampaignLifeStage.Egg&&d.SpecimenRoot&&d.SpecimenRoot.name=="CarePreservedAnatomy"&&d.ObservationBounds.size.sqrMagnitude>.1f),"an immature specimen retains its actual stage shape");
        yield return Capture("04-ImmatureSpecimen");
        Check(errors.Count==0,"no runtime errors or exceptions");
    }
    void Finish(Exception failure)
    {
        Application.logMessageReceived-=Log;
        File.AppendAllText(Output+"/Result.txt",(failure==null?"COMPLETE PASS":"FAILED "+failure)+"\n"+string.Join("\n",errors));EditorApplication.isPlaying=false;
    }
}
