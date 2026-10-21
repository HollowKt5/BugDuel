using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BeetleArena
{
    // 场景总协调器：连接数据模拟、两只 Actor、接触、HUD、镜头和音效。
    // 输入只映射为鼓励/暂停/换局；战斗决策仍由 Simulation 负责。
    public sealed class BeetleMatchController : MonoBehaviour
    {
        [Header("Scene references")]
        // 场景生成器写入这些引用；settings 是共享调参资产，actors 必须按索引保存两只选手。
        [SerializeField] public BeetleMatchSettings settings;
        [SerializeField] public BeetleActor[] actors;
        [SerializeField] public BeetleArenaHud hud;
        [SerializeField] public ArenaCamera spectatorCamera;
        [SerializeField] public ArenaAudio audioFeedback;
        public BeetleContactFeedback contactFeedback;
        [SerializeField] public Collider[] arenaContacts;
        [Header("Switchable arenas")]
        public BeetleArenaSurface[] arenas;
        [SerializeField] int arenaIndex;
        public int ArenaIndex => arenaIndex;
        public BeetleArenaSurface ActiveSurface => arenas != null && arenas.Length > 0 ? arenas[Mathf.Clamp(arenaIndex, 0, arenas.Length - 1)] : null;
        public string ArenaName => ActiveSurface ? ActiveSurface.displayName : "平地木桩";

        // 换场只改变地形，用同种子重新生成同阵容；清空旧接触、脚点、腾空和暂停状态。
        public void SwitchArena()
        {
            if (arenas == null || arenas.Length < 2)
                return;
            // 持久个体的换场/重赛必须由应用层重新检查资格并预订，不能复用旧健康快照。
            bool persistent = firstEntrant != null;
            if (persistent && (Simulation.Phase != MatchPhase.Finished || PersistentMatchRequested == null))
                return;
            int previousArena = arenaIndex;
            var previousSimulation = Simulation;
            bool started = false;
            SelectArena((arenaIndex + 1) % arenas.Length);
            try
            {
                StartMatch(previousSimulation != null ? previousSimulation.Seed : settings.initialSeed);
                started = !ReferenceEquals(Simulation, previousSimulation);
            }
            finally
            {
                // 应用可以拒绝或忽略重赛请求。只有成功绑定新模拟才提交持久阵容的场地切换。
                if (persistent && !started) SelectArena(previousArena);
            }
        }

        public void SelectArena(int index)
        {
            if (arenas == null || arenas.Length == 0)
                return;
            arenaIndex = Mathf.Clamp(index, 0, arenas.Length - 1);
            for (int i = 0; i < arenas.Length; i++)
                if (arenas[i])
                    arenas[i].gameObject.SetActive(i == arenaIndex);
            arenaContacts = ActiveSurface.contactColliders;
            Physics.SyncTransforms();
        }
        // 每局重新建立的接触求解器与模拟实例，对外只读引用供显示和验证工具访问。
        public BeetleContactSolver Contacts { get; private set; }
        public BeetleSimulation Simulation { get; private set; }
        public bool IsPaused { get; private set; }
        public int RoundNumber { get; private set; }
        public string LatestEvent { get; private set; } = "触角试探 · 即将开始";
        // 通知外部观察者新一局已绑定完成；事件本身不包含结果判定。
        public event Action MatchChanged;
        public event Action<BeetleMatchResult> MatchCompleted;
        // 只有被替换/销毁且尚未完成的比赛才取消，供外层应用释放个体占用。取消不伪造胜负。
        public event Action<string> MatchCancelled;
        public event Action<int> PersistentMatchRequested;
        public bool CanRequestNextMatch => firstEntrant == null || PersistentMatchRequested != null;
        BeetleMatchEntrant firstEntrant;
        // accumulator 为未消费的模拟时间，animationTime 为表现时间，hudClock 控制 10Hz UI 刷新。
        float accumulator, animationTime, hudClock;
        public float RoundClock { get; private set; }
        [SerializeField] public bool defeatSlowMotion = true;
        float defeatRemaining;
        bool defeatPlayed;
        public bool IsDefeatSlowMotion => defeatSlowMotion && defeatRemaining > 0;
        public void ToggleDefeatSlowMotion()
        {
            defeatSlowMotion = !defeatSlowMotion;
            if (!defeatSlowMotion)
            { defeatRemaining = 0; spectatorCamera.DefeatFocus = -1; }
            hud.Refresh();
        }
        public void CycleView()
        {
            spectatorCamera.CycleView();
            if (IsPaused && Simulation != null)
                spectatorCamera.Tick(BeetleCameraFrame.Capture(Simulation, actors), .5f);
            hud.Refresh();
        }

        // 运行入口：检查必需引用、约束参数、绑定按钮，再按配置或当前时刻选择初始种子。
        void Start()
        {
            if (!settings || actors == null || actors.Length != 2 || !actors[0] || !actors[1] || !hud || !spectatorCamera)
            {
                Debug.LogError("Beetle Arena is missing required scene references.", this);
                enabled = false;
                return;
            }
            hud.Bind(this);
            BeetleCombatDisplay.Install(spectatorCamera.GetComponent<Camera>());
            // 应用可在 Awake 中交付已预订的比赛；Unity Start 不能重置其场地或再次抽局。
            if (Simulation != null) return;
            settings.Validate();
            // 每次进入游戏默认平地；局内换场/重赛仍由 StartMatch 保留当前选择。
            SelectArena(0);
            int seed = settings.initialSeed == 0 ? unchecked(Environment.TickCount * 397 ^ DateTime.UtcNow.Millisecond) : settings.initialSeed;
            StartMatch(seed);
        }

        // 统一换局入口：解绑旧事件、创建状态、重置计时、绑定模型、安装接触求解器并刷新界面。
        // 初次 Solve(dt=0) 处理初始接触；之后由固定步长正式推进。
        public void StartMatch(int seed)
        {
            if (firstEntrant != null)
            {
                if (Simulation.Phase != MatchPhase.Finished) return;
                PersistentMatchRequested?.Invoke(seed);
                return;
            }
            StartMatch(seed, null, null);
        }

        public void StartExhibition(int seed) => StartMatch(seed, null, null);

        public void StartMatch(int seed, BeetleMatchEntrant first, BeetleMatchEntrant second)
            => StartMatch(new BeetleSimulation(settings, seed, first, second));

        // 应用层可交付已完成预订的模拟；这里负责场景绑定，不再次抽签或创建另一个 MatchId。
        public void StartMatch(BeetleSimulation next)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            if (!ReferenceEquals(next.Settings, settings) || next.Phase != MatchPhase.Countdown || next.ContactSolver != null)
                throw new InvalidOperationException("Bind a fresh match created with this controller's settings.");
            var first = next.Fighters[0].Entrant; var second = next.Fighters[1].Entrant;
            // 先验证完整请求；非法阵容不能取消已经运行的比赛或改变其订阅。
            if (!settings || actors == null || actors.Length != 2 || !actors[0] || !actors[1] || !hud || !spectatorCamera)
                throw new InvalidOperationException("Match controller requires settings, two actors, HUD and camera.");
            if (first != null && actors[0].rigId != first.RigId || second != null && actors[1].rigId != second.RigId)
                throw new InvalidOperationException("Entrant rig does not match the actor in its slot. Bind a calibrated actor before starting.");
            DetachSimulation();
            firstEntrant = first;
            Simulation = next;
            SelectArena(arenaIndex);
            EnsureLandingGroundCoverage();
            Simulation.SetGround(ActiveSurface);
            Simulation.EventRaised += OnContestEvent;
            Simulation.Completed += OnMatchCompleted;
            accumulator = animationTime = 0;
            IsPaused = false;
            RoundNumber++;
            RoundClock = 0;
            defeatRemaining = 0;
            defeatPlayed = false;
            LatestEvent = "触角试探 · 即将开始";
            for (int i = 0; i < actors.Length; i++)
                actors[i].Bind(Simulation.Fighters[i], settings, ActiveSurface);
            Contacts = new BeetleContactSolver(actors, arenaContacts, .006f);
            Simulation.ContactSolver = Contacts;
            Contacts.Solve(Simulation, 0);
            if (contactFeedback)
                contactFeedback.Bind(actors);
            spectatorCamera.ResetView();
            MatchChanged?.Invoke();
            hud.Refresh();
        }

        // 三倍远抛可越过旧地面边界。只扩展台外地面，保留擂台大小与落台判定。
        void EnsureLandingGroundCoverage()
        {
            if (arenaContacts == null)
                return;
            float maximumLaunchSpeed = settings.throwMaximumSpeed * settings.throwHeavySpeedMultiplier *
                BeetleThrowMechanics.TumblingLaunchSpeedMultiplier;
            float maximumRange = maximumLaunchSpeed * maximumLaunchSpeed / Mathf.Max(1, settings.gravity);
            float requiredWidth = 2 * (maximumRange + settings.arenaRadius + 4);
            foreach (var surface in arenaContacts)
            {
                if (!surface || Mathf.Abs(surface.bounds.max.y - settings.groundHeight) > .05f)
                    continue;
                Vector3 boundsSize = surface.bounds.size;
                if (boundsSize.x <= 0 || boundsSize.z <= 0)
                    continue;
                Vector3 scale = surface.transform.localScale;
                scale.x *= Mathf.Max(1, requiredWidth / boundsSize.x);
                scale.z *= Mathf.Max(1, requiredWidth / boundsSize.z);
                surface.transform.localScale = scale;
            }
            Physics.SyncTransforms();
        }

        // 仅结束后允许换新阵容；用整数线性变换生成下一种子，unchecked 允许预期的整数回绕。
        public void NextMatch()
        {
            if (Simulation == null || Simulation.Phase != MatchPhase.Finished)
                return;
            StartMatch(unchecked(Simulation.Seed * 1664525 + 1013904223));
        }
        // 沿用上局种子重新创建初始阵容；没有录制加油输入，也不是逐帧录像回放。
        public void Rematch()
        {
            if (Simulation == null || Simulation.Phase != MatchPhase.Finished)
                return;
            StartMatch(Simulation.Seed);
        }
        // UI 和键盘共用的鼓励入口：暂停时拒绝，具体资格/冷却由 Simulation.Encourage 检查。
        public bool Cheer(int index)
        {
            bool accepted = !IsPaused && Simulation != null && Simulation.Encourage(index);
            if (accepted)
                hud.Refresh();
            return accepted;
        }
        // 暂停通过本控制器的 dt=0 实现，不修改全局 Time.timeScale；结束后不再切换暂停。
        public void TogglePause()
        {
            if (Simulation == null || Simulation.Phase == MatchPhase.Finished)
                return;
            IsPaused = !IsPaused;
            if (audioFeedback)
                audioFeedback.SetPaused(IsPaused);
            hud.Refresh();
        }

        // 每个显示帧读取键盘，再通过累积器执行零次或多次固定模拟步，最后插值显示与刷新镜头。
        void Update()
        {
            if (Simulation == null)
                return;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.qKey.wasPressedThisFrame)
                    Cheer(0);
                if (keyboard.eKey.wasPressedThisFrame)
                    Cheer(1);
                if (keyboard.spaceKey.wasPressedThisFrame)
                    TogglePause();
                if (keyboard.rKey.wasPressedThisFrame)
                    NextMatch();
                if (keyboard.tKey.wasPressedThisFrame)
                    SwitchArena();
                if (keyboard.cKey.wasPressedThisFrame)
                    CycleView();
                if (keyboard.vKey.wasPressedThisFrame)
                    ToggleDefeatSlowMotion();
            }
            AdvanceRoundClock(Time.unscaledDeltaTime);
            // Physics catches up at most .1s per frame; the independent real clock
            // above still enforces the deadline on the first frame after a stall.
            float dt = IsPaused ? 0 : Mathf.Min(Time.unscaledDeltaTime, .1f);
            if (IsDefeatSlowMotion)
            {
                defeatRemaining = Mathf.Max(0, defeatRemaining - dt);
                dt *= .28f;
                if (defeatRemaining <= 0)
                    spectatorCamera.DefeatFocus = -1;
            }
            animationTime += dt;
            accumulator += dt;
            // 每一步先缓存旧根节点位置，Step 内更新 AI、接触和状态，再扣掉已消费的 0.02 秒。
            while (accumulator >= BeetleSimulation.FixedStep)
            {
                AdvanceFixedStep();
                accumulator -= BeetleSimulation.FixedStep;
            }
            // 剩余时间/固定步长得到插值系数；显示在旧状态和新状态之间平滑过渡。
            float alpha = accumulator / BeetleSimulation.FixedStep;
            Present(alpha, animationTime, dt);
            // HUD 使用真实帧时间每 0.1 秒刷新，暂停时按钮文案仍可更新。
            hudClock += Time.unscaledDeltaTime;
            if (hudClock >= .1f)
            { hudClock = 0; hud.Refresh(); }
        }
        /// <summary>正式游戏与自动化快进共用固定步入口；低层测试可直接调用 Simulation.Step 以隔离表现。</summary>
        public void AdvanceFixedStep()
        {
            if (Simulation == null || IsPaused)
                return;
            foreach (var actor in actors)
                actor.BeforeStep();
            Simulation.Step(BeetleSimulation.FixedStep);
            if (!defeatPlayed && (Simulation.Phase == MatchPhase.Resolving || Simulation.Phase == MatchPhase.Finished))
            {
                defeatPlayed = true;
                if (defeatSlowMotion && (Simulation.Phase == MatchPhase.Resolving ||
                    Simulation.ResultReason != BeetleResultReason.TimeLimit && Simulation.Winner >= 0))
                {
                    defeatRemaining = BeetleSimulation.FinishPresentationSeconds;
                    spectatorCamera.DefeatFocus = Simulation.Fighters[0].HasFallen ? 0 :
                        Simulation.Winner >= 0 ? 1 - Simulation.Winner : 1;
                }
            }
            if (contactFeedback)
                contactFeedback.Tick(Simulation, BeetleSimulation.FixedStep);
#if UNITY_EDITOR
            BeetleDebugHooks.Step(this);
#endif
        }

        public void AdvanceRoundClock(float seconds)
        {
            if (Simulation == null || IsPaused || seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds))
                return;
            if (Simulation.Phase == MatchPhase.Finished && !IsDefeatSlowMotion)
                return;
            RoundClock = Mathf.Min(BeetleSimulation.MaximumRoundSeconds, RoundClock + seconds);
            if (RoundClock < BeetleSimulation.MaximumRoundSeconds)
                return;
            Simulation.ReachTimeLimit();
            defeatPlayed = true;
            defeatRemaining = 0;
            spectatorCamera.DefeatFocus = -1;
        }

        /// <summary>共用显示顺序：插值 → 防穿透显示约束 → 标记（约束内部）→ 相机；不推进模拟。</summary>
        public void Present(float alpha, float time, float dt)
        {
            // 暂停保留上个已接受画面，也省掉重复的碰撞重试。HUD 由外层独立刷新。
            if (Simulation == null || IsPaused || dt <= 0)
                return;
            foreach (var actor in actors)
                actor.Render(alpha, time, dt);
            Contacts.ProjectView(Simulation, dt);
            spectatorCamera.Tick(BeetleCameraFrame.Capture(Simulation, actors), IsDefeatSlowMotion ? dt / .28f : dt);
        }

        // 文案只更新 HUD。碰撞声音与火星由最终接触采样驱动，避免虚晃/落台文案误触发。
        void OnContestEvent(ContestEvent e)
        {
            LatestEvent = e.Message;
        }
        void OnMatchCompleted(BeetleMatchResult result) => MatchCompleted?.Invoke(result);
        void DetachSimulation()
        {
            if (Simulation == null) return;
            Simulation.EventRaised -= OnContestEvent;
            Simulation.Completed -= OnMatchCompleted;
            if (Simulation.Phase != MatchPhase.Finished) MatchCancelled?.Invoke(Simulation.MatchId);
        }
        // 销毁时解除模拟事件订阅，防止场景切换后残留回调引用。
#if UNITY_EDITOR
        // 调试器用本入口暂停/单步，保持正式固定步顺序；不修改全局 timeScale。
        public void EditorSetPaused(bool paused)
        { IsPaused = paused; if (audioFeedback) audioFeedback.SetPaused(paused); if (hud) hud.Refresh(); }
        public void EditorStep()
        {
            if (Simulation == null)
                throw new InvalidOperationException("Start a match before stepping.");
            bool paused = IsPaused;
            IsPaused = false;
            try
            { AdvanceFixedStep(); animationTime = Simulation.MotionTime; accumulator = 0; Present(1, animationTime, BeetleSimulation.FixedStep); }
            finally { EditorSetPaused(paused); }
        }
        public void EditorReconnectEvents()
        {
            if (Simulation == null) return;
            Simulation.EventRaised -= OnContestEvent; Simulation.EventRaised += OnContestEvent;
            Simulation.Completed -= OnMatchCompleted; Simulation.Completed += OnMatchCompleted;
        }
#endif
        void OnDestroy() => DetachSimulation();
    }
}
