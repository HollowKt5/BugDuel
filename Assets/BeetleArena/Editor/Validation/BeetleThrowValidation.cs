using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>撬投回归：真实角部入角、受击者飞行、连续性、有效接触积分及显示独立性。</summary>
public static class BeetleThrowValidation
{
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    [MenuItem("Tools/Beetle Arena/Validate Lever Throws")]
    public static void Run() => RunCases(true);
    public static void RunCases(bool assertResults)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before validation.");
        using var fixture = new BeetleValidationScene("LeverThrowValidation");
        var report = new StringBuilder("LEVER THROW VALIDATION\n");
        Directory.CreateDirectory("Artifacts/LeverThrows");
        try
        {
            var match = fixture.Match;
            var settings = fixture.Settings;
            CheckMechanics(settings, report);
            float muscleForce = settings.throwMuscleForce;
            int throws = 0;
            var tiers = new System.Collections.Generic.HashSet<int>();
            // 爆发储备加入后，重标定受控输入以覆盖六档；不改变游戏阈值或放松结果断言。
            float[] forceScales = { .30f, .60f, 1f, 1.65f, .55f, 1f };
            for (int arena = 0; arena < 2; arena++)
                for (int variant = 0; variant < 6; variant++)
                {
                    settings.tossChance = variant >= 4 ? 1 : 0;
                    // 弱撬保留足够的入角能力，降低头部输出力；否则只会验证“根本没撬起来”。
                    settings.throwMuscleForce = muscleForce * forceScales[variant];
                    match.SelectArena(arena); match.StartMatch(71);
                    var sim = match.Simulation;
                    while (sim.Phase == MatchPhase.Countdown) sim.Step(.02f);
                    var a = sim.Fighters[0]; var b = sim.Fighters[1];
                    a.Traits.strength = variant >= 4 ? 2.1f : 1;
                    a.Position = new Vector2(-2.05f, 0); b.Position = new Vector2(2.05f, 0); a.Heading = 90; b.Heading = -90;
                    a.Action = BeetleAction.PryWindup; a.ActionTime = 0; a.ActionDuration = .55f;
                    b.Action = BeetleAction.Brace; b.ActionDuration = 10; b.Balance = .3f; b.Stamina = 35;
                    sim.SetGround(match.ActiveSurface);
                    for (int i = 0; i < 2; i++) match.actors[i].Bind(sim.Fighters[i], settings, match.ActiveSurface);
                    match.Contacts.Solve(sim, 0);
                    int launchTick = -1, supportedTicks = 0;
                    float peak = 0, overlap = 0, maxStep = 0, worstBudgetExcess = 0, minRelative = 20;
                    var csv = new StringBuilder("t,action,support,x,y,work,impulse,contact,sweep,omega,anchor,acceptance,vx,vy\n");
                    for (int tick = 0; tick < 300; tick++)
                    {
                        if (launchTick >= 0)
                        {
                            if (a.Action != BeetleAction.Toss && a.Action != BeetleAction.ReleaseLift)
                            { a.Action = BeetleAction.Brace; a.ActionTime = 0; a.ActionDuration = 10; }
                            if (!b.Airborne && !b.HasFallen) { b.Action = BeetleAction.Brace; b.ActionDuration = 10; b.ActionTime = 0; }
                        }
                        Vector3 old = new Vector3(b.Position.x, b.Height, b.Position.y);
                        foreach (var actor in match.actors) actor.BeforeStep();
                        sim.Step(.02f);
                        if (b.Lift.Supported) supportedTicks++;
                        if (a.Throws > 0 && launchTick < 0)
                        {
                            launchTick = tick; throws++;
                            tiers.Add(BeetleThrowMechanics.TierNumber(b.Lift.ThrowKind));
                            Require(b.VerticalVelocity > 0, "Successful lift gently lowered instead of thrown");
                            Require(!b.HasFallen && sim.Phase == MatchPhase.Fighting, "Launch awarded premature victory");
                        }
                        float step = Vector3.Distance(old, new Vector3(b.Position.x, b.Height, b.Position.y));
                        float budget = match.Contacts.MotionBudget(1);
                        maxStep = Mathf.Max(maxStep, step);
                        if (!b.HasFallen) worstBudgetExcess = Mathf.Max(worstBudgetExcess, step - budget);
                        peak = Mathf.Max(peak, b.Height - b.SurfaceHeight);
                        // 相对于出手时攻击者前向；不能用随后自动追踪目标而旋转的 Forward。
                        if (launchTick >= 0) minRelative = Mathf.Min(minRelative, Vector2.Dot(b.Position - a.Position, Vector2.right));
                        float work = b.Lift.Work, contact = b.Lift.ContactSeconds;
                        foreach (float alpha in new[] { .25f, .5f, .75f })
                        {
                            foreach (var actor in match.actors) actor.Render(alpha, sim.Elapsed, .02f / 3);
                            match.Contacts.ProjectView(sim, .02f / 3);
                            overlap = Mathf.Max(overlap, match.Contacts.MeasurePenetration());
                        }
                        Require(b.Lift.Work == work && b.Lift.ContactSeconds == contact, "Render modified throw integration");
                        csv.AppendLine($"{tick*.02f:F3},{b.Action},{b.Lift.Supported},{b.Position.x:F4},{b.Height:F4},{b.Lift.Work:F4},{b.Lift.Impulse:F4},{b.Lift.ContactSeconds:F3},{b.Lift.HeadSweep:F2},{b.Lift.AngularSpeed:F2},{match.Contacts.LiftAnchorError:F3},{match.Contacts.AcceptedMotionFraction:F4},{b.Velocity.x:F3},{b.VerticalVelocity:F3}");
                        if (launchTick >= 0 && new[] { 0, 10, 25, 40 }.Contains(tick - launchTick)) Capture(match, $"Throw-{arena}-{variant}-{tick-launchTick}.png");
                    }
                    report.AppendLine($"arena={arena}, strength={a.Traits.strength:F2}, support={supportedTicks*.02f:F2}s, throws={a.Throws}, kind={b.Lift.ThrowKind}, velocity={b.Lift.LaunchVelocity}, elevation={b.Lift.LaunchElevation:F1}, work={b.Lift.Work:F2}, impulse={b.Lift.Impulse:F2}, contact={b.Lift.ContactSeconds:F2}s, sweep={b.Lift.HeadSweep:F1}, peak={peak:F2}, behind={minRelative:F2}, step={maxStep:F3}, excess={worstBudgetExcess:F5}, overlap={overlap:F6}");
                    File.WriteAllText($"Artifacts/LeverThrows/Trajectory-{arena}-{variant}.csv", csv.ToString());
                    if (assertResults) Require(overlap <= .003f && worstBudgetExcess < .0002f, "Throw contact/continuity failed\n" + report);
                    if (assertResults && arena == 0) Require(a.Throws > 0, "Controlled flat lift did not throw\n" + report);
                    if (assertResults && arena == 0) Require(BeetleThrowMechanics.TierNumber(b.Lift.ThrowKind) == variant + 1,
                        "Unexpected throw tier for controlled fixture\n" + report);
                    if (assertResults && arena == 0 && variant == 5) Require(minRelative < -.5f, "Strong throw did not pass behind attacker\n" + report);
                }
            if (assertResults) Require(throws >= 6 && tiers.Count == 6, "Missing one of six throw tiers\n" + report);
            report.AppendLine(assertResults ? "ALL LEVER THROW CHECKS PASSED" : "DIAGNOSTIC RUN COMPLETE (assertions not enforced for trajectories)");
        }
        finally
        {
            File.WriteAllText("Artifacts/LeverThrows/Validation.txt", report.ToString());

        }
    }
    static void CheckMechanics(BeetleMatchSettings settings, StringBuilder report)
    {
        // 验证不等距分界的两侧及精确边界；方向门槛不能被单纯的大速度绕过。
        float[] cuts = { settings.throwQuickSpeed, settings.throwArcSpeed, settings.throwHighArcSpeed };
        for (int i = 0; i < cuts.Length; i++)
        {
            Require(BeetleThrowMechanics.TierNumber(BeetleThrowMechanics.Classify(settings, cuts[i] - .001f, false)) == i + 1, "Lower tier boundary");
            Require(BeetleThrowMechanics.TierNumber(BeetleThrowMechanics.Classify(settings, cuts[i], false)) == i + 2, "Upper tier boundary");
        }
        Require(BeetleThrowMechanics.TierNumber(BeetleThrowMechanics.Classify(settings, settings.throwMaximumSpeed, false)) == 4, "Forward throw classified as overhead");
        Require(BeetleThrowMechanics.TierNumber(BeetleThrowMechanics.Classify(settings, settings.throwPowerOverheadSpeed - .001f, true)) == 5, "Tier 5 boundary");
        Require(BeetleThrowMechanics.TierNumber(BeetleThrowMechanics.Classify(settings, settings.throwPowerOverheadSpeed, true)) == 6, "Tier 6 boundary");
        report.AppendLine("PASS: all six tier boundaries and direction gates.");
        float Sample(float strength, float mass, float duration, bool moving, float reserve = 1)
        {
            var sim = new BeetleSimulation(settings, 71); var a = sim.Fighters[0]; var b = sim.Fighters[1];
            a.Traits.strength = strength; a.BurstReserve = reserve; b.Traits.mass = mass; b.Lift.Supporter = 0;
            b.Lift.Effort = 1; b.Lift.GripReleased = true; b.Lift.Direction = Vector2.right;
            for (int i = 0; i < Mathf.RoundToInt(duration / .02f); i++)
                BeetleThrowMechanics.Accumulate(settings, a, b, .02f, moving ? .04f : 0, 1,
                    new Vector3(.5f, 1, 0), 1);
            if (!moving) { Require(b.Lift.Work == 0 && b.Lift.Impulse == 0, "Static contact created energy"); return 0; }
            return BeetleThrowMechanics.Release(settings, b).magnitude;
        }
        float normal = Sample(1, 1, .5f, true), strong = Sample(1.7f, 1, .5f, true);
        float heavy = Sample(1, 1.5f, .5f, true), shortContact = Sample(1, 1, .2f, true);
        Sample(1, 1, 2, false);
        float depleted = Sample(1, 1, .5f, true, .15f);
        Require(depleted < normal, "Depleted burst reserve did not reduce throw speed");
        report.AppendLine($"PASS: burst reserve lowers throw speed, fresh={normal:F3}, depleted={depleted:F3}.");
        Require(strong > normal && heavy < normal && shortContact < normal, "Force/mass/contact monotonicity failed");
        report.AppendLine($"PASS mechanics: normal={normal:F3}, stronger={strong:F3}, heavier={heavy:F3}, shorter={shortContact:F3}; stationary contact adds no work/impulse.");
    }
    static void Capture(BeetleMatchController match, string filename)
    {
        typeof(BeetleArenaCollisionValidation).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { match, filename });
        File.Copy("Artifacts/BeetleArena/" + filename, "Artifacts/LeverThrows/" + filename, true);
    }
}
