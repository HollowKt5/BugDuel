using System;
using System.IO;
using System.Linq;
using BeetleArena;
using BeetleArena.Campaign;
using BugDuel.Campaign;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using CampaignSpecimen=BeetleArena.Campaign.CampaignSpecimen;

public static class CampaignSceneBuilder
{
    const string Folder="Assets/Scenes/Campaign";
    [MenuItem("Tools/甲壳虫斗/场景与安装/安装战役流程", false, 202)]
    public static void Install()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before installing scenes.");
        if(!Application.isBatchMode && SceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Save the current scene before installation.");
        CampaignTransitionSetup.Apply();
        CareGroundingSetup.Apply();
        Directory.CreateDirectory(Folder);Directory.CreateDirectory("Assets/StreamingAssets");
        CampaignSpecimenRoomSetup.Install();
        var font=BeetleArenaSceneBuilder.MakeFont();var blur=Shader.Find("Hidden/BugDuel/CampaignBlur");
        if(!blur||!font)throw new InvalidOperationException("Campaign blur shader or font is missing.");
        var paths=Enum.GetValues(typeof(CampaignPage)).Cast<CampaignPage>().Select(p=>Folder+"/"+CampaignApp.SceneName(p)+".unity").ToArray();
        for(int i=0;i<paths.Length;i++)
        {
            // Idempotent installer: existing authored campaign scenes are never overwritten.
            if(File.Exists(paths[i]))continue;
            var page=(CampaignPage)i;
            string source=page==CampaignPage.Arena?BeetleNurseryNavigation.ArenaScenePath:BeetleNurseryNavigation.NurseryScenePath;
            var original=EditorSceneManager.OpenScene(source,OpenSceneMode.Single);
            if(!EditorSceneManager.SaveScene(original,paths[i],true))throw new IOException("Could not copy "+source);
            var scene=EditorSceneManager.OpenScene(paths[i],OpenSceneMode.Single);
            var go=new GameObject(page+"UI");var view=go.AddComponent<CampaignView>();view.page=page;view.font=font;
            var camera=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).First(c=>c.enabled);
            var backdrop=go.AddComponent<CampaignBackdrop>();backdrop.sceneCamera=camera;backdrop.blurShader=blur;view.backdrop=backdrop;
            if(page==CampaignPage.Arena)
            {
                var match=UnityEngine.Object.FindFirstObjectByType<BeetleMatchController>();match.enabled=false;
                var bridge=go.AddComponent<CampaignLeagueBridge>();bridge.controller=match;view.leagueBridge=bridge;
                // Keep legacy HUD references alive for the unchanged controller, suppress its canvas and shortcuts.
                foreach(var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))canvas.enabled=false;
                foreach(var navigation in UnityEngine.Object.FindObjectsByType<BeetleNurseryNavigation>(FindObjectsSortMode.None))UnityEngine.Object.DestroyImmediate(navigation);
            }
            else
            {
                foreach(var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))UnityEngine.Object.DestroyImmediate(canvas.gameObject);
                foreach(var controller in UnityEngine.Object.FindObjectsByType<BeetleHusbandryController>(FindObjectsSortMode.None))UnityEngine.Object.DestroyImmediate(controller.gameObject);
                foreach(var oldView in UnityEngine.Object.FindObjectsByType<BeetleHusbandryView>(FindObjectsSortMode.None))UnityEngine.Object.DestroyImmediate(oldView);
                var rig=UnityEngine.Object.FindFirstObjectByType<BeetleProceduralAnimator>();
                if(!rig)throw new InvalidOperationException("Nursery model rig is missing.");
                var specimen=rig.transform.root.gameObject.AddComponent<CampaignSpecimen>();specimen.rig=rig;specimen.selected=page==CampaignPage.Greenhouse;
                Place(specimen.transform,camera,page==CampaignPage.MainMenu?.7f:page==CampaignPage.Greenhouse?.845f:.75f,.5f);
                if(page==CampaignPage.MainMenu)
                {
                    specimen.transform.rotation=Quaternion.Euler(0,75,0);
                }
                camera.orthographicSize=page==CampaignPage.MainMenu?3.6f:4.2f;
                InstallDepthOfField(go,camera);
            }
            if(!EditorSceneManager.SaveScene(scene))throw new IOException("Could not save "+paths[i]);
        }
        string content="Assets/StreamingAssets/campaign-content.json";
        if(!File.Exists(content))File.WriteAllText(content,JsonUtility.ToJson(CampaignCatalog.Defaults(),true));
        var existing=EditorBuildSettings.scenes.Where(x=>!paths.Contains(x.path)).ToList();
        EditorBuildSettings.scenes=paths.Select(x=>new EditorBuildSettingsScene(x,true)).Concat(existing).ToArray();
        AssetDatabase.Refresh();AssetDatabase.SaveAssets();
        CampaignIridescenceSetup.Apply();
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(paths[0]);
        EditorSceneManager.OpenScene(paths[0],OpenSceneMode.Single);
        Debug.Log("Campaign flow installed: "+paths.Length+" scenes; combat source and original scenes preserved.");
    }
    static void Place(Transform item,Camera camera,float x,float y)
    {
        var ray=camera.ViewportPointToRay(new Vector3(x,y,0));var plane=new Plane(Vector3.up,Vector3.zero);
        if(plane.Raycast(ray,out float distance))item.position=ray.GetPoint(distance);
    }
    static void InstallDepthOfField(GameObject go,Camera camera)
    {
        var volume=go.AddComponent<Volume>();volume.isGlobal=true;volume.priority=1;var profile=ScriptableObject.CreateInstance<VolumeProfile>();
        var effect=profile.Add<DepthOfField>();effect.mode.Override(DepthOfFieldMode.Gaussian);effect.gaussianStart.Override(13);effect.gaussianEnd.Override(25);effect.gaussianMaxRadius.Override(.6f);
        string path="Assets/BeetleArena/Generated/CampaignDepthOfField.asset";
        var existing=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if(!existing){AssetDatabase.CreateAsset(profile,path);AssetDatabase.AddObjectToAsset(effect,profile);existing=profile;}else{UnityEngine.Object.DestroyImmediate(effect);UnityEngine.Object.DestroyImmediate(profile);}
        volume.sharedProfile=existing;camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
        var toggle=go.AddComponent<CampaignDepthOfFieldToggle>();toggle.target=volume;
    }
}
