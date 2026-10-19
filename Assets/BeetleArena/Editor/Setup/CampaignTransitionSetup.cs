using System;
using System.IO;
using BeetleArena.Campaign;
using UnityEditor;
using UnityEngine;

public static class CampaignTransitionSetup
{
    public const string AssetPath="Assets/BeetleArena/Resources/CampaignTransition.asset";
    [MenuItem("Tools/Beetle Arena/Ensure Campaign Transition Profile")]
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before creating the transition profile.");
        if(AssetDatabase.LoadAssetAtPath<CampaignTransitionProfile>(AssetPath))return;
        Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));AssetDatabase.Refresh();
        var profile=ScriptableObject.CreateInstance<CampaignTransitionProfile>();
        AssetDatabase.CreateAsset(profile,AssetPath);AssetDatabase.SaveAssetIfDirty(profile);
        Debug.Log("Campaign transition curves: "+AssetPath);
    }
}
