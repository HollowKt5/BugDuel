using UnityEngine;

namespace BeetleArena
{
    /// <summary>
    /// Synchronously rolls back a pose proposal across both beetles. The solver can scale down motion advance before running a full collision pass.
    /// The baseline is the last accepted safe pose, never a display frame pose; buffers are allocated once per match.
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
        public void Restore() { for(int i=0;i<bones.Length;i++)origin[i].Apply(bones[i]); }
        public void Apply(float fraction)
        {
            for(int i=0;i<bones.Length;i++)
            {
                BeetleLocalPose.Blend(bones[i], origin[i], proposed[i], fraction);
            }
        }
    }
}
