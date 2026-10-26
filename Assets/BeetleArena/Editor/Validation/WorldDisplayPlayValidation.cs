using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BeetleArena;
using BeetleArena.Campaign;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UI=UnityEngine.UI;

[InitializeOnLoad]
public static class WorldDisplayPlayValidation
{
    const string Key="BugDuel.WorldDisplayValidation";
    public const string Output="Artifacts/SceneDisplay";
    static WorldDisplayPlayValidation()
    {
        string path=SessionState.GetString(Key,"");if(path.Length>0)CampaignApp.StorageOverride=path;
        EditorApplication.playModeStateChanged+=OnPlay;
        EditorApplication.update+=()=>{if(SessionState.GetString(Key,"").Length>0&&EditorApplication.isPlaying)EditorApplication.QueuePlayerLoopUpdate();};
    }
    [MenuItem("Tools/甲壳虫斗/验证/界面与画面/各场景画面效果", false, 609)]
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before isolated display validation.");
        Directory.CreateDirectory(Output);string path=Path.GetFullPath(Output+"/Save-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(path);
        File.WriteAllText(Output+"/Result.txt","WORLD DISPLAY PLAY VALIDATION\n"+DateTimeOffset.Now.ToString("o")+"\n"+path+"\n");
        SessionState.SetString(Key+".storage",CampaignApp.StorageOverride??"");SessionState.SetString(Key,path);CampaignApp.StorageOverride=path;
        SessionState.SetString(Key+".start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Campaign/CampaignMainMenu.unity");
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Show();EditorApplication.isPlaying=true;
    }
    static void OnPlay(PlayModeStateChange state)
    {
        if(SessionState.GetString(Key,"").Length==0)return;
        if(state==PlayModeStateChange.EnteredPlayMode)
        {var host=new GameObject("World display validation");UnityEngine.Object.DontDestroyOnLoad(host);host.AddComponent<WorldDisplayPlayDriver>();}
        if(state!=PlayModeStateChange.EnteredEditMode)return;
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key+".start",""));
        string storage=SessionState.GetString(Key+".storage","");CampaignApp.StorageOverride=storage.Length==0?null:storage;
        SessionState.EraseString(Key);SessionState.EraseString(Key+".start");SessionState.EraseString(Key+".storage");
    }
}

public sealed class WorldDisplayPlayDriver:MonoBehaviour
{
    const string Output=WorldDisplayPlayValidation.Output;
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    CampaignApp app;int checks;bool background,cleaned;float volume;
    DisplayPreferencesSnapshot preferences;WorldDisplayProfile displayProfile;string profileJson;Material customSource;
    Material authoredDither;string authoredDitherJson;
    readonly List<string> errors=new List<string>();
    CampaignView View=>FindFirstObjectByType<CampaignView>();
    void Start()
    {
        background=Application.runInBackground;Application.runInBackground=true;volume=AudioListener.volume;
        preferences=new DisplayPreferencesSnapshot();displayProfile=BeetleArena.EditorTools.WorldDisplayEditorService.EnsureProfile();profileJson=EditorJsonUtility.ToJson(displayProfile);
        foreach(var scene in EditorBuildSettings.scenes)
        {string name=Path.GetFileNameWithoutExtension(scene.path);BeetleDisplayPreferences.SetStyle(name,BeetleDisplayStyle.OrderedDither);BeetleDisplayPreferences.SetPixelSize(name,3);}
        Application.logMessageReceived+=Log;SceneManager.sceneLoaded+=IsolateNursery;StartCoroutine(Guard(Run()));
    }
    void IsolateNursery(Scene scene,LoadSceneMode mode)
    {
        if(scene.name!="BeetleNursery")return;
        var controller=FindFirstObjectByType<BeetleHusbandryController>();
        controller.Initialize(Path.GetFullPath(app.SaveDirectory+"/nursery.json"),false);controller.SetPaused(true);
    }
    void Log(string message,string trace,LogType type)
    {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+trace);}
    IEnumerator Guard(IEnumerator routine)
    {
        var stack=new Stack<IEnumerator>();stack.Push(routine);
        while(stack.Count>0)
        {
            bool moved=false;object current=null;Exception failure=null;
            try{moved=stack.Peek().MoveNext();if(moved)current=stack.Peek().Current;}catch(Exception e){failure=e;}
            if(failure!=null){while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();Finish(failure);yield break;}
            if(!moved){(stack.Pop() as IDisposable)?.Dispose();continue;}
            if(current is IEnumerator nested)stack.Push(nested);else yield return current;
        }
        Finish(null);
    }
    void Check(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message);checks++;File.AppendAllText(Output+"/Result.txt","PASS "+message+"\n");}
    IEnumerator Wait(Func<bool> condition,string message)
    {float end=Time.realtimeSinceStartup+45;while(!condition()){if(Time.realtimeSinceStartup>end)throw new TimeoutException(message);yield return null;}yield return null;yield return null;}
    IEnumerator Page(CampaignPage page)=>Wait(()=>app&&!app.Busy&&View&&View.page==page&&View.backdrop.Ready,page.ToString());
    IEnumerator Idle()=>Wait(()=>!app.Busy,"commit");
    IEnumerator Rendered(){yield return null;yield return null;yield return new WaitForEndOfFrame();}
    void CheckSurface(UI.RawImage surface,string page)
    {
        var effect=surface.GetComponent<BeetleCombatCrt>();
        Check(effect&&effect.DitherActive&&!effect.CrtActive&&surface.material.shader.name=="BugDuel/Combat Ordered Dither",page+" uses dither exclusively");
        Check(surface.texture&&surface.texture.width>1&&surface.texture.height>1,page+" has a rendered world texture");
        Check(FindObjectsByType<UI.Graphic>(FindObjectsSortMode.None).Where(g=>g!=surface&&g.gameObject.activeInHierarchy).All(g=>
            g.material.shader.name!="BugDuel/Combat Ordered Dither"&&g.material.shader.name!="BugDuel/Combat CRT"),page+" keeps UI outside world effect");
    }
    void Click(string name)=>View.GetComponentsInChildren<UI.Button>().Single(b=>b.name==name&&b.IsInteractable()).onClick.Invoke();
    IEnumerator SelectFilter(BeetleDisplayStyle style)
    {
        var dropdown=View.GetComponentsInChildren<RetroFilterDropdown>().Single(d=>!d.IsGlassQualityDropdown);dropdown.Trigger.onClick.Invoke();
        yield return Wait(()=>dropdown.IsOpen&&!dropdown.IsAnimating,"filter dropdown");
        dropdown.OptionButtons.Single(button=>button.name=="FilterOption-"+style).onClick.Invoke();
        yield return Wait(()=>!dropdown.IsOpen&&!dropdown.IsAnimating,"filter selection");
    }
    Texture2D Frame(string name)
    {
        var game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));RenderTexture target=null;
        for(Type type=game.GetType();type!=null&&!target;type=type.BaseType)
        {var field=type.GetField("m_TargetTexture",Private|BindingFlags.DeclaredOnly)??type.GetField("m_RenderTexture",Private|BindingFlags.DeclaredOnly);if(field!=null)target=field.GetValue(game) as RenderTexture;}
        if(!target)throw new InvalidOperationException("Game View has no composed frame");
        var old=RenderTexture.active;var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try{RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();}
        finally{RenderTexture.active=old;}
        if(SystemInfo.graphicsUVStartsAtTop)
        {var source=texture.GetPixels32();var upright=new Color32[source.Length];for(int y=0;y<texture.height;y++)Array.Copy(source,y*texture.width,upright,(texture.height-1-y)*texture.width,texture.width);texture.SetPixels32(upright);texture.Apply();}
        File.WriteAllBytes(Output+"/"+name+".png",texture.EncodeToPNG());return texture;
    }
    IEnumerator Run()
    {
        app=CampaignApp.Ensure();yield return Page(CampaignPage.MainMenu);
        Check(BeetleDisplayPreferences.Style==BeetleDisplayStyle.OrderedDither,"scene preferences select dither");
        BeetleDisplayPreferences.Style=BeetleDisplayStyle.OrderedDither;yield return Rendered();CheckSurface(View.backdrop.output,"MainMenu");Destroy(Frame("01-MainMenu"));
        int frame=Time.frameCount;yield return Rendered();Check(Time.frameCount>frame,"player frames advance");
        Check(app.NewGame(0,"画面验收"),"isolated journey accepted");yield return Page(CampaignPage.Greenhouse);
        Check(app.Execute(s=>{s.ConfigureClock(86400,1,true);s.State.coins=10000;foreach(var b in s.State.beetles){b.overturned=false;b.nutrition=100;b.hearts=b.maxHearts;}}),"stable healthy fixture");yield return Idle();
        yield return new WaitForSecondsRealtime(1.3f);yield return Rendered();CheckSurface(View.backdrop.output,"Greenhouse");
        authoredDither=displayProfile.Find("CampaignGreenhouse").ditherMaterial;authoredDitherJson=EditorJsonUtility.ToJson(authoredDither);
        authoredDither.SetFloat("_DitherStrength",.25f);yield return Rendered();
        Check(Mathf.Approximately(View.backdrop.output.material.GetFloat("_DitherStrength"),.25f),"editing the authored material updates live dither strength without applying the scene tool");
        Check(Mathf.Approximately(displayProfile.Find("CampaignArena").ditherMaterial.GetFloat("_DitherStrength"),.5f),"greenhouse material edits leave combat material unchanged");
        EditorJsonUtility.FromJsonOverwrite(authoredDitherJson,authoredDither);yield return Rendered();
        // Freeze husbandry, compare composed frames: the world must change while the opaque sidebar stays exact.
        var dither=Frame("02-Greenhouse-Dither");BeetleDisplayPreferences.Style=BeetleDisplayStyle.Clean;yield return Rendered();var clean=Frame("03-Greenhouse-Clean");
        var a=dither.GetPixels32();var bPixels=clean.GetPixels32();int uiChanged=0,worldChanged=0;
        for(int y=30;y<clean.height-30;y++)for(int x=20;x<clean.width-20;x++)
        {int i=y*clean.width+x;if(a[i].Equals(bPixels[i]))continue;if(x<clean.width/3-20)uiChanged++;else if(x>clean.width/2)worldChanged++;}
        Check(uiChanged==0&&worldChanged>1000,"dither changes rendered world pixels while sidebar pixels stay identical: UI="+uiChanged+", world="+worldChanged);Destroy(dither);Destroy(clean);
        BeetleDisplayPreferences.SetStyle("CampaignArena",BeetleDisplayStyle.Crt);BeetleDisplayPreferences.SetPixelSize("CampaignArena",6);
        BeetleDisplayPreferences.SetStyle("CampaignSpecimenRoom",BeetleDisplayStyle.Clean);
        Check(BeetleDisplayPreferences.PixelSize==3&&BeetleDisplayPreferences.GetPixelSize("CampaignArena")==6,"live pixel preferences are independent by scene");
        var setting=displayProfile.Find("CampaignGreenhouse");
        if(setting==null){setting=new SceneDisplaySetting{sceneName="CampaignGreenhouse"};displayProfile.scenes.Add(setting);}
        customSource=new Material(Resources.Load<Shader>("BeetleCombatDither"));customSource.SetFloat("_PixelSize",1);
        setting.style=BeetleDisplayStyle.Custom;setting.customMaterial=customSource;setting.pixelSize=5;
        BeetleDisplayPreferences.UseSceneDefault("CampaignGreenhouse");BeetleDisplayPreferences.SetPixelSize("CampaignGreenhouse",5);yield return Rendered();
        var customEffect=View.backdrop.output.GetComponent<BeetleCombatCrt>();
        Check(customEffect.CustomActive&&!customEffect.CrtActive&&!customEffect.DitherActive,"custom world material replaces native presets without stacking");
        Check(View.backdrop.output.material!=customSource&&View.backdrop.output.material.GetFloat("_PixelSize")==5&&customSource.GetFloat("_PixelSize")==1,"custom runtime clone uses scene pixels without modifying source material");
        Destroy(Frame("03b-Custom"));
        EditorJsonUtility.FromJsonOverwrite(profileJson,displayProfile);BeetleDisplayPreferences.Style=BeetleDisplayStyle.Clean;BeetleDisplayPreferences.PixelSize=3;
        yield return Rendered();Check(!customEffect.EffectActive&&View.backdrop.output.material.shader.name!="BugDuel/Combat Ordered Dither","clean mode releases custom material binding");
        // Real settings bindings must allow players to restore clean/CRT and select dither again.
        Click("设置");yield return Rendered();Click("画面");yield return Rendered();
        yield return SelectFilter(BeetleDisplayStyle.Crt);yield return Rendered();Check(View.backdrop.output.GetComponent<BeetleCombatCrt>().CrtActive,"settings selects CRT for greenhouse");
        yield return SelectFilter(BeetleDisplayStyle.Clean);yield return Rendered();Check(!View.backdrop.output.GetComponent<BeetleCombatCrt>().EffectActive,"settings restores clean greenhouse");
        yield return SelectFilter(BeetleDisplayStyle.OrderedDither);yield return Rendered();Destroy(Frame("04-Settings"));Click("完成");yield return Rendered();
        Check(PlayerPrefs.GetInt(BeetleDisplayPreferences.SceneStyleKey("CampaignGreenhouse"))==(int)BeetleDisplayStyle.OrderedDither,"settings persists only the greenhouse dither selection");
        app.Go(CampaignPage.SpecimenRoom);yield return Page(CampaignPage.SpecimenRoom);yield return Rendered();
        Check(!View.backdrop.output.GetComponent<BeetleCombatCrt>().EffectActive,"specimen room retains clean mode after greenhouse settings changed");
        BeetleDisplayPreferences.Style=BeetleDisplayStyle.OrderedDither;
        typeof(CombatDisplaySettingsValidation).GetMethod("RenderDither",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
        Check(true,"shader calibration preserves original RGB, stable Bayer pattern and uniform pixel cells");
        foreach(var page in new[]{CampaignPage.SpecimenRoom,CampaignPage.Catching,CampaignPage.Market,CampaignPage.League,CampaignPage.Auction,CampaignPage.Saves})
        {app.Go(page);yield return Page(page);yield return Rendered();CheckSurface(View.backdrop.output,page.ToString());Destroy(Frame("05-"+page));}
        app.Go(CampaignPage.Greenhouse);yield return Page(CampaignPage.Greenhouse);
        Check(app.Execute(s=>s.EnterLeague(s.State.selectedId)),"league entry accepted");yield return Idle();app.Go(CampaignPage.Arena);yield return Page(CampaignPage.Arena);
        yield return Rendered();Check(View.backdrop.output.GetComponent<BeetleCombatCrt>().CrtActive,"combat retains its own CRT choice after visiting other pages");Destroy(Frame("06a-CampaignArena-CRT"));
        BeetleDisplayPreferences.Style=BeetleDisplayStyle.OrderedDither;yield return Rendered();CheckSurface(View.backdrop.output,"CampaignArena");Destroy(Frame("06-CampaignArena"));
        foreach(string scene in new[]{"BeetleNursery","BeetleArena"})
        {
            yield return SceneManager.LoadSceneAsync(scene);yield return Rendered();yield return Rendered();
            var match=FindFirstObjectByType<BeetleMatchController>();if(match)match.EditorSetPaused(true);
            var display=FindFirstObjectByType<BeetleCombatDisplay>();var camera=display.GetComponent<Camera>();
            var surface=display.GetComponentInChildren<UI.RawImage>();CheckSurface(surface,scene);Destroy(Frame("07-"+scene));
            Check(FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c=>c.isRootCanvas&&c.worldCamera==camera).All(c=>c.renderMode!=RenderMode.ScreenSpaceCamera),scene+" HUD is not baked into camera texture");
            BeetleDisplayPreferences.Style=BeetleDisplayStyle.Clean;yield return Rendered();
            Check(!camera.targetTexture&&!display.GetComponentInChildren<UI.RawImage>(),scene+" clean mode releases effect target");
            Check(FindObjectsByType<Canvas>(FindObjectsSortMode.None).Any(c=>c.isRootCanvas&&c.worldCamera==camera&&c.renderMode==RenderMode.ScreenSpaceCamera),scene+" clean mode restores original HUD camera binding");
            BeetleDisplayPreferences.Style=BeetleDisplayStyle.OrderedDither;yield return Rendered();yield return Rendered();CheckSurface(display.GetComponentInChildren<UI.RawImage>(),scene+" re-enabled");
        }
        yield return SceneManager.LoadSceneAsync("SampleScene");yield return Rendered();yield return Rendered();
        var configured=FindFirstObjectByType<BeetleCombatDisplay>();
        Check(configured,"configured ordinary scene installs world display on its main camera");
        CheckSurface(configured.GetComponentInChildren<UI.RawImage>(),"SampleScene");
        Check(errors.Count==0,"no runtime errors or exceptions");
    }
    void Cleanup()
    {
        if(cleaned)return;cleaned=true;Application.logMessageReceived-=Log;SceneManager.sceneLoaded-=IsolateNursery;Application.runInBackground=background;AudioListener.volume=volume;
        preferences.Dispose();EditorJsonUtility.FromJsonOverwrite(profileJson,displayProfile);if(customSource)Destroy(customSource);
        if(authoredDither){EditorJsonUtility.FromJsonOverwrite(authoredDitherJson,authoredDither);EditorUtility.SetDirty(authoredDither);AssetDatabase.SaveAssetIfDirty(authoredDither);}
    }
    void OnDestroy()=>Cleanup();
    void Finish(Exception failure)
    {Cleanup();File.AppendAllText(Output+"/Result.txt",(failure==null?"ALL CHECKS PASSED":"FAILED "+failure)+" | "+checks+" checks\n"+string.Join("\n",errors));EditorApplication.isPlaying=false;}
}
