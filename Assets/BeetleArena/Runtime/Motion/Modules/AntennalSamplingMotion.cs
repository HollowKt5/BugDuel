using UnityEngine;
namespace BeetleArena
{
    [CreateAssetMenu(menuName = "Beetle Arena/Motions/Antennal Sampling")]
    public sealed class AntennalSamplingMotion : BeetleMotionModule
    {
        [Range(0, 30)] public float scanYaw = 18;
        [Range(0, 20)] public float scanPitch = 9;
        public override string DisplayName => "触角间歇探测";
        public override BeetleMotionChannel Channel => BeetleMotionChannel.SupportOverlay;
        public override bool AppliesContactResponse => false;
        public override void SetDefaults() => evidence = "Context-dependent active tactile sampling: Krause & Durr 2012, doi:10.3389/fnbeh.2012.00030. Stick-insect evidence; beetle angles, pauses and defensive fold are artistic approximations, not measured Trypoxylus kinematics.";
        public override bool CanStart(in BeetleMotionContext c) => !c.Fallen || c.OnFloor;
        public override BeetleMotionSample Sample(in BeetleMotionContext c, float phase) => default;
        public override bool SampleAntenna(in BeetleMotionContext c, float phase, int slot, out Vector3 angles)
        {
            // 固定窗口的节奏不随上下文缩放时间，避免接触时频率突变造成相位瞬跳。
            float sweep = InsectMotionTiming.Bout(c.Time + slot * .19f, 1.17f, 31 + slot * 17);
            float inspect = InsectMotionTiming.Bout(c.Time + slot * .07f, .57f, 73 + slot * 11);
            float side = slot % 2 == 0 ? -1 : 1;
            bool loaded = c.Grounded && (c.Committed || c.Touching);
            float range = c.Fallen ? .65f : loaded ? .32f : 1;
            angles = new Vector3(scanPitch * (sweep - .35f * inspect) * range + (loaded ? 7 : 0),
                side * scanYaw * (sweep - .25f * inspect) * range, 0);
            return true;
        }
    }
}
