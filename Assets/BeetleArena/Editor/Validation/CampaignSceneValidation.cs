using System;
using System.Linq;
using BeetleArena;
using BeetleArena.Campaign;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public static class CampaignSceneValidation
{
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Run scene validation outside Play mode.");
        for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)
            throw new InvalidOperationException("Save the current scene before scene validation.");
        var setup=EditorSceneManager.GetSceneManagerSetup();
        Check(Resources.Load<CampaignTransitionProfile>(CampaignTransitionProfile.ResourceName),"transition curve profile included in Resources");
        try
        {
        foreach(CampaignPage page in Enum.GetValues(typeof(CampaignPage)))
        {
            string path="Assets/Scenes/Campaign/"+CampaignApp.SceneName(page)+".unity";
            var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
            var views=UnityEngine.Object.FindObjectsByType<CampaignView>(FindObjectsSortMode.None);
            Check(views.Length==1&&views[0].page==page&&views[0].font&&views[0].backdrop,"page bindings: "+page);
            Check(views[0].backdrop.sceneCamera,"camera bindings: "+page);
            Check(views[0].backdrop.blurShader&&!ShaderUtil.ShaderHasError(views[0].backdrop.blurShader),"background blur shader binding: "+page);
            Check(UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length==1,"single EventSystem: "+page);
            foreach(var root in scene.GetRootGameObjects())foreach(var transform in root.GetComponentsInChildren<Transform>(true))
                Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject)==0,"missing script: "+path+"/"+transform.name);
            if(page==CampaignPage.Arena)
            {
                var bridge=views[0].leagueBridge;Check(bridge&&bridge.controller&&!bridge.controller.enabled,"external league driver");
                Check(bridge.controller.settings.supportFootOffsets.Length==6&&bridge.controller.actors.Length==2&&bridge.controller.hud&&bridge.controller.spectatorCamera,"preserved combat references");
            }
            else
            {
                Check(UnityEngine.Object.FindObjectsByType<BeetleHusbandryController>(FindObjectsSortMode.None).Length==0,"no duplicate husbandry clock");
                foreach(var renderer in scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<Renderer>(true)))
                    if(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                        foreach(var material in renderer.sharedMaterials)if(material)
                        {
                            string expected=page==CampaignPage.MainMenu&&CampaignIridescenceSetup.IsIridescentSurface(renderer)
                                ? CampaignIridescenceSetup.ShaderName : BeetleArenaToonSetup.ShaderName;
                            Check(HusbandryTerrariumSetup.IsTerrariumMaterial(material)||material.shader.name==expected,
                                "world material: "+page+"/"+renderer.name);
                        }
            }
        }
        Check(!ShaderUtil.ShaderHasError(Shader.Find(BeetleArenaToonSetup.ShaderName)),"toon shader");
        CampaignIridescenceSetup.CheckShader();
        Debug.Log("Campaign scene contracts PASS: "+Enum.GetValues(typeof(CampaignPage)).Length+" scenes, no missing scripts, one EventSystem each, combat calibration and world bindings.");
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
    }
    static void Check(bool passed,string name){if(!passed)throw new InvalidOperationException("Campaign scene contract failed: "+name);}
}
