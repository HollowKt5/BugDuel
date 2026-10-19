using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BeetleArena.Campaign;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Capture every presented frame, including scene activation, rather than only one opening screenshot.
[InitializeOnLoad]
public static class CampaignTransitionValidation
{
    const string Key="BugDuel.TransitionAcceptance";
    static CampaignTransitionValidation()
    {
        string output=SessionState.GetString(Key,"");
        if(output.Length>0)CampaignApp.StorageOverride=Path.Combine(output,"Saves");
        EditorApplication.playModeStateChanged+=OnPlay;
    }
    [MenuItem("Tools/Beetle Arena/Validate Campaign Transition Frames")]
    public static void Validate() => Run(true);
    public static void Run(bool verify)
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop play before transition acceptance.");
        for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save scenes before transition acceptance.");
        string output=Path.GetFullPath("Artifacts/Campaign/Transitions/"+(verify?"After-":"Before-")+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(output);SessionState.SetString(Key,output);SessionState.SetBool(Key+"Verify",verify);
        CampaignApp.StorageOverride=Path.Combine(output,"Saves");
        File.WriteAllText("Artifacts/Campaign/Transitions/Latest.txt",output);
        File.WriteAllText(output+"/Result.txt","RUNNING\n");
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Campaign/CampaignMainMenu.unity");
        var window=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));window.Show();window.Focus();
        EditorApplication.isPlaying=true;
    }
    static void OnPlay(PlayModeStateChange state)
    {
        string output=SessionState.GetString(Key,"");if(output.Length==0)return;
        if(state==PlayModeStateChange.EnteredPlayMode)
        {
            var go=new GameObject("Transition frame acceptance");UnityEngine.Object.DontDestroyOnLoad(go);
            var driver=go.AddComponent<CampaignTransitionFrameDriver>();driver.Output=output;driver.Verify=SessionState.GetBool(Key+"Verify",true);
        }
        if(state==PlayModeStateChange.EnteredEditMode) { SessionState.EraseString(Key);CampaignApp.StorageOverride=null; }
    }
}

public sealed class CampaignTransitionFrameDriver : MonoBehaviour
{
    public string Output;
    public bool Verify;
    CampaignApp app;
    CampaignSceneTransition transition;
    EditorWindow game;
    RenderTexture sample;
    Texture2D reader;
    readonly List<string> errors=new List<string>();
    int totalLeaks,totalFrames,emptyTransitions;
    float largestStep;
    const int Width=480,Height=270;
    sealed class Frame
    {
        public int number,leaks,covered,order,pageOrder;
        public float alpha,progress,delta,mean;
        public float phaseTime;
        public bool busy,opening;
        public bool cover,shutters;
        public CampaignTransitionMode mode;
        public string scene;
        public Color32[] pixels;
    }
    void Start()
    {
        Application.runInBackground=true;
        game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));game.Focus();
        sample=new RenderTexture(Width,Height,0,RenderTextureFormat.ARGB32);
        reader=new Texture2D(Width,Height,TextureFormat.RGB24,false);
        Application.logMessageReceived+=Log;StartCoroutine(Guard(Run()));
    }
    void Log(string message,string stack,LogType type)
    { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+stack); }
    IEnumerator Guard(IEnumerator routine)
    {
        var stack=new Stack<IEnumerator>();stack.Push(routine);
        while(stack.Count>0)
        {
            object current=null;bool moved=false;Exception failure=null;
            try { moved=stack.Peek().MoveNext();if(moved)current=stack.Peek().Current; }
            catch(Exception e) { failure=e; }
            if(failure!=null) { Finish(failure);yield break; }
            if(!moved) { (stack.Pop() as IDisposable)?.Dispose();continue; }
            if(current is IEnumerator nested)stack.Push(nested);else yield return current;
        }
        Finish(null);
    }
    IEnumerator Run()
    {
        yield return null;yield return null;app=CampaignApp.Ensure();transition=app.GetComponent<CampaignSceneTransition>();
        var authored=Resources.Load<CampaignTransitionProfile>(CampaignTransitionProfile.ResourceName);
        Check(authored&&transition.Profile!=authored,"authored curves loaded into an isolated runtime copy");
        string authoredSnapshot=JsonUtility.ToJson(authored);
        Check(JsonUtility.FromJson<CampaignPreferences>("{\"language\":0}").transitionMode==CampaignTransitionMode.Television,"existing settings retain the TV transition");
        yield return Record("01-Menu-Saves",()=>app.Go(CampaignPage.Saves),CampaignPage.Saves);
        yield return Record("02-Saves-Greenhouse",()=>app.NewGame(0),CampaignPage.Greenhouse);
        yield return Record("03-Greenhouse-Market",()=>app.Go(CampaignPage.Market),CampaignPage.Market);
        yield return Record("04-Market-Saves",()=>app.Go(CampaignPage.Saves),CampaignPage.Saves);
        yield return Record("05-Saves-Menu",()=>app.Go(CampaignPage.MainMenu),CampaignPage.MainMenu);
        yield return Record("06-Menu-Saves-Warm",()=>app.Go(CampaignPage.Saves),CampaignPage.Saves);
        app.Preferences.transitionMode=CampaignTransitionMode.BlackFade;app.SavePreferences();
        var saved=JsonUtility.FromJson<CampaignPreferences>(File.ReadAllText(Path.Combine(app.SaveDirectory,"settings.json")));
        Check(saved.transitionMode==CampaignTransitionMode.BlackFade&&transition.Mode==CampaignTransitionMode.BlackFade,"black fade preference persists and applies");
        yield return Record("07-Fade-Saves-Menu",()=>app.Go(CampaignPage.MainMenu),CampaignPage.MainMenu);
        yield return Record("08-Fade-Menu-Saves",()=>app.Go(CampaignPage.Saves),CampaignPage.Saves);
        yield return Record("09-Fade-Saves-Greenhouse",()=>app.LoadSlot(0),CampaignPage.Greenhouse);
        yield return Record("10-Fade-Greenhouse-Market",()=>app.Go(CampaignPage.Market),CampaignPage.Market);
        yield return Record("11-Fade-Market-Saves",()=>app.Go(CampaignPage.Saves),CampaignPage.Saves);
        yield return Record("12-Fade-Saves-Greenhouse-Reload",()=>app.LoadSlot(0),CampaignPage.Greenhouse);
        transition.Profile.closeCurve=new AnimationCurve(new Keyframe(0,0,0,0),new Keyframe(1,1,2,2));
        transition.Profile.openCurve=new AnimationCurve(new Keyframe(0,0,0,0),new Keyframe(1,1,2,2));
        transition.Profile.closeDuration=.5f;transition.Profile.openDuration=.7f;
        app.Preferences.transitionMode=CampaignTransitionMode.Television;app.ApplyPreferences();
        yield return Record("13-CustomCurve-TV",()=>app.Go(CampaignPage.Market),CampaignPage.Market,true);
        app.Preferences.transitionMode=CampaignTransitionMode.BlackFade;app.ApplyPreferences();
        yield return Record("14-CustomCurve-Fade",()=>app.Go(CampaignPage.Greenhouse),CampaignPage.Greenhouse,true);
        Check(JsonUtility.ToJson(authored)==authoredSnapshot,"previewing custom curves leaves the authored asset unchanged");
        if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));
        if(Verify&&(totalLeaks>0||emptyTransitions>0||largestStep>.15f))throw new InvalidOperationException("Unexpected uncovered frames="+totalLeaks+", transitions without a full black frame="+emptyTransitions+", largest opening step="+largestStep);
    }
    void Check(bool condition,string message)
    {
        if(!condition)throw new InvalidOperationException(message);
        File.AppendAllText(Output+"/Result.txt","PASS "+message+"\n");
    }
    IEnumerator Record(string name,Action begin,CampaignPage target,bool quadratic=false)
    {
        var frames=new List<Frame>();float timeout=Time.realtimeSinceStartup+35;bool started=false;int settled=0;
        string source=SceneManager.GetActiveScene().name;
        var expectedMode=source==CampaignApp.SceneName(CampaignPage.MainMenu)||source==CampaignApp.SceneName(CampaignPage.Saves)?CampaignTransitionMode.BlackFade:transition.Mode;
        begin();
        while(settled<3)
        {
            yield return new WaitForEndOfFrame();
            var frame=ReadFrame();frames.Add(frame);started|=transition.Busy;
            if(started&&!app.Busy&&SceneManager.GetActiveScene().name==CampaignApp.SceneName(target))settled++;else settled=0;
            if(Time.realtimeSinceStartup>timeout)throw new TimeoutException(name);
        }
        int leaks=frames.Count(f=>f.leaks>0),black=frames.Count(f=>f.busy&&!f.opening&&f.alpha>=.999f&&f.leaks==0);
        float step=0,previous=0;
        foreach(var f in frames.Where(f=>f.opening)) { step=Mathf.Max(step,f.progress-previous);previous=f.progress; }
        largestStep=Mathf.Max(largestStep,step);totalLeaks+=leaks;totalFrames+=frames.Count;if(black==0)emptyTransitions++;
        var csv=new StringBuilder("frame,scene,busy,opening,alpha,progress,delta,mean,leakedPixels,coveredPixels,overlayOrder,pageOrder,mode,phaseTime\n");
        foreach(var f in frames)csv.AppendLine(string.Join(",",f.number,f.scene,f.busy,f.opening,F(f.alpha),F(f.progress),F(f.delta),F(f.mean),f.leaks,f.covered,f.order,f.pageOrder,f.mode,F(f.phaseTime)));
        File.WriteAllText(Output+"/"+name+".csv",csv.ToString());
        File.AppendAllText(Output+"/Result.txt",name+": frames="+frames.Count+", leakedFrames="+leaks+", blackFrames="+black+", maxOpeningStep="+F(step)+"\n");
        // Write only after the transition, so PNG compression and file I/O cannot hitch the animation.
        for(int i=0;i<frames.Count;i++)
        {
            reader.SetPixels32(frames[i].pixels);reader.Apply();File.WriteAllBytes(Output+"/"+name+"-"+i.ToString("D3")+".png",reader.EncodeToPNG());
        }
        var opening=frames.Where(f=>f.opening).ToArray();
        Check(opening.All(f=>f.mode==expectedMode),name+": correct transition for the source page");
        Check(opening.Length>4,name+": opening spans multiple rendered frames");
        if(opening[0].mode==CampaignTransitionMode.BlackFade)
        {
            Check(opening.All(f=>f.cover&&!f.shutters&&Mathf.Abs(f.alpha-(1-f.progress))<.001f),name+": full-screen fade, no TV shutters");
            Check(opening.Count(f=>f.alpha>.2f&&f.alpha<.8f&&f.mean>.001f&&f.mean<frames.Last().mean*.97f)>=3,name+": intermediate fade brightness is visible");
        }
        else Check(opening.All(f=>!f.cover&&f.shutters&&f.alpha==1),name+": opaque TV shutters follow reveal progress");
        Check(opening.Zip(opening.Skip(1),(a,b)=>b.progress>=a.progress).All(x=>x),name+": opening never reverses");
        if(quadratic)
            Check(frames.Where(f=>f.busy).All(f=>Mathf.Abs((f.opening?f.progress:f.alpha)-f.phaseTime*f.phaseTime)<.001f),name+": rendered animation uses authored quadratic curves");
    }
    static string F(float value) => value.ToString("0.000000",CultureInfo.InvariantCulture);
    Frame ReadFrame()
    {
        RenderTexture target=null;
        for(Type type=game.GetType();type!=null&&!target;type=type.BaseType)
        {
            var field=type.GetField("m_TargetTexture",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)??type.GetField("m_RenderTexture",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
            if(field!=null)target=field.GetValue(game) as RenderTexture;
        }
        if(!target)throw new InvalidOperationException("Game View has no render target");
        var active=RenderTexture.active;
        try { Graphics.Blit(target,sample);RenderTexture.active=sample;reader.ReadPixels(new Rect(0,0,Width,Height),0,0);reader.Apply(); }
        finally { RenderTexture.active=active; }
        var pixels=reader.GetPixels32();
        if(SystemInfo.graphicsUVStartsAtTop)
        {
            var upright=new Color32[pixels.Length];for(int y=0;y<Height;y++)Array.Copy(pixels,y*Width,upright,(Height-1-y)*Width,Width);pixels=upright;
        }
        var overlay=FindObjectsByType<Canvas>(FindObjectsSortMode.None).First(c=>c.name=="SceneFade");
        var page=FindObjectsByType<Canvas>(FindObjectsSortMode.None).FirstOrDefault(c=>c.name=="CampaignCanvas");
        var f=new Frame { number=Time.frameCount,scene=SceneManager.GetActiveScene().name,busy=transition.Busy,opening=transition.Opening,alpha=overlay.GetComponentInChildren<CanvasGroup>().alpha,progress=transition.OpenProgress,phaseTime=transition.PhaseTime,mode=transition.ActiveMode,delta=Time.unscaledDeltaTime,pixels=pixels,order=overlay.renderOrder,pageOrder=page?page.renderOrder:-1,
            cover=overlay.transform.Find("Fade/Cover").gameObject.activeSelf,shutters=overlay.transform.Find("Fade/UpperShutter").gameObject.activeSelf||overlay.transform.Find("Fade/LowerShutter").gameObject.activeSelf };
        for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
        {
            var p=pixels[y*Width+x];f.mean+=(p.r+p.g+p.b)/(3f*255*Width*Height);
            bool covered=f.busy&&f.alpha>=.999f&&(!f.opening||f.mode==CampaignTransitionMode.BlackFade||Mathf.Abs((y+.5f)/Height-.5f)>f.progress*.5f+.015f);
            if(covered) { f.covered++;if(p.r>4||p.g>4||p.b>4)f.leaks++; }
        }
        return f;
    }
    void Finish(Exception failure)
    {
        Application.logMessageReceived-=Log;
        File.AppendAllText(Output+"/Result.txt","Total frames="+totalFrames+", leaked frames="+totalLeaks+", largest opening step="+F(largestStep)+"\n"+(failure==null?(Verify?"COMPLETE PASS":"BASELINE CAPTURED"):"FAILED "+failure)+"\n");
        if(sample) { sample.Release();Destroy(sample); }if(reader)Destroy(reader);
        EditorApplication.isPlaying=false;
    }
}
