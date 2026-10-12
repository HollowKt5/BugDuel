using UnityEngine;

namespace BeetleArena
{
    /// <summary>
    /// 落台后的运动反馈。HasFallen 只代表比赛资格，不能等同于死亡或冻结动作。
    /// Simulation 推进独立时钟，ContactSolver 确认台外地面接触，Animator 只消费这些数据。
    /// 不从本模块重新进入战斗，也不改变已结算的胜负。
    /// </summary>
    public sealed class BeetleFallResponse
    {
        public bool Active { get; private set; }
        public bool OnFloor { get; private set; }
        public float Time { get; private set; }
        public float UnsupportedSeconds { get; private set; }
        public float GroundSeconds { get; private set; }
        public float ImpactSpeed { get; private set; }
        public float ForwardSpeed { get; private set; }
        public float SideSpeed { get; private set; }

        public void Begin(BeetleState state)
        {
            if (Active) return;
            Active = true;
            UnsupportedSeconds = state.Lift.UnsupportedTime;
            ForwardSpeed = Vector2.Dot(state.Velocity, state.Forward);
            SideSpeed = Vector2.Dot(state.Velocity, new Vector2(state.Forward.y, -state.Forward.x));
        }

        public void Tick(float dt)
        {
            if (!Active) return;
            Time += dt;
            if (OnFloor) GroundSeconds += dt;
            else UnsupportedSeconds += dt;
        }

        // 必须在最终接受的碰撞姿态上调用，不能把被回退的试算当作落地。
        public void SetFloorContact(bool supported, float downwardSpeed)
        {
            if (!Active) return;
            if (supported && !OnFloor) { ImpactSpeed = Mathf.Max(0, downwardSpeed); GroundSeconds = 0; }
            if (!supported) GroundSeconds = 0;
            OnFloor = supported;
        }

        public void Reset()
        {
            Active = OnFloor = false;
            Time = UnsupportedSeconds = GroundSeconds = ImpactSpeed = ForwardSpeed = SideSpeed = 0;
        }
    }
}
