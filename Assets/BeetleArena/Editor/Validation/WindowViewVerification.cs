using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WindowViewVerification
{
    public const string Output = "Artifacts/WindowVerification";

    [MenuItem("Tools/甲壳虫斗/验证/养殖/窗外地面效果截图", false, 555)]
    public static void Capture()
    {
        Debug.Log("[WindowViewVerification] Starting Capture...");
        Directory.CreateDirectory(Output);
        const string scenePath = "Assets/Scenes/Campaign/CampaignGreenhouse.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        var camGo = GameObject.Find("Main Camera");
        Camera camera = camGo ? camGo.GetComponent<Camera>() : null;
        if (!camera) camera = Object.FindFirstObjectByType<Camera>();
        if (!camera) throw new System.InvalidOperationException("Camera not found in scene: " + scenePath);

        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        var prevTarget = camera.targetTexture;
        camera.targetTexture = rt;

        // Pose 1: Overview (default) - pitch 20 deg
        camera.transform.position = new Vector3(0, 2.7f, -6.1f);
        camera.transform.rotation = Quaternion.Euler(20f, 0, 0);
        camera.Render();
        SaveRT(rt, Output + "/01-Window-Overview.png");

        // Pose 2: Pitch down (looking down at terrarium like user screenshot) - pitch 38 deg
        camera.transform.position = new Vector3(0, 4.3f, -5.5f);
        camera.transform.rotation = Quaternion.Euler(38f, 0, 0);
        camera.Render();
        SaveRT(rt, Output + "/02-Window-PitchDown.png");

        // Pose 3: Orbit - yaw 20 deg, pitch 28 deg
        camera.transform.position = new Vector3(-2.2f, 3.5f, -5.8f);
        camera.transform.rotation = Quaternion.Euler(28f, 20f, 0);
        camera.Render();
        SaveRT(rt, Output + "/03-Window-Orbit.png");

        camera.targetTexture = prevTarget;
        Object.DestroyImmediate(rt);
        Debug.Log("[WindowViewVerification] Capture completed successfully!");
    }

    static void SaveRT(RenderTexture rt, string path)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        RenderTexture.active = prev;
        Debug.Log($"[WindowViewVerification] Saved screenshot: {path} ({new FileInfo(path).Length} bytes)");
    }
}
