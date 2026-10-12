using System;
using UnityEngine;

namespace BeetleArena
{
    // 比赛阶段：准备倒计时、正常格斗、首次落台后的双落台等待、最终结束。
    public enum MatchPhase { Countdown, Fighting, Resolving, Finished }
    // 单只甲虫的动作状态。Windup 是准备动作，Pry 是抬举，Toss 是成功猛挑，Launched 是被挑飞。
    // 后续新增枚举值追加到末尾，避免旧的序列化整数映射到错误动作。
    public enum BeetleAction { Assess, Approach, Circle, ShoveWindup, Shove, PryWindup, Pry, Brace, Recover, Retreat, Stagger, Falling, Defeated, Winner, Probe, Disengage, ChargeWindup, Charge, HornLock, Toss, Launched, Lifting, Lifted, ReleaseLift, Module }
    // 短周期交锋节奏，与 MatchPhase 的整场流程、BeetleAction 的个体动作分层管理。
    public enum ExchangePhase { Probing, Clash, Recovery }

    [Serializable]
    // 本局个体属性，在创建后按只读数据使用；与每步变化的 BeetleState 分离。
    public sealed class BeetleTraits
    {
        // size 为基准体型倍率；lengthMm/massGrams 只供展示；mass 为归一化运动质量。
        // strength/grip 分别控制发力和抓地，endurance 为耐力上限，aggression 为攻击倾向，
        // agility 影响移动/转向，hornReach 用于简化接触模式的触及距离。
        public float size, lengthMm, massGrams, mass, strength, grip, endurance, aggression, agility, hornReach;
        // 依据体型与同一个局内随机源生成属性。质量按 size³、力量按 size² 变化，
        // 再给予小幅个体差异；这些是游戏近似，不是物种实验数据拟合。
        public static BeetleTraits Create(float size, System.Random random, BeetleMatchSettings settings)
        {
            float Vary() => .95f + (float)random.NextDouble() * .10f;
            return new BeetleTraits
            {
                size = size,
                lengthMm = settings.referenceLengthMm * size,
                massGrams = settings.referenceMassGrams * size * size * size,
                mass = size * size * size,
                strength = size * size * Vary(),
                grip = size * Vary(),
                endurance = 100 * Vary(),
                aggression = .45f + (float)random.NextDouble() * .3f,
                agility = Vary() / Mathf.Sqrt(size),
                hornReach = 1.65f * size
            };
        }
    }

    // 单个选手的全部动态数据。模拟写入，Actor/动画/HUD 读取；本类不依赖场景对象。
    public sealed class BeetleState
    {
        // 固定身份索引，0 为琥珀、1 为墨甲。Traits 引用在一局中保持不变。
        public readonly int Index;
        public readonly BeetleTraits Traits;
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
        // 抬举、侧倾和冲击的表现权重/角度，随模拟时间衰减；不等于实际高度或受力。
        public float LiftVisual, RollVisual, ImpactVisual;
        // 统计已结算的命中、推顶、抬举、试探、冲顶、进入角力、挑飞成功和合格挑飞机会。
        // Contacts 在双方各增加一次，不是每帧接触数量；后者由 ContactSolver.ContactFrames 记录。
        public int Contacts, Shoves, Pries, ProbeTouches, Charges, Locks, Tosses, TossOpportunities;
        // Airborne 决定是否受重力/禁用地面决策；HasFallen 是不可逆的本局落台标记。
        // AttackApplied 保证一个攻击动作只结算一次。
        public bool Airborne, HasFallen, AttackApplied;
        // 上次场景求解器是否检测并处理过双方重叠，供下一步命中条件使用。
        public bool TouchingOpponent;
        // 固定的左右绕行偏好（-1 或 +1），也用于表现侧倾方向。
        public float CircleDirection;
        // 将当前动作秒数归一化为 0～1，并保护持续时间过小时的除法。
        public float ActionProgress => Mathf.Clamp01(ActionTime / Mathf.Max(.001f, ActionDuration));
        // 疲劳每增加 1，上限减少初始耐力的 0.7%；模拟将疲劳限制在 100 内。
        public float StaminaLimit => Traits.endurance * (1 - .007f * Fatigue);
        // 体力比例使用初始耐力作为分母，因此高疲劳时恢复满也可能显示不足 100%。
        public float Stamina01 => Mathf.Clamp01(Stamina / Traits.endurance);
        // 将偏航角变为世界平面的正前方：0° 朝 +Z，90° 朝 +X。
        public Vector2 Forward => new Vector2(Mathf.Sin(Heading * Mathf.Deg2Rad), Mathf.Cos(Heading * Mathf.Deg2Rad));
        // 正在蓄力、发力或角力的动作集合，用于限制转向、控制迈步和累积疲劳。
        public bool Committed => Action == BeetleAction.Module || Action == BeetleAction.ShoveWindup || Action == BeetleAction.Shove || Action == BeetleAction.PryWindup || Action == BeetleAction.Pry ||
            Action == BeetleAction.ChargeWindup || Action == BeetleAction.Charge || Action == BeetleAction.HornLock || Action == BeetleAction.Toss || Action == BeetleAction.Lifting || Action == BeetleAction.ReleaseLift;

        // 初始化身份、站位和体力；其余字段使用声明处默认值，每局创建新状态。
        public BeetleState(int index, BeetleTraits traits, Vector2 position, float heading)
        {
            Index = index; Traits = traits; Position = position; Heading = heading; Stamina = traits.endurance;
        }
    }

    // 轻量事件快照：Message 给 HUD，Actor 为选手索引（-1 表示全局），Intensity 给音效。
    public readonly struct ContestEvent
    {
        public readonly string Message;
        public readonly int Actor;
        public readonly float Intensity;
        // 构造事件时固定文本和反馈强度；订阅者收到后不修改原始事件。
        public ContestEvent(string message, int actor = -1, float intensity = 0) { Message = message; Actor = actor; Intensity = intensity; }
    }
}
