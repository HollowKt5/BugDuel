using System;
using System.IO;
using System.Text;
using BeetleArena;
using BugDuel.Domain;
using UnityEditor;
using UnityEngine;

/// <summary>Operates exclusively in a unique temporary directory without touching user saves; covers structural rejection and atomic replacement failures.</summary>
public static class BeetlePersistenceValidation
{
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Reject<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception(message);
    }

    [MenuItem("Tools/甲壳虫斗/验证/存档与数据/名册持久化", false, 660)]
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "BugDuel-Persistence-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "roster.json");
        string neighbor = Path.Combine(directory, "unrelated.json");
        var report = new StringBuilder("ROSTER PERSISTENCE CONTRACTS\n" + DateTimeOffset.Now.ToString("o") + "\n");
        Directory.CreateDirectory(directory);
        try
        {
            Reject<ArgumentException>(() => new BeetleRosterJsonStore("roster.json"), "Relative path was accepted");
            var store = new BeetleRosterJsonStore(path);
            Reject<FileNotFoundException>(() => store.Load(), "Missing save was silently fabricated");
            var definition = new SpeciesDefinition("test-species", "测试物种", 55, 8);
            var individual = BeetleIndividual.Create(definition, "beetle-1", "测试甲虫", BeetleSex.Female);
            var data = new BeetleRosterSaveData
            {
                Individuals = new[] { BeetleSaveMapper.Export(individual) },
                AppliedMatchIds = new[] { "match-1" },
                HabitatSelections = new[] { new BeetleHabitatSelectionData { IndividualId = "beetle-1", HabitatId = "warm-soil" } }
            };
            File.WriteAllText(neighbor, "do not change", Encoding.UTF8);
            store.Save(data);
            var first = store.Load();
            Require(first.Version == 1 && first.Individuals.Length == 1 && first.Individuals[0].Id == "beetle-1" &&
                first.Individuals[0].DisplayName == "测试甲虫" && first.AppliedMatchIds.Length == 1 && first.AppliedMatchIds[0] == "match-1" &&
                first.HabitatSelections.Length == 1 && first.HabitatSelections[0].IndividualId == "beetle-1" &&
                first.HabitatSelections[0].HabitatId == "warm-soil",
                "Round trip lost identity, Unicode, version, settlement ledger or habitat selection");
            var restored = BeetleSaveMapper.Restore(first.Individuals[0], new SpeciesCatalog(new[] { definition }));
            Require(restored.Success, "Round trip no longer restores as a domain individual: " +
                restored.ErrorCode + " / " + restored.ErrorMessage + "\nJSON: " + File.ReadAllText(path));
            Require(first.Individuals[0].MotherId == null && first.Individuals[0].FatherId == null,
                "Optional parent strings did not round trip as absent parents");
            // Genuine non-empty lineage must be preserved; only strictly empty strings may be restored as absent.
            var inherited = BeetleSaveMapper.Export(individual);
            inherited.Id = "child-1";
            inherited.MotherId = "mother-1"; inherited.FatherId = "father-1";
            inherited.AncestorIds = new[] { "mother-1", "father-1", "grandparent-1" };
            store.Save(new BeetleRosterSaveData { Individuals = new[] { inherited } });
            var loadedChild = store.Load().Individuals[0];
            Require(loadedChild.MotherId == "mother-1" && loadedChild.FatherId == "father-1" && loadedChild.AncestorIds.Length == 3 &&
                BeetleSaveMapper.Restore(loadedChild, new SpeciesCatalog(new[] { definition })).Success,
                "Non-empty pedigree was lost during JSON normalization");
            inherited.MotherId = " ";
            store.Save(new BeetleRosterSaveData { Individuals = new[] { inherited } });
            Require(!BeetleSaveMapper.Restore(store.Load().Individuals[0], new SpeciesCatalog(new[] { definition })).Success,
                "Malformed non-empty parent was silently cleared");
            first.Individuals[0].DisplayName = "修改后的甲虫";
            first.AppliedMatchIds = new[] { "match-1", "match-2" };
            store.Save(first);
            Require(store.Load().Individuals[0].DisplayName == "修改后的甲虫" && store.Load().AppliedMatchIds.Length == 2,
                "Existing-file replacement did not commit the entire roster");
            string accepted = File.ReadAllText(path);
            report.AppendLine("PASS: explicit paths, missing file, Unicode/domain round trip, optional-parent normalization, intact pedigree, joint roster and ledger replacement.");

            data.Individuals = new[] { first.Individuals[0], first.Individuals[0] };
            Reject<InvalidDataException>(() => store.Save(data), "Duplicate individual IDs were accepted on save");
            Require(File.ReadAllText(path) == accepted, "Rejected save modified the old file");
            data.Individuals = new BeetleSaveData[] { null };
            Reject<InvalidDataException>(() => store.Save(data), "Null individual was accepted on save");
            data.Individuals = first.Individuals;
            data.AppliedMatchIds = new[] { "match-1", "match-1" };
            Reject<InvalidDataException>(() => store.Save(data), "Duplicate settlement IDs were accepted on save");
            data.AppliedMatchIds = new[] { " " };
            Reject<InvalidDataException>(() => store.Save(data), "Blank settlement ID was accepted on save");
            data.AppliedMatchIds = new string[] { null };
            Reject<InvalidDataException>(() => store.Save(data), "Null settlement ID was accepted on save");
            data.AppliedMatchIds = null;
            Reject<InvalidDataException>(() => store.Save(data), "Missing ledger array was accepted on save");
            data.AppliedMatchIds = Array.Empty<string>();
            data.HabitatSelections = null;
            Reject<InvalidDataException>(() => store.Save(data), "Missing habitat-selection array was accepted on save");
            data.HabitatSelections = new BeetleHabitatSelectionData[] { null };
            Reject<InvalidDataException>(() => store.Save(data), "Null habitat selection was accepted on save");
            data.HabitatSelections = new[] { first.HabitatSelections[0], first.HabitatSelections[0] };
            Reject<InvalidDataException>(() => store.Save(data), "Conflicting duplicate habitat assignments were accepted on save");
            data.HabitatSelections = new[] { new BeetleHabitatSelectionData { IndividualId = "missing-beetle", HabitatId = "warm-soil" } };
            Reject<InvalidDataException>(() => store.Save(data), "Habitat selection for unknown individual was accepted on save");
            data.HabitatSelections = new[] { new BeetleHabitatSelectionData { IndividualId = "beetle-1", HabitatId = " " } };
            Reject<InvalidDataException>(() => store.Save(data), "Blank habitat ID was accepted on save");
            data.HabitatSelections = first.HabitatSelections;
            data.Version = 2;
            Reject<NotSupportedException>(() => store.Save(data), "Unknown save version was accepted");
            Require(File.ReadAllText(path) == accepted, "Invalid inputs overwrote the accepted roster");
            report.AppendLine("PASS: invalid save records, missing arrays and unsupported version preserve existing bytes.");

            foreach (string broken in new[]
            {
                "", "not JSON", "{ broken", "[]", "null",
                "{\"Version\":1,\"Individuals\":[],\"HabitatSelections\":[]}",
                "{\"Version\":1,\"AppliedMatchIds\":[],\"HabitatSelections\":[]}",
                "{\"Version\":1,\"Individuals\":[],\"AppliedMatchIds\":[]}",
                "{\"Version\":1,\"Individuals\":[null],\"AppliedMatchIds\":[],\"HabitatSelections\":[]}",
                "{\"Version\":1,\"Individuals\":[{\"Id\":\"same\"},{\"Id\":\"same\"}],\"AppliedMatchIds\":[],\"HabitatSelections\":[]}",
                "{\"Version\":1,\"Individuals\":[],\"AppliedMatchIds\":[\"same\",\"same\"],\"HabitatSelections\":[]}",
                "{\"Version\":1,\"Individuals\":[],\"AppliedMatchIds\":[null],\"HabitatSelections\":[]}",
                "{\"Version\":1,\"Individuals\":[],\"AppliedMatchIds\":[],\"HabitatSelections\":[null]}",
                "{\"Version\":1,\"Individuals\":[],\"AppliedMatchIds\":[],\"HabitatSelections\":[{\"IndividualId\":\"unknown\",\"HabitatId\":\"soil\"}]}",
                "{\"Version\":1,\"Individuals\":[{\"Id\":\"beetle\"}],\"AppliedMatchIds\":[],\"HabitatSelections\":[{\"IndividualId\":\"beetle\",\"HabitatId\":\"soil\"},{\"IndividualId\":\"beetle\",\"HabitatId\":\"wood\"}]}"
            })
            {
                File.WriteAllText(path, broken);
                Reject<InvalidDataException>(() => store.Load(), "Malformed or structurally invalid save was accepted: " + broken);
                Require(File.ReadAllText(path) == broken, "Load repaired or overwrote rejected input");
            }
            foreach (string unsupported in new[]
            {
                "{\"Version\":2,\"Individuals\":[],\"AppliedMatchIds\":[],\"HabitatSelections\":[]}",
                "{\"Individuals\":[],\"AppliedMatchIds\":[],\"HabitatSelections\":[]}"
            })
            {
                File.WriteAllText(path, unsupported);
                Reject<NotSupportedException>(() => store.Load(), "Unknown or absent container version was accepted");
            }
            File.WriteAllText(path, accepted);
            report.AppendLine("PASS: corrupt/missing/duplicate JSON data and unknown/absent versions rejected without modifying files.");

            // Windows file share modes can explicitly forbid delete/replace; Unix allows unlinking open inodes.
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    data.Version = 1; data.AppliedMatchIds = new[] { "would-replace" };
                    Reject<IOException>(() => store.Save(data), "Replacing a locked destination unexpectedly succeeded");
                }
                Require(File.ReadAllText(path) == accepted, "Failed atomic replacement damaged the original save");
                report.AppendLine("PASS: locked destination rejects replacement and preserves original bytes (Windows).");
            }
            Require(Directory.GetFiles(directory).Length == 2, "Save left a temporary file behind");
            Require(File.ReadAllText(neighbor) == "do not change", "Save modified an unrelated neighbor file");
            report.AppendLine("PASS: no temporary files remain; unrelated neighbor files are intact.");
            report.AppendLine("ALL ROSTER PERSISTENCE CHECKS PASSED");
        }
        finally
        {
            // All deletion targets constructed from unique GUID folder and static filename, never recursively deleting foreign files.
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(neighbor)) File.Delete(neighbor);
            if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory);
            Directory.CreateDirectory("Artifacts/ArchitectureReview");
            File.WriteAllText("Artifacts/ArchitectureReview/Persistence.txt", report.ToString());
        }
        Debug.Log("All roster persistence checks passed.");
    }
}
