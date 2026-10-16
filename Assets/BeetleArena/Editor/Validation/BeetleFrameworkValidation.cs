using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>只验证，不重建正式场景或覆盖参数资产。适用于菜单和 -executeMethod。</summary>
public static class BeetleFrameworkValidation
{
    [MenuItem("Tools/Beetle Arena/Validate Extension Framework")]
    public static void Run()
    {
        var report = new StringBuilder("EXTENSION FRAMEWORK / " + DateTimeOffset.Now.ToString("o") + "\n");
        int failures = 0;
        void Check(string label, Action validation)
        {
            try { validation(); report.AppendLine("PASS: " + label); }
            catch (Exception error) { failures++; report.AppendLine("FAIL: " + label + "\n" + error); Debug.LogException(error); }
        }
        Check("Husbandry domain", BeetleDomainValidation.Run);
        Check("Feeding and overturn rescue", BeetleHusbandryInteractionValidation.Run);
        Check("Species integration", BeetleSpeciesIntegrationValidation.Run);
        Check("Roster transactions", BeetleRosterSessionValidation.Run);
        Check("Habitats and live content updates", BeetleContentValidation.Run);
        Check("Shipped example content package", () =>
        {
            var session = new BeetleArena.BeetleRosterSession(BeetleArena.BeetleContentExamples.Create(),
                new System.Collections.Generic.Dictionary<string, BeetleArena.BeetleCombatProfile>());
            var result = new BeetleArena.BeetleContentService(session).TryReloadFile(Path.GetFullPath("Content/Examples/BeetleContent.v2.json"));
            if (!result.Success || session.Content.Version != 2) throw new Exception(result.ErrorMessage ?? "Example revision was not applied.");
        });
        Check("Contact metadata", BeetleContactMetadataValidation.Run);
        Check("Persistence", BeetlePersistenceValidation.Run);
        Check("Camera follow", BeetleCameraValidation.Run);
        Check("Existing simulation rules", BeetleArenaValidation.Run);
        Check("Existing foreleg contact rules", BeetleForelegValidation.ContactsInProcess);
        Check("Existing lever throws", BeetleThrowValidation.Run);
        Check("Existing architecture contracts", BeetleArchitectureValidation.Run);
        Check("Camera scene previews", BeetleCameraValidation.CapturePreview);
        Directory.CreateDirectory("Artifacts/Framework");
        File.WriteAllText("Artifacts/Framework/Validation.txt", report.ToString());
        if (failures > 0) throw new Exception(failures + " framework validation groups failed.");
    }
}
