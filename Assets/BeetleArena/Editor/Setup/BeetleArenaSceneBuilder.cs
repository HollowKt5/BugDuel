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

// 通过 Unity Editor API 生成可运行的甲虫对战场景、关节化预制体、材质和 HUD。
// 模型交给 BeetleImportedModelBuilder 按 bug.fbx 建立骨架；场景通过 Unity API 生成。
public static class BeetleArenaSceneBuilder
{
    // 场景与生成资源的固定目标路径；重新 Build 会覆盖这些生成结果，应把手工定制另行管理。
    public const string ScenePath = "Assets/Scenes/BeetleArena.unity";
    public const string AssetFolder = "Assets/BeetleArena/Generated";
    // 双方 UI/阵营标记的主题色；身体材质的个体色由 Actor.Initialize 路径设置。
    static readonly Color Amber = new Color(.91f, .58f, .25f);
    static readonly Color Teal = new Color(.37f, .76f, .70f);
    // 本次构建复用的字体、填充条 Sprite 和台面碰撞体引用；不是运行时单例服务。
    static TMP_FontAsset font;
    static Sprite whiteSprite;
    static Collider[] stageContacts;

    // 只补字体字形、校验并保存参数资产，不重新生成场景层级；用于新增动作标签后刷新资源。
    public static void RefreshPresentation()
    {
        MakeFont();
        var settings = AssetDatabase.LoadAssetAtPath<BeetleMatchSettings>(AssetFolder + "/MatchSettings.asset");
        if (settings) { settings.Validate(); EditorUtility.SetDirty(settings); }
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Beetle Arena/Build Game Scene")]
    // 主生成流程：准备资源→建台面和相机→建双方 Actor→绑定控制器/HUD/输入→保存场景与预制体。
    // 使用临时 Additive 场景并在 finally 恢复原活动场景；还会设置 Play 启动场景和构建场景列表。
    public static void Build()
    {
        // TMP 必需资源尚未导入时发起导入并退出；待导入完成再重试，避免创建无字体引用的 UI。
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
        // 获取原始 FBX；保持源文件不变，生成可复用的骨架和网格派生资产。
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
            // 查找项目已有的无 RendererFeature 的 URP 渲染器，只为本场景相机选择，减少默认描边影响。
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

            // 在编辑器中把所有运行时依赖显式绑定，避免开始游戏后反复按名称查找对象。
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

            // 生成可在编辑器直接看到的初始姿态；进入 Play 后控制器仍会按实际种子重新创建本局。
            BeetleArenaTerrainSetup.Configure(controller);
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
            // 保存第一只的关节化结构为可复用预制体；运行时外观差异由绑定状态/材质属性决定。
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

    // 从正式模型生成骨架 Actor，额外绑定原游戏阵营标记。
    static BeetleActor BuildActor(GameObject source, int index, BeetleMatchSettings settings)
    {
        var actor = BeetleImportedModelBuilder.BuildActor(source, index);
        var marker = Primitive("Team identification disc", PrimitiveType.Cylinder, new Vector3(0, .016f, 0), new Vector3(1.65f, .006f, 1.65f), Material(index == 0 ? "Amber marker" : "Teal marker", index == 0 ? Amber * .7f : Teal * .65f, .2f));
        marker.transform.SetParent(actor.transform, true);
        actor.teamMarker = marker.transform;
        return actor;
    }

    // 建立树桩台面、年轮、边沿纹理、地面和环境灯光；只有两块主要接触体参与环境分离。
    static void BuildStage(float radius, float groundHeight)
    {
        var wood = Material("Cut wood", new Color(.40f, .265f, .14f), .24f);
        var bark = Material("Bark", new Color(.15f, .10f, .064f), .12f);
        var moss = Material("Forest floor", new Color(.08f, .12f, .072f), .08f);
        var stump = Primitive("Stump - ring-out surface", PrimitiveType.Cylinder, new Vector3(0, -.50f, 0), new Vector3(radius * 2, .5f, radius * 2), bark);
        var stumpVolume = new GameObject("Arena solid contact volume");
        stumpVolume.transform.SetParent(stump.transform, false);
        // 碰撞体顶部略抬高以覆盖可见木面/年轮，避免脚踩到装饰层下面。
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
        // 厚地面向下延伸，让落下的细角/足部从上方脱离重叠，避免薄板下表面成为最近分离方向。
        groundCollider.center = new Vector3(0, -50, 0); groundCollider.size = new Vector3(10, 100, 10); groundCollider.isTrigger = true;
        stageContacts = new Collider[] { stumpCollider, groundCollider };
        // 环境石块独立固定种子，重建场景保持一致，也不消耗比赛随机源。
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

    // 构建 Canvas/uGUI 界面并写入运行时引用：双方状态卡、鼓励按钮、阶段提示和结算面板。
    // 使用相机空间 Canvas 与 1600×900 参考尺寸；坐标由锚点、像素偏移和大小共同确定。
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
        // 左右两套卡片共用布局逻辑，通过锚点和 pivot 镜像定位，索引与两名选手保持一致。
        hud.fighters = new FighterHud[2];
        for (int i = 0; i < 2; i++)
        {
            Color accent = i == 0 ? Amber : Teal;
            Vector2 anchor = new Vector2(i, 1);
            Vector2 position = new Vector2(i == 0 ? 34 : -34, -34);
            var card = Panel("Fighter " + i, root, anchor, position, new Vector2(355, 196), new Color(.042f, .062f, .052f, .95f));
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
        Label("Rules", root, "只可加油，不能操控 · 每次鼓励持续 3.5 秒 / 冷却 6 秒\n掉出木台即负 · 无伤害血条 · 允许双方同时落台", new Vector2(.5f, 0), new Vector2(0, 48), new Vector2(700, 47), 15, new Color(.73f, .78f, .69f), TextAlignmentOptions.Center);
        hud.pauseButton = Button("Pause observation", root, new Vector2(1, .5f), new Vector2(-34, 0), new Vector2(138, 38), new Color(.20f, .28f, .23f), "暂停 [空格]", out hud.pauseText);
        hud.pauseButton.GetComponent<RectTransform>().pivot = new Vector2(1, .5f);
        hud.pauseText.color = new Color(.90f, .92f, .82f);
        var result = Panel("Match result", root, new Vector2(.5f, .48f), Vector2.zero, new Vector2(500, 245), new Color(.04f, .065f, .05f, .98f));
        result.pivot = new Vector2(.5f, .5f); hud.resultPanel = result.gameObject;
        Label("Result kicker", result, "RING OUT  /  落台结算", new Vector2(.5f, 1), new Vector2(0, -27), new Vector2(440, 28), 17, Amber, TextAlignmentOptions.Center);
        hud.resultText = Label("Result", result, "琥珀 获胜", new Vector2(.5f, 1), new Vector2(0, -72), new Vector2(460, 58), 38, Color.white, TextAlignmentOptions.Center);
        hud.rematchButton = Button("Rematch", result, new Vector2(0, 0), new Vector2(24, 24), new Vector2(210, 48), new Color(.24f, .32f, .26f), "原阵容再战", out var rematchLabel);
        rematchLabel.color = new Color(.90f, .92f, .82f);
        hud.nextButton = Button("Next match", result, new Vector2(1, 0), new Vector2(-24, 24), new Vector2(218, 48), Amber, "下一场 · 随机配对", out _);
        hud.nextButton.GetComponent<RectTransform>().pivot = new Vector2(1, 0);
        // 初始隐藏结算界面，由 HUD.Refresh 根据 Finished 阶段决定显示。
        result.gameObject.SetActive(false);
        return hud;
    }

    // UI 基础工厂：设置父节点、固定锚点、pivot、像素偏移和尺寸；parent 下不保留世界变换。
    static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false); rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor; rt.anchoredPosition = position; rt.sizeDelta = size;
        return rt;
    }
    // 在 RectTransform 上添加背景 Image，默认不拦截鼠标，只有按钮背景重新开启射线目标。
    static RectTransform Panel(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color color)
    {
        var rt = Rect(name, parent, anchor, position, size);
        var image = rt.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = false;
        return rt;
    }
    // 创建 TMP 标签并绑定中文字体；使用固定字号与省略号溢出策略，文本本身不挡点击。
    static TMP_Text Label(string name, Transform parent, string value, Vector2 anchor, Vector2 position, Vector2 size, float fontSize, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
    {
        var rt = Rect(name, parent, anchor, position, size);
        var text = rt.gameObject.AddComponent<TextMeshProUGUI>(); text.font = font; text.fontSize = fontSize;
        text.text = value; text.color = color; text.alignment = alignment; text.raycastTarget = false;
        text.enableAutoSizing = false; text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }
    // 生成背景与水平 Filled 前景，返回前景 Image 给 HUD 控制 fillAmount。
    static UnityEngine.UI.Image Bar(string name, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        var back = Panel(name, parent, new Vector2(0, 1), position, size, new Color(.15f, .19f, .16f));
        var fill = Panel("Fill", back, new Vector2(0, 1), Vector2.zero, size, color).GetComponent<UnityEngine.UI.Image>();
        fill.sprite = whiteSprite; fill.type = UnityEngine.UI.Image.Type.Filled;
        fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal; fill.fillOrigin = 0; fill.fillAmount = 1;
        return fill;
    }
    // 创建可点击背景、按钮过渡色和居中文字；out 参数把标签交给 HUD 动态改文案。
    static UnityEngine.UI.Button Button(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color color, string caption, out TMP_Text text)
    {
        var rt = Panel(name, parent, anchor, position, size, color);
        rt.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
        var button = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
        var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f); colors.pressedColor = new Color(.75f, .75f, .75f); colors.disabledColor = new Color(.6f, .6f, .6f, .8f); button.colors = colors;
        text = Label("Caption", rt, caption, new Vector2(.5f, .5f), Vector2.zero, size - new Vector2(12, 8), 20, new Color(.035f, .065f, .05f), TextAlignmentOptions.Center);
        return button;
    }

    // 创建或补充静态中文 TMP 字体。临时启用系统字体动态填字，保存时恢复 Static，
    // 并把材质/图集保存为子资源。依赖本机 Microsoft YaHei 或 Microsoft YaHei UI。
    // 注意：当前按全部 C# 源文本收集字符，包含注释；大量中文注释在下次运行本方法时也会进入字形集合。
    static TMP_FontAsset MakeFont()
    {
        string path = AssetFolder + "/ArenaCJK.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (existing)
        {
            string updatedCharacters = string.Concat(Directory.GetFiles("Assets/BeetleArena", "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
            // 沿用原字体资产 GUID，补字后标记图集与材质脏状态，避免重建字体导致已有 UI 引用失效。
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
    // 生成并缓存 2×2 白色 Sprite，供 Image 的水平填充模式使用；纹理作为 Sprite 子资源保存。
    static Sprite MakeWhiteSprite()
    {
        string path = AssetFolder + "/HudPixel.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing) return existing;
        var texture = new Texture2D(2, 2); texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); texture.Apply();
        var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f)); sprite.name = "HUD solid fill";
        AssetDatabase.CreateAsset(sprite, path); AssetDatabase.AddObjectToAsset(texture, sprite); return sprite;
    }
    // 仅在资源目录不存在时通过 AssetDatabase 创建，确保 Unity 同步生成目录元数据。
    static void Folder(string parent, string name) { if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name); }
    // 按名称复用生成目录里的材质，更新基础色和卡通预设并标记保存。
    static Material Material(string name, Color color, float smoothness)
    {
        string path = AssetFolder + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", color);
        // 重建场景仍走同一套卡通材质预设，避免生成器把效果改回 Lit。
        BeetleArenaToonSetup.Configure(material);
        EditorUtility.SetDirty(material); return material;
    }
    // 创建可见基本几何体并移除默认碰撞体；接触体随后集中配置，避免重复参与碰撞。
    static GameObject Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.position = position; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material; UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>()); return go;
    }
    // 创建朝向世界原点的方向光并启用软阴影；位置仅用于确定朝向，不产生距离衰减。
    static void Light(string name, Vector3 position, Color color, float intensity)
    {
        var light = new GameObject(name).AddComponent<UnityEngine.Light>(); light.type = LightType.Directional;
        light.transform.position = position; light.transform.LookAt(Vector3.zero); light.color = color; light.intensity = intensity; light.shadows = LightShadows.Soft;
    }
}

