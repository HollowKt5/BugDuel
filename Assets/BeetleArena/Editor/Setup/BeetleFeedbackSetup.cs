using System;
using BeetleArena;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Incrementally assembles audio/sparks and settings panel. Preserves scene grouping without rebuilding beetles or existing HUD.</summary>
public static class BeetleFeedbackSetup
{
    [MenuItem("Tools/甲壳虫斗/场景与安装/战斗模块/安装接触反馈", false, 222)]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before installing feedback.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != BeetleArenaSceneBuilder.ScenePath) throw new InvalidOperationException("Open BeetleArena.unity.");
        Configure(BeetleSceneHierarchy.FindMatch(scene));
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
    }
    public static void Configure(BeetleMatchController match)
    {
        var audio = match.audioFeedback;
        var sparks = audio.GetComponent<HornClashSparks>(); if (!sparks) sparks = audio.gameObject.AddComponent<HornClashSparks>();
        const string matPath = "Assets/BeetleArena/Generated/Materials/HornSpark.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (!material)
        {
            var shader = Shader.Find("BeetleArena/HornSpark"); if (!shader) throw new Exception("HornSpark shader missing");
            material = new Material(shader); AssetDatabase.CreateAsset(material, matPath);
        }
        sparks.sparkMaterial = material;
        var feedback = audio.GetComponent<BeetleContactFeedback>(); if (!feedback) feedback = audio.gameObject.AddComponent<BeetleContactFeedback>();
        feedback.audioFeedback = audio; feedback.sparks = sparks; match.contactFeedback = feedback;
        var ui = match.hud.GetComponent<BeetleFeedbackSettings>(); if (!ui) ui = match.hud.gameObject.AddComponent<BeetleFeedbackSettings>();
        ui.sparks = sparks;
        if (!ui.openButton)
        {
            ui.openButton = Button(match.hud.pauseButton, match.hud.transform, "Settings", "设置", new Vector2(1, .5f), new Vector2(-34, -186), new Vector2(230, 38));
            var panel = Rect("Contact Settings", match.hud.transform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(410, 230));
            ui.panel = panel.gameObject;
            var background = panel.gameObject.AddComponent<UnityEngine.UI.Image>(); background.color = new Color(.035f, .055f, .043f, .98f);
            var font = match.hud.clock.font;
            Label("Title", panel, "设置", font, new Vector2(25, -20), new Vector2(350, 40), 25);
            var row = Rect("Horn Sparks Toggle", panel, new Vector2(0, 1), new Vector2(25, -78), new Vector2(360, 42));
            var hit = row.gameObject.AddComponent<UnityEngine.UI.Image>(); hit.color = new Color(.12f, .18f, .14f, 1);
            var toggle = row.gameObject.AddComponent<UnityEngine.UI.Toggle>(); ui.sparksToggle = toggle; toggle.targetGraphic = hit;
            var box = Rect("Box", row, new Vector2(0, .5f), new Vector2(12, 0), new Vector2(23, 23));
            var boxImage = box.gameObject.AddComponent<UnityEngine.UI.Image>(); boxImage.color = new Color(.3f, .36f, .28f); boxImage.raycastTarget = false;
            var check = Rect("Check", box, new Vector2(.5f, .5f), Vector2.zero, new Vector2(15, 15));
            var checkImage = check.gameObject.AddComponent<UnityEngine.UI.Image>(); checkImage.color = new Color(1, .76f, .32f); checkImage.raycastTarget = false;
            toggle.graphic = checkImage; toggle.isOn = true;
            Label("Toggle Label", row, "角斗碰撞火星", font, new Vector2(50, -8), new Vector2(285, 30), 20);
            Label("Description", panel, "兵器碰撞般的细小火星", font, new Vector2(25, -132), new Vector2(360, 27), 16);
            ui.closeButton = Button(match.hud.pauseButton, panel, "Close Settings", "完成", new Vector2(1, 0), new Vector2(-25, 18), new Vector2(100, 38));
            panel.gameObject.SetActive(false);
        }
        // Keep settings below view, slow-motion and nursery controls in the right column.
        var settingsRect=(RectTransform)ui.openButton.transform;
        settingsRect.anchoredPosition=new Vector2(-34,-186);settingsRect.sizeDelta=new Vector2(230,38);
        // Supplements glyphs for new strings only, avoiding expanding font atlas for code comments.
        var f = match.hud.clock.font; f.atlasPopulationMode = AtlasPopulationMode.DynamicOS;
        f.TryAddCharacters("设置角斗碰撞火星兵器般的细小完成", out string missing); f.atlasPopulationMode = AtlasPopulationMode.Static;
        foreach(var texture in f.atlasTextures) if(texture) { if(!AssetDatabase.Contains(texture)) AssetDatabase.AddObjectToAsset(texture,f); EditorUtility.SetDirty(texture); }
        EditorUtility.SetDirty(f); EditorUtility.SetDirty(f.material);
        if (!string.IsNullOrEmpty(missing)) throw new Exception("Missing settings glyphs: " + missing);
        EditorUtility.SetDirty(match); EditorUtility.SetDirty(ui); EditorUtility.SetDirty(sparks); EditorUtility.SetDirty(feedback);
    }
    static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false);
        r.anchorMin = r.anchorMax = r.pivot = anchor; r.anchoredPosition = position; r.sizeDelta = size; return r;
    }
    static void Label(string name, Transform parent, string content, TMP_FontAsset font, Vector2 position, Vector2 size, float fontSize)
    {
        var r = Rect(name, parent, new Vector2(0, 1), position, size); var t = r.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = font; t.text = content; t.fontSize = fontSize; t.color = new Color(.94f, .93f, .82f); t.raycastTarget = false;
    }
    static UnityEngine.UI.Button Button(UnityEngine.UI.Button source, Transform parent, string name, string label, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var b = UnityEngine.Object.Instantiate(source, parent); b.name = name; b.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        var r = b.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = r.pivot = anchor; r.anchoredPosition = position; r.sizeDelta = size;
        var text = b.GetComponentInChildren<TMP_Text>(); text.text = label; text.fontSize = 19;
        return b;
    }
}
