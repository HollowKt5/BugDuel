using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using BeetleArena.Campaign;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.TextCore.LowLevel;

public static class CampaignLocalizationSetup
{
    const string Folder="Assets/Localization",Fonts=Folder+"/Fonts";
    [MenuItem("Tools/Beetle Arena/Prepare Chinese and English")]
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before preparing language assets.");
        Directory.CreateDirectory(Folder+"/Tables");AssetDatabase.Refresh();
        var settings=LocalizationEditorSettings.ActiveLocalizationSettings;
        if(!settings)
        {
            settings=ScriptableObject.CreateInstance<LocalizationSettings>();
            AssetDatabase.CreateAsset(settings,Folder+"/LocalizationSettings.asset");
            LocalizationEditorSettings.ActiveLocalizationSettings=settings;
        }
        foreach(var code in new[]{"zh-Hans","en"})
        {
            var locale=LocalizationEditorSettings.GetLocales().FirstOrDefault(l=>l.Identifier.Code==code);
            if(!locale){locale=Locale.CreateLocale(code);locale.LocaleName=code=="en"?"English":"简体中文";AssetDatabase.CreateAsset(locale,Folder+"/"+code+".asset");LocalizationEditorSettings.AddLocale(locale);}
        }
        LocalizationSettings.StartupLocaleSelectors.Clear();
        LocalizationSettings.StartupLocaleSelectors.Add(new SpecificLocaleSelector{LocaleId=new LocaleIdentifier("zh-Hans")});
        LocalizationSettings.InitializeSynchronously=true;EditorUtility.SetDirty(settings);
        var data=JsonUtility.FromJson<CampaignLocalization.Catalog>(File.ReadAllText(Folder+"/Source/GameText.json"));
        const string patterns="Assets/BeetleArena/Resources/CampaignLanguagePatterns.json";
        var sourcePatterns=new CampaignLocalization.Catalog{entries=data.entries.Select(e=>new CampaignLocalization.Entry{key=e.key,source=e.source,zh=e.zh}).ToArray()};
        AssetDatabase.ReleaseCachedFileHandles();
        File.WriteAllText(patterns,JsonUtility.ToJson(sourcePatterns,true));AssetDatabase.ImportAsset(patterns);
        var strings=LocalizationEditorSettings.GetStringTableCollection(CampaignLocalization.TableName)??LocalizationEditorSettings.CreateStringTableCollection(CampaignLocalization.TableName,Folder+"/Tables");
        foreach(var table in strings.StringTables)
        {
            foreach(var entry in data.entries)
            {
                string value=table.LocaleIdentifier.Code=="en"?entry.en:entry.zh;
                if(string.IsNullOrWhiteSpace(value))throw new InvalidOperationException("Missing translation: "+entry.key);
                table.AddEntry(entry.key,value).IsSmart=false;
            }
            EditorUtility.SetDirty(table);
        }
        EditorUtility.SetDirty(strings);EditorUtility.SetDirty(strings.SharedData);
        LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(null,strings);

        string chars=new string(string.Concat(data.entries.Select(e=>e.zh+e.en)).Where(c=>!char.IsControl(c)).Concat(Enumerable.Range(32,95).Select(c=>(char)c)).Distinct().ToArray());
        var source=AssetDatabase.LoadAssetAtPath<Font>(Fonts+"/LiSu.ttf");
        if(!source)throw new InvalidOperationException("LiSu.ttf has not imported.");
        var chinese=Bake(Fonts+"/ChineseLiSu.asset",source,chars,false);
        var dynamic=Bake(Fonts+"/ChineseLiSuFallback.asset",source,"",true);
        var symbols=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/BeetleArena/Generated/ArenaCJK.asset");
        chinese.fallbackFontAssetTable=new List<TMP_FontAsset>{dynamic};
        if(symbols)chinese.fallbackFontAssetTable.Add(symbols);
        string englishPath=Fonts+"/English.asset";
        if(!AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(englishPath))AssetDatabase.CopyAsset("Assets/BeetleArena/Resources/RetroUiFont.asset",englishPath);
        var english=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(englishPath);
        english.fallbackFontAssetTable=new List<TMP_FontAsset>{chinese};
        EditorUtility.SetDirty(chinese);EditorUtility.SetDirty(english);
        var fonts=LocalizationEditorSettings.GetAssetTableCollection(CampaignLocalization.FontTable)??LocalizationEditorSettings.CreateAssetTableCollection(CampaignLocalization.FontTable,Folder+"/Tables");
        fonts.AddAssetToTable(new LocaleIdentifier("zh-Hans"),"ui",chinese);
        fonts.AddAssetToTable(new LocaleIdentifier("en"),"ui",english);
        foreach(var table in fonts.AssetTables)EditorUtility.SetDirty(table);
        EditorUtility.SetDirty(fonts);EditorUtility.SetDirty(fonts.SharedData);
        LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(null,fonts);
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Artifacts/Localization");
        string missing=new string(chars.Where(c=>!chinese.HasCharacter(c,true,true)).ToArray());
        int gaps=strings.SharedData.Entries.Sum(e=>strings.StringTables.Count(t=>string.IsNullOrWhiteSpace(t.GetEntry(e.Id)?.Value)));
        foreach(var path in new[]{Fonts+"/ChineseLiSu.asset",Fonts+"/English.asset"})
            if(!AssetDatabase.LoadAllAssetsAtPath(path).Any(a=>a is Material))throw new InvalidOperationException("Missing saved font material: "+path);
        File.WriteAllText("Artifacts/Localization/Setup.txt",$"Entries: {data.entries.Length}; locales: zh-Hans, en; missing table values: {gaps}\nChinese: {chinese.faceInfo.familyName}; English: {english.faceInfo.familyName}\nMissing glyphs: {missing}\n");
        if(gaps>0||missing.Length>0)throw new InvalidOperationException("Incomplete localization assets; see Artifacts/Localization/Setup.txt");
        // Enable the optional report for this build so Addressables never blocks first-time
        // preparation on its report opt-in dialog. Restore the user's preference afterwards.
        bool report=ProjectConfigData.GenerateBuildLayout;
        try
        {
            ProjectConfigData.GenerateBuildLayout=true;
            AddressableAssetSettings.BuildPlayerContent(out var result);
            if(!string.IsNullOrEmpty(result.Error))throw new InvalidOperationException(result.Error);
        }
        finally { ProjectConfigData.GenerateBuildLayout=report; }
        File.AppendAllText("Artifacts/Localization/Setup.txt","PASS font sub-assets, glyph coverage and Addressables content build\n");
    }
    static TMP_FontAsset Bake(string path,Font source,string chars,bool dynamic)
    {
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if(!font)
        {
            font=TMP_FontAsset.CreateFontAsset(source,40,4,GlyphRenderMode.SDFAA,dynamic?1024:2048,dynamic?1024:2048,AtlasPopulationMode.Dynamic,true);
            font.name=Path.GetFileNameWithoutExtension(path);AssetDatabase.CreateAsset(font,path);AssetDatabase.AddObjectToAsset(font.material,font);
        }
        font.isMultiAtlasTexturesEnabled=true;font.atlasPopulationMode=AtlasPopulationMode.Dynamic;
        if(chars.Length>0)font.TryAddCharacters(chars,out _);
        var settings=new SerializedObject(font);var clear=settings.FindProperty("m_ClearDynamicDataOnBuild");if(clear!=null)clear.boolValue=dynamic;settings.ApplyModifiedPropertiesWithoutUndo();
        if(!dynamic)font.atlasPopulationMode=AtlasPopulationMode.Static;
        foreach(var atlas in font.atlasTextures){if(!atlas)continue;if(!AssetDatabase.Contains(atlas))AssetDatabase.AddObjectToAsset(atlas,font);EditorUtility.SetDirty(atlas);}
        font.material.mainTexture=font.atlasTexture;EditorUtility.SetDirty(font);EditorUtility.SetDirty(font.material);return font;
    }
}
