using System;
using UnityEngine;

namespace BeetleArena
{
    // Authoritative, fixed-step contest. No GameObjects, rendering, input or wall-clock dependency.
    // Forces use normalized game units; lengths/masses shown in the HUD are descriptive specimens.
    // 战斗规则核心：保存本局数据，按固定步长推进决策、命中、移动与胜负。
    // 本类不直接操作场景对象；注入 ContactSolver 后，碰撞阶段会调用依赖场景的求解器。
    // 这里的力量/质量是归一化游戏数值，HUD 的毫米/克是展示属性，不能直接当 SI 物理量代入。
    public sealed class BeetleSimulation
    {
        // 固定模拟步长：0.02 秒，即每秒 50 步。由 MatchController 的累积时间循环调用。
        public const float FixedStep = .02f;
        // 索引 0/1 始终对应两位选手；HUD、模型和事件 Actor 都使用同一索引。
        public readonly BeetleState[] Fighters = new BeetleState[2];
        public readonly BeetleMatchSettings Settings;
        public readonly int Seed;
        public MatchPhase Phase { get; private set; } = MatchPhase.Countdown;
        // 正式比赛累计秒数，不包含开场倒计时。胜负结算等待期间仍累计。
        public float Elapsed { get; private set; }
        // 独立固定步动作时钟：倒计时/比赛结束后仍走，暂停不走，渲染只读。
        public float MotionTime { get; private set; }
        public float Countdown { get; private set; }
        public int Winner { get; private set; } = -1; // -1: undecided or double-fall draw
        public int EventCount { get; private set; }
        public event Action<ContestEvent> EventRaised;
        // 实际游戏注入细分网格碰撞求解器；未注入时使用简化身体分离，便于纯数据测试。
        public IBeetleContactSolver ContactSolver { get; set; }
        // 场景注入同一地表给模拟与 IK；null 保留旧纯数据测试的水平圆台。
        public IBeetleGround Ground { get; private set; }

        public void SetGround(IBeetleGround ground)
        {
            Ground = ground;
            foreach (var b in Fighters)
            {
                UpdateGround(b);
                if (b.HasGround) b.Height = b.LastSupportHeight = b.SurfaceHeight;
            }
        }

        public bool SampleGround(Vector2 position, out BeetleGroundSample sample)
        {
            if (Ground != null) return Ground.Sample(position, out sample);
            sample = new BeetleGroundSample { height = 0, normal = Vector3.up, grip = 1 };
            return position.magnitude <= Settings.arenaRadius;
        }

        // 以根部高度控制接地，用六个名义足位平均法线稳定身体朝向；不从可视帧反写状态。
        void UpdateGround(BeetleState b)
        {
            b.HasGround = SampleGround(b.Position, out var center);
            if (!b.HasGround) { b.Footing = 0; return; }
            b.SurfaceHeight = center.height; b.SurfaceGrip = center.grip;
            Vector3 normal = center.normal;
            float support = 0;
            Vector2 right = new Vector2(b.Forward.y, -b.Forward.x);
            for (int side = -1; side <= 1; side += 2)
                for (int pair = 0; pair < 3; pair++)
                {
                    Vector2 foot;
                    if (Settings.supportFootOffsets != null && Settings.supportFootOffsets.Length == 6)
                    {
                        Vector3 local = Settings.supportFootOffsets[(side < 0 ? 0 : 3) + pair] * Settings.visualScale * b.Traits.size;
                        foot = b.Position + right * local.x + b.Forward * local.z;
                    }
                    else foot = b.Position + right * (side * 1.10f * b.Traits.size) + b.Forward * ((1 - pair) * .9f * b.Traits.size);
                    if (!SampleGround(foot, out var hit)) continue;
                    // 过大的局部高度差超出腿部可用行程，不计成有效支撑。
                    float reach = 1 - Mathf.InverseLerp(.45f * b.Traits.size, .85f * b.Traits.size, Mathf.Abs(hit.height - center.height));
                    support += reach; normal += hit.normal * reach;
                }
            b.SurfaceNormal = normal.normalized;
            b.Footing = Ground == null ? ComputeFooting(b) : support / 6;
            if (!b.Airborne) b.LastSupportHeight = b.SurfaceHeight;
        }
        // 局内随机源：构造时由种子初始化。重放还要求相同输入时序、参数和随机调用路径。
        // 涉及场景物理时，种子本身不提供跨平台逐帧一致性保证。
        readonly System.Random random;
        // AI 本步希望达到的平面速度；与已经包含被推冲量的实际 Velocity 分开保存。
        readonly Vector2[] desiredVelocity = new Vector2[2];
        // 先缓存双方的命中，再应用效果，避免先处理的一次攻击取消对方已经形成的命中。
        // 效果仍按索引顺序写入状态，因此不能视为所有受力计算都完全同步。
        readonly Hit[] hits = new Hit[2];
        readonly BeetleTacticalSnapshot[] tactics = new BeetleTacticalSnapshot[2];
        // 首次落台后等待另一方是否也落台的计时；exchangeTime 则负责短周期交锋节奏。
        float resolutionTime;
        float exchangeTime, recoveryWindow;
        public ExchangePhase Exchange { get; private set; } = ExchangePhase.Probing;
        // 模型长角会先于身体接触，所以网格求解与简化数据模拟采用不同的交战距离。
        float EngagementDistance => ContactSolver != null ? 4.15f : 2.85f;
        // Early contests consist of restrained pushing. Sustained engagement escalates
        // displacement and fatigue; this never blocks a fall or chooses a winner.
        // 随比赛时间平滑增加位移和疲劳强度，取值 0～1；它不指定胜者，也不阻止提前落台。
        public float Escalation => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Settings.openingSeconds,
            Settings.openingSeconds + Settings.escalationSeconds, Elapsed));

        // 一次有效接触的快照：攻击类别、水平推力方向、归一化发力、杠杆加成及视觉侧倾。
        struct Hit { public bool valid, pry, probe, charge; public Vector2 direction; public float force, leverage, roll; }

        // 生成双方个体和初始站位。先抽 A 的大小，再约束 B 的可抽范围，限制较大/较小的比例差。
        // Heading 使用度数，0 朝世界 +Z，90 朝 +X；模拟 Vector2 的第二分量对应世界 Z。
        public BeetleSimulation(BeetleMatchSettings settings, int seed)
        {
            Settings = settings ? settings : throw new ArgumentNullException(nameof(settings));
            Seed = seed;
            random = new System.Random(seed);
            float sizeA = Range(settings.minimumSize, settings.maximumSize);
            float sizeB = Range(Mathf.Max(settings.minimumSize, sizeA / (1 + settings.maximumSizeDifference)),
                Mathf.Min(settings.maximumSize, sizeA * (1 + settings.maximumSizeDifference)));
            Fighters[0] = new BeetleState(0, BeetleTraits.Create(sizeA, random, settings), new Vector2(-settings.spawnDistance, 0), 90);
            Fighters[1] = new BeetleState(1, BeetleTraits.Create(sizeB, random, settings), new Vector2(settings.spawnDistance, 0), -90);
            for (int i = 0; i < 2; i++) { Fighters[i].ActionDuration = Range(.7f, 1.2f); Fighters[i].CircleDirection = random.Next(2) == 0 ? -1 : 1; }
            Countdown = settings.countdownSeconds;
            foreach (var b in Fighters) b.Motion.Refresh(this, b);
        }

        // 玩家唯一的战斗干预：补少量体力、临时提升发力，并启动冷却。
        // 返回是否接受；不直接修改位置、速度或动作，不能当作方向键或攻击按钮。
        public bool Encourage(int index)
        {
            if (Phase != MatchPhase.Fighting || index < 0 || index >= Fighters.Length) return false;
            var b = Fighters[index];
            if (b.CheerCooldown > 0 || b.HasFallen || b.Airborne) return false;
            b.CheerCooldown = Settings.cheerCooldown;
            b.CheerRemaining = Settings.cheerDuration;
            b.Stamina = Mathf.Min(b.StaminaLimit, b.Stamina + Settings.cheerStamina);
            Raise(Name(index) + "得到鼓励，重新发力", index);
            return true;
        }

        // 单步流程：倒计时 → 更新计时/AI → 缓存双方命中 → 应用命中 → 积分移动
        // → 碰撞修正 → 落台标记 → 胜负结算。dt 必须有效且不超过 0.05 秒。
        public void Step(float dt)
        {
            if (dt <= 0 || dt > .05f || float.IsNaN(dt) || float.IsInfinity(dt))
                throw new ArgumentOutOfRangeException(nameof(dt), "Use fixed timesteps of at most 0.05 seconds.");
            MotionTime += dt;
            // 决策视图成对缓存；模块是否到期、双方本步动作修改都不能改变另一只的观测时点。
            for (int i = 0; i < 2; i++)
            {
                var b = Fighters[i];
                if (Phase == MatchPhase.Fighting)
                {
                    BeetleEnergyPolicy.TickClocks(b, dt);
                }
            }
            for (int i = 0; i < 2; i++)
                tactics[i] = new BeetleTacticalSnapshot(Fighters[i], Fighters[1 - i], Settings.arenaRadius, EngagementDistance);
            for (int i = 0; i < 2; i++)
            {
                var b = Fighters[i];
                if (Phase != MatchPhase.Fighting || b.Airborne || b.HasFallen) continue;
                b.TacticalAdvantage = Mathf.Lerp(b.TacticalAdvantage, tactics[i].Advantage, 1 - Mathf.Exp(-dt * 4));
                if (b.IntentTime < Settings.tacticalResponseSeconds) continue;
                var intent = BeetleTactics.Evaluate(b, tactics[i]);
                if (intent != b.Intent) { b.Intent = intent; b.IntentTime = 0; }
            }
            foreach (var b in Fighters)
            {
                b.Motion.Refresh(this, b);
                b.Motion.Tick(dt);
                if (b.Action == BeetleAction.Module && !b.Motion.HasActive)
                    SetAction(b, BeetleAction.Brace, .30f);
            }
            // 结算后仍让已落台的甲虫下落、沉降并接受接触修正，避免悬在半空。
            if (Phase == MatchPhase.Finished)
            {
                foreach (var fighter in Fighters)
                    if (fighter.HasFallen) Integrate(fighter, Vector2.zero, dt);
                ContactSolver?.Solve(this, dt);
                return;
            }
            if (Phase == MatchPhase.Countdown)
            {
                Countdown = Mathf.Max(0, Countdown - dt);
                if (Countdown == 0) { Phase = MatchPhase.Fighting; Raise("比赛开始 · 双方正在试探"); }
                return;
            }
            Elapsed += dt;
            if (Phase == MatchPhase.Fighting) UpdateExchange(dt);
            UpdateLiftResponses(dt);
            for (int i = 0; i < 2; i++)
            {
                var b = Fighters[i];
                b.CheerCooldown = Mathf.Max(0, b.CheerCooldown - dt);
                b.CheerRemaining = Mathf.Max(0, b.CheerRemaining - dt);
                b.TossCooldown = Mathf.Max(0, b.TossCooldown - dt);
                b.ImpactVisual = Mathf.MoveTowards(b.ImpactVisual, 0, dt * 3);
                b.LiftVisual = Mathf.MoveTowards(b.LiftVisual, 0, dt * 1.5f);
                b.RollVisual = Mathf.MoveTowards(b.RollVisual, 0, dt * 35);
                b.RecoveryCooldown = Mathf.Max(0, b.RecoveryCooldown - dt);
                if (BeetleRecoveryPolicy.IsRest(b.Action)) b.RecoveryTime += dt;
                b.ActionTime += dt;
                b.DecisionTime -= dt;
                hits[i] = default;
                desiredVelocity[i] = Vector2.zero;
                if (!b.HasFallen && !b.Airborne && b.Action != BeetleAction.Lifting && Phase == MatchPhase.Fighting)
                    Think(b, Fighters[1 - i], dt);
            }
            // Both attacks are calculated before either is applied: index order cannot cancel an attack.
            for (int i = 0; i < 2; i++) hits[i] = ComputeHit(Fighters[i], Fighters[1 - i]);
            for (int i = 0; i < 2; i++) if (hits[i].valid) ApplyHit(Fighters[i], Fighters[1 - i], hits[i]);
            for (int i = 0; i < 2; i++) Integrate(Fighters[i], desiredVelocity[i], dt);
            // 先处理真实接触再判断高度；防止使用尚未修正的穿透位置错误判负。
            if (ContactSolver != null) ContactSolver.Solve(this, dt);
            else ResolveBodyContact();
            ApplyModuleContacts();
            foreach (var fighter in Fighters)
            {
                if (!fighter.HasFallen && (!fighter.HasGround || fighter.Height < fighter.SurfaceHeight - Settings.fallDepth)
                    && fighter.Height < fighter.LastSupportHeight - Settings.fallDepth)
                {
                    fighter.Fall.Begin(fighter);
                    fighter.HasFallen = true;
                    SetAction(fighter, BeetleAction.Defeated, 1);
                    Raise(Name(fighter.Index) + "掉下擂台", fighter.Index, 1);
                }
            }
            CheckResult(dt);
        }

        // 外层状态机：试探、激烈交锋、恢复循环。它决定可选动作，不直接产生攻击效果。
        void UpdateExchange(float dt)
        {
            exchangeTime += dt;
            float duration = Exchange == ExchangePhase.Probing ? Settings.probingSeconds :
                Exchange == ExchangePhase.Clash ? Settings.clashSeconds : recoveryWindow;
            // 恢复只是降低交锋强度的机会，不是同步休息哨。接触中再次发力可提前结束窗口。
            bool renewedContact = Exchange == ExchangePhase.Recovery && exchangeTime >= .45f &&
                (Fighters[0].TouchingOpponent || Fighters[1].TouchingOpponent) &&
                (Fighters[0].Committed || Fighters[1].Committed);
            if (exchangeTime < duration && !renewedContact) return;
            exchangeTime = 0;
            Exchange = Exchange == ExchangePhase.Probing ? ExchangePhase.Clash :
                Exchange == ExchangePhase.Clash ? ExchangePhase.Recovery : ExchangePhase.Probing;
            if (Exchange == ExchangePhase.Recovery)
                recoveryWindow = Settings.exchangeRecoverySeconds * Range(.8f, 1.15f);
            Raise(Exchange == ExchangePhase.Clash ? "交锋升级 · 蓄力争夺支点" :
                Exchange == ExchangePhase.Recovery ? "短暂重整 · 寻触后再交锋" : "重新试探 · 寻找入角机会");
            // Let committed moves finish, but release idle decisions promptly.
            foreach (var b in Fighters)
                if (!b.Committed && !b.Airborne && !b.HasFallen && b.Action != BeetleAction.Stagger)
                    b.ActionTime = b.ActionDuration;
        }

        // 内层动作执行器：对准对手，处理动作结束后的衔接，产生本步期望速度和体力消耗。
        // facing 为正面与对手方向的点积；edge 为距中心/台面半径，接近 1 表示靠近边沿。
        void Think(BeetleState b, BeetleState opponent, float dt)
        {
            Vector2 toward = opponent.Position - b.Position;
            float distance = toward.magnitude;
            toward = distance > .001f ? toward / distance : b.Forward;
            float facing = Vector2.Dot(b.Forward, toward);
            float edge = b.Position.magnitude / Settings.arenaRadius;
            bool threatened = tactics[b.Index].Threatened;
            bool finishedAction = b.ActionTime >= b.ActionDuration;
            // 几何反馈表明动作持续推不动时先卸力，不能等长蓄力/模块一直顶在原地。
            // 这里只改动作意图；脚端、身体与撤步仍完整经过接触预算，不直接移动根节点。
            if (b.ContactBlockedSeconds > .6f && b.Action != BeetleAction.Disengage && b.Action != BeetleAction.Circle)
            {
                SetAction(b, BeetleAction.Disengage, .34f);
                finishedAction = false;
            }
            float targetHeading = Mathf.Atan2(toward.x, toward.y) * Mathf.Rad2Deg;
            // 蓄力和发力时降低转向速度，让攻击有承诺时间，避免冲顶过程中瞬间追踪侧方目标。
            float turn = b.Committed ? .32f : BeetleRecoveryPolicy.IsRest(b.Action) ? .45f : 1f;
            b.Heading = Mathf.MoveTowardsAngle(b.Heading, targetHeading, Settings.turnSpeed * b.Traits.agility * turn * dt);

            // 恢复有明确出口：到达体力目标/单段上限，或对手正在近距离发力。
            // 先经 SetAction 记录退出冷却，避免 Decide 在同一步再次选择 Recover。
            if (BeetleRecoveryPolicy.Ready(b, Settings, threatened))
            {
                SetAction(b, threatened ? BeetleAction.Brace : BeetleAction.Assess, threatened ? .32f : 0);
                finishedAction = !threatened;
            }

            // 优先完成连贯动作链：蓄力→冲顶→接触角力→可能抬举；其余动作结束才重新决策。
            if (b.Action == BeetleAction.Module && b.Motion.HasActive) finishedAction = false;
            if (finishedAction)
            {
                switch (b.Action)
                {
                    case BeetleAction.Probe:
                        // 有接触优势可接短推，疲劳/中性试探才收步；不再每次探角都强制后退。
                        if (b.AttackApplied && (b.Intent == BeetleIntent.Pressure || b.Intent == BeetleIntent.Counter) && BeetleTactics.CanCommit(b, 22, .3f))
                            SetAction(b, BeetleAction.ShoveWindup, Range(.22f, .30f));
                        else SetAction(b, BeetleAction.Disengage, Range(.28f, .48f));
                        break;
                    case BeetleAction.ChargeWindup: SetAction(b, BeetleAction.Charge, .48f); break;
                    case BeetleAction.Charge:
                        if (b.AttackApplied && BeetleTactics.CanCommit(b, 24, .30f) && distance < EngagementDistance + .25f && !opponent.Airborne)
                        { SetAction(b, BeetleAction.HornLock, Settings.hornLockSeconds); b.Locks++; }
                        else SetAction(b, BeetleAction.Brace, .45f);
                        break;
                    case BeetleAction.HornLock:
                        SetAction(b, b.PryRetryRemaining <= 0 && (Exchange == ExchangePhase.Clash || b.Intent == BeetleIntent.Pressure) && BeetleTactics.CanCommit(b, 32, .48f) && distance < EngagementDistance && !opponent.Airborne
                            ? BeetleAction.PryWindup : BeetleAction.Disengage, .55f);
                        break;
                    case BeetleAction.ShoveWindup: SetAction(b, BeetleAction.Shove, .34f); break;
                    case BeetleAction.PryWindup: SetAction(b, BeetleAction.Pry, 2.15f); break;
                    case BeetleAction.Pry:
                        if (!b.AttackApplied)
                        {
                            b.PryRetryRemaining = Settings.failedPryRetrySeconds;
                            SetAction(b, BeetleAction.Circle, .48f);
                        }
                        else SetAction(b, BeetleAction.Brace, Range(.24f, .38f));
                        break;
                    case BeetleAction.Shove:
                    case BeetleAction.Toss:
                        SetAction(b, BeetleAction.Brace, b.AttackApplied ? Range(.24f, .38f) : Settings.missedAttackExposure); break;
                    case BeetleAction.ReleaseLift: SetAction(b, BeetleAction.Brace, .4f); break;
                    case BeetleAction.Stagger: SetAction(b, BeetleAction.Brace, .5f); break;
                    default: Decide(b, opponent, distance, facing, edge, threatened); break;
                }
            }

            float effort = .48f + .52f * b.Stamina01;
            float speed = Settings.walkSpeed * b.Traits.agility * effort;
            // 消耗单位为体力点/秒；负值表示恢复。步末统一乘 dt，避免消耗依赖显示帧率。
            float staminaCost = 0;
            switch (b.Action)
            {
                case BeetleAction.Module:
                    var sample = b.Motion.CombatSample;
                    Vector2 localRight = new Vector2(b.Forward.y, -b.Forward.x);
                    desiredVelocity[b.Index] = (localRight * sample.LocalVelocity.x + b.Forward * sample.LocalVelocity.y) * effort;
                    staminaCost = Mathf.Clamp(sample.StaminaPerSecond, 0, 30);
                    break;
                case BeetleAction.Approach:
                    desiredVelocity[b.Index] = toward * speed * Mathf.Clamp01((facing + .1f) / .8f);
                    staminaCost = 1.5f;
                    if (distance < EngagementDistance && b.DecisionTime <= 0) Decide(b, opponent, distance, facing, edge, threatened);
                    break;
                case BeetleAction.Circle:
                    Vector2 tangent = new Vector2(-toward.y, toward.x) * b.CircleDirection;
                    Vector2 inward = -b.Position.normalized * Mathf.InverseLerp(.83f, 1f, edge);
                    desiredVelocity[b.Index] = (tangent * .6f + toward * Mathf.Clamp(distance - EngagementDistance, -.5f, .5f) + inward * 1.2f).normalized * speed * .7f;
                    staminaCost = b.Intent == BeetleIntent.Reposition || b.Intent == BeetleIntent.Recover ? -Settings.staminaRecovery * .50f : 1.3f;
                    break;
                case BeetleAction.Probe: desiredVelocity[b.Index] = toward * speed * .50f; staminaCost = 3f; break;
                case BeetleAction.Disengage:
                    desiredVelocity[b.Index] = -toward * speed * .40f;
                    staminaCost = -Settings.staminaRecovery * .7f; break;
                case BeetleAction.ChargeWindup: desiredVelocity[b.Index] = -toward * speed * .15f; staminaCost = 5f; break;
                case BeetleAction.Charge: desiredVelocity[b.Index] = b.Forward * speed * 2.4f; staminaCost = Settings.chargeStaminaCost; break;
                case BeetleAction.HornLock:
                    if (distance > EngagementDistance + .35f || opponent.Airborne)
                        SetAction(b, BeetleAction.Brace, .35f);
                    else { desiredVelocity[b.Index] = b.Forward * speed * .65f; staminaCost = Settings.hornLockStaminaCost; }
                    break;
                case BeetleAction.ShoveWindup: desiredVelocity[b.Index] = toward * speed * .18f; staminaCost = 4f; break;
                case BeetleAction.Shove: desiredVelocity[b.Index] = b.Forward * speed * 1.5f; staminaCost = 19f; break;
                case BeetleAction.PryWindup: desiredVelocity[b.Index] = b.Forward * speed * .72f; staminaCost = 6f; break;
                case BeetleAction.Pry:
                    // 低角略向一侧寻找胸腹下缘，避免两根叉角一直共面互顶。
                    Vector2 prySide = new Vector2(-b.Forward.y, b.Forward.x) * b.CircleDirection;
                    desiredVelocity[b.Index] = (b.Forward + prySide * .3f) * speed * 1.15f; staminaCost = 12f; break;
                case BeetleAction.Toss: staminaCost = 4f; break;
                case BeetleAction.ReleaseLift: desiredVelocity[b.Index] = -b.Forward * speed * .7f; staminaCost = 1; break;
                case BeetleAction.Brace: staminaCost = -Settings.staminaRecovery * .55f; break;
                case BeetleAction.Recover: staminaCost = -Settings.staminaRecovery; break;
                case BeetleAction.Retreat:
                    desiredVelocity[b.Index] = (-toward * .65f - b.Position.normalized * Mathf.InverseLerp(.45f, .90f, edge)).normalized * speed * .65f;
                    staminaCost = -Settings.staminaRecovery * .65f;
                    break;
                case BeetleAction.Assess: staminaCost = -Settings.staminaRecovery * .45f; break;
            }
            // A planted beetle avoids stepping backwards into empty space. Only
            // voluntary outward walking is reduced; received momentum still carries it out.
            // 只限制 AI 主动向台外走的分量，不钳制实际位置或受击速度，因此仍然能被推下台。
            desiredVelocity[b.Index] *= InsectMuscleAnimation.Locomotion(b);
            Vector2 radial = b.Position.normalized;
            float outward = Vector2.Dot(desiredVelocity[b.Index], radial);
            if (outward > 0) desiredVelocity[b.Index] -= radial * outward * Mathf.InverseLerp(.65f, .88f, edge);
            if (b.Committed || threatened) b.Fatigue = Mathf.Min(100, b.Fatigue + Settings.fatiguePerSecond * Mathf.Lerp(1, 3, Escalation) * dt);
            BeetleEnergyPolicy.Apply(b, Settings, staminaCost, threatened, dt);
            b.Balance = Mathf.MoveTowards(b.Balance, 1, dt * (b.Action == BeetleAction.Brace || b.Action == BeetleAction.Recover ? .40f : .12f));
        }

        // 按优先级选择下一动作：个体短恢复/威胁响应 → 调整朝向/距离 → 防守 → 试探或进攻。
        // 一个 choice 会参与多个顺序分支；各阈值不是相互独立的攻击概率。
        void Decide(BeetleState b, BeetleState opponent, float distance, float facing, float edge, bool threatened)
        {
            // 限制接近过程中提前重选动作的频率，避免每个模拟步都改变主意。
            b.DecisionTime = .3f;
            var view = tactics[b.Index];
            if (BeetleRecoveryPolicy.IsRest(b.Action) && !BeetleRecoveryPolicy.Ready(b, Settings, threatened))
            {
#if UNITY_EDITOR
                BeetleDebugHooks.Choose(this,b.Index,false,-1,"recovery-not-ready");
#endif
                SetAction(b, distance < EngagementDistance - .1f && edge < .76f ? BeetleAction.Retreat : BeetleAction.Recover, Range(.35f, .6f));
                return;
            }
            if (BeetleRecoveryPolicy.WantsRest(b, Settings, Exchange == ExchangePhase.Recovery, threatened))
            {
#if UNITY_EDITOR
                BeetleDebugHooks.Choose(this,b.Index,false,-1,"recovery-requested");
#endif
                SetAction(b, distance < EngagementDistance && edge < .76f ? BeetleAction.Retreat : BeetleAction.Recover, Range(.4f, .65f));
                return;
            }
            float choice = Range(0, 1);
            var selected = BeetleTactics.Choose(b, view, Exchange, EngagementDistance, choice);
            // 抢收招优先短推；受压先防守，不能被一个随机模块覆盖掉刚做出的战术选择。
            bool permitModule = !threatened && b.Intent != BeetleIntent.Defend && b.Intent != BeetleIntent.Recover && b.Intent != BeetleIntent.Reposition &&
                b.Intent != BeetleIntent.Counter && facing > .7f && distance < EngagementDistance && b.Stamina > 24 && b.BurstReserve > .30f;
#if UNITY_EDITOR
            BeetleDebugHooks.Choose(this,b.Index,permitModule,choice,
                threatened ? "opponent-threat" : b.Intent == BeetleIntent.Defend || b.Intent == BeetleIntent.Recover || b.Intent == BeetleIntent.Reposition || b.Intent == BeetleIntent.Counter ? "tactical-intent" :
                facing <= .7f ? "facing" : distance >= EngagementDistance ? "distance" : b.Stamina <= 24 ? "stamina" : b.BurstReserve <= .30f ? "burst-reserve" : choice >= Settings.moduleActionChance ? "random-gate" : "module-eligibility");
#endif
            if (permitModule && choice < Settings.moduleActionChance && b.Motion.TryStart(choice / Mathf.Max(.001f, Settings.moduleActionChance)))
            {
                SetAction(b, BeetleAction.Module, b.Motion.Active.duration);
                Raise(Name(b.Index) + b.Motion.Active.DisplayName, b.Index, .25f);
                return;
            }
            float duration = selected == BeetleAction.ChargeWindup ? Settings.chargeWindupSeconds * Range(.9f, 1.1f) :
                selected == BeetleAction.PryWindup ? .62f : selected == BeetleAction.ShoveWindup ? Range(.22f, .34f) :
                selected == BeetleAction.Brace ? Range(.28f, .48f) : selected == BeetleAction.Assess ? .24f :
                selected == BeetleAction.Disengage ? .34f : selected == BeetleAction.Circle ? Range(.45f, .72f) : Range(.48f, .76f);
            SetAction(b, selected, duration);
        }

        // 检查一次攻击是否进入生效窗口；只有通过范围、朝向和接触条件才设置 AttackApplied。
        // 同一动作可以等待后续接触，但一旦结算过就不会每帧重复命中。
        Hit ComputeHit(BeetleState b, BeetleState opponent)
        {
            if (Phase != MatchPhase.Fighting || b.HasFallen || b.Airborne || b.AttackApplied || opponent.HasFallen || opponent.Lift.Supported) return default;
            bool pry = b.Action == BeetleAction.Pry;
            bool probe = b.Action == BeetleAction.Probe, charge = b.Action == BeetleAction.Charge;
            if (!pry && !probe && !charge && b.Action != BeetleAction.Shove) return default;
            if (b.ActionProgress < (pry ? Settings.pryContactStart : .32f)) return default;
            Vector2 delta = opponent.Position - b.Position;
            float distance = delta.magnitude;
            Vector2 direction = distance > .001f ? delta / distance : b.Forward;
            float reach = pry ? 2.48f * b.Traits.size : (b.Traits.hornReach + opponent.Traits.hornReach) * .88f;
            float alignment = Vector2.Dot(b.Forward, direction);
            if (ContactSolver != null)
            {
                reach = 4.4f * b.Traits.size;
                // 使用上一次接触求解的双方接触标记。它表示部件之间有接触，未细分角尖/腹部接触点。
                if (!b.TouchingOpponent) return default;
            }
            if (distance > reach || alignment < .70f || Mathf.Abs(opponent.Height - b.Height) > .7f) return default;
            b.AttackApplied = true;
            float effort = BeetleEnergyPolicy.AttackEffort(b);
            if (b.CheerRemaining > 0) effort *= 1 + Settings.cheerEffortBonus;
            // 自身平衡和足部支撑影响有效发力；鼓励对 effort 的加成已在上一段计算。
            float load = Mathf.Lerp(.5f, 1, b.Balance) * Mathf.Lerp(.35f, 1, b.Footing);
            float defenseAngle = Vector2.Dot(opponent.Forward, -direction);
            // 抬举时侧向接触获得较大杠杆系数；这是行为近似，并未精确求解真实角尖支点。
            float leverage = pry ? Mathf.Lerp(1.55f, 1.16f, Mathf.InverseLerp(-.4f, 1, defenseAngle)) : 1;
            float multiplier = probe ? Settings.probeForceMultiplier : charge ? Settings.chargeForceMultiplier : 1;
            return new Hit { valid = true, pry = pry, probe = probe, charge = charge, direction = direction, force = b.Traits.strength * effort * load * alignment * multiplier,
                leverage = leverage, roll = b.CircleDirection * Range(12, 28) };
        }

        // 将命中转化为双方速度变化、体力/平衡变化和反馈事件。
        // 抵抗能力由抓地、体力、平衡、足部支撑以及撑地/角力状态共同决定。
        void ApplyHit(BeetleState attacker, BeetleState defender, Hit hit)
        {
            float brace = defender.Action == BeetleAction.Brace || defender.Action == BeetleAction.HornLock ? 1.28f : 1;
            float resistance = defender.Traits.grip * (.35f + .65f * defender.Stamina01) *
                Mathf.Lerp(.45f, 1, defender.Balance) * Mathf.Lerp(.20f, 1, defender.Footing) * brace;
            // advantage 是发力相对抵抗的比值；分母设下限，防止接近零时冲量无限增大。
            float advantage = hit.force * hit.leverage / Mathf.Max(.1f, resistance);
            if (hit.pry) advantage *= Settings.pryImpulse / 4.3f;
            if (hit.pry)
            {
                ApplySupportedPry(attacker, defender, hit, advantage);
                return;
            }
            // 把攻防优势映射为退让程度并限幅；随后乘攻击类型、发力和长局升级系数得到冲量。
            float yield = Mathf.Clamp(.20f + (advantage - .65f) * .78f, .10f, 1.7f);
            float displacement = Mathf.Lerp(Settings.openingDisplacement, Settings.finalDisplacement, Escalation);
            if (hit.probe) displacement *= .35f;
            else if (hit.charge) displacement = Mathf.Max(.20f, displacement);
            float impulse = Settings.shoveImpulse * hit.force * yield * displacement;
            defender.Velocity += hit.direction * (impulse / defender.Traits.mass);
            attacker.Velocity -= hit.direction * (impulse * .13f / attacker.Traits.mass);
            defender.Balance = Mathf.Max(.06f, defender.Balance - (hit.probe ? .025f : hit.pry ? .29f : hit.charge ? .20f : .13f) * advantage);
            defender.Stamina = Mathf.Max(0, defender.Stamina - (hit.probe ? .4f : hit.pry ? 4.5f : hit.charge ? 3.5f : 2.0f));
            defender.ImpactVisual = Mathf.Clamp01(impulse * .35f);
            attacker.Contacts++; defender.Contacts++;
            BeetleEnergyPolicy.ReceiveContact(attacker, defender, hit.probe ? .18f : hit.charge ? 1 : .65f);
            if (hit.probe)
            {
                attacker.ProbeTouches++;
                Raise(Name(attacker.Index) + "轻触试探 · 短推即收", attacker.Index, .10f);
            }
            else if (hit.charge)
            {
                attacker.Charges++;
                defender.ImpactVisual = Mathf.Max(.7f, defender.ImpactVisual);
                Raise(Name(attacker.Index) + "蓄力冲顶！争夺顶角支点", attacker.Index, .95f);
            }
            else
            {
                attacker.Shoves++;
                Raise(Name(attacker.Index) + "短促推顶 · " + (advantage > 1 ? "对手后滑" : "双方角力"), attacker.Index, .35f);
            }
        }

        // 新模块只提出冲量请求；最终碰撞姿态确实接触指定部件后，宿主才结算一次。
        // 空求解器、被举起、判负、攻击窗口外均不会造成远程/重复命中。
        void ApplyModuleContacts()
        {
            if (Phase != MatchPhase.Fighting || !(ContactSolver is IBeetleMotionContacts contacts)) return;
            for (int i = 0; i < 2; i++)
            {
                var a = Fighters[i]; var b = Fighters[1 - i];
                if (a.Action != BeetleAction.Module || !a.Motion.CanHit || a.Airborne || a.HasFallen || b.Airborne || b.HasFallen) continue;
                var sample = a.Motion.CombatSample;
                if (!a.Motion.Active.AppliesContactResponse || sample.Contact == BeetleMotionContact.None || sample.Impulse <= 0 ||
                    !contacts.HasMotionContact(i, sample.Contact, a.CircleDirection < 0 ? 0 : 3)) continue;
                Vector2 right = new Vector2(a.Forward.y, -a.Forward.x);
                Vector2 direction = (right * sample.LocalImpulse.x + a.Forward * sample.LocalImpulse.y).normalized;
                float drive = a.Traits.strength * BeetleEnergyPolicy.AttackEffort(a) *
                    Mathf.Lerp(.35f, 1, a.Footing) * Mathf.Lerp(.5f, 1, a.Balance);
                if (a.CheerRemaining > 0) drive *= 1 + Settings.cheerEffortBonus;
                float resistance = b.Traits.grip * Mathf.Lerp(.3f, 1, b.Stamina01) *
                    Mathf.Lerp(.3f, 1, b.Footing) * Mathf.Lerp(.5f, 1, b.Balance);
                float force = Mathf.Clamp(sample.Impulse, 0, 1.5f) * drive / Mathf.Max(.5f, resistance);
                force = Mathf.Min(force, 1.8f);
                b.Velocity += direction * (force / b.Traits.mass);
                a.Velocity -= direction * (force * .13f / a.Traits.mass);
                b.Balance = Mathf.Max(.08f, b.Balance - Mathf.Clamp(sample.BalanceCost, 0, .2f) * drive);
                b.ImpactVisual = Mathf.Max(b.ImpactVisual, Mathf.Clamp01(force * .5f));
                a.Contacts++; b.Contacts++; a.Motion.MarkHit(); a.AttackApplied = true;
                b.Stamina = Mathf.Max(0, b.Stamina - force * 1.5f);
                BeetleEnergyPolicy.ReceiveContact(a, b, Mathf.Clamp01(force));
                Raise(Name(a.Index) + a.Motion.Active.DisplayName + " · 接触发力", i, Mathf.Clamp01(force));
            }
        }

        // 上挑先建立承重点，不立即给自由飞行冲量。抓地强或没有角部接触时可以抵住。
        void ApplySupportedPry(BeetleState attacker, BeetleState defender, Hit hit, float advantage)
        {
            if (ContactSolver is IBeetleLiftContacts liftContacts && !liftContacts.TryBeginLift(this, attacker, defender))
            {
                // 仍在角尖互顶/找支点时继续入角，本步不算命中、不重复扣体力或削平衡。
                attacker.AttackApplied = false;
                return;
            }
            bool toss = false;
            float balanceAfterPry = defender.Balance - .29f * advantage;
            if (!attacker.Airborne && !defender.Airborne && attacker.TossCooldown <= 0 && defender.TossCooldown <= 0 &&
                attacker.Stamina01 >= Settings.tossMinimumStamina && attacker.Stamina >= Settings.tossStaminaCost &&
                attacker.Footing >= .7f && attacker.Balance >= .6f && advantage > 1.05f &&
                Vector2.Dot(attacker.Forward, hit.direction) > .85f &&
                (balanceAfterPry < .7f || defender.Footing < .75f || Vector2.Dot(defender.Forward, -hit.direction) < .5f))
            {
                attacker.TossOpportunities++;
                toss = Range(0, 1) < Settings.tossChance;
            }
            attacker.Pries++;
            defender.Balance = Mathf.Max(.06f, defender.Balance - .29f * advantage);
            if ((advantage <= 1.05f && defender.Balance >= .3f) || defender.Airborne ||
                (ContactSolver != null && !(ContactSolver is IBeetleLiftContacts)))
            {
                Raise(Name(defender.Index) + "撑足抵住，头角未形成有效托举", defender.Index, .3f);
                return;
            }
            var lift = defender.Lift;
            lift.Supporter = attacker.Index; lift.Time = lift.UnsupportedTime = lift.BlockedTime = 0;
            lift.GripReleased = false;
            lift.ResetMechanics();
            // 同一次攻击只选择一次发力节奏。普通成功顶举也必定尝试抛出；概率只控制额外爆发。
            lift.Effort = Range(.85f, 1.18f) * (toss ? 1.5f : 1);
            float drive = Mathf.Clamp01((hit.force * lift.Effort - .5f) / 1.3f);
            lift.RaiseSeconds = Settings.liftRaiseSeconds * Mathf.Lerp(1.1f, .7f, drive);
            lift.HoldSeconds = Settings.liftHoldSeconds * Range(.35f, 1f) * (toss ? .12f : 1);
            lift.SnapSeconds = Settings.throwSnapSeconds * Mathf.Lerp(1.2f, .8f, drive);
            lift.SweepTarget = Mathf.Lerp(98f, 138f, drive);
            attacker.LiftStrokeProgress = 0; attacker.LiftSweepTarget = lift.SweepTarget;
            lift.Pitch = lift.Roll = 0; lift.TwistSign = Mathf.Sign(hit.roll);
            lift.BurstEffort = toss; lift.Direction = hit.direction;
            defender.Airborne = true; defender.VerticalVelocity = 0; defender.Velocity = Vector2.zero;
            defender.Stamina = Mathf.Max(0, defender.Stamina - 4.5f);
            BeetleEnergyPolicy.ReceiveContact(attacker, defender, 1);
            attacker.Velocity *= .2f;
            attacker.Contacts++; defender.Contacts++;
            SetAction(attacker, BeetleAction.Lifting, lift.StrokeDuration + .35f);
            SetAction(defender, BeetleAction.Lifted, lift.StrokeDuration + .35f);
            Raise(Name(attacker.Index) + "角下托举，撬起前身并扭开抓地", attacker.Index, .85f);
        }

        // 固定步维护承重→挣扎→脱角。离地计时独立于动作切换，后足尚能触地时不提前挣扎。
        void UpdateLiftResponses(float dt)
        {
            foreach (var b in Fighters)
            {
                var lift = b.Lift;
                // 场景版本由求解器读取六只真实脚尖判断离地；无场景的数据模拟使用根部高度。
                if (ContactSolver == null && b.Airborne && !b.HasFallen && b.Height - b.SurfaceHeight > .14f)
                    lift.UnsupportedTime += dt;
                else if (!b.Airborne) lift.UnsupportedTime = 0;
                if (!lift.Supported)
                {
                    lift.Pitch = Mathf.MoveTowards(lift.Pitch, 0, dt * (b.Airborne ? 18 : 95));
                    lift.Roll = Mathf.MoveTowards(lift.Roll, 0, dt * 35);
                    continue;
                }
                var attacker = Fighters[lift.Supporter];
                lift.Time += dt;
                float previousStroke = lift.StrokeProgress;
                // 前段逐渐抬头，承重段短暂停留，末段加速猛扬。时间只生成意图，实际转角由接触层计量。
                float raise = Mathf.SmoothStep(0, 1, lift.RaiseProgress);
                float snap = Mathf.Clamp01((lift.Time - lift.SnapStart) / lift.SnapSeconds);
                lift.StrokeProgress = .56f * raise + .44f * snap * snap;
                attacker.LiftStrokeProgress = lift.StrokeProgress;
                attacker.LiftSweepTarget = lift.SweepTarget;
                lift.Pitch = -Settings.liftedBodyPitch * raise;
                lift.Roll = lift.TwistSign * 10 * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.3f, 1, raise));
                BeetleEnergyPolicy.Apply(attacker, Settings, snap > 0 ? 22 : 12, false, dt);
                if (ContactSolver == null)
                {
                    // 无场景测试采用理想杆几何；真实游戏绝不走此分支。
                    float angle = lift.StrokeProgress * lift.SweepTarget * Mathf.Deg2Rad;
                    Vector3 forward = new Vector3(lift.Direction.x, 0, lift.Direction.y);
                    BeetleThrowMechanics.Accumulate(Settings, attacker, b, dt,
                        (lift.StrokeProgress - previousStroke) * lift.SweepTarget * Mathf.Deg2Rad,
                        attacker.Traits.hornReach * Settings.visualScale,
                        forward * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle), 1);
                    if (b.Height - b.SurfaceHeight > .14f) lift.GripReleased = true;
                }
                bool blocked = lift.BlockedTime >= Settings.liftBlockedReleaseSeconds;
                bool lost = lift.LostContactSeconds >= .08f;
                bool ready = lift.Time >= lift.SnapStart + lift.SnapSeconds * .75f &&
                    lift.HeadSweep >= lift.SweepTarget * .72f;
                bool expired = lift.Time >= lift.StrokeDuration + .3f;
                bool interrupted = b.HasFallen || attacker.HasFallen || attacker.Airborne || attacker.Stamina <= 0;
                if (!blocked && !lost && !ready && !expired && !interrupted) continue;
                // 成功离地的顶举一律撬投。没形成支撑/没做功的受阻入角仍只能失败退出，不能隔空发射。
                bool canThrow = !b.HasFallen && !attacker.HasFallen && lift.ContactSeconds >= .08f &&
                    lift.Work >= .5f && lift.HeadSweep >= 12 && (lift.GripReleased || b.Height > b.SurfaceHeight + .12f);
                lift.Supporter = -1;
                if (canThrow) ThrowOpponent(attacker, b);
                else
                {
                    if (!attacker.HasFallen) SetAction(attacker, BeetleAction.ReleaseLift, .55f);
                    if (b.HasFallen) continue;
                    if (!lift.GripReleased && b.Height < b.SurfaceHeight + .12f)
                    { b.Airborne = false; b.VerticalVelocity = 0; }
                    SetAction(b, BeetleAction.Stagger, .45f);
                }
            }
        }

        // 脱角仅赋予速度，不改写根坐标。距离与方向由有效接触的功、冲量及切向方向连续决定。
        void ThrowOpponent(BeetleState attacker, BeetleState defender)
        {
            var lift = defender.Lift;
            Vector3 velocity = BeetleThrowMechanics.Release(Settings, defender);
            attacker.Throws++;
            if (lift.BurstEffort) attacker.Tosses++;
            attacker.Stamina = Mathf.Max(0, attacker.Stamina - Settings.tossStaminaCost * (lift.BurstEffort ? 1 : .35f));
            attacker.TossCooldown = defender.TossCooldown = Settings.tossCooldownSeconds;
            defender.Velocity = new Vector2(velocity.x, velocity.z);
            defender.VerticalVelocity = velocity.y;
            attacker.Velocity -= defender.Velocity * (.12f * defender.Traits.mass / attacker.Traits.mass);
            defender.Airborne = true; defender.Balance = Mathf.Min(.18f, defender.Balance);
            defender.LiftVisual = defender.ImpactVisual = 1;
            defender.RollVisual = lift.TwistSign * 24;
            SetAction(attacker, BeetleAction.Toss, .42f);
            SetAction(defender, BeetleAction.Launched, Mathf.Max(.25f, 2 * velocity.y / Settings.gravity));
            string description = BeetleThrowMechanics.Description(lift.ThrowKind);
            Raise(Name(attacker.Index) + description, attacker.Index, Mathf.Clamp01(velocity.magnitude / 6));
        }

        // 将速度积分到位置。地面运动通过抓地加速度趋近期望速度；腾空运动则主要受重力影响。
        // 场景碰撞在积分后统一修正，所以这里不能代替接触求解。
        void Integrate(BeetleState b, Vector2 desired, float dt)
        {
            // 托举期间的根位置由真实角-受击部位接触约束确定，不能同时施加自由下落重力。
            if (b.Lift.Supported && !b.HasFallen)
            {
                UpdateGround(b);
                // 无 GameObject 的数据测试用高度曲线近似托举，实际游戏始终由接触锚点跟随。
                if (ContactSolver == null) b.Height = b.SurfaceHeight + .6f * Mathf.SmoothStep(0, 1, b.Lift.RaiseProgress);
                b.Velocity = Vector2.zero; b.VerticalVelocity = 0;
                return;
            }
            if (b.HasFallen)
            {
                b.Fall.Begin(b);
                b.Fall.Tick(dt);
                // 空中保留出手动量；触地才加强滑动阻尼。结束比赛后此分支仍按固定步运行。
                b.VerticalVelocity -= Settings.gravity * dt;
                b.Height += b.VerticalVelocity * dt;
                b.Position += b.Velocity * dt;
                b.Velocity *= Mathf.Exp(-(b.Fall.OnFloor ? Settings.fallSlideDamping : .18f) * dt);
                if (ContactSolver == null)
                {
                    bool landed = b.Height <= Settings.groundHeight;
                    b.Fall.SetFloorContact(landed, -b.VerticalVelocity);
                    if (landed) { b.Height = Settings.groundHeight; b.VerticalVelocity = 0; }
                }
                return;
            }
            UpdateGround(b);
            bool beyondSupport = !b.HasGround;
            if (beyondSupport) b.Fall.Begin(b);
            if (b.Fall.Active) b.Fall.Tick(dt);
            if (beyondSupport && !b.Airborne)
            {
                b.Airborne = true;
                b.VerticalVelocity = Mathf.Min(0, b.VerticalVelocity);
                SetAction(b, BeetleAction.Falling, 1);
                Raise(Name(b.Index) + "失去台面支撑！", b.Index);
            }
            if (b.Airborne)
            {
                float previousHeight = b.Height;
                b.VerticalVelocity -= Settings.gravity * dt;
                b.Height += b.VerticalVelocity * dt;
                b.Velocity *= Mathf.Exp(-.18f * dt);
                // 身体位于台面范围内、下降且回到台面高度时落地；挑飞后有更长的恢复动作。
                // 必须从台面上方下降穿过表面；落到悬崖侧面以下后不能瞬移回台顶。
                if (!beyondSupport && previousHeight >= b.SurfaceHeight - .08f && b.Height <= b.SurfaceHeight && b.VerticalVelocity <= 0)
                {
                    b.Airborne = false; b.Height = b.LastSupportHeight = b.SurfaceHeight; b.VerticalVelocity = 0;
                    b.Fall.Reset();
                    b.Lift.UnsupportedTime = 0;
                    b.Lift.GripReleased = false;
                    b.Balance = Mathf.Max(.2f, b.Balance - .12f);
                    bool tossed = b.Action == BeetleAction.Launched;
                    SetAction(b, BeetleAction.Stagger, tossed ? 1.0f : .55f);
                    if (tossed)
                    {
                        b.ImpactVisual = .75f;
                        Raise(Name(b.Index) + "落回台面，重新撑起身体", b.Index, .55f);
                    }
                }
            }
            else
            {
                float slope = Vector3.Angle(Vector3.up, b.SurfaceNormal);
                float traction = Mathf.Lerp(.7f, 3.1f, b.Footing) * b.Traits.grip * b.SurfaceGrip * Mathf.Lerp(1, .65f, slope / 38);
                if (b.Action == BeetleAction.Brace) traction *= 1.2f;
                // 上坡主动行走变慢；抓地差/陡坡产生顺坡滑动，外部攻击冲量依然保留。
                Vector2 downhill = new Vector2(b.SurfaceNormal.x, b.SurfaceNormal.z);
                float uphill = Mathf.Max(0, -Vector2.Dot(desired.normalized, downhill));
                desired *= 1 / (1 + uphill * 1.8f);
                float slip = Mathf.InverseLerp(16, 38, slope) * (1 - b.Footing * b.Balance * .7f);
                b.Velocity += downhill * (Settings.gravity * slip * dt);
                b.Velocity = Vector2.MoveTowards(b.Velocity, desired, traction * dt);
                b.Height = b.SurfaceHeight;
            }
            b.Position += b.Velocity * dt;
            // 位移后立即刷新高度，避免在坡上滞后一模拟步而穿入地面。
            UpdateGround(b);
            if (!b.Airborne && b.HasGround) b.Height = b.SurfaceHeight;
        }

        // 估算六个名义足位有多少仍在圆形台面上，平均得到 0～1 支撑系数。
        // 这不是读取动画 IK 脚尖或每只脚的真实接触力；边缘用连续过渡减少数值跳变。
        float ComputeFooting(BeetleState b)
        {
            float support = 0;
            Vector2 right = new Vector2(b.Forward.y, -b.Forward.x);
            for (int side = -1; side <= 1; side += 2)
                for (int pair = 0; pair < 3; pair++)
                {
                    Vector2 foot = b.Position + right * (side * 1.10f * b.Traits.size) + b.Forward * ((1 - pair) * .9f * b.Traits.size);
                    support += Mathf.Clamp01((Settings.arenaRadius - foot.magnitude + .18f) / .36f);
                }
            return support / 6;
        }

        // 没有场景求解器时的备用算法：按朝向估算身体半径，重叠后按质量分开。
        // 只适用于纯数据测试，不能代表细角、腿部的实际防穿模精度。
        void ResolveBodyContact()
        {
            var a = Fighters[0]; var b = Fighters[1];
            if (a.HasFallen || b.HasFallen || Mathf.Abs(a.Height - b.Height) > .85f) return;
            Vector2 delta = b.Position - a.Position;
            float distance = delta.magnitude;
            Vector2 contactNormal = distance > .0001f ? delta / distance : Vector2.right;
            float extentA = Mathf.Lerp(.74f, 1.04f, Mathf.Abs(Vector2.Dot(a.Forward, contactNormal))) * a.Traits.size;
            float extentB = Mathf.Lerp(.74f, 1.04f, Mathf.Abs(Vector2.Dot(b.Forward, contactNormal))) * b.Traits.size;
            float minimum = extentA + extentB;
            if (distance >= minimum) return;
            Vector2 normal = distance > .0001f ? delta / distance : Vector2.right;
            float totalMass = a.Traits.mass + b.Traits.mass;
            float penetration = minimum - distance;
            a.Position -= normal * penetration * b.Traits.mass / totalMass;
            b.Position += normal * penetration * a.Traits.mass / totalMass;
            float closing = Vector2.Dot(a.Velocity - b.Velocity, normal);
            if (closing > 0)
            {
                a.Velocity -= normal * closing * b.Traits.mass / totalMass;
                b.Velocity += normal * closing * a.Traits.mass / totalMass;
            }
        }

        // 只有 HasFallen 才结算。第一只落台后留出 doubleFallWindow，允许随后双落台判平局。
        // 体力耗尽、碰到边缘或比赛超时都不会直接判负。
        void CheckResult(float dt)
        {
            bool a = Fighters[0].HasFallen, b = Fighters[1].HasFallen;
            if (!a && !b) return;
            if (Phase == MatchPhase.Fighting) { Phase = MatchPhase.Resolving; resolutionTime = Settings.doubleFallWindow; }
            resolutionTime -= dt;
            if (resolutionTime > 0 && !(a && b)) return;
            Winner = a && b ? -1 : a ? 1 : 0;
            Phase = MatchPhase.Finished;
            if (Winner >= 0) SetAction(Fighters[Winner], BeetleAction.Winner, 10);
            Raise(Winner < 0 ? "双方落台 · 本场平局" : Name(Winner) + "获胜 · 对手已落台", Winner);
        }

        // 统一切换动作并重置动作计时、持续时间及命中标记，使下一个动作有独立结算机会。
        void SetAction(BeetleState b, BeetleAction action, float duration, [System.Runtime.CompilerServices.CallerMemberName] string caller = "", [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
#if UNITY_EDITOR
            BeetleDebugHooks.Change(this,b.Index,b.Action,action,caller,line);
#endif
            // 承诺动作的收招可被看见并利用；命中较短，打空较长，不取消已开始的攻击链。
            bool endedAttack = b.Action == BeetleAction.Shove || b.Action == BeetleAction.Charge ||
                b.Action == BeetleAction.Pry || b.Action == BeetleAction.Module;
            if (endedAttack && (action == BeetleAction.Brace || action == BeetleAction.Disengage || action == BeetleAction.Circle))
                b.ExposureRemaining = Mathf.Max(b.ExposureRemaining, b.AttackApplied ? .24f : Settings.missedAttackExposure);
            // 受击、托举或结算抢占时立即归还模块控制权，不能等到下一步继续条件检查。
            if (action != BeetleAction.Module) b.Motion.Cancel();
            bool wasResting = BeetleRecoveryPolicy.IsRest(b.Action), willRest = BeetleRecoveryPolicy.IsRest(action);
            if (wasResting && !willRest) b.RecoveryCooldown = Settings.recoveryRetrySeconds;
            if (!wasResting || !willRest) b.RecoveryTime = 0;
            b.Action = action; b.ActionDuration = duration; b.ActionTime = 0; b.AttackApplied = false;
        }
        // 从本局随机源抽取区间数值；调用次数和顺序会影响后续所有随机选择。
        float Range(float minimum, float maximum) => minimum + (float)random.NextDouble() * (maximum - minimum);
        // 事件供 HUD 文案等展示消费；Intensity 不是伤害，也不再直接驱动碰撞声音。
        void Raise(string message, int actor = -1, float intensity = 0) { EventCount++; EventRaised?.Invoke(new ContestEvent(message, actor, intensity)); }
        // 将选手索引转换为显示名称；-1 等无选手事件不应调用本方法生成身份。
        public static string Name(int index) => index == 0 ? "琥珀" : "墨甲";
    }
}
