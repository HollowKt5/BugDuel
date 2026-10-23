using UnityEngine;

namespace BeetleArena
{
    // 比赛阶段：准备倒计时、正常格斗、首次落台后的双落台等待、最终结束。
    public enum MatchPhase
    {
        Countdown,
        Fighting,
        Resolving,
        Finished
    }

    public enum BeetleResultReason
    {
        None,
        RingOut,
        TimeLimit,
        UnableToRight
    }

    // 单只甲虫的动作状态。Windup 是准备动作，Pry 是抬举，Toss 是成功猛挑，Launched 是被挑飞。
    // 后续新增枚举值追加到末尾，避免旧的序列化整数映射到错误动作。
    public enum BeetleAction
    {
        Assess,
        Approach,
        Circle,
        ShoveWindup,
        Shove,
        PryWindup,
        Pry,
        Brace,
        Recover,
        Retreat,
        Stagger,
        Falling,
        Defeated,
        Winner,
        Probe,
        Disengage,
        ChargeWindup,
        Charge,
        HornLock,
        Toss,
        Launched,
        Lifting,
        Lifted,
        ReleaseLift,
        Module,
        Subdued
    }

    // 短周期交锋节奏，与 MatchPhase 的整场流程、BeetleAction 的个体动作分层管理。
    public enum ExchangePhase
    {
        Probing,
        Clash,
        Recovery
    }

    // 单个选手的全部动态数据。模拟写入，Actor/动画/HUD 读取；本类不依赖场景对象。
    public sealed class BeetleState
    {
        // 固定身份索引，0 为琥珀、1 为墨甲。Traits 引用在一局中保持不变。
        public readonly int Index;
        public readonly BeetleTraits Traits;
        public BeetleMatchEntrant Entrant { get; internal set; }
        public float VisualScale(BeetleMatchSettings settings) => Entrant?.VisualScale ?? settings.visualScale;
        // Position/Velocity 是世界 XZ 平面的坐标和每秒速度，Vector2.y 映射世界 Z。
        public Vector2 Position, Velocity;
        // Heading 使用角度；Height 为根节点世界 Y，高低起伏台面不再共用 Y=0。
        public float Heading, Height, VerticalVelocity;
        public readonly BeetleLiftResponse Lift = new BeetleLiftResponse();
        public readonly BeetleFallResponse Fall = new BeetleFallResponse();
        public readonly BeetleMotionRuntime Motion = new BeetleMotionRuntime();
        // SurfaceHeight/Normal 为本步支撑面；LastSupportHeight 用于离开边缘后的相对落差判负。
        public float SurfaceHeight, LastSupportHeight, SurfaceGrip = 1;
        public Vector3 SurfaceNormal = Vector3.up;
        public bool HasGround = true;
        // Stamina 为体力点数，Fatigue 累积后压低可恢复上限。
        // Balance 表示自身稳定程度，Footing 表示名义六足在台面上的支撑比例，均取 0～1。
        public float Stamina, Fatigue, Balance = 1, Footing = 1;
        // BurstReserve 是 0～1 的短时爆发余量；和可恢复耐力不同，不是第二条生命值。
        // 优势与接触记忆只影响选择，绝不作为额外伤害倍率。Simulation 是唯一写入者。
        public float BurstReserve = 1, ExertionCooldown, ExposureRemaining, ContactConfidence;
        public float TacticalAdvantage, IntentTime;
        public System.Random DecisionRandom;
        public ExchangePhase CombatPhase = ExchangePhase.Probing;
        public float BoutTime, BoutDuration = 2, TimeSinceAttack;
        public float ContactReleaseRemaining, ContactReleaseCooldown;
        // 找不到撬举支点后的短暂重试限制；期间可短推或换角度，避免原地重复昂贵失败。
        public float PryRetryRemaining;
        public BeetleIntent Intent = BeetleIntent.Assess;
        // 加油效果剩余秒数和按钮冷却秒数分开维护，效果结束不代表可以立即再加油。
        public float CheerRemaining, CheerCooldown;
        // 挑飞的双边冷却：成功后攻守双方都会获得，防止立即互相连续挑飞。
        public float TossCooldown;
        // 攻击者抬头驱动，由 Simulation 从受击者的托举阶段生成，动画只消费。
        public float LiftStrokeProgress, LiftSweepTarget;
        public int Throws; // 所有撬投次数；Tosses 仍表示额外爆发的猛挑，兼容已有统计。
        public BeetleAction Action = BeetleAction.Assess;
        // ActionTime/ActionDuration 为当前动作的秒数进度；DecisionTime 限制提前重新决策频率。
        public float ActionTime, ActionDuration = 1, DecisionTime;
        // 恢复片段跨 Recover/Retreat 重选保留计时，防止短动作无限续杯；Simulation 唯一写入。
        public float RecoveryTime, RecoveryCooldown;
        // Accepted contact geometry, sampled by the articulated solver. Render never writes these.
        public bool IsOverturned, RightingGrounded, RightingStable;
        public float OverturnedSeconds, RightingStableSeconds;
        public Vector3 RightingTorque;
        public int RightingSupports;
        // Landing friction is armed by a descending ground contact, never by time in flight.
        public bool LandingSliding;
        // Preserve the impact speed when the support-height fallback clears vertical velocity.
        public float LandingImpactSpeed;
        // 抬举、侧倾和冲击的表现权重/角度，随模拟时间衰减；不等于实际高度或受力。
        public float LiftVisual, RollVisual, ImpactVisual;
        // 统计已结算的命中、推顶、抬举、试探、冲顶、进入角力、挑飞成功和合格挑飞机会。
        // Contacts 在双方各增加一次，不是每帧接触数量；后者由 ContactSolver.ContactFrames 记录。
        public int Contacts, Shoves, Pries, ProbeTouches, Charges, Locks, Tosses, TossOpportunities;
        // Outgoing accepted-contact telemetry only; never used to choose a winner.
        public float AttackScore;
        public int ForelegActions, ForelegHits, ConsecutiveHornActions;
        public int FailedAttacks;
        // Airborne 决定是否受重力/禁用地面决策；HasFallen 是不可逆的本局落台标记。
        // AttackApplied 保证一个攻击动作只结算一次。
        public bool Airborne, HasFallen, AttackApplied;
        // 上次场景求解器是否检测并处理过双方重叠，供下一步命中条件使用。
        public bool TouchingOpponent;
        // 接触求解器写入的持续受阻时长，模拟只据此放弃推不动的动作，不绕过碰撞。
        public float ContactBlockedSeconds;
        // 固定的左右绕行偏好（-1 或 +1），也用于表现侧倾方向。
        public float CircleDirection;
        // 将当前动作秒数归一化为 0～1，并保护持续时间过小时的除法。
        public float ActionProgress => Mathf.Clamp01(ActionTime / Mathf.Max(.001f, ActionDuration));
        // 疲劳每增加 1，上限减少初始耐力的 0.7%；模拟将疲劳限制在 100 内。
        public float StaminaLimit => Traits.endurance * (1 - .007f * Fatigue);
        // 体力比例使用初始耐力作为分母，因此高疲劳时恢复满也可能显示不足 100%。
        public float Stamina01 => Mathf.Clamp01(Stamina / Traits.endurance);
        // 决策用当前可恢复上限；实际力量仍读 Stamina01，疲劳不会因此被消除。
        public float Readiness01 => Mathf.Clamp01(Stamina / Mathf.Max(.001f, StaminaLimit));
        // 将偏航角变为世界平面的正前方：0° 朝 +Z，90° 朝 +X。
        public Vector2 Forward => new Vector2(Mathf.Sin(Heading * Mathf.Deg2Rad), Mathf.Cos(Heading * Mathf.Deg2Rad));
        // 正在蓄力、发力或角力的动作集合，用于限制转向、控制迈步和累积疲劳。
        public bool Committed => Action == BeetleAction.Module || Action == BeetleAction.ShoveWindup || Action ==
            BeetleAction.Shove || Action == BeetleAction.PryWindup || Action == BeetleAction.Pry || Action ==
            BeetleAction.ChargeWindup || Action == BeetleAction.Charge || Action == BeetleAction.HornLock || Action ==
            BeetleAction.Toss || Action == BeetleAction.Lifting || Action == BeetleAction.ReleaseLift;
        // 初始化身份、站位和体力；其余字段使用声明处默认值，每局创建新状态。
        public BeetleState(int index, BeetleTraits traits, Vector2 position, float heading)
        {
            Index = index;
            Traits = traits;
            Position = position;
            Heading = heading;
            Stamina = traits.endurance;
        }
    }

    // 轻量事件快照：Message 给 HUD，Actor 为选手索引（-1 表示全局），Intensity 为展示强度提示；碰撞音效已改由真实接触采样驱动。
    public readonly struct ContestEvent
    {
        public readonly string Message;
        public readonly int Actor;
        public readonly float Intensity;
        // 构造事件时固定文本和反馈强度；订阅者收到后不修改原始事件。
        public ContestEvent(string message, int actor = -1, float intensity = 0)
        {
            Message = message;
            Actor = actor;
            Intensity = intensity;
        }
    }
}
