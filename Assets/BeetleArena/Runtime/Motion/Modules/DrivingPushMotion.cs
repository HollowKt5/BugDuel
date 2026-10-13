using UnityEngine;
namespace BeetleArena
{
    [CreateAssetMenu(menuName = "Beetle Arena/Motions/Driving Push")]
    public sealed class DrivingPushMotion : BeetleMotionModule
    {
        [Header("Forward drive")]
        [Range(20, 45)] public float headDrive = 38;
        [Range(.5f, 1.5f)] public float contactImpulse = 1.28f;
        [Range(.1f, .35f)] public float hindLegDrive = .29f;
        public override void SetDefaults() => evidence = "Trypoxylus dichotomus pushing: McCullough & Zinna 2013, Results Fighting Behavior. https://doi.org/10.1603/AN12155 . Pose/timing are game approximations.";
        public override string DisplayName => "压低重心 · 蹬足推顶";
        public override BeetleMotionChannel Channel => BeetleMotionChannel.Combat;
        public override bool CanStart(in BeetleMotionContext c) => c.Fighting && c.Grounded && !c.OpponentAirborne &&
            (c.Exchange == ExchangePhase.Clash || c.Intent == BeetleIntent.Pressure) && c.Distance < 4.4f && c.Facing > .8f && c.Stamina > .32f && c.Footing > .5f;
        static float Drive(float p) => InsectMuscleAnimation.Recruit(p);
        public override BeetleMotionSample Sample(in BeetleMotionContext c, float phase)
        {
            float p = Drive(phase) * Mathf.Lerp(.72f, 1, c.Burst);
            return new BeetleMotionSample { HeadPitch = headDrive * p, BodyPitch = 12 * p, BodyHeight = -.035f * p,
                PoseResponse = Mathf.Lerp(19, 28, c.Burst), Strain = 1.2f * p, StaminaPerSecond = 15, LocalVelocity = new Vector2(0, 1.0f * p),
                Contact = phase > .34f && phase < .7f ? BeetleMotionContact.Horn : BeetleMotionContact.None,
                LocalImpulse = Vector2.up, Impulse = contactImpulse, BalanceCost = .12f };
        }
        public override bool SampleLeg(in BeetleMotionContext c, float phase, int slot, out Vector3 offset)
        {
            // 后足贴地蹬压幅度增大；前中足保留常规支撑，所有足端仍接受 IK 和接触限制。
            offset = new Vector3(0, 0, -hindLegDrive * Drive(phase));
            return slot % 3 == 2;
        }
    }
}
