using UnityEngine;
namespace BeetleArena
{
    [CreateAssetMenu(menuName = "Beetle Arena/Motions/Impact Recovery")]
    public sealed class ImpactRecoveryMotion : BeetleMotionModule
    {
        [Range(0, 8)] public float recoilPitch = 4;
        [Range(0, .2f)] public float catchLift = .11f;
        public override string DisplayName => "受力退让 · 补足撑稳";
        public override BeetleMotionChannel Channel => BeetleMotionChannel.SupportOverlay;
        public override bool AppliesContactResponse => false;
        public override void SetDefaults() => evidence = "Presentation approximation of resistance and maintaining/recovering grip in beetle fights (McCullough & Zinna 2013; Eberhard Golofa fig.1). Exact recoil and catching-foot trajectories are animation design, not measured reflex data.";
        public override bool CanStart(in BeetleMotionContext c) => c.Fighting && c.Grounded && c.Impact > .025f &&
            c.Action != BeetleAction.Lifting && c.Action != BeetleAction.Toss && c.Action != BeetleAction.ReleaseLift;
        public override BeetleMotionSample Sample(in BeetleMotionContext c, float phase) => new BeetleMotionSample {
            BodyPitch = -recoilPitch * c.Impact, BodyRoll = -c.Side * 3 * c.Impact,
            HeadPitch = -9 * c.Impact, BodyHeight = -.025f * c.Impact };
        public override bool SampleLeg(in BeetleMotionContext c, float phase, int slot, out Vector3 offset)
        {
            // 一只前足短暂补步；中后足贴地展撑。零强度自动交还通用 IK，空挥没有此反应。
            bool front = slot == (c.Side < 0 ? 0 : 3);
            float i = c.Impact;
            offset = front ? new Vector3(0, catchLift * 4 * i * (1 - i), -.20f * i) :
                new Vector3((slot < 3 ? -1 : 1) * .04f * i, 0, -.12f * i);
            return front || slot % 3 != 0;
        }
    }
}
