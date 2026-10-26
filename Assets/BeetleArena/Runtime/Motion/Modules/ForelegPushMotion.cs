using UnityEngine;

namespace BeetleArena
{
    [CreateAssetMenu(menuName = "Beetle Arena/Motions/Foreleg Push")]
    public sealed class ForelegPushMotion : BeetleMotionModule
    {
        public override string DisplayName => "后足撑地 · 前足抵推";
        public override BeetleMotionChannel Channel => BeetleMotionChannel.Combat;
        public override BeetleMotionFamily Family => BeetleMotionFamily.Foreleg;

        public override void SetDefaults()
        {
            duration = .94f;
            cooldown = 1.9f;
            weight = 1.3f;
            evidence = "User video.mp4, re-reviewed forelegs at 3.10–3.82s and 18.25–18.89s: unilateral reach, brief extension and recovery while other legs support. Push timing/force are gameplay adaptations, not measured force.";
        }

        public override float SelectionWeight(in BeetleMotionContext c) => weight * Mathf.Lerp(.6f, 1.7f, c.Drive);

        public override bool CanStart(in BeetleMotionContext c) =>
            c.Fighting && c.Grounded && c.OpponentContactConfirmed && !c.OpponentAirborne &&
            c.Distance < 4.5f && c.ForelegTargetDistance < .6f && c.Facing > .45f &&
            c.Stamina > .24f && c.Footing > .58f && c.RearSupport > .4f;

        public override BeetleMotionSample Sample(in BeetleMotionContext c, float phase)
        {
            float extend = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.22f, .44f, phase));
            float release = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.72f, 1, phase));
            float load = Smooth(Mathf.InverseLerp(0, .22f, phase)) * release;
            // A single hind leg can maintain thoracic elevation while bearing primary support; output force scales with actual support fraction.
            // Directly multiplying 14 deg by single-leg support 0.55 drops to 7.7 deg, falling below the 8 deg threshold required to reach forelegs forward.
            float rearLoad = c.RearSupport > .4f ? Mathf.Lerp(.75f, 1, c.RearSupport) : c.RearSupport;
            // Accepted chest pose, not the requested pose, authorizes lifting a foreleg.
            // Startup verifies true physical contact; thoracic rise completes the short push during brief horn disengagement, while hit is certified by foreleg geometry.
            bool supportedRise = c.ContactConfirmedAtStart && c.ForebodyRaised && c.RearSupport > .4f;
            float push = supportedRise ? extend * release : 0;
            return new BeetleMotionSample
            {
                PoseResponse = 25,
                BodyRoll = -c.ForelegSide * 9 * push,
                // Load the rear support first. A blocked rise stays on the ground without a reaching gesture.
                BodyPitch = -14 * load * rearLoad,
                HeadPitch = -18 * load * rearLoad,
                BodyHeight = .055f * load * rearLoad,
                Strain = .8f * push,
                StaminaPerSecond = 8,
                LocalVelocity = new Vector2(c.ForelegSide * .18f * push, .9f * load + .65f * push),
                PlantRearFeet = phase >= .12f && phase <= .78f,
                RequiresRearSupport = true,
                Contact = supportedRise && phase >= .38f && phase <= .72f
                    ? BeetleMotionContact.Foreleg : BeetleMotionContact.None,
                ContactLegPlusOne = c.ForelegSide < 0 ? 1 : 4,
                LocalImpulse = new Vector2(-c.ForelegSide * .35f, 1),
                Impulse = 1.4f,
                BalanceCost = .2f,
                StaggerSeconds = .38f
            };
        }

        public override bool SampleLeg(in BeetleMotionContext c, float phase, int slot, out Vector3 offset)
        {
            offset = Vector3.zero;
            if (!c.ContactConfirmedAtStart || !c.ForebodyRaised || c.RearSupport <= .4f || phase < .26f)
                return false;
            // Keep the elbow folded toward the head, never wind it out to the side.
            Vector3 folded = new Vector3(-c.ForelegSide * .06f, .28f, -.12f);
            Vector3 pressed = new Vector3(-c.ForelegSide * .18f, .52f, .72f);
            if (phase < .34f)
                offset = Vector3.Lerp(Vector3.zero, folded, Smooth(Mathf.InverseLerp(.26f, .34f, phase)));
            else
                offset = Vector3.Lerp(folded, pressed, Smooth(Mathf.InverseLerp(.34f, .52f, phase))) *
                    (1 - Smooth(Mathf.InverseLerp(.70f, 1, phase)));
            offset += c.ForelegAimCorrection * Smooth(Mathf.InverseLerp(.34f, .52f, phase)) *
                (1 - Smooth(Mathf.InverseLerp(.70f, 1, phase)));
            return slot == (c.ForelegSide < 0 ? 0 : 3);
        }

        static float Smooth(float t) => Mathf.SmoothStep(0, 1, t);
        public override float LegSpeed(in BeetleMotionContext c, float phase, int slot) => 6.5f;
    }
}
