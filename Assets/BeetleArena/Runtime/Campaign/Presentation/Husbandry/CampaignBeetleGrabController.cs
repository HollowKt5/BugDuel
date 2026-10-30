using System.Collections.Generic;
using BugDuel.Campaign;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace BeetleArena.Campaign
{
    public enum CarePointerAction { None, Beetle, FeedingDish }

    public sealed class CampaignBeetleGrabController : MonoBehaviour
    {
        public const float HoldSeconds=.7f;
        public bool IsHolding { get; private set; }
        public bool IsManipulating => IsHolding||landing;
        public bool IsCharging => pending&&canLift;
        public float HoldProgress => IsCharging?Mathf.Clamp01((Time.unscaledTime-pressedAt)/HoldSeconds):0;
        public CampaignGrabProgressRing ChargeIndicator => chargeIndicator;
        public int PickupCount { get; private set; }
        public bool BackConsumedThisFrame => cancelledFrame==Time.frameCount;
        public string CurrentBeetleId { get; private set; }
        public float StruggleWeight { get; private set; }
        public Vector3 HeldWorldPosition => specimen.Enclosure.ToWorld(heldPoint)+Vector3.up*(specimen.GroundOffset+lift);
        public Vector2 GroundPoint => heldPoint;

        CampaignSpecimen specimen;
        CampaignConstructionController construction;
        CampaignBackdrop backdrop;
        CampaignApp app;
        CampaignCareController.Resident resident;
        CareWalkArea heldArea;
        Vector2 startPoint,heldPoint,pressPoint,lastHeldPointer;
        Vector3 gripOffset,pressedGripLocal;
        float gripHeight;
        float pressedAt,heldTime,nextStruggle,struggleStart=-10,struggleDuration,struggleStrength,struggleRhythm,lift,fallSpeed;
        bool pending,canLift,landing,pointerMoved;
        CarePointerAction pressedTarget;
        string pressedBeetle;
        Texture2D openCursor,closedCursor;
        CampaignGrabProgressRing chargeIndicator;
        int cursorState;
        int cancelledFrame=-1;
        uint random;
        readonly List<RaycastResult> hits=new List<RaycastResult>();
        readonly List<Renderer> hitRenderers=new List<Renderer>(64);
        PointerEventData pointer;
        EventSystem pointerEvents;

        public static CampaignBeetleGrabController Ensure(CampaignSpecimen animal,CampaignConstructionController builder,CampaignBackdrop world)
        {
            var result=animal.GetComponent<CampaignBeetleGrabController>();
            if(!result)result=animal.gameObject.AddComponent<CampaignBeetleGrabController>();
            result.specimen=animal;result.construction=builder;result.backdrop=world;result.app=CampaignApp.Instance;animal.Grab=result;
            return result;
        }
        CampaignBeetle Selected => app?.Session?.State.beetles.Find(b=>b.id==app.Session.State.selectedId);
        bool CanRead => isActiveAndEnabled&&app&&app.Session!=null&&!app.Busy&&!app.Modal&&!app.NeedsPlayerName&&!app.Constructing&&
            construction&&!construction.Editing&&backdrop&&!backdrop.UiFocused&&specimen&&specimen.isActiveAndEnabled&&!specimen.ConstructionHidden;
        // Repositioning a visible resident does not change its reserved match, listing or auction.
        bool CanLift(CampaignBeetle beetle) => CanRead&&beetle!=null&&beetle.Adult(app.Session.State.week)&&!beetle.overturned&&
            specimen.Enclosure!=null&&specimen.Enclosure.Area.CanWalk&&specimen.Care?.Find(beetle.id)!=null;

        public CarePointerAction TickPointer()
        {
            var mouse=Mouse.current;var beetle=Selected;
            if(!CanRead||!Application.isFocused||mouse==null)
            {Cancel();return CarePointerAction.None;}
            var screen=mouse.position.ReadValue();bool overUi=PointerOverUi(screen);
            if(landing)
            {
                AdvancePresentation(Mathf.Min(Time.unscaledDeltaTime,.05f));
                return CarePointerAction.None;
            }
            bool backPressed=app.Controls!=null&&app.Controls["Back"].WasPressedThisFrame();
            if(backPressed&&(IsHolding||IsCharging))
            {cancelledFrame=Time.frameCount;Cancel();return CarePointerAction.None;}
            if(IsHolding)
            {
                if(!CanLift(beetle)||beetle.id!=CurrentBeetleId||overUi&&mouse.leftButton.isPressed)
                {Cancel();return CarePointerAction.None;}
                // Screen movement is independent of the lift animation, camera and corrected world position.
                pointerMoved=mouse.leftButton.isPressed&&(screen-lastHeldPointer).sqrMagnitude>=1;
                if(pointerMoved)lastHeldPointer=screen;
                if(!overUi&&construction.TryPointerRay(screen,out var ray)&&new Plane(Vector3.up,Vector3.up*(gripHeight+lift)).Raycast(ray,out var distance))
                    MoveHeldTo(ray.GetPoint(distance)+gripOffset);
                if(!mouse.leftButton.isPressed)Release();
                AdvancePresentation(Mathf.Min(Time.unscaledDeltaTime,.05f));
                return CarePointerAction.None;
            }
            if(overUi||!construction.TryPointerRay(screen,out var hitRay))
            {ResetPress();SetCursor(0);return CarePointerAction.None;}
            var target=HitTarget(hitRay);Vector3 backPoint=default;
            bool back=target==CarePointerAction.Beetle&&CanLift(beetle)&&TryBackHit(hitRay,out backPoint);
            SetCursor(back||IsCharging?1:0);
            if(mouse.leftButton.wasPressedThisFrame)
            {
                pending=target!=CarePointerAction.None;pressedTarget=target;canLift=back;
                pressPoint=screen;pressedAt=Time.unscaledTime;pressedBeetle=beetle?.id;
                if(back)pressedGripLocal=specimen.transform.InverseTransformPoint(backPoint);
            }
            if(!pending)return CarePointerAction.None;
            if(pressedBeetle!=beetle?.id||(screen-pressPoint).sqrMagnitude>=144||canLift&&!CanLift(beetle))
            {ResetPress();return CarePointerAction.None;}
            if(mouse.leftButton.wasReleasedThisFrame)
            {
                var action=target==pressedTarget?pressedTarget:CarePointerAction.None;
                ResetPress();return action;
            }
            if(!mouse.leftButton.isPressed){ResetPress();return CarePointerAction.None;}
            // Retain the point grabbed on press while the animal walks during the hold delay.
            if(canLift)
            {
                ShowCharge(screen,HoldProgress);
                if(HoldProgress>=1)
                {
                    if(TryBeginHold())
                    {
                        var grip=specimen.transform.TransformPoint(pressedGripLocal);
                        gripHeight=grip.y;gripOffset=specimen.transform.position-grip;gripOffset.y=0;
                    }
                    else ResetPress();
                }
            }
            return CarePointerAction.None;
        }

        public CarePointerAction HitTarget(Ray ray)
        {
            var dish=specimen.FeedingDish;
            if(dish&&dish.bounds.IntersectRay(ray,out float dishDistance))
            {
                // Thin legs and long horns have loose bounding boxes; they must not steal a visible dish click.
                return TryBackHit(ray,out var back)&&Vector3.Distance(ray.origin,back)<dishDistance?
                    CarePointerAction.Beetle:CarePointerAction.FeedingDish;
            }
            float nearest=float.PositiveInfinity;bool animal=false;
            var stage=specimen.LifeStageDisplay;
            if(stage&&stage.HasStageEnvironment&&!stage.UsesAdultModel)
            {
                if(stage.ObservationBounds.IntersectRay(ray,out float distance)){nearest=distance;animal=true;}
            }
            else
            {
                specimen.GetComponentsInChildren(false,hitRenderers);
                foreach(var renderer in hitRenderers)
                    if(renderer.enabled&&renderer.gameObject.activeInHierarchy&&renderer.bounds.IntersectRay(ray,out float distance)&&distance<nearest)
                    {nearest=distance;animal=true;}
            }
            return animal?CarePointerAction.Beetle:CarePointerAction.None;
        }
        public bool HitBack(Ray ray)=>TryBackHit(ray,out _);
        bool TryBackHit(Ray ray,out Vector3 grip)
        {
            grip=default;float closest=float.PositiveInfinity;
            var rig=specimen.rig;if(!rig||rig.shellRenderers==null)return false;
            foreach(var renderer in rig.shellRenderers)
            {
                if(!renderer||!renderer.enabled||!renderer.gameObject.activeInHierarchy||renderer.transform.IsChildOf(rig.headPivot))continue;
                // Oriented shell bounds omit the legs and horn; only the upper central shell is grabbable.
                var localRay=new Ray(renderer.transform.InverseTransformPoint(ray.origin),renderer.transform.InverseTransformVector(ray.direction));
                var bounds=renderer.localBounds;bounds.extents=Vector3.Scale(bounds.extents,new Vector3(.85f,1,.85f));
                if(!bounds.IntersectRay(localRay,out float distance))continue;
                var point=renderer.transform.TransformPoint(localRay.GetPoint(distance));
                float worldDistance=Vector3.Distance(ray.origin,point);
                if(point.y>=renderer.bounds.center.y&&worldDistance<closest){closest=worldDistance;grip=point;}
            }
            return float.IsFinite(closest);
        }

        public bool TryBeginHold()
        {
            var beetle=Selected;if(IsManipulating||!CanLift(beetle))return false;
            resident=specimen.Care.Find(beetle.id);CurrentBeetleId=beetle.id;
            heldArea=specimen.Enclosure.Area;
            startPoint=heldPoint=resident.Motor.Position;resident.Motor.PlaceAt(startPoint);specimen.Care.Hold(beetle.id);
            IsHolding=true;landing=false;heldTime=lift=fallSpeed=StruggleWeight=0;struggleStart=-10;
            app.HandlingBeetle=true;
            gripHeight=specimen.rig.bodyPivot.position.y;gripOffset=Vector3.zero;
            PickupCount++;random=CareLocomotion.Seed(beetle.id)^0x57231u^unchecked((uint)PickupCount*0x9e3779b9u);
            if(random==0)random=0x57231u;
            lastHeldPointer=Mouse.current?.position.ReadValue()??pressPoint;
            pointerMoved=false;nextStruggle=0;struggleDuration=1;
            ResetPress();SetCursor(2);return true;
        }
        public void MoveHeldTo(Vector3 world)
        {
            if(!IsHolding)return;
            heldPoint=ResolveTarget(specimen.Enclosure.Area,heldPoint,specimen.Enclosure.ToLocal(world));
        }
        public static Vector2 ResolveTarget(CareWalkArea area,Vector2 current,Vector2 requested)
        {
            if(!float.IsFinite(requested.x)||!float.IsFinite(requested.y))return area.KeepInside(current);
            current=area.KeepInside(current);var goal=area.KeepInside(requested);
            if(area.SegmentClear(current,goal))return goal;
            float low=0,high=1;
            for(int i=0;i<16;i++)
            {
                float middle=(low+high)*.5f;
                if(area.SegmentClear(current,Vector2.Lerp(current,goal,middle)))low=middle;else high=middle;
            }
            return Vector2.Lerp(current,goal,low*.995f);
        }
        public void Release()
        {
            if(!IsHolding)return;
            ResetPress();SetCursor(0);
            IsHolding=false;landing=true;StruggleWeight=0;fallSpeed=0;
            pointerMoved=false;
            heldPoint=specimen.Enclosure.Area.KeepInside(heldPoint);resident.Motor.PlaceAt(heldPoint);
            resident.Brain.InvalidateArrival();
        }
        public void AdvancePresentation(float dt)
        {
            if(!IsManipulating||dt<=0)return;
            if(IsHolding)
            {
                heldTime+=dt;lift=Mathf.MoveTowards(lift,.72f,dt*3.6f);
                if(pointerMoved&&heldTime>=nextStruggle)
                {
                    struggleStart=heldTime;struggleDuration=.65f+Roll()*.55f;
                    struggleStrength=.8f+Roll()*.2f;struggleRhythm=2.2f+Roll()*1.6f;
                    nextStruggle=heldTime+struggleDuration+.45f+Roll()*1.15f;
                }
                pointerMoved=false;
                // Pointer movement starts a bout; stopping the pointer lets that bout finish.
                // Only a new movement after the cooldown may start the next one.
                float t=(heldTime-struggleStart)/struggleDuration;
                float attack=Mathf.SmoothStep(0,1,Mathf.Clamp01(t/.12f));
                float release=1-Mathf.SmoothStep(0,1,Mathf.Clamp01((t-.6f)/.4f));
                float pulse=.68f+.32f*Mathf.Pow(Mathf.Max(0,Mathf.Sin(t*Mathf.PI*2*struggleRhythm)),2);
                StruggleWeight=t>=0&&t<1?attack*release*pulse*struggleStrength:0;
            }
            else
            {
                fallSpeed+=dt*5;lift=Mathf.Max(0,lift-fallSpeed*dt);
                if(lift<=0)Finish(false);
            }
        }
        public void Cancel()
        {
            ResetPress();
            // A released beetle has already committed its safe landing point.
            if(IsManipulating)Finish(!landing);else SetCursor(0);
        }
        void ResetPress()
        {
            pending=canLift=false;pressedBeetle=null;
            if(chargeIndicator)chargeIndicator.gameObject.SetActive(false);
        }
        void ShowCharge(Vector2 screen,float progress)
        {
            var canvas=backdrop.output.canvas.rootCanvas;
            var host=(RectTransform)canvas.transform;
            var eventCamera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
            if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(host,screen,eventCamera,out var local))return;
            if(!chargeIndicator)
            {
                var node=new GameObject("BeetleGrabProgress",typeof(RectTransform),typeof(CanvasRenderer));
                node.transform.SetParent(host,false);
                chargeIndicator=node.AddComponent<CampaignGrabProgressRing>();
                var group=node.AddComponent<CanvasGroup>();group.interactable=group.blocksRaycasts=false;
                chargeIndicator.rectTransform.anchorMin=chargeIndicator.rectTransform.anchorMax=host.pivot;
                chargeIndicator.rectTransform.pivot=new Vector2(.5f,.5f);
                chargeIndicator.rectTransform.sizeDelta=new Vector2(48,48);
            }
            var center=local+new Vector2(38,-38);const float margin=28;
            center.x=Mathf.Clamp(center.x,host.rect.xMin+margin,host.rect.xMax-margin);
            center.y=Mathf.Clamp(center.y,host.rect.yMin+margin,host.rect.yMax-margin);
            chargeIndicator.rectTransform.anchoredPosition=center;chargeIndicator.Progress=progress;
            chargeIndicator.gameObject.SetActive(true);chargeIndicator.transform.SetAsLastSibling();
        }
        internal void ValidateState()
        {
            var beetle=Selected;
            if(IsManipulating&&(!CanLift(beetle)||beetle.id!=CurrentBeetleId||heldArea!=specimen.Enclosure.Area))Cancel();
        }
        void Finish(bool cancelled)
        {
            if(resident!=null&&specimen&&specimen.Enclosure!=null)
            {
                resident.Motor.PlaceAt(cancelled?startPoint:heldPoint);
                if(!cancelled)resident.Brain.InvalidateArrival();
            }
            if(specimen&&specimen.Care)specimen.Care.Release(CurrentBeetleId);
            if(app)app.HandlingBeetle=false;
            IsHolding=landing=false;lift=StruggleWeight=0;CurrentBeetleId=null;resident=null;heldArea=null;
            pointerMoved=false;
            if(specimen)specimen.RestoreAfterGrab();SetCursor(0);
        }
        float Roll(){uint x=random;x^=x<<13;x^=x>>17;x^=x<<5;random=x;return x/(float)uint.MaxValue;}
        bool PointerOverUi(Vector2 screen)
        {
            var events=EventSystem.current;if(!events)return false;
            if(pointer==null||pointerEvents!=events){pointer=new PointerEventData(events);pointerEvents=events;}
            pointer.position=screen;hits.Clear();events.RaycastAll(pointer,hits);return hits.Count>0;
        }
        void SetCursor(int value)
        {
            if(cursorState==value)return;cursorState=value;
            if(value==0){Cursor.SetCursor(null,Vector2.zero,CursorMode.Auto);return;}
            if(!openCursor)openCursor=CreateHand(false);if(!closedCursor)closedCursor=CreateHand(true);
            Cursor.SetCursor(value==1?openCursor:closedCursor,new Vector2(25,24),CursorMode.Auto);
        }
        static Texture2D CreateHand(bool closed)
        {
            const int size=48;var pixels=new Color32[size*size];
            var ink=new Color32(35,40,36,255);var paper=new Color32(255,255,250,255);var cuff=new Color32(202,210,200,255);
            float Segment(Vector2 p,float ax,float ay,float bx,float by,float radius)
            {
                var a=new Vector2(ax,ay);var line=new Vector2(bx-ax,by-ay);
                return Vector2.Distance(p,a+line*Mathf.Clamp01(Vector2.Dot(p-a,line)/line.sqrMagnitude))-radius;
            }
            Color Sample(Vector2 p)
            {
                float shape=Mathf.Min(Segment(p,25,28,26,34,10),Segment(p,25,35,25,39,6));
                float crease=Segment(p,21,39,30,39,.65f);
                if(closed)
                {
                    shape=Mathf.Min(shape,Segment(p,17,20,18,27,3.8f));
                    shape=Mathf.Min(shape,Segment(p,24,17,24,26,3.7f));
                    shape=Mathf.Min(shape,Segment(p,31,18,31,27,3.5f));
                    shape=Mathf.Min(shape,Segment(p,37,22,36,30,3.1f));
                    shape=Mathf.Min(shape,Segment(p,13,31,28,29,4));
                    crease=Mathf.Min(crease,Segment(p,20.5f,21,20.5f,25,.65f));
                    crease=Mathf.Min(crease,Segment(p,27.5f,20,27.5f,25,.65f));
                    crease=Mathf.Min(crease,Segment(p,34,23,34,27,.65f));
                    crease=Mathf.Min(crease,Segment(p,16,27,28,26.5f,.8f));
                    crease=Mathf.Min(crease,Segment(p,28,27,30,31,.8f));
                }
                else
                {
                    shape=Mathf.Min(shape,Segment(p,18,28,17,11,3.2f));
                    shape=Mathf.Min(shape,Segment(p,25,26,25,7,3.4f));
                    shape=Mathf.Min(shape,Segment(p,32,28,33,10,3.1f));
                    shape=Mathf.Min(shape,Segment(p,37,30,40,18,2.8f));
                    shape=Mathf.Min(shape,Segment(p,18,34,9,25,3.8f));
                    crease=Mathf.Min(crease,Segment(p,17,30,21,35,.7f));
                    crease=Mathf.Min(crease,Segment(p,23,30,32,29,.65f));
                }
                if(shape>2.4f)return Color.clear;
                if(shape>1.5f)return paper;
                if(shape>0||crease<0&&shape<-1)return ink;
                return p.y>39?cuff:paper;
            }
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                var sum=Color.clear;
                for(int sy=0;sy<2;sy++)for(int sx=0;sx<2;sx++)
                {
                    var sample=Sample(new Vector2(x+(sx+.5f)/2,y+(sy+.5f)/2));
                    sum.r+=sample.r*sample.a;sum.g+=sample.g*sample.a;sum.b+=sample.b*sample.a;sum.a+=sample.a;
                }
                if(sum.a>0)pixels[(size-1-y)*size+x]=new Color(sum.r/sum.a,sum.g/sum.a,sum.b/sum.a,sum.a/4);
            }
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false){name=closed?"Beetle held hand":"Beetle grab hand",filterMode=FilterMode.Bilinear,hideFlags=HideFlags.HideAndDontSave};
            texture.SetPixels32(pixels);texture.Apply(false,false);return texture;
        }
        void OnApplicationFocus(bool focused){if(!focused)Cancel();}
        void OnDisable()=>Cancel();
        void OnDestroy(){Cancel();if(chargeIndicator)Destroy(chargeIndicator.gameObject);if(openCursor)Destroy(openCursor);if(closedCursor)Destroy(closedCursor);}
    }
}
