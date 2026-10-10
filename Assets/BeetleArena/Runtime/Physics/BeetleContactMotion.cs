using UnityEngine;

namespace BeetleArena
{
    /// <summary>
    /// 两只甲虫同步回退同一份姿态提案。求解器可减少动作推进量，再完整检查碰撞。
    /// 起点为上次接受的安全姿态，绝不使用显示帧的姿态；缓存每局分配一次。
    /// </summary>
    public sealed class BeetleContactMotion
    {
        struct Pose
        {
            public Vector3 position, scale;
            public Quaternion rotation;
            public Pose(Transform t) { position=t.localPosition; rotation=t.localRotation; scale=t.localScale; }
        }
        readonly Transform[] bones;
        readonly Pose[] origin, proposed;
        public Vector3 Start { get; private set; }
        public BeetleContactMotion(BeetleActor actor)
        {
            var modelBones=actor.animator.model.GetComponentsInChildren<Transform>(true);
            bones=new Transform[modelBones.Length+1]; bones[0]=actor.transform;
            modelBones.CopyTo(bones,1);
            origin=new Pose[bones.Length]; proposed=new Pose[bones.Length];
        }
        public void Begin()
        {
            Start=bones[0].position;
            for(int i=0;i<bones.Length;i++)origin[i]=new Pose(bones[i]);
        }
        public void Propose() { for(int i=0;i<bones.Length;i++)proposed[i]=new Pose(bones[i]); }
        public void Apply(float fraction)
        {
            for(int i=0;i<bones.Length;i++)
            {
                bones[i].localPosition=Vector3.Lerp(origin[i].position,proposed[i].position,fraction);
                bones[i].localRotation=Quaternion.Slerp(origin[i].rotation,proposed[i].rotation,fraction);
                bones[i].localScale=Vector3.Lerp(origin[i].scale,proposed[i].scale,fraction);
            }
        }
    }
}
