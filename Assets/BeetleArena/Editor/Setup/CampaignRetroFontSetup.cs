using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

public static class CampaignRetroFontSetup
{
    [MenuItem("Tools/Beetle Arena/Prepare Retro Presentation")]
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before preparing presentation assets.");
        const string folder="Assets/BeetleArena/Resources";
        Directory.CreateDirectory(folder);AssetDatabase.Refresh();
        string source=string.Concat(Directory.GetFiles("Assets/BeetleArena/Runtime","*.cs",SearchOption.AllDirectories).Select(File.ReadAllText))+
            File.ReadAllText("Assets/StreamingAssets/campaign-content.json");
        string characters=new string(source.Where(c=>!char.IsControl(c)).Concat(Enumerable.Range(32,95).Select(i=>(char)i)).Distinct().ToArray());
        var latin=Bake(folder+"/RetroUiFont.asset","Tahoma",72,7,1024,new string(characters.Where(c=>c<256).ToArray()));
        var cjk=Bake(folder+"/RetroUiCjk.asset","SimSun",40,4,2048,new string(characters.Where(c=>c>=256).ToArray()));
        var fallback=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BeetleArenaSceneBuilder.AssetFolder+"/ArenaCJK.asset");
        latin.fallbackFontAssetTable=new System.Collections.Generic.List<TMP_FontAsset>{cjk};
        if(fallback)cjk.fallbackFontAssetTable=new System.Collections.Generic.List<TMP_FontAsset>{fallback};
        EditorUtility.SetDirty(latin);EditorUtility.SetDirty(cjk);AssetDatabase.SaveAssets();
        BeetleCombatCrtSetup.EnsureProfile();
        Directory.CreateDirectory("Artifacts/RetroUi");
        string missing=new string(characters.Where(c=>!latin.HasCharacter(c,true,true)).ToArray());
        File.WriteAllText("Artifacts/RetroUi/Fonts.txt","Static Tahoma 72/7 + SimSun 40/4, existing CJK/symbol fallback retained.\nMissing: "+missing+"\n");
        if(missing.Length>0)Debug.LogWarning("Retro font fallback missing: "+missing);
    }
    static TMP_FontAsset Bake(string path,string family,int size,int padding,int atlas,string characters)
    {
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if(!font)
        {
            font=TMP_FontAsset.CreateFontAsset(family,"Regular",size);
            if(!font)throw new InvalidOperationException("Missing font: "+family);
            var settings=new SerializedObject(font);
            settings.FindProperty("m_AtlasWidth").intValue=atlas;settings.FindProperty("m_AtlasHeight").intValue=atlas;
            settings.FindProperty("m_AtlasPadding").intValue=padding;settings.ApplyModifiedPropertiesWithoutUndo();
            font.material.SetFloat("_GradientScale",padding+1);
            font.name=Path.GetFileNameWithoutExtension(path);font.isMultiAtlasTexturesEnabled=true;
            AssetDatabase.CreateAsset(font,path);AssetDatabase.AddObjectToAsset(font.material,font);
        }
        font.atlasPopulationMode=AtlasPopulationMode.DynamicOS;
        font.TryAddCharacters(characters,out _);font.atlasPopulationMode=AtlasPopulationMode.Static;
        foreach(var texture in font.atlasTextures)
        { if(!texture)continue;if(!AssetDatabase.Contains(texture))AssetDatabase.AddObjectToAsset(texture,font);EditorUtility.SetDirty(texture); }
        EditorUtility.SetDirty(font);EditorUtility.SetDirty(font.material);return font;
    }
}
