using UnityEngine;
namespace BeetleArena
{
    [CreateAssetMenu(menuName = "Beetle Arena/Motions/Horn Feint")]
    public sealed class HornFeintMotion : BeetleMotionModule
    {
        [Header("Feint pose / model-local foot offsets")]
        [Range(20, 55)] public float headDip = 43;
        [Range(.08f, .3f)] public float footLift = .19f;
        [Range(.1f, .4f)] public float footReach = .30f;
        public override string DisplayName => "探角虚晃 · 收角后撤";
        public override BeetleMotionChannel Channel => BeetleMotionChannel.Combat;
        public override bool AppliesContactResponse => false;
        public override void SetDefaults()
        {
            duration = .82f; cooldown = 3.8f; weight = .6f;
            evidence = "Visual feint built from documented Trypoxylus horn probing/assessment (McCullough & Zinna 2013, https://doi.org/10.1603/AN12155). The dip-and-withdraw timing is a game abstraction, not evidence of intentional deception.";
        }
        public override bool CanStart(in BeetleMotionContext c) => c.Fighting && c.Grounded && !c.OpponentAirborne &&
            c.Distance > 2.8f && c.Distance < 4.4f && c.Facing > .7f && c.Stamina > .23f && c.Balance > .4f;
        // 短前探、短停顿、快速收角、较长复位；打破原先缓慢对称的正弦摆动。
        static float Dip(float phase) => phase < .27f ? Mathf.SmoothStep(0, 1, phase / .27f) :
            1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.37f, .76f, phase));
        public override BeetleMotionSample Sample(in BeetleMotionContext c, float phase)
        {
            float dip = Dip(phase);
            float recoil = Pulse(Mathf.InverseLerp(.52f, 1, phase));
            float forward = phase < .37f ? .65f * dip : c.Footing > .85f ? -.52f * dip : 0;
            return new BeetleMotionSample { HeadPitch = headDip * dip - 6 * recoil,
                HeadYaw = c.Side * 8 * dip, BodyPitch = 7 * dip - 2 * recoil,
                BodyRoll = -c.Side * 2.5f * dip, BodyHeight = -.025f * dip,
                Strain = .55f * dip, LocalVelocity = new Vector2(0, forward),
                StaminaPerSecond = 4, Contact = BeetleMotionContact.None };
        }
        public override bool SampleLeg(in BeetleMotionContext c, float phase, int slot, out Vector3 offset)
        {
            int lead = c.Side < 0 ? 0 : 3;
            if (slot % 3 == 0)
            {
                // 一足先短促探步，另一足在收角阶段换位；不同时抬起两只前足。
                bool first = slot == lead;
                float start = first ? 0 : .40f, end = first ? .36f : .83f;
                float t = Mathf.InverseLerp(start, end, phase);
                float p = Pulse(t);
                offset = new Vector3((slot < 3 ? -1 : 1) * .045f * p,
                    footLift * p, (first ? footReach : -footReach * .55f) * p);
                return phase >= start && phase <= end;
            }
            // 后足贴地向后撑，虚晃也有可见的发力链，但不产生攻击冲量。
            offset = new Vector3(0, 0, -.14f * Dip(phase));
            return slot % 3 == 2;
        }
    }
}
