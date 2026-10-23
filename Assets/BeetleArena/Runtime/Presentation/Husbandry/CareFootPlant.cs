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
        readonly Quaternion[] frames, landingFrames;
        readonly bool[] swinging;
        readonly float[] progress;
        int nextTripod;
        int groomIndex=-1;
        float groomBlend,groomTime;
        Vector3 groomBase;
        bool initialized;
        public float GroundHeight { get; set; }
        public System.Func<Vector3,float> SurfaceHeight { get; set; }
        float GroundAt(Vector3 point) => SurfaceHeight?.Invoke(point) ?? GroundHeight;
        public int SupportCount { get; private set; }
        public float MaximumError { get; private set; }
        public bool IsSupport(int index) => initialized&&!swinging[index]&&index!=groomIndex;
        public Vector3 Target(int index) => planted[index];
        public Vector3 Foot(int index) => Binding(index).tip.position;
        public float SoleHeight(int index) => GroundAt(initialized?planted[index]:rig.model.TransformPoint(rig.legs[index].restAnkle))+rig.legs[index].soleOffset*Scale+.003f;
        float Scale => Mathf.Abs(rig.model.lossyScale.x);
        BeetleModelRig.LegBinding Binding(int i) => imported.bindings[(rig.legs[i].side<0?0:3)+rig.legs[i].pair];
        public CareFootPlant(BeetleProceduralAnimator rig,float groundHeight)
        {
            this.rig=rig;imported=rig.importedRig;GroundHeight=groundHeight;
            int n=rig.legs.Length;planted=new Vector3[n];from=new Vector3[n];to=new Vector3[n];frames=new Quaternion[n];landingFrames=new Quaternion[n];
            swinging=new bool[n];progress=new float[n];
        }
        public void Reset() { initialized=false;SupportCount=0;groomIndex=-1;groomBlend=groomTime=0; }
        public void Present(CareState state,float dt)
        {
            if(!imported)return;
            if(!initialized)
            {
                for(int i=0;i<planted.Length;i++){planted[i]=Nominal(i);frames[i]=rig.model.rotation;swinging[i]=false;}
                initialized=true;
            }
            bool busy=false;
            for(int i=0;i<swinging.Length;i++)busy|=swinging[i];
            if(state==CareState.Grooming&&groomIndex<0&&!busy)
            {
                for(int i=0;i<planted.Length;i++)if(rig.legs[i].pair==0){groomIndex=i;groomBase=planted[i];groomTime=0;break;}
            }
            if(groomIndex>=0)
            {
                groomTime+=dt;groomBlend=Mathf.MoveTowards(groomBlend,state==CareState.Grooming?1:0,dt*4);
                planted[groomIndex]=groomBase+Vector3.up*(groomBlend*(.12f+.025f*Mathf.Sin(groomTime*7)))-rig.model.forward*(groomBlend*.06f);
                if(groomBlend<=0){planted[groomIndex]=groomBase;groomIndex=-1;}
            }
            // Alternate tripods: left front/rear + right middle, then their opposite partners.
            if(!busy&&groomIndex<0)
            {
                int chosen=-1;
                for(int pass=0;pass<2&&chosen<0;pass++)
                {
                    int group=(nextTripod+pass)%2;
                    for(int i=0;i<planted.Length;i++)
                        if(Group(i)==group&&Vector3.Distance(planted[i],Nominal(i))>.06f){chosen=group;break;}
                }
                if(chosen>=0)
                {
                    for(int i=0;i<planted.Length;i++)if(Group(i)==chosen)
                    { swinging[i]=true;progress[i]=0;from[i]=planted[i];to[i]=Nominal(i);landingFrames[i]=rig.model.rotation; }
                    nextTripod=1-chosen;
                }
            }
            SupportCount=0;MaximumError=0;
            for(int i=0;i<planted.Length;i++)
            {
                if(swinging[i])
                {
                    progress[i]=Mathf.Min(1,progress[i]+dt/.12f);
                    float t=progress[i],smooth=t*t*(3-2*t);
                    planted[i]=Vector3.Lerp(from[i],to[i],smooth)+Vector3.up*(Mathf.Sin(Mathf.PI*t)*.055f);
                    planted[i].y=Mathf.Max(planted[i].y,GroundAt(planted[i])+rig.legs[i].soleOffset*Scale+.003f);
                    // The footfall was planned in this orientation, not the body's later heading.
                    if(t>=1){swinging[i]=false;planted[i]=to[i];frames[i]=landingFrames[i];}
                }
                if(!swinging[i]&&i!=groomIndex)SupportCount++;
            }
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
        Vector3 Nominal(int i)
        {
            var target=rig.model.TransformPoint(rig.legs[i].restAnkle);target.y=GroundAt(target)+rig.legs[i].soleOffset*Scale+.003f;
            var b=Binding(i);var frame=rig.model.rotation;
            // Keep planned footfalls inside the measured limb reach, preserving the ground plane.
            var offset=frame*(b.footOffsetInModel*Scale);
            Vector3 delta=target-offset-b.hip.position;
            // Reserve extension for body travel while the opposite tripod is in the air.
            float reach=(b.upperLength+b.lowerLength)*Scale*.85f;
            float horizontal=Mathf.Sqrt(Mathf.Max(0,reach*reach-delta.y*delta.y));
            var flat=Vector2.ClampMagnitude(new Vector2(delta.x,delta.z),horizontal);
            target.x=b.hip.position.x+offset.x+flat.x;target.z=b.hip.position.z+offset.z+flat.y;
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
            b.ankle.rotation=frame;
            MaximumError=Mathf.Max(MaximumError,Vector3.Distance(b.tip.position,target));
        }
    }
}
