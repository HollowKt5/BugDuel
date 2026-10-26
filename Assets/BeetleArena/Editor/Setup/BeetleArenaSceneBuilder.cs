using System;
using System.IO;
using System.Linq;
using BeetleArena;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Generates runnable beetle combat scene, articulated prefabs, materials, and HUD via Unity Editor APIs.
// Delegates model rigging to BeetleImportedModelBuilder based on bug.fbx; constructs scene via Unity APIs.
public static class BeetleArenaSceneBuilder
{
    // Target paths for scenes and generated assets; rebuilding overwrites these generated files, so custom tweaks should be managed separately.
    public const string ScenePath = "Assets/Scenes/BeetleArena.unity";
    public const string AssetFolder = "Assets/BeetleArena/Generated";
    // Theme colors for contestant UI/faction markers; individual body material colors are assigned via Actor.Initialize.
    static readonly Color Amber = new Color(.91f, .58f, .25f);
    static readonly Color Teal = new Color(.37f, .76f, .70f);
    // Fonts, fill bar Sprites, and arena colliders reused during this build pass; not a runtime singleton service.
    static TMP_FontAsset font;
    static Sprite whiteSprite;
    static Collider[] stageContacts;

    // Supplements font glyphs, validates, and saves settings assets without regenerating scene hierarchy; refreshes assets after adding motion tags.
    public static void RefreshPresentation()
    {
        MakeFont();
        var settings = AssetDatabase.LoadAssetAtPath<BeetleMatchSettings>(AssetFolder + "/MatchSettings.asset");
        if (settings) { settings.Validate(); EditorUtility.SetDirty(settings); }
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/甲壳虫斗/场景与安装/生成战斗场景", false, 200)]
    // Main generation pipeline: prepare assets -> build arena & camera -> spawn contestants -> bind controller/HUD/input -> save scene & prefabs.
    // Uses a temporary Additive scene and restores original active scene in finally; configures Playmode scene and Build Settings scene list.
    public static void Build()
    {
        // Prompts import and exits if essential TMP assets are missing; retries after import to avoid creating UI without font references.
        if (!TMP_Settings.instance)
        {
            TMP_PackageResourceImporter.ImportResources(true, false, false);
            throw new InvalidOperationException("TMP essentials are being imported. Run Build again once import completes.");
        }
        Folder("Assets/BeetleArena", "Generated");
        Folder(AssetFolder, "Materials");
        font = MakeFont();
        whiteSprite = MakeWhiteSprite();
        var settings = AssetDatabase.LoadAssetAtPath<BeetleMatchSettings>(AssetFolder + "/MatchSettings.asset");
        if (!settings)
        {
            settings = ScriptableObject.CreateInstance<BeetleMatchSettings>();
            AssetDatabase.CreateAsset(settings, AssetFolder + "/MatchSettings.asset");
        }
        settings.Validate();
        // Retrieves raw FBX; preserves source file while generating reusable skeletal rig and mesh derivative assets.
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/bug.fbx");
        if (!source) throw new InvalidOperationException("The imported bug.fbx model is missing.");

        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            BuildStage(settings.arenaRadius, settings.groundHeight);
            var cameraObject = new GameObject("Spectator Camera", typeof(Camera), typeof(AudioListener), typeof(ArenaCamera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.075f, .11f, .085f);
            // This project already has a clean renderer alongside its default
            // fullscreen outline renderer. Select it only for this scene's camera.
            // Finds existing URP renderer without RendererFeatures specifically for scene camera, reducing default outline interference.
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline)
            {
                var renderers = new SerializedObject(pipeline).FindProperty("m_RendererDataList");
                for (int i = 0; i < renderers.arraySize; i++)
                {
                    var data = renderers.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;
                    if (data && data.rendererFeatures.Count == 0) { camera.GetUniversalAdditionalCameraData().SetRenderer(i); break; }
                }
            }
            var spectator = cameraObject.GetComponent<ArenaCamera>();
            spectator.ResetView();

            // Binds all runtime dependencies explicitly in editor, avoiding repeated runtime GameObject.Find queries.
            var controller = new GameObject("Match Director").AddComponent<BeetleMatchController>();
            controller.gameObject.AddComponent<ArenaStandaloneSmoke>();
            controller.settings = settings;
            controller.arenaContacts = stageContacts;
            controller.spectatorCamera = spectator;
            controller.audioFeedback = new GameObject("Contact Audio").AddComponent<ArenaAudio>();
            controller.actors = new BeetleActor[2];
            for (int i = 0; i < 2; i++) controller.actors[i] = BuildActor(source, i, settings);
            settings.supportFootOffsets = controller.actors[0].animator.legs.Select(l => l.restAnkle).ToArray();
            EditorUtility.SetDirty(settings);
            controller.hud = BuildHud(camera);
            var eventObject = new GameObject("Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

            // Spawns resting initial pose visible in Editor; MatchController re-rolls actual in-match lineup upon entering Play mode.
            BeetleArenaTerrainSetup.Configure(controller);
            BeetleCombatUpgrade.Configure(controller);
            var simulation = new BeetleSimulation(settings, settings.initialSeed);
            simulation.SetGround(controller.ActiveSurface);
            for (int i = 0; i < 2; i++)
            {
                controller.actors[i].Bind(simulation.Fighters[i], settings, controller.ActiveSurface);
                controller.actors[i].Render(1, 0, .02f);
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.32f, .40f, .39f);
            RenderSettings.ambientEquatorColor = new Color(.20f, .23f, .16f);
            RenderSettings.ambientGroundColor = new Color(.12f, .09f, .06f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(.075f, .11f, .085f);
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = .012f;
            BeetleFeedbackSetup.Configure(controller);
            BeetleSceneHierarchy.Organize(controller);
            EditorSceneManager.SaveScene(scene, ScenePath);
            // Saves first contestant's articulated rig as reusable prefab; runtime visual differentiation handled by bound state / material properties.
            PrefabUtility.SaveAsPrefabAsset(controller.actors[0].gameObject, AssetFolder + "/ArticulatedBeetle.prefab");
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("Beetle Arena built: " + ScenePath);
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    // Constructs skeletal Actor from production model, binding original game faction markers.
    static BeetleActor BuildActor(GameObject source, int index, BeetleMatchSettings settings)
    {
        var actor = BeetleImportedModelBuilder.BuildActor(source, index);
        var marker = Primitive("Team identification disc", PrimitiveType.Cylinder, new Vector3(0, .016f, 0), new Vector3(1.65f, .006f, 1.65f), Material(index == 0 ? "Amber marker" : "Teal marker", index == 0 ? Amber * .7f : Teal * .65f, .2f));
        marker.transform.SetParent(actor.transform, true);
        actor.teamMarker = marker.transform;
        return actor;
    }

    // Builds stump arena, annual rings, bark trim, floor, and environment lights; only two primary colliders participate in environment separation.
    static void BuildStage(float radius, float groundHeight)
    {
        var wood = Material("Cut wood", new Color(.40f, .265f, .14f), .24f);
        var bark = Material("Bark", new Color(.15f, .10f, .064f), .12f);
        var moss = Material("Forest floor", new Color(.08f, .12f, .072f), .08f);
        var stump = Primitive("Stump - ring-out surface", PrimitiveType.Cylinder, new Vector3(0, -.50f, 0), new Vector3(radius * 2, .5f, radius * 2), bark);
        var stumpVolume = new GameObject("Arena solid contact volume");
        stumpVolume.transform.SetParent(stump.transform, false);
        // Slightly elevates collider top to encompass visible wood surface / rings, preventing tarsi from stepping below decorative surfaces.
        stumpVolume.transform.localPosition = new Vector3(0, .03f, 0);
        var stumpCollider = stumpVolume.AddComponent<MeshCollider>();
        stumpCollider.sharedMesh = stump.GetComponent<MeshFilter>().sharedMesh;
        stumpCollider.convex = true; stumpCollider.isTrigger = true;
        Primitive("Wood surface", PrimitiveType.Cylinder, new Vector3(0, -.026f, 0), new Vector3(radius * 2, .025f, radius * 2), wood);
        for (int i = 0; i < 7; i++)
        {
            float r = radius - .17f - i * (radius / 8);
            var ring = Material("Growth ring " + i, Color.Lerp(new Color(.34f, .215f, .11f), new Color(.46f, .31f, .17f), i % 2 == 0 ? .25f : .75f), .22f);
            Primitive("Annual growth ring " + i, PrimitiveType.Cylinder, new Vector3(0, -.005f + i * .0015f, 0), new Vector3(r * 2, .003f, r * 2), ring);
        }
        var rim = Material("Rim marks", new Color(.67f, .50f, .28f), .28f);
        for (int i = 0; i < 48; i++)
        {
            float angle = i * Mathf.PI * 2 / 48;
            Vector3 p = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
            var strip = Primitive("Bark ridge " + i, PrimitiveType.Cube, p * (radius - .035f) + Vector3.down * .52f,
                new Vector3(.32f, .91f + Mathf.Sin(i * 7) * .08f, .15f), bark);
            strip.transform.rotation = Quaternion.Euler(0, angle * Mathf.Rad2Deg, Mathf.Sin(i * 3) * 3);
            var tick = Primitive("Flush rim tick " + i, PrimitiveType.Cube, p * (radius - .12f) + Vector3.up * .009f, new Vector3(.045f, .012f, .12f), rim);
            tick.transform.rotation = Quaternion.Euler(0, angle * Mathf.Rad2Deg, 0);
        }
        var ground = Primitive("Forest ground", PrimitiveType.Plane, new Vector3(0, groundHeight, 0), new Vector3(20, 1, 20), moss);
        var groundCollider = ground.AddComponent<BoxCollider>();
        // A deep solid ground prevents a falling/rolling thin appendage from
        // choosing the underside of a thin floor as its nearest escape direction.
        // Floor extends downwards so fallen horns/tarsi separate upwards, preventing thin floor undersides from becoming nearest separation normal.
        groundCollider.center = new Vector3(0, -50, 0); groundCollider.size = new Vector3(10, 100, 10); groundCollider.isTrigger = true;
        stageContacts = new Collider[] { stumpCollider, groundCollider };
        // Environment rocks seeded with fixed RNG, maintaining consistency on scene rebuilds without consuming combat RNG.
        var random = new System.Random(88);
        var stone = Material("Forest stones", new Color(.16f, .19f, .15f), .08f);
        for (int i = 0; i < 18; i++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2;
            float r = 7 + (float)random.NextDouble() * 6;
            float s = .3f + (float)random.NextDouble() * .65f;
            Primitive("Forest stone " + i, PrimitiveType.Sphere, new Vector3(Mathf.Sin(angle) * r, groundHeight + .1f, Mathf.Cos(angle) * r), new Vector3(s * 1.5f, s, s), stone);
        }
        Light("Warm canopy light", new Vector3(-4, 10, -6), new Color(1, .82f, .57f), 2.1f);
        Light("Cool forest rim", new Vector3(5, 7, 5), new Color(.62f, .79f, .9f), 1.1f);
    }

    // Builds Canvas / uGUI hierarchy and writes runtime references: fighter cards, encourage buttons, phase banners, and result screen.
    // Uses Camera-space Canvas with 1600x900 reference resolution; coordinates defined by anchors, pixel offsets, and dimensions.
    static BeetleArenaHud BuildHud(Camera camera)
    {
        var go = new GameObject("Spectator HUD", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster), typeof(BeetleArenaHud));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
        var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
        var hud = go.GetComponent<BeetleArenaHud>();
        var root = go.transform;
        Label("Title", root, "甲角 · 林间擂台", new Vector2(.5f, 1), new Vector2(0, -39), new Vector2(500, 45), 30, new Color(.94f, .88f, .70f), TextAlignmentOptions.Center);
        Label("Subtitle", root, "FOREST DUEL  /  自主格斗观察席", new Vector2(.5f, 1), new Vector2(0, -78), new Vector2(500, 30), 15, new Color(.65f, .72f, .66f), TextAlignmentOptions.Center);
        hud.clock = Label("Match clock", root, "00:00", new Vector2(.5f, 1), new Vector2(0, -123), new Vector2(220, 45), 31, Color.white, TextAlignmentOptions.Center);
        hud.roundText = Label("Round", root, "第 01 场 · 体型差不超过 8%", new Vector2(.5f, 1), new Vector2(0, -164), new Vector2(470, 30), 16, new Color(.74f, .75f, .66f), TextAlignmentOptions.Center);
        // Left and right contestant cards share layout logic, mirrored via anchors and pivots, matching contestant indexing.
        hud.fighters = new FighterHud[2];
        for (int i = 0; i < 2; i++)
        {
            Color accent = i == 0 ? Amber : Teal;
            Vector2 anchor = new Vector2(i, 1);
            Vector2 position = new Vector2(i == 0 ? 34 : -34, -34);
            var card = Panel("Fighter " + i, root, anchor, position, new Vector2(355, 250), new Color(.042f, .062f, .052f, .95f));
            card.pivot = new Vector2(i, 1);
            var ui = new FighterHud(); hud.fighters[i] = ui;
            ui.name = Label("Name", card, i == 0 ? "01 / 琥珀" : "02 / 墨甲", new Vector2(0, 1), new Vector2(20, -12), new Vector2(310, 42), 24, accent);
            ui.measurements = Label("Specimen stats", card, "体长 55.0 mm    体重 8.0 g", new Vector2(0, 1), new Vector2(20, -57), new Vector2(320, 25), 16, new Color(.79f, .79f, .69f));
            ui.staminaText = Label("Stamina label", card, "体力 100%", new Vector2(0, 1), new Vector2(20, -89), new Vector2(145, 23), 15, Color.white);
            ui.footingText = Label("Footing label", card, "抓地 / 稳定 100%", new Vector2(0, 1), new Vector2(190, -89), new Vector2(155, 23), 15, Color.white);
            ui.staminaFill = Bar("Stamina", card, new Vector2(20, -120), new Vector2(143, 7), accent);
            ui.footingFill = Bar("Footing", card, new Vector2(190, -120), new Vector2(143, 7), accent * .7f);
            ui.action = Label("Current action", card, "触角 / 角尖试探", new Vector2(0, 1), new Vector2(20, -144), new Vector2(320, 27), 18, new Color(.90f, .90f, .81f));
            var button = Button(i == 0 ? "Cheer Amber" : "Cheer Ink", root, new Vector2(i, 0), new Vector2(i == 0 ? 34 : -34, 30), new Vector2(355, 72), accent, i == 0 ? "Q   为琥珀加油" : "E   为墨甲加油", out var label);
            button.GetComponent<RectTransform>().pivot = new Vector2(i, 0);
            ui.cheerButton = button; ui.cheerText = label;
            ui.cheerFill = Bar("Cheer cooldown", button.transform, new Vector2(12, -63), new Vector2(331, 4), new Color(.13f, .22f, .18f));
        }
        hud.phaseText = Label("Match phase", root, "准备 3", new Vector2(.5f, 0), new Vector2(0, 133), new Vector2(500, 30), 19, new Color(.93f, .79f, .47f), TextAlignmentOptions.Center);
        hud.eventText = Label("Commentary", root, "触角试探 · 即将开始", new Vector2(.5f, 0), new Vector2(0, 99), new Vector2(700, 33), 21, Color.white, TextAlignmentOptions.Center);
        Label("Rules", root, "只有落台分胜负 · 时间到双方平局\n单局含开场与击败展示不超过 3 分钟（暂停除外）", new Vector2(.5f, 0), new Vector2(0, 48), new Vector2(700, 47), 15, new Color(.73f, .78f, .69f), TextAlignmentOptions.Center);
        hud.pauseButton = Button("Pause observation", root, new Vector2(1, .5f), new Vector2(-34, 0), new Vector2(230, 38), new Color(.20f, .28f, .23f), "暂停 [空格]", out hud.pauseText);
        hud.pauseButton.GetComponent<RectTransform>().pivot = new Vector2(1, .5f);
        hud.pauseText.color = new Color(.90f, .92f, .82f);
        var result = Panel("Match result", root, new Vector2(.5f, .48f), Vector2.zero, new Vector2(500, 245), new Color(.04f, .065f, .05f, .98f));
        result.pivot = new Vector2(.5f, .5f); hud.resultPanel = result.gameObject;
        Label("Result kicker", result, "MATCH RESULT  /  本场结算", new Vector2(.5f, 1), new Vector2(0, -27), new Vector2(440, 28), 17, Amber, TextAlignmentOptions.Center);
        hud.resultText = Label("Result", result, "琥珀 获胜", new Vector2(.5f, 1), new Vector2(0, -72), new Vector2(460, 100), 38, Color.white, TextAlignmentOptions.Center);
        hud.rematchButton = Button("Rematch", result, new Vector2(0, 0), new Vector2(24, 24), new Vector2(210, 48), new Color(.24f, .32f, .26f), "原阵容再战", out var rematchLabel);
        rematchLabel.color = new Color(.90f, .92f, .82f);
        hud.nextButton = Button("Next match", result, new Vector2(1, 0), new Vector2(-24, 24), new Vector2(218, 48), Amber, "下一场 · 随机配对", out _);
        hud.nextButton.GetComponent<RectTransform>().pivot = new Vector2(1, 0);
        // Settlement panel is hidden on launch; HUD.Refresh determines visibility based on Finished phase.
        result.gameObject.SetActive(false);
        return hud;
    }

    // UI primitive factory: sets parent, fixed anchors, pivot, pixel offsets, and size; parent does not retain world transforms.
    static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false); rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor; rt.anchoredPosition = position; rt.sizeDelta = size;
        return rt;
    }
    // Adds background Image to RectTransform; raycastTarget false by default, enabled only on button backgrounds.
    static RectTransform Panel(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color color)
    {
        var rt = Rect(name, parent, anchor, position, size);
        var image = rt.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = false;
        return rt;
    }
    // Spawns TMP label and binds Chinese font; uses fixed font size and ellipsis overflow; text does not block raycasts.
    static TMP_Text Label(string name, Transform parent, string value, Vector2 anchor, Vector2 position, Vector2 size, float fontSize, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
    {
        var rt = Rect(name, parent, anchor, position, size);
        var text = rt.gameObject.AddComponent<TextMeshProUGUI>(); text.font = font; text.fontSize = fontSize;
        text.text = value; text.color = color; text.alignment = alignment; text.raycastTarget = false;
        text.enableAutoSizing = false; text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }
    // Generates background and horizontal Filled foreground Image for HUD fillAmount control.
    static UnityEngine.UI.Image Bar(string name, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        var back = Panel(name, parent, new Vector2(0, 1), position, size, new Color(.15f, .19f, .16f));
        var fill = Panel("Fill", back, new Vector2(0, 1), Vector2.zero, size, color).GetComponent<UnityEngine.UI.Image>();
        fill.sprite = whiteSprite; fill.type = UnityEngine.UI.Image.Type.Filled;
        fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal; fill.fillOrigin = 0; fill.fillAmount = 1;
        return fill;
    }
    // Spawns clickable background, button color transitions, and centered label; out parameter exposes label for dynamic HUD updates.
    static UnityEngine.UI.Button Button(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color color, string caption, out TMP_Text text)
    {
        var rt = Panel(name, parent, anchor, position, size, color);
        rt.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
        var button = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
        var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f); colors.pressedColor = new Color(.75f, .75f, .75f); colors.disabledColor = new Color(.6f, .6f, .6f, .8f); button.colors = colors;
        text = Label("Caption", rt, caption, new Vector2(.5f, .5f), Vector2.zero, size - new Vector2(12, 8), 20, new Color(.035f, .065f, .05f), TextAlignmentOptions.Center);
        return button;
    }

    // Creates or supplements static Chinese TMP font. Temporarily enables dynamic system font filling, restoring to Static on save,
    // saving material and atlas as sub-assets. Depends on Microsoft YaHei or Microsoft YaHei UI.
    // Note: Scans C# source text for characters; English comments keep atlas compact.
    public static TMP_FontAsset MakeFont()
    {
        string path = AssetFolder + "/ArenaCJK.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (existing)
        {
            string updatedCharacters = string.Concat(Directory.GetFiles("Assets/BeetleArena", "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
            // Preserves original font asset GUID and marks atlas/material dirty to avoid invalidating existing UI references on font rebuild.
            existing.atlasPopulationMode = AtlasPopulationMode.DynamicOS;
            existing.TryAddCharacters(new string(updatedCharacters.Distinct().ToArray()), out string missingUpdated);
            existing.atlasPopulationMode = AtlasPopulationMode.Static;
            foreach (var texture in existing.atlasTextures)
            {
                if (!texture) continue;
                if (!AssetDatabase.Contains(texture)) AssetDatabase.AddObjectToAsset(texture, existing);
                EditorUtility.SetDirty(texture);
            }
            EditorUtility.SetDirty(existing); EditorUtility.SetDirty(existing.material);
            return existing;
        }
        var asset = TMP_FontAsset.CreateFontAsset("Microsoft YaHei", "Regular", 48);
        if (!asset) asset = TMP_FontAsset.CreateFontAsset("Microsoft YaHei UI", "Regular", 48);
        if (!asset) throw new InvalidOperationException("Microsoft YaHei system font unavailable.");
        asset.name = "Arena CJK - static glyphs";
        string characters = string.Concat(Directory.GetFiles("Assets/BeetleArena", "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        asset.TryAddCharacters(new string(characters.Distinct().ToArray()), out string missing);
        asset.atlasPopulationMode = AtlasPopulationMode.Static;
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.AddObjectToAsset(asset.material, asset);
        foreach (var texture in asset.atlasTextures) AssetDatabase.AddObjectToAsset(texture, asset);
        EditorUtility.SetDirty(asset);
        return asset;
    }
    // Generates and caches 2x2 white Sprite for Image horizontal fill mode; texture is saved as a Sprite sub-asset.
    static Sprite MakeWhiteSprite()
    {
        string path = AssetFolder + "/HudPixel.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing) return existing;
        var texture = new Texture2D(2, 2); texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); texture.Apply();
        var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f)); sprite.name = "HUD solid fill";
        AssetDatabase.CreateAsset(sprite, path); AssetDatabase.AddObjectToAsset(texture, sprite); return sprite;
    }
    // Creates directory via AssetDatabase only if missing, ensuring Unity synchronizes folder metadata.
    static void Folder(string parent, string name) { if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name); }
    // Reuses materials in generated folder by name, updating base color and toon preset before marking dirty.
    static Material Material(string name, Color color, float smoothness)
    {
        string path = AssetFolder + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", color);
        // Scene rebuild retains identical toon material preset, preventing generator from reverting to Lit.
        BeetleArenaToonSetup.Configure(material);
        EditorUtility.SetDirty(material); return material;
    }
    // Spawns visible primitive geometry and removes default colliders; colliders configured centrally to prevent duplicate collisions.
    static GameObject Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.position = position; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material; UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>()); return go;
    }
    // Spawns directional light facing world origin with soft shadows; position defines direction only without distance attenuation.
    static void Light(string name, Vector3 position, Color color, float intensity)
    {
        var light = new GameObject(name).AddComponent<UnityEngine.Light>(); light.type = LightType.Directional;
        light.transform.position = position; light.transform.LookAt(Vector3.zero); light.color = color; light.intensity = intensity; light.shadows = LightShadows.Soft;
    }
}

