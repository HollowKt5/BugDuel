using UnityEngine;

namespace BeetleArena
{
    // 保留已有枚举值；档位顺序通过 TierNumber 映射，不依赖枚举整数大小。
    public enum BeetleThrowKind { None, ShortPry, ArcThrow, Overhead, QuickPry, HighArc, PowerOverhead }

    /// <summary>
    /// 杠杆撬投的纯计算层。接触层提供已接受的抬头转角、力臂和切向方向；
    /// 只在有效接触且确实抬头时积分功和冲量，静止顶住不能无限蓄力。
    /// τ=F_in*l_in，F_out=τ/l_out，W=∫τ dθ，J=∫F_out dt。
    /// 游戏使用归一化单位与效率上限，不是生物力学实验的数值拟合。
    /// 杠杆依据：Weber et al. 2023, doi:10.1016/j.cub.2023.08.066。
    /// </summary>
    public static class BeetleThrowMechanics
    {
        public static void Accumulate(BeetleMatchSettings settings, BeetleState attacker,
            BeetleState defender, float dt, float raisedRadians, float arm,
            Vector3 tangent, float contactQuality)
        {
            if (dt <= 0 || !defender.Lift.Supported) return;
            var lift = defender.Lift;
            float quality = Mathf.Clamp01(contactQuality);
            lift.ContactSeconds += dt * quality;
            lift.LostContactSeconds = quality > .15f ? 0 : lift.LostContactSeconds + dt;
            lift.LeverArm = Mathf.Max(.12f, arm);
            float delta = Mathf.Clamp(raisedRadians, 0, .35f);
            lift.AngularSpeed = Mathf.Lerp(lift.AngularSpeed, delta / dt, 1 - Mathf.Exp(-dt * 30));
            lift.HeadSweep += delta * Mathf.Rad2Deg;
            lift.PeakAngularSpeed = Mathf.Max(lift.PeakAngularSpeed, lift.AngularSpeed);
            if (quality <= 0 || delta <= .00001f) return;

            float effort = (.4f + .6f * attacker.Stamina01) * lift.Effort;
            if (attacker.CheerRemaining > 0) effort *= 1 + settings.cheerEffortBonus;
            float inputArm = settings.throwInputArm * attacker.Traits.size;
            float torque = settings.throwMuscleForce * attacker.Traits.strength * effort * inputArm
                * Mathf.Lerp(.55f, 1, attacker.Footing) * Mathf.Lerp(.6f, 1, attacker.Balance);
            // 尚未脱足时克服抓地；已失去支撑后只保留小幅损耗。
            float resistance = defender.Lift.GripReleased ? .08f :
                .35f * defender.Traits.grip * defender.Footing * Mathf.Lerp(.3f, 1, defender.Balance);
            lift.OutputForce = Mathf.Max(0, torque / lift.LeverArm - resistance);
            float netTorque = lift.OutputForce * lift.LeverArm;
            lift.Work = Mathf.Min(settings.throwWorkLimit, lift.Work + netTorque * delta * quality);
            lift.Impulse += lift.OutputForce * dt * quality;
            if (tangent.sqrMagnitude > .0001f) lift.ReleaseTangent = tangent.normalized;
        }

        public static int TierNumber(BeetleThrowKind kind)
        {
            switch (kind)
            {
                case BeetleThrowKind.ShortPry: return 1;
                case BeetleThrowKind.QuickPry: return 2;
                case BeetleThrowKind.ArcThrow: return 3;
                case BeetleThrowKind.HighArc: return 4;
                case BeetleThrowKind.Overhead: return 5;
                case BeetleThrowKind.PowerOverhead: return 6;
                default: return 0;
            }
        }

        public static string Description(BeetleThrowKind kind)
        {
            switch (kind)
            {
                case BeetleThrowKind.ShortPry: return "近身短促撬飞！";
                case BeetleThrowKind.QuickPry: return "快速扬角，向前抛出！";
                case BeetleThrowKind.ArcThrow: return "持续发力，弧线抛出！";
                case BeetleThrowKind.HighArc: return "强力扬角，高弧抛起！";
                case BeetleThrowKind.Overhead: return "后扬头角，翻抛对手！";
                case BeetleThrowKind.PowerOverhead: return "猛然发力，越顶反抛！";
                default: return "撬投";
            }
        }

        /// <summary>按连续出手速度和实际方向划为六档；四个不等距阈值均可配置。</summary>
        public static BeetleThrowKind Classify(BeetleMatchSettings settings, float speed, bool backwards)
        {
            if (backwards) return speed < settings.throwPowerOverheadSpeed ? BeetleThrowKind.Overhead : BeetleThrowKind.PowerOverhead;
            if (speed < settings.throwQuickSpeed) return BeetleThrowKind.ShortPry;
            if (speed < settings.throwArcSpeed) return BeetleThrowKind.QuickPry;
            return speed < settings.throwHighArcSpeed ? BeetleThrowKind.ArcThrow : BeetleThrowKind.HighArc;
        }

        /// <summary>输出连续速度和仰角。六档不把同档所有接触固定成同一条轨迹。</summary>
        public static Vector3 Release(BeetleMatchSettings settings, BeetleState defender)
        {
            var lift = defender.Lift;
            float mass = Mathf.Max(.1f, defender.Traits.mass);
            float speedFromWork = Mathf.Sqrt(2 * lift.Work * settings.throwEfficiency / mass);
            float speedFromImpulse = lift.Impulse / mass;
            // 功限制能量，冲量限制短促/滑脱接触；长时间但不抬头仍不会产生额外速度。
            float speed = Mathf.Min(speedFromWork, speedFromImpulse + .4f * speedFromWork);
            speed = Mathf.Clamp(speed, settings.throwMinimumSpeed, settings.throwMaximumSpeed);
            Vector3 forward = new Vector3(lift.Direction.x, 0, lift.Direction.y).normalized;
            Vector3 tangent = lift.ReleaseTangent.sqrMagnitude > .01f ? lift.ReleaseTangent.normalized : Vector3.up;
            float elevation = Mathf.Atan2(Mathf.Max(.12f, tangent.y), Vector3.Dot(tangent, forward)) * Mathf.Rad2Deg;
            bool overhead = lift.HeadSweep >= settings.throwOverheadSweep &&
                lift.Work / mass >= settings.throwOverheadWork && lift.PeakAngularSpeed >= settings.throwOverheadAngularSpeed;
            // 低能量接触提前滑离，不能一直挂在角上翻到身后。高能量才保留向后切向分量；
            // 限制过平的反抛，给真实身体越过攻击者的高度余量，碰撞仍完整开启。
            float ordinaryLimit = Mathf.Lerp(38, 82, Mathf.Clamp01(lift.Work / mass / 32));
            // 后扬到强反抛之间连续增加允许仰角，跨档不突然切换轨迹。
            float overheadLimit = Mathf.Lerp(100, 118, Mathf.InverseLerp(settings.throwHighArcSpeed, settings.throwMaximumSpeed, speed));
            elevation = Mathf.Clamp(elevation, 32, overhead ? overheadLimit : ordinaryLimit);
            float angle = elevation * Mathf.Deg2Rad;
            Vector3 side = Vector3.Cross(Vector3.up, forward);
            float lateral = Mathf.Clamp(Vector3.Dot(tangent, side), -.22f, .22f);
            Vector3 direction = (forward * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle) + side * lateral).normalized;
            lift.LaunchVelocity = direction * speed;
            lift.LaunchElevation = Mathf.Atan2(lift.LaunchVelocity.y, Vector3.Dot(lift.LaunchVelocity, forward)) * Mathf.Rad2Deg;
            lift.ThrowKind = Classify(settings, speed, Vector3.Dot(lift.LaunchVelocity, forward) < 0);
            return lift.LaunchVelocity;
        }
    }
}
