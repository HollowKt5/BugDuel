using System;
using System.IO;
using System.Linq;
using BeetleArena.Campaign;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class CampaignIridescenceSetup
{
    public const string ShaderName = "BeetleArena/IridescentShell";
    public const string ScenePath = "Assets/Scenes/Campaign/CampaignMainMenu.unity";
    public const string RampPath = "Assets/BeetleArena/Art/Textures/ShellIridescenceRamp.jpg";
    public const string Folder = "Assets/BeetleArena/Generated/MenuIridescence";
    public const string ProbeName = "Menu shell reflection studio";

    [MenuItem("Tools/甲壳虫斗/画面与资源/材质与灯光/应用菜单虹彩甲壳", false, 302)]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before editing menu materials.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the open scene first.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ApplyToScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        Debug.Log("Single menu beetle installed with iridescent body, darker legs and original eyes/horns.");
    }

    public static bool IsEye(Renderer renderer)
    {
        // Explicit anatomy of bug.fbx: the two spherical eye meshes, not the surrounding head.
        return renderer.name == "tripo_part_15" || renderer.name == "tripo_part_20";
    }

    public static bool IsHorn(Renderer renderer)
    {
        var binding = renderer.GetComponentInParent<BeetleArena.BeetleHornRig>();
        return binding && binding.Owns(renderer) || renderer.name.Contains("jaw from part ") ||
            !binding && renderer.name == "tripo_part_5";
    }

    public static bool IsLeg(Renderer renderer)
    {
        var specimen = renderer.GetComponentInParent<CampaignSpecimen>();
        return renderer is SkinnedMeshRenderer skin && specimen && specimen.rig &&
            skin.bones.Any(bone => specimen.rig.legs.Any(leg => bone == leg.hip || bone == leg.knee || bone == leg.ankle));
    }

    public static bool IsIridescentSurface(Renderer renderer)
    {
        var specimen = renderer.GetComponentInParent<CampaignSpecimen>();
        return specimen && specimen.rig && specimen.rig.model && renderer.transform.IsChildOf(specimen.rig.model)
            && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer) && !IsEye(renderer) && !IsHorn(renderer);
    }

    public static void ApplyToScene(Scene scene)
    {
        if (scene.path != ScenePath) throw new InvalidOperationException("Iridescence installer targets the main menu only.");
        CheckShader();
        var specimens = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<CampaignSpecimen>(true))
            .OrderBy(x => x.phase).ToArray();
        if (specimens.Length == 0) throw new InvalidOperationException("Missing menu specimen.");
        var specimen = specimens[0];
        if (!specimen.rig || !specimen.rig.model || specimen.rig.model.GetComponentsInChildren<Renderer>(true).Count(IsEye) != 2)
            throw new InvalidOperationException("Review menu eye bindings before replacing body materials.");
        BeetleHornBindingUpgrade.Configure(specimen.rig);
        foreach (var duplicate in specimens.Skip(1)) Object.DestroyImmediate(duplicate.gameObject);
        var camera = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<CampaignBackdrop>()).Single().sceneCamera;
        // Preserve the original first specimen's safe placement, clear of the terrarium rim.
        var ray = camera.ViewportPointToRay(new Vector3(.63f, .56f, 0));
        if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance)) specimen.transform.position = ray.GetPoint(distance);
        specimen.transform.localScale = Vector3.one * .8f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(specimen.transform);
        var style = specimen.rig.shellRenderers.SelectMany(x => x.sharedMaterials)
            .FirstOrDefault(x => x && x.shader.name == ShaderName);
        Directory.CreateDirectory(Folder);
        AssetDatabase.ImportAsset(RampPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(RampPath);
        if (!importer) throw new InvalidOperationException("Missing supplied rainbow image: " + RampPath);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = false; // A fixed row must not bleed into the image's dark margins.
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 1024;
        importer.isReadable = false;
        importer.SaveAndReimport();
        var ramp = AssetDatabase.LoadAssetAtPath<Texture2D>(RampPath);
        foreach (var renderer in specimen.rig.model.GetComponentsInChildren<Renderer>(true).Where(IsHorn))
        {
            renderer.sharedMaterials = renderer.sharedMaterials.Select(OriginalMaterial).ToArray();
            EditorUtility.SetDirty(renderer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
        foreach (var renderer in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Renderer>(true)).Where(IsIridescentSurface))
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (!source) continue;
                if (source.shader.name == ShaderName)
                {
                    if (IsLeg(renderer)) materials[i] = LegMaterial(source);
                    continue;
                }
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
                if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("Beetle source material must be an asset.");
                string path = Folder + "/Shell-" + guid + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material)
                {
                    material = style ? new Material(style) : new Material(Shader.Find(ShaderName));
                    material.name = "Menu rainbow metal " + source.name;
                    material.enableInstancing = true;
                    if (source.HasProperty("_BaseMap"))
                    {
                        material.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
                        material.SetTextureScale("_BaseMap", source.GetTextureScale("_BaseMap"));
                        material.SetTextureOffset("_BaseMap", source.GetTextureOffset("_BaseMap"));
                    }
                    material.SetTexture("_IridescenceRamp", ramp);
                    AssetDatabase.CreateAsset(material, path);
                }
                materials[i] = IsLeg(renderer) ? LegMaterial(material) : material;
            }
            renderer.sharedMaterials = materials;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            EditorUtility.SetDirty(renderer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
        foreach (var material in specimen.rig.model.GetComponentsInChildren<Renderer>(true)
                     .Where(IsIridescentSurface).SelectMany(x => x.sharedMaterials).Distinct())
            UpgradeMetalFinish(material);
        var probe = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<ReflectionProbe>(true)).FirstOrDefault(x => x.name == ProbeName);
        if (!probe) probe = new GameObject(ProbeName).AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Custom;
        probe.customBakedTexture = StudioCube();
        probe.size = Vector3.one * 60;
        probe.blendDistance = 2;
        probe.intensity = 1;
        probe.importance = 100;
        probe.boxProjection = false;
        EditorUtility.SetDirty(probe);
    }

    static void UpgradeMetalFinish(Material material)
    {
        if (material.GetFloat("_MetalFinishVersion") >= 1) return;
        // Migrate the shipped preset once. Preserve artist-tuned values, especially hue,
        // saturation and independent leg brightness; reinstallation must be idempotent.
        ReplaceDefault("_RampOffset", .72f, .32f);
        ReplaceDefault("_AngleShift", -.62f, .3f);
        ReplaceDefault("_AnglePower", 1, .7f);
        ReplaceDefault("_Metallic", .94f, 1);
        ReplaceDefault("_Smoothness", .78f, .88f);
        ReplaceDefault("_CoatStrength", .65f, .18f);
        material.SetFloat("_MetalFinishVersion", 1);
        EditorUtility.SetDirty(material);

        void ReplaceDefault(string property, float previous, float next)
        {
            if (Mathf.Abs(material.GetFloat(property) - previous) < .0001f)
                material.SetFloat(property, next);
        }
    }

    static Material OriginalMaterial(Material material)
    {
        if (!material || material.shader.name != ShaderName) return material;
        // Shell-<GUID> records the exact source material; restore it without touching shared assets.
        string name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(material));
        var original = name.StartsWith("Shell-", StringComparison.Ordinal)
            ? AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(name.Substring(6))) : null;
        if (!original) throw new InvalidOperationException("Missing original horn material: " + material.name);
        return original;
    }

    static Material LegMaterial(Material source)
    {
        string sourcePath = AssetDatabase.GetAssetPath(source);
        if (sourcePath.StartsWith(Folder + "/Leg-", StringComparison.Ordinal)) return source;
        string path = Folder + "/Leg-" + AssetDatabase.AssetPathToGUID(sourcePath) + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material) return material;
        material = new Material(source) { name = "Menu dark rainbow legs " + source.name };
        material.SetFloat("_Brightness", Mathf.Clamp(source.GetFloat("_Brightness") * .65f, .25f, 2));
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // Static menu-only softbox environment; no real-time probe capture or per-frame cubemap rendering.
    static Cubemap StudioCube()
    {
        string path = Folder + "/MenuShellStudioV2.asset";
        const string assetName = "Menu shell polished studio";
        var existing = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
        if (existing && existing.name == assetName) return existing;
        const int size = 256;
        var cube = new Cubemap(size, TextureFormat.RGBAHalf, true) { name = assetName, filterMode = FilterMode.Trilinear };
        for (int face = 0; face < 6; face++)
        {
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float u = 2 * (x + .5f) / size - 1, v = 2 * (y + .5f) / size - 1;
                Vector3 d;
                switch ((CubemapFace)face)
                {
                    case CubemapFace.PositiveX: d = new Vector3(1, -v, -u); break;
                    case CubemapFace.NegativeX: d = new Vector3(-1, -v, u); break;
                    case CubemapFace.PositiveY: d = new Vector3(u, 1, v); break;
                    case CubemapFace.NegativeY: d = new Vector3(u, -1, -v); break;
                    case CubemapFace.PositiveZ: d = new Vector3(u, -v, 1); break;
                    default: d = new Vector3(-u, -v, -1); break;
                }
                d.Normalize();
                // Dark gaps and finite softboxes give polished metal readable reflection
                // boundaries. Smooth Gaussian blobs alone made it look like soft plastic.
                Color baseLight = Color.Lerp(new Color(.012f, .018f, .014f),
                    new Color(.11f, .13f, .12f), Mathf.SmoothStep(0, 1, d.y * .5f + .5f));
                float key = Softbox(d, new Vector3(-.6f, .7f, -.4f), .85f, .46f, .22f);
                float fill = Softbox(d, new Vector3(.7f, .4f, .55f), .5f, .7f, .20f);
                float strip = Softbox(d, new Vector3(-.15f, .55f, .82f), .075f, .85f, .055f);
                pixels[y * size + x] = baseLight + new Color(1.8f, 1.65f, 1.4f) * key
                    + new Color(1.1f, 1.25f, 1.3f) * fill + new Color(2.4f, 2.4f, 2.4f) * strip;
            }
            cube.SetPixels(pixels, (CubemapFace)face);
        }
        cube.Apply(true, true);
        if (existing)
        {
            EditorUtility.CopySerialized(cube, existing);
            Object.DestroyImmediate(cube);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(cube, path);
        return cube;
    }

    static float Softbox(Vector3 direction, Vector3 center, float width, float height, float feather)
    {
        center.Normalize();
        float facing = Vector3.Dot(direction, center);
        if (facing <= 0) return 0;
        Vector3 right = Vector3.Cross(Vector3.up, center).normalized;
        Vector3 up = Vector3.Cross(center, right);
        float x = Mathf.Abs(Vector3.Dot(direction, right) / facing);
        float y = Mathf.Abs(Vector3.Dot(direction, up) / facing);
        return (1 - Mathf.SmoothStep(0, 1, (x - width) / feather))
             * (1 - Mathf.SmoothStep(0, 1, (y - height) / feather));
    }

    public static void CheckShader()
    {
        var shader = Shader.Find(ShaderName);
        if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
            throw new InvalidOperationException("Iridescent shader unavailable: " + (shader ? string.Join("\n", ShaderUtil.GetShaderMessages(shader).Select(x => x.message)) : "missing"));
    }

}
