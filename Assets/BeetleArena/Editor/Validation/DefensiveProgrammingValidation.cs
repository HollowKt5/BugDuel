using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;

// Focused regression suite: no Play run, user saves, or authored asset changes.
public static class DefensiveProgrammingValidation
{
    [MenuItem("Tools/甲壳虫斗/验证/架构与性能/边界与防御检查", false, 742)]
    public static void Run()
    {
        var report = new StringBuilder("DEFENSIVE PROGRAMMING REFACTOR / " + DateTimeOffset.Now.ToString("o") + "\n");
        var failures = new List<Exception>();
        void Check(string name, Action action)
        {
            try { action(); report.AppendLine("PASS: " + name); }
            catch (Exception error) { report.AppendLine("FAIL: " + name + "\n" + error); failures.Add(error); }
        }
        Check("Domain values, save cleanup and lifecycle compatibility", BeetleDomainValidation.Run);
        Check("Breeding isolation, exception identity and atomic rejection", () => BreedingRegressionValidation.Run());
        Check("Content hot reload and stage-specific habitat relocation", BeetleContentValidation.Run);
        Check("Roster transactions and recovery", BeetleRosterSessionValidation.Run);
        Check("Feeding, overturn rescue and time partitioning", BeetleHusbandryInteractionValidation.Run);
        Check("Roster persistence", BeetlePersistenceValidation.Run);
        Check("Campaign transaction exception diagnostics", CampaignExceptionValidation.Run);
        Check("Campaign data and gameplay rules", CampaignDataValidation.Run);
        Check("Mailbox transactions and state validation", CampaignMailboxValidation.Run);
        Check("Contact metadata and collider lifecycle", BeetleContactMetadataValidation.Run);
        report.AppendLine(failures.Count == 0 ? "ALL CHECKS PASSED" : "FAILED GROUPS: " + failures.Count);
        Directory.CreateDirectory("Artifacts/DefensiveReview");
        File.WriteAllText("Artifacts/DefensiveReview/Verification.txt", report.ToString());
        if (failures.Count != 0) throw new AggregateException("Defensive programming regressions", failures);
    }
}
