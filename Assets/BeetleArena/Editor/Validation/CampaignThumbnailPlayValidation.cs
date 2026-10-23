using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BeetleArena.Campaign;
using BugDuel.Campaign;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UI=UnityEngine.UI;
using CampaignSpecimen=BeetleArena.Campaign.CampaignSpecimen;

// Exercises actual rendered images and the save-page RawImages, using isolated save files.
[InitializeOnLoad]
public static class CampaignThumbnailPlayValidation
{
    const string Key="BugDuel.ThumbnailValidation";
    public const string Output="Artifacts/SaveThumbnails";
    static CampaignThumbnailPlayValidation()
    {
        string path=SessionState.GetString(Key,"");if(path.Length>0)CampaignApp.StorageOverride=path;
        EditorApplication.playModeStateChanged+=OnPlay;
        EditorApplication.update+=()=>{if(SessionState.GetString(Key,"").Length>0&&EditorApplication.isPlaying)EditorApplication.QueuePlayerLoopUpdate();};
    }
    [MenuItem("Tools/Beetle Arena/Validate Save Thumbnail Play Flow")]
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before thumbnail validation.");
        Directory.CreateDirectory(Output);
        string path=Path.GetFullPath(Output+"/Save-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(path);
        File.WriteAllText(Output+"/Result.txt","SAVE THUMBNAIL PLAY VALIDATION\n"+DateTimeOffset.Now.ToString("o")+"\n"+path+"\n");
        SessionState.SetString(Key+".storage",CampaignApp.StorageOverride??"");SessionState.SetString(Key,path);CampaignApp.StorageOverride=path;
        SessionState.SetString(Key+".start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Campaign/CampaignMainMenu.unity");
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Show();EditorApplication.isPlaying=true;
    }
    static void OnPlay(PlayModeStateChange change)
    {
        if(SessionState.GetString(Key,"").Length==0)return;
        if(change==PlayModeStateChange.EnteredPlayMode)
        {var host=new GameObject("Save thumbnail validation");UnityEngine.Object.DontDestroyOnLoad(host);host.AddComponent<CampaignThumbnailPlayDriver>();}
        if(change!=PlayModeStateChange.EnteredEditMode)return;
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key+".start",""));
        string storage=SessionState.GetString(Key+".storage","");CampaignApp.StorageOverride=storage.Length==0?null:storage;
        SessionState.EraseString(Key);SessionState.EraseString(Key+".start");SessionState.EraseString(Key+".storage");
    }
}

public sealed class CampaignThumbnailPlayDriver:MonoBehaviour
{
    const string Output=CampaignThumbnailPlayValidation.Output;
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    CampaignApp app;bool background,cleaned;int checks,failures;
    readonly List<string> errors=new List<string>();
    CampaignView View=>FindFirstObjectByType<CampaignView>();
    CampaignBackdrop Backdrop=>View.backdrop;
    CampaignConstructionController Construction=>FindFirstObjectByType<CampaignConstructionController>();
    void Start()
    {
        background=Application.runInBackground;Application.runInBackground=true;
        Application.logMessageReceived+=Log;StartCoroutine(Guard(Run()));
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
    void Check(bool condition,string label)
    {checks++;if(!condition)failures++;File.AppendAllText(Output+"/Result.txt",(condition?"PASS ":"FAIL ")+label+"\n");}
    void Set(string name,object value)=>typeof(CampaignApp).GetField(name,Private).SetValue(app,value);
    IEnumerator Wait(Func<bool> condition,string label)
    {
        float deadline=Time.realtimeSinceStartup+45;
        while(!condition()){if(Time.realtimeSinceStartup>deadline)throw new TimeoutException(label+" | "+app?.Message);yield return null;}
        yield return null;yield return null;
    }
    IEnumerator Idle()=>Wait(()=>app&&!app.Busy,"commit");
    IEnumerator Page(CampaignPage page)=>Wait(()=>app&&!app.Busy&&View&&View.page==page&&Backdrop.Ready&&SceneManager.GetActiveScene().name==CampaignApp.SceneName(page),page.ToString());
    IEnumerator Stable()
    {yield return new WaitForSecondsRealtime(1.3f);yield return new WaitForEndOfFrame();}
    static Texture2D Read(RenderTexture target)
    {
        var previous=RenderTexture.active;var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try{RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();return texture;}
        finally{RenderTexture.active=previous;}
    }
    void ScreenShot(string name)
    {
        var game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));RenderTexture target=null;
        for(Type type=game.GetType();type!=null&&!target;type=type.BaseType)
        {
            var field=type.GetField("m_TargetTexture",Private|BindingFlags.DeclaredOnly)??type.GetField("m_RenderTexture",Private|BindingFlags.DeclaredOnly);
            if(field!=null)target=field.GetValue(game) as RenderTexture;
        }
        var texture=Read(target);
        try
        {
            if(SystemInfo.graphicsUVStartsAtTop)
            {var pixels=texture.GetPixels32();var upright=new Color32[pixels.Length];for(int y=0;y<texture.height;y++)Array.Copy(pixels,y*texture.width,upright,(texture.height-1-y)*texture.width,texture.width);texture.SetPixels32(upright);texture.Apply();}
            File.WriteAllBytes(Output+"/"+name+".png",texture.EncodeToPNG());
        }
        finally{Destroy(texture);}
    }
    // Independent CPU reference: fit the exact world frame without the capture method's GL draw.
    Texture2D Reference(string name)
    {
        var source=Read(Backdrop.Frame);var expected=new Texture2D(320,180,TextureFormat.RGB24,false);
        float scale=Mathf.Min(320f/source.width,180f/source.height),w=source.width*scale,h=source.height*scale;
        var pixels=new Color[320*180];
        for(int y=0;y<180;y++)for(int x=0;x<320;x++)
        {
            float u=(x+.5f-(320-w)*.5f)/w,v=(y+.5f-(180-h)*.5f)/h;
            pixels[y*320+x]=u<0||u>1||v<0||v>1?Color.black:source.GetPixelBilinear(u,v);
        }
        expected.SetPixels(pixels);expected.Apply();Destroy(source);
        File.WriteAllBytes(Output+"/"+name+"-Expected.png",expected.EncodeToPNG());ScreenShot(name+"-Game");return expected;
    }
    void Compare(string encoded,Texture2D expected,string label)
    {
        if(string.IsNullOrEmpty(encoded)){Check(false,label+": missing image");Destroy(expected);return;}
        var actual=new Texture2D(2,2);actual.LoadImage(Convert.FromBase64String(encoded));
        File.WriteAllBytes(Output+"/"+label+"-Saved.jpg",Convert.FromBase64String(encoded));
        Check(actual.width==320&&actual.height==180,label+": JPEG dimensions 320x180");
        float error=Difference(actual,expected),flipped=Difference(actual,expected,true);
        Check(error<.015f&&error<flipped*.7f,label+": frame matches in color, orientation and fit; MAE="+error.ToString("F5")+", flipped="+flipped.ToString("F5"));
        Destroy(actual);Destroy(expected);
    }
    static float Difference(Texture2D a,Texture2D b,bool flip=false)
    {
        double sum=0;for(int y=0;y<180;y++)for(int x=0;x<320;x++)
        {Color ca=a.GetPixelBilinear((x+.5f)/320,(y+.5f)/180),cb=b.GetPixelBilinear((x+.5f)/320,flip?1-(y+.5f)/180:(y+.5f)/180);sum+=Math.Abs(ca.r-cb.r)+Math.Abs(ca.g-cb.g)+Math.Abs(ca.b-cb.b);}
        return (float)(sum/(320*180*3));
    }
    void CheckSlotImages()
    {
        var images=View.GetComponentsInChildren<UI.RawImage>().Where(image=>image.name=="Thumbnail").OrderBy(image=>image.transform.position.x).ToArray();
        Check(images.Length==4,"save page has four populated thumbnail views");
        for(int i=0;i<images.Length;i++)
        {
            var saved=new Texture2D(2,2);saved.LoadImage(Convert.FromBase64String(app.ReadSlot(i).thumbnail));
            Check(images[i].texture is Texture2D shown&&Difference(shown,saved)<.00001f,"slot "+i+" displays its own persisted image");Destroy(saved);
            var size=images[i].rectTransform.rect.size;
            Check(Mathf.Abs(size.x/size.y-320f/180)<.0001f,"slot "+i+" preserves image aspect without stretching");
        }
    }
    IEnumerator Overwrite(int slot)
    {
        var button=View.GetComponentsInChildren<UI.Button>().Where(b=>b.name=="覆盖").OrderBy(b=>b.transform.position.x).ElementAt(slot);
        Check(button.IsInteractable(),"overwrite button is available for slot "+slot);button.onClick.Invoke();yield return null;yield return null;
        View.GetComponentsInChildren<UI.Button>().Single(b=>b.name=="确定"&&b.IsInteractable()).onClick.Invoke();yield return null;yield return null;
        Check(!app.Modal,"overwrite confirmation closes");
    }
    IEnumerator Run()
    {
        app=CampaignApp.Ensure();yield return Page(CampaignPage.MainMenu);
        Check(Path.GetFullPath(app.SaveDirectory)==Path.GetFullPath(CampaignApp.StorageOverride),"isolated storage");
        int frame=Time.frameCount;yield return null;yield return null;Check(Time.frameCount>frame,"player frames advance");
        if(!app.NewGame(0,"快照验收"))throw new InvalidOperationException(app.Message);yield return Page(CampaignPage.Greenhouse);
        if(!app.Execute(s=>s.ConfigureClock(86400,1,false)))throw new InvalidOperationException(app.Message);yield return Idle();yield return Stable();
        // First automatic save must acquire an actual image, even without visiting another page.
        var expected=Reference("01-Auto");app.Preferences.autoSaveSeconds=30;Set("dirty",true);Set("saveClock",31f);
        yield return null;yield return Idle();Compare(app.ReadSlot(0).thumbnail,expected,"01-Auto");

        app.Session.State.clock.paused=true;yield return Stable();expected=Reference("02-Manual");
        Check(app.SaveSlot(1),"manual save accepted");Compare(app.ReadSlot(1).thumbnail,expected,"02-Manual");
        // Presentation changes are deliberately not domain mutations.
        FindFirstObjectByType<CampaignObservationCamera>().SetMode(CampaignObservationMode.Specimen);yield return Stable();
        expected=Reference("03-ViewOnly");app.Go(CampaignPage.Saves);yield return Page(CampaignPage.Saves);
        Check(app.SaveSlot(2),"save from save page accepted");Compare(app.ReadSlot(2).thumbnail,expected,"03-ViewOnly");

        app.Go(CampaignPage.Greenhouse);yield return Page(CampaignPage.Greenhouse);
        string first=app.Session.State.selectedId;string second=app.Session.State.beetles.First(b=>b.id!=first).id;
        Construction.SetEditing(true);Construction.BeginPlace(CampaignDecorations.Fern);
        if(!Construction.PreviewAtCell(1,3)||!Construction.Confirm())throw new InvalidOperationException("Fern fixture rejected");
        yield return Idle();Construction.SetEditing(false);yield return Stable();
        Check(Construction.Bindings.Count==1,"placed scenery is visible in selected enclosure");expected=Reference("04-Scenery");
        Check(app.SaveSlot(3),"scenery manual save accepted");Compare(app.ReadSlot(3).thumbnail,expected,"04-Scenery");

        app.Select(second);yield return Stable();Check(Construction.Bindings.Count==0,"other beetle has independent empty enclosure");
        expected=Reference("05-Selection");app.Go(CampaignPage.Saves);yield return Page(CampaignPage.Saves);
        Compare(app.ReadSlot(0).thumbnail,expected,"05-Selection");CheckSlotImages();yield return new WaitForEndOfFrame();ScreenShot("06-SavePage");
        // Run the real overwrite/confirm button bindings and check the rebuilt displayed texture.
        app.Go(CampaignPage.Greenhouse);yield return Page(CampaignPage.Greenhouse);View.ToggleSidebar();
        yield return Wait(()=>!View.SidebarAnimationActive,"sidebar folded");yield return Stable();expected=Reference("07-Folded");
        app.Go(CampaignPage.Saves);yield return Page(CampaignPage.Saves);yield return Overwrite(1);
        Compare(app.ReadSlot(1).thumbnail,expected,"07-Folded");yield return null;yield return null;CheckSlotImages();yield return new WaitForEndOfFrame();ScreenShot("08-Overwritten");

        app.LoadSlot(3);yield return Page(CampaignPage.Greenhouse);yield return Stable();
        Check(app.Session.State.selectedId==first&&Construction.Bindings.Count==1,"loading a slot restores the pictured beetle and scenery");
        expected=Reference("09-Reloaded");Check(app.SaveSlot(0),"save after reload accepted");Compare(app.ReadSlot(0).thumbnail,expected,"09-Reloaded");
        string clean=app.ReadSlot(0).thumbnail;
        Construction.SetEditing(true);Construction.BeginPlace(CampaignDecorations.Stones);Construction.PreviewAtCell(9,5);yield return Stable();
        Check(app.SaveSlot(0)&&app.ReadSlot(0).thumbnail==clean,"construction grid, hidden animal and placement ghost do not replace clean image");
        Construction.SetEditing(false);yield return Stable();
        Backdrop.SetUiFocus(true);
        Check(!Backdrop.Ready&&app.SaveSlot(0)&&app.ReadSlot(0).thumbnail==clean,"unrendered frame retains previous valid image");
        Backdrop.SetUiFocus(false);yield return Stable();
        // A non-greenhouse page must retain the last greenhouse image.
        app.Go(CampaignPage.Market);yield return Page(CampaignPage.Market);string last=app.ReadSlot(0).thumbnail;
        Check(app.SaveSlot(2)&&app.ReadSlot(2).thumbnail==last,"market saves preserve greenhouse image, not market backdrop");

        app.Go(CampaignPage.Greenhouse);yield return Page(CampaignPage.Greenhouse);FindFirstObjectByType<CampaignObservationCamera>().SetMode(CampaignObservationMode.Specimen);yield return Stable();
        expected=Reference("10-PauseFlush");Set("dirty",false);
        typeof(CampaignApp).GetMethod("OnApplicationPause",Private).Invoke(app,new object[]{true});
        Compare(app.ReadSlot(0).thumbnail,expected,"10-PauseFlush");
        if(!app.Execute(s=>{var b=s.Beetle(first);b.stage=CampaignLifeStage.Larva3;b.stageAgeDays=s.StageDuration(b)*.35;b.totalAgeDays=b.stageAgeDays;b.adultAgeDays=0;b.feed=-1;}))throw new InvalidOperationException(app.Message);
        yield return Idle();yield return Stable();
        Check(FindObjectsByType<CampaignSpecimen>(FindObjectsSortMode.None).Single(s=>s.selected).LifeStageDisplay.CurrentStage==CampaignLifeStage.Larva3,"larval stage has its own rendered enclosure");
        expected=Reference("11-Larva");Check(app.SaveSlot(2),"larval enclosure save accepted");Compare(app.ReadSlot(2).thumbnail,expected,"11-Larva");
        Check(errors.Count==0,"no runtime errors or exceptions");
    }
    void Cleanup(){if(cleaned)return;cleaned=true;Application.logMessageReceived-=Log;Application.runInBackground=background;}
    void OnDestroy()=>Cleanup();
    void Finish(Exception failure)
    {
        Cleanup();File.AppendAllText(Output+"/Result.txt",(failure==null&&failures==0?"ALL CHECKS PASSED":"FAILED")+" | "+checks+" checks, "+failures+" failures\n"+failure+"\n"+string.Join("\n",errors));
        EditorApplication.isPlaying=false;
    }
}
