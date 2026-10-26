using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BeetleArena
{
    // Scene coordinator: orchestrates data simulation, two Actors, contacts, HUD, camera, and audio.
    // Input maps strictly to encourage/pause/rematch; combat tactics remain governed by Simulation.
    public sealed class BeetleMatchController : MonoBehaviour
    {
        [Header("Scene references")]
        // Scene builder writes these references; settings is shared config asset; actors must hold contestants indexed by slot.
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

        // Arena swap alters terrain only, regenerating identical roster with same seed; clears legacy contacts, footsteps, airborne, and paused states.
        public void SwitchArena()
        {
            if (arenas == null || arenas.Length < 2)
                return;
            // Arena swaps/rematches for persistent individuals must be re-validated and reserved by application layer, not reusing stale health snapshots.
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
                // Application may reject or ignore rematch requests. Commits roster arena swap only upon successful binding of new simulation.
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
        // Contact solver and simulation instances recreated per match; exposed as read-only references for display and validation tools.
        public BeetleContactSolver Contacts { get; private set; }
        public BeetleSimulation Simulation { get; private set; }
        public bool IsPaused { get; private set; }
        public int RoundNumber { get; private set; }
        public string LatestEvent { get; private set; } = "触角试探 · 即将开始";
        // Notifies external observers that a new match has finished binding; event itself contains no outcome ruling.
        public event Action MatchChanged;
        public event Action<BeetleMatchResult> MatchCompleted;
        // Cancels only replaced/destroyed matches that have not finished, allowing outer application to release contestant locks. Never fakes match results.
        public event Action<string> MatchCancelled;
        public event Action<int> PersistentMatchRequested;
        public bool CanRequestNextMatch => firstEntrant == null || PersistentMatchRequested != null;
        BeetleMatchEntrant firstEntrant;
        BeetleFeedbackSettings feedbackSettings;
        // accumulator holds unconsumed simulation time; animationTime tracks presentation time; hudClock controls 10Hz UI refresh.
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

        // Entry point: validates required references, clamps parameters, binds buttons, selects initial seed from config or timestamp.
        void Start()
        {
            if (!settings || actors == null || actors.Length != 2 || !actors[0] || !actors[1] || !hud || !spectatorCamera)
            {
                Debug.LogError("Beetle Arena is missing required scene references.", this);
                enabled = false;
                return;
            }
            hud.Bind(this);
            feedbackSettings = hud.GetComponent<BeetleFeedbackSettings>();
            BeetleCombatDisplay.Install(spectatorCamera.GetComponent<Camera>());
            // Application may deliver pre-reserved match in Awake; Unity Start must not reset its terrain or re-roll match.
            if (Simulation != null) return;
            settings.Validate();
            // Defaults to flat terrain on first entry; in-match terrain swaps and rematches preserve selection via StartMatch.
            SelectArena(0);
            int seed = settings.initialSeed == 0 ? unchecked(Environment.TickCount * 397 ^ DateTime.UtcNow.Millisecond) : settings.initialSeed;
            StartMatch(seed);
        }

        // Unified match transition: unbinds old events, instantiates state, resets timers, binds models, installs solver, refreshes UI.
        // Initial Solve(dt=0) handles starting contacts; fixed time step takes over thereafter.
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

        // Application layer can deliver a pre-reserved simulation; handles scene binding here without re-rolling seed or MatchId.
        public void StartMatch(BeetleSimulation next)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            if (!ReferenceEquals(next.Settings, settings) || next.Phase != MatchPhase.Countdown || next.ContactSolver != null)
                throw new InvalidOperationException("Bind a fresh match created with this controller's settings.");
            var first = next.Fighters[0].Entrant; var second = next.Fighters[1].Entrant;
            // Validates full request first; invalid roster cannot cancel an actively running match or modify its subscriptions.
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

        // 3x long throws can overshoot legacy ground boundary. Expands outer floor only, preserving arena rim size and ring-out evaluation.
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

        // Roster change allowed only post-match; generates next seed via linear congruential integer transform; unchecked permits expected integer wrap.
        public void NextMatch()
        {
            if (Simulation == null || Simulation.Phase != MatchPhase.Finished)
                return;
            StartMatch(unchecked(Simulation.Seed * 1664525 + 1013904223));
        }
        // Recreates initial lineup reusing previous seed; does not record encouragement input, not a deterministic frame-by-frame replay.
        public void Rematch()
        {
            if (Simulation == null || Simulation.Phase != MatchPhase.Finished)
                return;
            StartMatch(Simulation.Seed);
        }
        // Shared encouragement entry for UI and keyboard: rejected when paused; eligibility/cooldown verified by Simulation.Encourage.
        public bool Cheer(int index)
        {
            bool accepted = !IsPaused && Simulation != null && Simulation.Encourage(index);
            if (accepted)
                hud.Refresh();
            return accepted;
        }
        // Pausing implemented via controller dt=0 without mutating global Time.timeScale; cannot toggle pause after match completes.
        public void TogglePause()
        {
            if (Simulation == null || Simulation.Phase == MatchPhase.Finished)
                return;
            IsPaused = !IsPaused;
            if (audioFeedback)
                audioFeedback.SetPaused(IsPaused);
            hud.Refresh();
        }

        // Polls keyboard per display frame, advances zero or more fixed simulation steps via accumulator, then interpolates visuals and updates camera.
        void Update()
        {
            if (Simulation == null)
                return;
            var keyboard = Keyboard.current;
            if (keyboard != null && !(feedbackSettings && feedbackSettings.IsOpen))
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
            // Caches prior root position before each step; Step() updates AI, contacts, and state before deducting consumed 0.02s.
            while (accumulator >= BeetleSimulation.FixedStep)
            {
                AdvanceFixedStep();
                accumulator -= BeetleSimulation.FixedStep;
            }
            // Remainder / fixed step yields interpolation factor; visual pose smoothly transitions between previous and current states.
            float alpha = accumulator / BeetleSimulation.FixedStep;
            Present(alpha, animationTime, dt);
            // HUD refreshes every 0.1s using unscaled frame time; button labels update even while paused.
            hudClock += Time.unscaledDeltaTime;
            if (hudClock >= .1f)
            { hudClock = 0; hud.Refresh(); }
        }
        /// <summary>Shared fixed-step entry for runtime game and automated fast-forward; low-level tests can invoke Simulation.Step directly to isolate presentation.</summary>
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

        /// <summary>Shared presentation pipeline: interpolate -> penetration-preventing visual constraint -> markers (internal to constraint) -> camera; does not advance simulation.</summary>
        public void Present(float alpha, float time, float dt)
        {
            // Preserves last accepted frame during pause, eliminating redundant collision retries. HUD refreshes independently via outer loop.
            if (Simulation == null || IsPaused || dt <= 0)
                return;
            foreach (var actor in actors)
                actor.Render(alpha, time, dt);
            Contacts.ProjectView(Simulation, dt);
            spectatorCamera.Tick(BeetleCameraFrame.Capture(Simulation, actors), IsDefeatSlowMotion ? dt / .28f : dt);
        }

        // Text updates HUD only. Collision audio and sparks are driven by final contact sampling, preventing feints or ring-out text from false-triggering.
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
        // Unsubscribes from simulation events on destroy, preventing lingering callback references across scene transitions.
#if UNITY_EDITOR
        // Debugger uses this entry to pause/step, preserving authoritative fixed-step sequence without mutating global timeScale.
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
