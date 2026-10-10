using System;
using System.IO;
using System.Linq;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>对真实模型执行托举连续性回归。检查固定步、三个插值位置及显示对模拟的隔离。</summary>
public static class BeetleLiftContinuityValidation
{
    [MenuItem("Tools/Beetle Arena/Validate Lift Continuity")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before validation.");
        const string temporary = "Assets/BeetleArena/Generated/LiftContinuityValidation.unity";
        var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!AssetDatabase.CopyAsset(BeetleArenaSceneBuilder.ScenePath, temporary)) throw new Exception("Temporary scene exists.");
        var scene = EditorSceneManager.OpenScene(temporary, OpenSceneMode.Additive);
        var report = new StringBuilder("LIFT CONTINUITY REGRESSION\n");
        try
        {
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            var match = scene.GetRootGameObjects().Select(go => go.GetComponent<BeetleMatchController>()).First(m => m);
            match.hud.Bind(match);
            ValidateStruggle(match, report);
            ValidateRenderIsolation(match, report);
            foreach (int arena in new[] { 0, 1 })
                foreach (int seed in new[] { 7319, 71, 2731 }) ValidateMatch(match, arena, seed, report);
            report.AppendLine("PASS: all continuity, contact and replay checks.");
        }
        finally
        {
            Directory.CreateDirectory("Artifacts/LiftContinuity");
            File.WriteAllText("Artifacts/LiftContinuity/Validation.txt", report.ToString());
            EditorSceneManager.CloseScene(scene, true);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
            AssetDatabase.DeleteAsset(temporary);
        }
    }

    static Vector3 Position(BeetleState b) => new Vector3(b.Position.x, b.Height, b.Position.y);
    static void Tick(BeetleMatchController match)
    { foreach (var actor in match.actors) actor.BeforeStep(); match.Simulation.Step(.02f); }

    static void ValidateStruggle(BeetleMatchController match, StringBuilder report)
    {
        match.SelectArena(0); match.StartMatch(71);
        var state = match.Simulation.Fighters[0]; var anim = match.actors[0].animator;
        state.Airborne = true;
        float last = 0;
        for (int i = 0; i <= 200; i++)
        {
            state.Lift.UnsupportedTime = i * .01f;
            anim.Pose(state, 0, 0);
            if (i < 70 && anim.StruggleWeight > 0) throw new Exception("Premature struggle.");
            if (anim.StruggleFrequency < last - .00001f) throw new Exception("Struggle frequency went backwards.");
            if (i > 71 && anim.StruggleFrequency - last > .05f) throw new Exception("Struggle frequency jumped.");
            last = anim.StruggleFrequency;
        }
        if (Mathf.Abs(last - match.settings.struggleMaxHz) > .001f) throw new Exception("Fast struggle was not reached.");
        state.Airborne = false; anim.Pose(state, 0, 0);
        if (anim.StruggleWeight != 0 || anim.StruggleFrequency != 0) throw new Exception("Landing did not reset struggle.");
        report.AppendLine($"PASS: delayed struggle, continuous acceleration {match.settings.struggleStartHz:F2} -> {last:F2} Hz, landing reset.");
    }

    // 同一受控托举分别运行无显示、每步三次插值显示。对比完整根轨迹，而非只比较结局。
    static void ValidateRenderIsolation(BeetleMatchController match, StringBuilder report)
    {
        var reference = new Vector3[600];
        for (int run = 0; run < 2; run++)
        {
            match.SelectArena(0); match.StartMatch(71);
            var sim = match.Simulation;
            while (sim.Phase == MatchPhase.Countdown) Tick(match);
            var a = sim.Fighters[0]; var b = sim.Fighters[1];
            a.Position = new Vector2(-2.05f, 0); b.Position = new Vector2(2.05f, 0); a.Heading = 90; b.Heading = -90;
            a.Action = BeetleAction.PryWindup; a.ActionTime = 0; a.ActionDuration = .55f;
            b.Action = BeetleAction.Brace; b.ActionDuration = 10; b.Balance = .3f; b.Stamina = 35;
            sim.SetGround(match.ActiveSurface);
            for (int i = 0; i < 2; i++) match.actors[i].Bind(sim.Fighters[i], match.settings, match.ActiveSurface);
            match.Contacts.Solve(sim, 0);
            for (int tick = 0; tick < 300; tick++)
            {
                Tick(match);
                for (int i = 0; i < 2; i++)
                {
                    var p = Position(sim.Fighters[i]); int index = tick * 2 + i;
                    if (run == 0) reference[index] = p;
                    else if (Vector3.Distance(reference[index], p) > .00001f)
                        throw new Exception($"Rendering contaminated simulation at tick {tick}, actor {i}.");
                }
                if (run == 1)
                    foreach (float alpha in new[] { .25f, .5f, .75f })
                    {
                        foreach (var actor in match.actors) actor.Render(alpha, sim.Elapsed, .005f);
                        match.Contacts.ProjectView(sim, .005f);
                    }
            }
        }
        report.AppendLine("PASS: 600 root samples identical with zero versus three interpolated renders per step (tolerance 0.00001).");
    }

    static void ValidateMatch(BeetleMatchController match, int arena, int seed, StringBuilder report)
    {
        match.SelectArena(arena); match.StartMatch(seed);
        var sim = match.Simulation;
        while (sim.Phase == MatchPhase.Countdown) Tick(match);
        float nearLift = 0, maxStep = 0, maxViewStep = 0, depth = 0, peak = 0;
        int supported = 0, struggles = 0, stalled = 0, longestStall = 0;
        var lastView = match.actors.Select(a => a.transform.position).ToArray();
        bool previousMeasured = false;
        for (int tick = 0; tick < 15000 && sim.Phase != MatchPhase.Finished; tick++)
        {
            var before = sim.Fighters.Select(Position).ToArray(); Tick(match);
            if (sim.Fighters.Any(b => b.Lift.Supported))
            {
                nearLift = sim.Elapsed + 2; supported++;
                peak = Mathf.Max(peak, sim.Fighters.Where(b => b.Lift.Supported).Max(b => b.Height - b.SurfaceHeight));
            }
            if (match.actors.Any(a => a.animator.StruggleWeight > .01f)) struggles++;
            if (sim.Elapsed >= nearLift) { foreach (var actor in match.actors) actor.Render(.75f, sim.Elapsed, .02f); match.Contacts.ProjectView(sim, .02f); previousMeasured = false; continue; }
            stalled = match.Contacts.AcceptedMotionFraction == 0 ? stalled + 1 : 0;
            longestStall = Mathf.Max(longestStall, stalled);
            for (int i = 0; i < 2; i++)
            {
                var b = sim.Fighters[i];
                if (b.HasFallen) continue;
                float step = Vector3.Distance(before[i], Position(b)); maxStep = Mathf.Max(maxStep, step);
                // 自由落体允许按实际速度移动；这里拦截无速度来源的整虫突跳。
                float allowed = Mathf.Max(.081f, new Vector3(b.Velocity.x, b.VerticalVelocity, b.Velocity.y).magnitude * .02f + .02f);
                if (step > allowed) throw new Exception($"Root jump: arena={arena}, seed={seed}, time={sim.Elapsed:F2}, step={step:F4}.");
            }
            depth = Mathf.Max(depth, match.Contacts.MeasurePenetration());
            foreach (float alpha in new[] { .25f, .5f, .75f })
            {
                foreach (var actor in match.actors) actor.Render(alpha, sim.Elapsed, .005f);
                match.Contacts.ProjectView(sim, .005f);
                for (int i = 0; i < 2; i++)
                {
                    if (previousMeasured && !sim.Fighters[i].HasFallen)
                        maxViewStep = Mathf.Max(maxViewStep, Vector3.Distance(lastView[i], match.actors[i].transform.position));
                    lastView[i] = match.actors[i].transform.position;
                }
                previousMeasured = true;
                depth = Mathf.Max(depth, match.Contacts.MeasurePenetration());
            }
        }
        report.AppendLine($"arena={arena} seed={seed} phase={sim.Phase} seconds={sim.Elapsed:F1}, supportedSteps={supported}, rise={peak:F3}, struggleSteps={struggles}, rootStep={maxStep:F4}, viewStep={maxViewStep:F4}, overlap={depth:F6}, longestRejected={longestStall}, limitedSteps={match.Contacts.LimitedMotionSteps}.");
        if (depth > .003f || longestStall > 25 || sim.Phase != MatchPhase.Finished)
            throw new Exception("Continuity/contact/match completion regression. See Artifacts/LiftContinuity/Validation.txt.");
    }
}


