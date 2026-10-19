using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

public static class CampaignFontSetup
{
    [MenuItem("Tools/Beetle Arena/Update Campaign Font Coverage")]
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before updating font assets.");
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BeetleArenaSceneBuilder.AssetFolder+"/ArenaCJK.asset");
        if(!font)throw new InvalidOperationException("Missing ArenaCJK font");
        string text=string.Concat(new[]{"Assets/BeetleArena/Runtime/Campaign","Assets/BeetleArena/Domain/Campaign"}
            .SelectMany(p=>Directory.GetFiles(p,"*.cs",SearchOption.AllDirectories)).Select(File.ReadAllText))
            +File.ReadAllText("Assets/StreamingAssets/campaign-content.json");
        string characters=new string(text.Where(c=>!char.IsControl(c)).Distinct().ToArray());
        font.atlasPopulationMode=AtlasPopulationMode.DynamicOS;
        font.TryAddCharacters(characters,out string missing);
        font.atlasPopulationMode=AtlasPopulationMode.Static;
        var cjk=Fallback("CampaignCJKFallback","Microsoft YaHei");
        var symbols=Fallback("CampaignSymbolFallback","Segoe UI Symbol");
        font.fallbackFontAssetTable=font.fallbackFontAssetTable??new System.Collections.Generic.List<TMP_FontAsset>();
        foreach(var fallback in new[]{cjk,symbols})if(!font.fallbackFontAssetTable.Contains(fallback))font.fallbackFontAssetTable.Add(fallback);
        cjk.TryAddCharacters("龘鱿兜锹樱桃鳃",out _);symbols.TryAddCharacters(missing??"",out _);
        string unresolved=new string(characters.Where(c=>!font.HasCharacter(c,true,true)).ToArray());
        foreach(var asset in new[]{font,cjk,symbols})
        {
            foreach(var texture in asset.atlasTextures)
            { if(!texture)continue;if(!AssetDatabase.Contains(texture))AssetDatabase.AddObjectToAsset(texture,asset);EditorUtility.SetDirty(texture); }
            EditorUtility.SetDirty(asset);EditorUtility.SetDirty(asset.material);
        }
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Artifacts/Campaign/Acceptance");
        File.WriteAllText("Artifacts/Campaign/Acceptance/FontCoverage.txt","Fixed characters: "+characters.Length+"\nUnresolved: "+unresolved+"\nStatic primary + 1024 dynamic OS CJK/symbol fallbacks; clear dynamic data on build.\n");
        if(unresolved.Length>0)throw new InvalidOperationException("Font coverage missing: "+unresolved);
        Debug.Log("Campaign font coverage PASS: "+characters.Length+" characters, dynamic CJK and symbol fallbacks.");
    }
    static TMP_FontAsset Fallback(string name,string family)
    {
        string path=BeetleArenaSceneBuilder.AssetFolder+"/"+name+".asset";
        var asset=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if(!asset)
        {
            asset=TMP_FontAsset.CreateFontAsset(family,"Regular",48);
            if(!asset)throw new InvalidOperationException("System font unavailable: "+family);
            asset.name=name;asset.isMultiAtlasTexturesEnabled=true;
            var dimensions=new SerializedObject(asset);dimensions.FindProperty("m_AtlasWidth").intValue=1024;dimensions.FindProperty("m_AtlasHeight").intValue=1024;dimensions.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(asset,path);AssetDatabase.AddObjectToAsset(asset.material,asset);
            foreach(var texture in asset.atlasTextures)if(texture)AssetDatabase.AddObjectToAsset(texture,asset);
        }
        asset.atlasPopulationMode=AtlasPopulationMode.DynamicOS;
        var serialized=new SerializedObject(asset);serialized.FindProperty("m_ClearDynamicDataOnBuild").boolValue=true;serialized.ApplyModifiedPropertiesWithoutUndo();
        return asset;
    }
}
