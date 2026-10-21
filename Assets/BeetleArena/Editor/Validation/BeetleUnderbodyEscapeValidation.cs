using System;
using System.IO;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEngine;

// 从真实入角生成现场，再模拟撬举未成功时的收招；不把碰撞体塞进对方体内。
public static class BeetleUnderbodyEscapeValidation
{
    [MenuItem("Tools/Beetle Arena/Validate Underbody Escape")]
    public static void Run()
    {
        const string folder = "Artifacts/UnderbodyEscape";
        Directory.CreateDirectory(folder);
        var report = new StringBuilder("UNDERBODY WITHDRAWAL (actual articulated geometry)\n");
        bool passed = true;
        using var fixture = new BeetleValidationScene("UnderbodyEscapeValidation");
        var match = fixture.Match;
        foreach (int arena in new[] { 0, 1 })
            foreach (int attacker in new[] { 0, 1 })
            {
                // This seed's larger flat-arena animal never reaches under the body;
                // the reciprocal flat case and both rocky cases generate real insertion.
                if (arena == 0 && attacker == 0) continue;
                match.SelectArena(arena);
                match.StartMatch(2731);
                var sim = match.Simulation;
                while (sim.Phase == MatchPhase.Countdown) match.AdvanceFixedStep();
                var a = sim.Fighters[attacker];
                var b = sim.Fighters[1 - attacker];
                a.Position = new Vector2(-2.05f, 0); b.Position = new Vector2(2.05f, 0);
                a.Heading = 90; b.Heading = -90;
                a.Action = BeetleAction.PryWindup; a.ActionTime = 0; a.ActionDuration = .55f;
                b.Action = BeetleAction.Brace; b.ActionTime = 0; b.ActionDuration = 10;
                b.Balance = 1; b.Stamina = b.Traits.endurance;
                a.Traits.strength = .55f; b.Traits.grip = 1.35f;
                sim.SetGround(match.ActiveSurface);
                for (int i = 0; i < 2; i++) match.actors[i].Bind(sim.Fighters[i], fixture.Settings, match.ActiveSurface);
                match.Contacts.Solve(sim, 0);
                int captured = -1, cleared = -1;
                bool underbody = false;
                float overlap = 0, budgetExcess = 0, blocked = 0, longestBlocked = 0, underTime = 0;
                var trace = new StringBuilder("tick,time,action,otherAction,underbody,accepted,blocked,release,x,z,headPitch,pair\n");
                for (int tick = 0; tick < 700; tick++)
                {
                    underTime = underbody ? underTime + BeetleSimulation.FixedStep : 0;
                    // 收招发生在建立托举前；相当于这次撬举没达到承重条件。
                    if (captured < 0 && underTime >= .12f && !b.Lift.Supported)
                    {
                        captured = tick;
                        a.Action = BeetleAction.Disengage; a.ActionTime = 0; a.ActionDuration = .55f;
                        a.AttackApplied = false;
                    }
                    Vector3 beforeA = Position(a), beforeB = Position(b);
                    match.AdvanceFixedStep();
                    underbody = Underbody(match, attacker);
                    if (captured >= 0 && !underbody && cleared < 0) cleared = tick;
                    if (captured >= 0)
                    {
                        blocked = match.Contacts.AcceptedMotionFraction < .05f ? blocked + BeetleSimulation.FixedStep : 0;
                        longestBlocked = Mathf.Max(longestBlocked, blocked);
                    }
                    overlap = Mathf.Max(overlap, match.Contacts.MeasurePenetration());
                    budgetExcess = Mathf.Max(budgetExcess, Vector3.Distance(beforeA, Position(a)) - match.Contacts.MotionBudget(attacker),
                        Vector3.Distance(beforeB, Position(b)) - match.Contacts.MotionBudget(1 - attacker));
                    trace.AppendLine($"{tick},{sim.Elapsed:F2},{a.Action},{b.Action},{underbody},{match.Contacts.AcceptedMotionFraction:F4},{a.ContactBlockedSeconds:F3},{a.ContactReleaseRemaining:F3},{a.Position.x:F4},{a.Position.y:F4},{Mathf.DeltaAngle(0, match.actors[attacker].animator.headPivot.localEulerAngles.x):F2},{match.Contacts.LastSeparationPair}");
                    match.Present(.5f, sim.MotionTime, BeetleSimulation.FixedStep);
                    overlap = Mathf.Max(overlap, match.Contacts.MeasurePenetration());
                    if (captured >= 0 && tick - captured >= 150) break;
                }
                float escape = cleared < 0 || captured < 0 ? -1 : (cleared - captured + 1) * BeetleSimulation.FixedStep;
                bool ok = captured >= 0 && escape > 0 && escape < 1 && longestBlocked < .16f && overlap < .003f && budgetExcess < .0003f;
                passed &= ok;
                report.AppendLine($"arena={arena}, attacker={attacker}, captured={captured}, escape={escape:F3}s, longestBlocked={longestBlocked:F3}s, retreats={match.Contacts.ContactRetreatSteps}, overlap={overlap:F6}, budgetExcess={budgetExcess:F6}, pass={ok}");
                File.WriteAllText($"{folder}/Trace-{arena}-{attacker}.csv", trace.ToString());
                File.WriteAllText(folder + "/Report.txt", report.ToString());
            }
        if (!passed) throw new Exception("Underbody escape failed; see " + folder + "/Report.txt");
    }

    static Vector3 Position(BeetleState state) => new Vector3(state.Position.x, state.Height, state.Position.y);

    static bool Underbody(BeetleMatchController match, int attacker)
    {
        Physics.SyncTransforms();
        var source = match.actors[attacker].GetComponent<BeetleContactRig>();
        var target = match.actors[1 - attacker].GetComponent<BeetleContactRig>();
        for (int i = 0; i < source.shapes.Length; i++)
        {
            if (!source.IsHorn(i)) continue;
            var horn = source.shapes[i];
            for (int j = 0; j < target.shapes.Length; j++)
            {
                if (target.Role(j) != BeetleContactRole.Body || target.IsHorn(j)) continue;
                var body = target.shapes[j];
                Vector3 q = body.ClosestPoint(horn.bounds.center);
                Vector3 p = horn.ClosestPoint(q);
                q = body.ClosestPoint(p);
                if (Vector3.Distance(p, q) < .09f && p.y <= q.y + .02f && p.y < body.bounds.center.y) return true;
            }
        }
        return false;
    }
}
