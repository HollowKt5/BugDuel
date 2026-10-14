using UnityEngine;
namespace BeetleArena
{
    [CreateAssetMenu(menuName = "Beetle Arena/Motions/Driving Push")]
    public sealed class DrivingPushMotion : BeetleMotionModule
    {
        [Header("Forward drive")]
        [Range(20, 45)] public float headDrive = 38;
        [Range(.5f, 1.5f)] public float contactImpulse = 1.28f;
        public override void SetDefaults() => evidence = "Trypoxylus dichotomus pushing: McCullough & Zinna 2013, Results Fighting Behavior. https://doi.org/10.1603/AN12155 . Pose/timing are game approximations.";
        public override string DisplayName => "后足撑地 · 扬头推顶";
        public override BeetleMotionChannel Channel => BeetleMotionChannel.Combat;
        public override BeetleMotionFamily Family => BeetleMotionFamily.RaisedDrive;
        public override float SelectionWeight(in BeetleMotionContext c) => weight * Mathf.Lerp(.45f, 2.1f, c.Drive);
        public override bool CanStart(in BeetleMotionContext c) => c.Fighting && c.Grounded && !c.OpponentAirborne &&
            (c.Exchange == ExchangePhase.Clash || c.Intent == BeetleIntent.Pressure) && c.Distance < 4.4f && c.Facing > .8f && c.Stamina > .32f && c.Footing > .5f && c.RearSupport > .4f;
        public override BeetleMotionSample Sample(in BeetleMotionContext c, float phase)
        {
            float load = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .22f, phase));
            float rise = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.22f, .48f, phase));
            float release = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.74f, 1, phase));
            float grip = Mathf.Clamp01(c.RearSupport);
            float drive = rise * release * Mathf.Lerp(.72f, 1, c.Burst);
            return new BeetleMotionSample { HeadPitch = (headDrive * .16f * load - headDrive * .92f * rise) * release * grip,
                BodyPitch = (4 * load - 18 * rise) * release * grip, BodyHeight = .065f * rise * release * grip,
                PoseResponse = Mathf.Lerp(19, 28, c.Burst), Strain = 1.2f * drive, StaminaPerSecond = 15,
                LocalVelocity = new Vector2(0, 1.1f * drive), PlantRearFeet = phase >= .12f && phase <= .78f, RequiresRearSupport = true,
                Contact = phase > .34f && phase < .76f ? BeetleMotionContact.Horn : BeetleMotionContact.None,
                LocalImpulse = Vector2.up, Impulse = contactImpulse, BalanceCost = .12f };
        }
    }
}
