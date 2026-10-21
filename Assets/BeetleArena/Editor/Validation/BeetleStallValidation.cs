using System;
using System.IO;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEngine;

/// <summary>正常 AI 对局中的双虫停滞检查；不强制动作、不移动根节点、不跳过碰撞。</summary>
public static class BeetleStallValidation
{
    [MenuItem("Tools/Beetle Arena/Validate Combat Stalls")]
    public static void Run()
    {
        Directory.CreateDirectory("Artifacts/DeadlockFix");
        var report = new StringBuilder("NATURAL AI STALL CHECKS (fixed seeds, not an exhaustive guarantee)\n");
        try
        {
            // 原始互锁现场还要验证 0/1/4 次显示插值不会影响脱困。
            BeetleContactEscapeValidation.Run();
            using var fixture = new BeetleValidationScene("CombatStallValidation");
            var match = fixture.Match;
            for (int arena = 0; arena < match.arenas.Length; arena++)
            {
                foreach (int seed in new[] { 71, 7319 })
                    CheckRound(match, arena, seed, report);
            }
            report.AppendLine("ALL STALL CHECKS PASSED");
        }
        catch (Exception exception)
        {
            report.AppendLine(exception.ToString());
            throw;
        }
        finally
        {
            File.WriteAllText("Artifacts/DeadlockFix/NaturalRounds.txt", report.ToString());
        }
    }

    static void CheckRound(BeetleMatchController match, int arena, int seed, StringBuilder report)
    {
        match.SelectArena(arena);
        match.StartMatch(seed);
        var sim = match.Simulation;
        Vector2 anchorA = sim.Fighters[0].Position;
        Vector2 anchorB = sim.Fighters[1].Position;
        float noProgress = 0, longestNoProgress = 0, blocked = 0, longestBlocked = 0;
        float penetration = 0, budgetExcess = 0;
        int pushStarts = 0, falsePushStarts = 0;
        bool wasPushingA = false, wasPushingB = false;
        for (int tick = 0; tick < 9300 && sim.Phase != MatchPhase.Finished; tick++)
        {
            var a = sim.Fighters[0];
            var b = sim.Fighters[1];
            Vector3 beforeA = new Vector3(a.Position.x, a.Height, a.Position.y);
            Vector3 beforeB = new Vector3(b.Position.x, b.Height, b.Position.y);
            match.AdvanceFixedStep();
            bool pushingA = a.Motion.Active is ForelegPushMotion;
            bool pushingB = b.Motion.Active is ForelegPushMotion;
            if (pushingA && !wasPushingA)
            {
                pushStarts++;
                if (!a.Motion.Context.OpponentContactConfirmed)
                    falsePushStarts++;
            }
            if (pushingB && !wasPushingB)
            {
                pushStarts++;
                if (!b.Motion.Context.OpponentContactConfirmed)
                    falsePushStarts++;
            }
            wasPushingA = pushingA;
            wasPushingB = pushingB;

            bool bothStanding = sim.Phase == MatchPhase.Fighting && !a.Airborne && !b.Airborne &&
                !a.HasFallen && !b.HasFallen && !a.IsOverturned && !b.IsOverturned;
            bool progressed = Vector2.Distance(anchorA, a.Position) >= .1f ||
                Vector2.Distance(anchorB, b.Position) >= .1f;
            if (!bothStanding || progressed)
            {
                anchorA = a.Position;
                anchorB = b.Position;
                noProgress = 0;
            }
            else
                noProgress += BeetleSimulation.FixedStep;
            blocked = bothStanding && match.Contacts.AcceptedMotionFraction < .05f
                ? blocked + BeetleSimulation.FixedStep : 0;
            longestNoProgress = Mathf.Max(longestNoProgress, noProgress);
            longestBlocked = Mathf.Max(longestBlocked, blocked);
            penetration = Mathf.Max(penetration, match.Contacts.MeasurePenetration());
            budgetExcess = Mathf.Max(budgetExcess,
                Vector3.Distance(beforeA, new Vector3(a.Position.x, a.Height, a.Position.y)) - match.Contacts.MotionBudget(0),
                Vector3.Distance(beforeB, new Vector3(b.Position.x, b.Height, b.Position.y)) - match.Contacts.MotionBudget(1));
            if (noProgress > 5 || blocked > 2)
                throw new Exception($"Stall: arena={arena}, seed={seed}, t={sim.Elapsed:F2}, " +
                    $"noProgress={noProgress:F2}, blocked={blocked:F2}, actions={a.Action}/{b.Action}, " +
                    $"accepted={match.Contacts.AcceptedMotionFraction:F4}, pair={match.Contacts.LastSeparationPair}");
        }
        report.AppendLine($"arena={arena}, seed={seed}, duration={sim.Elapsed:F2}, result={sim.ResultReason}, " +
            $"longestNoProgress={longestNoProgress:F2}s, longestBlocked={longestBlocked:F2}s, " +
            $"overlap={penetration:F6}, budgetExcess={budgetExcess:F6}, retreats={match.Contacts.ContactRetreatSteps}, " +
            $"pushStarts={pushStarts}, falsePushStarts={falsePushStarts}, " +
            $"forelegHits={sim.Fighters[0].ForelegHits + sim.Fighters[1].ForelegHits}");
        File.WriteAllText("Artifacts/DeadlockFix/NaturalRounds.txt", report.ToString());
        if (sim.Phase != MatchPhase.Finished || falsePushStarts != 0 || penetration > .006f || budgetExcess > .0003f)
            throw new Exception("Round did not finish safely, or a push started without contact.\n" + report);
    }
}
