using System;
using System.IO;
using System.Linq;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEngine;

/// <summary>Temporal continuity validation: fixed-step boundary crossing, repeated evaluation at identical alpha, and render rate invariance.</summary>
public static class BeetleMotionValidation
{
    [MenuItem("Tools/甲壳虫斗/验证/模型与动作/动作连续性", false, 706)]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop play first.");
        using var fixture = new BeetleValidationScene("MotionValidationScene");
        Directory.CreateDirectory("Artifacts/BeetleArena");
        var report = new StringBuilder("MOTION CONTINUITY VALIDATION\n");
        {
            var match = fixture.Match;
            for (int mode = 0; mode < 2; mode++)
            {
                match.SelectArena(mode); match.StartMatch(7319);
                while (match.Simulation.Phase == MatchPhase.Countdown) match.Simulation.Step(.02f);
                // Observes position of each component rather than root alone, catching issues where joints previously jittered at 50 Hz.
                var joints = match.actors.SelectMany(a => a.animator.model.GetComponentsInChildren<Transform>(true)).ToArray();
                var end = new Vector3[joints.Length]; var middle = new Vector3[joints.Length];
                float boundary = 0, repeat = 0, penetration = 0, legacyJump = 0;
                for (int tick = 0; tick < 900; tick++)
                {
                    foreach (var a in match.actors) a.Render(1, match.Simulation.Elapsed, .02f);
                    for (int i = 0; i < joints.Length; i++) end[i] = joints[i].position;
                    foreach (var a in match.actors) a.BeforeStep();
                    match.Simulation.Step(.02f);
                    foreach (var a in match.actors) a.Render(0, match.Simulation.Elapsed, .02f);
                    for (int i = 0; i < joints.Length; i++) boundary = Mathf.Max(boundary, Vector3.Distance(end[i], joints[i].position));
                    // Simulates legacy bug where root lagged on previous step while joints advanced to new step; quantifies timing fix.
                    foreach (var a in match.actors)
                    {
                        var p = a.transform.position; var r = a.transform.rotation;
                        a.Render(1, match.Simulation.Elapsed, .02f);
                        a.transform.SetPositionAndRotation(p, r);
                    }
                    for (int i = 0; i < joints.Length; i++) legacyJump = Mathf.Max(legacyJump, Vector3.Distance(end[i], joints[i].position));
                    foreach (var a in match.actors) a.Render(.5f, match.Simulation.Elapsed, .02f);
                    for (int i = 0; i < joints.Length; i++) middle[i] = joints[i].position;
                    foreach (var a in match.actors) a.Render(.5f, match.Simulation.Elapsed, .02f);
                    for (int i = 0; i < joints.Length; i++) repeat = Mathf.Max(repeat, Vector3.Distance(middle[i], joints[i].position));
                    match.Contacts.ProjectView(match.Simulation);
                    if (tick % 10 == 0) penetration = Mathf.Max(penetration, match.Contacts.MeasurePenetration());
                }
                report.AppendLine($"Arena {mode}: 900 fixed steps, {joints.Length} transforms. Old root-only boundary jump={legacyJump:F6}; full-pose boundary error={boundary:F6}; repeat error={repeat:F6}; penetration={penetration:F6}.");
                File.WriteAllText("Artifacts/BeetleArena/MotionValidation.txt", report.ToString());
                if (boundary > .0001f || repeat > .0001f || penetration > .006f) throw new Exception(report.ToString());
                // Identical seed and fixed step evaluated at 1x vs 4x render calls per step, confirming rendering never pollutes authoritative positions.
                Vector3[] first = null;
                for (int run = 0; run < 2; run++)
                {
                    match.StartMatch(7319);
                    for (int tick = 0; tick < 500; tick++)
                    {
                        foreach (var a in match.actors) a.BeforeStep();
                        match.Simulation.Step(.02f);
                        for (int frame = 0; frame <= run * 3; frame++)
                        {
                            foreach (var a in match.actors) a.Render((frame + 1f) / (run * 3 + 1), match.Simulation.Elapsed, .005f);
                            match.Contacts.ProjectView(match.Simulation);
                        }
                    }
                    var positions = match.Simulation.Fighters.Select(b => new Vector3(b.Position.x, b.Height, b.Position.y)).ToArray();
                    if (run == 0) first = positions;
                    else
                    {
                        float error = Mathf.Max(Vector3.Distance(first[0], positions[0]), Vector3.Distance(first[1], positions[1]));
                        report.AppendLine($"Arena {mode}: 1 vs 4 render frames per step, simulation position error={error:F8}.");
                        if (error > .0001f) throw new Exception("Render frequency changed simulation\n" + report);
                    }
                }
            }
            report.AppendLine("ALL MOTION CHECKS PASSED");
            File.WriteAllText("Artifacts/BeetleArena/MotionValidation.txt", report.ToString());
        }
    }
}
