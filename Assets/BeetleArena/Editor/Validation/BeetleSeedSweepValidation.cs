using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using BeetleArena;
using BeetleArena.EditorTools;
using UnityEditor;
using UnityEngine;

// Complete natural rounds, including airborne/support states and the displayed pose.
// Chunked on Editor.update so diagnostics do not freeze the Editor for an entire sweep.
public static class BeetleSeedSweepValidation
{
    static readonly int[] Seeds = { 1, 2, 3, 4, 5, 17, 23, 42, 101, 2731, 9633, 20261020 };
    static BeetleValidationScene fixture;
    static string folder;
    static StringBuilder report, trace;
    static int caseIndex, lastCase, tick, completed;
    static Vector3 anchorA, anchorB, viewAnchorA, viewAnchorB;
    static float still, viewStill, lagTime, maxStill, maxViewStill, maxLag, overlap, budgetExcess;
    static bool captured;
    static double nextStatus;
    static double stepWorkMs, maxStepWorkMs;
    public static bool Running => fixture != null;

    public static string Start(int firstCase = 0, int count = 48)
    {
        if (Running || EditorApplication.isPlaying)
            throw new InvalidOperationException("Stop the existing sweep/Play before starting.");
        if (firstCase < 0 || firstCase >= Seeds.Length * 4 || count < 1)
            throw new ArgumentOutOfRangeException(nameof(firstCase));
        folder = "Artifacts/SeedSweep/" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        Directory.CreateDirectory(folder);
        File.WriteAllText("Artifacts/SeedSweep/Latest.txt", folder);
        report = new StringBuilder("NATURAL SEED SWEEP; render=50Hz alpha=.5; no forced actions\n");
        caseIndex = firstCase; lastCase = Math.Min(Seeds.Length * 4, firstCase + count); completed = 0;
        try
        {
            fixture = new BeetleValidationScene("SeedSweepValidation");
            BeginCase();
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            WriteStatus("RUNNING");
        }
        catch { Stop("SETUP FAILED"); throw; }
        return folder;
    }

    static void BeforeReload() => Stop("INTERRUPTED by assembly reload");
    public static void Stop(string reason = "STOPPED")
    {
        EditorApplication.update -= Update;
        AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
        if (fixture == null) return;
        try { WriteStatus(reason); }
        finally
        {
            var owned = fixture;
            fixture = null;
            try { owned.Dispose(); }
            catch (Exception error)
            {
                report.AppendLine("CLEANUP FAILED\n" + error);
                // Running is already false, so Update cannot publish this failure for us.
                File.WriteAllText(folder + "/Status.txt", $"FAILED: CLEANUP\ncompleted={completed}; case={caseIndex}; lastCaseExclusive={lastCase}\n{DateTime.Now:O}\n{error}");
                File.WriteAllText(folder + "/Report.txt", report.ToString());
                throw;
            }
        }
    }

    static BeetleMatchController Match => fixture.Match;
    static Vector3 Position(BeetleState b) => new Vector3(b.Position.x, b.Height, b.Position.y);
    static string CaseName => $"Case-{caseIndex:D2}-Seed-{Seeds[caseIndex / 4]}-Arena-{caseIndex % 4}";

    static void BeginCase()
    {
        Match.SelectArena(caseIndex % 4);
        Match.StartMatch(Seeds[caseIndex / 4]);
        tick = 0; still = viewStill = lagTime = maxStill = maxViewStill = maxLag = overlap = budgetExcess = 0;
        stepWorkMs = maxStepWorkMs = 0;
        captured = false;
        anchorA = Position(Match.Simulation.Fighters[0]); anchorB = Position(Match.Simulation.Fighters[1]);
        viewAnchorA = Match.actors[0].transform.position; viewAnchorB = Match.actors[1].transform.position;
        trace = new StringBuilder("tick,time,phase,actionA,actionB,airA,airB,supportA,supportB,overturnedA,overturnedB,accepted,still,viewStill,lag,blockedA,blockedB,releaseA,releaseB,ax,ay,az,bx,by,bz,viewAx,viewAy,viewAz,viewBx,viewBy,viewBz,pair\n");
    }

    static void Update()
    {
        if (!Running) return;
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Stop("INTERRUPTED by Play"); return; }
            var work = Stopwatch.StartNew();
            while (Running && work.ElapsedMilliseconds < 120) Step();
            if (Running && EditorApplication.timeSinceStartup >= nextStatus)
            {
                nextStatus = EditorApplication.timeSinceStartup + 1;
                WriteStatus("RUNNING");
            }
        }
        catch (Exception error)
        {
            report.AppendLine(error.ToString());
            if (Running) Stop("ERROR");
            UnityEngine.Debug.LogException(error);
        }
    }

    static void Step()
    {
        const float dt = BeetleSimulation.FixedStep;
        var match = Match; var sim = match.Simulation;
        var a = sim.Fighters[0]; var b = sim.Fighters[1];
        Vector3 beforeA = Position(a), beforeB = Position(b);
        long started = Stopwatch.GetTimestamp();
        match.AdvanceFixedStep(); tick++;
        Vector3 pa = Position(a), pb = Position(b);
        budgetExcess = Mathf.Max(budgetExcess, Vector3.Distance(beforeA, pa) - match.Contacts.MotionBudget(0),
            Vector3.Distance(beforeB, pb) - match.Contacts.MotionBudget(1));
        bool active = sim.Phase == MatchPhase.Fighting || sim.Phase == MatchPhase.Resolving;
        // Airborne/Lifted must remain included: a blocked lift can otherwise hide forever.
        if (!active || Vector3.Distance(anchorA, pa) > .05f || Vector3.Distance(anchorB, pb) > .05f)
        { anchorA = pa; anchorB = pb; still = 0; }
        else still += dt;
        maxStill = Mathf.Max(maxStill, still);

        match.Present(.5f, sim.MotionTime, dt);
        double workMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        stepWorkMs += workMs; maxStepWorkMs = Math.Max(maxStepWorkMs, workMs);
        Vector3 va = match.actors[0].transform.position, vb = match.actors[1].transform.position;
        if (!active || Vector3.Distance(viewAnchorA, va) > .05f || Vector3.Distance(viewAnchorB, vb) > .05f)
        { viewAnchorA = va; viewAnchorB = vb; viewStill = 0; }
        else viewStill += dt;
        maxViewStill = Mathf.Max(maxViewStill, viewStill);
        // Subtract ordinary half-step interpolation lag, especially during fast throws.
        float speedA = new Vector3(a.Velocity.x, a.VerticalVelocity, a.Velocity.y).magnitude;
        float speedB = new Vector3(b.Velocity.x, b.VerticalVelocity, b.Velocity.y).magnitude;
        float lag = Mathf.Max(0, Vector3.Distance(pa, va) - speedA * dt,
            Vector3.Distance(pb, vb) - speedB * dt);
        maxLag = Mathf.Max(maxLag, lag);
        lagTime = active && lag > .5f ? lagTime + dt : 0;
        if (tick % 5 == 0 || still > 2 || viewStill > 2 || lagTime > .5f)
        {
            overlap = Mathf.Max(overlap, match.Contacts.MeasurePenetration());
            trace.AppendLine($"{tick},{sim.Elapsed:F3},{sim.Phase},{a.Action},{b.Action},{a.Airborne},{b.Airborne},{a.Lift.Supporter},{b.Lift.Supporter},{a.IsOverturned},{b.IsOverturned},{match.Contacts.AcceptedMotionFraction:F6},{still:F3},{viewStill:F3},{lag:F4},{a.ContactBlockedSeconds:F3},{b.ContactBlockedSeconds:F3},{a.ContactReleaseRemaining:F3},{b.ContactReleaseRemaining:F3},{pa.x:F5},{pa.y:F5},{pa.z:F5},{pb.x:F5},{pb.y:F5},{pb.z:F5},{va.x:F5},{va.y:F5},{va.z:F5},{vb.x:F5},{vb.y:F5},{vb.z:F5},{match.Contacts.LastSeparationPair}");
        }
        if (!captured && (still > 3 || viewStill > 3 || lag > .5f))
        {
            captured = true;
            BeetleDebugSnapshot.Save(match, folder + "/" + CaseName + "-Early.json");
        }
        // Righting adjudication takes 5 seconds. Let that finish before diagnosing a deadlock.
        string failure = active && still > 6 ? "SIMULATION STALL" :
            active && viewStill > 6 ? "DISPLAY STALL" : lagTime > 2 ? "DISPLAY DIVERGED" :
            budgetExcess > .0003f ? "MOTION BUDGET" : overlap > .006f ? "PENETRATION" :
            tick > 9400 ? "ROUND NEVER FINISHED" : null;
        if (failure != null)
        {
            report.AppendLine(Summary(failure));
            File.WriteAllText(folder + "/" + CaseName + ".csv", trace.ToString());
            match.hud.Refresh();
            BeetleDebugSnapshot.Save(match, folder + "/" + CaseName + "-Failure.json");
            BeetleDebugCapture.Save(match, folder + "/" + CaseName + "-Failure.png");
            Stop("FAILED: " + failure);
            return;
        }
        if (sim.Phase != MatchPhase.Finished) return;
        report.AppendLine(Summary("PASS"));
        File.WriteAllText(folder + "/" + CaseName + ".csv", trace.ToString());
        completed++;
        if (caseIndex + 1 >= lastCase) { Stop("PASSED"); return; }
        caseIndex++;
        BeginCase();
    }

    static string Summary(string result)
    {
        var sim = Match.Simulation;
        return $"{CaseName}: {result}; time={sim.Elapsed:F2}; result={sim.ResultReason}; simStill={maxStill:F2}; viewStill={maxViewStill:F2}; excessLag={maxLag:F3}; overlap={overlap:F6}; budgetExcess={budgetExcess:F6}; retreats={Match.Contacts.ContactRetreatSteps}; headRetreats={Match.Contacts.ContactHeadRetreatSteps}; stepMsAvg={stepWorkMs / Math.Max(1, tick):F3}; stepMsMax={maxStepWorkMs:F3}";
    }

    static void WriteStatus(string state)
    {
        File.WriteAllText(folder + "/Report.txt", report.ToString());
        File.WriteAllText(folder + "/Status.txt", $"{state}\ncompleted={completed}; case={caseIndex}; lastCaseExclusive={lastCase}; seed={Seeds[caseIndex / 4]}; arena={caseIndex % 4}; t={Match.Simulation.Elapsed:F2}; simStill={still:F2}; viewStill={viewStill:F2}; lagTime={lagTime:F2}\n{DateTime.Now:O}");
    }
}
