using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BeetleArena;
using BeetleArena.Campaign;
using BugDuel.Campaign;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SpecimenLocalePlayValidation
{
    const string Key="BugDuel.SpecimenLocaleValidation";
    public const string Output="Artifacts/SpecimenLocale";
    public static bool FollowUp=>SessionState.GetBool(Key+".followup",false);
    public static string Report=>Output+(FollowUp?"/FollowUp.txt":"/Result.txt");
    public static void RunTextFollowUp(){SessionState.SetBool(Key+".followup",true);Run();}
    static SpecimenLocalePlayValidation()
    {
        string path=SessionState.GetString(Key,"");if(path.Length>0)CampaignApp.StorageOverride=path;
        EditorApplication.playModeStateChanged+=OnPlay;
        EditorApplication.update+=()=>{if(SessionState.GetString(Key,"").Length>0&&EditorApplication.isPlaying)EditorApplication.QueuePlayerLoopUpdate();};
    }
    [MenuItem("Tools/甲壳虫斗/验证/本地化/标本室与语言切换", false, 643)]
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before isolated validation.");
        Directory.CreateDirectory(Output);
        string path=Path.GetFullPath(Output+"/Save-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        var session=CampaignSession.New(CampaignCatalog.Defaults(),76192);session.RenamePlayer("Atlas");session.State.coins=100000;
        for(int i=0;i<3;i++)
        {
            var species=session.Catalog.species[i];var beetle=session.Create(species.id,species.length,2);
            beetle.dead=true;beetle.hearts=0;session.State.beetles.Add(beetle);
            string id=session.PreserveDead(beetle.id,"第 1 年 01 月 01 日");session.PlaceSpecimen(id,new[]{0,5,11}[i]);
        }
        foreach(var beetle in session.State.beetles){beetle.overturned=false;beetle.nutrition=100;beetle.hearts=beetle.maxHearts;}
        session.Validate();new CampaignSaveRepository(path).Write(0,new CampaignSave {state=session.State,catalog=session.Catalog});
        File.WriteAllText(Report,"SPECIMEN ROOM AND LANGUAGE REVIEW\n"+DateTimeOffset.Now.ToString("o")+"\n");
        SessionState.SetString(Key+".storage",CampaignApp.StorageOverride??"");SessionState.SetString(Key,path);CampaignApp.StorageOverride=path;
        SessionState.SetString(Key+".start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Campaign/CampaignMainMenu.unity");
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Show();EditorApplication.isPlaying=true;
    }
    static void OnPlay(PlayModeStateChange state)
    {
        if(SessionState.GetString(Key,"").Length==0)return;
        if(state==PlayModeStateChange.EnteredPlayMode)
        {var host=new GameObject("Specimen language acceptance");UnityEngine.Object.DontDestroyOnLoad(host);host.AddComponent<SpecimenLocalePlayDriver>();}
        if(state!=PlayModeStateChange.EnteredEditMode)return;
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key+".start",""));
        string storage=SessionState.GetString(Key+".storage","");CampaignApp.StorageOverride=storage.Length==0?null:storage;
        SessionState.EraseString(Key);SessionState.EraseString(Key+".start");SessionState.EraseString(Key+".storage");
        SessionState.EraseBool(Key+".followup");
    }
}

public sealed class SpecimenLocalePlayDriver:MonoBehaviour
{
    const string Output=SpecimenLocalePlayValidation.Output;
    CampaignApp app;
    readonly List<string> errors=new List<string>();
    DisplayPreferencesSnapshot preferences;
    bool background,cleaned;float volume;int language,checks;
    CampaignView View=>FindFirstObjectByType<CampaignView>();
    void Start()
    {
        background=Application.runInBackground;Application.runInBackground=true;volume=AudioListener.volume;language=CampaignLocalization.English?1:0;
        preferences=new DisplayPreferencesSnapshot();Application.logMessageReceived+=Log;SceneManager.sceneLoaded+=PrepareStandalone;StartCoroutine(Guard(Run()));
    }
    void PrepareStandalone(Scene scene,LoadSceneMode mode)
    {
        if(scene.name!="BeetleNursery")return;
        var controller=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<BeetleHusbandryController>(true)).Single();
        // sceneLoaded runs before Start: the standalone controller also needs its own
        // save path because its persistence is independent of CampaignApp.StorageOverride.
        controller.Initialize(Path.Combine(CampaignApp.StorageOverride,"Standalone","husbandry-roster.json"),false);
        controller.simulateAccidents=false;
    }
    void Log(string message,string trace,LogType type)
    {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert||message.Contains("Unicode value"))errors.Add(message+"\n"+trace);}
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
    void Check(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message);checks++;File.AppendAllText(SpecimenLocalePlayValidation.Report,"PASS "+message+"\n");}
    IEnumerator Wait(Func<bool> condition,string message)
    {float until=Time.realtimeSinceStartup+40;while(!condition()){if(Time.realtimeSinceStartup>until)throw new TimeoutException(message+" | "+app?.Message);yield return null;}yield return null;yield return null;}
    IEnumerator Page(CampaignPage page)=>Wait(()=>app&&!app.Busy&&View&&View.page==page&&View.backdrop.Ready,page.ToString());
    void Invoke(string method)=>typeof(CampaignView).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic,null,Type.EmptyTypes,null).Invoke(View,null);
    void SetTab(string field,int value)=>typeof(CampaignView).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(View,value);
    IEnumerator Close(){Invoke("CloseModal");yield return new WaitForSecondsRealtime(.3f);}
    IEnumerator Run()
    {
        yield return Wait(()=>CampaignApp.Instance&&!CampaignApp.Instance.Busy,"startup");app=CampaignApp.Instance;
        app.LoadSlot(0);yield return Page(CampaignPage.Greenhouse);
        int frame=Time.frameCount;yield return new WaitForSecondsRealtime(.25f);Check(Time.frameCount>frame+2,"player loop advances");
        foreach(int locale in new[]{0,1})
        {
            CampaignLocalization.Select(locale);string suffix=locale==0?"ZH":"EN";
            var pages=SpecimenLocalePlayValidation.FollowUp?new[]{CampaignPage.Market}:new[]{CampaignPage.SpecimenRoom,CampaignPage.Greenhouse,CampaignPage.Market,CampaignPage.Catching,CampaignPage.League,CampaignPage.Auction,CampaignPage.Saves,CampaignPage.MainMenu};
            foreach(var page in pages)
            {
                app.Go(page);yield return Page(page);yield return Capture(page+"-"+suffix);
                if(page==CampaignPage.SpecimenRoom&&locale==0)
                {
                    var room=FindFirstObjectByType<CampaignSpecimenRoom>();
                    int poses=SpecimenRoomGeometryChecks.Check(room);Check(poses>100,"closed room and near plane coverage: "+poses+" assertions across 648 camera poses");
                    room.ResetView();yield return Capture("Room-Reset");
                    foreach(var pose in new[]{new Vector2(-38,26),new Vector2(38,-26)})
                    {
                        SetRoom(room,"yaw",pose.x);SetRoom(room,"pitch",pose.y);
                        SetRoom(room,"targetDistance",GetRoom<float>(room,"overviewDistance")*1.3f);
                        SetRoom(room,"targetFocus",new Vector3(pose.x<0?-5:5,pose.y>0?4:-3.6f,0));
                        yield return new WaitForSecondsRealtime(.8f);yield return Capture("Room-Extreme-"+(pose.x<0?"Left":"Right"));
                    }
                    room.ResetView();room.Focus(app.Session.State.specimens[0].id);yield return new WaitForSecondsRealtime(.8f);yield return Capture("Room-Close");room.ResetView();
                }
                if(page==CampaignPage.Greenhouse)
                {
                    Invoke("OpenMailbox");yield return Capture("Mailbox-"+suffix);yield return Close();
                    for(int tab=0;tab<5;tab++)
                    {
                        SetTab("settingsTab",tab);Invoke("OpenSettings");yield return Capture("Settings-"+tab+"-"+suffix);
                        if(tab==3)
                        {
                            var dropdown=FindObjectsByType<RetroFilterDropdown>(FindObjectsSortMode.None).Single(d=>d.isActiveAndEnabled&&!d.IsGlassQualityDropdown);
                            dropdown.Open();yield return Capture("Filters-"+suffix);dropdown.Close();yield return new WaitForSecondsRealtime(.2f);
                        }
                        yield return Close();
                    }
                }
                if(page==CampaignPage.Market)
                {
                    for(int tab=1;tab<5;tab++)
                    {SetTab("marketCategory",tab);Invoke("QueueRefresh");yield return Capture("Market-"+tab+"-"+suffix);}
                }
            }
        }
        // Standalone scenes are separate entry points, so dispose the campaign before loading them.
        Destroy(app.gameObject);yield return Wait(()=>!CampaignApp.Instance,"dispose campaign before standalone");app=null;
        foreach(string scene in new[]{"BeetleArena","BeetleNursery"})
        {
            SceneManager.LoadScene(scene);yield return new WaitForSecondsRealtime(1);
            foreach(int locale in new[]{0,1})
            {
                CampaignLocalization.Select(locale);string suffix=locale==0?"ZH":"EN";yield return Capture(scene+"-"+suffix);
                var settings=FindFirstObjectByType<BeetleFeedbackSettings>();
                if(settings)
                {
                    settings.Open();yield return Capture(scene+"-Settings-"+suffix);settings.Close();yield return new WaitForSecondsRealtime(.3f);
                }
            }
        }
        Check(errors.Count==0,"no runtime errors or missing glyph warnings");
    }
    static void SetRoom(CampaignSpecimenRoom room,string field,object value)=>typeof(CampaignSpecimenRoom).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(room,value);
    static T GetRoom<T>(CampaignSpecimenRoom room,string field)=>(T)typeof(CampaignSpecimenRoom).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(room);
    IEnumerator Capture(string name)
    {
        yield return new WaitForSecondsRealtime(.4f);yield return new WaitForEndOfFrame();
        var labels=FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Where(t=>t.isActiveAndEnabled).ToArray();
        File.WriteAllLines(Output+"/"+name+"-Text.txt",labels.OrderBy(t=>t.name).Select(t=>t.name+"\t"+t.text.Replace("\n"," / ")));
        File.WriteAllLines(Output+"/"+name+"-Overflow.txt",labels.Where(t=>t.isTextTruncated).Select(t=>t.name+"\t"+t.text.Replace("\n"," / ")));
        if(name.StartsWith("BeetleArena")||name.StartsWith("BeetleNursery"))
            Check(labels.All(t=>!t.isTextTruncated),"standalone UI text fits: "+name);
        // This fixture uses the deliberate player name Atlas. Physical Esc and email
        // addresses are identifiers; the remaining system text must use the chosen language.
        var mixed=labels.Select(t=>t.richText?Regex.Replace(t.text,"<[^>]*>",""):t.text).Where(value=>
        {
            if(CampaignLocalization.English)return Regex.IsMatch(value,@"[\u4e00-\u9fff]");
            value=Regex.Replace(value,@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+","").Replace("Atlas","").Replace("Esc","");
            return Regex.IsMatch(value,@"[A-Za-z]{2,}");
        }).ToArray();
        Check(mixed.Length==0,"no mixed system text: "+name+ (mixed.Length>0?" / "+string.Join(" | ",mixed):""));
        var game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));RenderTexture target=null;
        for(Type type=game.GetType();type!=null&&!target;type=type.BaseType)
        {var field=type.GetField("m_TargetTexture",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)??type.GetField("m_RenderTexture",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);if(field!=null)target=field.GetValue(game) as RenderTexture;}
        if(!target)throw new InvalidOperationException("No composed Game View frame");
        var previous=RenderTexture.active;var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try
        {
            RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();
            if(SystemInfo.graphicsUVStartsAtTop)
            {var source=texture.GetPixels32();var upright=new Color32[source.Length];for(int y=0;y<texture.height;y++)Array.Copy(source,y*texture.width,upright,(texture.height-1-y)*texture.width,texture.width);texture.SetPixels32(upright);texture.Apply();}
            File.WriteAllBytes(Output+"/"+name+".png",texture.EncodeToPNG());
        }
        finally{RenderTexture.active=previous;Destroy(texture);}
        Check(labels.Length>0,"composed localized view: "+name);
    }
    void Cleanup()
    {
        if(cleaned)return;cleaned=true;Application.logMessageReceived-=Log;SceneManager.sceneLoaded-=PrepareStandalone;preferences?.Dispose();
        Application.runInBackground=background;AudioListener.volume=volume;CampaignLocalization.Select(language);
    }
    void OnDestroy()=>Cleanup();
    void Finish(Exception failure)
    {Cleanup();File.AppendAllText(SpecimenLocalePlayValidation.Report,(failure==null?"COMPLETE PASS":"FAILED "+failure)+" | "+checks+" checks\n"+string.Join("\n",errors));EditorApplication.isPlaying=false;}
}
