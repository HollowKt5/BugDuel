using System;
using System.IO;
using System.Linq;
using BeetleArena.Campaign;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CampaignToonSetup
{
    const string Folder="Assets/BeetleArena/Generated/CampaignMaterials";
    [MenuItem("Tools/Beetle Arena/Apply Campaign Opaque UI and Toon World")]
    public static void Apply()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before changing scene materials.");
        for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save the open scene before applying campaign materials.");
        var setup=EditorSceneManager.GetSceneManagerSetup();
        Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        try
        {
            foreach(CampaignPage page in Enum.GetValues(typeof(CampaignPage)))
            {
                if(page==CampaignPage.Arena)continue;
                var scene=EditorSceneManager.OpenScene("Assets/Scenes/Campaign/"+CampaignApp.SceneName(page)+".unity",OpenSceneMode.Single);
                foreach(var renderer in scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<Renderer>(true)))
                {
                    if(!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))continue;
                    // The menu installer restores the exact source horn materials itself.
                    if(page==CampaignPage.MainMenu && CampaignIridescenceSetup.IsHorn(renderer))continue;
                    var materials=renderer.sharedMaterials;
                    for(int i=0;i<materials.Length;i++)
                    {
                        var source=materials[i]; if(!source)continue;
                        if(HusbandryTerrariumSetup.IsTerrariumMaterial(source))continue;
                        if(source.shader && (source.shader.name==BeetleArenaToonSetup.ShaderName ||
                            page==CampaignPage.MainMenu && CampaignIridescenceSetup.IsIridescentSurface(renderer) && source.shader.name==CampaignIridescenceSetup.ShaderName))continue;
                        string sourcePath=AssetDatabase.GetAssetPath(source);
                        string id=AssetDatabase.AssetPathToGUID(sourcePath);
                        if(string.IsNullOrEmpty(id))throw new InvalidOperationException("Unpersisted world material: "+source.name);
                        string path=Folder+"/"+id+".mat";
                        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                        if(!material)
                        {
                            material=new Material(source); material.name=source.name;
                            AssetDatabase.CreateAsset(material,path); BeetleArenaToonSetup.Configure(material);
                        }
                        materials[i]=material;
                    }
                    renderer.sharedMaterials=materials; EditorUtility.SetDirty(renderer);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
                if(page==CampaignPage.MainMenu)CampaignIridescenceSetup.ApplyToScene(scene);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        Debug.Log("Campaign toon world installed in seven menu/management scenes; UI uses its default material.");
    }
}
