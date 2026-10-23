using System;
using System.Reflection;
using BeetleArena;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[CustomEditor(typeof(RetroUiAudioProfile))]
public sealed class RetroUiAudioProfileEditor : Editor
{
    static readonly Type AudioUtility = typeof(Editor).Assembly.GetType("UnityEditor.AudioUtil");
    static readonly MethodInfo PlayPreview = AudioUtility?.GetMethod("PlayPreviewClip", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
        null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
    static readonly MethodInfo StopPreview = AudioUtility?.GetMethod("StopAllPreviewClips", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
        null, Type.EmptyTypes, null);
    AudioClip preview;
    HelpBox status;
    public static bool CanPreview => PlayPreview != null && StopPreview != null;

    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();
        root.Add(new HelpBox("调高「音高」会更尖，调高「明亮度」会更脆。缩短「尾音衰减」会更干。「咯 → 哒间隔」默认 60 毫秒，越大两段越分明。运行中修改，下一次 UI 点击即生效。", HelpBoxMessageType.Info));
        void Field(string property, string label) => root.Add(new PropertyField(serializedObject.FindProperty(property), label));
        Field("volume", "音量");
        Field("pitch", "音高 / 尖锐程度");
        Field("brightness", "明亮度 / 清脆程度");
        Field("attackMs", "起音时间（毫秒）");
        Field("decayMs", "尾音衰减（毫秒）");
        Field("noise", "摩擦噪声比例");
        Field("reboundMs", "咯 → 哒间隔（毫秒）");
        Field("reboundStrength", "第二下「哒」力度");
        var buttons = new VisualElement();
        buttons.style.flexDirection = FlexDirection.Row;
        var listen = new Button(Preview) { text = "试听" }; listen.style.flexGrow = 1; listen.SetEnabled(CanPreview);
        buttons.Add(listen);
        var stop = new Button(ReleasePreview) { text = "停止" }; stop.style.flexGrow = 1;
        buttons.Add(stop); root.Add(buttons);
        root.Add(new Button(() =>
        {
            Undo.RecordObject(target, "Reset UI click sound");
            ((RetroUiAudioProfile)target).ResetToDefaults();
            EditorUtility.SetDirty(target);
            serializedObject.Update();
        }) { text = "恢复清脆默认值（可撤销）" });
        root.Add(new Button(() =>
        {
            serializedObject.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(target);
            status.text = "参数已保存。";
            status.messageType = HelpBoxMessageType.Info;
        }) { text = "保存参数" });
        status = new HelpBox(CanPreview ? "无需运行游戏即可试听。试听使用这里的音量；游戏内还受主音量控制。" :
            "当前 Unity 版本没有可用的音频预览接口；仍可在游戏内试听。", CanPreview ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning);
        root.Add(status);
        return root;
    }

    void Preview()
    {
        try
        {
            ReleasePreview();
            serializedObject.ApplyModifiedProperties();
            preview = MouseClickSynthesizer.CreateClip(((RetroUiAudioProfile)target).Settings);
            PlayPreview.Invoke(null, new object[] { preview, 0, false });
            status.text = "已试听当前参数。";
            status.messageType = HelpBoxMessageType.Info;
        }
        catch (Exception ex)
        {
            status.text = "试听失败：" + ex.GetBaseException().Message;
            status.messageType = HelpBoxMessageType.Error;
            ReleasePreview();
        }
    }

    void ReleasePreview()
    {
        if (!preview) return;
        try { StopPreview?.Invoke(null, null); }
        finally { DestroyImmediate(preview); preview = null; }
    }
    void OnDisable() => ReleasePreview();
}

public static class RetroUiAudioSetup
{
    public const string ProfilePath = "Assets/BeetleArena/Resources/UiClickSound.asset";

    [MenuItem("Tools/Beetle Arena/UI Click Sound Settings")]
    public static void SelectSettings()
    {
        Selection.activeObject = EnsureProfile();
        EditorGUIUtility.PingObject(Selection.activeObject);
    }

    public static RetroUiAudioProfile EnsureProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<RetroUiAudioProfile>(ProfilePath);
        if (!profile)
        {
            profile = ScriptableObject.CreateInstance<RetroUiAudioProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
            AssetDatabase.SaveAssetIfDirty(profile);
        }
        return profile;
    }
}
