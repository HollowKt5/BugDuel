using System;
using System.Collections.Generic;
using System.IO;
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
    string path, ownedAssetGuid;
    bool ownsAsset, disposing, disposed;
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
            ownedAssetGuid = AssetDatabase.AssetPathToGUID(path);
            // Finish the copied asset's import before its scene can be opened and immediately disposed.
            // Releasing cached handles during cleanup does not wait for an outstanding import reader.
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
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
        if (disposed || disposing) return;
        disposing = true;
        var errors = new List<Exception>();
        void Attempt(Action release)
        {
            try { release(); }
            catch (Exception error) { errors.Add(error); }
        }
        try
        {
            if (previous.IsValid() && previous.isLoaded)
                Attempt(() =>
                {
                    if (SceneManager.GetActiveScene() != previous && !SceneManager.SetActiveScene(previous))
                        throw new InvalidOperationException("Cannot restore the previous active scene before closing " + path);
                });
            if (Scene.IsValid() && Scene.isLoaded)
            {
                // 普通 MonoBehaviour 在 Edit Mode 手动 Initialize 后未必收到 OnDestroy。
                // 单个组件释放失败也继续清理其它组件、场景和参数副本。
                Attempt(() =>
                {
                    foreach (var root in Scene.GetRootGameObjects())
                    {
                        foreach (var audio in root.GetComponentsInChildren<ArenaAudio>(true)) Attempt(audio.ReleaseResources);
                        foreach (var sparks in root.GetComponentsInChildren<HornClashSparks>(true)) Attempt(sparks.ReleaseResources);
                    }
                });
                Attempt(() =>
                {
                    if (!EditorSceneManager.CloseScene(Scene, true))
                        throw new InvalidOperationException("Cannot close the owned validation scene: " + path);
                });
            }
            Attempt(() => { if (Settings) UnityEngine.Object.DestroyImmediate(Settings); });
            Match = null; Settings = null;
            // CloseScene can leave its serialized-file stream alive until unused assets unload.
            // Wait for that release while retaining the user's managed asset references.
            Attempt(() => EditorUtility.UnloadUnusedAssetsImmediate(true));
            Attempt(DeleteOwnedAsset);
        }
        finally
        {
            // 失败保留可重试状态，不能把尚未释放的资源误记成已清理。
            disposed = !(Scene.IsValid() && Scene.isLoaded) && !Settings && !ownsAsset;
            disposing = false;
        }
        if (errors.Count > 0) throw new AggregateException("Validation fixture cleanup failed: " + path, errors);
    }

    void DeleteOwnedAsset()
    {
        if (!ownsAsset) return;
        string absolutePath = Path.GetFullPath(path);
        string generatedDirectory = Path.GetFullPath("Assets/BeetleArena/Generated");
        if (Path.GetDirectoryName(absolutePath) != generatedDirectory || Path.GetExtension(absolutePath) != ".unity")
            throw new InvalidOperationException("Validation asset is outside its owned scene directory: " + path);
        if (Scene.IsValid() && Scene.isLoaded)
            throw new InvalidOperationException("Cannot delete a validation asset while its scene remains loaded: " + path);
        string metaPath = absolutePath + ".meta";
        if (!File.Exists(absolutePath) && !File.Exists(metaPath))
        {
            ownsAsset = false;
            return;
        }
        RequireOwnedGuid();
        // CloseScene 结束场景生命周期，但导入/读取场景时缓存的文件句柄仍可能占用文件。
        // 显式同步释放后再删除；立即退出与异常退出均不依赖编辑器再跑一帧。
        AssetDatabase.ReleaseCachedFileHandles();
        if (!AssetDatabase.DeleteAsset(path))
        {
            // 原生删除失败时保留严格所有权检查，而不是把失败悄悄记成已清理。
            // 仅对本实例复制且仍拥有的单个场景兜底；绝不枚举、删除目录或删其它资产。
            RequireOwnedGuid();
            if (Scene.IsValid() && Scene.isLoaded)
                throw new InvalidOperationException("Validation scene reopened during cleanup: " + path);
            bool metaMatches = false;
            if (File.Exists(metaPath))
                foreach (string line in File.ReadLines(metaPath))
                    if (line == "guid: " + ownedAssetGuid) metaMatches = true;
            if (!metaMatches)
                throw new InvalidOperationException("Validation scene disk identity changed; refusing cleanup: " + path);
            AssetDatabase.ReleaseCachedFileHandles();
            File.Delete(absolutePath);
            File.Delete(metaPath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("Validation fixture used verified file cleanup after AssetDatabase.DeleteAsset returned false: " + path);
        }
        if (File.Exists(absolutePath) || File.Exists(metaPath) || AssetDatabase.LoadAssetAtPath<SceneAsset>(path))
            throw new InvalidOperationException("Owned validation scene still exists after cleanup: " + path);
        ownsAsset = false;
    }

    void RequireOwnedGuid()
    {
        if (string.IsNullOrEmpty(ownedAssetGuid) || AssetDatabase.AssetPathToGUID(path) != ownedAssetGuid)
            throw new InvalidOperationException("Validation scene ownership changed; refusing to delete: " + path);
    }
}
