using System;
using System.IO;
using System.Linq;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Installs toon materials and validates visual fidelity. Updates generated materials only, without replacing scene, collider, or UI materials.</summary>
public static class BeetleArenaToonSetup
{
    public const string ShaderName = "BeetleArena/Toon";
    static readonly string[] Folders = { "Assets/BeetleArena/Generated/Materials", "Assets/BeetleArena/Generated/ImportedModel" };

    // Resolves materials by asset GUID so updates propagate immediately to scene and prefab references.
    static Material[] Materials() => AssetDatabase.FindAssets("t:Material", Folders)
        .Select(id => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(id))).ToArray();

    [MenuItem("Tools/甲壳虫斗/画面与资源/材质与灯光/应用战斗卡通材质", false, 300)]
    public static void Apply()
    {
        CheckShader();
        foreach (var material in Materials()) Configure(material);
        AssetDatabase.SaveAssets();
        SceneView.RepaintAll();
        Debug.Log("Beetle Arena: toon materials applied.");
    }

    /// <summary>Shared presets for scene and model builders. Preserves base textures and UV tiling; colors dictate material roles.</summary>
    public static void Configure(Material material)
    {
        var shader = Shader.Find(ShaderName);
        if (!shader) throw new InvalidOperationException("Missing shader: " + ShaderName);
        Color original = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;
        material.shader = shader;
        // Clears legacy Lit keywords to prevent stale shader variants; all materials here are opaque surfaces.
        material.shaderKeywords = Array.Empty<string>();
        material.renderQueue = -1;
        material.enableInstancing = true;
        string name = material.name;
        bool beetle = AssetDatabase.GetAssetPath(material).StartsWith(Folders[1], StringComparison.Ordinal)
            && name != "Display floor";
        material.SetColor("_BaseColor", original);
        material.SetColor("_ShadowColor", new Color(.35f, .40f, .53f));
        material.SetColor("_MidColor", new Color(.70f, .77f, .85f));
        material.SetColor("_LightColor", new Color(1, .96f, .86f));
        material.SetFloat("_ShadowThreshold", .28f);
        material.SetFloat("_LightThreshold", .68f);
        material.SetFloat("_BandSoftness", .025f);
        material.SetFloat("_ShadowStrength", .95f);
        material.SetFloat("_AmbientStrength", .25f);
        material.SetColor("_SpecColor", new Color(1, .77f, .39f));
        material.SetFloat("_SpecStrength", beetle ? .65f : .02f);
        material.SetFloat("_SpecPower", beetle ? 38 : 64);
        material.SetFloat("_SpecThreshold", .30f);
        material.SetFloat("_SpecSoftness", .035f);
        material.SetColor("_RimColor", new Color(.26f, .59f, .64f));
        material.SetFloat("_RimStrength", beetle ? .16f : 0);
        material.SetFloat("_RimThreshold", .76f);
        material.SetColor("_OutlineColor", new Color(.025f, .031f, .034f));
        material.SetFloat("_OutlineWidth", beetle ? 1.6f : 1.0f);

        // Pairs bright carapace colors with darker joints, preventing the beetle from reading as a solid black silhouette at distance.
        switch (name)
        {
            case "Elytra chestnut": material.SetColor("_BaseColor", new Color(.72f, .30f, .085f)); break;
            case "Elytra edge": material.SetColor("_BaseColor", new Color(.82f, .41f, .13f)); break;
            case "Thorax bronze": material.SetColor("_BaseColor", new Color(.35f, .19f, .10f)); break;
            case "Head umber": material.SetColor("_BaseColor", new Color(.25f, .135f, .075f)); break;
            case "Horn": material.SetColor("_BaseColor", new Color(.37f, .23f, .13f)); break;
            case "Underside": material.SetColor("_BaseColor", new Color(.17f, .105f, .065f)); break;
            case "Seams and legs":
                material.SetColor("_BaseColor", new Color(.18f, .115f, .07f));
                material.SetFloat("_OutlineWidth", .8f); material.SetFloat("_SpecStrength", .22f); break;
            case "Eyes": material.SetFloat("_OutlineWidth", .7f); material.SetFloat("_SpecStrength", 1); break;
            case "Eye highlights": material.SetFloat("_OutlineWidth", 0); break;
        }
        // Annual rings and faction markers are thin decals; outlines are omitted on them to avoid thick black halos.
        if (name.StartsWith("Growth ring", StringComparison.Ordinal) || name == "Rim marks"
            || name.EndsWith("marker", StringComparison.Ordinal) || name == "Forest floor" || name == "Display floor")
            material.SetFloat("_OutlineWidth", 0);
        EditorUtility.SetDirty(material);
    }

    // Shader import may succeed while a specific pass fails compilation; must inspect ShaderUtil messages concurrently.
    static void CheckShader()
    {
        var shader = Shader.Find(ShaderName);
        if (!shader || !shader.isSupported) throw new InvalidOperationException("Toon shader is missing or unsupported.");
        if (ShaderUtil.ShaderHasError(shader))
            throw new InvalidOperationException(string.Join("\n", ShaderUtil.GetShaderMessages(shader).Select(m => m.message)));
    }

    [MenuItem("Tools/甲壳虫斗/验证/界面与画面/卡通渲染", false, 607)]
    public static void Validate()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop play before toon validation.");
        CheckShader();
        const string temporary = "Assets/BeetleArena/Generated/ToonValidationScene.unity";
        var previous = SceneManager.GetActiveScene();
        if (!AssetDatabase.CopyAsset(BeetleArenaSceneBuilder.ScenePath, temporary))
            throw new InvalidOperationException("Could not copy scene for toon preview.");
        var scene = EditorSceneManager.OpenScene(temporary, OpenSceneMode.Additive);
        // Temporarily disables lights outside active scene to prevent multi-scene main light selection conflicts; restored on exit.
        var otherLights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
            .Where(l => l.gameObject.scene != scene && l.enabled).ToArray();
        var materials = Materials();
        var widths = materials.Select(m => m.GetFloat("_OutlineWidth")).ToArray();
        var spec = materials.Select(m => m.GetFloat("_SpecStrength")).ToArray();
        bool asyncCompilation = ShaderUtil.allowAsyncCompilation;
        try
        {
            // A/B keyword toggling triggers initial shader compilation; screenshots must await compilation rather than mistaking missing draws for differences.
            ShaderUtil.allowAsyncCompilation = false;
            SceneManager.SetActiveScene(scene);
            foreach (var light in otherLights) light.enabled = false;
            var match = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BeetleMatchController>()).Single();
            match.hud.Bind(match); match.StartMatch(7319);
            for (int i = 0; i < 350; i++)
            {
                foreach (var actor in match.actors) actor.BeforeStep();
                match.Simulation.Step(.02f);
            }
            foreach (var actor in match.actors) actor.Render(1, match.Simulation.Elapsed, .02f);
            match.Contacts.ProjectView(match.Simulation);
            match.hud.Refresh();
            var camera = match.spectatorCamera.GetComponent<Camera>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
            camera.cullingMask = 1 << 31;
            Directory.CreateDirectory("Artifacts/BeetleArena");
            Capture(camera, "ToonGame.png");
            match.hud.gameObject.SetActive(false);
            Vector3 focus = (match.actors[0].transform.position + match.actors[1].transform.position) * .5f + Vector3.up * .55f;
            camera.transform.position = focus + new Vector3(1.2f, 3.2f, 8);
            camera.transform.LookAt(focus); camera.orthographicSize = 2.6f;
            Color32[] full = Capture(camera, "ToonCloseup.png");
            foreach (var material in materials) material.SetFloat("_OutlineWidth", 0);
            int outlinePixels = Difference(full, Capture(camera, "ToonWithoutOutline.png"));
            for (int i = 0; i < materials.Length; i++) materials[i].SetFloat("_OutlineWidth", widths[i]);
            foreach (var material in materials) material.SetFloat("_SpecStrength", 0);
            int highlightPixels = Difference(full, Capture(camera, "ToonWithoutHighlight.png"));
            for (int i = 0; i < materials.Length; i++) materials[i].SetFloat("_SpecStrength", spec[i]);
            // Disables shadows in temporary duplicate to validate shadow pass directly, rather than merely checking property values.
            foreach (var light in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>())) light.shadows = LightShadows.None;
            int shadowPixels = Difference(full, Capture(camera, "ToonWithoutShadows.png"));
            CheckShader();
            string report = $"Toon materials: {materials.Length}\nOutline changed pixels: {outlinePixels}\nHighlight changed pixels: {highlightPixels}\nShadow changed pixels: {shadowPixels}\nShader compiler errors: 0\n";
            File.WriteAllText("Artifacts/BeetleArena/ToonValidation.txt", report);
            if (outlinePixels < 100 || highlightPixels < 100 || shadowPixels < 100)
                throw new InvalidOperationException("Rendering feature did not produce a visible difference.\n" + report);
            Debug.Log(report);
        }
        finally
        {
            // A/B captures mutate shared materials temporarily and must restore them; temporary test scenes are discarded without saving.
            for (int i = 0; i < materials.Length; i++)
            { materials[i].SetFloat("_OutlineWidth", widths[i]); materials[i].SetFloat("_SpecStrength", spec[i]); }
            foreach (var light in otherLights) if (light) light.enabled = true;
            ShaderUtil.allowAsyncCompilation = asyncCompilation;
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(previous);
            AssetDatabase.DeleteAsset(temporary);
        }
    }

    // Returns screenshot pixels for same-pose A/B diffing, rather than assuming outlines work purely based on shader import.
    static Color32[] Capture(Camera camera, string name)
    {
        var target = new RenderTexture(1600, 900, 24);
        var pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
        try
        {
            camera.targetTexture = target; Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); pixels.Apply();
            File.WriteAllBytes("Artifacts/BeetleArena/" + name, pixels.EncodeToPNG());
            return pixels.GetPixels32();
        }
        finally
        {
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(target);
        }
    }

    // Tolerates minor color quantization; total RGB delta exceeding 8/255 registers as visible difference.
    static int Difference(Color32[] a, Color32[] b)
    {
        int changed = 0;
        for (int i = 0; i < a.Length; i++)
            if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) > 8) changed++;
        return changed;
    }
}
