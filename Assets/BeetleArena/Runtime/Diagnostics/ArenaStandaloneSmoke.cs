using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BeetleArena
{
    // Opt-in standalone integration check. Normal games never execute this path.
    // 独立运行程序的集成检查组件；必须显式传入 --beetle-smoke 才会执行。
    // 普通玩家启动时立即禁用，不自动推进对局、不退出游戏。
    public sealed class ArenaStandaloneSmoke : MonoBehaviour
    {
        // 截图/报告输出目录，可用 --beetle-artifacts 指定；failed 累计日志中的运行错误。
        string destination;
        bool failed;
        // 协程先等待场景初始化，再检查真实帧循环、按钮冷却、暂停、落台、重赛和新配对。
        // 每段 try/catch 把异常记录为失败并退出，供外部脚本读取退出码。
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--beetle-smoke") < 0) { enabled = false; yield break; }
            destination = Path.Combine(Application.persistentDataPath, "SmokeTest");
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--beetle-artifacts") destination = args[i + 1];
            Directory.CreateDirectory(destination);
            Application.logMessageReceived += CaptureError;
            // 让同场景的 MatchController.Start 有机会完成初始化，避免依赖组件 Start 的相对顺序。
            yield return null;
            var match = GetComponent<BeetleMatchController>();
            try
            {
                if (match.Simulation == null) throw new Exception("Match did not initialize");
                if (FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length != 1) throw new Exception("EventSystem count");
                foreach (var actor in match.actors)
                    if (actor.animator.legs.Length != 6) throw new Exception("Six-leg rig missing");
                match.StartMatch(7319);
            }
            catch (Exception ex) { Fail(ex); yield break; }
            // Let the actual frame loop, animation, camera, input system and HUD run together.
            // 前七秒由正常 Update 推进，覆盖真实的动画、镜头和 UI；之后才使用加速测试循环。
            yield return new WaitForSecondsRealtime(7);
            try
            {
                Capture(match, "GamePreview.png");
                match.hud.fighters[0].cheerButton.onClick.Invoke();
                if (match.Simulation.Fighters[0].CheerCooldown <= 0 || match.Cheer(0)) throw new Exception("Cheer wiring / cooldown");
                match.hud.pauseButton.onClick.Invoke(); if (!match.IsPaused) throw new Exception("Pause wiring");
            }
            catch (Exception ex) { Fail(ex); yield break; }
            // 记录暂停瞬间的模拟时间，等待真实时间后应完全不变，以验证暂停有效。
            float pausedTime = match.Simulation.Elapsed;
            yield return new WaitForSecondsRealtime(.3f);
            try
            {
                if (match.Simulation.Elapsed != pausedTime) throw new Exception("Simulation advanced while paused");
                match.hud.pauseButton.onClick.Invoke();
                Advance(match, 65); Capture(match, "CombatPreview.png");
                Advance(match, 290);
                if (match.Simulation.Phase != MatchPhase.Finished || !match.hud.resultPanel.activeSelf) throw new Exception("No runtime ring-out result");
                Capture(match, "ResultPreview.png");
                int seed = match.Simulation.Seed;
                match.hud.rematchButton.onClick.Invoke();
                if (match.Simulation.Phase != MatchPhase.Countdown || match.Simulation.Seed != seed) throw new Exception("Rematch");
                // The first bout above exercises the full real contest. For this
                // reset check, produce another genuine fall without repeating it.
                Advance(match, 3.2f);
                match.Simulation.Fighters[0].Position = new Vector2(match.settings.arenaRadius + 3, 0);
                match.Simulation.Fighters[0].Height = -1;
                Advance(match, 3);
                match.hud.nextButton.onClick.Invoke();
                if (match.Simulation.Phase != MatchPhase.Countdown || match.Simulation.Seed == seed) throw new Exception("New pairing");
                File.WriteAllText(Path.Combine(destination, "RuntimeValidation.txt"), failed ? "FAILED: see RuntimeErrors.txt" :
                    "PASS: actual standalone frame loop, two six-leg rigs, one EventSystem, cheer/cooldown buttons, paused simulation, complete ring-out, result panel, rematch and new pairing. No runtime errors.\n");
                Application.Quit(failed ? 1 : 0);
            }
            catch (Exception ex) { Fail(ex); }
        }
        // 测试专用快速推进：按相同固定步长执行模拟、显示约束和镜头，不等待真实时间。
        // 结束后留最多 80 步让落台对象沉降；此耗时不能作为实际游戏 FPS。
        static void Advance(BeetleMatchController match, float seconds)
        {
            int settling = 0;
            for (int i = 0; i < Mathf.CeilToInt(seconds / .02f); i++)
            {
                foreach (var actor in match.actors) actor.BeforeStep();
                match.Simulation.Step(.02f);
                foreach (var actor in match.actors) actor.Render(1, match.Simulation.Elapsed, .02f);
                match.Contacts.ProjectView(match.Simulation);
                match.spectatorCamera.Tick(match.Simulation, .02f);
                if (match.Simulation.Phase == MatchPhase.Finished && ++settling > 80) break;
            }
            match.hud.Refresh();
        }
        // 把相机画面渲染到临时 RenderTexture 后保存 PNG；finally 恢复渲染目标并释放临时对象。
        void Capture(BeetleMatchController match, string name)
        {
            var camera = match.spectatorCamera.GetComponent<Camera>();
            var target = new RenderTexture(1600, 900, 24);
            var pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target; Canvas.ForceUpdateCanvases(); camera.Render();
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(destination, name), pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                Destroy(pixels); Destroy(target);
            }
        }
        // 收集 Error/Exception/Assert 到报告文件；普通 Log/Warning 不令本检查失败。
        void CaptureError(string text, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            failed = true; File.AppendAllText(Path.Combine(destination, "RuntimeErrors.txt"), text + "\n" + stack + "\n");
        }
        // 记录无法继续的异常并以非零退出码结束独立程序。
        void Fail(Exception ex) { File.WriteAllText(Path.Combine(destination, "RuntimeValidation.txt"), "FAILED: " + ex); Application.Quit(1); }
        // 移除全局日志回调，避免组件销毁后仍被调用。
        void OnDestroy() => Application.logMessageReceived -= CaptureError;
    }
}
