using System;
using System.IO;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BeetleCameraValidation
{
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    [MenuItem("Tools/Beetle Arena/Validate Camera Follow")]
    public static void Run()
    {
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewPreviewScene();
        var report = new StringBuilder("CAMERA FOLLOW CONTRACTS\n");
        try
        {
            var go = new GameObject("Isolated camera", typeof(Camera), typeof(ArenaCamera));
            SceneManager.MoveGameObjectToScene(go, scene);
            var camera = go.GetComponent<Camera>(); var director = go.GetComponent<ArenaCamera>();
            camera.aspect = 16f / 9;
            director.ResetView();
            var frame = new BeetleCameraFrame(new BeetleCameraSubject(new Vector3(2, 1.3f, 1), 2.1f, false, false, Vector3.right),
                new BeetleCameraSubject(new Vector3(-2, 2.6f, -1), 2.1f, false, false, Vector3.left), false);
            director.CycleView();
            for (int i = 0; i < 500; i++) director.Tick(frame, .02f);
            var point = camera.WorldToViewportPoint(frame.First.Center);
            Require(Mathf.Abs(point.x - .5f) < .002f && Mathf.Abs(point.y - director.followViewportHeight) < .002f && point.z > 0,
                "Followed body must appear at the configured upper-center viewport position.");
            Require(go.transform.position.y > frame.First.Center.y + 15, "Oblique follow is too low.");
            Require(Mathf.Abs(Mathf.Asin(-go.transform.forward.y)*Mathf.Rad2Deg-60)<.01f, "Follow view must be 60 degrees above the ground.");
            RequireBehind(camera, frame.First);
            report.AppendLine("PASS: 60-degree oblique follow, upper-center framing and camera behind the first fighter's heading.");

            var position = go.transform.position; var rotation = go.transform.rotation; float size = camera.orthographicSize;
            director.Tick(frame, 0); director.Tick(frame, float.NaN); director.Tick(frame, -1);
            Require(go.transform.position == position && go.transform.rotation == rotation && camera.orthographicSize == size,
                "Paused or invalid time changed camera pose.");
            director.DefeatFocus = 1; director.Tick(frame, .000001f);
            Require(Vector3.Distance(position, go.transform.position) < .001f, "Entering defeat view causes an instantaneous framing jump.");
            director.DefeatFocus = -1;
            director.CycleView();
            for (int i = 0; i < 500; i++) director.Tick(frame, .02f);
            point = camera.WorldToViewportPoint(frame.Second.Center);
            Require(Mathf.Abs(point.x - .5f) < .002f && Mathf.Abs(point.y - director.followViewportHeight) < .002f,
                "Second fighter follow did not use the second subject.");
            Require(Mathf.Abs(Mathf.Asin(-go.transform.forward.y)*Mathf.Rad2Deg-60)<.01f, "Second fighter follow must use the same 60-degree elevation.");
            RequireBehind(camera, frame.Second);
            position = go.transform.position;
            director.CycleView(); director.Tick(frame, .000001f);
            Require(Vector3.Distance(position, go.transform.position) < .001f, "Leaving follow causes an instantaneous framing jump.");
            director.ResetView();
            Require(director.ViewMode == 0 && director.DefeatFocus == -1, "Reset pose and reported mode disagree.");
            report.AppendLine("PASS: pause/invalid-dt freeze, second fighter, tiny-dt mode switches, reset mode consistency.");

            director.CycleView(); camera.aspect = .25f;
            for (int i = 0; i < 750; i++) director.Tick(frame, .02f);
            Require(camera.orthographicSize * camera.aspect >= frame.First.Radius - .002f, "Narrow viewport clips the followed body.");
            report.AppendLine("PASS: narrow-window body fitting.");

            var thrown = new BeetleCameraFrame(new BeetleCameraSubject(new Vector3(30, 8, 12), 2.1f, true, false), frame.Second, false);
            director.Tick(thrown, .02f);
            point = camera.WorldToViewportPoint(thrown.First.Center);
            float horizontalRadius = thrown.First.Radius / (2 * camera.orthographicSize * camera.aspect);
            float verticalRadius = thrown.First.Radius / (2 * camera.orthographicSize);
            Require(point.x - horizontalRadius >= -.001f && point.x + horizontalRadius <= 1.001f &&
                point.y - verticalRadius >= -.001f && point.y + verticalRadius <= 1.001f, "Fast followed throw escaped the viewport.");
            report.AppendLine("PASS: rapid airborne follow retains the entire subject while the focus catches up.");

            // 位置由平滑朝向和距离共同推导，静止焦点不能在画面中横向游移。
            director.ResetView(); camera.aspect = 16f / 9;
            var centered = new BeetleCameraFrame(new BeetleCameraSubject(director.Focus, 2, false, false),
                new BeetleCameraSubject(director.Focus, 2, false, false), false);
            director.CycleView();
            for (int i = 0; i < 120; i++)
            {
                director.Tick(centered, .02f);
                point = camera.WorldToViewportPoint(director.Focus);
                Require(Mathf.Abs(point.x - .5f) < .001f && point.y >= .499f && point.y <= .561f, "View transition moved focus outside its framing path.");
            }
            report.AppendLine("PASS: camera position and orientation preserve the framing path during transitions.");

            // A world-north offset passes a +Z-only fixture. Exercise every quadrant and the yaw seam.
            foreach (float heading in new[] { 0f, 90f, 180f, 270f, 359f, 1f })
            {
                var forward = Quaternion.Euler(0, heading, 0) * Vector3.forward;
                var subject = new BeetleCameraSubject(director.Focus, 2, false, false, forward);
                var turning = new BeetleCameraFrame(subject, centered.Second, false);
                for (int i = 0; i < 500; i++) director.Tick(turning, .02f);
                RequireBehind(camera, subject);
                Require(Mathf.Abs(Mathf.Asin(-go.transform.forward.y) * Mathf.Rad2Deg - 60) < .01f,
                    "Changing fighter heading changed the follow elevation.");
                point = camera.WorldToViewportPoint(subject.Center);
                Require(Mathf.Abs(point.x - .5f) < .002f && Mathf.Abs(point.y - director.followViewportHeight) < .002f,
                    "Turning the fighter lost the configured framing.");
            }
            var reverse = new BeetleCameraFrame(new BeetleCameraSubject(director.Focus, 2, false, false,
                Quaternion.Euler(0, 181, 0) * Vector3.forward), centered.Second, false);
            position = go.transform.position; rotation = go.transform.rotation;
            director.Tick(reverse, .000001f);
            Require(Vector3.Distance(position, go.transform.position) < .001f && Quaternion.Angle(rotation, go.transform.rotation) < .01f,
                "An abrupt subject turn snapped the follow camera.");
            rotation = go.transform.rotation;
            director.Tick(reverse, .02f);
            Require(Quaternion.Angle(rotation, go.transform.rotation) > 1 && Quaternion.Angle(rotation, go.transform.rotation) < 45,
                "The follow camera must respond to a turn smoothly rather than remain fixed or snap behind it.");
            report.AppendLine("PASS: all heading quadrants, 359/1-degree wrap, stable elevation/framing and smooth 180-degree turn.");
            ValidateCapture(report);
            report.AppendLine("ALL CAMERA CHECKS PASSED");
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            EditorSceneManager.ClosePreviewScene(scene);
            Directory.CreateDirectory("Artifacts/Framework");
            File.WriteAllText("Artifacts/Framework/CameraValidation.txt", report.ToString());
        }
    }

    static void RequireBehind(Camera camera, in BeetleCameraSubject subject)
    {
        Vector3 back = Vector3.ProjectOnPlane(camera.transform.position - subject.Center, Vector3.up).normalized;
        Vector3 looking = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
        Require(Vector3.Dot(back, subject.Forward) < -.999f && Vector3.Dot(looking, subject.Forward) > .999f,
            "Follow camera is not behind and looking along the selected fighter's heading.");
        Require(Mathf.Abs(camera.transform.right.y) < .001f, "Following a heading introduced camera roll.");
    }

    static void ValidateCapture(StringBuilder report)
    {
        using var fixture = new BeetleValidationScene("CameraFrameCaptureValidation");
        var match = fixture.Match;
        match.StartMatch(71);
        var sim = match.Simulation;
        var dataFrame = BeetleCameraFrame.Capture(sim);
        Vector3 stateForward = new Vector3(sim.Fighters[0].Forward.x, 0, sim.Fighters[0].Forward.y);
        Require(Vector3.Distance(dataFrame.First.Forward, stateForward) < .00001f,
            "A camera frame without actors lost the authoritative heading.");
        var actor = match.actors[0];
        actor.transform.rotation = Quaternion.Euler(24, 137, -12);
        actor.animator.bodyPivot.localRotation = Quaternion.Euler(75, -40, 150);
        var displayed = BeetleCameraFrame.Capture(sim, match.actors);
        var expected = Vector3.ProjectOnPlane(actor.transform.forward, Vector3.up).normalized;
        Require(Vector3.Distance(displayed.First.Forward, expected) < .00001f && Vector3.Distance(expected, stateForward) > .1f,
            "Frame capture followed simulation yaw instead of the displayed actor root.");
        Require(Vector3.Distance(displayed.First.Center, actor.animator.bodyPivot.position) < .00001f,
            "Frame capture stopped following the displayed body center.");
        actor.animator.bodyPivot.localRotation = Quaternion.Euler(-85, 65, -110);
        var tumbled = BeetleCameraFrame.Capture(sim, match.actors);
        Require(Vector3.Distance(tumbled.First.Forward, displayed.First.Forward) < .00001f && Mathf.Abs(tumbled.First.Forward.y) < .00001f,
            "A torso tumble changed the camera's horizontal heading.");
        report.AppendLine("PASS: frame capture uses displayed root heading/body center, ignores torso tumble, and retains data-only heading fallback.");
    }

    public static void CapturePreview()
    {
        using var fixture = new BeetleValidationScene("CameraFrameworkPreview");
        var match = fixture.Match;
        match.StartMatch(71);
        for (int i = 0; i < 160; i++) match.AdvanceFixedStep();
        match.Present(1, match.Simulation.MotionTime, .02f);
        var director = match.spectatorCamera;
        var camera = director.GetComponent<Camera>();
        var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
        var target = new RenderTexture(1280, 800, 24);
        var texture = new Texture2D(1280, 800, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            for (int mode = 1; mode <= 2; mode++)
            {
                director.CycleView();
                for (int i = 0; i < 500; i++) director.Tick(BeetleCameraFrame.Capture(match.Simulation, match.actors), .02f);
                match.hud.Refresh();
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); texture.Apply();
                Directory.CreateDirectory("Artifacts/Framework");
                File.WriteAllBytes("Artifacts/Framework/Follow-" + mode + ".png", texture.EncodeToPNG());
            }
        }
        finally
        {
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
