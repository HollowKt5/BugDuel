using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>真实模型中足的可达余量回归；同时经过生产固定步与接触求解，不只检查 IK 公式。</summary>
public static class BeetleLegReachValidation
{
    [MenuItem("Tools/Beetle Arena/Validate Middle Leg Reach")]
    public static void Run()
    {
        using var fixture = new BeetleValidationScene("LegReachValidation");
        var match = fixture.Match;
        var report = new StringBuilder("MIDDLE LEG REACH\n" + DateTimeOffset.Now.ToString("o") + "\n");
        try
        {
            for (int arena = 0; arena < 2; arena++)
            {
                match.SelectArena(arena); match.StartMatch(71);
                int samples = 0, limited = 0, far = 0;
                float error = 0, boneError = 0;
                for (int tick = 0; tick < 1800; tick++)
                {
                    match.AdvanceFixedStep(); match.Present(1, match.Simulation.MotionTime, .02f);
                    foreach (var actor in match.actors)
                    {
                        if (actor.State.Airborne || actor.State.HasFallen) continue;
                        var rig = actor.animator.importedRig;
                        float scale = Mathf.Abs(rig.model.lossyScale.x);
                        foreach (int index in new[] { 1, 4 })
                        {
                            var b = rig.bindings[index];
                            float bend = Vector3.Angle(b.knee.position - b.hip.position, b.ankle.position - b.knee.position);
                            samples++; if (bend < 21) limited++; if (b.targetError > .12f) far++;
                            error = Mathf.Max(error, b.targetError);
                            boneError = Mathf.Max(boneError,
                                Mathf.Abs(Vector3.Distance(b.hip.position, b.knee.position) - b.upperLength * scale),
                                Mathf.Abs(Vector3.Distance(b.knee.position, b.ankle.position) - b.lowerLength * scale));
                        }
                    }
                }
                report.AppendLine($"arena={arena}: samples={samples}, nearLimit={100f * limited / samples:F2}%, errorOver12cm={100f * far / samples:F2}%, maxTargetError={error:F5}, boneLengthError={boneError:F6}");
                if (samples < 100 || limited > samples * .20f || far > samples * .05f || boneError > .0001f)
                    throw new Exception("Middle leg spends too long at extension limit, misses support, or changes bone length.");
            }
            report.AppendLine("ALL MIDDLE LEG CHECKS PASSED");
        }
        finally
        {
            Directory.CreateDirectory("Artifacts/StrategyReview");
            File.WriteAllText("Artifacts/StrategyReview/LegReach.txt", report.ToString());
        }
    }
}
