using System;
using System.Collections.Generic;
using System.Linq;
using BeetleArena;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>给现有游戏增量安装双擂台，不重新生成甲虫、相机、旧 HUD 或比赛参数。</summary>
public static class BeetleArenaTerrainSetup
{
    const string Folder = "Assets/BeetleArena/Generated";

    [MenuItem("Tools/Beetle Arena/Install Switchable Arenas")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop play before installing arenas.");
        var scene = SceneManager.GetSceneByPath(BeetleArenaSceneBuilder.ScenePath);
        bool loaded = scene.IsValid() && scene.isLoaded;
        var previous = SceneManager.GetActiveScene();
        if (!loaded) scene = EditorSceneManager.OpenScene(BeetleArenaSceneBuilder.ScenePath, OpenSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var match = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BeetleMatchController>(true)).Single();
            Configure(match);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }
        finally
        {
            if (!loaded) EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    // 也供原场景生成器调用，保证后续重新 Build 不丢失新擂台。重复安装复用已有对象。
    public static void Configure(BeetleMatchController match)
    {
        if (match.arenas == null || match.arenas.Length < 2)
        {
            var roots = match.gameObject.scene.GetRootGameObjects();
            var flat = new GameObject("Flat stump arena").AddComponent<BeetleArenaSurface>();
            flat.displayName = "平地木桩";
            foreach (var root in roots)
                if (root.name == "Stump - ring-out surface" || root.name == "Wood surface"
                    || root.name.StartsWith("Annual growth ring ") || root.name.StartsWith("Bark ridge ") || root.name.StartsWith("Flush rim tick "))
                    root.transform.SetParent(flat.transform, true);
            var stump = flat.GetComponentsInChildren<Collider>(true).Single();
            var floor = roots.Single(r => r.name == "Forest ground").GetComponent<Collider>();
            flat.supportColliders = new[] { stump };
            flat.contactColliders = new[] { stump, floor };
            var uneven = new GameObject("Uneven rock arena").AddComponent<BeetleArenaSurface>();
            uneven.displayName = "起伏岩台"; uneven.grip = .94f;
            var mesh = MakeRockMesh(match.settings.arenaRadius);
            var filter = uneven.gameObject.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
            var renderer = uneven.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] {
                RockMaterial("Rock surface", new Color(.39f,.48f,.43f)),
                RockMaterial("Rock contour", new Color(.33f,.41f,.37f)),
                RockMaterial("Rock cliff", new Color(.18f,.25f,.24f)) };
            // 静态非凸网格保留凹凸表面；不能设 convex，否则谷地会被凸包填平。
            var collider = uneven.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh = mesh;
            collider.convex = false; collider.isTrigger = false;
            uneven.supportColliders = new Collider[] { collider };
            uneven.contactColliders = new Collider[] { collider, floor };
            match.arenas = new[] { flat, uneven };
        }
        EnsureButton(match.hud);
        match.SelectArena(0);
        // 编辑状态也正确站在台面；正式 Play 时仍会根据当前种子重新绑定。
        var sim = new BeetleSimulation(match.settings, match.settings.initialSeed);
        sim.SetGround(match.ActiveSurface);
        for (int i = 0; i < 2; i++)
        { match.actors[i].Bind(sim.Fighters[i], match.settings, match.ActiveSurface); match.actors[i].Render(1, 0, .02f); }
        EditorUtility.SetDirty(match); EditorUtility.SetDirty(match.hud);
    }

    // 缓坡、低谷和斜向脊线，约一世界单位高差；波长大于步长，让六足可在坡面上交替支撑。
    static float Height(float x, float z) => .48f + .34f * Mathf.Sin(x * .55f)
        + .20f * Mathf.Cos(z * .70f) + .16f * Mathf.Sin(x * .45f + z * .65f);

    static Mesh MakeRockMesh(float radius)
    {
        const int segments = 64, rings = 24;
        var vertices = new List<Vector3> { new Vector3(0, Height(0, 0), 0) };
        var uv = new List<Vector2> { new Vector2(.5f, .5f) };
        var triangles = new[] { new List<int>(), new List<int>(), new List<int>() };
        for (int ring = 1; ring <= rings; ring++)
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments, r = radius * ring / rings;
                float x = Mathf.Sin(angle) * r, z = Mathf.Cos(angle) * r;
                vertices.Add(new Vector3(x, Height(x, z), z)); uv.Add(new Vector2(x, z) / (radius * 2) + Vector2.one * .5f);
            }
        for (int i = 0; i < segments; i++) triangles[0].AddRange(new[] { 0, 1 + i, 1 + (i + 1) % segments });
        for (int ring = 1; ring < rings; ring++)
            for (int i = 0; i < segments; i++)
            {
                int a = 1 + (ring - 1) * segments + i, b = a + segments;
                int d = 1 + (ring - 1) * segments + (i + 1) % segments, c = d + segments;
                // 稀疏地层色带帮助远景读出坡面方向，几何面与碰撞使用完全相同的顶点。
                triangles[ring % 6 == 0 ? 1 : 0].AddRange(new[] { a, b, c, a, c, d });
            }
        int sideStart = vertices.Count;
        for (int i = 0; i < segments; i++)
        {
            var p = vertices[1 + (rings - 1) * segments + i];
            vertices.Add(p); vertices.Add(new Vector3(p.x, -1.25f, p.z));
            uv.Add(new Vector2((float)i / segments, 1)); uv.Add(new Vector2((float)i / segments, 0));
        }
        for (int i = 0; i < segments; i++)
        {
            int a = sideStart + i * 2, d = sideStart + (i + 1) % segments * 2;
            triangles[2].AddRange(new[] { a, a + 1, d + 1, a, d + 1, d });
        }
        int bottom = vertices.Count; vertices.Add(new Vector3(0, -1.25f, 0)); uv.Add(Vector2.zero);
        for (int i = 0; i < segments; i++) triangles[2].AddRange(new[] { bottom, sideStart + (i + 1) % segments * 2 + 1, sideStart + i * 2 + 1 });
        string path = Folder + "/UnevenRockArena.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!mesh) { mesh = new Mesh { name = "Uneven rock arena mesh" }; AssetDatabase.CreateAsset(mesh, path); }
        else mesh.Clear();
        mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.subMeshCount = 3;
        for (int i = 0; i < 3; i++) mesh.SetTriangles(triangles[i], i);
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
        return mesh;
    }

    static Material RockMaterial(string name, Color color)
    {
        string path = Folder + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(Shader.Find(BeetleArenaToonSetup.ShaderName)) { name = name }; AssetDatabase.CreateAsset(material, path); }
        BeetleArenaToonSetup.Configure(material);
        material.SetColor("_BaseColor", color); material.SetFloat("_SpecStrength", .025f);
        material.SetFloat("_OutlineWidth", name == "Rock cliff" ? 1.4f : 0);
        EditorUtility.SetDirty(material); return material;
    }

    static void EnsureButton(BeetleArenaHud hud)
    {
        if (!hud.arenaButton)
        {
            // 复用原按钮的字体、主题和交互样式，仅新建一个入口；布局不会触碰已有控件。
            var button = UnityEngine.Object.Instantiate(hud.pauseButton, hud.transform);
            button.name = "Switch arena"; hud.arenaButton = button;
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1); rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = new Vector2(0, -202); rect.sizeDelta = new Vector2(310, 58);
            hud.arenaText = button.GetComponentInChildren<TMP_Text>();
            var textRect = hud.arenaText.rectTransform;
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(5, 3); textRect.offsetMax = new Vector2(-5, -3);
            hud.arenaText.fontSize = 17; hud.arenaText.text = "擂台：平地木桩  [T]\n切换并重开";
        }
        // 只补本功能会显示的字符，不因代码注释变多而重新收集整项目字形。
        var font = hud.arenaText.font;
        font.atlasPopulationMode = AtlasPopulationMode.DynamicOS;
        font.TryAddCharacters("擂台：平地木桩起伏岩台切换并重开T", out string missing);
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        foreach (var texture in font.atlasTextures)
        {
            if (!texture) continue;
                if (!AssetDatabase.Contains(texture)) AssetDatabase.AddObjectToAsset(texture, font);
            EditorUtility.SetDirty(texture);
        }
        EditorUtility.SetDirty(font); EditorUtility.SetDirty(font.material);
        if (!string.IsNullOrEmpty(missing)) Debug.LogWarning("Arena label missing glyphs: " + missing);
    }
}

