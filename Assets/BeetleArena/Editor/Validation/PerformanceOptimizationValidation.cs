using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PerformanceOptimizationValidation
{
    [MenuItem("Tools/甲壳虫斗/验证/架构与性能/性能优化", false, 743)]
    public static void Run()
    {
        Directory.CreateDirectory("Artifacts/Performance");
        // Batch starts with an unsaved untitled scene, which Unity refuses to keep
        // open while the save-isolation fixture creates an additive empty scene.
        if(Application.isBatchMode)EditorSceneManager.OpenScene("Assets/Scenes/Campaign/CampaignMainMenu.unity");
        CampaignSnapshotValidation.Run();
        CampaignDataValidation.Run();
        CampaignSaveCommitValidation.Run();
        CampaignThumbnailValidation.Run();
        BeetleContactEscapeValidation.Run();
        BeetleUnderbodyEscapeValidation.Run();
        File.WriteAllText("Artifacts/Performance/Validation.txt", DateTimeOffset.Now.ToString("o") +
            "\nPASS: snapshot graph, campaign data, durable save isolation, thumbnail GPU readback, captured contact escape at 0/1/4 renders, underbody withdrawal.\n");
        Debug.Log("PERFORMANCE OPTIMIZATION CONTRACTS PASS");
    }
}
