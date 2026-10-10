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
            if (arenas == null || arenas.Length < 2) return;
            SelectArena((arenaIndex + 1) % arenas.Length);
            StartMatch(Simulation != null ? Simulation.Seed : settings.initialSeed);
        }

        public void SelectArena(int index)
        {
            if (arenas == null || arenas.Length == 0) return;
            arenaIndex = Mathf.Clamp(index, 0, arenas.Length - 1);
            for (int i = 0; i < arenas.Length; i++) if (arenas[i]) arenas[i].gameObject.SetActive(i == arenaIndex);
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
        // accumulator 为未消费的模拟时间，animationTime 为表现时间，hudClock 控制 10Hz UI 刷新。
        float accumulator, animationTime, hudClock;

        // 运行入口：检查必需引用、约束参数、绑定按钮，再按配置或当前时刻选择初始种子。
        void Start()
        {
            if (!settings || actors == null || actors.Length != 2 || !actors[0] || !actors[1] || !hud || !spectatorCamera)
            {
                Debug.LogError("Beetle Arena is missing required scene references.", this);
                enabled = false; return;
            }
            settings.Validate();
            // 每次进入游戏默认平地；局内换场/重赛仍由 StartMatch 保留当前选择。
            SelectArena(0);
            hud.Bind(this);
            int seed = settings.initialSeed == 0 ? unchecked(Environment.TickCount * 397 ^ DateTime.UtcNow.Millisecond) : settings.initialSeed;
            StartMatch(seed);
        }

        // 统一换局入口：解绑旧事件、创建状态、重置计时、绑定模型、安装接触求解器并刷新界面。
        // 初次 Solve(dt=0) 处理初始接触；之后由固定步长正式推进。
        public void StartMatch(int seed)
        {
            if (Simulation != null) Simulation.EventRaised -= OnContestEvent;
            Simulation = new BeetleSimulation(settings, seed);
            SelectArena(arenaIndex);
            Simulation.SetGround(ActiveSurface);
            Simulation.EventRaised += OnContestEvent;
            accumulator = animationTime = 0; IsPaused = false; RoundNumber++;
            LatestEvent = "触角试探 · 即将开始";
            for (int i = 0; i < actors.Length; i++) actors[i].Bind(Simulation.Fighters[i], settings, ActiveSurface);
            Contacts = new BeetleContactSolver(actors, arenaContacts, .006f);
            Simulation.ContactSolver = Contacts;
            Contacts.Solve(Simulation, 0);
            spectatorCamera.ResetView();
            MatchChanged?.Invoke();
            hud.Refresh();
        }

        // 仅结束后允许换新阵容；用整数线性变换生成下一种子，unchecked 允许预期的整数回绕。
        public void NextMatch()
        {
            if (Simulation == null || Simulation.Phase != MatchPhase.Finished) return;
            StartMatch(unchecked(Simulation.Seed * 1664525 + 1013904223));
        }
        // 沿用上局种子重新创建初始阵容；没有录制加油输入，也不是逐帧录像回放。
        public void Rematch()
        {
            if (Simulation == null || Simulation.Phase != MatchPhase.Finished) return;
            StartMatch(Simulation.Seed);
        }
        // UI 和键盘共用的鼓励入口：暂停时拒绝，具体资格/冷却由 Simulation.Encourage 检查。
        public bool Cheer(int index)
        {
            bool accepted = !IsPaused && Simulation != null && Simulation.Encourage(index);
            if (accepted) hud.Refresh();
            return accepted;
        }
        // 暂停通过本控制器的 dt=0 实现，不修改全局 Time.timeScale；结束后不再切换暂停。
        public void TogglePause()
        {
            if (Simulation == null || Simulation.Phase == MatchPhase.Finished) return;
            IsPaused = !IsPaused; hud.Refresh();
        }

        // 每个显示帧读取键盘，再通过累积器执行零次或多次固定模拟步，最后插值显示与刷新镜头。
        void Update()
        {
            if (Simulation == null) return;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.qKey.wasPressedThisFrame) Cheer(0);
                if (keyboard.eKey.wasPressedThisFrame) Cheer(1);
                if (keyboard.spaceKey.wasPressedThisFrame) TogglePause();
                if (keyboard.rKey.wasPressedThisFrame) NextMatch();
                if (keyboard.tKey.wasPressedThisFrame) SwitchArena();
            }
            // 使用不受 timeScale 影响的时间；单帧最多补 0.1 秒，避免长卡顿后无限追帧。
            // 该限额也意味着严重卡顿时，比赛时间可能落后于真实墙钟时间。
            float dt = IsPaused ? 0 : Mathf.Min(Time.unscaledDeltaTime, .1f);
            animationTime += dt;
            accumulator += dt;
            // 每一步先缓存旧根节点位置，Step 内更新 AI、接触和状态，再扣掉已消费的 0.02 秒。
            while (accumulator >= BeetleSimulation.FixedStep)
            {
                foreach (var actor in actors) actor.BeforeStep();
                Simulation.Step(BeetleSimulation.FixedStep);
                accumulator -= BeetleSimulation.FixedStep;
            }
            // 剩余时间/固定步长得到插值系数；显示在旧状态和新状态之间平滑过渡。
            float alpha = accumulator / BeetleSimulation.FixedStep;
            foreach (var actor in actors) actor.Render(alpha, animationTime, dt);
            // 插值可能重新引入重叠，因此显示前另做位置约束；该修正不回写模拟状态。
            Contacts.ProjectView(Simulation, dt);
            spectatorCamera.Tick(Simulation, dt);
            // HUD 使用真实帧时间每 0.1 秒刷新，暂停时按钮文案仍可更新。
            hudClock += Time.unscaledDeltaTime;
            if (hudClock >= .1f) { hudClock = 0; hud.Refresh(); }
        }
        // 把最近一条比赛事件交给 HUD，并按强度触发接触音效；不从音效反向驱动战斗。
        void OnContestEvent(ContestEvent e)
        {
            LatestEvent = e.Message;
            if (audioFeedback && e.Intensity > 0) audioFeedback.Contact(e.Actor, e.Intensity, actors);
        }
        // 销毁时解除模拟事件订阅，防止场景切换后残留回调引用。
        void OnDestroy() { if (Simulation != null) Simulation.EventRaised -= OnContestEvent; }
    }
}
