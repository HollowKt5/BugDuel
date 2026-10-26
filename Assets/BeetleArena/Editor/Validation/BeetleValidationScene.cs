using System;
using System.Collections.Generic;
using System.IO;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Temporary scene and parameter duplicate dedicated to Editor validation. Wraps entire validation (including reports) in a using block;
/// cleans up upon successful completion, assertion failures, or mid-construction aborts; never directly opens and closes user scenes.
/// Motion module assets remain read-only shared references; tests mutating module fields must construct/destroy their own copies.
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
            // Generates unique path when encountering leftover or pre-existing user assets with identical names, avoiding overwriting/deleting files.
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
                // Standard MonoBehaviours manually initialized in Edit Mode do not reliably receive OnDestroy.
                // Continues cleaning up remaining components, scenes, and parameter duplicates even if an individual component teardown fails.
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
            // Preserves retryable state on failure, avoiding mistakenly marking unreleased resources as cleaned.
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
        // CloseScene concludes scene lifecycle, though cached file handles from import/read may hold file locks.
        // Synchronously releases before deleting; immediate and exceptional exits avoid depending on the editor executing subsequent frames.
        AssetDatabase.ReleaseCachedFileHandles();
        if (!AssetDatabase.DeleteAsset(path))
        {
            // Retains strict ownership checks when native deletion fails, rather than silently logging failure as cleaned.
            // Acts as fallback exclusively for the single scene cloned and owned by this instance; never enumerates, deletes directories, or touches other assets.
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
