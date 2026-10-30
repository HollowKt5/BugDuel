using UnityEngine;

namespace BeetleArena
{
    /// <summary>Fits the grounded feet only. The root and world-space anchors remain owned by the motor and step planner.</summary>
    public sealed class CareSupportPose
    {
        public Vector3 Normal { get; private set; }=Vector3.up;
        float height;
        public void Reset() { Normal=Vector3.up;height=0; }

        public void Apply(BeetleProceduralAnimator rig,CareFootPlant feet,float dt,float scale)
        {
            if(feet.SupportCount<3)return;
            Vector3 center=Vector3.zero;
            for(int i=0;i<rig.legs.Length;i++)if(feet.IsSupport(i))
                center+=feet.Target(i)-Vector3.up*(rig.legs[i].soleOffset*scale+.003f);
            center/=feet.SupportCount;
            float xx=0,xz=0,zz=0,xy=0,zy=0;
            for(int i=0;i<rig.legs.Length;i++)if(feet.IsSupport(i))
            {
                var d=feet.Target(i)-Vector3.up*(rig.legs[i].soleOffset*scale+.003f)-center;
                xx+=d.x*d.x;xz+=d.x*d.z;zz+=d.z*d.z;xy+=d.x*d.y;zy+=d.z*d.y;
            }
            float determinant=xx*zz-xz*xz;
            float slopeX=0,slopeZ=0;
            if(determinant>1e-10f)
            { slopeX=(xy*zz-zy*xz)/determinant;slopeZ=(zy*xx-xy*xz)/determinant; }
            var normal=new Vector3(-slopeX,1,-slopeZ).normalized;
            normal=Vector3.RotateTowards(Vector3.up,normal,12*Mathf.Deg2Rad,0);
            var body=rig.bodyPivot;
            float plane=center.y+slopeX*(body.position.x-center.x)+slopeZ*(body.position.z-center.z);
            float desiredHeight=Mathf.Clamp(plane-feet.GroundHeight,-.08f*scale,.16f*scale);
            float blend=1-Mathf.Exp(-Mathf.Max(0,dt)*10);
            Normal=Vector3.Slerp(Normal,normal,blend);height=Mathf.Lerp(height,desiredHeight,blend);
            body.rotation=Quaternion.FromToRotation(Vector3.up,Normal)*body.rotation;
            body.position+=Vector3.up*height;
        }
    }
}
