using UnityEngine;

namespace BeetleArena
{
    // 受击者的托举反馈。模拟拥有时序，接触求解器拥有两个局部接触点，动画只消费状态。
    // 对应头角杠杆托举/失去抓地的行为近似，不是假定角尖能产生任意方向的吸附力。
    // Reference: Weber et al. 2023, https://doi.org/10.1016/j.cub.2023.08.066
    public sealed class BeetleLiftResponse
    {
        public int Supporter = -1;
        public float Time, RaiseSeconds, HoldSeconds;
        public float Pitch, Roll, UnsupportedTime, TwistSign;
        // 接触求解持续无法推进的时间。用于松脱失败的托举，不能靠累积位置修正强行抬起。
        public float BlockedTime;
        // 本次是否额外爆发用力；普通成功顶举也会抛出，不由此标记开关。
        public bool BurstEffort;
        public float Effort, SnapSeconds, SweepTarget, StrokeProgress;
        // 接触层只在固定步最终姿态被接受后写入以下测量值，显示插值不累计。
        public float ContactSeconds, LostContactSeconds, HeadSweep, AngularSpeed, PeakAngularSpeed;
        public float LeverArm, OutputForce, Work, Impulse;
        public Vector3 ReleaseTangent, LaunchVelocity;
        public float LaunchElevation;
        public BeetleThrowKind ThrowKind;
        public float SnapStart => RaiseSeconds + HoldSeconds;
        public float StrokeDuration => SnapStart + SnapSeconds;
        public void ResetMechanics()
        {
            ContactSeconds = LostContactSeconds = HeadSweep = AngularSpeed = PeakAngularSpeed = 0;
            LeverArm = OutputForce = Work = Impulse = StrokeProgress = LaunchElevation = 0;
            ReleaseTangent = LaunchVelocity = Vector3.zero; ThrowKind = BeetleThrowKind.None;
        }
        // 六足首次同时失去支撑后锁存；空中挣扎偶尔擦地不能重新算作抓住台面。
        public bool GripReleased;
        public Vector2 Direction;
        public bool Supported => Supporter >= 0;
        public float RaiseProgress => Mathf.Clamp01(Time / Mathf.Max(.05f, RaiseSeconds));
    }

    // 只有实际场景接触才能建立托举。隔空或腿碰腿不能退化成通用向上冲量。
    public interface IBeetleLiftContacts
    {
        bool TryBeginLift(BeetleSimulation simulation, BeetleState attacker, BeetleState defender);
    }
}
