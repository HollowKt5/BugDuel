using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BeetleArena.EditorTools
{
    public static class WorldDisplayEditorService
    {
        public const string ProfilePath="Assets/BeetleArena/Resources/WorldDisplay.asset";
        public const string ScreenEffectTag="BugDuelScreenEffect";
        const string MaterialFolder="Assets/BeetleArena/Resources/WorldDisplayMaterials";
        public static WorldDisplayProfile EnsureProfile()
        {
            var profile=AssetDatabase.LoadAssetAtPath<WorldDisplayProfile>(ProfilePath);
            if(!profile)
            {
                profile=ScriptableObject.CreateInstance<WorldDisplayProfile>();
                AssetDatabase.CreateAsset(profile,ProfilePath);AssetDatabase.SaveAssetIfDirty(profile);
            }
            BeetleDisplayPreferences.ReloadProfile();return profile;
        }
        public static List<string> ScenePaths()=>EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).Distinct().ToList();
        public static bool IsScreenShader(Shader shader)
        {
            if(!shader||!shader.isSupported||ShaderUtil.ShaderHasError(shader))return false;
            var material=new Material(shader);
            try{return material.HasProperty("_MainTex")&&string.Equals(material.GetTag(ScreenEffectTag,false,""),"True",StringComparison.OrdinalIgnoreCase);}
            finally{UnityEngine.Object.DestroyImmediate(material);}
        }
        public static List<Shader> DiscoverShaders()=>AssetDatabase.FindAssets("t:Shader",new[]{"Assets"})
            .Select(guid=>AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(IsScreenShader).OrderBy(shader=>shader.name,StringComparer.Ordinal).ToList();
        public static BeetleDisplayStyle StyleFor(Shader shader)
        {
            if(!shader)return BeetleDisplayStyle.Clean;
            if(shader==Resources.Load<Shader>("BeetleCombatDither"))return BeetleDisplayStyle.OrderedDither;
            if(shader==Resources.Load<Shader>("BeetleDenseDither"))return BeetleDisplayStyle.DenseDither;
            if(shader==Resources.Load<Shader>("BeetleCombatCrt"))return BeetleDisplayStyle.Crt;
            return BeetleDisplayStyle.Custom;
        }
        public static Shader ShaderFor(SceneDisplaySetting setting)
        {
            if(setting.style==BeetleDisplayStyle.OrderedDither)return Resources.Load<Shader>("BeetleCombatDither");
            if(setting.style==BeetleDisplayStyle.DenseDither)return Resources.Load<Shader>("BeetleDenseDither");
            if(setting.style==BeetleDisplayStyle.Crt)return Resources.Load<Shader>("BeetleCombatCrt");
            return setting.style==BeetleDisplayStyle.Custom&&setting.customMaterial?setting.customMaterial.shader:null;
        }
        public static void Apply(WorldDisplayProfile profile,string sceneName,Shader shader,int pixels,Material material=null)
        {
            if(shader&&!IsScreenShader(shader))throw new ArgumentException("请选择带 BugDuelScreenEffect=True 标记且使用 _MainTex 的可用画面 shader。");
            var style=StyleFor(shader);
            bool dither=BeetleDisplayPreferences.IsDither(style);
            var setting=profile.Find(sceneName);
            if(dither&&!material)material=setting?.DitherMaterial(style);
            if(style==BeetleDisplayStyle.Custom||dither)
            {
                if(material&&material.shader!=shader)throw new ArgumentException("材质使用的 shader 与所选画面 shader 不一致。");
                if(!material)
                {
                    EnsureMaterialFolder();
                    material=new Material(shader){name=sceneName+" screen"};
                    string suffix=style==BeetleDisplayStyle.DenseDither?"DenseDither":dither?"Dither":"";
                    string path=AssetDatabase.GenerateUniqueAssetPath(MaterialFolder+"/"+sceneName+suffix+".mat");
                    AssetDatabase.CreateAsset(material,path);Undo.RegisterCreatedObjectUndo(material,"Create screen material");
                }
                if(!AssetDatabase.Contains(material))throw new ArgumentException("请使用已保存为项目资产的画面材质。");
            }
            Undo.RecordObject(profile,"Apply scene screen effect");
            if(setting==null){setting=new SceneDisplaySetting{sceneName=sceneName};profile.scenes.Add(setting);}
            setting.style=style;setting.customMaterial=style==BeetleDisplayStyle.Custom?material:null;
            int selectedPixels=Mathf.Clamp(pixels,1,dither?3:6);
            if(style!=BeetleDisplayStyle.DenseDither)setting.pixelSize=selectedPixels;
            if(dither)
            {
                if(style==BeetleDisplayStyle.DenseDither)setting.denseDitherMaterial=material;
                else setting.ditherMaterial=material;
                Undo.RecordObject(material,"Apply scene screen material");
                material.SetFloat("_PixelSize",BeetleDisplayPreferences.DitherPixelScale(selectedPixels));
                EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);
            }
            EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);
            BeetleDisplayPreferences.UseSceneDefault(sceneName);BeetleDisplayPreferences.ReloadProfile();
            if(EditorApplication.isPlaying)BeetleCombatDisplay.InstallScene(UnityEngine.SceneManagement.SceneManager.GetSceneByName(sceneName));
            EditorApplication.QueuePlayerLoopUpdate();SceneView.RepaintAll();
        }
        public static void ApplyThreePixelLevelDefaults()
        {
            var profile=EnsureProfile();Undo.RecordObject(profile,"Use second dither pixel level by default");
            foreach(var setting in profile.scenes)
            {
                setting.pixelSize=2;
                foreach(var material in new[]{setting.ditherMaterial,setting.denseDitherMaterial})
                {
                    if(!material||!material.HasProperty("_PixelSize"))continue;
                    Undo.RecordObject(material,"Use 2px dither default");material.SetFloat("_PixelSize",2);
                    EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);
                }
            }
            EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);BeetleDisplayPreferences.ReloadProfile();
        }
        // Run once after importing the new shader; rerunning keeps authored choices and edits.
        public static int EnsureDenseDitherMaterials()
        {
            var profile=EnsureProfile();var shader=Resources.Load<Shader>("BeetleDenseDither");
            if(!IsScreenShader(shader))throw new InvalidOperationException("请先导入可用的 BeetleDenseDither shader。");
            EnsureMaterialFolder();Undo.RecordObject(profile,"Add dense dither materials");
            var names=ScenePaths().Select(Path.GetFileNameWithoutExtension).Concat(profile.scenes.Select(s=>s.sceneName)).Distinct().ToArray();
            int added=0;
            foreach(var scene in names)
            {
                var setting=profile.Find(scene);
                if(setting==null){setting=new SceneDisplaySetting{sceneName=scene};profile.scenes.Add(setting);}
                if(setting.denseDitherMaterial)continue;
                string path=MaterialFolder+"/"+scene+"DenseDither.mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!material)
                {
                    material=new Material(shader){name=scene+"DenseDither"};
                    AssetDatabase.CreateAsset(material,path);Undo.RegisterCreatedObjectUndo(material,"Create dense dither material");
                }
                setting.denseDitherMaterial=material;added++;
            }
            if(added>0){EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);}
            BeetleDisplayPreferences.ReloadProfile();return added;
        }
        static void EnsureMaterialFolder()
        {
            if(!AssetDatabase.IsValidFolder(MaterialFolder))AssetDatabase.CreateFolder("Assets/BeetleArena/Resources","WorldDisplayMaterials");
        }
        public static string SceneLabel(string scene)
        {
            switch(scene)
            {
                case "CampaignMainMenu":return "主菜单";case "CampaignSaves":return "存档";
                case "CampaignGreenhouse":return "温室养殖";case "CampaignCatching":return "捕虫";
                case "CampaignMarket":return "市场";case "CampaignLeague":return "联赛准备";
                case "CampaignArena":return "联赛格斗";case "CampaignAuction":return "冠军拍卖";
                case "CampaignSpecimenRoom":return "标本室";case "BeetleArena":return "独立格斗";
                case "BeetleNursery":return "独立养殖";default:return scene;
            }
        }
    }
}
