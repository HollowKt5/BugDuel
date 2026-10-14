using System;
using System.IO;
using System.Linq;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>双擂台专项验证：台面采样、六足与地形接触、腾空落地、落台、切换和完整自动比赛。</summary>
public static class BeetleArenaTerrainValidation
{
    const string ReportPath = "Artifacts/BeetleArena/TerrainValidation.txt";
    [MenuItem("Tools/Beetle Arena/Validate Switchable Arenas")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop play before terrain validation.");
        const string temporary = "Assets/BeetleArena/Generated/TerrainValidationScene.unity";
        var previous = SceneManager.GetActiveScene();
        if (!AssetDatabase.CopyAsset(BeetleArenaSceneBuilder.ScenePath, temporary)) throw new Exception("Scene copy failed");
        var scene = EditorSceneManager.OpenScene(temporary, OpenSceneMode.Additive);
        var report = new StringBuilder("SWITCHABLE ARENA VALIDATION\n");
        Directory.CreateDirectory("Artifacts/BeetleArena");
        try
        {
            SceneManager.SetActiveScene(scene);
            var match = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BeetleMatchController>(true)).Single();
            match.hud.Bind(match);
            Require(match.arenas.Length == 4 && match.hud.arenaButton && match.hud.arenaText, "Missing arenas or HUD");
            for (int mode = 0; mode < match.arenas.Length; mode++)
            {
                match.SelectArena(mode); match.StartMatch(7319);
                var surface = match.ActiveSurface;
                Require(surface.Sample(new Vector2(-2.3f, 0), out var left) && surface.Sample(new Vector2(2.3f, 0), out _), "Spawn support missing");
                surface.Sample(new Vector2(2.3f, 0), out var right);
                Require(!surface.Sample(new Vector2(8, 0), out _), "Forest floor incorrectly counts as arena support");
                if (mode == 1) Require(Mathf.Abs(left.height - right.height) > .5f, "Uneven arena has no meaningful height variation");
                float residual = 0, minClearance = float.PositiveInfinity, maxClearance = 0, maxTilt = 0;
                // 三处不同坡向站立，使用真实 IK 与场景碰撞，记录足踝高度而非只检查根节点。
                foreach (float z in new[] { -2f, 0f, 2f })
                {
                    match.StartMatch(7319);
                    for (int i = 0; i < 2; i++) match.Simulation.Fighters[i].Position = new Vector2(i == 0 ? -2.1f : 2.1f, z);
                    match.Simulation.SetGround(surface);
                    foreach (var actor in match.actors) actor.Bind(actor.State, match.settings, surface);
                    for (int tick = 0; tick < 35; tick++) match.Contacts.Solve(match.Simulation, .02f);
                    residual = Mathf.Max(residual, match.Contacts.MeasurePenetration());
                    foreach (var actor in match.actors)
                    {
                        maxTilt = Mathf.Max(maxTilt, Vector3.Angle(Vector3.up, actor.transform.up));
                        foreach (var leg in actor.animator.legs)
                        {
                            Vector3 p = leg.ankle.position;
                            if (!surface.Sample(new Vector2(p.x, p.z), out var hit)) continue;
                            minClearance = Mathf.Min(minClearance, p.y - hit.height);
                            maxClearance = Mathf.Max(maxClearance, p.y - hit.height);
                        }
                    }
                }
                report.AppendLine($"Arena {mode}: height difference={Mathf.Abs(left.height-right.height):F3}, body tilt={maxTilt:F1}, ankle clearance={minClearance:F3}..{maxClearance:F3}, penetration={residual:F6}.");
                File.WriteAllText(ReportPath, report.ToString());
                Require(residual < .006f && minClearance > .02f && maxClearance < .40f, "Invalid planted pose\n" + report);
                if (mode == 1) Require(maxTilt > 3, "Body did not follow slope");
                // 同一地表上方挑起再落下，应重新接地；抛到边缘外则应判负。
                match.StartMatch(7319); Fight(match);
                var b = match.Simulation.Fighters[0];
                b.Airborne = true; b.Height += 1.1f; b.VerticalVelocity = 1.8f; b.Action = BeetleAction.Launched;
                for (int i = 0; i < 120 && b.Airborne; i++) Step(match);
                Require(!b.Airborne && !b.HasFallen && b.HasGround, "Failed to land on active surface");
                match.StartMatch(7319); Fight(match);
                b = match.Simulation.Fighters[0]; b.Position = new Vector2(match.settings.arenaRadius + 1.6f, 0);
                b.Airborne = true; b.VerticalVelocity = -.5f;
                for (int i = 0; i < 120 && !b.HasFallen; i++) Step(match);
                Require(b.HasFallen, "Ring-out failed on arena " + mode);
                report.AppendLine($"Arena {mode}: landing and ring-out PASS.");
                // 两场实际自动比赛，采样检查可视模型接触；不设置指定胜者或定时强制结束。
                foreach (int seed in new[] { 7319, 7950 })
                {
                    match.StartMatch(seed); Fight(match);
                    float worst = 0;
                    for (int tick = 0; tick < 18000 && match.Simulation.Phase != MatchPhase.Finished; tick++)
                    {
                        Step(match);
                        if (tick % 100 == 0)
                        {
                            foreach (var actor in match.actors) actor.Render(.5f, match.Simulation.Elapsed, .02f);
                            match.Contacts.ProjectView(match.Simulation);
                            float measured = match.Contacts.MeasurePenetration();
                            if (measured > .003f)
                            {
                                string contact = match.Contacts.LastWorstContact;
                                Capture(match, "TerrainContactResidual.png");
                                match.Contacts.ProjectView(match.Simulation);
                                report.AppendLine($"  Contact residual at {mode}/{seed}/{match.Simulation.Elapsed:F2}s: {measured:F6}, {contact}, extra projection={match.Contacts.MeasurePenetration():F6}");
                            }
                            worst = Mathf.Max(worst, measured);
                        }
                        if (tick % 1000 == 0) File.WriteAllText("Artifacts/BeetleArena/TerrainProgress.txt", $"arena={mode}, seed={seed}, elapsed={match.Simulation.Elapsed:F1}, penetration={worst:F6}");
                        if (seed == 7319 && tick == 200) Capture(match, "Terrain-" + mode + ".png");
                    }
                    report.AppendLine($"Arena {mode}, seed {seed}: {match.Simulation.Phase}, {match.Simulation.Elapsed:F1}s, winner={match.Simulation.Winner}, penetration={worst:F6}.");
                    File.WriteAllText(ReportPath, report.ToString());
                    Require(match.Simulation.Phase == MatchPhase.Finished && worst < .006f, "Match or collision regression\n" + report);
                }
            }
            match.StartMatch(7319); float size = match.Simulation.Fighters[0].Traits.size;
            for (int i = 0; i < 6; i++)
            {
                int before = match.ArenaIndex;
                match.TogglePause(); match.hud.arenaButton.onClick.Invoke();
                Require(match.ArenaIndex != before && !match.IsPaused && match.Simulation.Phase == MatchPhase.Countdown, "Switch/reset wiring failed");
                Require(match.Simulation.Seed == 7319 && match.Simulation.Fighters[0].Traits.size == size, "Switch changed roster");
                Require(match.arenas.Count(a => a.gameObject.activeSelf) == 1, "Multiple arenas active");
            }
            report.AppendLine("PASS: six HUD switches reset pause/round and preserve seed/roster; only one arena active.");
            report.AppendLine("ALL TERRAIN CHECKS PASSED"); File.WriteAllText(ReportPath, report.ToString());
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(previous); AssetDatabase.DeleteAsset(temporary);
        }
    }

    static void Fight(BeetleMatchController match) { while (match.Simulation.Phase == MatchPhase.Countdown) match.Simulation.Step(.02f); }
    static void Step(BeetleMatchController match)
    {
        foreach (var actor in match.actors) actor.BeforeStep();
        match.Simulation.Step(.02f);
    }
    static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }

    // 截图隔离其他打开场景，并暂时关闭其灯光；所有临时全局状态在 finally 恢复。
    static void Capture(BeetleMatchController match, string file)
    {
        var camera = match.spectatorCamera.GetComponent<Camera>();
        var others = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.gameObject.scene != match.gameObject.scene && l.enabled).ToArray();
        var objects = match.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToArray();
        var layers = objects.Select(o => o.layer).ToArray(); int mask = camera.cullingMask;
        var target = new RenderTexture(1600, 900, 24); var pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        bool async = ShaderUtil.allowAsyncCompilation;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            foreach (var light in others) light.enabled = false;
            foreach (var obj in objects) obj.layer = 31;
            camera.cullingMask = 1 << 31;
            foreach (var actor in match.actors) actor.Render(1, match.Simulation.Elapsed, .02f);
            match.Contacts.ProjectView(match.Simulation); match.hud.Refresh();
            camera.targetTexture = target; Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); pixels.Apply();
            File.WriteAllBytes("Artifacts/BeetleArena/" + file, pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = oldTarget; RenderTexture.active = oldActive; camera.cullingMask = mask;
            for (int i = 0; i < objects.Length; i++) objects[i].layer = layers[i];
            foreach (var light in others) if (light) light.enabled = true;
            ShaderUtil.allowAsyncCompilation = async;
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
        }
    }
}
