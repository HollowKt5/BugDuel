using UnityEngine;

namespace BeetleArena
{
    /// <summary>
    /// 两只甲虫同步回退同一份姿态提案。求解器可减少动作推进量，再完整检查碰撞。
    /// 起点为上次接受的安全姿态，绝不使用显示帧的姿态；缓存每局分配一次。
    /// </summary>
    public sealed class BeetleContactMotion
    {
        readonly Transform[] bones;
        readonly BeetleLocalPose[] origin, proposed;
        public Vector3 Start { get; private set; }
        public BeetleContactMotion(BeetleActor actor)
        {
            var modelBones=actor.PoseBones;
            bones=new Transform[modelBones.Length+1]; bones[0]=actor.transform;
            modelBones.CopyTo(bones,1);
            origin=new BeetleLocalPose[bones.Length]; proposed=new BeetleLocalPose[bones.Length];
        }
        public void Begin()
        {
            Start=bones[0].position;
            for(int i=0;i<bones.Length;i++)origin[i]=new BeetleLocalPose(bones[i]);
        }
        public void Propose() { for(int i=0;i<bones.Length;i++)proposed[i]=new BeetleLocalPose(bones[i]); }
        public void Apply(float fraction)
        {
            for(int i=0;i<bones.Length;i++)
            {
                BeetleLocalPose.Blend(bones[i], origin[i], proposed[i], fraction);
            }
        }
    }
}
