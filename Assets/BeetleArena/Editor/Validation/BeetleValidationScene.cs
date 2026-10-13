using System;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor 验证独占的临时场景与参数副本。用 using 包住整个验证（包括写报告），
/// 正常完成、断言失败或构造中途失败都会清理；绝不直接打开并关闭用户的原场景。
/// 动作模块资产仍是只读共享引用，测试若修改模块自身字段，须自行创建/销毁模块副本。
/// </summary>
public sealed class BeetleValidationScene : IDisposable
{
    readonly Scene previous;
    string path;
    bool ownsAsset, disposed;
    public Scene Scene { get; private set; }
    public BeetleMatchController Match { get; private set; }
    public BeetleMatchSettings Settings { get; private set; }

    public BeetleValidationScene(string name)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before validation.");
        previous = SceneManager.GetActiveScene();
        try
        {
            // 遇到上次遗留或用户已有同名资产时生成新路径，不覆盖、不删除已有文件。
            path = AssetDatabase.GenerateUniqueAssetPath("Assets/BeetleArena/Generated/" + name + ".unity");
            ownsAsset = AssetDatabase.CopyAsset(BeetleArenaSceneBuilder.ScenePath, path);
            if (!ownsAsset) throw new InvalidOperationException("Cannot copy validation scene: " + path);
            Scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(Scene);
            Match = BeetleSceneHierarchy.FindMatch(Scene);
            Settings = UnityEngine.Object.Instantiate(Match.settings);
            Match.settings = Settings;
            Match.hud.Bind(Match);
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (Scene.IsValid() && Scene.isLoaded)
            {
                // 普通 MonoBehaviour 在 Edit Mode 手动 Initialize 后未必收到 OnDestroy。
                // 先明确释放副本拥有的非资产资源，再关闭场景；即使释放失败也确保关场景。
                try
                {
                    foreach (var root in Scene.GetRootGameObjects())
                    {
                        foreach (var audio in root.GetComponentsInChildren<ArenaAudio>(true)) audio.ReleaseResources();
                        foreach (var sparks in root.GetComponentsInChildren<HornClashSparks>(true)) sparks.ReleaseResources();
                    }
                }
                finally { EditorSceneManager.CloseScene(Scene, true); }
            }
        }
        finally
        {
            try { if (Settings) UnityEngine.Object.DestroyImmediate(Settings); }
            finally { if (ownsAsset) AssetDatabase.DeleteAsset(path); }
        }
    }
}
