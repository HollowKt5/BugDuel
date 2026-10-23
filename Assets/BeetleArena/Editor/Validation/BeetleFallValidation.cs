using System;
using System.IO;
using System.Linq;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEngine;

/// <summary>真实模型落败反馈：空中连续性、地面碰撞、结束后恢复、显示隔离和双落台规则。</summary>
public static class BeetleFallValidation
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    [MenuItem("Tools/Beetle Arena/Validate Fall Recovery")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before validation.");
        using var fixture = new BeetleValidationScene("FallValidation");
        var report = new StringBuilder("FALL RECOVERY VALIDATION\n" + DateTimeOffset.Now.ToString("o") + "\n");
        Directory.CreateDirectory("Artifacts/FallRecovery");
        try
        {
            var match = fixture.Match;
            var settings = fixture.Settings;
            settings.Validate();
            for (int arena = 0; arena < 2; arena++)
                for (int variant = 0; variant < 4; variant++)
                {
                    var baseline = RunCase(match, arena, variant, false, report);
                    var displayed = RunCase(match, arena, variant, true, report);
                    for (int i = 0; i < baseline.Length; i++)
                        Require(Vector3.Distance(baseline[i], displayed[i]) < .00005f, "Render changed simulation trajectory");
                }
            // 同步双落台仍是平局；恢复动作不能把负方重新加入战斗。
            var sim = new BeetleSimulation(settings, 71);
            while (sim.Phase == MatchPhase.Countdown) sim.Step(.02f);
            foreach (var b in sim.Fighters) { b.HasFallen = true; b.Airborne = true; b.Height = -1; }
            sim.Step(.02f);
            Require(sim.Phase == MatchPhase.Finished && sim.Winner == -1, "Double-fall rule changed");
            for (int i = 0; i < 200; i++) sim.Step(.02f);
            Require(sim.Fighters.All(b => b.HasFallen && b.Fall.OnFloor && Mathf.Abs(b.VerticalVelocity) < .001f), "Data fallback failed to settle");
            report.AppendLine("PASS: double fall, data fallback, render independence. ALL FALL CHECKS PASSED.");
        }
        finally
        {
            File.WriteAllText("Artifacts/FallRecovery/Validation.txt", report.ToString());

        }
    }
    static Vector3[] RunCase(BeetleMatchController match, int arena, int variant, bool render, StringBuilder report)
    {
        match.SelectArena(arena); match.StartMatch(71);
        var sim = match.Simulation;
        while (sim.Phase == MatchPhase.Countdown) sim.Step(.02f);
        var b = sim.Fighters[1]; var actor = match.actors[1]; var anim = actor.animator;
        // 第四例紧贴台边下落，覆盖身体/长角尚未完全离开侧壁的接触。
        b.Position = new Vector2(match.settings.arenaRadius + (variant == 3 ? .4f : 2.7f), 0);
        b.Heading = variant == 1 ? 0 : -90;
        b.Height = variant == 2 ? 1.4f : .2f;
        b.Velocity = new Vector2(variant == 2 ? 4 : 1.5f, variant == 1 ? 1 : 0);
        b.VerticalVelocity = variant == 2 ? 4 : -1;
        b.Airborne = true; b.HasGround = false;
        b.Action = variant == 2 ? BeetleAction.Launched : BeetleAction.Falling;
        b.ActionDuration = 10;
        b.Lift.UnsupportedTime = variant == 2 ? 1.1f : 0;
        for (int j = 0; j < 2; j++) match.actors[j].Bind(sim.Fighters[j], match.settings, match.ActiveSurface);
        match.Contacts.Solve(sim, 0);
        float overlap = 0, maxAngle = 0, maxFoot = 0, excess = 0, airborneLegTravel = 0;
        int airSamples = 0;
        var path = new Vector3[240];
        for (int tick = 0; tick < path.Length; tick++)
        {
            foreach (var beetle in match.actors) beetle.BeforeStep();
            var oldRoot = actor.transform.position;
            var oldRot = anim.bodyPivot.rotation;
            var oldFoot = anim.legs.Select(l => l.lastFoot).ToArray();
            sim.Step(.02f);
            path[tick] = new Vector3(b.Position.x, b.Height, b.Position.y);
            excess = Mathf.Max(excess, Vector3.Distance(oldRoot, path[tick]) - match.Contacts.MotionBudget(1));
            maxAngle = Mathf.Max(maxAngle, Quaternion.Angle(oldRot, anim.bodyPivot.rotation));
            for (int l = 0; l < anim.legs.Length; l++)
            {
                float step = Vector3.Distance(oldFoot[l], anim.legs[l].lastFoot);
                maxFoot = Mathf.Max(maxFoot, step);
                if (b.HasFallen && !b.Fall.OnFloor) airborneLegTravel += (anim.legs[l].lastFoot - oldFoot[l] - (path[tick] - oldRoot)).magnitude;
            }
            if (b.HasFallen && !b.Fall.OnFloor) airSamples++;
            overlap = Mathf.Max(overlap, match.Contacts.MeasurePenetration());
            float clock = b.Fall.Time, groundClock = b.Fall.GroundSeconds;
            if (render)
                foreach (float alpha in new[] { .2f, .6f, .9f })
                {
                    foreach (var beetle in match.actors) beetle.Render(alpha, sim.Elapsed, .02f / 3);
                    match.Contacts.ProjectView(sim, .02f / 3);
                    overlap = Mathf.Max(overlap, match.Contacts.MeasurePenetration());
                }
            Require(clock == b.Fall.Time && groundClock == b.Fall.GroundSeconds, "Render advanced recovery clock");
            if (render && arena == 0 && variant == 2 && (tick == 15 || tick == 40 || tick == 70 || tick == 120 || tick == 220))
                Capture(match, "Fall-" + tick + ".png");
        }
        report.AppendLine($"arena={arena}, variant={variant}, render={render}, winner={sim.Winner}, floor={b.Fall.OnFloor}, floorTime={b.Fall.GroundSeconds:F2}, clock={b.Fall.Time:F2}, airSamples={airSamples}, legTravel={airborneLegTravel:F3}, maxAngle={maxAngle:F3}, footStep={maxFoot:F3}, excess={excess:F6}, overlap={overlap:F6}, upright={Vector3.Angle(anim.bodyPivot.up, Vector3.up):F2}");
        Require(sim.Winner == 0 && b.HasFallen && sim.Phase == MatchPhase.Finished, "Loser/winner changed");
        Require(b.Fall.OnFloor && b.Fall.GroundSeconds > 1.5f && b.Fall.Time > sim.Elapsed, "Recovery froze after match ended");
        Require(airSamples > 0 && airborneLegTravel > .05f, "Loser legs froze in air");
        Require(maxAngle < 16 && maxFoot < .65f && excess < .0003f && overlap < .003f, "Fall continuity or collision failed");
        Require(Vector3.Angle(anim.bodyPivot.up, Vector3.up) < 12, "Failed to brace upright after landing");
        Require(Mathf.Abs(b.VerticalVelocity) < .01f && b.Velocity.magnitude < .05f, "Floor velocity did not settle");
        return path;
    }
    static void Capture(BeetleMatchController match, string filename)
    {
        var camera = match.spectatorCamera.GetComponent<Camera>();
        var position = camera.transform.position; var rotation = camera.transform.rotation;
        float size = camera.orthographicSize;
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active; int mask = camera.cullingMask;
        var objects = match.gameObject.scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToArray();
        var layers = objects.Select(go => go.layer).ToArray();
        var target = new RenderTexture(1280, 720, 24); var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        bool hud = match.hud.gameObject.activeSelf;
        try
        {
            foreach (var go in objects) go.layer = 31;
            camera.cullingMask = 1 << 31; match.hud.gameObject.SetActive(false);
            Vector3 focus = match.actors[1].transform.position + Vector3.up * .65f;
            camera.transform.position = focus + new Vector3(4, 3, 6); camera.transform.LookAt(focus); camera.orthographicSize = 2.5f;
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
            File.WriteAllBytes("Artifacts/FallRecovery/" + filename, pixels.EncodeToPNG());
        }
        finally
        {
            for (int i = 0; i < objects.Length; i++) objects[i].layer = layers[i];
            camera.cullingMask = mask; camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
            camera.transform.SetPositionAndRotation(position, rotation); camera.orthographicSize = size;
            match.hud.gameObject.SetActive(hud); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
        }
    }
}
