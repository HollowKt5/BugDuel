using System;
using System.IO;
using System.Linq;
using System.Text;
using BeetleArena;
using UnityEngine;

// Isolated, reproducible contact-solved demonstrations. Not a perception score.
public static class BeetleReadabilityValidation
{
    const string Folder = "Artifacts/ActionReadability";
    public static void Run()
    {
        Directory.CreateDirectory(Folder + "/Frames");
        var report = new StringBuilder("ACTION READABILITY: actual rig + contact solver, 25 fps, no slow motion.\nForced starts against braced opponent; this is not natural action frequency or a blind recognition test.\n");
        using var fixture = new BeetleValidationScene("ReadabilityValidation");
        var m = fixture.Match;
        var modules = fixture.Settings.motionModules.Where(x => x && x.enabledInMatch && x.Channel == BeetleMotionChannel.Combat).ToArray();
        var camera = m.spectatorCamera.GetComponent<Camera>();
        var rt = new RenderTexture(640, 360, 24);
        var pixels = new Texture2D(640, 360, TextureFormat.RGB24, false);
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        try
        {
            foreach (var module in modules)
            {
                m.SelectArena(0); m.StartMatch(71);
                while (m.Simulation.Phase == MatchPhase.Countdown) m.AdvanceFixedStep();
                var sim = m.Simulation; var a = sim.Fighters[0]; var b = sim.Fighters[1];
                a.Position = new Vector2(-1.55f, 0); b.Position = new Vector2(1.55f, 0);
                a.Heading = 90; b.Heading = -90;
                foreach (var f in sim.Fighters) { f.Motion.Cancel(); f.Action = BeetleAction.Brace; f.ActionTime = 0; f.ActionDuration = 20; f.DecisionTime = 20; }
                sim.SetGround(m.ActiveSurface);
                for (int i = 0; i < 2; i++) m.actors[i].Bind(sim.Fighters[i], m.settings, m.ActiveSurface);
                m.Contacts.Solve(sim, 0); a.Motion.Refresh(sim, a);
                a.Motion.EditorPreviewStart(module); a.Action = BeetleAction.Module; a.ActionDuration = a.Motion.Duration; a.ActionTime = 0;
                m.hud.gameObject.SetActive(false);
                float maxOverlap = 0, maxStep = 0, maxFootHeight = 0;
                Vector2 screenMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity), screenMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                Vector3 last = m.actors[0].animator.importedRig.bindings[a.Motion.ForelegSide < 0 ? 0 : 3].tip.position;
                for (int tick = 0; tick < 80; tick++)
                {
                    // Let only the selected action run; no new AI action after its recovery.
                    if (!a.Motion.HasActive) { a.Action = BeetleAction.Brace; a.ActionTime = 0; a.ActionDuration = 20; a.DecisionTime = 20; }
                    b.Action = BeetleAction.Brace; b.ActionTime = 0; b.ActionDuration = 20; b.DecisionTime = 20;
                    m.AdvanceFixedStep(); m.Present(1, sim.MotionTime, .02f);
                    maxOverlap = Mathf.Max(maxOverlap, m.Contacts.MeasurePenetration());
                    var tip = m.actors[0].animator.importedRig.bindings[a.Motion.ForelegSide < 0 ? 0 : 3].tip.position;
                    maxStep = Mathf.Max(maxStep, Vector3.Distance(last, tip)); last = tip;
                    maxFootHeight = Mathf.Max(maxFootHeight, tip.y - a.SurfaceHeight);
                    if (tick % 2 != 0) continue;
                    for (int view = 0; view < 2; view++)
                    {
                        Vector3 focus = new Vector3(0, .25f, 0);
                        camera.orthographicSize = view == 0 ? m.spectatorCamera.overviewSize - .75f : 3.3f;
                        camera.transform.position = focus + (view == 0 ? m.spectatorCamera.viewOffset : new Vector3(0, 5, -8));
                        camera.transform.LookAt(focus);
                        camera.targetTexture = rt;
                        if (view == 0 && a.Motion.HasActive)
                        {
                            var point = camera.WorldToViewportPoint(tip); var pixel = new Vector2(point.x * 640, point.y * 360);
                            screenMin = Vector2.Min(screenMin, pixel); screenMax = Vector2.Max(screenMax, pixel);
                        }
                        camera.Render(); RenderTexture.active = rt;
                        pixels.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); pixels.Apply();
                        File.WriteAllBytes($"{Folder}/Frames/{module.GetType().Name}-{view}-{tick / 2:D2}.png", pixels.EncodeToPNG());
                    }
                }
                report.AppendLine($"{module.GetType().Name}: duration={a.Motion.Duration:F3}, hits={a.Motion.Hits}, maxOverlap={maxOverlap:F6}, maxTipStep={maxStep:F4}, footHeight={maxFootHeight:F3}, overviewTipBoundsPixels={screenMax-screenMin}");
                File.WriteAllText(Folder + "/Validation.txt", report.ToString());
                if (maxOverlap > .006f) throw new Exception("Preview penetrates: " + module.name);
            }
            report.AppendLine("PASS: seven contact-solved clips, overview and close view. Human readability still requires viewing.");
            File.WriteAllText(Folder + "/Validation.txt", report.ToString());
        }
        finally
        {
            camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
            UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
