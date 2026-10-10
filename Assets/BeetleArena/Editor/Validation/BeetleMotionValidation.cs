using System;
using System.IO;
using System.Linq;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>时间连续性验证：跨固定步边界、同一插值重复求值、以及不同渲染频率下的模拟隔离。</summary>
public static class BeetleMotionValidation
{
    [MenuItem("Tools/Beetle Arena/Validate Motion Continuity")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop play first.");
        const string path = "Assets/BeetleArena/Generated/MotionValidationScene.unity";
        var previous = SceneManager.GetActiveScene();
        if (!AssetDatabase.CopyAsset(BeetleArenaSceneBuilder.ScenePath, path)) throw new Exception("Copy failed");
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        var report = new StringBuilder("MOTION CONTINUITY VALIDATION\n");
        try
        {
            SceneManager.SetActiveScene(scene);
            var match = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BeetleMatchController>()).Single();
            match.hud.Bind(match);
            for (int mode = 0; mode < 2; mode++)
            {
                match.SelectArena(mode); match.StartMatch(7319);
                while (match.Simulation.Phase == MatchPhase.Countdown) match.Simulation.Step(.02f);
                // 观察每个部件的位置，而非只观察根节点，以便检出原先关节以 50 Hz 跳动的问题。
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
                    // 模拟旧写法：根节点在上一步，关节却已是新一步；用于量化所修复的时序不一致。
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
                // 相同种子/固定步，分别每步渲染一次与四次，检查渲染没有污染权威位置。
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
        finally
        {
            EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(previous); AssetDatabase.DeleteAsset(path);
        }
    }
}
