using System;
using System.Linq;
using BeetleArena;
using UnityEditor;
using UnityEngine;

/// <summary>默认动作装配，仅编辑器使用。运行核心不引用任何具体动作类型。</summary>
public static class BeetleMotionSetup
{
    [MenuItem("Tools/Beetle Arena/Install Motion Modules")]
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
        // 安装是显式操作；正常开局绝不自动把用户已移除的模块重新加回去。
        settings.motionModules = modules;
        settings.Validate(); EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
    }
}
