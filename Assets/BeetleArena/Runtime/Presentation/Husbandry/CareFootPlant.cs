using BugDuel.Domain;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>Care-only tripod stepping and two-bone IK. Support feet are world anchors, never pose waves.</summary>
    public sealed class CareFootPlant
    {
        readonly BeetleProceduralAnimator rig;
        readonly BeetleModelRig imported;
        readonly Vector3[] planted, from, to;
        readonly Quaternion[] frames, startFrames, landingFrames;
        readonly bool[] swinging;
        readonly float[] progress;
        readonly float[] durations,heights,delays;
        readonly CareSupportPose supportPose=new CareSupportPose();
        Vector3 previousPosition,velocity;
        float previousHeading,turnRate,stancePause;
        int nextTripod;
        int groomIndex=-1;
        int nextGroomSide=-1;
        float groomBlend,groomTime,groomPause;
        Vector3 groomBase;
        bool initialized;
        public float GroundHeight { get; set; }
        public System.Func<Vector3,float> SurfaceHeight { get; set; }
        float GroundAt(Vector3 point) => SurfaceHeight?.Invoke(point) ?? GroundHeight;
        public int SupportCount { get; private set; }
        public float MaximumError { get; private set; }
        public Vector3 SupportNormal => supportPose.Normal;
        public bool IsSupport(int index) => initialized&&!swinging[index]&&index!=groomIndex;
        public Vector3 Target(int index) => planted[index];
        public Vector3 Foot(int index) => Binding(index).tip.position;
        public float SoleHeight(int index) => GroundAt(initialized?planted[index]:rig.model.TransformPoint(rig.legs[index].restAnkle))+rig.legs[index].soleOffset*Scale+.003f;
        float Scale => Mathf.Abs(rig.model.lossyScale.x);
        BeetleModelRig.LegBinding Binding(int i) => imported.BindingFor(rig.legs[i]);
        public CareFootPlant(BeetleProceduralAnimator rig,float groundHeight)
        {
            this.rig=rig;imported=rig.importedRig;GroundHeight=groundHeight;
            int n=rig.legs.Length;planted=new Vector3[n];from=new Vector3[n];to=new Vector3[n];frames=new Quaternion[n];startFrames=new Quaternion[n];landingFrames=new Quaternion[n];
            swinging=new bool[n];progress=new float[n];durations=new float[n];heights=new float[n];delays=new float[n];
        }
        public void Reset()
        {
            initialized=false;SupportCount=0;MaximumError=0;groomIndex=-1;groomBlend=groomTime=0;
            groomPause=0;nextGroomSide=-1;
            nextTripod=0;velocity=Vector3.zero;turnRate=stancePause=0;supportPose.Reset();
        }
        public void Present(CareState state,float dt,float speed=0)
        {
            if(!imported)return;
            if(!initialized)
            {
                for(int i=0;i<planted.Length;i++){planted[i]=Nominal(i);frames[i]=rig.model.rotation;swinging[i]=false;}
                previousPosition=rig.model.position;previousHeading=rig.model.eulerAngles.y;
                initialized=true;
            }
            if(dt>0)
            {
                var travel=Vector3.ProjectOnPlane((rig.model.position-previousPosition)/dt,Vector3.up);
                velocity=Vector3.Lerp(velocity,Vector3.ClampMagnitude(travel,.6f),1-Mathf.Exp(-dt*16));
                turnRate=Mathf.Lerp(turnRate,Mathf.Clamp(Mathf.DeltaAngle(previousHeading,rig.model.eulerAngles.y)/dt,-90,90),1-Mathf.Exp(-dt*16));
                previousPosition=rig.model.position;previousHeading=rig.model.eulerAngles.y;
                stancePause=Mathf.Max(0,stancePause-dt);
            }
            bool busy=false;
            for(int i=0;i<swinging.Length;i++)busy|=swinging[i];
            groomPause=Mathf.Max(0,groomPause-dt);
            if(state==CareState.Grooming&&groomIndex<0&&!busy&&groomPause<=0)
            {
                for(int i=0;i<planted.Length;i++)if(rig.legs[i].pair==0&&rig.legs[i].side==nextGroomSide)
                { groomIndex=i;groomBase=planted[i];groomTime=0;nextGroomSide=-nextGroomSide;break; }
            }
            if(groomIndex>=0)
            {
                groomTime+=dt;
                float lift=Mathf.SmoothStep(0,1,groomTime/.22f)*(1-Mathf.SmoothStep(0,1,(groomTime-.76f)/.3f));
                groomBlend=Mathf.MoveTowards(groomBlend,state==CareState.Grooming?lift:0,dt*6);
                float stroke=Mathf.Sin(Mathf.Clamp01((groomTime-.22f)/.54f)*Mathf.PI*4);
                float side=rig.legs[groomIndex].side;
                // One forefoot brushes inward while five feet carry the animal; set it down before swapping sides.
                planted[groomIndex]=groomBase+Scale*groomBlend*(Vector3.up*(.16f+stroke*.025f)-
                    rig.model.right*(side*.10f)+rig.model.forward*(stroke*.035f));
                if(groomBlend<=0&&groomTime>.1f)
                { planted[groomIndex]=groomBase;groomIndex=-1;groomPause=.16f; }
            }
            // The body must actually travel before a walking step is needed. Resting posture,
            // breathing and terrain settling never start an endless series of corrective taps.
            bool moving=velocity.sqrMagnitude>.000025f*Scale*Scale||Mathf.Abs(turnRate)>2;
            if(dt>0&&!busy&&groomIndex<0&&moving)
            {
                float duration=Mathf.Lerp(.40f,.34f,Mathf.Clamp01(speed/Mathf.Max(.001f,.22f*Scale)));
                int chosen=-1;
                for(int pass=0;pass<2&&chosen<0;pass++)
                {
                    int group=(nextTripod+pass)%2;
                    for(int i=0;i<planted.Length;i++)
                    {
                        if(Group(i)!=group)continue;
                        var b=Binding(i);
                        float stride=Mathf.Min(.11f,(b.upperLength+b.lowerLength)*.09f)*Scale;
                        var offset=Vector3.ProjectOnPlane(planted[i]-Nominal(i),Vector3.up);
                        if(offset.magnitude>stride||!CanHold(i,duration+.12f))
                        { chosen=group;break; }
                    }
                }
                // Share the load with all six feet between transfers. Only an impending
                // reach limit may shorten that pause during an unusually fast external turn.
                if(chosen>=0&&(stancePause<=0||!GroupCanHold(chosen,.16f)))
                {
                    duration=SafeSwingDuration(1-chosen,duration);
                    int order=0;
                    for(int i=0;i<planted.Length;i++)if(Group(i)==chosen)
                        BeginStep(i,duration,order++*.018f);
                    nextTripod=1-chosen;
                }
            }
            // A route may turn after a slow swing has already launched. Recheck the
            // supporting tripod against that new motion instead of stretching its bones.
            float swingAdvance=busy?SafeSwingAdvance(dt):1;
            SupportCount=0;MaximumError=0;
            for(int i=0;i<planted.Length;i++)
            {
                if(swinging[i])
                {
                    float advance=Mathf.Max(0,dt-delays[i]);delays[i]=Mathf.Max(0,delays[i]-dt);
                    progress[i]=Mathf.Min(1,progress[i]+advance*swingAdvance/durations[i]);
                    float t=progress[i];
                    // Unweight and lift first, carry the foot forward, then lower it with a
                    // soft landing. This keeps the pushing stance visibly distinct from swing.
                    float smooth=Mathf.SmoothStep(0,1,Mathf.Clamp01((t-.12f)/.76f));
                    float lift=Mathf.SmoothStep(0,1,Mathf.Clamp01(t/.23f))*
                        (1-Mathf.SmoothStep(0,1,Mathf.Clamp01((t-.62f)/.38f)));
                    frames[i]=Quaternion.Slerp(startFrames[i],landingFrames[i],smooth);
                    planted[i]=Vector3.Lerp(from[i],to[i],smooth)+Vector3.up*(lift*heights[i]);
                    planted[i].y=Mathf.Max(planted[i].y,GroundAt(planted[i])+rig.legs[i].soleOffset*Scale+.003f);
                    // The footfall was planned in this orientation, not the body's later heading.
                    if(t>=1){swinging[i]=false;planted[i]=to[i];frames[i]=landingFrames[i];}
                }
                if(!swinging[i]&&i!=groomIndex)SupportCount++;
            }
            if(busy&&SupportCount==planted.Length)
                stancePause=Mathf.Lerp(.13f,.08f,Mathf.Clamp01(speed/Mathf.Max(.001f,.22f*Scale)));
            supportPose.Apply(rig,this,dt,Scale);
            // Lower the thorax if necessary; never lift a support foot to hide an unreachable target.
            float lower=0;
            for(int i=0;i<planted.Length;i++)
            {
                var b=Binding(i);Vector3 ankle=planted[i]-frames[i]*(b.footOffsetInModel*Scale);
                Vector3 delta=ankle-b.hip.position;
                float reach=(b.upperLength+b.lowerLength)*Scale*.997f;
                float vertical=Mathf.Sqrt(Mathf.Max(0,reach*reach-delta.x*delta.x-delta.z*delta.z));
                lower=Mathf.Max(lower,b.hip.position.y-ankle.y-vertical);
            }
            rig.bodyPivot.position-=Vector3.up*Mathf.Max(0,lower);
            for(int i=0;i<planted.Length;i++)Solve(i,planted[i],frames[i]);
        }
        int Group(int i) => (rig.legs[i].pair+(rig.legs[i].side<0?0:1))%2;
        void BeginStep(int i,float duration,float delay=0)
        {
            float lead=Mathf.Min(.65f,duration*1.45f+.05f);
            swinging[i]=true;progress[i]=0;delays[i]=delay;from[i]=planted[i];to[i]=Nominal(i,lead);
            startFrames[i]=frames[i];landingFrames[i]=Quaternion.AngleAxis(turnRate*lead,Vector3.up)*rig.model.rotation;
            float length=Vector3.Distance(from[i],to[i]);
            durations[i]=duration;
            heights[i]=Mathf.Clamp(length*.30f,.025f*Scale,(Mathf.Abs(turnRate)>8?.045f:.065f)*Scale);
        }
        Vector3 PredictedRoot(float seconds)
        {
            float halfAngle=turnRate*Mathf.Deg2Rad*seconds*.5f;
            float arc=Mathf.Abs(halfAngle)<.0001f?1:Mathf.Sin(halfAngle)/halfAngle;
            return rig.model.position+Quaternion.AngleAxis(turnRate*seconds*.5f,Vector3.up)*(velocity*(seconds*arc));
        }
        bool CanHold(int i,float seconds)
        {
            var b=Binding(i);
            Vector3 ankle=planted[i]-frames[i]*(b.footOffsetInModel*Scale);
            float reach=(b.upperLength+b.lowerLength)*Scale*.975f;
            // Test the middle as well as the end: a curved turn can briefly consume more
            // reach than its endpoint. Bone lengths and the model scale set every limit.
            for(int sample=1;sample<=2;sample++)
            {
                float t=seconds*sample*.5f;
                Vector3 hip=PredictedRoot(t)+Quaternion.AngleAxis(turnRate*t,Vector3.up)*(b.hip.position-rig.model.position);
                if((ankle-hip).sqrMagnitude>reach*reach)return false;
            }
            return true;
        }
        bool GroupCanHold(int group,float seconds)
        {
            for(int i=0;i<planted.Length;i++)if(Group(i)==group&&!CanHold(i,seconds))return false;
            return true;
        }
        float SafeSwingDuration(int supportGroup,float desired)
        {
            if(GroupCanHold(supportGroup,desired+.06f))return desired;
            float low=.12f,high=desired;
            for(int iteration=0;iteration<6;iteration++)
            {
                float middle=(low+high)*.5f;
                if(GroupCanHold(supportGroup,middle+.06f))low=middle;else high=middle;
            }
            return low;
        }
        float SafeSwingAdvance(float dt)
        {
            float remaining=0;
            for(int i=0;i<planted.Length;i++)if(swinging[i])
                remaining=Mathf.Max(remaining,(1-progress[i])*durations[i]+delays[i]);
            if(remaining<=dt)return 1;
            float allowed=remaining;
            for(int i=0;i<planted.Length;i++)if(!swinging[i]&&!CanHold(i,allowed+dt))
            {
                float low=0,high=allowed;
                for(int iteration=0;iteration<6;iteration++)
                {
                    float middle=(low+high)*.5f;
                    if(CanHold(i,middle+dt))low=middle;else high=middle;
                }
                allowed=low;
            }
            return Mathf.Clamp(remaining/Mathf.Max(dt,allowed),1,4);
        }
        Vector3 Nominal(int i,float anticipate=0)
        {
            var rotation=Quaternion.AngleAxis(turnRate*anticipate,Vector3.up);
            var root=PredictedRoot(anticipate);
            var target=root+rotation*(rig.model.TransformPoint(rig.legs[i].restAnkle)-rig.model.position);
            target.y=GroundAt(target)+rig.legs[i].soleOffset*Scale+.003f;
            var b=Binding(i);var frame=rotation*rig.model.rotation;
            var hip=root+rotation*(b.hip.position-rig.model.position);
            // Keep planned footfalls inside the measured limb reach, preserving the ground plane.
            var offset=frame*(b.footOffsetInModel*Scale);
            Vector3 delta=target-offset-hip;
            // Reserve extension for body travel while the opposite tripod is in the air.
            float reach=(b.upperLength+b.lowerLength)*Scale*.85f;
            float horizontal=Mathf.Sqrt(Mathf.Max(0,reach*reach-delta.y*delta.y));
            var flat=Vector2.ClampMagnitude(new Vector2(delta.x,delta.z),horizontal);
            target.x=hip.x+offset.x+flat.x;target.z=hip.z+offset.z+flat.y;
            target.y=GroundAt(target)+rig.legs[i].soleOffset*Scale+.003f;
            return target;
        }
        void Solve(int i,Vector3 target,Quaternion frame)
        {
            var b=Binding(i);b.hip.localRotation=b.hipRest;b.knee.localRotation=b.kneeRest;
            Vector3 hip=b.hip.position,ankle=target-frame*(b.footOffsetInModel*Scale),delta=ankle-hip;
            float a=b.upperLength*Scale,c=b.lowerLength*Scale;
            float distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-c)+.0001f,a+c-.0001f);
            Vector3 direction=delta.sqrMagnitude>1e-10f?delta.normalized:Vector3.down;
            Vector3 pole=Vector3.ProjectOnPlane(rig.bodyPivot.TransformDirection(b.bendInBody),direction);
            if(pole.sqrMagnitude<1e-6f)pole=Vector3.ProjectOnPlane(rig.model.right*rig.legs[i].side,direction);
            if(pole.sqrMagnitude<1e-6f)pole=Vector3.ProjectOnPlane(rig.model.forward,direction);
            pole.Normalize();float along=(a*a-c*c+distance*distance)/(2*distance);
            Vector3 knee=hip+direction*along+pole*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            b.hip.rotation=Quaternion.FromToRotation(b.knee.position-hip,knee-hip)*b.hip.rotation;
            b.knee.rotation=Quaternion.FromToRotation(b.ankle.position-b.knee.position,hip+direction*distance-b.knee.position)*b.knee.rotation;
            b.ankle.rotation=frame*b.ankleInModel;
            MaximumError=Mathf.Max(MaximumError,Vector3.Distance(b.tip.position,target));
        }
    }
}
