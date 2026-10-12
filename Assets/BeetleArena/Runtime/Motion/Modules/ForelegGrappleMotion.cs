using UnityEngine;
namespace BeetleArena
{
    [CreateAssetMenu(menuName = "Beetle Arena/Motions/Foreleg Grapple (Golofa reference)")]
    public sealed class ForelegGrappleMotion : BeetleMotionModule
    {
        public override void SetDefaults()
        {
            evidence = "Cross-species adaptation: Eberhard, Golofa porteri, Psyche 84:292-298 (1977 volume, published 1978), figures 1a-c. Not established for Trypoxylus; constrained to current leg reach. https://repository.si.edu/bitstreams/a06da26f-32ea-4bd7-b73a-b72a12d0d6f9/download";
            duration = 1.1f; cooldown = 2.8f; weight = .9f;
        }
        // Eberhard 的 Golofa porteri 观察：前足包围/向后下方扒拉，非人类拳击。
        // 当前 Trypoxylus 外形仅作跨物种保守移植；资产 evidence 明确标注，可独立移除。
        public override string DisplayName => "前足探抵 · 扒拉解围";
        public override BeetleMotionChannel Channel => BeetleMotionChannel.Combat;
        public override bool CanStart(in BeetleMotionContext c) => c.Fighting && c.Grounded && !c.OpponentAirborne &&
            c.Distance < 4.3f && c.Facing > .55f && c.Stamina > .25f && c.Footing > .6f;
        public override BeetleMotionSample Sample(in BeetleMotionContext c, float phase)
        {
            float p = Pulse(phase);
            return new BeetleMotionSample { HeadPitch = 24 * p, BodyRoll = -c.Side * 4 * p, BodyPitch = -2 * p,
                Strain = .55f * p, StaminaPerSecond = 7, LocalVelocity = new Vector2(c.Side * .08f, .22f) * p,
                Contact = phase > .30f && phase < .72f ? BeetleMotionContact.Foreleg : BeetleMotionContact.None,
                LocalImpulse = new Vector2(c.Side * .4f, -.3f), Impulse = .28f, BalanceCost = .08f };
        }
        public override bool SampleLeg(in BeetleMotionContext c, float phase, int slot, out Vector3 offset)
        {
            int active = c.Side < 0 ? 0 : 3;
            float p = Pulse(phase);
            // 先伸前足，后向下/后方扒回；只动一条前足，其他五足继续支持。
            offset = new Vector3(c.Side * .05f * p, .20f * p,
                .52f * p * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.5f, .85f, phase))));
            return slot == active;
        }
    }
}
