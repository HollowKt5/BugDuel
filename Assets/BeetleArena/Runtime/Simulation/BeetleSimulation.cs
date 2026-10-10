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
        // 首次落台后等待另一方是否也落台的计时；exchangeTime 则负责短周期交锋节奏。
        float resolutionTime;
        float exchangeTime;
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
            foreach (var fighter in Fighters)
            {
                if (!fighter.HasFallen && (!fighter.HasGround || fighter.Height < fighter.SurfaceHeight - Settings.fallDepth)
                    && fighter.Height < fighter.LastSupportHeight - Settings.fallDepth)
                {
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
                Exchange == ExchangePhase.Clash ? Settings.clashSeconds : Settings.exchangeRecoverySeconds;
            if (exchangeTime < duration) return;
            exchangeTime = 0;
            Exchange = Exchange == ExchangePhase.Probing ? ExchangePhase.Clash :
                Exchange == ExchangePhase.Clash ? ExchangePhase.Recovery : ExchangePhase.Probing;
            Raise(Exchange == ExchangePhase.Clash ? "交锋升级 · 蓄力争夺支点" :
                Exchange == ExchangePhase.Recovery ? "双方拉开 · 调整抓地与体力" : "重新试探 · 寻找入角机会");
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
            bool threatened = opponent.Committed && distance < EngagementDistance + .25f;
            bool finishedAction = b.ActionTime >= b.ActionDuration;
            float targetHeading = Mathf.Atan2(toward.x, toward.y) * Mathf.Rad2Deg;
            // 蓄力和发力时降低转向速度，让攻击有承诺时间，避免冲顶过程中瞬间追踪侧方目标。
            float turn = b.Committed ? .32f : 1f;
            b.Heading = Mathf.MoveTowardsAngle(b.Heading, targetHeading, Settings.turnSpeed * b.Traits.agility * turn * dt);

            // 优先完成连贯动作链：蓄力→冲顶→接触角力→可能抬举；其余动作结束才重新决策。
            if (finishedAction)
            {
                switch (b.Action)
                {
                    case BeetleAction.Probe: SetAction(b, BeetleAction.Disengage, Range(.45f, .75f)); break;
                    case BeetleAction.ChargeWindup: SetAction(b, BeetleAction.Charge, .48f); break;
                    case BeetleAction.Charge:
                        if (b.AttackApplied && distance < EngagementDistance + .25f && !opponent.Airborne)
                        { SetAction(b, BeetleAction.HornLock, Settings.hornLockSeconds); b.Locks++; }
                        else SetAction(b, BeetleAction.Brace, .45f);
                        break;
                    case BeetleAction.HornLock:
                        SetAction(b, Exchange == ExchangePhase.Clash && b.Stamina > 24 && distance < EngagementDistance && !opponent.Airborne
                            ? BeetleAction.PryWindup : BeetleAction.Disengage, .55f);
                        break;
                    case BeetleAction.ShoveWindup: SetAction(b, BeetleAction.Shove, .34f); break;
                    case BeetleAction.PryWindup: SetAction(b, BeetleAction.Pry, 2.15f); break;
                    case BeetleAction.Shove:
                    case BeetleAction.Toss:
                    case BeetleAction.Pry: SetAction(b, BeetleAction.Brace, Range(.35f, .7f)); break;
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
                case BeetleAction.Approach:
                    desiredVelocity[b.Index] = toward * speed * Mathf.Clamp01((facing + .1f) / .8f);
                    staminaCost = 1.5f;
                    if (distance < EngagementDistance && b.DecisionTime <= 0) Decide(b, opponent, distance, facing, edge, threatened);
                    break;
                case BeetleAction.Circle:
                    Vector2 tangent = new Vector2(-toward.y, toward.x) * b.CircleDirection;
                    Vector2 inward = -b.Position.normalized * Mathf.InverseLerp(.83f, 1f, edge);
                    desiredVelocity[b.Index] = (tangent * .6f + toward * Mathf.Clamp(distance - EngagementDistance, -.5f, .5f) + inward * 1.2f).normalized * speed * .7f;
                    staminaCost = 1.3f;
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
                case BeetleAction.Brace: staminaCost = threatened ? 1.5f : -Settings.staminaRecovery * .6f; break;
                case BeetleAction.Recover: staminaCost = -Settings.staminaRecovery; break;
                case BeetleAction.Retreat:
                    desiredVelocity[b.Index] = (-toward * .65f - b.Position.normalized * Mathf.InverseLerp(.45f, .90f, edge)).normalized * speed * .65f;
                    staminaCost = -Settings.staminaRecovery * .65f;
                    break;
                case BeetleAction.Assess: staminaCost = -1; break;
            }
            // A planted beetle avoids stepping backwards into empty space. Only
            // voluntary outward walking is reduced; received momentum still carries it out.
            // 只限制 AI 主动向台外走的分量，不钳制实际位置或受击速度，因此仍然能被推下台。
            Vector2 radial = b.Position.normalized;
            float outward = Vector2.Dot(desiredVelocity[b.Index], radial);
            if (outward > 0) desiredVelocity[b.Index] -= radial * outward * Mathf.InverseLerp(.65f, .88f, edge);
            if (b.Committed || threatened) b.Fatigue = Mathf.Min(100, b.Fatigue + Settings.fatiguePerSecond * Mathf.Lerp(1, 3, Escalation) * dt);
            b.Stamina = Mathf.Clamp(b.Stamina - staminaCost * dt, 0, b.StaminaLimit);
            b.Balance = Mathf.MoveTowards(b.Balance, 1, dt * (b.Action == BeetleAction.Brace || b.Action == BeetleAction.Recover ? .40f : .12f));
        }

        // 按优先级选择下一动作：恢复阶段/低体力 → 调整朝向/距离 → 防守 → 试探或进攻。
        // 一个 choice 会参与多个顺序分支；各阈值不是相互独立的攻击概率。
        void Decide(BeetleState b, BeetleState opponent, float distance, float facing, float edge, bool threatened)
        {
            // 限制接近过程中提前重选动作的频率，避免每个模拟步都改变主意。
            b.DecisionTime = .3f;
            if (Exchange == ExchangePhase.Recovery)
            {
                SetAction(b, distance < EngagementDistance + .4f && edge < .78f ? BeetleAction.Disengage : BeetleAction.Recover, Range(.6f, .9f));
                return;
            }
            if ((b.Action == BeetleAction.Recover || b.Action == BeetleAction.Retreat) && b.Stamina < b.StaminaLimit * (Exchange == ExchangePhase.Clash ? .60f : .84f))
            {
                SetAction(b, distance > 3.2f ? BeetleAction.Recover : BeetleAction.Retreat, Range(.6f, 1.0f));
                return;
            }
            if (b.Stamina < 18)
            {
                SetAction(b, edge > .78f ? BeetleAction.Brace : BeetleAction.Retreat, Range(.9f, 1.6f));
                if (distance > 3) SetAction(b, BeetleAction.Recover, Range(1, 1.7f));
                return;
            }
            if (facing < .70f) { SetAction(b, BeetleAction.Assess, .35f); return; }
            float engagement = EngagementDistance;
            if (distance > engagement) { SetAction(b, BeetleAction.Approach, .7f); return; }
            float choice = Range(0, 1);
            if (threatened && choice < .30f) { SetAction(b, BeetleAction.Brace, Range(.5f, .85f)); return; }
            if (edge > .88f && choice < .22f) { SetAction(b, BeetleAction.Circle, .85f); return; }
            if (b.Balance < .45f) { SetAction(b, BeetleAction.Brace, .65f); return; }
            if (Exchange == ExchangePhase.Probing)
            {
                SetAction(b, choice < .22f ? BeetleAction.Circle : BeetleAction.Probe, Range(.7f, 1.0f));
                return;
            }
            if (choice < .20f - b.Traits.aggression * .12f) { SetAction(b, BeetleAction.Circle, Range(.65f, 1.15f)); return; }
            if (b.Stamina01 < .40f && choice < .38f) { SetAction(b, BeetleAction.Recover, .7f); return; }
            bool pry = b.Contacts >= 2 && distance < (ContactSolver != null ? 4.05f : 2.70f) && choice < .40f + b.Traits.aggression * .45f && b.Stamina > 22;
            if (b.Stamina > 30 && (!b.TouchingOpponent || choice > .45f))
                SetAction(b, BeetleAction.ChargeWindup, Settings.chargeWindupSeconds * Range(.9f, 1.1f));
            else SetAction(b, pry ? BeetleAction.PryWindup : BeetleAction.ShoveWindup, pry ? .62f : Range(.26f, .40f));
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
            float effort = .38f + .62f * b.Stamina01;
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
            lift.RaiseSeconds = Settings.liftRaiseSeconds; lift.HoldSeconds = Settings.liftHoldSeconds;
            lift.Pitch = lift.Roll = 0; lift.TwistSign = Mathf.Sign(hit.roll);
            lift.TossOnRelease = toss; lift.Direction = hit.direction;
            defender.Airborne = true; defender.VerticalVelocity = 0; defender.Velocity = Vector2.zero;
            defender.Stamina = Mathf.Max(0, defender.Stamina - 4.5f);
            attacker.Velocity *= .2f;
            attacker.Contacts++; defender.Contacts++;
            SetAction(attacker, BeetleAction.Lifting, lift.RaiseSeconds + lift.HoldSeconds);
            SetAction(defender, BeetleAction.Lifted, lift.RaiseSeconds + lift.HoldSeconds);
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
                float t = Mathf.SmoothStep(0, 1, lift.RaiseProgress);
                lift.Pitch = -Settings.liftedBodyPitch * t;
                lift.Roll = lift.TwistSign * 10 * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.3f, 1, t));
                attacker.Stamina = Mathf.Max(0, attacker.Stamina - 14 * dt);
                bool blocked = lift.BlockedTime >= Settings.liftBlockedReleaseSeconds;
                if (!blocked && lift.Time < lift.RaiseSeconds + lift.HoldSeconds && !b.HasFallen &&
                    !attacker.HasFallen && !attacker.Airborne && attacker.Stamina > 0) continue;
                lift.Supporter = -1;
                if (!attacker.HasFallen) SetAction(attacker, BeetleAction.ReleaseLift, .65f);
                if (b.HasFallen) continue;
                // 普通托举脱角后下落；只有之前通过概率及体力门槛的猛挑才附加抛出速度。
                b.VerticalVelocity = -.15f; b.Velocity = lift.Direction * .3f;
                // 叉角/地形阻挡导致失败时取消预选的挑飞，不允许未抬起就凭空发射。
                if (blocked && !lift.GripReleased && b.Height < b.SurfaceHeight + .12f)
                { b.Airborne = false; b.VerticalVelocity = 0; }
                if (lift.TossOnRelease && !blocked && !attacker.HasFallen)
                    TossOpponent(attacker, b, new Hit { direction = lift.Direction, roll = lift.TwistSign });
                else SetAction(b, BeetleAction.Stagger, .65f);
            }
        }

        // 挑飞成功后的附加效果：额外消耗体力，给双方设置冷却，增加向外冲量与向上初速度。
        // 这里只启动腾空和动作，不判胜；后续由重力、台面支撑与高度规则决定是否落台。
        void TossOpponent(BeetleState attacker, BeetleState defender, Hit hit)
        {
            attacker.Tosses++;
            attacker.Stamina = Mathf.Max(0, attacker.Stamina - Settings.tossStaminaCost);
            attacker.TossCooldown = defender.TossCooldown = Settings.tossCooldownSeconds;
            float lift = Settings.tossVerticalSpeed / Mathf.Sqrt(defender.Traits.mass);
            float impulse = Settings.tossHorizontalImpulse * Mathf.Lerp(.85f, 1.1f, attacker.Stamina01);
            defender.Velocity += hit.direction * (impulse / defender.Traits.mass);
            attacker.Velocity -= hit.direction * (impulse * .15f / attacker.Traits.mass);
            defender.Airborne = true;
            defender.Height = Mathf.Max(defender.SurfaceHeight + .02f, defender.Height);
            defender.VerticalVelocity = lift;
            defender.Balance = Mathf.Min(.18f, defender.Balance);
            defender.LiftVisual = defender.ImpactVisual = 1;
            defender.RollVisual = Mathf.Sign(hit.roll) * 32;
            SetAction(attacker, BeetleAction.Toss, .5f);
            // 2v/g 是同高度自由抛起的理想飞行时长，用作视觉动作进度；实际落地仍由积分判断。
            SetAction(defender, BeetleAction.Launched, 2 * lift / Settings.gravity);
            Raise(Name(attacker.Index) + "抓住失衡，猛然挑飞！", attacker.Index, 1);
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
                b.VerticalVelocity -= Settings.gravity * dt;
                b.Height = Mathf.Max(Settings.groundHeight - .15f, b.Height + b.VerticalVelocity * dt);
                b.Position += b.Velocity * dt;
                b.Velocity *= Mathf.Exp(-1.2f * dt);
                return;
            }
            UpdateGround(b);
            bool beyondSupport = !b.HasGround;
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
        void SetAction(BeetleState b, BeetleAction action, float duration)
        {
            b.Action = action; b.ActionDuration = duration; b.ActionTime = 0; b.AttackApplied = false;
        }
        // 从本局随机源抽取区间数值；调用次数和顺序会影响后续所有随机选择。
        float Range(float minimum, float maximum) => minimum + (float)random.NextDouble() * (maximum - minimum);
        // 事件供 HUD 文案和音效消费；Intensity 是反馈强度，并非伤害值。
        void Raise(string message, int actor = -1, float intensity = 0) { EventCount++; EventRaised?.Invoke(new ContestEvent(message, actor, intensity)); }
        // 将选手索引转换为显示名称；-1 等无选手事件不应调用本方法生成身份。
        public static string Name(int index) => index == 0 ? "琥珀" : "墨甲";
    }
}
