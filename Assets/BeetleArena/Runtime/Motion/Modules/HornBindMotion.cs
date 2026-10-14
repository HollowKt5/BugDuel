using UnityEngine;
namespace BeetleArena
{
    [CreateAssetMenu(menuName = "Beetle Arena/Motions/Horn Bind")]
    public sealed class HornBindMotion : BeetleMotionModule
    {
        [Header("Horn drive")]
        [Range(16, 40)] public float headDrive = 34;
        [Range(.5f, 1.5f)] public float contactImpulse = 1.10f;
        [Range(.08f, .3f)] public float braceTravel = .25f;
        public override void SetDefaults() => evidence = "Trypoxylus dichotomus binding / lateral horn pushing: McCullough & Zinna 2013, Results Fighting Behavior. https://doi.org/10.1603/AN12155 . Pose/timing are game approximations.";
        public override string DisplayName => "交叉顶角 · 侧向角力";
        public override BeetleMotionChannel Channel => BeetleMotionChannel.Combat;
        public override float SelectionWeight(in BeetleMotionContext c) => weight * Mathf.Lerp(.35f, 2.1f, c.Lateral);
        public override bool CanStart(in BeetleMotionContext c) => c.Fighting && c.Grounded && !c.OpponentAirborne &&
            (c.Exchange == ExchangePhase.Clash || c.Intent == BeetleIntent.Pressure) && c.Distance < 4.3f && c.Facing > .65f && c.Stamina > .28f;
        // 先压住支撑，再短促侧顶，随后卸力；峰值前移，留下回弹/恢复时间。
        static float Drive(float p) => InsectMuscleAnimation.Recruit(p);
        public override BeetleMotionSample Sample(in BeetleMotionContext c, float phase)
        {
            float effort = Drive(phase) * Mathf.Lerp(.72f, 1, c.Burst), side = c.Side;
            return new BeetleMotionSample {
                HeadPitch = headDrive * .5f * effort, HeadYaw = side * 25 * effort, BodyRoll = -side * 17 * effort,
                PoseResponse = Mathf.Lerp(19, 28, c.Burst), BodyPitch = 3 * effort, BodyHeight = -.02f * effort, Strain = 1.2f * effort, StaminaPerSecond = 11,
                LocalVelocity = new Vector2(side * .30f, .70f) * effort,
                Contact = phase > .34f && phase < .75f ? BeetleMotionContact.Horn : BeetleMotionContact.None,
                LocalImpulse = new Vector2(side * .85f, .35f), Impulse = contactImpulse, BalanceCost = .14f };
        }
        public override bool SampleLeg(in BeetleMotionContext c, float phase, int slot, out Vector3 offset)
        {
            float effort = Drive(phase);
            // 中后足横向撑住并向后蹬；不做悬空的“拳击”，仍由地表和 IK 限制足端。
            offset = new Vector3(-c.Side * .085f * effort, 0, -braceTravel * effort);
            return slot % 3 == 1 || slot == (c.Side < 0 ? 2 : 5);
        }
    }
}
