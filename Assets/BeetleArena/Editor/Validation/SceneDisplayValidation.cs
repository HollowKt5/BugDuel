using System;
using System.IO;
using System.Linq;
using BeetleArena;
using BeetleArena.EditorTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class SceneDisplayValidation
{
    public const string Output="Artifacts/SceneDisplay";
    const string Fixture="Assets/BeetleArena/Editor/Validation/Fixtures/SceneDisplayTest.shader";

    [MenuItem("Tools/甲壳虫斗/验证/界面与画面/场景画面编辑器", false, 610)]
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Run editor validation outside Play mode.");
        Directory.CreateDirectory(Output);
        string result=Output+"/EditorResult.txt";
        File.WriteAllText(result,"SCENE DISPLAY EDITOR VALIDATION\n"+DateTimeOffset.Now.ToString("o")+"\n");
        var profile=WorldDisplayEditorService.EnsureProfile();
        string original=EditorJsonUtility.ToJson(profile);
        var materialStates=profile.scenes.SelectMany(s=>new[]{s.ditherMaterial,s.denseDitherMaterial}).Where(m=>m).Distinct().ToDictionary(m=>m,m=>EditorJsonUtility.ToJson(m));
        string materialPath=null;
        using(var preferences=new DisplayPreferencesSnapshot())
        {
            int checks=0;
            void Check(bool condition,string message)
            {
                if(!condition)throw new InvalidOperationException(message);
                checks++;File.AppendAllText(result,"PASS "+message+"\n");
            }
            try
            {
                var shaders=WorldDisplayEditorService.DiscoverShaders();
                var dither=Resources.Load<Shader>("BeetleCombatDither");var dense=Resources.Load<Shader>("BeetleDenseDither");
                var crt=Resources.Load<Shader>("BeetleCombatCrt");
                Check(shaders.Contains(dither)&&shaders.Contains(dense)&&shaders.Contains(crt),"scanner finds soft dither, dense dither and CRT");
                Check(WorldDisplayEditorService.StyleFor(dense)==BeetleDisplayStyle.DenseDither,"dense shader maps to its distinct style");
                Check(!shaders.Any(s=>s.name.Contains("Blur")||s.name.Contains("Toon")),"scanner excludes model and internal blur shaders");
                WorldDisplayEditorService.Apply(profile,"CampaignGreenhouse",dither,2);
                WorldDisplayEditorService.Apply(profile,"CampaignArena",crt,6);
                WorldDisplayEditorService.Apply(profile,"CampaignSpecimenRoom",null,3);
                Check(BeetleDisplayPreferences.GetStyle("CampaignGreenhouse")==BeetleDisplayStyle.OrderedDither&&
                      BeetleDisplayPreferences.GetStyle("CampaignArena")==BeetleDisplayStyle.Crt&&
                      BeetleDisplayPreferences.GetStyle("CampaignSpecimenRoom")==BeetleDisplayStyle.Clean,"three scenes keep independent styles");
                Check(BeetleDisplayPreferences.GetPixelSize("CampaignGreenhouse")==2&&BeetleDisplayPreferences.GetPixelSize("CampaignArena")==6,"pixel sizes remain independent");
                var greenhouseMaterial=profile.Find("CampaignGreenhouse").ditherMaterial;
                greenhouseMaterial.SetFloat("_PixelSize",4);greenhouseMaterial.SetFloat("_DitherStrength",.25f);
                Check(BeetleDisplayPreferences.GetPixelSize("CampaignGreenhouse")==3&&
                      profile.Find("CampaignArena").ditherMaterial!=greenhouseMaterial,"material Inspector pixel changes affect only their own scene");
                BeetleDisplayPreferences.SetStyle("CampaignGreenhouse",BeetleDisplayStyle.Clean);
                Check(BeetleDisplayPreferences.GetStyle("CampaignArena")==BeetleDisplayStyle.Crt,"player override does not change another scene");
                BeetleDisplayPreferences.UseSceneDefault("CampaignGreenhouse");
                Check(BeetleDisplayPreferences.GetStyle("CampaignGreenhouse")==BeetleDisplayStyle.OrderedDither,"reset restores authored scene default");
                Check(File.ReadAllText(WorldDisplayEditorService.ProfilePath).Contains("CampaignGreenhouse"),"project configuration is persisted on disk");

                var greenhouse=profile.Find("CampaignGreenhouse");
                var denseMaterial=greenhouse.denseDitherMaterial;
                var arenaDense=profile.Find("CampaignArena").denseDitherMaterial;
                Check(denseMaterial&&arenaDense&&denseMaterial!=greenhouseMaterial&&denseMaterial!=arenaDense,
                    "dense materials are independent of soft dither and other scenes");
                string softBefore=EditorJsonUtility.ToJson(greenhouseMaterial);
                string arenaDenseBefore=EditorJsonUtility.ToJson(arenaDense);
                BeetleDisplayPreferences.SetStyle("CampaignGreenhouse",BeetleDisplayStyle.OrderedDither);
                BeetleDisplayPreferences.SetPixelSize("CampaignGreenhouse",5);
                BeetleDisplayPreferences.SetStyle("CampaignGreenhouse",BeetleDisplayStyle.DenseDither);
                BeetleDisplayPreferences.SetPixelSize("CampaignGreenhouse",6);
                Check(PlayerPrefs.GetInt(BeetleDisplayPreferences.ScenePixelSizeKey("CampaignGreenhouse"))==3&&
                      PlayerPrefs.GetInt(BeetleDisplayPreferences.SceneDensePixelSizeKey("CampaignGreenhouse"))==3,
                    "soft and dense player pixel overrides coexist independently");
                WorldDisplayEditorService.Apply(profile,"CampaignGreenhouse",dense,2);
                Check(BeetleDisplayPreferences.GetStyle("CampaignGreenhouse")==BeetleDisplayStyle.DenseDither&&
                      WorldDisplayEditorService.ShaderFor(greenhouse)==dense&&
                      BeetleDisplayPreferences.GetDitherMaterial("CampaignGreenhouse")==denseMaterial&&
                      BeetleDisplayPreferences.GetPixelSize("CampaignGreenhouse")==2,
                    "applying dense selects the scene's dense material and authored pixel size");
                Check(!PlayerPrefs.HasKey(BeetleDisplayPreferences.SceneStyleKey("CampaignGreenhouse"))&&
                      !PlayerPrefs.HasKey(BeetleDisplayPreferences.ScenePixelSizeKey("CampaignGreenhouse"))&&
                      !PlayerPrefs.HasKey(BeetleDisplayPreferences.SceneDensePixelSizeKey("CampaignGreenhouse")),
                    "editor apply clears style plus both dither pixel overrides");
                Check(greenhouse.ditherMaterial==greenhouseMaterial&&EditorJsonUtility.ToJson(greenhouseMaterial)==softBefore&&
                      EditorJsonUtility.ToJson(arenaDense)==arenaDenseBefore&&profile.Find("CampaignArena").style==BeetleDisplayStyle.Crt,
                    "applying dense preserves soft material parameters and other scene settings");
                WorldDisplayEditorService.Apply(profile,"CampaignGreenhouse",dither,4);
                Check(BeetleDisplayPreferences.GetDitherMaterial("CampaignGreenhouse")==greenhouseMaterial&&
                      greenhouse.denseDitherMaterial==denseMaterial&&Mathf.Approximately(denseMaterial.GetFloat("_PixelSize"),2),
                    "switching back to soft preserves the separate dense material");

                // A new compatible shader must be discoverable without adding a C# enum value.
                string source=File.ReadAllText(AssetDatabase.GetAssetPath(dither));
                File.WriteAllText(Fixture,source.Replace("BugDuel/Combat Ordered Dither","BugDuel/Validation Screen")
                    .Replace("\"BeetleDitherCore.hlsl\"","\"Assets/BeetleArena/Resources/BeetleDitherCore.hlsl\""));
                AssetDatabase.ImportAsset(Fixture,ImportAssetOptions.ForceSynchronousImport);
                var custom=AssetDatabase.LoadAssetAtPath<Shader>(Fixture);
                Check(custom&&!ShaderUtil.ShaderHasError(custom),"custom shader fixture compiles with the shared dither include");
                Check(WorldDisplayEditorService.DiscoverShaders().Contains(custom),"scanner discovers newly imported compatible shader");
                WorldDisplayEditorService.Apply(profile,"CampaignMarket",custom,4);
                var material=profile.Find("CampaignMarket").customMaterial;
                Check(material&&AssetDatabase.Contains(material)&&material.shader==custom,"custom shader application creates a persistent material");
                Check(BeetleDisplayPreferences.GetStyle("CampaignMarket")==BeetleDisplayStyle.Custom,"custom shader is represented in scene configuration");
                materialPath=AssetDatabase.GetAssetPath(material);

                WorldDisplayWindow.Open();var window=EditorWindow.GetWindow<WorldDisplayWindow>();
                window.position=new Rect(70,70,660,680);window.CreateGUI();
                var sceneField=window.rootVisualElement.Q<DropdownField>("scene");
                sceneField.index=WorldDisplayEditorService.ScenePaths().FindIndex(p=>Path.GetFileNameWithoutExtension(p)=="CampaignArena");
                var shaderField=window.rootVisualElement.Q<DropdownField>("shader");shaderField.index=0;
                Check(window.rootVisualElement.Q<SliderInt>("pixels")==null&&window.rootVisualElement.Q<Slider>("strength")==null,
                    "scene tool contains no duplicate shader parameter sliders");
                Undo.FlushUndoRecordObjects();Undo.IncrementCurrentGroup();
                window.ApplySelection();
                Check(profile.Find("CampaignArena").style==BeetleDisplayStyle.Clean,"editor window applies clean mode to selected scene");
                Check(profile.Find("CampaignGreenhouse").style==BeetleDisplayStyle.OrderedDither,"window application preserves other scene settings");
                Undo.FlushUndoRecordObjects();Undo.PerformUndo();
                Check(profile.Find("CampaignArena").style==BeetleDisplayStyle.Crt,"undo restores previous scene style");
                // Restore references before deleting the validation-only generated material.
                EditorJsonUtility.FromJsonOverwrite(original,profile);EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);
                File.AppendAllText(result,"ALL CHECKS PASSED | "+checks+" checks\n");
            }
            catch(Exception error){File.AppendAllText(result,"FAILED "+error+"\n");throw;}
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(original,profile);EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);
                foreach(var pair in materialStates)
                {EditorJsonUtility.FromJsonOverwrite(pair.Value,pair.Key);EditorUtility.SetDirty(pair.Key);AssetDatabase.SaveAssetIfDirty(pair.Key);}
                if(materialPath!=null)AssetDatabase.DeleteAsset(materialPath);
                AssetDatabase.DeleteAsset(Fixture);BeetleDisplayPreferences.ReloadProfile();
                Undo.ClearUndo(profile);
                var window=EditorWindow.GetWindow<WorldDisplayWindow>();window.CreateGUI();
            }
        }
    }
}
