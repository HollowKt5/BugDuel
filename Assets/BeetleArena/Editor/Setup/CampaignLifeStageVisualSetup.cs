using System.IO;
using BeetleArena.Campaign;
using UnityEditor;
using UnityEngine;

public static class CampaignLifeStageVisualSetup
{
    public const string ProfilePath = "Assets/BeetleArena/Resources/LifeStageVisuals.asset";

    [MenuItem("Tools/甲壳虫斗/画面与资源/养殖表现/创建生命阶段表现配置", false, 322)]
    public static void Install() => EnsureProfile();

    /// <summary>Creates only the missing profile; preserves all Inspector edits on repeat calls.</summary>
    public static CampaignLifeStageVisualProfile EnsureProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<CampaignLifeStageVisualProfile>(ProfilePath);
        if (profile) return profile;
        Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath));
        profile = ScriptableObject.CreateInstance<CampaignLifeStageVisualProfile>();
        AssetDatabase.CreateAsset(profile, ProfilePath);
        AssetDatabase.SaveAssets();
        return profile;
    }
}
