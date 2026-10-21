using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Delete only known generated intermediates with no asset, source or generator GUID references.</summary>
public static class BeetleGeneratedAssetCleanup
{
    [MenuItem("Tools/Beetle Arena/Clean Unused Generated Intermediates")]
    public static void Clean() => Debug.Log(Run(true));

    public static string Run(bool apply)
    {
        const string legacyEnvironment = "MenuShellStudio";
        string folder = CampaignIridescenceSetup.Folder;
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before cleaning assets.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scenes before checking references.");
        var paths = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) &&
            !AssetDatabase.IsValidFolder(p)).ToArray();
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
            foreach (var dependency in AssetDatabase.GetDependencies(path, false))
                if (dependency != path) referenced.Add(dependency);
        string source = string.Join("\n", paths.Where(p => p.EndsWith(".cs", StringComparison.Ordinal))
            .Select(File.ReadAllText));
        // Current splitters regenerate from bug.fbx, not these pre-split copies.
        var legacyMeshes = new HashSet<string>(new[] { "Part2", "Part14", "Part3", "Part5" }
            .Select(name => "Assets/BeetleArena/Generated/ImportedModel/" + name + ".asset"));
        var candidates = paths.Where(p => legacyMeshes.Contains(p) || p.StartsWith(folder + "/", StringComparison.Ordinal) &&
            (p.EndsWith(".mat", StringComparison.Ordinal) || p == folder + "/" + legacyEnvironment + ".asset"))
            .Where(p => !referenced.Contains(p)).Where(p =>
            {
                string guid = AssetDatabase.AssetPathToGUID(p);
                // Leg-<shell GUID> is a generator dependency, even without a scene reference.
                return !string.IsNullOrEmpty(guid) && !source.Contains(guid) && !source.Contains(Path.GetFileName(p)) &&
                    !paths.Any(other => other != p && Path.GetFileName(other).Contains(guid));
            }).OrderBy(p => p).ToArray();
        var report = new StringBuilder(apply ? "GENERATED INTERMEDIATE CLEANUP\n" : "GENERATED INTERMEDIATE CANDIDATES\n");
        long bytes = 0;
        foreach (var path in candidates)
        {
            long size = new FileInfo(path).Length + (File.Exists(path + ".meta") ? new FileInfo(path + ".meta").Length : 0);
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (apply && !AssetDatabase.DeleteAsset(path)) throw new IOException("Cannot remove unused asset: " + path);
            bytes += size;
            report.AppendLine($"{(apply ? "REMOVED" : "CANDIDATE")}\t{path}\t{guid}\t{size}");
        }
        report.AppendLine($"Assets={candidates.Length}; bytes={bytes}; checked Unity dependencies, source references and filename GUID links.");
        Directory.CreateDirectory("Artifacts/Cleanup");
        File.WriteAllText("Artifacts/Cleanup/GeneratedAssets" + (apply ? "Removed" : "Audit") + ".txt", report.ToString());
        return report.ToString();
    }
}
