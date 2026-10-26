using System;
using System.IO;
using BeetleArena.Campaign;
using UnityEditor;
using UnityEngine;

public static class CampaignTransitionSetup
{
    public const string AssetPath="Assets/BeetleArena/Resources/CampaignTransition.asset";
    [MenuItem("Tools/甲壳虫斗/画面与资源/界面与字体/更新转场配置", false, 344)]
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before creating the transition profile.");
        var profile=AssetDatabase.LoadAssetAtPath<CampaignTransitionProfile>(AssetPath);
        if(!profile)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));AssetDatabase.Refresh();
            profile=ScriptableObject.CreateInstance<CampaignTransitionProfile>();AssetDatabase.CreateAsset(profile,AssetPath);
        }
        // Upgrade only the previous stock hold; preserve an already authored custom duration.
        if(Mathf.Approximately(profile.blackHoldDuration,.06f))profile.blackHoldDuration=2.06f;
        profile.shutterShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/BeetleArena/Resources/"+CampaignTransitionProfile.ShutterResourceName+".shader");
        if(!profile.shutterShader)throw new InvalidOperationException("Import the campaign transition shutter shader before applying the profile.");
        EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);
        Debug.Log("Campaign transition curves: "+AssetPath);
    }
}
