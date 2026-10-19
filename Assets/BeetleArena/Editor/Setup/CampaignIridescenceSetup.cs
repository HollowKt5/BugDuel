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

    [MenuItem("Tools/Beetle Arena/Apply Menu Iridescent Shell")]
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
        Debug.Log("Menu iridescent shell installed. Other scenes, rig meshes and UI materials preserved.");
    }

    public static bool IsShell(Renderer renderer)
    {
        var specimen = renderer.GetComponentInParent<CampaignSpecimen>();
        return specimen && specimen.rig && specimen.rig.shellRenderers.Contains(renderer);
    }

    public static void ApplyToScene(Scene scene)
    {
        if (scene.path != ScenePath) throw new InvalidOperationException("Iridescence installer targets the main menu only.");
        CheckShader();
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
        foreach (var renderer in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Renderer>(true)).Where(IsShell))
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (!source) continue;
                if (source.shader.name == ShaderName) continue;
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
                if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("Shell source material must be an asset.");
                string path = Folder + "/Shell-" + guid + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material)
                {
                    material = new Material(Shader.Find(ShaderName)) { name = "Menu rainbow metal shell", enableInstancing = true };
                    if (source.HasProperty("_BaseMap"))
                    {
                        material.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
                        material.SetTextureScale("_BaseMap", source.GetTextureScale("_BaseMap"));
                        material.SetTextureOffset("_BaseMap", source.GetTextureOffset("_BaseMap"));
                    }
                    material.SetTexture("_IridescenceRamp", ramp);
                    AssetDatabase.CreateAsset(material, path);
                }
                materials[i] = material;
            }
            renderer.sharedMaterials = materials;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            EditorUtility.SetDirty(renderer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
        var probe = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<ReflectionProbe>(true)).FirstOrDefault(x => x.name == ProbeName);
        if (!probe) probe = new GameObject(ProbeName).AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Custom;
        probe.customBakedTexture = StudioCube();
        probe.size = Vector3.one * 60;
        probe.blendDistance = 2;
        probe.intensity = 1.25f;
        probe.importance = 100;
        probe.boxProjection = false;
        EditorUtility.SetDirty(probe);
    }

    // Static menu-only softbox environment; no real-time probe capture or per-frame cubemap rendering.
    static Cubemap StudioCube()
    {
        string path = Folder + "/MenuShellStudio.asset";
        var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
        if (cube) return cube;
        const int size = 128;
        cube = new Cubemap(size, TextureFormat.RGBAHalf, true) { name = "Menu shell studio", filterMode = FilterMode.Trilinear };
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
                Color baseLight = Color.Lerp(new Color(.025f, .035f, .02f), new Color(.36f, .42f, .38f), Mathf.Clamp01(d.y * .5f + .5f));
                float key = Mathf.Pow(Mathf.Max(0, Vector3.Dot(d, new Vector3(-.6f, .7f, -.4f).normalized)), 20);
                float fill = Mathf.Pow(Mathf.Max(0, Vector3.Dot(d, new Vector3(.7f, .4f, .55f).normalized)), 10);
                float strip = Mathf.Pow(Mathf.Max(0, 1 - Mathf.Abs(d.x + .2f) * 4), 6) * Mathf.Clamp01(d.y * 2);
                pixels[y * size + x] = baseLight + new Color(3.5f, 3.2f, 2.7f) * key + new Color(1.7f, 2, 2.2f) * fill + Color.white * strip;
            }
            cube.SetPixels(pixels, (CubemapFace)face);
        }
        cube.Apply(true, true);
        AssetDatabase.CreateAsset(cube, path);
        return cube;
    }

    public static void CheckShader()
    {
        var shader = Shader.Find(ShaderName);
        if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
            throw new InvalidOperationException("Iridescent shader unavailable: " + (shader ? string.Join("\n", ShaderUtil.GetShaderMessages(shader).Select(x => x.message)) : "missing"));
    }
}
