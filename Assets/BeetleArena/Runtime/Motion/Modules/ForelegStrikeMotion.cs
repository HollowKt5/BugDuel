using UnityEngine;
namespace BeetleArena
{
    [CreateAssetMenu(menuName = "Beetle Arena/Motions/Foreleg Strike")]
    public sealed class ForelegStrikeMotion : BeetleMotionModule
    {
        public override string DisplayName => "前足扬起拍压";
        public override BeetleMotionChannel Channel => BeetleMotionChannel.Combat;
        public override BeetleMotionFamily Family => BeetleMotionFamily.Foreleg;
        public override void SetDefaults()
        {
            duration = .62f; cooldown = 1.5f; weight = 1.25f;
            evidence = "User video.mp4: foreleg extension/retraction separate from head motion, inspected at 3.10–3.82s and 18.25–18.89s. Short forward/downward stroke is an artistic readable adaptation; footage alone does not measure impact force or prove every reach lands.";
        }
        public override float SelectionWeight(in BeetleMotionContext c) => weight * Mathf.Lerp(.55f, 1.9f, c.Lateral) * c.Tempo;
        public override bool CanStart(in BeetleMotionContext c) => c.Fighting && c.Grounded && !c.OpponentAirborne &&
            c.Distance < 4.5f && c.ForelegTargetDistance < .6f && c.Facing > .4f && c.Stamina > .28f && c.Footing > .6f;
        public override BeetleMotionSample Sample(in BeetleMotionContext c, float phase)
        {
            float stroke = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.38f, .54f, phase));
            float recover = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.58f, 1, phase));
            float windup = Mathf.SmoothStep(0, 1, phase / .18f) * (1 - stroke);
            // Counter-lean during the raised silhouette, then a short diagonal slap.
            // The head stays quiet so it cannot read as another horn attack.
            return new BeetleMotionSample { PoseResponse = 29, HeadPitch = 7 * stroke * recover,
                BodyRoll = c.ForelegSide * (10 * stroke * recover - 11 * windup), BodyPitch = 3 * stroke * recover,
                Strain = stroke * recover, StaminaPerSecond = 10,
                LocalVelocity = new Vector2(c.ForelegSide * .12f, .95f) * Pulse(phase),
                Contact = phase >= .40f && phase <= .76f ? BeetleMotionContact.Foreleg : BeetleMotionContact.None,
                ContactLegPlusOne = c.ForelegSide < 0 ? 1 : 4,
                LocalImpulse = new Vector2(-c.ForelegSide * .75f, .7f), Impulse = .85f, BalanceCost = .16f };
        }
        public override bool SampleLeg(in BeetleMotionContext c, float phase, int slot, out Vector3 offset)
        {
            Vector3 raised = new Vector3(c.ForelegSide * .42f, 1.22f, -.24f);
            Vector3 struck = new Vector3(-c.ForelegSide * .28f, .72f, .70f);
            Vector3 follow = new Vector3(-c.ForelegSide * .32f, .28f, .22f);
            if (phase < .18f) offset = Vector3.Lerp(Vector3.zero, raised, Mathf.SmoothStep(0, 1, phase / .18f));
            else if (phase < .38f) offset = raised; // Hold the open silhouette before the fast slap.
            else if (phase < .54f) offset = Vector3.Lerp(raised, struck, Mathf.SmoothStep(0, 1, (phase - .38f) / .16f));
            else if (phase < .78f) offset = Vector3.Lerp(struck, follow, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.60f, .78f, phase)));
            else offset = Vector3.Lerp(follow, Vector3.zero, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.78f, 1, phase)));
            offset += c.ForelegAimCorrection * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.38f, .54f, phase)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.60f, .78f, phase)));
            return slot == (c.ForelegSide < 0 ? 0 : 3);
        }
        public override float LegSpeed(in BeetleMotionContext c, float phase, int slot) => phase < .18f || phase >= .38f && phase <= .78f ? 9 : 5;
    }
}
