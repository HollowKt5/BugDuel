using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BeetleArena;
using BeetleArena.Campaign;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>Runs against the isolated greenhouse preview; restores its complete observation pose afterward.</summary>
public static class HusbandryCameraBoundaryValidation
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.DeclaredOnly;

    public static int Check(CampaignObservationCamera observation,Camera camera)
    {
        if(!observation||!camera)throw new ArgumentNullException(nameof(observation));
        var room=UnityEngine.Object.FindObjectsByType<HusbandryDaylight>(FindObjectsSortMode.None)
            .Single(r=>r.gameObject.scene==observation.gameObject.scene);
        var side=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
            .Single(r=>r.name=="Side plaster"&&r.gameObject.scene==observation.gameObject.scene);
        var snapshot=new CameraState(observation,camera);var fields=snapshot.fields;
        T Read<T>(string name)=>(T)fields.Single(f=>f.Name==name).GetValue(observation);
        void Set(string name,object value)=>fields.Single(f=>f.Name==name).SetValue(observation,value);
        var apply=typeof(CampaignObservationCamera).GetMethod("ApplyPose",Fields);
        var desiredFocus=typeof(CampaignObservationCamera).GetMethod("DesiredFocus",Fields);
        var lateUpdate=typeof(CampaignObservationCamera).GetMethod("LateUpdate",Fields);
        int checks=0,poses=0;var report=new StringBuilder();var corners=new Vector3[4];
        void Require(bool valid,string message)
        {if(!valid)throw new InvalidOperationException("Husbandry camera: "+message);checks++;}
        void CheckPose(string label,bool preserveDistance)
        {
            apply.Invoke(observation,null);poses++;
            float yaw=Read<float>("azimuth");
            Require(observation.Distance<=Read<float>("maxDistance")+.001f&&Read<float>("targetDistance")<=Read<float>("maxDistance")+.001f,
                "actual or requested distance exceeds the indoor zoom limit; "+label);
            Require(yaw>=CampaignObservationCamera.MinimumRoomAzimuth-.001f&&yaw<=CampaignObservationCamera.MaximumRoomAzimuth+.001f,
                "orbit leaves the front sector; "+label);
            camera.CalculateFrustumCorners(new Rect(0,0,1,1),camera.nearClipPlane,Camera.MonoOrStereoscopicEye.Mono,corners);
            for(int i=-1;i<corners.Length;i++)
            {
                var point=i<0?camera.transform.position:camera.transform.TransformPoint(corners[i]);
                Require(point.z<=room.windowCenter.z-.4f+.001f,"camera/near-plane crosses the window; "+label);
                Require(point.x>=side.bounds.max.x+.06f-.001f,"camera/near-plane crosses the side wall; "+label);
                Require(point.y>observation.GroundHeight,"camera/near-plane crosses the soil; "+label);
            }
            if(preserveDistance)
                Require(Mathf.Abs(Vector3.Distance(camera.transform.position,Read<Vector3>("focus"))-observation.Distance)<.002f,
                    "room restriction shortens the framing distance; "+label);
        }
        try
        {
            foreach(float aspect in new[]{.4f,1.2f,2.4f})
            foreach(int view in new[]{0,1,2})
            {
                observation.SetConstructionView(false);camera.aspect=aspect;
                var mode=view==1?CampaignObservationMode.Specimen:CampaignObservationMode.Environment;
                observation.SetMode(mode);observation.ResetView();
                Require(Read<float>("maxDistance")<=Read<float>("targetDistance")*CampaignObservationCamera.MaximumRoomZoomFactor+.002f,
                    "room zoom-out exceeds the allowed margin beyond default framing in "+mode);
                if(view==2)observation.SetConstructionView(true);
                if(view==2)Require(Read<float>("maxDistance")<=Read<float>("targetDistance")*CampaignObservationCamera.MaximumRoomZoomFactor+.002f,
                    "construction zoom-out exceeds the allowed margin beyond default framing");
                Require(observation.Mode==mode,"requested observation mode is unavailable");
                if(view==2)Require(Mathf.Abs(observation.Elevation-67)<.001f,"construction loses its overhead angle");
                Set("focus",desiredFocus.Invoke(observation,null));
                foreach(float yaw in new[]{-100000f,180f,100000f})
                foreach(float pitch in new[]{CampaignObservationCamera.MinimumElevation,CampaignObservationCamera.MaximumElevation})
                foreach(bool far in new[]{false,true})
                {
                    float distance=Read<float>(far?"maxDistance":"minDistance");
                    Set("azimuth",yaw);Set("elevation",pitch);Set("distance",distance);Set("targetDistance",distance);
                    CheckPose($"view={view}, aspect={aspect}, yaw={yaw}, pitch={pitch}, far={far}",true);
                }
                report.AppendLine($"PASS { (view==2?"construction":mode.ToString()) } at aspect {aspect}: angle, zoom and full near-plane boundaries");
            }

            observation.SetConstructionView(false);camera.aspect=1.2f;
            observation.SetMode(CampaignObservationMode.Environment);observation.ResetView();
            Set("focus",desiredFocus.Invoke(observation,null));
            Set("distance",Read<float>("minDistance"));Set("targetDistance",Read<float>("minDistance"));
            foreach(float sign in new[]{-1f,1f})
            {
                for(int i=0;i<5;i++)
                {
                    observation.ApplyOrbitInput(new Vector2(sign*1000000,sign*1000000),sign*1000);
                    Set("distance",Read<float>("targetDistance"));
                    CheckPose("repeated extreme input direction "+sign,true);
                }
                float limit=Read<float>("azimuth");
                observation.ApplyOrbitInput(new Vector2(-sign*12,0),0);
                CheckPose("immediate reverse direction "+sign,true);
                Require((Read<float>("azimuth")-limit)*sign<-.01f,"dragging away from a boundary has hidden input overshoot");
            }
            report.AppendLine("PASS repeated extreme drag/zoom stops at the boundary and reversing responds immediately");

            foreach(var mode in new[]{CampaignObservationMode.Environment,CampaignObservationMode.Specimen})
            {
                observation.SetMode(mode);observation.ResetView();
                Set("focus",desiredFocus.Invoke(observation,null));Set("distance",Read<float>("targetDistance"));
                CheckPose("reset "+mode,true);
                float yaw=Read<float>("azimuth"),pitch=observation.Elevation,zoom=Read<float>("targetDistance");
                observation.SetConstructionView(true);Set("distance",Read<float>("targetDistance"));
                CheckPose("enter construction from "+mode,true);
                observation.SetConstructionView(false);Set("distance",Read<float>("targetDistance"));
                CheckPose("exit construction to "+mode,true);
                Require(observation.Mode==mode&&Mathf.Abs(Read<float>("azimuth")-yaw)<.001f&&Mathf.Abs(observation.Elevation-pitch)<.001f&&
                    Mathf.Abs(Read<float>("targetDistance")-zoom)<.001f,"construction does not restore the prior observation view");
            }
            report.AppendLine("PASS environment/specimen reset and construction enter/exit preserve safe framing");

            // Exercise the real resize path while already at the far stop; independently resetting each
            // aspect would miss a stale interpolated distance from a previously much narrower viewport.
            observation.SetConstructionView(false);observation.SetMode(CampaignObservationMode.Environment);
            camera.aspect=.4f;lateUpdate.Invoke(observation,null);observation.ResetView();
            observation.ApplyOrbitInput(Vector2.zero,-1000);Set("distance",Read<float>("targetDistance"));
            foreach(float resizedAspect in new[]{2.4f,.4f,1.2f})
            {
                camera.aspect=resizedAspect;lateUpdate.Invoke(observation,null);
                Set("focus",desiredFocus.Invoke(observation,null));
                CheckPose("live resize at far stop to "+resizedAspect,true);
                foreach(var resizedMode in new[]{CampaignObservationMode.Specimen,CampaignObservationMode.Environment})
                {
                    observation.SetMode(resizedMode);Set("focus",desiredFocus.Invoke(observation,null));
                    CheckPose("mode change after live resize to "+resizedMode,true);
                    observation.ApplyOrbitInput(Vector2.zero,-1000);Set("distance",Read<float>("targetDistance"));
                    CheckPose("extreme wheel after live resize in "+resizedMode,true);
                }
                observation.SetConstructionView(true);Set("focus",desiredFocus.Invoke(observation,null));
                CheckPose("construction after live resize",true);
                observation.SetConstructionView(false);Set("focus",desiredFocus.Invoke(observation,null));
                CheckPose("leaving construction after live resize",true);
            }
            Set("distance",Read<float>("maxDistance")*8);
            CheckPose("stale interpolated distance from old viewport",true);
            report.AppendLine("PASS far-stop resize, specimen/environment changes and construction never retain an excessive camera distance");

            // A stale or externally authored focus must not bypass the final physical guard.
            Set("focus",new Vector3(side.bounds.max.x-2,observation.GroundHeight+1,room.windowCenter.z+8));
            Set("azimuth",250f);Set("elevation",78f);Set("distance",1.25f);
            CheckPose("out-of-room focus",false);
            report.AppendLine("PASS final pose guard also contains an externally displaced focus");
            report.AppendLine($"COMPLETE PASS: {poses} camera poses, {checks} assertions");
            return checks;
        }
        catch(Exception error){report.AppendLine("FAIL "+error);throw;}
        finally
        {
            snapshot.Restore();
            Directory.CreateDirectory(HusbandryPolishValidation.Output);
            File.WriteAllText(HusbandryPolishValidation.Output+"/CameraBounds.txt",report.ToString());
        }
    }

    public static IEnumerator CaptureMaximumZoom(CampaignObservationCamera observation,Camera camera,RenderTexture frame)
    {
        if(!observation||!camera||!frame)throw new ArgumentNullException(nameof(observation));
        var snapshot=new CameraState(observation,camera);
        var fields=snapshot.fields;
        void Set(string name,object value)=>fields.Single(f=>f.Name==name).SetValue(observation,value);
        float Read(string name)=>(float)fields.Single(f=>f.Name==name).GetValue(observation);
        var apply=typeof(CampaignObservationCamera).GetMethod("ApplyPose",Fields);
        var desiredFocus=typeof(CampaignObservationCamera).GetMethod("DesiredFocus",Fields);
        var distanceLimits=typeof(CampaignObservationCamera).GetMethod("UpdateDistanceLimits",Fields);
        RenderTexture wideFrame=null;var previousActive=RenderTexture.active;
        try
        {
            // Hold only the observation controller. The live world and its RenderTexture still render normally.
            observation.enabled=false;observation.SetConstructionView(false);
            observation.SetMode(CampaignObservationMode.Environment);observation.ResetView();
            Set("focus",desiredFocus.Invoke(observation,null));
            observation.ApplyOrbitInput(Vector2.zero,-1000);Set("distance",Read("targetDistance"));
            string[] names={"Room-MaxZoomOut","Room-MaxZoomOut-Left","Room-MaxZoomOut-Right"};
            float[] yaws={180,CampaignObservationCamera.MaximumRoomAzimuth,CampaignObservationCamera.MinimumRoomAzimuth};
            for(int i=0;i<names.Length;i++)
            {
                Set("azimuth",yaws[i]);apply.Invoke(observation,null);
                if(observation.Distance>Read("maxDistance")+.001f)throw new InvalidOperationException("Maximum-zoom capture exceeds its room limit.");
                yield return null;yield return new WaitForEndOfFrame();
                HusbandryPolishDriver.Capture(frame,names[i]);
                File.AppendAllText(HusbandryPolishValidation.Output+"/CameraBounds.txt",
                    $"PASS captured {names[i]} from the live world at distance {observation.Distance:F3}, yaw {Read("azimuth"):F2}, aspect {camera.aspect:F3}\n");
            }

            // Render an actual wider viewport rather than stretching the normal world image. Keep
            // these manual renders in one frame so the backdrop cannot rebind its regular target.
            var descriptor=frame.descriptor;descriptor.width=1920;descriptor.height=800;
            descriptor.msaaSamples=1;descriptor.useMipMap=false;descriptor.autoGenerateMips=false;
            wideFrame=new RenderTexture(descriptor){name="Room ultrawide camera verification"};wideFrame.Create();
            camera.targetTexture=wideFrame;camera.aspect=2.4f;Set("aspect",camera.aspect);
            observation.ResetView();distanceLimits.Invoke(observation,null);
            Set("focus",desiredFocus.Invoke(observation,null));
            observation.ApplyOrbitInput(Vector2.zero,-1000);Set("distance",Read("targetDistance"));
            string[] wideNames={"Room-MaxZoomOut-Ultrawide-Left","Room-MaxZoomOut-Ultrawide-Right"};
            float[] wideYaws={CampaignObservationCamera.MaximumRoomAzimuth,CampaignObservationCamera.MinimumRoomAzimuth};
            for(int i=0;i<wideNames.Length;i++)
            {
                Set("azimuth",wideYaws[i]);apply.Invoke(observation,null);
                if(observation.Distance>Read("maxDistance")+.001f)throw new InvalidOperationException("Ultrawide capture exceeds its room zoom limit.");
                camera.Render();HusbandryPolishDriver.Capture(wideFrame,wideNames[i]);
                File.AppendAllText(HusbandryPolishValidation.Output+"/CameraBounds.txt",
                    $"PASS captured {wideNames[i]} at 1920x800, distance {observation.Distance:F3}, yaw {Read("azimuth"):F2}, aspect {camera.aspect:F3}, far clip {camera.farClipPlane:F2}\n");
            }
        }
        finally
        {
            snapshot.Restore();RenderTexture.active=previousActive;
            if(wideFrame){wideFrame.Release();UnityEngine.Object.Destroy(wideFrame);}
        }
    }

    sealed class CameraState
    {
        readonly CampaignObservationCamera observation;readonly Camera camera;
        public readonly FieldInfo[] fields;readonly object[] values;
        readonly bool enabled,orthographic;readonly float aspect,fieldOfView,near,far,orthographicSize;
        readonly Vector3 position;readonly Quaternion rotation;
        readonly RenderTexture targetTexture;
        readonly DepthOfField depth;readonly bool depthActive;
        readonly UnityEngine.Rendering.MinFloatParameter[] depthParameters;
        readonly float[] depthValues;readonly bool[] depthOverrides;
        public CameraState(CampaignObservationCamera observation,Camera camera)
        {
            this.observation=observation;this.camera=camera;enabled=observation.enabled;
            fields=typeof(CampaignObservationCamera).GetFields(Fields).Where(f=>!f.IsInitOnly).ToArray();
            values=fields.Select(f=>f.GetValue(observation)).ToArray();
            aspect=camera.aspect;fieldOfView=camera.fieldOfView;near=camera.nearClipPlane;far=camera.farClipPlane;
            orthographic=camera.orthographic;orthographicSize=camera.orthographicSize;
            position=camera.transform.position;rotation=camera.transform.rotation;
            targetTexture=camera.targetTexture;
            depth=(DepthOfField)fields.Single(f=>f.Name=="depthOfField").GetValue(observation);
            depthActive=depth&&depth.active;
            if(depth)
            {
                depthParameters=new[]{depth.gaussianStart,depth.gaussianEnd,depth.focusDistance};
                depthValues=depthParameters.Select(p=>p.value).ToArray();depthOverrides=depthParameters.Select(p=>p.overrideState).ToArray();
            }
        }
        public void Restore()
        {
            for(int i=0;i<fields.Length;i++)fields[i].SetValue(observation,values[i]);
            camera.targetTexture=targetTexture;camera.aspect=aspect;camera.fieldOfView=fieldOfView;camera.nearClipPlane=near;camera.farClipPlane=far;
            camera.orthographic=orthographic;camera.orthographicSize=orthographicSize;
            camera.transform.SetPositionAndRotation(position,rotation);
            if(depth)
            {
                depth.active=depthActive;
                for(int i=0;i<depthParameters.Length;i++){depthParameters[i].value=depthValues[i];depthParameters[i].overrideState=depthOverrides[i];}
            }
            observation.enabled=enabled;
        }
    }
}
