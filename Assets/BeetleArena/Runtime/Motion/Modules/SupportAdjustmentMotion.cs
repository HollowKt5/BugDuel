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
            c.Fallen ? c.OnFloor && c.FloorTime > 1.8f : c.Grounded && c.Speed < .3f && !c.Committed && c.Impact < .025f;
        // Unilateral support shift: unloads that side first, lifts foot with low amplitude to replant, then shifts center of mass. Tuned for animation.
        // Fixed phase window with individual phase offsets; window duration does not fluctuate with per-frame Footing, avoiding curve popping.
        static void Adjustment(in BeetleMotionContext c, out int slot, out float load, out float lift)
        {
            const float window = 1.9f;
            int n = Mathf.FloorToInt(c.Time / window);
            slot = Order[n % Order.Length];
            float start = Mathf.Lerp(.15f, .40f, InsectMotionTiming.Value(n, 113));
            float span = Mathf.Lerp(.65f, .9f, InsectMotionTiming.Value(n, 114));
            float p = Mathf.Clamp01((c.Time - n * window - start) / span);
            float ramp = c.Fallen ? Mathf.SmoothStep(0, 1, Mathf.Clamp01((c.FloorTime - 1.8f) / .5f)) : 1;
            // Subtle adjustments retained when grounded, moderately increased during low support or imbalance; does not claim per-leg muscle dynamics.
            float need = Mathf.Lerp(.65f, 1, Mathf.Clamp01(1 - c.Footing + 1 - c.Balance));
            // Winner retains discernible post-match stepping; compensates Winner only without amplifying idle sway during active combat.
            if (c.Action == BeetleAction.Winner) need *= 1.3f;
            if (c.Touching) need *= .65f;
            load = Mathf.SmoothStep(0, 1, p / .22f) * (1 - Mathf.SmoothStep(0, 1, (p - .72f) / .28f)) * ramp * need;
            lift = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.InverseLerp(.24f, .76f, p)), 2) * ramp * need;
        }
        public override BeetleMotionSample Sample(in BeetleMotionContext c, float phase)
        {
            Adjustment(c, out int slot, out float load, out _);
            float side = slot < 3 ? -1 : 1;
            return new BeetleMotionSample { BodyRoll = -side * 1.3f * load,
                BodyPitch = .35f * load, BodyHeight = -.004f * load,
                HeadYaw = (InsectMotionTiming.Bout(c.Time, 2.13f, 151) -
                    InsectMotionTiming.Bout(c.Time + .71f, 1.73f, 173)) * 4 };
        }
        public override bool SampleLeg(in BeetleMotionContext c, float phase, int slot, out Vector3 offset)
        {
            Adjustment(c, out int chosen, out _, out float lift);
            offset = new Vector3((slot < 3 ? -1 : 1) * .025f * lift, .07f * lift, .025f * lift);
            return slot == chosen && lift > .0001f;
        }
    }
}
