using UnityEngine;

namespace BeetleArena
{
    public enum BeetleMotionChannel { Combat, SupportOverlay }
    public enum BeetleMotionContact { None, Horn, Foreleg }

    /// <summary>传给模块的值快照。没有 State、Transform、随机源或求解器引用，模块不能反写比赛。</summary>
    public readonly struct BeetleMotionContext
    {
        public readonly float Time, Speed, Size, Stamina, Footing, Balance, Distance, Facing, Side, FloorTime, Impact;
        public readonly bool Fighting, Grounded, Fallen, OnFloor, Touching, OpponentAirborne, Committed;
        public readonly BeetleIntent Intent;
        public readonly float Burst;
        public readonly BeetleAction Action;
        public readonly ExchangePhase Exchange;
        public readonly Vector2 OpponentLocal;
        public BeetleMotionContext(BeetleSimulation sim, BeetleState b)
        {
            var other = sim.Fighters[1 - b.Index];
            Vector2 delta = other.Position - b.Position;
            var right = new Vector2(b.Forward.y, -b.Forward.x);
            Time = sim.MotionTime + b.Index * .83f; Speed = b.Velocity.magnitude; Size = b.Traits.size;
            Stamina = b.Stamina01; Footing = b.Footing; Balance = b.Balance;
            Distance = delta.magnitude; Facing = Vector2.Dot(b.Forward, delta.normalized);
            Side = b.CircleDirection; FloorTime = b.Fall.GroundSeconds; Impact = Mathf.Clamp01(b.ImpactVisual);
            Fighting = sim.Phase == MatchPhase.Fighting; Grounded = !b.Airborne && !b.HasFallen;
            Fallen = b.HasFallen; OnFloor = b.Fall.OnFloor; Touching = b.TouchingOpponent;
            OpponentAirborne = other.Airborne || other.HasFallen; Committed = b.Committed;
            Action = b.Action; Exchange = sim.Exchange; Intent = b.Intent; Burst = b.BurstReserve;
            OpponentLocal = new Vector2(Vector2.Dot(delta, right), Vector2.Dot(delta, b.Forward));
        }
    }

    /// <summary>模块只交付意图。移动、消耗、碰撞确认和一次性冲量均由宿主统一处理。</summary>
    public struct BeetleMotionSample
    {
        public float BodyPitch, BodyRoll, HeadPitch, HeadYaw, BodyHeight, Strain, StaminaPerSecond;
        // 非零时只改变该动作的关节响应速度；默认保留旧动作/抬举的平滑参数。
        public float PoseResponse;
        public Vector2 LocalVelocity;
        public BeetleMotionContact Contact;
        public Vector2 LocalImpulse;
        public float Impulse, BalanceCost;
    }

    /// <summary>
    /// 无状态动作资产。每个模块独立文件，Inspector 引用列表即注册表；无需给核心添加 switch/case。
    /// 共享资产禁止保存计时/命中状态。实例数据只属于 BeetleMotionRuntime。
    /// </summary>
    public abstract class BeetleMotionModule : ScriptableObject
    {
        public bool enabledInMatch = true;
        [Min(0)] public float weight = 1;
        [Range(.25f, 3)] public float duration = .9f;
        [Range(.2f, 12)] public float cooldown = 2.5f;
        [TextArea] public string evidence;
        public abstract string DisplayName { get; }
        public abstract BeetleMotionChannel Channel { get; }
        public virtual bool AppliesContactResponse => true;
        public abstract bool CanStart(in BeetleMotionContext context);
        public virtual bool CanContinue(in BeetleMotionContext c) => c.Fighting && c.Grounded && !c.OpponentAirborne && c.Distance < 4.8f;
        public abstract BeetleMotionSample Sample(in BeetleMotionContext context, float phase);
        // offset 使用模型局部坐标、单位缩放。slot=左前/中/后、右前/中/后；最多覆盖自己的活动区域。
        public virtual bool SampleLeg(in BeetleMotionContext context, float phase, int slot, out Vector3 offset)
        { offset = Vector3.zero; return false; }
        // 触角为独立可选通道：返回相对绑定姿态的局部欧拉角，由宿主平滑。
        public virtual bool SampleAntenna(in BeetleMotionContext context, float phase, int slot, out Vector3 angles)
        { angles = Vector3.zero; return false; }
        // 新资产的默认参数/证据由自己的类提供，装配器不认识具体动作类型。
        public virtual void SetDefaults() { }
        protected virtual void OnEnable() { if (string.IsNullOrWhiteSpace(evidence)) SetDefaults(); }
        protected static float Pulse(float phase) => Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(phase)), 2);
        protected virtual void OnValidate()
        { weight = Mathf.Max(0, weight); duration = Mathf.Clamp(duration, .25f, 3); cooldown = Mathf.Clamp(cooldown, .2f, 12); }
    }

    /// <summary>可选的物理能力。没有该接口时，新模块的攻击不会以距离近似假造命中。</summary>
    public interface IBeetleMotionContacts
    {
        bool HasMotionContact(int attacker, BeetleMotionContact kind, int forelegSlot);
    }
}
