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
        public float phaseTime,closing,time;
        public bool busy,opening,holding;
        public bool cover,shutters,blocks;
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
        Check(Mathf.Approximately(transition.Profile.CloseSecondsFor(CampaignTransitionMode.BlackFade),transition.Profile.CloseSeconds*3)&&Mathf.Approximately(transition.Profile.OpenSecondsFor(CampaignTransitionMode.BlackFade),transition.Profile.OpenSeconds*3),"black fade takes three times the base close and open durations");
        Check(Mathf.Approximately(transition.Profile.BlackHoldSeconds,2.06f),"default full black hold adds two seconds to the previous 0.06 seconds");
        Check(Enumerable.Range(0,101).All(i=>transition.Profile.ShutterOpacity(i/100f,0)==1&&transition.Profile.ShutterOpacity(i/100f,1)==0),"soft shutters have exact opaque and transparent endpoints including the center seam and outer edges");
        Check(JsonUtility.FromJson<CampaignPreferences>("{\"language\":0}").transitionMode==CampaignTransitionMode.Television,"existing settings retain the TV transition");
        yield return Record("01-Menu-Saves",()=>app.Go(CampaignPage.Saves),CampaignPage.Saves);
        yield return Record("02-Saves-Greenhouse",()=>app.NewGame(0,"验收玩家"),CampaignPage.Greenhouse);
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
        yield return SameSceneShutters();
        Check(JsonUtility.ToJson(authored)==authoredSnapshot,"previewing custom curves leaves the authored asset unchanged");
        if(errors.Count>0)throw new InvalidOperationException(string.Join("\n",errors));
        if(Verify&&(totalLeaks>0||emptyTransitions>0||largestStep>.15f))throw new InvalidOperationException("Unexpected uncovered frames="+totalLeaks+", transitions without a full black frame="+emptyTransitions+", largest opening step="+largestStep);
    }
    void Check(bool condition,string message)
    {
        if(!condition)throw new InvalidOperationException(message);
        File.AppendAllText(Output+"/Result.txt","PASS "+message+"\n");
    }
    IEnumerator Record(string name,Action begin,CampaignPage target,bool quadratic=false,CampaignTransitionMode? explicitMode=null,bool whiteProbe=false)
    {
        var frames=new List<Frame>();float timeout=Time.realtimeSinceStartup+35;bool started=false;int settled=0;
        string source=SceneManager.GetActiveScene().name;
        var expectedMode=explicitMode??(source==CampaignApp.SceneName(CampaignPage.MainMenu)||source==CampaignApp.SceneName(CampaignPage.Saves)?CampaignTransitionMode.BlackFade:transition.Mode);
        begin();
        while(settled<3)
        {
            yield return new WaitForEndOfFrame();
            var frame=ReadFrame();frames.Add(frame);started|=transition.Busy;
            if(started&&!app.Busy&&SceneManager.GetActiveScene().name==CampaignApp.SceneName(target))settled++;else settled=0;
            if(Time.realtimeSinceStartup>timeout)throw new TimeoutException(name);
        }
        int leaks=frames.Count(f=>f.leaks>0),black=frames.Count(f=>f.holding&&f.alpha>=.999f&&f.leaks==0);
        float step=0,previous=0;
        foreach(var f in frames.Where(f=>f.opening)) { step=Mathf.Max(step,f.progress-previous);previous=f.progress; }
        largestStep=Mathf.Max(largestStep,step);totalLeaks+=leaks;totalFrames+=frames.Count;if(black==0)emptyTransitions++;
        var csv=new StringBuilder("frame,scene,busy,opening,alpha,progress,delta,mean,leakedPixels,coveredPixels,overlayOrder,pageOrder,mode,phaseTime,holding,closeProgress,realtime\n");
        foreach(var f in frames)csv.AppendLine(string.Join(",",f.number,f.scene,f.busy,f.opening,F(f.alpha),F(f.progress),F(f.delta),F(f.mean),f.leaks,f.covered,f.order,f.pageOrder,f.mode,F(f.phaseTime),f.holding,F(f.closing),F(f.time)));
        File.WriteAllText(Output+"/"+name+".csv",csv.ToString());
        File.AppendAllText(Output+"/Result.txt",name+": frames="+frames.Count+", leakedFrames="+leaks+", blackFrames="+black+", maxOpeningStep="+F(step)+"\n");
        // Write only after the transition, so PNG compression and file I/O cannot hitch the animation.
        for(int i=0;i<frames.Count;i++)
        {
            reader.SetPixels32(frames[i].pixels);reader.Apply();File.WriteAllBytes(Output+"/"+name+"-"+i.ToString("D3")+".png",reader.EncodeToPNG());
        }
        var opening=frames.Where(f=>f.opening).ToArray();
        var closing=frames.Where(f=>f.busy&&!f.opening&&!f.holding).ToArray();
        var holding=frames.Where(f=>f.holding).ToArray();
        Check(opening.All(f=>f.mode==expectedMode),name+": correct transition for the source page");
        Check(opening.Length>4,name+": opening spans multiple rendered frames");
        Check(closing.Length>3&&holding.Length>0,name+": both closing and full black hold are presented");
        float tolerance=Mathf.Max(.08f,frames.Max(f=>f.delta));
        Check(holding[0].time-closing[0].time+tolerance>=transition.Profile.CloseSecondsFor(expectedMode)&&frames.First(f=>!f.busy&&f.time>opening[0].time).time-opening[0].time+tolerance>=transition.Profile.OpenSecondsFor(expectedMode),name+": rendered closing and opening respect the mode durations");
        Check(opening[0].time-holding[0].time+tolerance>=transition.Profile.BlackHoldSeconds&&holding.All(f=>f.cover&&!f.shutters&&f.alpha==1&&f.leaks==0),name+": full black hold uses an opaque cover for the configured duration without light leaks");
        Check(frames.Where(f=>f.busy).All(f=>f.blocks)&&!frames.Last().blocks&&frames.Last().alpha==0&&!frames.Last().shutters,name+": input is blocked during transition and no curtain remains afterward");
        if(opening[0].mode==CampaignTransitionMode.BlackFade)
        {
            Check(opening.All(f=>f.cover&&!f.shutters&&Mathf.Abs(f.alpha-(1-f.progress))<.001f),name+": full-screen fade, no TV shutters");
            Check(closing.All(f=>f.cover&&!f.shutters&&Mathf.Abs(f.alpha-f.closing)<.001f),name+": black fade closes with opacity");
            Check(opening.Count(f=>f.alpha>.2f&&f.alpha<.8f&&f.mean>.001f&&f.mean<frames.Last().mean*.97f)>=3,name+": intermediate fade brightness is visible");
        }
        else Check(opening.Concat(closing).All(f=>!f.cover&&f.shutters&&f.alpha==1),name+": TV opens and closes with the same soft shutters");
        Check(closing.Zip(closing.Skip(1),(a,b)=>b.closing>=a.closing).All(x=>x),name+": closing never reverses");
        Check(opening.Zip(opening.Skip(1),(a,b)=>b.progress>=a.progress).All(x=>x),name+": opening never reverses");
        if(quadratic)
            Check(frames.Where(f=>f.busy).All(f=>Mathf.Abs((f.opening?f.progress:f.closing)-f.phaseTime*f.phaseTime)<.001f),name+": rendered animation uses authored quadratic curves");
        if(whiteProbe)
        {
            int softRows=0;
            foreach(var frame in opening.Concat(closing).Where(f=>(f.opening?f.progress:1-f.closing)>.3f&&(f.opening?f.progress:1-f.closing)<.7f))
            {
                int rows=Enumerable.Range(0,Height).Count(y=>{var p=frame.pixels[y*Width+Width/2];return p.r>12&&p.r<243&&Math.Abs(p.r-p.g)<3&&Math.Abs(p.r-p.b)<3;});
                softRows=Math.Max(softRows,rows);
            }
            Check(transition.Profile.ShutterSoftness>0?softRows>12:softRows<=4,name+": inspector softness changes the rendered edge width (intermediate rows="+softRows+")");
            Check(frames.Last().mean>.98f,name+": shutter completion fully reveals the white probe including screen edges");
        }
    }
    IEnumerator SameSceneShutters()
    {
        var probe=new GameObject("Transition white probe",typeof(Canvas));var canvas=probe.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.overrideSorting=true;canvas.sortingOrder=900;
        var image=new GameObject("White",typeof(RectTransform),typeof(UnityEngine.UI.Image));image.transform.SetParent(probe.transform,false);
        var rect=(RectTransform)image.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
        image.GetComponent<UnityEngine.UI.Image>().color=Color.white;Canvas.ForceUpdateCanvases();yield return null;
        int calls=0;bool calledUnderCover=false;float previousSoftness=transition.Profile.shutterSoftness;
        foreach(float softness in new[]{.08f,0})
        {
            transition.Profile.shutterSoftness=softness;
            yield return Record(softness>0?"15-SameScene-SoftShutters":"16-SameScene-SharpShutters",()=>
            {
                bool started=transition.Play(()=>{calls++;calledUnderCover=transition.Busy&&transition.Holding&&!transition.Opening;},error=>throw new InvalidOperationException(error),CampaignTransitionMode.Television);
                Check(started&&transition.Busy&&!transition.Play(()=>calls++,error=>{}),"same-scene transition starts immediately and rejects a second concurrent request");
            },CampaignPage.Greenhouse,true,CampaignTransitionMode.Television,true);
        }
        Check(calls==2&&calledUnderCover,"same-scene changes execute exactly once at full black");
        transition.Profile.shutterSoftness=previousSoftness;Destroy(probe);yield return null;
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
        var group=overlay.GetComponentInChildren<CanvasGroup>();
        var f=new Frame { number=Time.frameCount,scene=SceneManager.GetActiveScene().name,busy=transition.Busy,opening=transition.Opening,holding=transition.Holding,closing=transition.CloseProgress,time=Time.realtimeSinceStartup,alpha=group.alpha,blocks=group.blocksRaycasts,progress=transition.OpenProgress,phaseTime=transition.PhaseTime,mode=transition.ActiveMode,delta=Time.unscaledDeltaTime,pixels=pixels,order=overlay.renderOrder,pageOrder=page?page.renderOrder:-1,
            cover=overlay.transform.Find("Fade/Cover").gameObject.activeSelf,shutters=overlay.transform.Find("Fade/UpperShutter").gameObject.activeSelf||overlay.transform.Find("Fade/LowerShutter").gameObject.activeSelf };
        for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
        {
            var p=pixels[y*Width+x];f.mean+=(p.r+p.g+p.b)/(3f*255*Width*Height);
            float screenY=(y+.5f)/Height;
            // Downsampling can mix a boundary pixel; test the nearer-to-center sample too.
            float inwardY=Mathf.MoveTowards(screenY,.5f,2f/Height);
            bool covered=f.busy&&f.alpha>=.999f&&(f.cover||transition.Profile.ShutterOpacity(inwardY,f.opening?f.progress:1-f.closing)>=.99999f);
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
