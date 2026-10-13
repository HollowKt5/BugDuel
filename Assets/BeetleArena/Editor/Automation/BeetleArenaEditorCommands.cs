using System;
using System.IO;
using System.Linq;
using BeetleArena;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

// Small project-local automation entry point. Commands use Unity APIs, never scene YAML edits.
[InitializeOnLoad]
// 工程内的轻量编辑器自动化桥接：轮询 Temp 下的单个 JSON 请求，调用 Unity 编辑器 API。
// 只在 Editor 编译；命令文件不是队列，调用者应等当前 id 的响应后再发下一条。
public static class BeetleArenaEditorCommands
{
    // id 用于匹配响应，action 选择固定命令，seconds 仅供快速推进使用；没有执行任意代码的入口。
    [Serializable] class Request { public string id, action; public float seconds; }
    // 最多约每 0.4 秒轮询一次；编译或资源刷新期间暂不处理请求。
    static double nextPoll;
    // 从 Application.dataPath 的上级取得当前工程根目录，不依赖启动进程的工作目录。
    static string Root => Directory.GetParent(Application.dataPath).FullName;
    // 请求放在 Temp；执行前删除请求文件，避免域重载后重复执行同一动作。
    static string RequestPath => Path.Combine(Root, "Temp/BeetleArenaCommand.json");
    // 响应覆盖写入，首行含 id 和 OK/ERROR。调用方必须核对 id，避免把旧结果误认作本次完成。
    static string ResponsePath => Path.Combine(Root, "Temp/BeetleArenaResponse.txt");
    // InitializeOnLoad 触发静态初始化：注册轮询与错误日志，写出就绪时间戳供外部确认加载。
    static BeetleArenaEditorCommands()
    {
        EditorApplication.update += Poll;
        Application.logMessageReceived += Log;
        Directory.CreateDirectory(Path.Combine(Root, "Artifacts/BeetleArena"));
        File.WriteAllText(Path.Combine(Root, "Temp/BeetleArenaBridgeReady.txt"), DateTime.Now.ToString("O"));
    }
    // 仅收集错误/异常/断言，并附时间与堆栈；普通消息不写入错误文件。
    static void Log(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        File.AppendAllText(Path.Combine(Root, "Artifacts/BeetleArena/EditorErrors.txt"), DateTime.Now.ToString("O") + " " + message + "\n" + stack + "\n");
    }
    // 读取→反序列化→删除请求→执行→写响应；请求文件暂不可读时留待下一轮。
    // 长验证在编辑器主线程执行期间会阻塞本轮更新，不适合当运行时服务使用。
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        nextPoll = EditorApplication.timeSinceStartup + .4;
        if (!File.Exists(RequestPath)) return;
        Request request;
        try { request = JsonUtility.FromJson<Request>(File.ReadAllText(RequestPath)); File.Delete(RequestPath); }
        catch (IOException) { return; }
        try
        {
            string answer = Execute(request);
            File.WriteAllText(ResponsePath, request.id + " OK\n" + answer);
        }
        catch (Exception ex) { File.WriteAllText(ResponsePath, request.id + " ERROR\n" + ex); Debug.LogException(ex); }
    }
    // 命令分发：refresh/prepare 管导入，build/tune 写资源，play/stop/open 切场景或播放状态，
    // inspect/capture 观察，advance/smoke/validate 等进行测试；build 指生成场景，不是打包 exe。
    static string Execute(Request request)
    {
        switch (request.action)
        {
            case "refresh": AssetDatabase.Refresh(); return "Refresh requested.";
            // 只更新游戏材质；渲染验证在临时场景副本中进行。
            case "apply-toon": BeetleArenaToonSetup.Apply(); return "Toon materials saved.";
            case "install-terrain": BeetleArenaTerrainSetup.Install(); return "Switchable arenas saved.";
            case "validate-terrain": BeetleArenaTerrainValidation.Run(); return File.ReadAllText(Path.Combine(Root, "Artifacts/BeetleArena/TerrainValidation.txt"));
            case "validate-motion": BeetleMotionValidation.Run(); return File.ReadAllText(Path.Combine(Root, "Artifacts/BeetleArena/MotionValidation.txt"));
            case "switch-arena": Running().SwitchArena(); return Running().ArenaName;
            case "validate-toon": BeetleArenaToonSetup.Validate(); return File.ReadAllText(Path.Combine(Root, "Artifacts/BeetleArena/ToonValidation.txt"));
            // 使用临时副本验证新动作和实际接触，避免直接改动正在编辑的游戏场景实例。
            case "validate-clashes":
                BeetleArenaCollisionValidation.RunInEditor();
                return File.ReadAllText(Path.Combine(Root, "Artifacts/BeetleArena/CollisionValidation.txt"));
            case "prepare":
                if (!TMP_Settings.instance) TMP_PackageResourceImporter.ImportResources(true, false, false);
                return "TMP resource import requested.";
            case "build":
                if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop play before rebuilding.");
                BeetleArenaSceneBuilder.Build(); return "Scene and articulated prefab saved.";
            case "validate": BeetleArenaValidation.Run(); return File.ReadAllText(Path.Combine(Root, "Artifacts/BeetleArena/SimulationValidation.txt"));
            // 旧版快捷调参命令，会直接保存列出的资产数值；调用前应了解其写入范围。
            case "tune":
                var settings = AssetDatabase.LoadAssetAtPath<BeetleMatchSettings>(BeetleArenaSceneBuilder.AssetFolder + "/MatchSettings.asset");
                settings.shoveImpulse = 2.7f; settings.pryImpulse = 4.3f; settings.fatiguePerSecond = .12f;
                settings.arenaRadius = 5.2f; settings.spawnDistance = 2.3f;
                settings.initialSeed = 0;
                EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
                return "Contest force and recovery tuning saved.";
            case "play":
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(BeetleArenaSceneBuilder.ScenePath);
                EditorApplication.isPlaying = true; return "Play requested.";
            case "stop": EditorApplication.isPlaying = false; return "Stop requested.";
            // 检测到任何已加载场景未保存时拒绝切换，避免静默丢失用户编辑。
            case "open":
                for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) return "Existing scene has unsaved edits. Play is configured to start BeetleArena.";
                EditorSceneManager.OpenScene(BeetleArenaSceneBuilder.ScenePath); return "Opened game scene.";
            case "inspect":
                var found = UnityEngine.Object.FindFirstObjectByType<BeetleMatchController>();
                return found && found.Simulation != null ? Describe(found) : "No running match. Playing=" + EditorApplication.isPlaying;
            case "capture": return Capture("GamePreview.png");
            case "advance":
                var controller = Running();
                Advance(controller, request.seconds > 0 ? request.seconds : 10);
                return Describe(controller);
            case "smoke": return Smoke();
            default: throw new ArgumentException("Unknown command: " + request.action);
        }
    }
    // 需要真实 Play 模式的命令共用前置检查；找不到已启动模拟就立即报错。
    static BeetleMatchController Running()
    {
        var controller = UnityEngine.Object.FindFirstObjectByType<BeetleMatchController>();
        if (!EditorApplication.isPlaying || !controller || controller.Simulation == null) throw new InvalidOperationException("Game is not running.");
        return controller;
    }
    // 生成紧凑状态文本，供外部检查阶段、时间、胜者、位置与动作计数。
    static string Describe(BeetleMatchController c) => $"phase={c.Simulation.Phase}; elapsed={c.Simulation.Elapsed:F2}; winner={c.Simulation.Winner}; paused={c.IsPaused}\n" +
        string.Join("\n", c.Simulation.Fighters.Select(f => $"{f.Index}: {f.Action}, position={f.Position}, height={f.Height:F2}, stamina={f.Stamina:F1}, shoves={f.Shoves}, pries={f.Pries}"));
    // 按固定步长在一个编辑器调用内加速推进模拟、显示约束和相机，并在结算后短暂沉降。
    // 直接改变当前运行中的对局，仅用于测试/调试，不是玩家快进功能。
    static void Advance(BeetleMatchController c, float seconds)
    {
        int settling = 0;
        for (int i = 0; i < Mathf.CeilToInt(seconds / .02f); i++)
        {
            c.AdvanceFixedStep();
            c.Present(1, c.Simulation.MotionTime, BeetleSimulation.FixedStep);
            if (c.Simulation.Phase == MatchPhase.Finished && ++settling > 80) break;
        }
        c.hud.Refresh();
    }
    // 读取运行中的相机并保存 1600×900 PNG；finally 恢复原渲染目标、释放临时纹理。
    static string Capture(string name)
    {
        var c = Running();
        var camera = c.spectatorCamera.GetComponent<Camera>();
        var texture = new RenderTexture(1600, 900, 24);
        var pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        string path = Path.Combine(Root, "Artifacts/BeetleArena/" + name);
        try
        {
            camera.targetTexture = texture; Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture.active = texture; pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
            UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(texture);
        }
        return path;
    }
    // 重开固定种子对局，检查事件系统、六足绑定、鼓励冷却、暂停按钮、落台结果及重赛。
    // 会改变当前 Play 对局和输出截图/报告；不是只读健康检查。
    static string Smoke()
    {
        var c = Running();
        c.StartMatch(7319); Advance(c, 3.2f);
        if (UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length != 1) throw new Exception("Expected one EventSystem");
        foreach (var actor in c.actors)
            if (actor.animator.legs.Length != 6 || actor.animator.legs.Any(l => !l.hip || !l.upper || !l.lower || !l.ankle)) throw new Exception("Invalid six-leg rig");
        c.hud.fighters[0].cheerButton.onClick.Invoke();
        if (c.Simulation.Fighters[0].CheerCooldown <= 0) throw new Exception("HUD cheer not wired");
        if (c.Cheer(0)) throw new Exception("Spam accepted");
        c.hud.pauseButton.onClick.Invoke(); if (!c.IsPaused) throw new Exception("Pause not wired");
        c.hud.pauseButton.onClick.Invoke(); if (c.IsPaused) throw new Exception("Resume not wired");
        Advance(c, 12); Capture("CombatPreview.png");
        Advance(c, 290);
        if (c.Simulation.Phase != MatchPhase.Finished) throw new Exception("Runtime match failed to finish");
        if (!c.hud.resultPanel.activeSelf) throw new Exception("Missing result UI");
        Capture("ResultPreview.png");
        c.hud.rematchButton.onClick.Invoke(); if (c.Simulation.Phase != MatchPhase.Countdown) throw new Exception("Rematch not wired");
        Advance(c, 8); Capture("GamePreview.png");
        string result = "PASS: two six-leg rigs, one input EventSystem, HUD cheer/cooldown, pause/resume, full autonomous ring-out, result panel and rematch.\n" + Describe(c);
        File.WriteAllText(Path.Combine(Root, "Artifacts/BeetleArena/RuntimeValidation.txt"), result);
        return result;
    }
}
