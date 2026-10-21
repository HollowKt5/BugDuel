using System;
using System.IO;
using System.Linq;
using System.Text;
using BeetleArena.Campaign;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class CampaignIridescenceValidation
{
    const string Output = "Artifacts/IridescentShell";
    const string TemporaryScene = "Assets/BeetleArena/Generated/MenuIridescenceValidation.unity";

    [MenuItem("Tools/Beetle Arena/Validate Menu Iridescence")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before rendering comparisons.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the open scene first.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        bool asyncCompilation = ShaderUtil.allowAsyncCompilation;
        Material[] copies = Array.Empty<Material>();
        Directory.CreateDirectory(Output);
        var report = new StringBuilder();
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            CampaignIridescenceSetup.CheckShader();
            var original = EditorSceneManager.OpenScene(CampaignIridescenceSetup.ScenePath, OpenSceneMode.Single);
            if (!EditorSceneManager.SaveScene(original, TemporaryScene, true)) throw new IOException("Cannot create validation scene copy.");
            var scene = EditorSceneManager.OpenScene(TemporaryScene, OpenSceneMode.Single);
            var specimens = Object.FindObjectsByType<CampaignSpecimen>(FindObjectsSortMode.None).OrderBy(x => x.phase).ToArray();
            var body = specimens.SelectMany(x => x.rig.model.GetComponentsInChildren<Renderer>(true)).ToArray();
            var shells = body.Where(CampaignIridescenceSetup.IsIridescentSurface).ToArray();
            Check(specimens.Length == 1 && shells.Length > 3, "one menu beetle / iridescent body", report);
            Check(body.Count(CampaignIridescenceSetup.IsEye) == 2 && body.Where(CampaignIridescenceSetup.IsEye)
                .All(x => x.sharedMaterials.All(m => m.shader.name == BeetleArenaToonSetup.ShaderName)), "both eyes retain original toon materials", report);
            Check(body.Any(CampaignIridescenceSetup.IsHorn) && body.Where(CampaignIridescenceSetup.IsHorn)
                .All(x => x.sharedMaterials.All(m => m.shader.name == BeetleArenaToonSetup.ShaderName)), "horns retain original materials", report);
            Check(body.Any(CampaignIridescenceSetup.IsLeg) && body.Where(CampaignIridescenceSetup.IsLeg)
                .All(x => x.sharedMaterials.All(m => AssetDatabase.GetAssetPath(m).StartsWith(CampaignIridescenceSetup.Folder + "/Leg-"))), "legs use independent darker materials", report);
            Check(shells.All(x => x.sharedMaterials.All(m => m.shader.name == CampaignIridescenceSetup.ShaderName)), "all non-eye/non-horn body surfaces receive the effect: " + shells.Length, report);
            var other = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Renderer>(true)).Except(shells);
            Check(other.All(x => x.sharedMaterials.All(m => !m || m.shader.name == BeetleArenaToonSetup.ShaderName)), "eyes, horns and background retain toon materials", report);
            var source = shells.SelectMany(x => x.sharedMaterials).Distinct().ToArray();
            copies = source.Select(x => new Material(x)).ToArray();
            foreach (var renderer in shells)
                renderer.sharedMaterials = renderer.sharedMaterials.Select(x => copies[Array.IndexOf(source, x)]).ToArray();
            foreach (var name in new[] { "ShellForward", "ShellOutline", "ShadowCaster", "DepthOnly", "DepthNormals" })
                Check(copies.All(x => x.FindPass(name) >= 0), "render pass " + name, report);
            var camera = Object.FindFirstObjectByType<CampaignBackdrop>().sceneCamera;
            Capture(camera, "MenuWorld.png");
            var bounds = specimens[0].rig.shellRenderers[0].bounds;
            foreach (var renderer in specimens[0].rig.shellRenderers) bounds.Encapsulate(renderer.bounds);
            Vector3 focus = bounds.center;
            camera.transform.position = focus + new Vector3(2.5f, 3.5f, 5);
            camera.transform.LookAt(focus);
            camera.orthographic = true;
            camera.orthographicSize = 1.65f;
            camera.farClipPlane = 100;
            Color32[] full = Capture(camera, "Closeup.png");
            foreach (var material in copies) material.SetFloat("_Iridescence", 0);
            int rainbow = Difference(full, Capture(camera, "WithoutIridescence.png"));
            Restore(copies, source);
            foreach (var material in copies) material.SetFloat("_AngleShift", 0);
            int angle = Difference(full, Capture(camera, "WithoutAngleShift.png"));
            Restore(copies, source);
            foreach (var material in copies) material.SetFloat("_Metallic", 0);
            int metallic = Difference(full, Capture(camera, "WithoutMetal.png"));
            Restore(copies, source);
            // Material copies only: leave the user's menu colors intact.
            for (int i = 0; i < copies.Length; i++)
            {
                var material = copies[i];
                material.SetFloat("_RampOffset", .42f);
                material.SetFloat("_AngleShift", .1f);
                material.SetFloat("_HueShift", 0);
                material.SetFloat("_Saturation", 1.5f);
                material.SetFloat("_Brightness", source[i].GetFloat("_Brightness") * 1.05f);
            }
            Capture(camera, "Green.png");
            foreach (var material in copies) material.SetFloat("_HueShift", 120);
            Capture(camera, "Blue.png");
            foreach (var material in copies) material.SetFloat("_HueShift", -120);
            Capture(camera, "Red.png");
            Restore(copies, source);
            camera.transform.RotateAround(focus, Vector3.up, 40);
            Capture(camera, "Angle40.png");
            camera.transform.RotateAround(focus, Vector3.up, -80);
            Capture(camera, "AngleMinus40.png");
            Check(rainbow > 1000, "rainbow reflectance visibly changes pixels: " + rainbow, report);
            Check(angle > 1000, "view-angle color shift visibly changes pixels: " + angle, report);
            Check(metallic > 1000, "metal response visibly changes pixels: " + metallic, report);
            CampaignIridescenceSetup.CheckShader();
            Check(true, "shader compiled after exercising active render variants", report);
            report.AppendLine("COMPLETE PASS");
        }
        finally
        {
            File.WriteAllText(Output + "/Validation.txt", report.ToString());
            foreach (var copy in copies) Object.DestroyImmediate(copy);
            ShaderUtil.allowAsyncCompilation = asyncCompilation;
            // Discard inspection transforms and material copies; never save the validation pose.
            EditorSceneManager.RestoreSceneManagerSetup(setup);
            AssetDatabase.DeleteAsset(TemporaryScene);
        }
    }

    static Color32[] Capture(Camera camera, string name)
    {
        var target = new RenderTexture(1600, 1000, 24);
        var texture = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(Output + "/" + name, texture.EncodeToPNG());
            return texture.GetPixels32();
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(target);
        }
    }
    static int Difference(Color32[] a, Color32[] b)
    {
        int count = 0;
        for (int i = 0; i < a.Length; i++)
            if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) > 16) count++;
        return count;
    }
    static void Restore(Material[] copies, Material[] source)
    {
        for (int i = 0; i < copies.Length; i++) copies[i].CopyPropertiesFromMaterial(source[i]);
    }
    static void Check(bool ok, string name, StringBuilder report)
    {
        report.AppendLine((ok ? "PASS " : "FAIL ") + name);
        if (!ok) throw new InvalidOperationException(name);
    }
}
