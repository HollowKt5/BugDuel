using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEngine;

/// <summary>Measures accepted poses over complete exchanges, not isolated peak poses.</summary>
public static class BeetleMotionMixValidation
{
    const string Folder = "Artifacts/MotionMix";
    public static void Before() => Batch("Before");
    public static void After() => Batch("After");
    sealed class Metrics
    {
        public int ticks, raised, deep, starts, contacts;
        public float run, longest;
        public string previous;
        public readonly Dictionary<string, int> time = new Dictionary<string, int>();
        public readonly Dictionary<string, int> raisedByAction = new Dictionary<string, int>();
        public readonly Dictionary<string, int> transitions = new Dictionary<string, int>();
        public void Add(Dictionary<string, int> counts, string key)
        { counts.TryGetValue(key, out int n); counts[key] = n + 1; }
        public string Counts(Dictionary<string, int> counts) => string.Join(", ", counts.OrderByDescending(x => x.Value).Select(x => x.Key + "=" + x.Value));
    }
    static void Batch(string label)
    {
        Directory.CreateDirectory(Folder);
        var report = new StringBuilder("MOTION MIX " + label + "\nAngles relative to actor's terrain-aligned frame; raised=head+body < -20deg, deep < -35deg. Excludes airborne/fallen, includes real lifts. 0.02s ticks.\n");
        try
        {
            if (label == "After") VerifyMixRules(report);
            using (var fixture = new BeetleValidationScene("MotionMixValidation"))
            {
                var m = fixture.Match;
                foreach (int seed in new[] { 7319, 71 })
                for (int arena = 0; arena < 4; arena++)
                {
                    m.SelectArena(arena); m.StartMatch(seed);
                    var stats = new[] { new Metrics(), new Metrics() };
                    for (int tick = 0; tick < 3150 && m.Simulation.Phase != MatchPhase.Finished; tick++)
                    {
                        m.AdvanceFixedStep();
                        if (m.Simulation.Phase != MatchPhase.Fighting) continue;
                        for (int i = 0; i < 2; i++)
                        {
                            var b = m.Simulation.Fighters[i]; var s = stats[i];
                            if (b.HasFallen || b.Airborne) { s.run = 0; continue; }
                            var animator = m.actors[i].animator;
                            float head = Mathf.DeltaAngle(0, animator.headPivot.localEulerAngles.x);
                            float body = Mathf.DeltaAngle(0, animator.bodyPivot.localEulerAngles.x);
                            string action = b.Motion.HasActive ? b.Motion.Active.GetType().Name : b.Action.ToString();
                            s.ticks++; s.Add(s.time, action); s.contacts = b.Contacts;
                            if (head + body < -20)
                            { s.raised++; s.run += .02f; s.longest = Mathf.Max(s.longest, s.run); s.Add(s.raisedByAction, action); }
                            else s.run = 0;
                            if (head + body < -35) s.deep++;
                            if (action != s.previous)
                            {
                                if (s.previous != null) s.Add(s.transitions, s.previous + ">" + action);
                                s.starts++; s.previous = action;
                            }
                        }
                    }
                    for (int i = 0; i < 2; i++)
                    {
                        var s = stats[i];
                        report.AppendLine($"seed={seed} arena={arena} fighter={i} seconds={s.ticks*.02f:F2} raised={100f*s.raised/Math.Max(1,s.ticks):F2}% deep={100f*s.deep/Math.Max(1,s.ticks):F2}% longest={s.longest:F2}s transitions={s.starts} contacts={s.contacts}");
                        report.AppendLine("timeTicks: " + s.Counts(s.time));
                        report.AppendLine("raisedTicks: " + s.Counts(s.raisedByAction));
                        report.AppendLine("transitions: " + s.Counts(s.transitions));
                    }
                    File.WriteAllText(Folder + "/" + label + ".txt", report.ToString());
                }
            }
            report.AppendLine("MEASUREMENT COMPLETE");
            File.WriteAllText(Folder + "/" + label + ".txt", report.ToString());
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        { File.WriteAllText(Folder + "/" + label + ".txt", report.AppendLine(e.ToString()).ToString()); Debug.LogException(e); EditorApplication.Exit(1); }
    }
    static void VerifyMixRules(StringBuilder report)
    {
        var settings = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<BeetleMatchSettings>("Assets/BeetleArena/Generated/MatchSettings.asset"));
        var drive = ScriptableObject.CreateInstance<DrivingPushMotion>();
        var surge = ScriptableObject.CreateInstance<HornSurgeMotion>();
        try
        {
            settings.motionModules = new BeetleMotionModule[] { drive, surge };
            settings.moduleActionChance = 0;
            var sim = new BeetleSimulation(settings, 71);
            while (sim.Phase == MatchPhase.Countdown) sim.Step(.02f);
            void Ready(BeetleState b)
            {
                b.Action = BeetleAction.Brace; b.ActionTime = 0; b.ActionDuration = 10;
                b.Position = new Vector2(b.Index == 0 ? -1.5f : 1.5f, 0); b.Heading = b.Index == 0 ? 90 : -90;
                b.Stamina = 100; b.Footing = 1; b.BurstReserve = 1; b.TouchingOpponent = true;
                b.CombatPhase = ExchangePhase.Clash; b.Airborne = false;
            }
            foreach (var b in sim.Fighters) Ready(b);
            var a = sim.Fighters[0]; var other = sim.Fighters[1];
            a.Motion.Refresh(sim, a); a.Motion.EditorPreviewStart(drive); a.Motion.Cancel();
            settings.motionModules = new BeetleMotionModule[] { surge };
            a.Motion.Refresh(sim, a); other.Motion.Refresh(sim, other);
            if (a.Motion.TryStart(0)) throw new Exception("Alternate raised action bypasses shared recovery");
            if (!other.Motion.TryStart(0)) throw new Exception("Fighters incorrectly share recovery");
            other.Motion.Cancel();
            for (int tick = 0; tick < 160; tick++) sim.Step(.02f);
            foreach (var b in sim.Fighters) Ready(b);
            a.Motion.Refresh(sim, a);
            if (!a.Motion.TryStart(0)) throw new Exception("Shared recovery never expires");
            report.AppendLine("PASS shared raised-drive recovery across modules, independent fighters, recovery expires.");
        }
        finally
        { UnityEngine.Object.DestroyImmediate(settings); UnityEngine.Object.DestroyImmediate(drive); UnityEngine.Object.DestroyImmediate(surge); }
    }
}
