using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace BeetleArena.EditorTools
{
    public sealed class WorldDisplayWindow:EditorWindow
    {
        const string Folder="Assets/BeetleArena/Editor/Presentation/";
        WorldDisplayProfile profile;
        List<string> scenes;
        List<Shader> shaders;
        [SerializeField] string selectedScene;
        T Q<T>(string name) where T:VisualElement=>rootVisualElement.Q<T>(name);
        string SceneName=>Path.GetFileNameWithoutExtension(scenes[Q<DropdownField>("scene").index]);
        Shader SelectedShader=>Q<DropdownField>("shader").index==0?null:shaders[Q<DropdownField>("shader").index-1];
        [MenuItem("Tools/Beetle Arena/Scene Display Settings")]
        public static void Open()=>GetWindow<WorldDisplayWindow>("场景画面").Show();
        void OnEnable()=>Undo.undoRedoPerformed+=OnUndo;
        void OnDisable()=>Undo.undoRedoPerformed-=OnUndo;
        public void CreateGUI()
        {
            rootVisualElement.Clear();AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Folder+"WorldDisplayWindow.uxml").CloneTree(rootVisualElement);
            minSize=new Vector2(520,560);
            Q<ObjectField>("material").objectType=typeof(Material);Q<ObjectField>("material").allowSceneObjects=false;
            Q<DropdownField>("scene").RegisterValueChangedCallback(_=>ReadScene());
            Q<DropdownField>("shader").RegisterValueChangedCallback(_=>{Q<ObjectField>("material").SetValueWithoutNotify(null);ShaderChanged();});
            Q<ObjectField>("material").RegisterValueChangedCallback(_=>ShaderChanged());
            Q<Button>("active").clicked+=()=>SelectScene(SceneManager.GetActiveScene().name);
            Q<Button>("refresh").clicked+=RefreshCatalog;
            Q<Button>("apply").clicked+=ApplySelection;
            Q<Button>("clean").clicked+=()=>{Q<DropdownField>("shader").index=0;ApplySelection();};
            Q<Button>("inspect-profile").clicked+=()=>Selection.activeObject=profile;
            Q<Button>("inspect-material").clicked+=()=>Selection.activeObject=Q<ObjectField>("material").value;
            RefreshCatalog();
        }
        void RefreshCatalog()
        {
            profile=WorldDisplayEditorService.EnsureProfile();scenes=WorldDisplayEditorService.ScenePaths();shaders=WorldDisplayEditorService.DiscoverShaders();
            Q<DropdownField>("scene").choices=scenes.Select(path=>WorldDisplayEditorService.SceneLabel(Path.GetFileNameWithoutExtension(path))+" · "+Path.GetFileNameWithoutExtension(path)).ToList();
            Q<DropdownField>("shader").choices=new[]{"清晰原画（关闭效果）"}.Concat(shaders.Select(s=>s.name)).ToList();
            SelectScene(selectedScene??SceneManager.GetActiveScene().name);
        }
        void SelectScene(string name)
        {
            int index=scenes.FindIndex(path=>Path.GetFileNameWithoutExtension(path)==name);
            Q<DropdownField>("scene").SetValueWithoutNotify(Q<DropdownField>("scene").choices[Mathf.Max(0,index)]);ReadScene();
        }
        void ReadScene()
        {
            selectedScene=SceneName;
            var setting=profile.Find(selectedScene)??new SceneDisplaySetting{sceneName=selectedScene,style=BeetleDisplayPreferences.GetStyle(selectedScene),pixelSize=BeetleDisplayPreferences.GetPixelSize(selectedScene)};
            var shader=WorldDisplayEditorService.ShaderFor(setting);int index=shader?shaders.IndexOf(shader)+1:0;
            Q<DropdownField>("shader").SetValueWithoutNotify(Q<DropdownField>("shader").choices[index]);
            Q<ObjectField>("material").SetValueWithoutNotify(BeetleDisplayPreferences.IsDither(setting.style)?setting.DitherMaterial(setting.style):setting.customMaterial);
            Q<Label>("scene-path").text=scenes[Q<DropdownField>("scene").index];ShaderChanged();
            Q<Label>("status").text=$"{WorldDisplayEditorService.SceneLabel(selectedScene)}：项目配置 {BeetleDisplayPreferences.StyleName(setting.style)}\n当前生效 {BeetleDisplayPreferences.StyleName(BeetleDisplayPreferences.GetStyle(selectedScene))} · 发现 {shaders.Count} 个画面 Shader\n参数在材质 Inspector 调整；游戏内像素设置可覆盖本机场景。";
        }
        void ShaderChanged()
        {
            var shader=SelectedShader;var style=WorldDisplayEditorService.StyleFor(shader);
            bool materialStyle=style==BeetleDisplayStyle.Custom||BeetleDisplayPreferences.IsDither(style);
            Q<ObjectField>("material").style.display=materialStyle?DisplayStyle.Flex:DisplayStyle.None;
            if(BeetleDisplayPreferences.IsDither(style)&&!Q<ObjectField>("material").value)
                Q<ObjectField>("material").SetValueWithoutNotify(profile.Find(SceneName)?.DitherMaterial(style));
            Q<Button>("inspect-material").SetEnabled(materialStyle&&Q<ObjectField>("material").value);
            Q<Label>("shader-path").text=shader?AssetDatabase.GetAssetPath(shader):"世界画面直接显示原始纹理，不使用屏幕效果。";
        }
        public void ApplySelection()
        {
            try
            {
                var material=Q<ObjectField>("material").value as Material;
                var style=WorldDisplayEditorService.StyleFor(SelectedShader);
                int pixels=material&&material.HasProperty("_PixelSize")?Mathf.RoundToInt(material.GetFloat("_PixelSize")):
                    style==BeetleDisplayStyle.DenseDither?1:profile.Find(SceneName)?.pixelSize??3;
                WorldDisplayEditorService.Apply(profile,SceneName,SelectedShader,pixels,material);
                ReadScene();Q<Label>("status").text="已保存并应用，仅影响所选场景。\n"+Q<Label>("status").text;
            }
            catch(ArgumentException error){Q<Label>("status").text=error.Message;}
            catch(Exception error){Debug.LogException(error);Q<Label>("status").text="应用失败，请查看 Console。";}
        }
        void OnUndo()
        {
            if(!profile||Q<DropdownField>("scene")==null)return;
            AssetDatabase.SaveAssetIfDirty(profile);BeetleDisplayPreferences.ReloadProfile();ReadScene();EditorApplication.QueuePlayerLoopUpdate();
        }
    }
}
