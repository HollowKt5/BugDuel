using UnityEngine;
namespace BeetleArena
{
    [CreateAssetMenu(menuName = "Beetle Arena/Motions/Support Adjustment")]
    public sealed class SupportAdjustmentMotion : BeetleMotionModule
    {
        public override void SetDefaults() => evidence = "Support-repositioning animation approximation; amplitudes/timing are artistic, not species-measured combat signals. See TechnicalGuide motion evidence section.";
        static readonly int[] Order = { 0, 4, 2, 3, 1, 5 };
        public override string DisplayName => "静置换足 · 支撑调整";
        public override BeetleMotionChannel Channel => BeetleMotionChannel.SupportOverlay;
        public override bool CanStart(in BeetleMotionContext c) =>
            c.Fallen ? c.OnFloor && c.FloorTime > 1.8f : c.Grounded && c.Speed < .3f && !c.Committed;
        public override BeetleMotionSample Sample(in BeetleMotionContext c, float phase)
        {
            // 小幅重心换侧让支撑调整可见；既非威吓结论，也不暗中产生战斗冲量。
            float ramp = c.Fallen ? Mathf.SmoothStep(0, 1, Mathf.Clamp01((c.FloorTime - 1.8f) / .5f)) : 1;
            return new BeetleMotionSample { BodyRoll = Mathf.Sin(c.Time * 1.7f) * 1.8f * ramp,
                BodyPitch = Mathf.Sin(c.Time * 1.1f) * 1.1f * ramp, BodyHeight = .012f * Mathf.Sin(c.Time * 2) * ramp,
                HeadYaw = Mathf.Sin(c.Time * 1.3f) * 3 * ramp };
        }
        public override bool SampleLeg(in BeetleMotionContext c, float phase, int slot, out Vector3 offset)
        {
            // 每 0.72 秒轮到一条腿，仅前 0.42 秒抬足再放回；同一时刻最多一足离开支撑。
            // 后两腿交错安排，避免连续把同侧支撑抽掉。没有逐帧 Random。
            int n = Mathf.FloorToInt(c.Time / .72f);
            float p = Mathf.Clamp01((c.Time - n * .72f) / .42f);
            float pulse = Pulse(p);
            float ramp = c.Fallen ? Mathf.SmoothStep(0, 1, Mathf.Clamp01((c.FloorTime - 1.8f) / .5f)) : 1;
            offset = new Vector3((slot < 3 ? -1 : 1) * .035f * pulse, .13f * pulse, .055f * pulse) * ramp;
            return slot == Order[n % 6];
        }
    }
}
