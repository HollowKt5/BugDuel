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
            var frame = new BeetleCameraFrame(new BeetleCameraSubject(new Vector3(2, 1.3f, 1), 2.1f, false, false),
                new BeetleCameraSubject(new Vector3(-2, 2.6f, -1), 2.1f, false, false), false);
            director.CycleView();
            for (int i = 0; i < 500; i++) director.Tick(frame, .02f);
            var point = camera.WorldToViewportPoint(frame.First.Center);
            Require(Mathf.Abs(point.x - .5f) < .002f && Mathf.Abs(point.y - director.followViewportHeight) < .002f && point.z > 0,
                "Followed body must appear at the configured upper-center viewport position.");
            Require(go.transform.position.y > frame.First.Center.y + 15.9f, "Overhead follow is too low.");
            Require(Vector3.Dot(go.transform.up, Vector3.forward) > .999f, "Vertical follow orientation is unstable.");
            report.AppendLine("PASS: raised overhead view, body-centered framing and stable north-up orientation.");

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
