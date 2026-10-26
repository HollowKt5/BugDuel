using System;
using System.IO;
using System.Linq;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Replaces the backdrop's moving highlight with a fixed pool of light covering the enclosure.</summary>
public static class HusbandryLightingSetup
{
    public const string BackdropMaterialPath = HusbandryTerrariumSetup.MaterialFolder + "/Backdrop.mat";
    public const string PoolMaterialPath = HusbandryTerrariumSetup.MaterialFolder + "/SpotlightPool.mat";

    [MenuItem("Tools/甲壳虫斗/画面与资源/材质与灯光/应用养殖固定灯光", false, 303)]
    public static void Apply() => Apply(false);

    /// <param name="migrateLegacyLights">Explicitly reset the two existing lights to the fixed-light defaults.</param>
    public static void Apply(bool migrateLegacyLights)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play before applying husbandry lighting.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.isDirty || string.IsNullOrEmpty(scene.path))
                throw new InvalidOperationException("Save all open scenes before applying husbandry lighting.");
        }
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (string path in new[] { BeetleNurseryNavigation.NurseryScenePath, "Assets/Scenes/Campaign/CampaignGreenhouse.unity" })
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                Configure(scene, migrateLegacyLights);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save husbandry lighting: " + path);
            }
            AssetDatabase.SaveAssets();
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
    }

    public static void Configure(Scene scene) => Configure(scene, false);

    static void Configure(Scene scene, bool migrateLegacyLights)
    {
        var roots = scene.GetRootGameObjects();
        var renderers = roots.SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToArray();
        var backdrop = renderers.Single(renderer => renderer.name == "Backdrop");
        var soil = renderers.Single(renderer => renderer.name == "SoilBed");
        var material = AssetDatabase.LoadAssetAtPath<Material>(BackdropMaterialPath);
        if (!material)
        {
            material = new Material(backdrop.sharedMaterial) { name = "Husbandry Backdrop" };
            AssetDatabase.CreateAsset(material, BackdropMaterialPath);
            if (material.shader.name != BeetleArenaToonSetup.ShaderName) BeetleArenaToonSetup.Configure(material);
            material.SetFloat("_SpecStrength", 0);
            EditorUtility.SetDirty(material);
        }
        backdrop.sharedMaterial = material;
        EditorUtility.SetDirty(backdrop);

        var lights = roots.SelectMany(root => root.GetComponentsInChildren<Light>(true)).ToArray();
        var fill = lights.Single(light => light.name == "SoftFill");
        var canopy = lights.Single(light => light.name == "WarmCanopy");
        var spot = lights.SingleOrDefault(light => light.name == "EnclosureSpot");
        bool install = !spot;
        if (install)
        {
            var go = new GameObject("EnclosureSpot");
            SceneManager.MoveGameObjectToScene(go, scene);
            spot = go.AddComponent<Light>();
        }
        if (install || migrateLegacyLights)
        {
            spot.type = LightType.Spot;
            spot.shadows = LightShadows.None;
            spot.intensity = 120;
            spot.color = new Color(1, .94f, .84f);
            spot.range = 28;
            spot.spotAngle = 86;
            spot.innerSpotAngle = 70;
            var ground = soil.bounds;
            spot.transform.SetPositionAndRotation(new Vector3(ground.center.x, ground.max.y + 12, ground.center.z), Quaternion.Euler(90, 0, 0));
        }
        var spotlight = spot.GetComponent<HusbandrySpotlight>();
        if (!spotlight) spotlight = spot.gameObject.AddComponent<HusbandrySpotlight>();
        spotlight.backdrop = backdrop;
        if (!spotlight.groundPool)
        {
            var pool = GameObject.CreatePrimitive(PrimitiveType.Quad);
            pool.name = "SpotlightPool";
            SceneManager.MoveGameObjectToScene(pool, scene);
            UnityEngine.Object.DestroyImmediate(pool.GetComponent<Collider>());
            spotlight.groundPool = pool.GetComponent<MeshRenderer>();
            spotlight.groundPool.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            spotlight.groundPool.receiveShadows = false;
            var poolMaterial = AssetDatabase.LoadAssetAtPath<Material>(PoolMaterialPath);
            if (!poolMaterial)
            {
                poolMaterial = new Material(Shader.Find("BeetleArena/HusbandrySpotlightPool")) { name = "Spotlight Pool" };
                AssetDatabase.CreateAsset(poolMaterial, PoolMaterialPath);
            }
            spotlight.groundPool.sharedMaterial = poolMaterial;
        }
        spotlight.Apply();
        EditorUtility.SetDirty(spotlight);
        EditorUtility.SetDirty(spot);
        EditorUtility.SetDirty(spot.transform);
        if (install || migrateLegacyLights)
        {
            canopy.intensity = 1.1f;
            canopy.shadows = LightShadows.Soft;
            fill.type = LightType.Directional;
            fill.intensity = .45f;
            fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.LookRotation(-new Vector3(5, 5, 6));
            EditorUtility.SetDirty(canopy);
            EditorUtility.SetDirty(fill);
            EditorUtility.SetDirty(fill.transform);
        }

        var previous = SceneManager.GetActiveScene();
        try
        {
            SceneManager.SetActiveScene(scene);
            RenderSettings.sun = canopy;
        }
        finally { SceneManager.SetActiveScene(previous); }
    }
}
