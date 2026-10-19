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
                if(page==CampaignPage.MainMenu)
                {
                    var camera=UnityEngine.Object.FindFirstObjectByType<CampaignBackdrop>().sceneCamera;
                    var specimens=UnityEngine.Object.FindObjectsByType<CampaignSpecimen>(FindObjectsSortMode.None).OrderBy(x=>x.phase).ToArray();
                    for(int i=0;i<specimens.Length;i++)
                    {
                        var ray=camera.ViewportPointToRay(new Vector3(i==0?.63f:.86f,i==0?.56f:.44f,0));
                        if(new Plane(Vector3.up,Vector3.zero).Raycast(ray,out float distance))specimens[i].transform.position=ray.GetPoint(distance);
                        specimens[i].transform.localScale=Vector3.one*.8f;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(specimens[i].transform);
                    }
                }
                foreach(var renderer in scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<Renderer>(true)))
                {
                    if(!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))continue;
                    var materials=renderer.sharedMaterials;
                    for(int i=0;i<materials.Length;i++)
                    {
                        var source=materials[i]; if(!source)continue;
                        if(source.shader && (source.shader.name==BeetleArenaToonSetup.ShaderName ||
                            page==CampaignPage.MainMenu && CampaignIridescenceSetup.IsShell(renderer) && source.shader.name==CampaignIridescenceSetup.ShaderName))continue;
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
