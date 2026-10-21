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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// A short presentation smoke check. All economic transactions use an isolated test save.
[InitializeOnLoad]
public static class RetroPresentationValidation
{
    public static void RunBilingual(){SessionState.SetBool("BugDuel.BilingualValidation",true);try{Run();}catch{SessionState.EraseBool("BugDuel.BilingualValidation");throw;}}
    const string Key="BugDuel.RetroValidation",Season="BugDuel.Retro.Season",Dark="BugDuel.Retro.Dark";
    public const string Output="Artifacts/RetroUi";
    [Serializable] sealed class SceneRecord { public string path;public bool loaded,active; }
    [Serializable] sealed class Previous
    {
        public SceneRecord[] scenes;
        public string startScene,storage;
        public bool background,seasonExists,darkExists,localeMenu;
        public int season,dark;
    }
    static RetroPresentationValidation()
    {
        string folder=SessionState.GetString(Key,"");
        if(folder.Length>0)CampaignApp.StorageOverride=folder;
        EditorApplication.playModeStateChanged+=OnPlay;
    }
    [MenuItem("Tools/Beetle Arena/Validate Retro Presentation")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before presentation validation.");
        for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save open scenes before validation.");
        var previous=new Previous
        {
            scenes=EditorSceneManager.GetSceneManagerSetup().Select(s=>new SceneRecord {path=s.path,loaded=s.isLoaded,active=s.isActive}).ToArray(),
            startScene=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),storage=CampaignApp.StorageOverride,
            background=Application.runInBackground,seasonExists=PlayerPrefs.HasKey(Season),darkExists=PlayerPrefs.HasKey(Dark),
            season=PlayerPrefs.GetInt(Season),dark=PlayerPrefs.GetInt(Dark),localeMenu=UnityEditor.Localization.LocalizationEditorSettings.ShowLocaleMenuInGameView
        };
        SessionState.SetString(Key+".Previous",JsonUtility.ToJson(previous));
        // The package's debug overlay accesses EditorStyles before OnGUI during automated Play.
        // Exercise the game's real language selector; restore the author's overlay preference afterwards.
        UnityEditor.Localization.LocalizationEditorSettings.ShowLocaleMenuInGameView=false;
        Directory.CreateDirectory(Output);
        string folder=Path.GetFullPath(Output+"/TestSave/"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(folder);SessionState.SetString(Key,folder);CampaignApp.StorageOverride=folder;
        CampaignRetroTheme.SetAppearance(0,false);
        File.WriteAllText(Output+"/Result.txt","RUNNING\nIsolated saves: "+folder+"\n");
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Campaign/CampaignMainMenu.unity");
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Show();EditorApplication.isPlaying=true;
    }
    static void OnPlay(PlayModeStateChange state)
    {
        if(SessionState.GetString(Key,"").Length==0)return;
        if(state==PlayModeStateChange.EnteredPlayMode)
        {
            var go=new GameObject("Retro presentation validation");UnityEngine.Object.DontDestroyOnLoad(go);go.AddComponent<RetroPresentationDriver>();
        }
        if(state!=PlayModeStateChange.EnteredEditMode)return;
        var previous=JsonUtility.FromJson<Previous>(SessionState.GetString(Key+".Previous","{}"));
        SessionState.EraseString(Key);SessionState.EraseString(Key+".Previous");
        SessionState.EraseBool("BugDuel.BilingualValidation");
        SessionState.EraseBool("BugDuel.MailboxValidation");
        CampaignApp.StorageOverride=string.IsNullOrEmpty(previous.storage)?null:previous.storage;
        Application.runInBackground=previous.background;
        UnityEditor.Localization.LocalizationEditorSettings.ShowLocaleMenuInGameView=previous.localeMenu;
        if(previous.seasonExists)PlayerPrefs.SetInt(Season,previous.season);else PlayerPrefs.DeleteKey(Season);
        if(previous.darkExists)PlayerPrefs.SetInt(Dark,previous.dark);else PlayerPrefs.DeleteKey(Dark);
        PlayerPrefs.Save();
        EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(previous.startScene)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(previous.startScene);
        if(previous.scenes!=null&&previous.scenes.Length>0)
        {
            if(previous.scenes.All(s=>string.IsNullOrEmpty(s.path)))EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            else EditorSceneManager.RestoreSceneManagerSetup(previous.scenes.Select(s=>new SceneSetup {path=s.path,isLoaded=s.loaded,isActive=s.active}).ToArray());
        }
        File.AppendAllText(Output+"/Result.txt","RESTORED editor scenes, start scene, storage override, PlayerPrefs appearance and background preference.\n");
    }
}

public sealed partial class RetroPresentationDriver : MonoBehaviour
{
    const string Output=RetroPresentationValidation.Output;
    CampaignApp app;
    Mouse mouse,previousMouse;
    InputSettings originalInput,testInput;
    bool background,cleaned,finished;
    float began;
    readonly HashSet<string> errors=new HashSet<string>();
    void Start()
    {
        began=Time.realtimeSinceStartup;background=Application.runInBackground;Application.runInBackground=true;
        originalInput=InputSystem.settings;testInput=Instantiate(originalInput);InputSystem.settings=testInput;
        testInput.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        testInput.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        previousMouse=Mouse.current;mouse=InputSystem.AddDevice<Mouse>("RetroValidationMouse");
        Application.logMessageReceived+=Log;StartCoroutine(Guard(SessionState.GetBool("BugDuel.MailboxValidation",false)?RunMailbox():SessionState.GetBool("BugDuel.BilingualValidation",false)?RunBilingual():Run()));
    }
    void Log(string message,string stack,LogType type)
    {
        if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert||message.Contains("character with Unicode value"))errors.Add(message+"\n"+stack);
    }
    IEnumerator Guard(IEnumerator routine)
    {
        var stack=new Stack<IEnumerator>();stack.Push(routine);
        while(stack.Count>0)
        {
            object current=null;bool moved=false;Exception failure=null;
            try
            {
                if(Time.realtimeSinceStartup-began>60)throw new TimeoutException("Presentation validation exceeded 60 seconds.");
                moved=stack.Peek().MoveNext();if(moved)current=stack.Peek().Current;
            }
            catch(Exception error){failure=error;}
            if(failure!=null){Finish(failure);yield break;}
            if(!moved){(stack.Pop() as IDisposable)?.Dispose();continue;}
            if(current is IEnumerator nested)stack.Push(nested);else yield return current;
        }
        Finish(null);
    }
    void Check(bool ok,string message)
    {
        if(!ok)throw new InvalidOperationException(message+"; app="+app?.Message);
        File.AppendAllText(Output+"/Result.txt","PASS "+message+"\n");
    }
    IEnumerator Wait(Func<bool> condition,string message)
    {
        float deadline=Time.realtimeSinceStartup+15;
        while(!condition()){if(Time.realtimeSinceStartup>deadline)throw new TimeoutException(message+"; "+app?.Message);yield return null;}
        yield return null;yield return null;
    }
    IEnumerator Page(CampaignPage page)=>Wait(()=>app&&!app.Busy&&SceneManager.GetActiveScene().name==CampaignApp.SceneName(page),"Page "+page);
    IEnumerator Idle()=>Wait(()=>app&&!app.Busy,"Transaction");
    IEnumerator Pause(float seconds){float end=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<end)yield return null;}
    IEnumerator MouseFrame(Vector2 position,bool right=false,Vector2 delta=default,Vector2 scroll=default)
    {
        var state=new MouseState {position=position,delta=delta,scroll=scroll};
        if(right)state=state.WithButton(MouseButton.Right);
        InputSystem.QueueStateEvent(mouse,state);yield return null;yield return null;
    }
    IEnumerator Run()
    {
        yield return null;app=CampaignApp.Ensure();
        Check(app.SaveDirectory.StartsWith(Path.GetFullPath(Output+"/TestSave"),StringComparison.OrdinalIgnoreCase),"storage isolated from player saves");
        yield return Page(CampaignPage.MainMenu);yield return Capture("01-MainMenu");
        app.NewGame(0);yield return Page(CampaignPage.Greenhouse);
        var backdrop=FindFirstObjectByType<CampaignBackdrop>();var camera=FindFirstObjectByType<CampaignObservationCamera>();
        Check(camera&&backdrop&&backdrop.Frame,"greenhouse observation camera and render target exist");
        yield return Pause(.5f);CheckViewport(backdrop,camera);yield return Capture("02-GreenhouseOverview");
        app.CaptureThumbnail();Check(app.SaveSlot(0),"observation thumbnail can be saved");yield return Idle();
        var thumbnail=Convert.FromBase64String(app.ReadSlot(0).thumbnail);File.WriteAllBytes(Output+"/02-Thumbnail.jpg",thumbnail);
        var thumb=new Texture2D(2,2);thumb.LoadImage(thumbnail);
        Check(thumb.width==320&&thumb.height==180&&thumb.GetPixel(2,90).grayscale<.02f&&thumb.GetPixel(160,90).grayscale>.01f,"save thumbnail fits observation aspect with black side bars");Destroy(thumb);
        yield return CheckMouseGates(camera);
        foreach(float pitch in new[]{-100000f,100000f})foreach(float zoom in new[]{-1000f,1000f})
        {
            camera.ApplyOrbitInput(new Vector2(650,pitch),zoom);yield return Pause(.15f);
            Check(camera.Elevation>=CampaignObservationCamera.MinimumElevation&&camera.Elevation<=CampaignObservationCamera.MaximumElevation,"orbit elevation limit: "+camera.Elevation);
            var corners=new Vector3[4];backdrop.sceneCamera.CalculateFrustumCorners(new Rect(0,0,1,1),backdrop.sceneCamera.nearClipPlane,Camera.MonoOrStereoscopicEye.Mono,corners);
            Check(corners.All(p=>backdrop.sceneCamera.transform.TransformPoint(p).y>camera.GroundHeight),"camera near plane stays above soil under extreme zoom/orbit");
        }
        camera.ResetView();camera.SetMode(CampaignObservationMode.Specimen);yield return Pause(.7f);
        Check(camera.Mode==CampaignObservationMode.Specimen,"selected specimen observation mode");yield return Capture("03-GreenhouseSpecimen");
        app.Go(CampaignPage.Market);yield return Page(CampaignPage.Market);
        Check(FindObjectsByType<Button>(FindObjectsSortMode.None).Count(b=>b.isActiveAndEnabled&&b.IsInteractable())>=6,"market has interactive controls");
        var artwork=FindObjectsByType<CampaignMarketArtwork>(FindObjectsSortMode.None);
        Check(artwork.Length>0&&artwork.All(a=>a.canvasRenderer&&a.GetComponent<CanvasRenderer>()),"market artwork owns CanvasRenderer components");
        yield return Capture("04-MarketLight");
        var stock=app.Session.State.stock.OrderBy(b=>app.Session.Asking(b)).First();string stockId=stock.id;
        long coins=app.Session.State.coins,cost=CampaignEconomy.Money(app.Session.Asking(stock)*1.1);int count=app.Session.State.beetles.Count;
        Check(app.Execute(s=>s.Buy(stockId)),"market accepts a real purchase");yield return Idle();
        Check(app.Session.State.coins==coins-cost&&app.Session.State.beetles.Count==count&&app.Session.State.stock.All(b=>b.id!=stockId)&&app.Session.State.mail.Last().Pending,"purchase commits gold, stock and mail delivery atomically");
        CampaignRetroTheme.SetMode(true);FindFirstObjectByType<CampaignView>().SendMessage("QueueRefresh");yield return null;yield return Capture("05-MarketDark");
        CampaignRetroTheme.SetMode(false);
        var view=FindFirstObjectByType<CampaignView>();
        typeof(CampaignView).GetField("marketTab",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,1);
        view.SendMessage("QueueRefresh");yield return null;yield return Capture("05b-MarketBBS");
        view.SendMessage("OpenMarketHelp");yield return Capture("05c-MarketHelp");view.SendMessage("CloseModal");
        string entrant=app.Session.State.beetles.First(b=>b.Adult(app.Session.State.week)&&!b.dead&&!b.overturned&&b.hearts>=3&&b.nutrition>=40).id;
        Check(app.Execute(s=>s.EnterLeague(entrant)),"valid league reservation created");yield return Idle();
        app.Go(CampaignPage.Arena);yield return Page(CampaignPage.Arena);yield return Pause(.2f);
        backdrop=FindFirstObjectByType<CampaignBackdrop>();var crt=backdrop.output.GetComponent<BeetleCombatCrt>();
        Check(crt&&crt.EffectActive&&backdrop.output.material&&backdrop.output.material.shader.name=="BugDuel/Combat CRT","combat world uses active CRT shader");
        Check(!backdrop.output.raycastTarget&&FindFirstObjectByType<CampaignLeagueBridge>().controller.Simulation!=null,"CRT leaves UI raycasts and real combat simulation intact");
        yield return Capture("06-ArenaCrt");
        SceneManager.LoadScene("BeetleArena");
        yield return Wait(()=>SceneManager.GetActiveScene().name=="BeetleArena"&&FindFirstObjectByType<BeetleCombatDisplay>(),"standalone combat display");
        yield return Pause(.3f);
        var display=FindFirstObjectByType<BeetleCombatDisplay>();
        var standalone=FindObjectsByType<BeetleCombatCrt>(FindObjectsSortMode.None).FirstOrDefault(c=>c.EffectActive);
        Check(display&&standalone&&standalone.output.texture,"standalone combat CRT installed");
        var hud=FindFirstObjectByType<BeetleArenaHud>();
        Check(hud&&hud.GetComponentInParent<Canvas>().renderMode==RenderMode.ScreenSpaceOverlay,"standalone HUD stays outside CRT world capture");
        yield return Capture("07-StandaloneCrt");
        var original=display.profile;var temporary=Instantiate(original);display.profile=temporary;
        try
        {
            temporary.effectEnabled=false;yield return null;yield return null;
            Check(hud.GetComponentInParent<Canvas>().renderMode==RenderMode.ScreenSpaceCamera&&!display.GetComponent<Camera>().targetTexture,"disabling CRT restores standalone HUD camera and releases world target");
            temporary.effectEnabled=true;yield return null;yield return null;
            Check(hud.GetComponentInParent<Canvas>().renderMode==RenderMode.ScreenSpaceOverlay&&display.GetComponent<Camera>().targetTexture,"re-enabling CRT restores isolated display");
        }
        finally { if(display)display.profile=original;Destroy(temporary); }
        Check(errors.Count==0,"no runtime errors, exceptions or missing glyph warnings");
    }
    void CheckViewport(CampaignBackdrop backdrop,CampaignObservationCamera observation)
    {
        Canvas.ForceUpdateCanvases();var corners=new Vector3[4];backdrop.output.rectTransform.GetWorldCorners(corners);
        float left=corners[0].x,right=corners[2].x,height=corners[2].y-corners[0].y;
        Check(Mathf.Abs(left-Screen.width/3f)<2&&Mathf.Abs(right-Screen.width)<2,"world occupies actual right two thirds of screen");
        Check(Mathf.Abs((float)backdrop.Frame.width/backdrop.Frame.height-(right-left)/height)<.005f,"world RT preserves actual viewport aspect");
        Check(!observation.PointerOverWorld(new Vector2(Screen.width/6f,Screen.height/2f))&&observation.PointerOverWorld(new Vector2(Screen.width*2f/3f,Screen.height/2f)),"observation hit rectangle excludes left controls");
    }
    IEnumerator CheckMouseGates(CampaignObservationCamera camera)
    {
        if(!Application.isFocused)
        {
            File.AppendAllText(Output+"/Result.txt","SKIP physical mouse replay: Game View application focus unavailable; rectangle and orbit limit checks still run.\n");
            yield break;
        }
        yield return Pause(.5f);float elevation=camera.Elevation,distance=camera.Distance;
        Vector2 left=new Vector2(Screen.width/6f,Screen.height/2f),world=new Vector2(Screen.width*2f/3f,Screen.height/2f);
        yield return MouseFrame(left,true);yield return MouseFrame(left,true,new Vector2(0,300),new Vector2(0,120));yield return MouseFrame(left);
        Check(Mathf.Abs(elevation-camera.Elevation)<.001f&&Mathf.Abs(distance-camera.Distance)<.03f,"physical mouse over left UI does not orbit or zoom world");
        yield return MouseFrame(world,true);yield return MouseFrame(world,true,new Vector2(0,180));yield return MouseFrame(world);
        Check(Mathf.Abs(elevation-camera.Elevation)>1,"right mouse drag over world rotates camera");
        app.Modal=true;elevation=camera.Elevation;distance=camera.Distance;
        yield return MouseFrame(world,true);yield return MouseFrame(world,true,new Vector2(0,-300),new Vector2(0,120));yield return MouseFrame(world);
        Check(Mathf.Abs(elevation-camera.Elevation)<.001f&&Mathf.Abs(distance-camera.Distance)<.03f,"modal blocks physical camera input");app.Modal=false;
    }
    IEnumerator Capture(string name)
    {
        var game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));game.Focus();game.Repaint();
        yield return null;yield return new WaitForEndOfFrame();RenderTexture target=null;
        for(Type type=game.GetType();type!=null&&!target;type=type.BaseType)
        {
            var field=type.GetField("m_TargetTexture",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)??type.GetField("m_RenderTexture",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
            if(field!=null)target=field.GetValue(game) as RenderTexture;
        }
        if(!target)throw new InvalidOperationException("Game View composed target is missing.");
        var previous=RenderTexture.active;Texture2D texture=null;
        try
        {
            RenderTexture.active=target;texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();var pixels=texture.GetPixels32();
            Check(pixels.Where((p,i)=>i%41==0).Select(p=>(int)p.r+(p.g<<8)+(p.b<<16)).Distinct().Take(33).Count()>32,"composed frame contains scene and UI: "+name);
            if(SystemInfo.graphicsUVStartsAtTop)
            {
                var upright=new Color32[pixels.Length];for(int y=0;y<target.height;y++)Array.Copy(pixels,y*target.width,upright,(target.height-1-y)*target.width,target.width);
                texture.SetPixels32(upright);texture.Apply();
            }
            File.WriteAllBytes(Output+"/"+name+".png",texture.EncodeToPNG());
            File.WriteAllLines(Output+"/"+name+"-TextClipping.txt",FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Where(t=>t.isTextTruncated).Select(t=>t.text.Replace("\n"," / ")+" | "+t.rectTransform.rect));
        }
        finally{RenderTexture.active=previous;if(texture)Destroy(texture);}
    }
    void Cleanup()
    {
        if(cleaned)return;cleaned=true;Application.logMessageReceived-=Log;
        if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);if(previousMouse!=null&&previousMouse.added)previousMouse.MakeCurrent();
        if(originalInput)InputSystem.settings=originalInput;if(testInput)Destroy(testInput);Application.runInBackground=background;
        if(app)app.Modal=false;
    }
    void Finish(Exception failure)
    {
        if(finished)return;finished=true;Cleanup();
        File.WriteAllLines(Output+"/Errors.txt",errors);File.AppendAllText(Output+"/Result.txt",(failure==null?"COMPLETE PASS":"FAILED "+failure)+"\nElapsed: "+(Time.realtimeSinceStartup-began).ToString("0.00")+" seconds\n");
        EditorApplication.isPlaying=false;
    }
    void OnDestroy(){Cleanup();}
}
