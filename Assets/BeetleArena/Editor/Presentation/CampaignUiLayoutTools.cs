using BeetleArena.Campaign;
using UnityEditor;
using UnityEngine;

public static class CampaignUiLayoutTools
{
    public const string AssetPath="Assets/BeetleArena/Resources/CampaignUiLayout.asset";

    [MenuItem("Tools/甲壳虫斗/常用工具/UI 布局参数",false,104)]
    public static void SelectLayout()
    {
        var profile=AssetDatabase.LoadAssetAtPath<CampaignUiLayoutProfile>(AssetPath);
        if(!profile)
        {
            profile=ScriptableObject.CreateInstance<CampaignUiLayoutProfile>();
            AssetDatabase.CreateAsset(profile,AssetPath);
            AssetDatabase.SaveAssetIfDirty(profile);
        }
        Selection.activeObject=profile;
        EditorGUIUtility.PingObject(profile);
    }
}
