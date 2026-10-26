using System.Linq;
using BeetleArena;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BeetleCombatUpgrade
{
    [MenuItem("Tools/甲壳虫斗/场景与安装/战斗模块/应用战斗调优", false, 223)]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop play before applying refinements.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(BeetleArenaSceneBuilder.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(BeetleArenaSceneBuilder.ScenePath);
        var match = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BeetleMatchController>(true)).Single();
        BeetleArenaTerrainSetup.Configure(match);
        Configure(match);
        BeetleArenaSceneBuilder.MakeFont();
        match.settings.probingSeconds = 3.2f; match.settings.clashSeconds = 12;
        match.settings.openingSeconds = 12; match.settings.escalationSeconds = 55;
        match.settings.openingDisplacement = .75f; match.settings.finalDisplacement = 2.25f;
        match.settings.throwHeavySpeedMultiplier = 2.2f;
        match.settings.throwInspectionMode = true; // User-requested elevated frequency for play inspection.
        match.settings.shoveImpulse = 3.6f;
        match.settings.burstDrainPerSecond = .26f;
        match.settings.burstRecoveryPerSecond = .40f;
        match.settings.contactRecoveryMultiplier = .72f;
        match.settings.exertionRecoveryDelay = .24f;
        match.settings.moduleActionChance = .58f;
        match.settings.forelegActionChance = .48f;
        // Append only the new action; preserve any deliberately removed modules.
        const string surgePath = "Assets/BeetleArena/Generated/Motions/HornSurgeMotion.asset";
        var surge = AssetDatabase.LoadAssetAtPath<HornSurgeMotion>(surgePath);
        if (!surge) { surge = ScriptableObject.CreateInstance<HornSurgeMotion>(); AssetDatabase.CreateAsset(surge, surgePath); }
        if (!match.settings.motionModules.Contains(surge)) match.settings.motionModules = match.settings.motionModules.Concat(new BeetleMotionModule[] { surge }).ToArray();
        Append<ForelegPushMotion>(match.settings);
        Append<ForelegStrikeMotion>(match.settings);
        EditorUtility.SetDirty(match.settings);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
    }
    static void Append<T>(BeetleMatchSettings settings) where T : BeetleMotionModule
    {
        string path = "Assets/BeetleArena/Generated/Motions/" + typeof(T).Name + ".asset";
        var module = AssetDatabase.LoadAssetAtPath<T>(path);
        if (!module) { module = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(module, path); }
        if (!settings.motionModules.Contains(module)) settings.motionModules = settings.motionModules.Concat(new BeetleMotionModule[] { module }).ToArray();
    }
    public static void Configure(BeetleMatchController match)
    {
        var hud = match.hud;
        hud.resultText.rectTransform.sizeDelta = new Vector2(460, 100);
        foreach (var text in hud.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.name == "Rules") text.text = "只有落台分胜负 · 时间到双方平局\n单局含开场与击败展示不超过 3 分钟（暂停除外）";
            if (text.name == "Result kicker") text.text = "MATCH RESULT  /  本场结算";
        }
        if (!hud.viewButton) hud.viewButton = Button(hud, "View mode", -48, out hud.viewText);
        if (!hud.slowMotionButton) hud.slowMotionButton = Button(hud, "Defeat slow motion", -94, out hud.slowMotionText);
        hud.viewText.text = "视角：全景  [C]"; hud.slowMotionText.text = "击败慢镜头：开  [V]";
        foreach (var label in new[] { hud.viewText, hud.slowMotionText })
        {
            var rect = label.rectTransform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(6, 2); rect.offsetMax = new Vector2(-6, -2);
        }
        if (hud.traitTexts == null || hud.traitTexts.Length != 2) hud.traitTexts = new TMP_Text[2];
        for (int i = 0; i < 2; i++)
        {
            var ui = hud.fighters[i];
            var oldReserve = ui.measurements.transform.parent.Find("Contest reserve");
            if (oldReserve) Object.DestroyImmediate(oldReserve.gameObject);
            if (!hud.traitTexts[i])
            {
                var text = Object.Instantiate(ui.measurements, ui.measurements.transform.parent);
                text.name = "Quantified traits";
                text.rectTransform.anchoredPosition = new Vector2(20, -181);
                text.rectTransform.sizeDelta = new Vector2(320, 54);
                text.fontSize = 14; text.alignment = TextAlignmentOptions.TopLeft;
                hud.traitTexts[i] = text;
            }
            var card = ui.measurements.transform.parent.GetComponent<RectTransform>();
            card.sizeDelta = new Vector2(card.sizeDelta.x, 250);
            AddNeck(match.actors[i].animator);
            ConfigureHeadClearance(match.actors[i].animator);
        }
        EditorUtility.SetDirty(hud); EditorUtility.SetDirty(match);
    }
    static void ConfigureHeadClearance(BeetleProceduralAnimator anim)
    {
        var samples = new System.Collections.Generic.Dictionary<Vector2Int, Vector3>();
        void Add(Vector3 p)
        {
            var key = new Vector2Int(Mathf.FloorToInt(p.x / .08f), Mathf.FloorToInt(p.z / .10f));
            if (!samples.TryGetValue(key, out var old) || p.y < old.y) samples[key] = p;
        }
        foreach (var filter in anim.headPivot.GetComponentsInChildren<MeshFilter>())
        {
            if (!filter.sharedMesh) continue;
            foreach (var vertex in filter.sharedMesh.vertices)
            {
                Vector3 p = anim.headPivot.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                Add(p);
            }
        }
        var baked = new Mesh();
        try
        {
            foreach (var renderer in anim.headPivot.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.BakeMesh(baked);
                foreach (var vertex in baked.vertices)
                    Add(anim.headPivot.InverseTransformPoint(renderer.transform.position + renderer.transform.rotation * vertex));
            }
        }
        finally { Object.DestroyImmediate(baked); }
        anim.importedRig.headGroundPoints = samples.Values.ToArray();
        EditorUtility.SetDirty(anim.importedRig);
    }
    static UnityEngine.UI.Button Button(BeetleArenaHud hud, string name, float y, out TMP_Text text)
    {
        var button = Object.Instantiate(hud.pauseButton, hud.pauseButton.transform.parent);
        button.name = name;
        var rect = button.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(-34, y); rect.sizeDelta = new Vector2(230, 38);
        text = button.GetComponentInChildren<TMP_Text>(); text.fontSize = 16;
        return button;
    }
    static void AddNeck(BeetleProceduralAnimator anim)
    {
        var existing = anim.model.Find("Flexible cervical membrane");
        var head = anim.headPivot; var body = anim.bodyPivot;
        Quaternion oldHead = head.localRotation, oldBody = body.localRotation;
        Vector3 oldScale = head.localScale;
        head.localRotation = body.localRotation = Quaternion.identity; head.localScale = Vector3.one;
        try
        {
            const int rings = 5, sides = 24;
            var positions = new Vector3[rings * sides + 2]; var uv = new Vector2[positions.Length];
            var weights = new BoneWeight[positions.Length]; var indices = new int[(rings - 1) * sides * 6 + sides * 6];
            Vector3 center = anim.model.InverseTransformPoint(head.position);
            for (int ring = 0; ring < rings; ring++) for (int side = 0; side < sides; side++)
            {
                float t = ring / (float)(rings - 1), a = side * Mathf.PI * 2 / sides;
                int v = ring * sides + side;
                // Both ends sit inside the existing shells; blended middle seals the moving joint.
                positions[v] = center + new Vector3(Mathf.Cos(a) * Mathf.Lerp(.52f, .34f, t),
                    Mathf.Lerp(.12f, 0, t) + Mathf.Sin(a) * Mathf.Lerp(.40f, .27f, t), Mathf.Lerp(-.32f, .20f, t));
                uv[v] = new Vector2(side / (float)sides, t);
                weights[v] = new BoneWeight { boneIndex0 = 0, weight0 = 1 - t, boneIndex1 = 1, weight1 = t };
                if (ring == rings - 1) continue;
                int n = ring * sides + (side + 1) % sides, k = (ring * sides + side) * 6;
                indices[k] = v; indices[k + 1] = n; indices[k + 2] = v + sides;
                indices[k + 3] = n; indices[k + 4] = n + sides; indices[k + 5] = v + sides;
            }
            int back = rings * sides, front = back + 1;
            positions[back] = center + new Vector3(0, .12f, -.32f); positions[front] = center + new Vector3(0, 0, .20f);
            weights[back] = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
            weights[front] = new BoneWeight { boneIndex0 = 1, weight0 = 1 };
            for (int side = 0; side < sides; side++)
            {
                int n = (side + 1) % sides, k = (rings - 1) * sides * 6 + side * 6;
                indices[k] = back; indices[k + 1] = n; indices[k + 2] = side;
                indices[k + 3] = front; indices[k + 4] = (rings - 1) * sides + side; indices[k + 5] = (rings - 1) * sides + n;
            }
            var mesh = new Mesh { name = "Cervical membrane", vertices = positions, uv = uv, triangles = indices, boneWeights = weights,
                bindposes = new[] { body.worldToLocalMatrix * anim.model.localToWorldMatrix, head.worldToLocalMatrix * anim.model.localToWorldMatrix } };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            string path = "Assets/BeetleArena/Generated/CervicalMembrane.asset";
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!saved) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
            else { EditorUtility.CopySerialized(mesh, saved); EditorUtility.SetDirty(saved); Object.DestroyImmediate(mesh); }
            var go = existing ? existing.gameObject : new GameObject("Flexible cervical membrane"); go.transform.SetParent(anim.model, false);
            var renderer = go.GetComponent<SkinnedMeshRenderer>(); if (!renderer) renderer = go.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = saved;
            renderer.bones = new[] { body, head }; renderer.rootBone = body; renderer.updateWhenOffscreen = true;
            const string materialPath = "Assets/BeetleArena/Generated/Materials/Cervical membrane.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material)
            {
                material = new Material(Shader.Find(BeetleArenaToonSetup.ShaderName)) { name = "Cervical membrane" };
                BeetleArenaToonSetup.Configure(material);
                material.SetColor("_BaseColor", new Color(.10f, .036f, .018f));
                material.SetFloat("_SpecStrength", .025f); material.SetFloat("_OutlineWidth", 0);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            renderer.sharedMaterial = material;
            renderer.localBounds = new Bounds(center, Vector3.one * 3);
        }
        finally { head.localRotation = oldHead; body.localRotation = oldBody; head.localScale = oldScale; }
    }
}
