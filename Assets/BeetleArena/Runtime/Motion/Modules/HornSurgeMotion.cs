using UnityEngine;
namespace BeetleArena
{
    [CreateAssetMenu(menuName = "Beetle Arena/Motions/Horn Surge")]
    public sealed class HornSurgeMotion : BeetleMotionModule
    {
        public override string DisplayName => "压角蓄势 · 扬头顶击";
        public override BeetleMotionChannel Channel => BeetleMotionChannel.Combat;
        public override BeetleMotionFamily Family => BeetleMotionFamily.RaisedDrive;
        public override float SelectionWeight(in BeetleMotionContext c) => weight * Mathf.Lerp(.5f, 2.2f, c.LiftStyle);
        public override void SetDefaults()
        {
            duration = .95f; cooldown = 2.4f; weight = 1.15f;
            evidence = "User video.mp4 (53.97s), observed 3.75–4.25s and 51–53s: quiet support then brief large head/forebody surge. Artistic adaptation, not measured biomechanics.";
        }
        public override bool CanStart(in BeetleMotionContext c) => c.Fighting && c.Grounded &&
            !c.OpponentAirborne && c.Touching && c.Facing > .8f && c.Footing > .65f &&
            c.Stamina > .4f && c.Burst > .45f && c.RearSupport > .4f && c.Exchange == ExchangePhase.Clash;
        public override BeetleMotionSample Sample(in BeetleMotionContext c, float phase)
        {
            float load = Mathf.SmoothStep(0, 1, phase / .28f);
            float strike = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.32f, .44f, phase));
            float release = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.50f, .74f, phase));
            float drive = strike * release;
            return new BeetleMotionSample {
                HeadPitch = (30 * load - 89 * strike) * release * c.RearSupport,
                BodyPitch = (6 * load - 17 * strike) * release * c.RearSupport, BodyHeight = -.04f * load * (1 - strike),
                PoseResponse = 27, Strain = 1.5f * release,
                LocalVelocity = new Vector2(0, phase < .30f ? -.12f * load : 1.35f * drive),
                StaminaPerSecond = 17, PlantRearFeet = phase >= .12f && phase <= .65f, RequiresRearSupport = true,
                Contact = phase >= .35f && phase <= .62f ? BeetleMotionContact.Horn : BeetleMotionContact.None,
                LocalImpulse = new Vector2(c.Side * .24f, 1), Impulse = 1.35f, BalanceCost = .17f
            };
        }
    }
}
