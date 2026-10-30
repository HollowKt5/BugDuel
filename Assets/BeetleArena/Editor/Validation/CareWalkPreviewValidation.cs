using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BeetleArena;
using BeetleArena.Campaign;
using BugDuel.Domain;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>Fixed-step native-rig walk, rendered by URP in the isolated greenhouse fixture.</summary>
public static class CareWalkPreviewValidation
{
    const int SimulationRate=60,CaptureRate=20,Seconds=6;
    public static IEnumerator Capture(CampaignSpecimen specimen,Camera sceneCamera,Action<bool,string> check)
    {
        string folder=HusbandryPolishValidation.Output+"/Walk";Directory.CreateDirectory(folder);
        var source=specimen.gameObject;bool sourceActive=source.activeSelf;
        var app=CampaignApp.Instance;bool paused=app.Session.State.clock.paused;
        var native=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BeetleArena/Generated/ArticulatedBeetle.prefab");
        GameObject clone=null,cameraObject=null;RenderTexture frame=null;Texture2D pixels=null;
        try
        {
            app.Session.State.clock.paused=true;
            clone=UnityEngine.Object.Instantiate(native);clone.name="Deterministic care walk preview";
            SceneManager.MoveGameObjectToScene(clone,source.scene);
            foreach(var behavior in clone.GetComponentsInChildren<MonoBehaviour>(true))behavior.enabled=false;
            foreach(var collider in clone.GetComponentsInChildren<Collider>(true))collider.enabled=false;
            foreach(var actor in clone.GetComponentsInChildren<BeetleActor>(true))if(actor.teamMarker)actor.teamMarker.gameObject.SetActive(false);
            var rig=clone.GetComponentInChildren<BeetleProceduralAnimator>(true);
            clone.transform.localScale=specimen.transform.lossyScale;rig.model.localScale=specimen.rig.model.localScale;
            var center=specimen.enclosureFloor.bounds.center;
            clone.transform.SetPositionAndRotation(new Vector3(center.x-.35f,specimen.Enclosure.GroundHeight+specimen.GroundOffset,center.z-.25f),Quaternion.Euler(0,80,0));
            var pose=new CarePosePresenter(rig,specimen.Enclosure.GroundHeight);
            pose.Feet.SurfaceHeight=specimen.Feet.SurfaceHeight;pose.Reset(false,false,119);
            for(int i=0;i<rig.shellRenderers.Length&&i<specimen.rig.shellRenderers.Length;i++)
            {
                var block=new MaterialPropertyBlock();specimen.rig.shellRenderers[i].GetPropertyBlock(block);rig.shellRenderers[i].SetPropertyBlock(block);
            }
            source.SetActive(false);
            var visible=rig.GetComponentsInChildren<Renderer>().Where(renderer=>renderer.enabled&&renderer.gameObject.activeInHierarchy).ToArray();
            var bounds=visible[0].bounds;foreach(var renderer in visible.Skip(1))bounds.Encapsulate(renderer.bounds);
            cameraObject=new GameObject("Care walk verification camera",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraObject,source.scene);
            var camera=cameraObject.GetComponent<Camera>();camera.CopyFrom(sceneCamera);
            var sourceData=sceneCamera.GetUniversalAdditionalCameraData();var cameraData=camera.GetUniversalAdditionalCameraData();
            EditorUtility.CopySerialized(sourceData,cameraData);cameraData.renderPostProcessing=false;
            camera.orthographic=false;camera.fieldOfView=42;camera.aspect=4f/3;camera.nearClipPlane=.025f;
            camera.rect=new Rect(0,0,1,1);camera.depth=sceneCamera.depth+1;
            float radius=bounds.extents.magnitude,distance=radius/Mathf.Tan(camera.fieldOfView*.5f*Mathf.Deg2Rad)*1.12f;
            var focus=bounds.center+new Vector3(.25f,0,0);
            camera.transform.position=focus+new Vector3(.85f,.58f,-1).normalized*distance;camera.transform.LookAt(focus);
            frame=new RenderTexture(960,720,24,RenderTextureFormat.ARGB32){name="Care walk verification frame",antiAliasing=1};frame.Create();
            camera.targetTexture=frame;camera.enabled=true;pixels=new Texture2D(960,720,TextureFormat.RGB24,false);
            var report=new StringBuilder("frame,seconds,state,speed,x,y,z,heading,supports,ik_error\n");
            int minSupport=6,captures=0;float maxError=0;var origin=clone.transform.position;
            var previousFeet=new Vector3[rig.legs.Length];var wasSupport=new bool[rig.legs.Length];float maxSlip=0;
            for(int i=0;i<previousFeet.Length;i++){previousFeet[i]=pose.Feet.Foot(i);wasSupport[i]=pose.Feet.IsSupport(i);}
            for(int tick=0;tick<SimulationRate*Seconds;tick++)
            {
                float t=tick/(float)SimulationRate,dt=1f/SimulationRate;
                float speed=t<.45f?0:t<1.35f?Mathf.SmoothStep(0,.15f,(t-.45f)/.9f):t<3.1f?.15f:
                    t<3.65f?Mathf.SmoothStep(.15f,0,(t-3.1f)/.55f):t<4.35f?0:Mathf.SmoothStep(0,.15f,(t-4.35f)/.8f);
                float turn=t>=1.8f&&t<3.1f?24:t>=4.8f?-18:0;
                clone.transform.Rotate(0,turn*dt,0);clone.transform.position+=clone.transform.forward*speed*dt;
                var state=speed>.001f?CareState.Wandering:CareState.Idle;
                pose.Present(state,speed,dt,turn);
                minSupport=Mathf.Min(minSupport,pose.Feet.SupportCount);maxError=Mathf.Max(maxError,pose.Feet.MaximumError);
                for(int i=0;i<previousFeet.Length;i++)
                {
                    bool support=pose.Feet.IsSupport(i);var foot=pose.Feet.Foot(i);
                    if(support&&wasSupport[i])maxSlip=Mathf.Max(maxSlip,Vector3.Distance(previousFeet[i],foot));
                    previousFeet[i]=foot;wasSupport[i]=support;
                }
                if((tick+1)%(SimulationRate/CaptureRate)!=0)continue;
                // Render through the real pipeline once for each three fixed simulation steps.
                yield return new WaitForEndOfFrame();
                var previous=RenderTexture.active;
                try
                {
                    RenderTexture.active=frame;pixels.ReadPixels(new Rect(0,0,960,720),0,0);pixels.Apply();
                    File.WriteAllBytes(folder+"/Walk-"+captures.ToString("D3")+".png",pixels.EncodeToPNG());
                }
                finally{RenderTexture.active=previous;}
                var p=clone.transform.position;
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,"{0},{1:F3},{2},{3:F4},{4:F5},{5:F5},{6:F5},{7:F3},{8},{9:F6}",
                    captures,(tick+1)*dt,state,speed,p.x,p.y,p.z,clone.transform.eulerAngles.y,pose.Feet.SupportCount,pose.Feet.MaximumError));
                captures++;
            }
            File.WriteAllText(folder+"/Sequence.csv",report.ToString());
            File.WriteAllText(folder+"/README.txt","Native articulated beetle in the isolated greenhouse. Simulation: 60 Hz; PNG playback: 20 fps; duration: 6 seconds.\n"+
                "0-.45s sensing; .45-1.35s accelerate; 1.35-3.10s walk then turn; 3.10-3.65s settle; 3.65-4.35s pause; 4.35-6s resume.\n"+
                "Camera is stationary. The navigation root genuinely translates and turns; this is not a pose-only preview.\n"+
                $"Captures={captures}; root travel={Vector3.Distance(origin,clone.transform.position):F3}; minimum supports={minSupport}; maximum IK error={maxError:F6}; planted slip={maxSlip:F6}\n");
            check(captures==CaptureRate*Seconds&&Vector3.Distance(origin,clone.transform.position)>.4f,
                "six-second native walk preview captures actual translation, turn, stop and resume at 20 fps");
            check(minSupport>=3&&maxError<.02f&&maxSlip<.002f,
                $"rendered walk retains support (IK {maxError:F5}, planted slip {maxSlip:F5})");
        }
        finally
        {
            if(cameraObject)
            {
                var camera=cameraObject.GetComponent<Camera>();camera.enabled=false;camera.targetTexture=null;
                UnityEngine.Object.Destroy(cameraObject);
            }
            if(clone)UnityEngine.Object.Destroy(clone);
            if(frame){frame.Release();UnityEngine.Object.Destroy(frame);}
            if(pixels)UnityEngine.Object.Destroy(pixels);
            if(source)source.SetActive(sourceActive);
            app.Session.State.clock.paused=paused;
        }
    }
}
