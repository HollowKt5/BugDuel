using System;
using System.Linq;
using BeetleArena;
using UnityEditor;
using UnityEngine;

/// <summary>Default motion setup, Editor-only. Runtime core holds no references to concrete motion types.</summary>
public static class BeetleMotionSetup
{
    [MenuItem("Tools/甲壳虫斗/场景与安装/战斗模块/安装动作模块", false, 221)]
    public static void Install()
    {
        const string folder = "Assets/BeetleArena/Generated/Motions";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/BeetleArena/Generated", "Motions");
        var settings = AssetDatabase.LoadAssetAtPath<BeetleMatchSettings>("Assets/BeetleArena/Generated/MatchSettings.asset");
        var modules = TypeCache.GetTypesDerivedFrom<BeetleMotionModule>().Where(t => !t.IsAbstract && !t.FullName.Contains("Validation"))
            .OrderBy(t => t.FullName, StringComparer.Ordinal).Select(type => {
                string path = folder + "/" + type.Name + ".asset";
                var module = AssetDatabase.LoadAssetAtPath<BeetleMotionModule>(path);
                if (!module) { module = (BeetleMotionModule)ScriptableObject.CreateInstance(type); AssetDatabase.CreateAsset(module, path); }
                return module;
            }).ToArray();
        // Installation is an explicit action; normal startup never re-adds modules removed by user.
        settings.motionModules = modules;
        settings.Validate(); EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
    }
}
