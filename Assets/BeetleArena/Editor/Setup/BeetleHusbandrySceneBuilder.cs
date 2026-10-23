using System;
using System.IO;
using System.Linq;
using BeetleArena;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>只通过 Editor API 创建养殖场景；对既有对战场景只增添入口，不重建它。</summary>
public static class BeetleHusbandrySceneBuilder
{
    public const string ScenePath = BeetleNurseryNavigation.NurseryScenePath;
    const string PrefabPath = "Assets/BeetleArena/Generated/ArticulatedBeetle.prefab";
    const string MaterialFolder = "Assets/BeetleArena/Generated/NurseryMaterials";
    static readonly Color PanelColor = new Color(.055f, .105f, .082f, .97f);
    static readonly Color Accent = new Color(.83f, .70f, .43f);
    static readonly Color ButtonColor = new Color(.19f, .31f, .23f);

    // 独立批处理进程没有用户未保存场景；先载入已保存的对战场景，避开启动时的 Untitled 占位场景。
    public static void BuildInBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use the normal build menu in an interactive Editor.");
        EditorSceneManager.OpenScene(BeetleNurseryNavigation.ArenaScenePath, OpenSceneMode.Single);
        BuildAndInstall();
    }

    [MenuItem("Tools/Beetle Arena/Build Husbandry Scene")]
    public static void BuildAndInstall()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before building the nursery.");
        if (File.Exists(ScenePath) || AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
            throw new InvalidOperationException("The nursery scene already exists; refusing to overwrite: " + ScenePath);
        if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(BeetleNurseryNavigation.ArenaScenePath))
            throw new InvalidOperationException("Build the existing Beetle Arena scene first.");
        var arena = SceneManager.GetSceneByPath(BeetleNurseryNavigation.ArenaScenePath);
        bool openedArena = !arena.IsValid() || !arena.isLoaded;
        if (!openedArena && arena.isDirty)
            throw new InvalidOperationException("Save the open Arena scene before installing its nursery button.");
        var previous = SceneManager.GetActiveScene();
        Scene nursery = default;
        try
        {
            if (openedArena) arena = EditorSceneManager.OpenScene(BeetleNurseryNavigation.ArenaScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(arena);
            nursery = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            BuildContents(nursery);
            if (!EditorSceneManager.SaveScene(nursery, ScenePath)) throw new IOException("Could not save the nursery scene.");
            InstallArenaEntry(arena);
            if (!EditorSceneManager.SaveScene(arena)) throw new IOException("Could not save the Arena navigation button.");
            var entries = EditorBuildSettings.scenes.ToList();
            foreach (string path in new[] { BeetleNurseryNavigation.ArenaScenePath, ScenePath })
            {
                var existing = entries.FirstOrDefault(entry => entry.path == path);
                if (existing != null) existing.enabled = true;
                else entries.Add(new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = entries.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("Beetle Nursery installed: " + ScenePath);
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (nursery.IsValid() && nursery.isLoaded && !EditorSceneManager.CloseScene(nursery, true))
                Debug.LogError("Could not close the newly built nursery scene.");
            if (openedArena && arena.IsValid() && arena.isLoaded && !EditorSceneManager.CloseScene(arena, true))
                Debug.LogError("Could not close the Arena scene opened for navigation installation.");
        }
    }

    /// <summary>供验证使用：向调用方拥有的空场景构建内容，不保存场景、不更改 Build Settings。</summary>
    public static BeetleHusbandryController BuildContents(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded || scene.rootCount != 0)
            throw new ArgumentException("BuildContents requires an empty loaded scene.", nameof(scene));
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (!prefab) throw new InvalidOperationException("The articulated beetle prefab is missing: " + PrefabPath);
        var font = BeetleArenaSceneBuilder.MakeFont();
        var previous = SceneManager.GetActiveScene();
        if (SceneManager.GetActiveScene() != scene && !SceneManager.SetActiveScene(scene))
            throw new InvalidOperationException("BuildContents cannot activate its loaded scene: " + scene.name);
        try
        {
            BuildTerrarium();
            var camera = BuildCamera();
            HusbandryLightingSetup.Configure(scene);
            var system = new GameObject("HusbandryController");
            var controller = system.AddComponent<BeetleHusbandryController>();
            controller.secondsPerGameDay = 600; controller.simulateAccidents = true; controller.loadOnStart = true;
            var navigation = system.AddComponent<BeetleNurseryNavigation>();
            var specimen = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            specimen.name = "ObservedBeetle";
            specimen.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, 145, 0));
            specimen.transform.localScale = Vector3.one;
            PrefabUtility.RecordPrefabInstancePropertyModifications(specimen.transform);
            foreach (var actor in specimen.GetComponentsInChildren<BeetleActor>(true))
            {
                actor.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(actor);
                if (actor.teamMarker)
                {
                    actor.teamMarker.gameObject.SetActive(false);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(actor.teamMarker.gameObject);
                }
            }
            foreach (var contacts in specimen.GetComponentsInChildren<BeetleContactRig>(true))
            { contacts.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(contacts); }
            foreach (var collider in specimen.GetComponentsInChildren<Collider>(true))
            { collider.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(collider); }
            var view = specimen.AddComponent<BeetleHusbandryView>();
            view.controller = controller;
            view.sourceRig = specimen.GetComponentInChildren<BeetleProceduralAnimator>(true);
            if (!view.sourceRig) throw new InvalidOperationException("The display prefab has no procedural rig.");
            BuildHud(controller, navigation, camera, font);
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            return controller;
        }
        finally { if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); }
    }

    static Camera BuildCamera()
    {
        var go = new GameObject("NurseryCamera", typeof(Camera), typeof(AudioListener));
        go.tag = "MainCamera";
        var camera = go.GetComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = 6.8f;
        camera.nearClipPlane = .1f; camera.farClipPlane = 60;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.045f, .08f, .064f);
        go.transform.position = new Vector3(6.2f, 7.4f, -8.5f);
        go.transform.LookAt(new Vector3(0, .8f, 0));
        var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipeline)
        {
            var renderers = new SerializedObject(pipeline).FindProperty("m_RendererDataList");
            for (int i = 0; renderers != null && i < renderers.arraySize; i++)
            {
                var data = renderers.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;
                if (data && data.rendererFeatures.Count == 0) { camera.GetUniversalAdditionalCameraData().SetRenderer(i); break; }
            }
        }
        return camera;
    }

    static void BuildTerrarium()
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/BeetleArena/Generated", "NurseryMaterials");
        HusbandryTerrariumSetup.EnsureAssets();
        var soil = AssetDatabase.LoadAssetAtPath<Material>(HusbandryTerrariumSetup.SoilTopMaterialPath);
        var wood = Material("WarmWood", new Color(.34f, .25f, .15f));
        var leaf = Material("DeepLeaves", new Color(.13f, .25f, .15f));
        var stone = Material("Clay", new Color(.47f, .39f, .27f));
        var jelly = Material("AmberFood", new Color(.82f, .52f, .19f));
        var floor = Material("ForestBackdrop", new Color(.07f, .115f, .085f));
        var root = new GameObject("Terrarium").transform;
        Primitive(root, "Backdrop", PrimitiveType.Cube, new Vector3(0, -.65f, 0), new Vector3(30, .2f, 30), floor);
        Primitive(root, "SoilBed", PrimitiveType.Cube, new Vector3(0, -.22f, 0), new Vector3(9.4f, .4f, 7.4f), soil);
        var log = Primitive(root, "ClimbingLog", PrimitiveType.Cylinder, new Vector3(-2.8f, .28f, 1.7f), new Vector3(.68f, 1.4f, .68f), wood);
        log.transform.rotation = Quaternion.Euler(0, 28, 90);
        Primitive(root, "FoodDish", PrimitiveType.Cylinder, new Vector3(2.6f, .035f, -1.7f), new Vector3(1.05f, .06f, 1.05f), stone);
        Primitive(root, "FoodJelly", PrimitiveType.Cylinder, new Vector3(2.6f, .12f, -1.7f), new Vector3(.64f, .06f, .64f), jelly);
        var random = new System.Random(8041);
        for (int i = 0; i < 26; i++)
        {
            float x = (float)random.NextDouble() * 8.5f - 4.25f;
            float z = (float)random.NextDouble() * 6.6f - 3.3f;
            if (Mathf.Abs(x) < 1.7f && Mathf.Abs(z) < 2.2f) continue;
            float scale = .10f + (float)random.NextDouble() * .22f;
            Primitive(root, "Substrate" + i, PrimitiveType.Sphere, new Vector3(x, 0, z), new Vector3(scale, scale * .38f, scale * .7f), i % 3 == 0 ? stone : wood);
        }
        for (int i = 0; i < 7; i++)
        {
            var piece = Primitive(root, "Leaf" + i, PrimitiveType.Sphere, new Vector3(-3.5f + i * .35f, .07f, 2.8f), new Vector3(.45f, .08f, 1.2f), leaf);
            piece.transform.rotation = Quaternion.Euler(0, -55 + i * 23, 8);
        }
        HusbandryTerrariumSetup.ApplyToTerrarium(root);
        Light("WarmCanopy", new Vector3(-5, 9, -4), new Color(1, .86f, .65f), 1.8f);
        Light("SoftFill", new Vector3(5, 5, 6), new Color(.69f, .82f, .73f), .8f);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.39f, .42f, .33f);
        RenderSettings.ambientEquatorColor = new Color(.25f, .28f, .20f);
        RenderSettings.ambientGroundColor = new Color(.13f, .10f, .07f);
        RenderSettings.fog = false;
    }

    static void BuildHud(BeetleHusbandryController controller, BeetleNurseryNavigation navigation, Camera camera, TMP_FontAsset font)
    {
        var go = new GameObject("NurseryHUD", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster), typeof(BeetleHusbandryHud));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
        var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
        var hud = go.GetComponent<BeetleHusbandryHud>(); hud.controller = controller;
        var root = go.transform;
        Label(root, "Title", "甲角 · 林间养殖室", font, 29, new Vector2(0, 1), new Vector2(24, -22), new Vector2(460, 42), Accent);
        Label(root, "Subtitle", "BEETLE NURSERY  /  照料 · 生长 · 观察", font, 14, new Vector2(0, 1), new Vector2(26, -65), new Vector2(500, 24), Color.white);
        hud.pauseButton = Button(root, "PauseButton", "暂停养殖", font, new Vector2(1, 1), new Vector2(-268, -28), new Vector2(140, 42), out hud.pauseText);
        hud.saveButton = Button(root, "SaveButton", "保存", font, new Vector2(1, 1), new Vector2(-146, -28), new Vector2(110, 42), out _);
        hud.reloadButton = Button(root, "ReloadButton", "更新内容", font, new Vector2(1, 1), new Vector2(-24, -28), new Vector2(110, 42), out _);
        var left = Panel(root, "IndividualPanel", PanelColor);
        Stretch(left, new Vector2(0, 0), new Vector2(0, 1), new Vector2(24, 166), new Vector2(274, -104));
        Label(left, "Heading", "我的甲虫", font, 23, new Vector2(0, 1), new Vector2(18, -18), new Vector2(214, 34), Accent);
        Label(left, "ListHint", "选择个体查看与照料", font, 15, new Vector2(0, 1), new Vector2(18, -58), new Vector2(214, 24), Color.white);
        hud.individualList = ScrollList(left, "IndividualList", new Vector2(12, 82), new Vector2(-12, -94), false);
        var back = Button(left, "ArenaButton", "返回林间擂台", font, new Vector2(0, 0), new Vector2(18, 20), new Vector2(214, 44), out _);
        UnityEventTools.AddPersistentListener(back.onClick, navigation.OpenArena);
        var right = Panel(root, "CarePanel", PanelColor);
        Stretch(right, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-374, 166), new Vector2(-24, -104));
        hud.selectedName = Label(right, "SelectedName", "等待选择个体", font, 24, new Vector2(0, 1), new Vector2(20, -18), new Vector2(310, 38), Accent);
        hud.statusText = Label(right, "Status", "观察中", font, 18, new Vector2(0, 1), new Vector2(20, -62), new Vector2(310, 50), Color.white);
        hud.conditionText = Label(right, "Condition", "健康 · 营养 · 体力", font, 18, new Vector2(0, 1), new Vector2(20, -120), new Vector2(310, 100), Color.white);
        hud.detailsText = Label(right, "Details", "个体档案", font, 16, new Vector2(0, 1), new Vector2(20, -230), new Vector2(310, 94), Color.white);
        Label(right, "FoodHeading", "喂食与照料", font, 20, new Vector2(0, 1), new Vector2(20, -338), new Vector2(310, 30), Accent);
        hud.foodList = ScrollList(right, "FoodList", new Vector2(14, 84), new Vector2(-14, -378), false);
        UnityEngine.Object.DestroyImmediate(hud.foodList.GetComponent<UnityEngine.UI.VerticalLayoutGroup>());
        var foodGrid = hud.foodList.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
        foodGrid.cellSize = new Vector2(154, 68); foodGrid.spacing = new Vector2(6, 6);
        foodGrid.padding = new RectOffset(4, 4, 0, 0); foodGrid.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount; foodGrid.constraintCount = 2;
        hud.helpButton = Button(right, "HelpButton", "帮它翻身", font, new Vector2(0, 0), new Vector2(20, 20), new Vector2(310, 48), out _);
        hud.messageText = Label(root, "Message", "观察甲虫，留意翻倒与健康变化。", font, 18, new Vector2(.47f, 0), new Vector2(0, 172), new Vector2(850, 58), Color.white);
        hud.messageText.alignment = TextAlignmentOptions.Center;
        var habitats = Panel(root, "HabitatPanel", PanelColor);
        Stretch(habitats, new Vector2(0, 0), new Vector2(1, 0), new Vector2(24, 24), new Vector2(-24, 142));
        Label(habitats, "HabitatHeading", "饲养环境", font, 22, new Vector2(0, 1), new Vector2(20, -18), new Vector2(180, 34), Accent);
        Label(habitats, "HabitatHint", "选择适合当前阶段的环境", font, 14, new Vector2(0, 1), new Vector2(20, -62), new Vector2(180, 42), Color.white);
        hud.habitatList = ScrollList(habitats, "HabitatList", new Vector2(220, 16), new Vector2(-16, -16), true);
        hud.choiceTemplate = Button(root, "ChoiceTemplate", "选项", font, new Vector2(0, 0), Vector2.zero, new Vector2(280, 64), out var choiceText);
        choiceText.fontSize = 17; choiceText.enableAutoSizing = true; choiceText.fontSizeMin = 14; choiceText.fontSizeMax = 17;
        var layout = hud.choiceTemplate.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        layout.minHeight = 64; layout.preferredHeight = 64; layout.preferredWidth = 280; layout.flexibleHeight = 0; layout.flexibleWidth = 0;
        hud.choiceTemplate.gameObject.SetActive(false);
    }

    static Transform ScrollList(Transform parent, string name, Vector2 minimum, Vector2 maximum, bool horizontal)
    {
        var root = Rect(parent, name);
        Stretch(root, Vector2.zero, Vector2.one, minimum, maximum);
        var scroll = root.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        var viewport = Panel(root, "Viewport", new Color(1, 1, 1, .012f));
        Stretch(viewport, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewport.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
        viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
        var content = Rect(viewport, "Content");
        content.pivot = new Vector2(0, 1);
        if (horizontal)
        {
            content.anchorMin = Vector2.zero; content.anchorMax = new Vector2(0, 1); content.sizeDelta = Vector2.zero;
            var group = content.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            group.spacing = 10; group.childControlWidth = true; group.childControlHeight = true;
            group.childForceExpandWidth = false; group.childForceExpandHeight = true;
            content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        }
        else
        {
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.sizeDelta = Vector2.zero;
            var group = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            group.spacing = 8; group.childControlWidth = true; group.childControlHeight = true;
            group.childForceExpandWidth = true; group.childForceExpandHeight = false;
            content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        }
        content.anchoredPosition = Vector2.zero;
        scroll.viewport = viewport; scroll.content = content; scroll.horizontal = horizontal; scroll.vertical = !horizontal;
        scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 32;
        return content;
    }

    static void InstallArenaEntry(Scene arena)
    {
        var hud = arena.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BeetleArenaHud>(true)).Single();
        if (hud.transform.Find("NurseryButton")) throw new InvalidOperationException("The Arena nursery entry already exists; refusing duplicate installation.");
        var navigation = hud.GetComponent<BeetleNurseryNavigation>();
        if (!navigation) navigation = hud.gameObject.AddComponent<BeetleNurseryNavigation>();
        var button = Button(hud.transform, "NurseryButton", "养殖 [B]", BeetleArenaSceneBuilder.MakeFont(), new Vector2(1, .5f), new Vector2(-34, -140), new Vector2(230, 38), out _);
        UnityEventTools.AddPersistentListener(button.onClick, navigation.OpenNursery);
        EditorSceneManager.MarkSceneDirty(arena);
    }

    static RectTransform Rect(Transform parent, string name)
    { var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); return rect; }
    static void Stretch(RectTransform rect, Vector2 minimum, Vector2 maximum, Vector2 offsetMin, Vector2 offsetMax)
    { rect.anchorMin = minimum; rect.anchorMax = maximum; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax; }
    static RectTransform Panel(Transform parent, string name, Color color)
    { var rect = Rect(parent, name); var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = false; return rect; }
    static TMP_Text Label(Transform parent, string name, string caption, TMP_FontAsset font, float size, Vector2 anchor, Vector2 position, Vector2 dimensions, Color color)
    {
        var rect = Rect(parent, name); rect.anchorMin = rect.anchorMax = rect.pivot = anchor; rect.anchoredPosition = position; rect.sizeDelta = dimensions;
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>(); text.font = font; text.fontSize = size; text.color = color; text.text = caption;
        text.raycastTarget = false; text.alignment = TextAlignmentOptions.TopLeft; text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }
    static UnityEngine.UI.Button Button(Transform parent, string name, string caption, TMP_FontAsset font, Vector2 anchor, Vector2 position, Vector2 dimensions, out TMP_Text label)
    {
        var rect = Panel(parent, name, ButtonColor); rect.anchorMin = rect.anchorMax = rect.pivot = anchor; rect.anchoredPosition = position; rect.sizeDelta = dimensions;
        var image = rect.GetComponent<UnityEngine.UI.Image>(); image.raycastTarget = true;
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
        var colors = button.colors; colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f); colors.pressedColor = new Color(.74f, .83f, .75f); colors.disabledColor = new Color(.5f, .5f, .5f, .65f); button.colors = colors;
        label = Label(rect, "Caption", caption, font, 18, new Vector2(.5f, .5f), Vector2.zero, dimensions - new Vector2(20, 12), Color.white);
        Stretch(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(10, 6), new Vector2(-10, -6)); label.alignment = TextAlignmentOptions.Center;
        return button;
    }
    static Material Material(string name, Color color)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path); if (existing) return existing;
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", .18f);
        AssetDatabase.CreateAsset(material, path); return material;
    }
    static GameObject Primitive(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material; UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>()); return go;
    }
    static void Light(string name, Vector3 position, Color color, float intensity)
    {
        var go = new GameObject(name, typeof(UnityEngine.Light)); go.transform.position = position; go.transform.LookAt(Vector3.zero);
        var light = go.GetComponent<UnityEngine.Light>(); light.type = LightType.Directional; light.color = color; light.intensity = intensity; light.shadows = LightShadows.Soft;
    }
}
