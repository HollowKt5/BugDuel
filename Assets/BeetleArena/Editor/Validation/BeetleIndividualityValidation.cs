using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using BeetleArena;
using UnityEditor;
using UnityEngine;

public static class BeetleIndividualityValidation
{
    const string Folder = "Artifacts/Individuality";
    public static void Baseline() => Batch("Before");
    public static void After() => Batch("After");
    public static void Pilot() => Batch("Pilot");
    public static void Final()
    {
        try
        {
            BeetleCombatUpgrade.Apply();
            VerifyRules(AssetDatabase.LoadAssetAtPath<BeetleMatchSettings>("Assets/BeetleArena/Generated/MatchSettings.asset"));
            Run("After");
            BeetleCombatRefinementValidation.Run();
            BeetleContactEscapeValidation.Run();
            BeetleCombatRefinementValidation.CaptureReview();
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    public static void VerifyRules(BeetleMatchSettings settings)
    {
        var copy = UnityEngine.Object.Instantiate(settings);
        try
        {
            copy.countdownSeconds = 0;
            var sim = new BeetleSimulation(copy, 411);
            var reference = new BeetleSimulation(copy, 411);
            for (int i = 0; i < 31; i++) sim.Fighters[0].DecisionRandom.NextDouble();
            if (sim.Fighters[1].DecisionRandom.NextDouble() != reference.Fighters[1].DecisionRandom.NextDouble())
                throw new Exception("An opponent decision consumed this animal's random stream");
            var a = sim.Fighters[0]; var b = sim.Fighters[1];
            a.BoutDuration = .06f; b.BoutDuration = 2;
            a.ActionDuration = b.ActionDuration = 5;
            for (int i = 0; i < 10; i++) sim.Step(.02f);
            if (a.CombatPhase != ExchangePhase.Clash || b.CombatPhase != ExchangePhase.Probing ||
                a.Action != BeetleAction.Assess || b.Action != BeetleAction.Assess)
                throw new Exception("Individual phase transition changed both brains or cancelled their actions");
            a.Intent = b.Intent = BeetleIntent.Pressure;
            a.TouchingOpponent = b.TouchingOpponent = true; a.Contacts = b.Contacts = 3;
            a.Position = new Vector2(-1, 0); b.Position = new Vector2(1, 0);
            a.Traits.liftPreference = .95f; b.Traits.liftPreference = .05f;
            var left = BeetleTactics.Choose(a, new BeetleTacticalSnapshot(a,b,5.2f,4.5f), ExchangePhase.Clash, 4.5f, .4f);
            var right = BeetleTactics.Choose(b, new BeetleTacticalSnapshot(b,a,5.2f,4.5f), ExchangePhase.Clash, 4.5f, .4f);
            if (left != BeetleAction.PryWindup || right != BeetleAction.ShoveWindup)
                throw new Exception("Individual preferences did not change the same tactical decision");
            a.Action = b.Action = BeetleAction.Recover;
            a.Stamina = a.StaminaLimit * .29f; b.Stamina = b.StaminaLimit * .22f;
            a.BurstReserve = b.BurstReserve = .25f;
            var av = new BeetleTacticalSnapshot(a,b,5.2f,4.5f);
            var bv = new BeetleTacticalSnapshot(b,a,5.2f,4.5f);
            if (!av.CanPressRecovery || bv.CanPressRecovery || BeetleTactics.Evaluate(a,av) != BeetleIntent.Counter)
                throw new Exception("Mutual recovery prevents the stronger animal from exploiting the opening");
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/Rules.txt", "PASS: independent decision streams; independent phases preserve actions; style changes choices; mutual recovery allows the stronger animal to press.\n");
        }
        finally { UnityEngine.Object.DestroyImmediate(copy); }
    }
    static void Batch(string tag)
    {
        try { if (tag != "Before") BeetleCombatUpgrade.Apply(); Run(tag); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    static void Run(string tag)
    {
        Directory.CreateDirectory(Folder);
        using var fixture = new BeetleValidationScene("IndividualityValidation");
        var m = fixture.Match;
        var report = new StringBuilder();
        int[] arenas = { 0, 0, 1, 3 }, seeds = { 7319, 1041, 7319, 791 };
        for (int test = 0; test < (tag == "Pilot" ? 1 : arenas.Length); test++)
        {
            m.SelectArena(arenas[test]); m.StartMatch(seeds[test]);
            var trace = new StringBuilder("time,fighter,from,to,blocked,stamina,burst,distance\n");
            var seconds = new[] { new Dictionary<string, float>(), new Dictionary<string, float>() };
            var contactSeconds = new[] { new Dictionary<string, float>(), new Dictionary<string, float>() };
            var last = new string[2];
            float time = 0, same = 0, retreat = 0, attack = 0, stalled = 0, longest = 0, overlap = 0;
            int escape = 0, reversals = 0;
            int[] signs = new int[2]; float[] reversedAt = { -10, -10 };
            Vector2 a = m.Simulation.Fighters[0].Position, b = m.Simulation.Fighters[1].Position;
            for (int tick = 0; tick < (tag == "Pilot" ? 1650 : 4650) && m.Simulation.Phase != MatchPhase.Finished; tick++)
            {
                m.AdvanceFixedStep(); m.Present(1, m.Simulation.MotionTime, .02f);
                var s = m.Simulation; var f = s.Fighters;
                if (s.Phase != MatchPhase.Fighting) continue;
                time += .02f;
                string[] names = new string[2];
                for (int i = 0; i < 2; i++)
                {
                    names[i] = f[i].Action == BeetleAction.Module ? f[i].Motion.Active?.GetType().Name ?? "Module" : f[i].Action.ToString();
                    if (!seconds[i].ContainsKey(names[i])) seconds[i][names[i]] = 0;
                    seconds[i][names[i]] += .02f;
                    if (m.Contacts.HasMotionContact(i, BeetleMotionContact.Horn, 0))
                    {
                        if (!contactSeconds[i].ContainsKey(names[i])) contactSeconds[i][names[i]] = 0;
                        contactSeconds[i][names[i]] += .02f;
                    }
                    if (last[i] != names[i])
                    {
                        trace.AppendLine($"{s.Elapsed:F2},{i},{last[i]},{names[i]},{f[i].ContactBlockedSeconds:F2},{f[i].Stamina:F1},{f[i].BurstReserve:F2},{Vector2.Distance(f[0].Position, f[1].Position):F2}");
                        if (f[i].Action == BeetleAction.Disengage && f[i].ContactBlockedSeconds > .6f) escape++;
                        last[i] = names[i];
                    }
                    float radialSpeed = Vector2.Dot(f[i].Velocity, (f[1-i].Position-f[i].Position).normalized);
                    int sign = radialSpeed > .13f ? 1 : radialSpeed < -.13f ? -1 : 0;
                    if (sign != 0 && sign != signs[i])
                    {
                        if (signs[i] != 0 && s.Elapsed - reversedAt[i] < 1.5f) reversals++;
                        signs[i] = sign; reversedAt[i] = s.Elapsed;
                    }
                }
                if (names[0] == names[1]) same += .02f;
                if (f[0].Action == BeetleAction.Disengage && f[1].Action == BeetleAction.Disengage) retreat += .02f;
                if (f[0].Committed || f[1].Committed) attack += .02f;
                if (Vector2.Distance(a, f[0].Position) + Vector2.Distance(b, f[1].Position) > .10f)
                { a = f[0].Position; b = f[1].Position; stalled = 0; }
                else stalled += .02f;
                longest = Mathf.Max(longest, stalled);
                if (tick % 50 == 0) overlap = Mathf.Max(overlap, m.Contacts.MeasurePenetration());
            }
            report.AppendLine($"arena={arenas[test]} seed={seeds[test]} seconds={time:F2} phase={m.Simulation.Phase} same={same/time:P1} bothRetreat={retreat:F2}s engaged={attack/time:P1} escape={escape} quickReversals={reversals} longestStationary={longest:F2} overlap={overlap:F6}");
            for (int i = 0; i < 2; i++)
            {
                var f = m.Simulation.Fighters[i];
                report.AppendLine($" fighter{i} attacks={f.Shoves+f.Charges+f.Pries+f.Motion.Hits} contacts={f.Contacts} modules={f.Motion.Starts} traits={f.Traits.TotalScore}");
                foreach (var pair in seconds[i]) report.Append($" {pair.Key}={pair.Value:F1}s");
                report.AppendLine();
                report.Append(" hornContact:");
                foreach (var pair in contactSeconds[i]) report.Append($" {pair.Key}={pair.Value:F2}s");
                report.AppendLine();
            }
            File.WriteAllText($"{Folder}/{tag}-{test}.csv", trace.ToString());
            File.WriteAllText($"{Folder}/{tag}.txt", report.ToString());
            if (longest > 6 || overlap > .006f) throw new Exception("Physical regression during individuality validation");
        }
    }
}
