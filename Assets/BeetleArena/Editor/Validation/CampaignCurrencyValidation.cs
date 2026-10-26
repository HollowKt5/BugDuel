using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BeetleArena.Campaign;
using BugDuel.Campaign;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class CampaignCurrencyValidation
{
    const string Key="BugDuel.CurrencyValidation";
    public const string Output="Artifacts/Currency/Play";
    static CampaignCurrencyValidation()
    {
        string path=SessionState.GetString(Key,"");if(path.Length>0)CampaignApp.StorageOverride=path;
        EditorApplication.playModeStateChanged+=OnPlay;
        EditorApplication.update+=()=>{if(SessionState.GetString(Key,"").Length>0&&EditorApplication.isPlaying)EditorApplication.QueuePlayerLoopUpdate();};
    }
    [MenuItem("Tools/甲壳虫斗/验证/界面与画面/金币反馈", false, 601)]
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before isolated currency validation.");
        Directory.CreateDirectory(Output);
        string path=Path.GetFullPath(Output+"/Save-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        SessionState.SetString(Key,path);CampaignApp.StorageOverride=path;
        File.WriteAllText(Output+"/Result.txt","RUNNING\nIsolated saves: "+path+"\n");
        SessionState.SetString(Key+".start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Campaign/CampaignMainMenu.unity");
        EditorApplication.isPlaying=true;
    }
    static void OnPlay(PlayModeStateChange state)
    {
        if(SessionState.GetString(Key,"").Length==0)return;
        if(state==PlayModeStateChange.EnteredPlayMode)
        {var go=new GameObject("Currency acceptance");UnityEngine.Object.DontDestroyOnLoad(go);go.AddComponent<CampaignCurrencyValidationDriver>();}
        if(state==PlayModeStateChange.EnteredEditMode)
        {
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key+".start",""));
            SessionState.EraseString(Key);SessionState.EraseString(Key+".start");CampaignApp.StorageOverride=null;
        }
    }
}

public sealed class CampaignCurrencyValidationDriver:MonoBehaviour
{
    CampaignApp app;
    CampaignCurrencyHud hud;
    CampaignCurrencyAudio audioFeedback;
    readonly List<CampaignCurrencyChange> changes=new List<CampaignCurrencyChange>();
    readonly List<string> errors=new List<string>();
    const string Output=CampaignCurrencyValidation.Output;
    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    void Start(){Application.runInBackground=true;Application.logMessageReceived+=Log;StartCoroutine(Guard(Run()));}
    void Log(string message,string trace,LogType type){if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)errors.Add(message+"\n"+trace);}
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
    void Check(bool valid,string message)
    {
        if(!valid)throw new InvalidOperationException(message+" | "+app?.Message);
        File.AppendAllText(Output+"/Result.txt","PASS "+message+"\n");
    }
    IEnumerator Wait(Func<bool> condition,string message)
    {
        float end=Time.realtimeSinceStartup+30;
        while(!condition()){if(Time.realtimeSinceStartup>end)throw new TimeoutException(message);yield return null;}
    }
    IEnumerator Idle()=>Wait(()=>app&&!app.Busy,"durable commit");
    void CommitNow()=>typeof(CampaignApp).GetMethod("FinishCommit",Private).Invoke(app,new object[]{false});
    CampaignCurrencyHud MakeHud()
    {
        var rect=new GameObject("Currency validation host",typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(transform,false);rect.sizeDelta=new Vector2(280,62);
        return CampaignCurrencyHud.Attach(rect,Resources.Load<TMP_FontAsset>("RetroUiFont"),Color.white,Color.green,Color.red);
    }
    IEnumerator Run()
    {
        yield return Wait(()=>CampaignApp.Instance&&!CampaignApp.Instance.Busy,"startup");app=CampaignApp.Instance;
        app.CurrencyChanged+=changes.Add;audioFeedback=CampaignCurrencyAudio.Ensure(app);hud=MakeHud();
        Check(app.Session==null&&hud.GetComponent<CanvasGroup>().alpha==0,"main menu without a session has no balance UI");
        app.NewGame(0,"验收玩家");yield return Wait(()=>!app.Busy&&SceneManager.GetActiveScene().name==CampaignApp.SceneName(CampaignPage.Greenhouse),"new game");
        Check(changes.Count==0&&audioFeedback.PlayedCount==0&&hud.TargetBalance==app.Session.State.coins,"new game sets a silent initial balance");
        Check(app.Execute(s=>s.ConfigureClock(600,1,true)),"clock pause transaction accepted");yield return Idle();
        Check(app.SaveSlot(2),"initial balance saved to an isolated slot");
        long initial=app.CurrencyBalance;
        Check(app.Execute(s=>s.Spend(100)),"spend transaction accepted");
        Check(changes.Count==0&&hud.TargetBalance==initial,"accepted transaction has no feedback before disk commit");yield return Idle();
        Check(changes.Count==1&&changes[0].Delta==-100&&audioFeedback.LastPitch<1,"committed spending emits one lower-pitched coin cue");
        Check(hud.TargetBalance==initial-100&&hud.BalanceTweenActive&&hud.ActiveFloatCount>0,"spending starts a numeric tween and an independent signed float");
        var same=CampaignCurrencyHud.Attach((RectTransform)hud.transform,Resources.Load<TMP_FontAsset>("RetroUiFont"),Color.white,Color.green,Color.red);
        Check(same==hud&&hud.ReceivedCount==1&&hud.BalanceTweenActive,"rebinding the same host preserves the active tween");
        long[] delta={100,250,-75,40,-20,15};
        foreach(long amount in delta)
        {
            Check(app.Execute(s=>{if(amount>0)s.Earn(amount);else s.Spend(-amount);}),"rapid transaction accepted: "+amount);yield return Idle();
        }
        long expected=initial-100;foreach(long amount in delta)expected+=amount;
        Check(changes.Count==7&&hud.ReceivedCount==7&&app.CurrencyBalance==expected&&hud.TargetBalance==expected,"rapid mixed transactions retain every event and the exact final balance");
        Check(hud.ReceivedNetDelta==expected-initial&&app.CurrencyNetChange==expected-initial&&audioFeedback.LastPitch>1,"net deltas match and incoming coins use the higher pitch");
        yield return Wait(()=>hud.PendingFloats==0&&hud.ActiveFloatCount==0&&!hud.BalanceTweenActive,"feedback completion");
        Check(hud.FloatsShown==7&&Math.Abs(hud.DisplayedBalance-expected)<.01,"each committed delta produced a float and the counter reaches its exact total");
        int played=audioFeedback.PlayedCount;float master=app.Preferences.master;app.Preferences.master=0;AudioListener.volume=0;
        app.Execute(s=>s.Spend(10));yield return Idle();
        Check(audioFeedback.PlayedCount==played&&audioFeedback.ReceivedCount==changes.Count,"muting suppresses sound without dropping currency events");
        app.Preferences.master=master;AudioListener.volume=master;
        Check(app.SaveSlot(1),"changed balance saved to a second isolated slot");long saved=app.CurrencyBalance;
        int count=changes.Count;played=audioFeedback.PlayedCount;
        app.LoadSlot(2);app.LoadSlot(1);yield return Idle();
        Check(app.CurrencyBalance==initial&&changes.Count==count&&audioFeedback.PlayedCount==played,"a rapid overlapping load is ignored and accepted load resets silently");
        app.LoadSlot(1);yield return Idle();
        Check(app.CurrencyBalance==saved&&changes.Count==count&&audioFeedback.PlayedCount==played&&hud.ReceivedNetDelta==0,"loading another balance does not turn the difference into income");
        Destroy(hud.gameObject);hud=MakeHud();
        Check(hud.TargetBalance==saved&&hud.ReceivedCount==0&&audioFeedback.PlayedCount==played,"a new page HUD binds silently without replaying old feedback");
        app.NewGame(3,"验收玩家");yield return Idle();
        Check(app.CurrencyBalance==initial&&changes.Count==count&&audioFeedback.PlayedCount==played,"a new journey silently replaces an existing balance");

        // Hold the exact temporary save file so failure occurs in the durable writer,
        // after domain validation, without touching any player-owned save directory.
        string temporary=app.SlotPath(0)+".tmp";long before=app.CurrencyBalance;
        using(var locked=new FileStream(temporary,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
        {
            Check(app.Execute(s=>s.Spend(100)),"write-failure transaction passed domain validation");yield return Idle();
            Check(app.Session.State.coins==before&&app.CurrencyBalance==before&&changes.Count==count&&audioFeedback.PlayedCount==played,"failed durable save leaves session, HUD and audio unchanged");
        }
        File.Delete(temporary);

        // Exercise the actual automatic-clock branch with one tick spanning many weeks.
        app.enabled=false;
        try
        {
            app.Execute(s=>
            {
                s.State.hall.Add(new HallRecord{name="Currency test",speciesId="red",length=48,price=1000,week=s.State.week});
                s.ConfigureClock(10,1000,false);
            });CommitNow();
            int week=app.Session.State.week;before=app.CurrencyBalance;
            typeof(CampaignApp).GetField("calendarClock",Private).SetValue(app,1f);
            typeof(CampaignApp).GetMethod("Update",Private).Invoke(app,null);
            Check(app.Busy&&changes.Count==count&&app.CurrencyBalance==before,"a natural multiweek tick waits for its durable commit");
            CommitNow();int weeks=app.Session.State.week-week;long payment=app.Session.Catalog.leagues[app.Session.Catalog.Tier(48).tier-1].win/10;
            Check(weeks>1&&changes.Count==count+1&&app.LastCurrencyChange.Delta==payment*weeks,"one multiweek tick emits its exact complete royalty delta once");
            app.Execute(s=>s.ConfigureClock(600,1,true));CommitNow();
        }
        finally{app.enabled=true;}
        Check(errors.Count==0,"no runtime errors or exceptions");
    }
    void Finish(Exception error)
    {
        Application.logMessageReceived-=Log;if(app){app.CurrencyChanged-=changes.Add;app.enabled=true;}
        File.AppendAllText(Output+"/Result.txt",(error==null?"COMPLETE PASS":"FAILED "+error)+"\n"+string.Join("\n",errors));EditorApplication.isPlaying=false;
    }
}
