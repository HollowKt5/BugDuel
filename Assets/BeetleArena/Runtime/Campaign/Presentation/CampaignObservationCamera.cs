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
        public CampaignObservationMode Mode { get; private set; }
        public float Elevation => elevation;
        public float Distance => distance;
        public float GroundHeight => groundHeight;
        public Vector3 Focus => focus;
        public bool Dragging => dragging;
        public const float MinimumElevation=18,MaximumElevation=78;
        CampaignBackdrop backdrop;
        Camera targetCamera;
        CampaignSpecimen specimen;
        Renderer[] specimenRenderers;
        Renderer ground;
        Transform environment;
        Bounds environmentBounds;
        Vector3 focus,specimenOffset;
        float azimuth=145,elevation=48,distance,targetDistance,minDistance,maxDistance,groundHeight;
        float specimenRadius=2,aspect;
        bool initialized,dragging;
        string selectedId,habitat;
        CampaignCatalog catalog;
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
            dragging=false;azimuth=145;elevation=Mode==CampaignObservationMode.Environment?48:30;
            MeasureSpecimen();UpdateDistanceLimits();
            targetDistance=FramingDistance(Mode==CampaignObservationMode.Environment?environmentBounds.extents.magnitude:specimenRadius);
            targetDistance=Mathf.Clamp(targetDistance,minDistance,maxDistance);
            // First bind is covered by scene fade; later resets ease to the new composition.
            if(distance<=0){distance=targetDistance;focus=DesiredFocus();ApplyPose();}
        }

        public void RefreshEnvironment()
        {
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
            bool found=false;var bounds=new Bounds(specimen.transform.position,Vector3.zero);
            foreach(var renderer in specimenRenderers)
            {
                if(!renderer||!renderer.gameObject.activeInHierarchy)continue;
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
            maxDistance=Mathf.Max(minDistance+1,FramingDistance(radius)*2.2f);
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
                var point=specimen.transform.TransformPoint(specimenOffset);
                point.y=Mathf.Max(point.y,groundHeight+.3f);return point;
            }
            return environmentBounds.center;
        }

        void LateUpdate()
        {
            if(!initialized||!targetCamera)return;
            var app=CampaignApp.Instance;var session=app?.Session;
            var individual=session?.State.beetles.Find(b=>b.id==session.State.selectedId);
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
            ApplyOrbitInput(dragging&&!mouse.rightButton.wasPressedThisFrame?mouse.delta.ReadValue():Vector2.zero,
                Mathf.Clamp(mouse.scroll.ReadValue().y/120f,-4,4));
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
            azimuth=Mathf.Repeat(azimuth+dragPixels.x*sensitivity,360);
            elevation=Mathf.Clamp(elevation+dragPixels.y*sensitivity,MinimumElevation,MaximumElevation);
            float zoomSensitivity=wheelSteps>0?.20f:.15f;
            targetDistance=Mathf.Clamp(targetDistance*Mathf.Exp(-wheelSteps*zoomSensitivity),minDistance,maxDistance);
        }

        void ApplyPose()
        {
            float yaw=azimuth*Mathf.Deg2Rad,pitch=elevation*Mathf.Deg2Rad;
            var direction=new Vector3(Mathf.Sin(yaw)*Mathf.Cos(pitch),Mathf.Sin(pitch),Mathf.Cos(yaw)*Mathf.Cos(pitch));
            var position=focus+direction*distance;
            // Protect the complete near plane, not only the camera's center, from crossing the soil.
            float halfNearHeight=targetCamera.nearClipPlane*Mathf.Tan(targetCamera.fieldOfView*.5f*Mathf.Deg2Rad);
            float clearance=targetCamera.nearClipPlane+halfNearHeight*Mathf.Max(1,targetCamera.aspect)+.06f;
            position.y=Mathf.Max(position.y,groundHeight+clearance);
            targetCamera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(focus-position,Vector3.up));
            if(depthOfField)
            {
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
