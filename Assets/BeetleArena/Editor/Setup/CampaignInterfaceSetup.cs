using System;
using System.IO;
using System.Linq;
using BeetleArena.Campaign;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CampaignInterfaceSetup
{
    [MenuItem("Tools/甲壳虫斗/场景与安装/更新菜单仓库与育种界面", false, 204)]
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before asset setup.");
        const string path="Assets/Scenes/Campaign/CampaignBreeding.unity";
        if(!File.Exists(path))
        {
            if(!AssetDatabase.CopyAsset("Assets/Scenes/Campaign/CampaignCatching.unity",path))throw new IOException("Could not create breeding scene.");
            var previous=SceneManager.GetActiveScene();var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try
            {
                var view=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CampaignView>(true)).Single();
                view.page=CampaignPage.Breeding;view.name="BreedingUI";EditorUtility.SetDirty(view);
                EditorSceneManager.SaveScene(scene);
            }
            finally {EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(previous);}
        }
        if(!EditorBuildSettings.scenes.Any(s=>s.path==path))EditorBuildSettings.scenes=EditorBuildSettings.scenes.Concat(new[]{new EditorBuildSettingsScene(path,true)}).ToArray();
        PlayerSettings.productName="Betles Duel";
        PrepareEnglishFont();AssetDatabase.SaveAssets();
    }

    public static TMP_FontAsset PrepareEnglishFont()
    {
        const string path="Assets/Localization/Fonts/English.asset";
        const string templatePath="Assets/BeetleArena/Resources/RetroUiFont.asset";
        var template=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(templatePath);
        if(!template||template.faceInfo.familyName!="Tahoma")throw new InvalidOperationException("Baked Tahoma UI font missing.");
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if(!font)
        {
            if(!AssetDatabase.CopyAsset(templatePath,path))throw new IOException("Could not prepare English UI font.");
            font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        }
        if(font.faceInfo.familyName=="Tahoma")return font;
        // Keep the localized font GUID and its sub-asset IDs. Reuse the project's baked
        // SDF face so the runtime does not depend on fonts installed on the player's OS.
        var material=font.material;var atlases=font.atlasTextures;
        if(!material||atlases==null||atlases.Length!=template.atlasTextures.Length)
            throw new InvalidOperationException("Unexpected localized font sub-assets.");
        var fallbacks=font.fallbackFontAssetTable;
        EditorUtility.CopySerialized(template.material,material);
        for(int i=0;i<atlases.Length;i++)
        {
            EditorUtility.CopySerialized(template.atlasTextures[i],atlases[i]);
            atlases[i].name="Tahoma UI Atlas "+i;EditorUtility.SetDirty(atlases[i]);
        }
        EditorUtility.CopySerialized(template,font);
        font.name="Tahoma UI";font.material=material;font.atlasTextures=atlases;
        font.fallbackFontAssetTable=fallbacks;material.name="Tahoma UI Material";material.mainTexture=atlases[0];
        font.ReadFontAssetDefinition();EditorUtility.SetDirty(material);EditorUtility.SetDirty(font);
        return font;
    }
}
