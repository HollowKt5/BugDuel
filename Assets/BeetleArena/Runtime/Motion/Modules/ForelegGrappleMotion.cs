using UnityEngine;
namespace BeetleArena
{
    [CreateAssetMenu(menuName = "Beetle Arena/Motions/Foreleg Grapple (Golofa reference)")]
    public sealed class ForelegGrappleMotion : BeetleMotionModule
    {
        [Range(.15f, .4f)] public float footLift = .28f;
        [Range(.3f, .8f)] public float footReach = .66f;
        // Retraction is shorter than extension: middle and hind legs bear weight first, while foreleg extends, contacts, and pulls downward-backward.
        static float Reach(float p) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.05f, .34f, p));
        static float Rake(float p) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.56f, .72f, p));
        public override void SetDefaults()
        {
            evidence = "Cross-species adaptation: Eberhard, Golofa porteri, Psyche 84:292-298 (1977 volume, published 1978), figures 1a-c. Not established for Trypoxylus; constrained to current leg reach. https://repository.si.edu/bitstreams/a06da26f-32ea-4bd7-b73a-b72a12d0d6f9/download";
            duration = 1.1f; cooldown = 2.8f; weight = .9f;
        }
        // Eberhard's observations of Golofa porteri: forelegs envelop and pull downward-backward, unlike humanoid boxing.
        // Current Trypoxylus morphology is a conservative cross-species adaptation; clearly noted in asset evidence and independently removable.
        public override string DisplayName => "前足探抵 · 扒拉解围";
        public override BeetleMotionChannel Channel => BeetleMotionChannel.Combat;
        public override BeetleMotionFamily Family => BeetleMotionFamily.Foreleg;
        public override float SelectionWeight(in BeetleMotionContext c) => weight * Mathf.Lerp(.3f, 1.2f, c.Lateral);
        public override bool CanStart(in BeetleMotionContext c) => c.Fighting && c.Grounded && !c.OpponentAirborne &&
            c.Distance < 4.3f && c.Facing > .55f && c.Stamina > .25f && c.Footing > .6f;
        public override BeetleMotionSample Sample(in BeetleMotionContext c, float phase)
        {
            float p = Pulse(phase);
            return new BeetleMotionSample { PoseResponse = 23, HeadPitch = 12 * p, BodyRoll = -c.ForelegSide * 10 * p, BodyPitch = 3 * p,
                Strain = .55f * p, StaminaPerSecond = 7, LocalVelocity = new Vector2(c.Side * .08f, .22f) * p,
                Contact = phase > .30f && phase < .80f ? BeetleMotionContact.Foreleg : BeetleMotionContact.None,
                ContactLegPlusOne = c.ForelegSide < 0 ? 1 : 4,
                LocalImpulse = new Vector2(c.ForelegSide * .4f, -.3f), Impulse = .28f, BalanceCost = .08f };
        }
        public override bool SampleLeg(in BeetleMotionContext c, float phase, int slot, out Vector3 offset)
        {
            int active = c.ForelegSide < 0 ? 0 : 3;
            float reach = Reach(phase), rake = Rake(phase);
            float recover = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.76f, 1, phase));
            // Raises only a single foreleg, avoiding imposing long-armed Golofa or humanoid boxing onto short-legged models.
            offset = new Vector3(c.ForelegSide * (.40f * reach - .62f * rake) * recover,
                footLift * 1.6f * reach * (1 - .75f * rake) * recover,
                (footReach * reach - (footReach + .38f) * rake) * recover);
            return slot == active;
        }
        public override float LegSpeed(in BeetleMotionContext c, float phase, int slot) => phase >= .56f && phase <= .76f ? 7 : 4;
    }
}
