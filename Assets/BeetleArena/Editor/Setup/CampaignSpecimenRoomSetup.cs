using System;
using System.Linq;
using BeetleArena.Campaign;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class CampaignSpecimenRoomSetup
{
    public const string ScenePath="Assets/Scenes/Campaign/CampaignSpecimenRoom.unity";
    [MenuItem("Tools/甲壳虫斗/场景与安装/安装标本室", false, 203)]
    public static void Install()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before installing the specimen room.");
        if(!System.IO.File.Exists(ScenePath))
        {
            var previous=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var cameraObject=new GameObject("GalleryCamera",typeof(Camera),typeof(AudioListener));
            var camera=cameraObject.GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.farClipPlane=100;
            var presentation=new GameObject("SpecimenRoomUI");
            var backdrop=presentation.AddComponent<CampaignBackdrop>();backdrop.sceneCamera=camera;backdrop.blurShader=Shader.Find("Hidden/BugDuel/CampaignBlur");
            var font=Resources.Load<TMP_FontAsset>("RetroUiFont");
            var room=presentation.AddComponent<CampaignSpecimenRoom>();room.font=font;
            room.beetlePrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BeetleArena/Generated/ArticulatedBeetle.prefab");
            if(!room.beetlePrefab||!font)throw new InvalidOperationException("The gallery requires the existing beetle prefab and retro font.");
            var view=presentation.AddComponent<CampaignView>();view.page=CampaignPage.SpecimenRoom;view.font=font;view.backdrop=backdrop;
            var events=new GameObject("CampaignEvents",typeof(UnityEngine.EventSystems.EventSystem),typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            events.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();
            var key=new GameObject("GalleryKeyLight",typeof(Light));key.transform.rotation=Quaternion.Euler(32,-28,0);
            var light=key.GetComponent<Light>();light.type=LightType.Directional;light.color=new Color(1,.89f,.72f);light.intensity=1.5f;light.shadows=LightShadows.Soft;
            var fill=new GameObject("GalleryFillLight",typeof(Light));fill.transform.rotation=Quaternion.Euler(-22,40,0);
            var fillLight=fill.GetComponent<Light>();fillLight.type=LightType.Directional;fillLight.color=new Color(.67f,.81f,1);fillLight.intensity=.45f;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.38f,.42f,.44f);
            EditorSceneManager.SaveScene(scene,ScenePath);EditorSceneManager.CloseScene(scene,true);
            if(previous.IsValid())SceneManager.SetActiveScene(previous);
        }
        var scenes=EditorBuildSettings.scenes.ToList();
        var existing=scenes.FirstOrDefault(s=>s.path==ScenePath);
        if(existing==null)scenes.Add(new EditorBuildSettingsScene(ScenePath,true));else existing.enabled=true;
        EditorBuildSettings.scenes=scenes.ToArray();AssetDatabase.SaveAssets();
    }
}
