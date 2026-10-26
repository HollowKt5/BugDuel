using UnityEngine;

namespace BeetleArena
{
    /// <summary>Dual fixed-step pose snapshots. Display interpolation only mutates Transforms; authoritative pose is restored before next simulation step.</summary>
    public sealed class BeetlePoseBuffer
    {
        readonly Transform[] bones;
        readonly BeetleLocalPose[] previous, current;

        public BeetlePoseBuffer(Transform model)
        {
            bones = model.GetComponentsInChildren<Transform>(true);
            previous = new BeetleLocalPose[bones.Length]; current = new BeetleLocalPose[bones.Length];
            for (int i = 0; i < bones.Length; i++) previous[i] = current[i] = new BeetleLocalPose(bones[i]);
        }

        // Invoked prior to AI/IK fixed step to prevent intermediate poses from the previous visual frame from entering collision queries.
        public void BeginStep()
        {
            for (int i = 0; i < bones.Length; i++)
            {
                previous[i] = current[i];
                current[i].Apply(bones[i]);
            }
        }

        public void Capture()
        {
            for (int i = 0; i < bones.Length; i++) current[i] = new BeetleLocalPose(bones[i]);
        }

        // Blends between two established results without advancing walkCycle or gait timers; arrays allocated per match, zero per-frame heap allocations.
        public void Render(float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            for (int i = 0; i < bones.Length; i++)
            {
                BeetleLocalPose.Blend(bones[i], previous[i], current[i], alpha);
            }
        }

        // Read-only reference allowing this Actor's contact buffer to reuse hierarchy enumeration; consumers must not mutate this array.
        internal Transform[] Bones => bones;
    }
}
