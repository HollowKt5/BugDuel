using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEngine;

/// <summary>模块独立性、真实接触与长时间活体反馈验证。临时场景/配置均不污染用户资产。</summary>
public static class BeetleModuleValidation
{
    static void Require(bool value, string why) { if (!value) throw new Exception(why); }
    [MenuItem("Tools/Beetle Arena/Validate Motion Modules")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new Exception("Stop Play first");
        using var fixture = new BeetleValidationScene("ModuleValidation");
        var report = new StringBuilder("MODULE VALIDATION\n" + DateTimeOffset.Now.ToString("o") + "\n");
        Directory.CreateDirectory("Artifacts/ModularActions");
        var settings = fixture.Settings;
        try
        {
            var match = fixture.Match;
            var configured = settings.motionModules;
            Contracts(settings, report);
            RegistryChanges(settings, report);
            foreach (var module in configured.Where(m => m && m.Channel == BeetleMotionChannel.Combat))
            {
                int hits = 0;
                for (int variant = 0; variant < 3; variant++) hits += Combat(match, module, variant, report);
                report.AppendLine($"module={module.name}: total real-contact hits={hits}");
                Require(module.AppliesContactResponse ? hits > 0 : hits == 0,
                    module.AppliesContactResponse ? "Module never made a real contact: " + module.name : "Feint produced a combat impulse");
            }
            settings.motionModules = configured;
            AliveAfterFinish(match, report);
            for (int arena = 0; arena < 2; arena++) NaturalMatch(match, arena, report);
            report.AppendLine("ALL MODULE CHECKS PASSED");
        }
        finally
        {
            File.WriteAllText("Artifacts/ModularActions/Validation.txt", report.ToString());
        }
    }
    static void Contracts(BeetleMatchSettings s, StringBuilder report)
    {
        var original = s.motionModules;
        var module = ScriptableObject.CreateInstance<BeetleModuleValidationProbe>();
        try
        {
            // 插入一个新类型而不改运行核心：新增/重复/空引用/移除/禁用/实例隔离。
            s.motionModules = new BeetleMotionModule[] { null, module, module };
            var sim = new BeetleSimulation(s, 7); var a = sim.Fighters[0]; var b = sim.Fighters[1];
            Require(a.Motion.TryStart(.5f), "New module cannot register");
            a.Motion.Tick(.1f);
            Require(a.Motion.Elapsed > 0 && b.Motion.Elapsed == 0 && !b.Motion.HasActive, "Shared asset shares mutable state");
            Require(a.Motion.PoseSample().BodyRoll == 2, "Duplicate module applied twice");
            module.enabledInMatch = false; a.Motion.Tick(.02f); Require(!a.Motion.HasActive, "Disabled action survives");
            module.enabledInMatch = true;
            sim = new BeetleSimulation(s, 9); a = sim.Fighters[0];
            Require(a.Motion.TryStart(.5f), "Module could not restart in new match");
            s.motionModules = Array.Empty<BeetleMotionModule>(); a.Motion.Refresh(sim, a); a.Motion.Tick(.02f);
            Require(!a.Motion.HasActive, "Removed action survives");
            Require(!a.Motion.TryStart(.5f), "Empty list invents action");
            s.motionModules = new[] { module }; sim = new BeetleSimulation(s, 11); a = sim.Fighters[0];
            Require(a.Motion.TryStart(.5f), "Interrupted module fixture could not start");
            typeof(BeetleSimulation).GetMethod("SetAction", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(sim, new object[] { a, BeetleAction.Stagger, .5f, nameof(Contracts), 0 });
            Require(!a.Motion.HasActive, "State interruption leaves module pose active");
            report.AppendLine("PASS: new-type registration, null/duplicate references, per-actor isolation, disable/remove/empty-list fallback.");
        }
        finally { s.motionModules = original; UnityEngine.Object.DestroyImmediate(module); }
    }
    static void RegistryChanges(BeetleMatchSettings settings, StringBuilder report)
    {
        var original = settings.motionModules;
        var first = ScriptableObject.CreateInstance<BeetleModuleValidationProbe>();
        var second = ScriptableObject.CreateInstance<BeetleModuleValidationProbe>();
        try
        {
            settings.motionModules = new BeetleMotionModule[] { first, second };
            var sim = new BeetleSimulation(settings, 19);
            var a = sim.Fighters[0]; var b = sim.Fighters[1];
            // Same array, different order: authored edits must remain visible without a restart.
            settings.motionModules[0] = second; settings.motionModules[1] = first;
            a.Motion.Refresh(sim, a);
            Require(a.Motion.TryStart(0) && a.Motion.Active == second, "In-place reorder retained stale priority");
            a.Motion.MarkHit(); a.Motion.Tick(.1f); a.Velocity = new Vector2(.3f, .4f);
            for (int i = 0; i < 100; i++) a.Motion.Refresh(sim, a);
            Require(a.Motion.Active == second && a.Motion.Starts == 1 && a.Motion.Hits == 1 &&
                Mathf.Abs(a.Motion.Elapsed - .1f) < 1e-6f && Mathf.Abs(a.Motion.Context.Speed - .5f) < 1e-6f,
                "Unchanged registry reset action state or stopped refreshing context");
            settings.motionModules[0] = null;
            a.Motion.Refresh(sim, a); a.Motion.Tick(.02f);
            Require(!a.Motion.HasActive, "In-place removal kept the active module");
            settings.motionModules[1] = second;
            b.Motion.Refresh(sim, b);
            Require(b.Motion.TryStart(0) && b.Motion.Active == second, "Same-length replacement was ignored");
            UnityEngine.Object.DestroyImmediate(second);
            b.Motion.Refresh(sim, b); b.Motion.Tick(.02f);
            Require(!b.Motion.HasActive && !b.Motion.TryStart(0), "Destroyed cached asset remained usable");
            settings.motionModules = null;
            a.Motion.Refresh(sim, a);
            Require(!a.Motion.TryStart(0), "Null configuration invented a module");
            report.AppendLine("PASS: in-place reorder/removal/replacement, unchanged-list action/context continuity, destroyed asset cancellation and null fallback.");
        }
        finally
        {
            settings.motionModules = original;
            UnityEngine.Object.DestroyImmediate(first);
            if (second) UnityEngine.Object.DestroyImmediate(second);
        }
    }

    static int Combat(BeetleMatchController match, BeetleMotionModule module, int variant, StringBuilder report)
    {
        match.settings.motionModules = new[] { module };
        match.SelectArena(variant == 2 ? 1 : 0); match.StartMatch(71);
        var sim = match.Simulation;
        while (sim.Phase == MatchPhase.Countdown) sim.Step(.02f);
        var a = sim.Fighters[0]; var b = sim.Fighters[1];
        // Modules read the individual's phase; the simulation's aggregate Exchange is HUD-only.
        a.CombatPhase = ExchangePhase.Clash;
        float gap = variant == 1 ? 3.1f : 3.65f;
        a.Position = new Vector2(-gap * .5f, 0); b.Position = new Vector2(gap * .5f, 0);
        a.Heading = 90; b.Heading = variant == 1 ? 30 : -90;
        a.Action = b.Action = BeetleAction.Brace; a.ActionTime = b.ActionTime = 0; a.ActionDuration = b.ActionDuration = 20;
        sim.SetGround(match.ActiveSurface);
        for (int i = 0; i < 2; i++) match.actors[i].Bind(sim.Fighters[i], match.settings, match.ActiveSurface);
        match.Contacts.Solve(sim, 0);
        // 只为测试进入动作提供触碰历史；产生冲量仍必须通过真实几何探测。
        a.TouchingOpponent = true; a.Motion.Refresh(sim, a);
        bool started = a.Motion.TryStart(.2f);
        if (!started) { report.AppendLine($"{module.name}, variant={variant}: geometry not eligible"); return 0; }
        a.Action = BeetleAction.Module; a.ActionDuration = a.Motion.Duration; a.ActionTime = 0;
        float maxOverlap = 0, excess = 0, feet = 0, headAngle = 0;
        Quaternion startHead = match.actors[0].animator.headPivot.localRotation;
        for (int tick = 0; tick < 100; tick++)
        {
            if (!a.Motion.HasActive) { a.Action = BeetleAction.Brace; a.ActionDuration = 20; a.ActionTime = 0; }
            var old = new Vector3(a.Position.x, a.Height, a.Position.y);
            var foot = match.actors[0].animator.legs[3].lastFoot;
            foreach (var actor in match.actors) actor.BeforeStep(); sim.Step(.02f);
            feet += Vector3.Distance(foot, match.actors[0].animator.legs[3].lastFoot);
            headAngle = Mathf.Max(headAngle, Quaternion.Angle(startHead, match.actors[0].animator.headPivot.localRotation));
            excess = Mathf.Max(excess, Vector3.Distance(old, new Vector3(a.Position.x, a.Height, a.Position.y)) - match.Contacts.MotionBudget(0));
            maxOverlap = Mathf.Max(maxOverlap, match.Contacts.MeasurePenetration());
            float elapsed = a.Motion.Elapsed; int hitCount = a.Motion.Hits;
            foreach (var actor in match.actors) actor.Render(.65f, sim.MotionTime, .02f);
            match.Contacts.ProjectView(sim, .02f);
            Require(elapsed == a.Motion.Elapsed && hitCount == a.Motion.Hits, "Render modifies action state");
            if (tick == 24 && variant == 0) Capture(match, module.name + ".png");
        }
        report.AppendLine($"{module.name}, variant={variant}, starts={a.Motion.Starts}, hits={a.Motion.Hits}, feet={feet:F3}, head={headAngle:F2}, excess={excess:F6}, overlap={maxOverlap:F6}");
        Require(!a.Motion.HasActive && a.Motion.Hits <= 1, "Action stuck or repeated hit");
        Require(excess < .0003f && maxOverlap < .003f, "Action breaks contact continuity");
        Require(headAngle > 2, "Action has no visible pose");
        return a.Motion.Hits;
    }
    static void AliveAfterFinish(BeetleMatchController match, StringBuilder report)
    {
        match.StartMatch(71); var sim = match.Simulation;
        while (sim.Phase == MatchPhase.Countdown) sim.Step(.02f);
        var b = sim.Fighters[1]; b.Position = new Vector2(8, 0); b.Height = -1; b.HasFallen = b.Airborne = true; b.Action = BeetleAction.Defeated;
        for (int i = 0; i < 2; i++) match.actors[i].Bind(sim.Fighters[i], match.settings, match.ActiveSurface);
        float[] footSpan = new float[2], head = new float[2], antenna = new float[2];
        var footStart = match.actors.Select(a => new Vector3[a.animator.legs.Length]).ToArray();
        var antennaStart = match.actors.Select(a => new Quaternion[a.animator.antennae.Length]).ToArray();
        var headStart = new Quaternion[2];
        for (int tick = 0; tick < 750; tick++)
        {
            foreach (var a in match.actors) a.BeforeStep();
            sim.Step(.02f);
            if (tick >= 400)
                for (int i = 0; i < 2; i++)
                {
                    var actor = match.actors[i]; var anim = actor.animator;
                    if (tick == 400) headStart[i] = anim.headPivot.localRotation;
                    head[i] = Mathf.Max(head[i], Quaternion.Angle(headStart[i], anim.headPivot.localRotation));
                    // Support adjustment rotates among six legs; one fixed foreleg may remain planted.
                    // Measure excursion, not accumulated numerical jitter, and remove root motion.
                    for (int leg = 0; leg < anim.legs.Length; leg++)
                    {
                        Vector3 point = actor.transform.InverseTransformPoint(anim.legs[leg].lastFoot);
                        if (tick == 400) footStart[i][leg] = point;
                        footSpan[i] = Mathf.Max(footSpan[i], Vector3.Distance(footStart[i][leg], point));
                    }
                    for (int feeler = 0; feeler < anim.antennae.Length; feeler++)
                    {
                        var rotation = anim.antennae[feeler].localRotation;
                        if (tick == 400) antennaStart[i][feeler] = rotation;
                        antenna[i] = Mathf.Max(antenna[i], Quaternion.Angle(antennaStart[i][feeler], rotation));
                    }
                }
        }
        report.AppendLine($"post-finish 8..15s: winnerFootSpan={footSpan[0]:F3}, loserFootSpan={footSpan[1]:F3}, winnerHead={head[0]:F2}, loserHead={head[1]:F2}, winnerAntenna={antenna[0]:F2}, loserAntenna={antenna[1]:F2}, clock={sim.MotionTime:F2}, elapsed={sim.Elapsed:F2}");
        Require(sim.Phase == MatchPhase.Finished && sim.Winner == 0, "Post-finish activity fixture did not settle its expected result");
        for (int i = 0; i < 2; i++)
            Require(head[i] > 2 && (footSpan[i] > .04f || antenna[i] > 3),
                (i == 0 ? "Winner" : "Loser") + " lacks visible post-finish head and limb/antenna activity");
    }
    static void Capture(BeetleMatchController match, string name)
    {
        typeof(BeetleArenaCollisionValidation).GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { match, name });
        File.Copy("Artifacts/BeetleArena/" + name, "Artifacts/ModularActions/" + name, true);
    }
    static void NaturalMatch(BeetleMatchController match, int arena, StringBuilder report)
    {
        match.SelectArena(arena); match.StartMatch(71);
        var sim = match.Simulation;
        var starts = new System.Collections.Generic.Dictionary<string, int>();
        float overlap = 0, excess = 0, blocked = 0, maxBlocked = 0;
        for (int tick = 0; tick < 14000 && sim.Phase != MatchPhase.Finished; tick++)
        {
            var counts = sim.Fighters.Select(b => b.Motion.Starts).ToArray();
            var old = sim.Fighters.Select(b => new Vector3(b.Position.x, b.Height, b.Position.y)).ToArray();
            // 自然对局走正式固定步/显示入口；插值隔离另有低层专门测试。
            match.AdvanceFixedStep(); match.Present(1, sim.MotionTime, .02f);
            for (int i = 0; i < 2; i++)
            {
                var b = sim.Fighters[i];
                if (b.Motion.Starts > counts[i] && b.Motion.Active)
                { string name = b.Motion.Active.name; starts[name] = starts.TryGetValue(name, out int n) ? n + 1 : 1; }
                if (sim.Phase != MatchPhase.Countdown)
                    excess = Mathf.Max(excess, Vector3.Distance(old[i], new Vector3(b.Position.x, b.Height, b.Position.y)) - match.Contacts.MotionBudget(i));
            }
            if (sim.Phase == MatchPhase.Fighting)
            {
                blocked = match.Contacts.AcceptedMotionFraction <= 0 ? blocked + .02f : 0;
                maxBlocked = Mathf.Max(maxBlocked, blocked);
            }
            if (tick % 5 == 0) overlap = Mathf.Max(overlap, match.Contacts.MeasurePenetration());
        }
        report.AppendLine($"natural arena={arena}: finished={sim.Phase}, seconds={sim.Elapsed:F1}, winner={sim.Winner}, starts=" +
            string.Join(",", starts.Select(kv => kv.Key + ":" + kv.Value)) + $", hits={sim.Fighters.Sum(b => b.Motion.Hits)}, blocked={maxBlocked:F2}s, excess={excess:F6}, overlap={overlap:F6}");
        Require(sim.Phase == MatchPhase.Finished && starts.Count >= 2, "Natural match missing module variety or never finished");
        Require(maxBlocked < 3 && excess < .0003f && overlap < .003f, "Natural fight stuck/teleported/penetrated");
    }
}

public sealed class BeetleModuleValidationProbe : BeetleMotionModule
{
    public override string DisplayName => "test-only-module";
    public override BeetleMotionChannel Channel => BeetleMotionChannel.Combat;
    public override bool CanStart(in BeetleMotionContext c) => true;
    public override bool CanContinue(in BeetleMotionContext c) => true;
    public override BeetleMotionSample Sample(in BeetleMotionContext c, float p) => new BeetleMotionSample { BodyRoll = 2 };
}
