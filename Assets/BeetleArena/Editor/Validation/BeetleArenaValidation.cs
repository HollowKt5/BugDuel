using System;
using System.IO;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEngine;

// Headless data validator: spawns no beetle GameObjects and injects no physical contact solvers.
// Used for combat rules and numerical regressions; mesh penetration and physical pacing are validated in CollisionValidation.
public static class BeetleArenaValidation
{
    [MenuItem("Tools/甲壳虫斗/验证/战斗/基础模拟", false, 500)]
    // Sequentially checks size bounds, identical-input replay, encouragement permissions, ring-out rules, and 64 automated matches, outputting text/CSV.
    public static void Run()
    {
        var settings = AssetDatabase.LoadAssetAtPath<BeetleMatchSettings>(BeetleArenaSceneBuilder.AssetFolder + "/MatchSettings.asset");
        Require(settings, "Missing match settings");
        var report = new StringBuilder("BEETLE ARENA VALIDATION\n");
        // Samples random pairings to verify they adhere to size ranges and maximum relative size differentials.
        for (int seed = 0; seed < 2000; seed++)
        {
            var sim = new BeetleSimulation(settings, seed);
            float a = sim.Fighters[0].Traits.size, b = sim.Fighters[1].Traits.size;
            Require(a >= settings.minimumSize && b >= settings.minimumSize && a <= settings.maximumSize && b <= settings.maximumSize, "Size bounds");
            Require(Mathf.Max(a, b) / Mathf.Min(a, b) <= 1 + settings.maximumSizeDifference + .00001f, "Pair mismatch cap");
        }
        report.AppendLine("PASS: 2000 seeds respect size range and maximum relative difference.");
        // Re-evaluates headless simulation with identical seed and input timeline; demonstrates reproducibility within current environment,
        // without claiming bitwise cross-platform determinism for scene physics.
        var first = Started(settings, 189);
        var second = Started(settings, 189);
        for (int i = 0; i < 3500; i++)
        {
            if (i % 401 == 0) { first.Encourage(0); second.Encourage(0); }
            first.Step(.02f); second.Step(.02f);
        }
        for (int i = 0; i < 2; i++) Require(first.Fighters[i].Position == second.Fighters[i].Position && first.Fighters[i].Stamina == second.Fighters[i].Stamina, "Deterministic replay");
        report.AppendLine("PASS: identical seeds and encouragement timelines reproduce state exactly.");
        // Encouragement must accept first valid request, reject cooldown spam and invalid indices, and cannot manipulate position/actions directly.
        var cheer = Started(settings, 91);
        var p = cheer.Fighters[0].Position; var v = cheer.Fighters[0].Velocity; var action = cheer.Fighters[0].Action;
        Require(cheer.Encourage(0), "First encouragement accepted");
        Require(!cheer.Encourage(0), "Repeated encouragement rejected");
        Require(cheer.Fighters[0].Position == p && cheer.Fighters[0].Velocity == v && cheer.Fighters[0].Action == action, "Encouragement cannot steer or attack");
        Require(!cheer.Encourage(-1) && !cheer.Encourage(2), "Invalid fighter indices rejected");
        report.AppendLine("PASS: encouragement cannot change position, velocity or action; cooldown rejects spam.");
        // Evaluates zero stamina, edge proximity, single ring-out, and simultaneous ring-out, preventing edge grazes or fatigue from false loss rulings.
        var inside = Started(settings, 7);
        inside.Fighters[0].Stamina = 0;
        inside.Fighters[0].Position = new Vector2(settings.arenaRadius - .05f, 0);
        inside.Step(.02f);
        Require(!inside.Fighters[0].HasFallen && inside.Phase == MatchPhase.Fighting, "Edge touch or zero stamina must not lose");
        var single = Started(settings, 8);
        single.Fighters[0].Position = new Vector2(settings.arenaRadius + .5f, 0);
        single.Fighters[0].Height = -.8f;
        for (int i = 0; i < 100; i++) single.Step(.02f);
        Require(single.Phase == MatchPhase.Finished && single.Winner == 1, "Actual fall decides winner");
        Require(!single.Encourage(1), "Finished match blocks cheer");
        var both = Started(settings, 9);
        for (int i = 0; i < 2; i++) { both.Fighters[i].Position = new Vector2((i == 0 ? -1 : 1) * (settings.arenaRadius + .5f), 0); both.Fighters[i].Height = -.8f; }
        both.Step(.02f);
        Require(both.Phase == MatchPhase.Finished && both.Winner == -1, "Simultaneous fall is a draw");
        report.AppendLine("PASS: no stamina knockout or edge-touch loss; single falls, double falls and post-match input handled.");

        int completed = 0, winnerA = 0, shoves = 0, pries = 0;
        float longest = 0, shortest = 300, total = 0;
        int inDurationRange = 0;
        var durations = new StringBuilder("Seed,EncouragedFighter,Winner,Seconds\n");
        // Sweeps batches across unassisted, encourage A, and encourage B inputs, asserting finite values, stamina, and balance ranges.
        for (int seed = 0; seed < 64; seed++)
        {
            var sim = Started(settings, seed * 7919 + 31);
            for (int tick = 0; tick < 15000 && sim.Phase != MatchPhase.Finished; tick++)
            {
                if (seed % 3 == 1 && tick % 310 == 0) sim.Encourage(0);
                if (seed % 3 == 2 && tick % 310 == 0) sim.Encourage(1);
                sim.Step(.02f);
                foreach (var fighter in sim.Fighters)
                {
                    Require(Finite(fighter.Position.x) && Finite(fighter.Position.y) && Finite(fighter.Height), "Finite state");
                    Require(fighter.Stamina >= 0 && fighter.Stamina <= fighter.Traits.endurance + .001f, "Stamina bounds");
                    Require(fighter.Balance >= 0 && fighter.Balance <= 1, "Balance bounds");
                }
            }
            if (sim.Phase == MatchPhase.Finished) completed++;
            else report.AppendLine("UNRESOLVED seed=" + sim.Seed + " positions=" + sim.Fighters[0].Position + "," + sim.Fighters[1].Position);
            if (sim.Winner == 0) winnerA++;
            foreach (var fighter in sim.Fighters) { shoves += fighter.Shoves; pries += fighter.Pries; }
            longest = Mathf.Max(longest, sim.Elapsed); shortest = Mathf.Min(shortest, sim.Elapsed); total += sim.Elapsed;
            // Logs bouts falling within target duration; actual combat terminates upon ring-out without arbitrary 180s cutoffs.
            if (sim.Elapsed >= 60 && sim.Elapsed <= 180) inDurationRange++;
            durations.AppendLine($"{sim.Seed},{(seed % 3 == 0 ? -1 : seed % 3 - 1)},{sim.Winner},{sim.Elapsed:F2}");
        }
        report.AppendLine($"Batch: {completed}/64 completed by ring-out; amber wins {winnerA}; shoves {shoves}; pries {pries}; mean {total / 64:F1}s; min {shortest:F1}s; max {longest:F1}s.");
        report.AppendLine($"Duration target: {inDurationRange}/64 within 60-180 seconds. A fall is always decisive, including during the opening; no forced result timer.");
        Directory.CreateDirectory("Artifacts/BeetleArena");
        File.WriteAllText("Artifacts/BeetleArena/SimulationValidation.txt", report.ToString());
        File.WriteAllText("Artifacts/BeetleArena/MatchDurations.csv", durations.ToString());
        Require(completed == 64, "Autonomous matches must finish without intervention (see report)");
        Require(shoves > 0 && pries > 0, "Both shoving and escalating pry behaviour exercised");
        Require(winnerA > 0 && winnerA < 64, "Both contestants can win");
        // This test exercises the data-only fallback; actual game timings and
        // mesh contact safety are measured in BeetleArenaCollisionValidation.
        report.AppendLine("ALL CHECKS PASSED");
        File.WriteAllText("Artifacts/BeetleArena/SimulationValidation.txt", report.ToString());
        Debug.Log(report.ToString());
    }
    // Instantiates headless instance and consumes opening countdown, returning state eligible for encouragement and standard combat.
    static BeetleSimulation Started(BeetleMatchSettings settings, int seed)
    {
        var sim = new BeetleSimulation(settings, seed);
        while (sim.Phase == MatchPhase.Countdown) sim.Step(.02f);
        return sim;
    }
    // Rejects NaN and positive/negative infinity, catching integration or division errors early.
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    // Standard assertion entry: throws on condition failure; batch runner catches exception and returns failure exit code.
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Validation failed: " + message); }
}
