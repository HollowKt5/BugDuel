using System.Collections.Generic;
using BugDuel.Campaign;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BeetleArena.Campaign
{
    public enum CampaignObservationMode { Environment, Specimen }

    /// <summary>Presentation-only orbit camera. It never moves the animal or its care simulation.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class CampaignObservationCamera : MonoBehaviour
    {
        [Min(.1f), Tooltip("1 = previous wheel speed; 10 = ten times faster.")]
        public float zoomSpeedMultiplier = 10f;
        public CampaignObservationMode Mode { get; private set; }
        public float Elevation => elevation;
        public float Distance => distance;
        public int UserInputRevision { get; private set; }
        public float GroundHeight => groundHeight;
        public const float MinimumElevation=18,MaximumElevation=78;
        public const float MinimumRoomAzimuth=140,MaximumRoomAzimuth=220;
        public const float MaximumRoomZoomFactor=1.1f;
        CampaignBackdrop backdrop;
        Camera targetCamera;
        CampaignSpecimen specimen;
        Renderer[] specimenRenderers;
        Renderer ground;
        HusbandryDaylight room;
        Renderer sideWall;
        Transform environment;
        HusbandryGlassEnclosure glassEnclosure;
        int enclosureRevision=-1;
        Bounds environmentBounds;
        Vector3 focus,specimenOffset;
        float azimuth=145,elevation=48,distance,targetDistance,minDistance,maxDistance,groundHeight;
        float specimenRadius=2,aspect;
        bool initialized,dragging;
        string selectedId,habitat;
        CampaignCatalog catalog;
        int lifeStageRevision=-1;
        bool constructionView;
        CampaignObservationMode beforeConstructionMode;
        float beforeConstructionElevation,beforeConstructionAzimuth,beforeConstructionDistance;
        readonly List<RaycastResult> uiHits=new List<RaycastResult>();
        PointerEventData pointer;
        EventSystem pointerEvents;
        Volume focusVolume;
        VolumeProfile focusProfile,previousProfile;
        DepthOfField depthOfField;

        public static CampaignObservationCamera Ensure(CampaignBackdrop world)
        {
            if(!world||!world.sceneCamera)return null;
            var controller=world.GetComponent<CampaignObservationCamera>();
            if(!controller)controller=world.gameObject.AddComponent<CampaignObservationCamera>();
            controller.Bind(world);return controller;
        }

        void Bind(CampaignBackdrop world)
        {
            if(initialized&&backdrop==world)return;
            backdrop=world;targetCamera=world.sceneCamera;
            targetCamera.orthographic=false;targetCamera.fieldOfView=42;
            targetCamera.nearClipPlane=.04f;targetCamera.farClipPlane=Mathf.Max(100,targetCamera.farClipPlane);
            foreach(var candidate in FindObjectsByType<CampaignSpecimen>(FindObjectsSortMode.None))
                if(candidate.selected&&candidate.gameObject.scene==gameObject.scene){specimen=candidate;break;}
            specimenRenderers=specimen?specimen.GetComponentsInChildren<Renderer>(true):null;
            foreach(var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if(renderer.name=="SoilBed"&&renderer.gameObject.scene==gameObject.scene){ground=renderer;environment=renderer.transform.parent;break;}
            foreach(var candidate in FindObjectsByType<HusbandryDaylight>(FindObjectsSortMode.None))
                if(candidate.gameObject.scene==gameObject.scene){room=candidate;break;}
            if(room)
                foreach(var renderer in room.GetComponentsInChildren<Renderer>(true))
                    if(renderer.name=="Side plaster"){sideWall=renderer;break;}
            glassEnclosure=environment?environment.GetComponentInChildren<HusbandryGlassEnclosure>(true):null;
            enclosureRevision=glassEnclosure?glassEnclosure.Revision:-1;
            ConfigureFocus();
            RefreshEnvironment();aspect=targetCamera.aspect;initialized=true;Mode=CampaignObservationMode.Environment;ResetView();
        }

        void ConfigureFocus()
        {
            var toggle=backdrop.GetComponent<CampaignDepthOfFieldToggle>();focusVolume=toggle?toggle.target:null;
            if(!focusVolume)return;
            previousProfile=focusVolume.HasInstantiatedProfile()?focusVolume.profile:null;
            var source=previousProfile?previousProfile:focusVolume.sharedProfile;
            if(!source||!source.TryGet<DepthOfField>(out _))return;
            // Camera focus is runtime state; never write distances into the shared scene asset.
            focusProfile=ScriptableObject.CreateInstance<VolumeProfile>();focusProfile.name="Observation camera focus";
            foreach(var component in source.components)focusProfile.components.Add(Instantiate(component));
            focusProfile.TryGet(out depthOfField);focusVolume.profile=focusProfile;
        }

        public void SetMode(CampaignObservationMode mode)
        {
            if(mode==CampaignObservationMode.Specimen&&!HasSelectedSpecimen())mode=CampaignObservationMode.Environment;
            if(!initialized||Mode==mode)return;
            Mode=mode;ResetView();
        }

        public void ResetView()
        {
            if(!initialized)return;
            dragging=false;azimuth=Mode==CampaignObservationMode.Environment?180:145;elevation=Mode==CampaignObservationMode.Environment?20:30;
            targetCamera.fieldOfView=Mode==CampaignObservationMode.Environment?50:42;
            MeasureSpecimen();UpdateDistanceLimits();
            targetDistance=FramingDistance(Mode==CampaignObservationMode.Environment?environmentBounds.extents.magnitude:specimenRadius);
            targetDistance=Mathf.Clamp(targetDistance,minDistance,maxDistance);
            // First bind is covered by scene fade; later resets ease to the new composition.
            if(distance<=0){distance=targetDistance;focus=DesiredFocus();ApplyPose();}
        }

        public void SetConstructionView(bool value)
        {
            if(!initialized||constructionView==value)return;
            constructionView=value;dragging=false;
            if(value)
            {
                beforeConstructionMode=Mode;beforeConstructionElevation=elevation;
                beforeConstructionAzimuth=azimuth;beforeConstructionDistance=targetDistance;
                Mode=CampaignObservationMode.Environment;elevation=67;
                RefreshEnvironment();UpdateDistanceLimits();targetDistance=Mathf.Clamp(FramingDistance(environmentBounds.extents.magnitude),minDistance,maxDistance);
            }
            else
            {
                Mode=beforeConstructionMode;elevation=beforeConstructionElevation;azimuth=beforeConstructionAzimuth;
                RefreshEnvironment();MeasureSpecimen();UpdateDistanceLimits();targetDistance=Mathf.Clamp(beforeConstructionDistance,minDistance,maxDistance);
            }
        }

        public void RefreshEnvironment()
        {
            var life=specimen?specimen.LifeStageDisplay:null;
            if(life&&life.HasStageEnvironment)
            {
                groundHeight=life.GroundHeight;environmentBounds=life.EnvironmentBounds;return;
            }
            groundHeight=ground?ground.bounds.max.y:0;
            environmentBounds=ground?ground.bounds:new Bounds(Vector3.zero,new Vector3(10,1,8));
            if(environment)
                foreach(var renderer in environment.GetComponentsInChildren<Renderer>())
                    if(renderer!=ground&&renderer.name!="Backdrop"&&renderer.enabled)environmentBounds.Encapsulate(renderer.bounds);
            environmentBounds.Encapsulate(environmentBounds.center+Vector3.up*1.5f);
        }

        void MeasureSpecimen()
        {
            if(!specimen||specimenRenderers==null)return;
            var life=specimen.LifeStageDisplay;
            if(life&&life.HasStageEnvironment)
            { specimenRadius=Mathf.Max(.35f,life.ObservationBounds.extents.magnitude);return; }
            bool found=false;var bounds=new Bounds(specimen.transform.position,Vector3.zero);
            foreach(var renderer in specimenRenderers)
            {
                if(!renderer||!renderer.enabled||!renderer.gameObject.activeInHierarchy)continue;
                if(found)bounds.Encapsulate(renderer.bounds);else{bounds=renderer.bounds;found=true;}
            }
            if(!found)return;
            specimenRadius=Mathf.Max(.5f,bounds.extents.magnitude);
            specimenOffset=specimen.transform.InverseTransformPoint(bounds.center);
        }

        void UpdateDistanceLimits()
        {
            float radius=Mode==CampaignObservationMode.Environment?environmentBounds.extents.magnitude:specimenRadius;
            minDistance=Mathf.Max(1.25f,radius*1.15f);
            // Keep the complete subject in narrow viewports while allowing only a small retreat from the indoor overview.
            float framing=FramingDistance(radius);
            maxDistance=room?Mathf.Max(minDistance,framing)*MaximumRoomZoomFactor:Mathf.Max(minDistance+1,framing*2.2f);
            targetDistance=Mathf.Clamp(targetDistance,minDistance,maxDistance);
            // A smaller subject or wider viewport may immediately tighten this bound; retain the zero-distance first-bind sentinel.
            if(room)distance=Mathf.Min(distance,maxDistance);
        }

        float FramingDistance(float radius) => FramingDistance(radius,targetCamera.aspect);

        float FramingDistance(float radius,float viewAspect)
        {
            float vertical=targetCamera.fieldOfView*.5f*Mathf.Deg2Rad;
            float horizontal=Mathf.Atan(Mathf.Tan(vertical)*Mathf.Max(.1f,viewAspect));
            return radius/Mathf.Sin(Mathf.Min(vertical,horizontal))*1.08f;
        }

        bool HasSelectedSpecimen()
        {
            var state=CampaignApp.Instance?.Session?.State;
            return specimen&&state!=null&&state.beetles.Exists(b=>b.id==state.selectedId);
        }

        Vector3 DesiredFocus()
        {
            if(Mode==CampaignObservationMode.Specimen&&HasSelectedSpecimen())
            {
                var life=specimen.LifeStageDisplay;
                var point=life&&life.HasStageEnvironment?life.ObservationBounds.center:specimen.transform.TransformPoint(specimenOffset);
                point.y=Mathf.Max(point.y,groundHeight+.3f);return point;
            }
            return environmentBounds.center;
        }

        void LateUpdate()
        {
            if(!initialized||!targetCamera)return;
            var app=CampaignApp.Instance;var session=app?.Session;
            var individual=session?.State.beetles.Find(b=>b.id==session.State.selectedId);
            var life=specimen?specimen.LifeStageDisplay:null;
            if(life&&lifeStageRevision!=life.Revision)
            {
                lifeStageRevision=life.Revision;RefreshEnvironment();MeasureSpecimen();UpdateDistanceLimits();
                targetDistance=Mathf.Clamp(FramingDistance(Mode==CampaignObservationMode.Environment?environmentBounds.extents.magnitude:specimenRadius),minDistance,maxDistance);
            }
            else if(life&&life.HasStageEnvironment&&Mode==CampaignObservationMode.Specimen)
            {
                float previousRadius=specimenRadius;MeasureSpecimen();
                targetDistance*=specimenRadius/previousRadius;UpdateDistanceLimits();
            }
            if(glassEnclosure&&enclosureRevision!=glassEnclosure.Revision)
            {
                enclosureRevision=glassEnclosure.Revision;RefreshEnvironment();UpdateDistanceLimits();
                // A taller enclosure changes framing, not the player's orbit or specimen close-up.
                if(Mode==CampaignObservationMode.Environment)
                {
                    targetDistance=Mathf.Clamp(FramingDistance(environmentBounds.extents.magnitude),minDistance,maxDistance);
                    if(constructionView&&beforeConstructionMode==CampaignObservationMode.Environment)beforeConstructionDistance=targetDistance;
                }
            }
            if(individual==null&&Mode==CampaignObservationMode.Specimen){Mode=CampaignObservationMode.Environment;ResetView();}
            if(catalog!=session?.Catalog||habitat!=individual?.habitat)
            {
                catalog=session?.Catalog;habitat=individual?.habitat;RefreshEnvironment();UpdateDistanceLimits();
            }
            if(selectedId!=individual?.id)
            {
                selectedId=individual?.id;MeasureSpecimen();UpdateDistanceLimits();
                if(Mode==CampaignObservationMode.Specimen)targetDistance=Mathf.Clamp(FramingDistance(specimenRadius),minDistance,maxDistance);
            }
            if(!Mathf.Approximately(aspect,targetCamera.aspect))
            {
                float radius=Mode==CampaignObservationMode.Environment?environmentBounds.extents.magnitude:specimenRadius;
                // Refit after canvas resizing; retain zoom relative to the previous aspect.
                targetDistance*=FramingDistance(radius)/FramingDistance(radius,aspect);
                aspect=targetCamera.aspect;UpdateDistanceLimits();
            }
            ReadInput(app);
            targetDistance=Mathf.Clamp(targetDistance,minDistance,maxDistance);
            float blend=1-Mathf.Exp(-12*Mathf.Min(Time.unscaledDeltaTime,.1f));
            focus=Vector3.Lerp(focus,DesiredFocus(),blend);distance=Mathf.Lerp(distance,targetDistance,blend);
            ApplyPose();
        }

        void ReadInput(CampaignApp app)
        {
            var mouse=Mouse.current;
            if(mouse==null||!Application.isFocused||!app||app.Busy||app.Modal||backdrop.UiFocused)
            { dragging=false;return; }
            bool inside=PointerOverWorld(mouse.position.ReadValue());
            if(!inside||!mouse.rightButton.isPressed)dragging=false;
            bool wantsInput=mouse.rightButton.wasPressedThisFrame||dragging||Mathf.Abs(mouse.scroll.ReadValue().y)>.01f;
            if(!inside||!wantsInput)return;
            if(PointerOverUi(mouse.position.ReadValue())){dragging=false;return;}
            if(mouse.rightButton.wasPressedThisFrame)dragging=true;
            var drag=dragging&&!mouse.rightButton.wasPressedThisFrame?mouse.delta.ReadValue():Vector2.zero;
            float wheel=Mathf.Clamp(mouse.scroll.ReadValue().y/120f,-4,4);
            if(drag.sqrMagnitude>.01f||Mathf.Abs(wheel)>.01f)UserInputRevision++;
            ApplyOrbitInput(drag,wheel);
        }

        public bool PointerOverWorld(Vector2 screenPosition)
        {
            if(!backdrop||!backdrop.output)return false;
            var canvas=backdrop.output.canvas;
            return RectTransformUtility.RectangleContainsScreenPoint(backdrop.output.rectTransform,screenPosition,
                canvas&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null);
        }

        bool PointerOverUi(Vector2 position)
        {
            var events=EventSystem.current;if(!events)return false;
            if(pointer==null||pointerEvents!=events){pointer=new PointerEventData(events);pointerEvents=events;}
            pointer.Reset();pointer.position=position;uiHits.Clear();events.RaycastAll(pointer,uiHits);
            foreach(var hit in uiHits)
                if(hit.gameObject&&hit.gameObject!=backdrop.output.gameObject)return true;
            return false;
        }

        /// <summary>Input in screen pixels and normalized wheel detents; useful for replay and validation.</summary>
        public void ApplyOrbitInput(Vector2 dragPixels,float wheelSteps)
        {
            if(!initialized)return;
            float sensitivity=180f/Mathf.Max(300,Screen.height);
            azimuth=LimitAzimuth(azimuth+dragPixels.x*sensitivity);
            elevation=Mathf.Clamp(elevation-dragPixels.y*sensitivity,MinimumElevation,MaximumElevation);
            float zoomSensitivity=(wheelSteps>0?.20f:.15f)*3f*zoomSpeedMultiplier;
            targetDistance=Mathf.Clamp(targetDistance*Mathf.Exp(-wheelSteps*zoomSensitivity),minDistance,maxDistance);
        }

        float LimitAzimuth(float value) => room?Mathf.Clamp(value,MinimumRoomAzimuth,MaximumRoomAzimuth):Mathf.Repeat(value,360);

        void ApplyPose()
        {
            if(room&&maxDistance>0)distance=Mathf.Min(distance,maxDistance);
            azimuth=LimitAzimuth(azimuth);
            elevation=Mathf.Clamp(elevation,MinimumElevation,MaximumElevation);
            float near=targetCamera.nearClipPlane,tangent=Mathf.Tan(targetCamera.fieldOfView*.5f*Mathf.Deg2Rad);
            float nearRadius=near*Mathf.Sqrt(1+tangent*tangent*(1+targetCamera.aspect*targetCamera.aspect));
            float indoorLeft=sideWall?sideWall.bounds.max.x+.06f+nearRadius:float.NegativeInfinity;
            if(room&&sideWall)
            {
                // Preserve framing distance in narrow windows: reduce the left orbit before the camera reaches the wall.
                float horizontalRadius=distance*Mathf.Cos(elevation*Mathf.Deg2Rad);
                float safeYaw=180+Mathf.Asin(Mathf.Clamp01((focus.x-indoorLeft)/Mathf.Max(.001f,horizontalRadius)))*Mathf.Rad2Deg;
                azimuth=Mathf.Min(azimuth,safeYaw);
            }
            float yaw=azimuth*Mathf.Deg2Rad,pitch=elevation*Mathf.Deg2Rad;
            var direction=new Vector3(Mathf.Sin(yaw)*Mathf.Cos(pitch),Mathf.Sin(pitch),Mathf.Cos(yaw)*Mathf.Cos(pitch));
            var position=focus+direction*distance;
            // Protect the complete near plane, not only the camera's center, from crossing the soil.
            float halfNearHeight=targetCamera.nearClipPlane*Mathf.Tan(targetCamera.fieldOfView*.5f*Mathf.Deg2Rad);
            float clearance=targetCamera.nearClipPlane+halfNearHeight*Mathf.Max(1,targetCamera.aspect)+.06f;
            position.y=Mathf.Max(position.y,groundHeight+clearance);
            if(room)
            {
                // The room's window center sits behind its indoor frame. Protect the full near plane after all interpolation and mode changes.
                position.z=Mathf.Min(position.z,room.windowCenter.z-.4f-nearRadius);
                position.x=Mathf.Max(position.x,indoorLeft);
            }
            targetCamera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(focus-position,Vector3.up));
            if(room&&room.sky)
            {
                // The distant sky must survive the larger framing distance used by narrow viewports.
                var skyBounds=room.sky.bounds;
                targetCamera.farClipPlane=Mathf.Max(100,Vector3.Distance(position,skyBounds.center)+skyBounds.extents.magnitude+2);
            }
            if(depthOfField)
            {
                depthOfField.active=Mode==CampaignObservationMode.Specimen;
                float radius=Mode==CampaignObservationMode.Environment?environmentBounds.extents.magnitude:specimenRadius*.7f;
                float focusedDistance=Vector3.Distance(position,focus);
                depthOfField.gaussianStart.Override(focusedDistance+radius);
                depthOfField.gaussianEnd.Override(focusedDistance+radius+Mathf.Max(4,radius));
                depthOfField.focusDistance.Override(focusedDistance);
            }
        }

        void OnDisable(){dragging=false;}
        void OnDestroy()
        {
            if(!focusProfile)return;
            if(focusVolume&&focusVolume.HasInstantiatedProfile()&&focusVolume.profile==focusProfile)focusVolume.profile=previousProfile;
            foreach(var component in focusProfile.components)if(component)Destroy(component);
            Destroy(focusProfile);
        }
    }
}
