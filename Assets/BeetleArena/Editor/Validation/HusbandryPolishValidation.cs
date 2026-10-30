using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BeetleArena;
using BeetleArena.Campaign;
using BugDuel.Campaign;
using BugDuel.Domain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CampaignSpecimen=BeetleArena.Campaign.CampaignSpecimen;

[InitializeOnLoad]
public static class HusbandryPolishValidation
{
    public const string Output="Artifacts/HusbandryPolish";
    const string Key="BugDuel.HusbandryPolish";
    static HusbandryPolishValidation()
    {
        var storage=SessionState.GetString(Key,"");if(storage.Length>0)CampaignApp.StorageOverride=storage;
        EditorApplication.playModeStateChanged+=OnPlay;
        EditorApplication.update+=()=>{if(EditorApplication.isPlaying&&SessionState.GetString(Key,"").Length>0)EditorApplication.QueuePlayerLoopUpdate();};
    }
    [MenuItem("Tools/甲壳虫斗/验证/养殖/室内饲育桌与丰富动作",false,549)]
    public static void RunLogic()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before logic checks.");
        Directory.CreateDirectory(Output);var report=new StringBuilder();
        var preview=EditorSceneManager.NewPreviewScene();
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);report.AppendLine("PASS "+message);}
        try
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BeetleArena/Generated/ArticulatedBeetle.prefab");
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,preview);instance.SetActive(false);
            var rig=instance.GetComponentInChildren<BeetleProceduralAnimator>(true);var pose=new CarePosePresenter(rig);pose.Reset(false,false,119);
            bool left=false,right=false;float maxError=0;int minSupports=6;
            for(int i=0;i<360;i++)
            {
                pose.Present(CareState.Grooming,0,1f/60);maxError=Mathf.Max(maxError,pose.Feet.MaximumError);
                for(int j=0;j<rig.legs.Length;j++)if(rig.legs[j].pair==0&&!pose.Feet.IsSupport(j))
                {if(rig.legs[j].side<0)left=true;else right=true;}
                minSupports=Mathf.Min(minSupports,pose.Feet.SupportCount);
            }
            Check(minSupports>=5,"grooming keeps five supports throughout six seconds");
            Check(left&&right&&maxError<.02f,"both forelegs groom in turn within measured limb reach");
            for(int i=0;i<60;i++)pose.Present(CareState.Idle,0,1f/60);
            Check(pose.Feet.SupportCount==6,"grooming interruption lands the forefoot");
            var jaw=rig.GetComponentInChildren<BeetleJawRig>(true);Check(jaw&&jaw.left&&jaw.right,"imported beetle exposes two mouthparts");
            float opening=0;for(int i=0;i<240;i++){pose.Present(CareState.Eating,0,1f/60);opening=Mathf.Max(opening,Quaternion.Angle(jaw.leftRest,jaw.left.localRotation));}
            Check(opening>3,"feeding actually articulates mouthparts");
            var frozen=jaw.left.localRotation;
            for(int i=0;i<90;i++)pose.PresentAmbient(CareState.Eating,1f/60);
            Check(Quaternion.Angle(frozen,jaw.left.localRotation)<.001f,"paused sensing does not advance chewing");
            pose.Reset(false,false,119);for(int i=0;i<80;i++)pose.Present(CareState.Idle,0,1f/60);
            var first=rig.antennae[0].localRotation;
            pose.Reset(false,false,581);for(int i=0;i<80;i++)pose.Present(CareState.Idle,0,1f/60);
            Check(Quaternion.Angle(first,rig.antennae[0].localRotation)>1,"individual identities have different sensing rhythms");
            pose.Reset(false,false,119);for(int i=0;i<80;i++)pose.Present(CareState.Idle,0,1f/60);
            Check(Quaternion.Angle(first,rig.antennae[0].localRotation)<.001f,"same identity replays the same presentation sequence");
            foreach(int rate in new[]{20,60,144})foreach(int direction in new[]{-1,1})
            {
                instance.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);pose.Reset(false,false,119);
                int singleLeg=0,samples=0,minSupport=6;float bodyDelta=0,lastY=rig.bodyPivot.position.y;
                for(int i=0;i<rate*8;i++)
                {
                    instance.transform.Rotate(0,direction*48f/rate,0);pose.Present(CareState.Wandering,0,1f/rate);
                    if(i<rate){lastY=rig.bodyPivot.position.y;continue;}
                    samples++;if(pose.Feet.SupportCount==5)singleLeg++;
                    minSupport=Mathf.Min(minSupport,pose.Feet.SupportCount);
                    bodyDelta=Mathf.Max(bodyDelta,Mathf.Abs(rig.bodyPivot.position.y-lastY));lastY=rig.bodyPivot.position.y;
                }
                Check(minSupport>=3&&bodyDelta<.012f,$"pivot turn stable at {rate} Hz, direction {direction}, body step {bodyDelta:F5}");
                Check((float)singleLeg/samples<.3f,$"pivot turn alternates tripod groups instead of hopping one foot at a time ({singleLeg}/{samples})");
            }
            foreach(string shader in new[]{"BeetleArena/HusbandryDust","BeetleArena/HusbandryWindowSky","BeetleArena/HusbandrySoftToon","BeetleArena/HusbandrySeasonalFoliage","BeetleArena/HusbandryWeather","BeetleArena/HusbandryGlass","BeetleArena/Toon","BeetleArena/IridescentShell"})
            {
                var value=Shader.Find(shader);Check(value&&value.isSupported&&!ShaderUtil.ShaderHasError(value),"compiled shader "+shader);
            }
            report.AppendLine(CareMotionValidation.CheckLogic());
            report.AppendLine(HusbandryTreeModelValidation.CheckLogic());
            CareAmbientValidation.Run();CareGroundingValidation.Run();CareBehaviorValidation.Run();CareEnclosureValidation.Run();
            report.AppendLine("PASS existing motion, ambient, grounding, behavior and enclosure regressions");
            report.AppendLine("COMPLETE PASS");
        }
        catch(Exception error){report.AppendLine("FAIL "+error);throw;}
        finally{EditorSceneManager.ClosePreviewScene(preview);File.WriteAllText(Output+"/Logic.txt",report.ToString());}
    }

    public static void RunPlay()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before isolated preview.");
        Directory.CreateDirectory(Output);
        string storage=Path.GetFullPath(Output+"/Save-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var session=CampaignSession.New(CampaignCatalog.Defaults(),84517);session.RenamePlayer("桌边饲育员");
        session.State.clock.elapsedDays=Math.Floor(session.State.clock.elapsedDays)+.5;
        foreach(var beetle in session.State.beetles){beetle.nutrition=100;beetle.overturned=false;}
        new CampaignSaveRepository(storage).Write(0,new CampaignSave{state=session.State,catalog=session.Catalog});
        SessionState.SetString(Key+".oldStorage",CampaignApp.StorageOverride??"");
        SessionState.SetString(Key+".start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetString(Key,storage);CampaignApp.StorageOverride=storage;
        File.WriteAllText(Output+"/Play.txt","Isolated room play verification\n");
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Campaign/CampaignMainMenu.unity");
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Show();EditorApplication.isPlaying=true;
    }
    static void OnPlay(PlayModeStateChange state)
    {
        if(SessionState.GetString(Key,"").Length==0)return;
        if(state==PlayModeStateChange.EnteredPlayMode)
        {var go=new GameObject("Room verification");UnityEngine.Object.DontDestroyOnLoad(go);go.AddComponent<HusbandryPolishDriver>();}
        if(state!=PlayModeStateChange.EnteredEditMode)return;
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key+".start",""));
        var old=SessionState.GetString(Key+".oldStorage","");CampaignApp.StorageOverride=old.Length==0?null:old;
        SessionState.EraseString(Key);SessionState.EraseString(Key+".oldStorage");SessionState.EraseString(Key+".start");
    }
}

public sealed class HusbandryPolishDriver:MonoBehaviour
{
    bool previousBackground;DisplayPreferencesSnapshot preferences;int errors;float deadline;
    void Start()
    {
        previousBackground=Application.runInBackground;Application.runInBackground=true;preferences=new DisplayPreferencesSnapshot();
        Application.logMessageReceived+=Log;deadline=Time.realtimeSinceStartup+110;StartCoroutine(Run());
    }
    void Log(string message,string stack,LogType type)
    {if(type==LogType.Error||type==LogType.Exception){errors++;File.AppendAllText(HusbandryPolishValidation.Output+"/Play.txt",message+"\n"+stack+"\n");}}
    void Update(){if(Time.realtimeSinceStartup>deadline)Finish("FAIL timed out");}
    void Check(bool ok,string message)
    {
        File.AppendAllText(HusbandryPolishValidation.Output+"/Play.txt",(ok?"PASS ":"FAIL ")+message+"\n");
        if(!ok){errors++;throw new InvalidOperationException(message);}
    }
    IEnumerator Run()
    {
        while(!CampaignApp.Instance||CampaignApp.Instance.Busy)yield return null;
        var app=CampaignApp.Instance;
        // This app reads from the isolated preview directory; keep first-time hints out of visual captures.
        app.Preferences.learnedControls=63;app.LoadSlot(0);
        CampaignView view=null;
        while(app.Busy||!(view=FindFirstObjectByType<CampaignView>())||view.page!=CampaignPage.Greenhouse||!view.backdrop.Ready)yield return null;
        // Dismiss only the new fixture's one-time hint, never a player's modal.
        if(app.Modal)typeof(CampaignView).GetMethod("CloseModal",BindingFlags.Instance|BindingFlags.NonPublic)?.Invoke(view,null);
        BeetleDisplayPreferences.Style=BeetleDisplayStyle.Clean;
        BeetleGlassPreferences.SetQuality(BeetleGlassQuality.Realistic);
        yield return new WaitForSecondsRealtime(2);
        var specimen=FindObjectsByType<CampaignSpecimen>(FindObjectsSortMode.None).Single(s=>s.selected);
        var atmosphere=FindFirstObjectByType<HusbandryAtmosphere>();
        Check(atmosphere&&atmosphere.Ready,"room canopy cookie is bound at runtime");
        Check(GameObject.Find("HusbandryRoom")&&GameObject.Find("Field notebook cover"),"room and desktop props loaded");
        var desk=GameObject.Find("Backdrop").GetComponent<Renderer>().bounds;
        var sill=GameObject.Find("Window sill").GetComponent<Renderer>().bounds;
        Check(desk.size.x>=28&&desk.size.x>desk.size.z*2,"one continuous long desk spans the window and both work areas");
        Check(desk.max.z>=sill.min.z&&desk.max.z<=sill.max.z,"desktop back edge meets the window sill without a walkway gap");
        var leftWall=GameObject.Find("Side plaster").GetComponent<Renderer>().bounds;
        float wallOverlap=leftWall.max.x-desk.min.x;
        Check(wallOverlap>=0&&wallOverlap<.06f,"the long desk meets the left wall without a gap or a visible wall intersection");
        bool OnDesktop(Bounds bounds)=>bounds.min.x>=desk.min.x-.001f&&bounds.max.x<=desk.max.x+.001f&&
            bounds.min.z>=desk.min.z-.001f&&bounds.max.z<=desk.max.z+.001f;
        var propNames=new[]{"Field notebook cover","Graphite pencil","Misting bottle","Plant pot","Care tray","Feeding tweezers"};
        var deskProps=FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r=>r.gameObject.scene==atmosphere.gameObject.scene&&propNames.Contains(r.name)).ToArray();
        Check(propNames.All(name=>deskProps.Any(r=>r.name==name))&&deskProps.All(r=>OnDesktop(r.bounds)),
            "notebook, pencil, misting bottle, plant, tray and tweezers remain supported by the shifted desktop");
        var enclosure=FindObjectsByType<HusbandryGlassEnclosure>(FindObjectsSortMode.None).Single(value=>value.gameObject.scene==atmosphere.gameObject.scene);
        var soil=GameObject.Find("SoilBed").GetComponent<Renderer>();
        Check(enclosure.GetComponentsInChildren<Renderer>().All(r=>OnDesktop(r.bounds))&&OnDesktop(soil.bounds),
            "the complete beetle enclosure stays within the desktop footprint");
        var roomDaylight=atmosphere.GetComponent<HusbandryDaylight>();
        Check(roomDaylight&&Mathf.Abs(soil.bounds.center.x-desk.center.x)<.025f&&
            Mathf.Abs(roomDaylight.windowCenter.x-desk.center.x)<.025f,
            "enclosure, long desk and window share the translated horizontal center");
        var sky=GameObject.Find("Window sky").GetComponent<Renderer>();
        Check(sky.sharedMaterial.HasProperty("_GardenEnabled")&&sky.sharedMaterial.GetFloat("_GardenEnabled")==0&&
            GameObject.Find("WindowTreeExterior")&&GameObject.Find("Pleated linen curtain"),"geometric outdoor tree replaces the painted garden behind folded curtains");
        Check(specimen.Enclosure.Area.CanWalk,"room dressing leaves enclosure navigation reachable");
        var carePosition=specimen.Enclosure.ToLocal(specimen.transform.position);
        var resident=specimen.Care.Find(app.Session.State.selectedId);
        Check(specimen.Enclosure.Area.IsClear(carePosition)&&
            Vector2.Distance(carePosition,specimen.Enclosure.Area.KeepInside(carePosition))<.025f&&
            resident!=null&&Vector2.Distance(carePosition,resident.Motor.Position)<.025f&&
            Mathf.Abs(specimen.transform.position.y-specimen.Enclosure.GroundHeight-specimen.GroundOffset)<.025f,
            "selected beetle remains in the translated enclosure and matches its local locomotion position");
        int cameraChecks=HusbandryCameraBoundaryValidation.Check(CampaignObservationCamera.Ensure(view.backdrop),view.backdrop.sceneCamera);
        Check(cameraChecks>0,$"indoor camera boundaries and construction transitions passed ({cameraChecks} assertions)");
        int frame=Time.frameCount;var antenna=specimen.rig.antennae[0].localRotation;
        var cookie=atmosphere.canopy.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>().lightCookieOffset;
        yield return new WaitForSecondsRealtime(3);
        Check(Time.frameCount>frame+10,"game advances instead of rendering a frozen editor frame");
        Check(Quaternion.Angle(antenna,specimen.rig.antennae[0].localRotation)>.1f,"beetle senses the room in actual Play mode");
        Check(cookie!=atmosphere.canopy.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>().lightCookieOffset,"soft leaf shadows sway");
        Capture(view.backdrop.WorldFrame,"Room-Overview");
        yield return CaptureComposed("Room-GameView");
        yield return HusbandryCameraBoundaryValidation.CaptureMaximumZoom(CampaignObservationCamera.Ensure(view.backdrop),view.backdrop.sceneCamera,view.backdrop.WorldFrame);
        var daylight=FindFirstObjectByType<HusbandryDaylight>();Check(daylight&&daylight.Daylight>.9f,"noon uses daylight from the campaign clock");
        app.Session.State.clock.paused=true;
        app.Session.State.clock.elapsedDays=Math.Floor(app.Session.State.clock.elapsedDays)+18.3/24;
        yield return new WaitForSecondsRealtime(2);Capture(view.backdrop.WorldFrame,"Room-Sunset");
        float sunset=daylight.Daylight;
        app.Session.State.clock.elapsedDays=Math.Floor(app.Session.State.clock.elapsedDays)+23.0/24;
        yield return new WaitForSecondsRealtime(2);Capture(view.backdrop.WorldFrame,"Room-Night");
        Check(daylight.Daylight<.01f&&sunset>daylight.Daylight,"sunset and night follow the same saved clock as the UI");
        float frozenHour=daylight.Hour;yield return new WaitForSecondsRealtime(.5f);
        Check(daylight.Hour==frozenHour,"pausing the calendar freezes the time of day");
        yield return HusbandrySeasonalTreeValidation.Run(app,view.backdrop.WorldFrame,Check);
        app.Session.State.clock.elapsedDays=Math.Floor(app.Session.State.clock.elapsedDays)+.5;app.Session.State.clock.paused=false;
        var camera=CampaignObservationCamera.Ensure(view.backdrop);camera.SetMode(CampaignObservationMode.Specimen);
        yield return new WaitForSecondsRealtime(2);Capture(view.backdrop.WorldFrame,"Beetle-Closeup");
        yield return CareWalkPreviewValidation.Capture(specimen,view.backdrop.sceneCamera,Check);
        camera.SetMode(CampaignObservationMode.Environment);yield return new WaitForSecondsRealtime(2);
        BeetleDisplayPreferences.Style=BeetleDisplayStyle.OrderedDither;yield return new WaitForSecondsRealtime(1);
        yield return CaptureComposed("Room-GameView-Dither");
        Check(specimen.Feet.MaximumError<.02f&&specimen.Feet.SupportCount>=3,"living beetle retains support in the room");
        // Disable and re-enable the effect to verify cookie ownership and resource recreation.
        atmosphere.enabled=false;yield return null;Check(!atmosphere.canopy.cookie,"disabling atmosphere restores the previous light cookie");
        atmosphere.enabled=true;yield return null;Check(atmosphere.Ready,"atmosphere can be enabled again without stale resources");
        Check(errors==0,"no runtime errors");Finish("COMPLETE PASS");
    }
    public static void Capture(RenderTexture frame,string name)
    {
        var previous=RenderTexture.active;var pixels=new Texture2D(frame.width,frame.height,TextureFormat.RGB24,false);
        try{RenderTexture.active=frame;pixels.ReadPixels(new Rect(0,0,frame.width,frame.height),0,0);pixels.Apply();File.WriteAllBytes(HusbandryPolishValidation.Output+"/"+name+".png",pixels.EncodeToPNG());}
        finally{RenderTexture.active=previous;Destroy(pixels);}
    }
    IEnumerator CaptureComposed(string name)
    {
        yield return new WaitForEndOfFrame();
        var game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));RenderTexture target=null;
        for(var type=game.GetType();type!=null&&!target;type=type.BaseType)
        {
            var field=type.GetField("m_TargetTexture",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)??
                type.GetField("m_RenderTexture",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
            if(field!=null)target=field.GetValue(game) as RenderTexture;
        }
        if(!target)throw new InvalidOperationException("No composed Game View frame");
        var previous=RenderTexture.active;var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try
        {
            RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();
            if(SystemInfo.graphicsUVStartsAtTop)
            {
                var source=pixels.GetPixels32();var upright=new Color32[source.Length];
                for(int y=0;y<target.height;y++)Array.Copy(source,y*target.width,upright,(target.height-1-y)*target.width,target.width);
                pixels.SetPixels32(upright);pixels.Apply();
            }
            File.WriteAllBytes(HusbandryPolishValidation.Output+"/"+name+".png",pixels.EncodeToPNG());
        }
        finally{RenderTexture.active=previous;Destroy(pixels);}
    }
    void Finish(string message){File.AppendAllText(HusbandryPolishValidation.Output+"/Play.txt",message+"\n");EditorApplication.isPlaying=false;}
    void OnDestroy(){Application.logMessageReceived-=Log;preferences?.Dispose();Application.runInBackground=previousBackground;}
}
