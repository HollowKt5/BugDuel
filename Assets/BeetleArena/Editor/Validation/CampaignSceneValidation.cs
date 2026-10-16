using System;
using System.Linq;
using BeetleArena;
using BeetleArena.Campaign;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

public static class CampaignSceneValidation
{
    public static void Run()
    {
        // Refresh the shared CJK atlas and retain all already-authored campaign scenes.
        CampaignSceneBuilder.Install();
        foreach(CampaignPage page in Enum.GetValues(typeof(CampaignPage)))
        {
            string path="Assets/Scenes/Campaign/"+CampaignApp.SceneName(page)+".unity";
            var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
            var views=UnityEngine.Object.FindObjectsByType<CampaignView>(FindObjectsSortMode.None);
            Check(views.Length==1&&views[0].page==page&&views[0].font&&views[0].glassShader&&views[0].backdrop,"page bindings: "+page);
            Check(views[0].backdrop.sceneCamera&&views[0].backdrop.blurShader,"camera bindings: "+page);
            Check(UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length==1,"single EventSystem: "+page);
            foreach(var root in scene.GetRootGameObjects())foreach(var transform in root.GetComponentsInChildren<Transform>(true))
                Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject)==0,"missing script: "+path+"/"+transform.name);
            if(page==CampaignPage.Arena)
            {
                var bridge=views[0].leagueBridge;Check(bridge&&bridge.controller&&!bridge.controller.enabled,"external league driver");
                Check(bridge.controller.settings.supportFootOffsets.Length==6&&bridge.controller.actors.Length==2&&bridge.controller.hud&&bridge.controller.spectatorCamera,"preserved combat references");
            }
            else Check(UnityEngine.Object.FindObjectsByType<BeetleHusbandryController>(FindObjectsSortMode.None).Length==0,"no duplicate husbandry clock");
        }
        Check(!ShaderUtil.ShaderHasError(Shader.Find("BugDuel/CampaignGlass"))&&!ShaderUtil.ShaderHasError(Shader.Find("Hidden/BugDuel/CampaignBlur")),"glass shaders");
        EditorSceneManager.OpenScene("Assets/Scenes/Campaign/CampaignMainMenu.unity",OpenSceneMode.Single);
        Debug.Log("Campaign scene contracts PASS: all 8 scene bindings, no missing scripts, one EventSystem each, isolated care clocks, combat calibration, glass shaders.");
    }
    static void Check(bool passed,string name){if(!passed)throw new InvalidOperationException("Campaign scene contract failed: "+name);}
}
