using System;
using System.IO;
using System.Reflection;
using System.Text;
using BeetleArena;
using BeetleArena.EditorTools;
using UnityEngine;

// Read-only experiments on the captured compound-collider pose in an isolated scene.
public static class BeetleSeedLockDiagnosis
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run(bool yieldJoints = false)
    {
        string folder = File.ReadAllText("Artifacts/SeedSweep/Latest.txt").Trim();
        var sources = Directory.GetFiles(folder, "*-Failure.json");
        if (sources.Length != 1) throw new InvalidOperationException("The latest sweep must have exactly one failure snapshot.");
        var snapshot = BeetleDebugSnapshot.Read(sources[0]);
        string output = folder + "/Diagnosis";
        Directory.CreateDirectory(output);
        using var fixture = new BeetleValidationScene("SeedLockDiagnosis");
        var match = fixture.Match;
        match.SelectArena((int)snapshot["arena"]); match.StartMatch((int)snapshot["seed"]);
        BeetleDebugSnapshot.Restore(match, snapshot);
        match.EditorSetPaused(false);
        var sim = match.Simulation;
        foreach (var actor in match.actors) actor.BeforeStep();
        var solver = match.Contacts;
        var reset = new[] { new BeetleContactMotion(match.actors[0]), new BeetleContactMotion(match.actors[1]) };
        foreach (var m in reset) m.Begin();
        var cache = typeof(BeetleContactSolver).GetMethod("Cache", Private);
        var constrain = typeof(BeetleContactSolver).GetMethod("Constrain", Private);
        var yieldMethod = typeof(BeetleContactSolver).GetMethod("YieldArticulatedContacts", Private);
        var accepted = typeof(BeetleContactSolver).GetMethod("MotionAccepted", Private);
        var log = new StringBuilder("actor,pitchDelta,direction,distance,rawOverlap,finalOverlap,rootA,rootB,accepted,progress,worst,pair\n");
        for (int actor = 0; actor < 2; actor++)
        foreach (float pitch in new[] { 0f, -4f, 4f, 8f })
        foreach (float distance in new[] { .025f, .01f, .004f })
        for (int direction = 0; direction < 8; direction++)
        {
            foreach (var m in reset) m.Restore();
            var current = match.actors[actor];
            Vector3 movement = Quaternion.AngleAxis(direction * 45, Vector3.up) * -current.transform.forward;
            movement.y = 0; movement = movement.normalized * distance;
            var origin = current.transform.position;
            if (!sim.SampleGround(new Vector2(origin.x, origin.z), out var from) ||
                !sim.SampleGround(new Vector2(origin.x + movement.x, origin.z + movement.z), out var to))
                continue;
            movement.y = to.height - from.height;
            current.transform.position += movement;
            current.animator.headPivot.localRotation *= Quaternion.Euler(pitch, 0, 0);
            float raw = solver.MeasurePenetration();
            cache.Invoke(solver, null);
            if (yieldJoints) yieldMethod.Invoke(solver, new object[] { sim });
            constrain.Invoke(solver, new object[] { sim, true, true });
            float remaining = solver.MeasurePenetration();
            float da = Vector3.Distance(reset[0].Start, match.actors[0].transform.position);
            float db = Vector3.Distance(reset[1].Start, match.actors[1].transform.position);
            float progress = Vector3.Dot(current.transform.position - origin, movement.normalized);
            log.AppendLine($"{actor},{pitch},{direction},{distance:F3},{raw:F6},{remaining:F6},{da:F5},{db:F5},{accepted.Invoke(solver, null)},{progress:F5},{solver.LastWorstContact},{solver.LastSeparationPair}");
        }
        File.WriteAllText(output + (yieldJoints ? "/TrialsYield.csv" : "/Trials.csv"), log.ToString());
    }
}
