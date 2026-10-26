using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BeetleArena
{
    // Opt-in standalone integration check. Normal games never execute this path.
    // Integration smoke test component for standalone builds; executes only when --beetle-smoke is explicitly passed.
    // Disables immediately on standard player launch without advancing matches or quitting the game.
    public sealed class ArenaStandaloneSmoke : MonoBehaviour
    {
        // Screenshot/report output directory, configurable via --beetle-artifacts; failed tracks cumulative runtime errors.
        string destination;
        bool failed;
        // Coroutine waits for scene initialization, then validates real frame loops, button cooldowns, pause, ring-out, rematch, and pairings.
        // Each try/catch logs exceptions as failure and exits with appropriate process exit codes for automated harnesses.
        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--beetle-smoke") < 0) { enabled = false; yield break; }
            destination = Path.Combine(Application.persistentDataPath, "SmokeTest");
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--beetle-artifacts") destination = args[i + 1];
            Directory.CreateDirectory(destination);
            Application.logMessageReceived += CaptureError;
            // Yields to allow MatchController.Start in the same scene to complete initialization, decoupling from component execution order.
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
            // First seven seconds advance via normal Update to cover real animation, camera, and UI; fast-forward loop begins thereafter.
            yield return new WaitForSecondsRealtime(7);
            try
            {
                Capture(match, "GamePreview.png");
                match.hud.fighters[0].cheerButton.onClick.Invoke();
                if (match.Simulation.Fighters[0].CheerCooldown <= 0 || match.Cheer(0)) throw new Exception("Cheer wiring / cooldown");
                match.hud.pauseButton.onClick.Invoke(); if (!match.IsPaused) throw new Exception("Pause wiring");
            }
            catch (Exception ex) { Fail(ex); yield break; }
            // Records simulation time at pause; verifies pause effectiveness by ensuring simulation time remains unchanged over real time.
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
        // Test-only fast-forward: executes simulation, display constraints, and camera at identical fixed steps without awaiting real time.
        // Allows up to 80 steps post-match for fallen objects to settle; duration must not be construed as gameplay FPS.
        static void Advance(BeetleMatchController match, float seconds)
        {
            int settling = 0;
            for (int i = 0; i < Mathf.CeilToInt(seconds / .02f); i++)
            {
                match.AdvanceFixedStep();
                match.Present(1, match.Simulation.MotionTime, BeetleSimulation.FixedStep);
                if (match.Simulation.Phase == MatchPhase.Finished && ++settling > 80) break;
            }
            match.hud.Refresh();
        }
        // Renders camera output to temporary RenderTexture and writes PNG; finally restores render target and releases temporary RT.
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
        // Collects Error/Exception/Assert into report file; standard Log/Warning messages do not fail the smoke test.
        void CaptureError(string text, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            failed = true; File.AppendAllText(Path.Combine(destination, "RuntimeErrors.txt"), text + "\n" + stack + "\n");
        }
        // Logs unrecoverable exception and terminates standalone process with non-zero exit code.
        void Fail(Exception ex) { File.WriteAllText(Path.Combine(destination, "RuntimeValidation.txt"), "FAILED: " + ex); Application.Quit(1); }
        // Deregisters global log callback to prevent invocations after component destruction.
        void OnDestroy() => Application.logMessageReceived -= CaptureError;
    }
}
