using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BeetleArena;
using BugDuel.Domain;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Habitat selection and full-package hot reload contracts, using transient authoring objects only.</summary>
public static class BeetleContentValidation
{
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    [MenuItem("Tools/甲壳虫斗/验证/存档与数据/内容重载与栖息地", false, 666)]
    public static void Run()
    {
        var scene = SceneManager.GetActiveScene();
        int sceneCount = SceneManager.sceneCount;
        bool wasDirty = scene.isDirty;
        var report = new StringBuilder("CONTENT / HABITAT CONTRACTS\n");
        try
        {
            using (var fixture = new Fixture())
            {
                HabitatSelection(fixture);
                report.AppendLine("PASS: stage-filtered choices, environment effects, unknown/unsuitable choices, relocation and saved habitat selections.");
                Reload(fixture);
                report.AppendLine("PASS: complete JSON revisions, rejected schema/data/version preservation, species/selected habitat removal, lifecycle compatibility and active contests.");
                StageSpecificReload();
                report.AppendLine("PASS: shortened lifecycle can leave a stage-specific box without losing age or consuming forbidden care time.");
                FileReload(fixture);
                report.AppendLine("PASS: explicit file reload, malformed/missing files preserve current content.");
            }
            Require(SceneManager.sceneCount == sceneCount && SceneManager.GetActiveScene() == scene && scene.isDirty == wasDirty,
                "Content validation changed the user's scene state");
            report.AppendLine("ALL CONTENT CHECKS PASSED");
        }
        finally
        {
            Directory.CreateDirectory("Artifacts/Framework");
            File.WriteAllText("Artifacts/Framework/ContentValidation.txt", report.ToString());
        }
    }

    static BeetleIndividual Read(BeetleRosterSession session, string id)
    {
        Require(session.TryGet(id, out var individual), "Missing content fixture individual: " + id);
        return individual;
    }

    static void HabitatSelection(Fixture fixture)
    {
        var session = fixture.Pair();
        var larva = BeetleIndividual.Create(fixture.Definition, "larva", "Larva", BeetleSex.Unknown, LifeStage.Larva);
        var pupa = BeetleIndividual.Create(fixture.Definition, "pupa", "Pupa", BeetleSex.Female, LifeStage.Pupa);
        session.Add(larva); session.Add(pupa);
        Require(session.AvailableHabitats("female").Count == 2 && session.AvailableHabitats("larva").Count == 1,
            "Habitat options were not filtered by lifecycle stage");
        Require(session.AvailableHabitats("unknown").Count == 0, "Unknown individual received habitat options");
        Require(!session.SelectHabitat("female", "missing").Success && !session.SelectHabitat("female", "nursery").Success,
            "Unknown or unsuitable habitat was selected");
        Require(session.GetSelectedHabitatId("female") == null, "Rejected selection changed location");
        Require(!session.AdvanceInSelectedHabitat("female", 1).Success, "Unselected environment implicitly advanced care");
        Require(session.SelectHabitat("female", "calm").Success && session.SelectHabitat("male", "harsh").Success,
            "Compatible adult environment could not be selected");
        Require(session.AdvanceInSelectedHabitat("female", .5).Success && session.AdvanceInSelectedHabitat("male", .5).Success,
            "Selected environments did not advance individuals");
        Require(Read(session, "female").Condition.Health > Read(session, "male").Condition.Health
            && Read(session, "female").Condition.Stress < Read(session, "male").Condition.Stress,
            "Different environments had identical care effects");
        string before = JsonUtility.ToJson(session.Export());
        Require(!session.AdvanceInHabitat("female", "unknown", 1).Success, "Unknown environment advanced care");
        Require(JsonUtility.ToJson(session.Export()) == before, "Rejected environment partially changed roster/location");

        string beforeCrossStage = JsonUtility.ToJson(session.Export());
        Require(!session.AdvanceInHabitat("pupa", "nursery", fixture.Definition.Lifecycle.PupaDays + .125).Success,
            "Long offline advance continued past the environment's allowed stages");
        Require(JsonUtility.ToJson(session.Export()) == beforeCrossStage,
            "Relocation-required advance partially aged or moved the individual");
        Require(session.SelectHabitat("pupa", "nursery").Success
            && session.AdvanceInSelectedHabitat("pupa", fixture.Definition.Lifecycle.PupaDays).Success,
            "Developmental environment could not mature pupa");
        Require(Read(session, "pupa").Stage == LifeStage.Adult && session.GetSelectedHabitatId("pupa") == "nursery",
            "Lifecycle transition lost the individual's physical location");
        var saved = session.Export();
        var restored = fixture.Session(); restored.Restore(saved);
        Require(restored.GetSelectedHabitatId("pupa") == "nursery", "Post-transition location could not round trip");
        Require(!restored.AdvanceInSelectedHabitat("pupa", .125).Success, "Matured adult remained in incompatible nursery without relocation");
        Require(restored.SelectHabitat("pupa", "calm").Success && restored.AdvanceInSelectedHabitat("pupa", .125).Success,
            "Relocation did not restore care advancement");
        saved.HabitatSelections[0].HabitatId = "unknown";
        string restoredBefore = JsonUtility.ToJson(restored.Export());
        bool rejected = false;
        try { restored.Restore(saved); } catch (ArgumentException) { rejected = true; }
        Require(rejected && JsonUtility.ToJson(restored.Export()) == restoredBefore, "Invalid saved habitat selection partially replaced roster");
    }

    static void Reload(Fixture fixture)
    {
        var session = fixture.Pair();
        session.SelectHabitat("female", "calm");
        session.SelectHabitat("male", "calm");
        var service = new BeetleContentService(session);
        string valid = service.ExportJson(false);
        foreach (string malformed in new[] { "", "{", "{}", "[]", valid.Replace("\"SchemaVersion\":" + BeetleContentData.CurrentSchemaVersion + ",", "") })
            RejectUpdate(service, session, malformed, "Malformed or schema-less JSON was applied");
        RejectUpdate(service, session, valid, "Current content revision was republished");

        var update = Next(service);
        update.SchemaVersion++;
        RejectUpdate(service, session, JsonUtility.ToJson(update), "Future schema was interpreted as current schema");
        update = Next(service); update.Species[0].NutritionLossPerDay = -1;
        RejectUpdate(service, session, JsonUtility.ToJson(update), "Invalid care rates were published");
        update = Next(service); update.Species = new[] { update.Species[0], update.Species[0] };
        RejectUpdate(service, session, JsonUtility.ToJson(update), "Duplicate species IDs were published");
        update = Next(service); update.Habitats[0].AllowedStages = new[] { 99 };
        RejectUpdate(service, session, JsonUtility.ToJson(update), "Unknown lifecycle stages were published");

        update = Next(service);
        update.Species[0].DisplayName = "Reloaded species";
        update.Species[0].AdultLengthMm = 66;
        update.Species[0].NutritionLossPerDay = 12;
        Require(service.TryApplyJson(JsonUtility.ToJson(update)).Success && service.Current.Version == 2,
            "Valid full content revision could not be published");
        Require(session.GetSelectedHabitatId("female") == "calm", "Compatible update discarded habitat selection");
        double beforeNutrition = Read(session, "female").Condition.Nutrition;
        Require(session.AdvanceInSelectedHabitat("female", .25).Success,
            "Existing individual could not use updated care profile");
        Require(Math.Abs(Read(session, "female").Condition.Nutrition - (beforeNutrition - 3)) < 1e-8,
            "Care kept stale species parameters after update");
        var match = session.BeginMatch("female", "male", fixture.Settings, 5);
        Require(Math.Abs(match.Fighters[0].Traits.lengthMm - 66) < .0001f,
            "New contest used stale ScriptableObject morphology after content update");
        update = Next(service);
        RejectUpdate(service, session, JsonUtility.ToJson(update), "Active contest allowed content publication", allowActive: true);
        Require(!session.SelectHabitat("female", "harsh").Success, "Active participant moved to another habitat");
        session.Cancel(match.MatchId);

        update = Next(service);
        update.Species = new[] { BeetleSpeciesContentData.FromDefinition(new SpeciesDefinition("unused", "Unused", 50, 6)) };
        RejectUpdate(service, session, JsonUtility.ToJson(update), "Species referenced by existing individuals was removed");
        update = Next(service); update.Species[0].EggDays += 1;
        var beforeLifecycleUpdate = Read(session, "female");
        Require(service.TryApplyJson(JsonUtility.ToJson(update)).Success, "Lifecycle tuning rejected existing adults");
        var afterLifecycleUpdate = Read(session, "female");
        Require(afterLifecycleUpdate.TotalAgeDays == beforeLifecycleUpdate.TotalAgeDays &&
            afterLifecycleUpdate.StageAgeDays == beforeLifecycleUpdate.StageAgeDays &&
            afterLifecycleUpdate.Stage == beforeLifecycleUpdate.Stage, "Lifecycle tuning rewrote historical ages or stage");
        Require(session.Feed("female", new FoodPortion(FoodKind.Fruit, 1, 1)).Success &&
            session.MarkOverturned("female").Success && session.HelpRight("female").Success,
            "Lifecycle hot update blocked feeding or righting");
        update = Next(service);
        var habitats = new List<BeetleHabitatContentData>();
        foreach (var habitat in update.Habitats) if (habitat.Id != "calm") habitats.Add(habitat);
        update.Habitats = habitats.ToArray();
        RejectUpdate(service, session, JsonUtility.ToJson(update), "Occupied habitat was removed without relocation");
        Require(session.SelectHabitat("female", "harsh").Success && session.SelectHabitat("male", "harsh").Success,
            "Could not relocate occupants before removing habitat");
        Require(service.TryApplyJson(JsonUtility.ToJson(update)).Success && !service.Current.TryGetHabitat("calm", out _),
            "Unused habitat could not be removed after relocation");

        update = Next(service);
        foreach (var habitat in update.Habitats) if (habitat.Id == "harsh") habitat.AllowedStages = new[] { (int)LifeStage.Larva };
        RejectUpdate(service, session, JsonUtility.ToJson(update), "Habitat rule revision invalidated current living occupants");
        var relocation = Next(service);
        var available = new List<BeetleHabitatContentData>(relocation.Habitats)
        {
            BeetleHabitatContentData.FromDefinition(new HabitatDefinition("refuge", "Replacement enclosure", new HabitatConditions(),
                new[] { LifeStage.Adult }))
        };
        relocation.Habitats = available.ToArray();
        Require(service.TryApplyJson(JsonUtility.ToJson(relocation)).Success, "Replacement enclosure could not be added");
        Require(session.SelectHabitat("female", "refuge").Success && session.SelectHabitat("male", "refuge").Success,
            "Existing occupants could not move before suitability update");
        update = Next(service);
        foreach (var habitat in update.Habitats) if (habitat.Id == "harsh") habitat.AllowedStages = new[] { (int)LifeStage.Larva };
        Require(service.TryApplyJson(JsonUtility.ToJson(update)).Success && session.GetSelectedHabitatId("female") == "refuge"
            && session.AdvanceInSelectedHabitat("female", .1).Success,
            "Relocated occupants could not continue after habitat suitability update");
        // Authoring eligibility remains content-owned even though geometry still comes from a static rig profile.
        update = Next(service); update.Species[0].CombatEnabled = false;
        Require(service.TryApplyJson(JsonUtility.ToJson(update)).Success, "Combat eligibility revision failed");
        bool rejectedMatch = false;
        try { session.BeginMatch("female", "male", fixture.Settings, 6); } catch (InvalidOperationException) { rejectedMatch = true; }
        Require(rejectedMatch && session.ActiveMatchCount == 0, "Hot-disabled species still entered combat");
    }

    static void StageSpecificReload()
    {
        var oldSpecies = new SpeciesDefinition("stage-box", "Stage box", 50, 8);
        var habitats = new[]
        {
            new HabitatDefinition("larva-box", "Larva box", new HabitatConditions(), new[] { LifeStage.Larva }),
            new HabitatDefinition("pupa-box", "Pupa box", new HabitatConditions(), new[] { LifeStage.Pupa })
        };
        var session = new BeetleRosterSession(new ContentCatalog(1, new[] { oldSpecies }, habitats),
            new Dictionary<string, BeetleCombatProfile>());
        var larva = BeetleIndividual.Create(oldSpecies, "larva", "Larva", BeetleSex.Unknown, LifeStage.Larva);
        new HusbandryService().Advance(larva, oldSpecies, new HabitatConditions(), 8);
        session.Add(larva);
        session.SelectHabitat(larva.Id, "larva-box");
        var revised = new SpeciesDefinition(oldSpecies.Id, oldSpecies.DisplayName, 50, 8,
            lifecycle: new LifecycleProfile(3, 5, 10, 90));
        Require(session.TryUpdateContent(new ContentCatalog(2, new[] { revised }, habitats)).Success,
            "Stage-specific box prevented a lifecycle balance update");
        var boundary = session.AdvanceInSelectedHabitat(larva.Id, .25);
        var pupa = Read(session, larva.Id);
        Require(boundary.Success && boundary.Events.Count == 1 && pupa.Stage == LifeStage.Pupa &&
            pupa.TotalAgeDays == larva.TotalAgeDays && pupa.StageAgeDays == 0,
            "Overdue stage could not reach the relocation boundary without consuming care time");
        string beforeRejected = JsonUtility.ToJson(session.Export());
        Require(!session.AdvanceInSelectedHabitat(larva.Id, .25).Success &&
            JsonUtility.ToJson(session.Export()) == beforeRejected,
            "New pupa advanced in an incompatible box or rejected command mutated state");
        Require(session.SelectHabitat(larva.Id, "pupa-box").Success &&
            session.AdvanceInSelectedHabitat(larva.Id, .25).Success && Read(session, larva.Id).StageAgeDays == .25,
            "Shortened-stage individual is stuck between stage-specific boxes");
    }

    static BeetleContentData Next(BeetleContentService service)
    {
        var next = BeetleContentData.FromCatalog(service.Current);
        next.Version++;
        return next;
    }

    static void RejectUpdate(BeetleContentService service, BeetleRosterSession session, string payload, string message, bool allowActive = false)
    {
        var previous = service.Current;
        string records = allowActive ? null : JsonUtility.ToJson(session.Export());
        var result = service.TryApplyJson(payload);
        Require(!result.Success && result.Events.Count == 0 && ReferenceEquals(service.Current, previous), message);
        if (!allowActive) Require(JsonUtility.ToJson(session.Export()) == records, "Rejected content update partially changed roster");
    }

    static void FileReload(Fixture fixture)
    {
        var session = fixture.Pair();
        var service = new BeetleContentService(session);
        string directory = Path.GetFullPath("Artifacts/Framework");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "ContentValidationPackage.json");
        File.WriteAllText(path, JsonUtility.ToJson(Next(service), true));
        Require(service.TryReloadFile(path).Success && service.Current.Version == 2, "Explicit absolute content package did not reload");
        var previous = service.Current;
        Require(!service.TryReloadFile("relative-content.json").Success, "Relative reload path was accepted");
        Require(!service.TryReloadFile(Path.Combine(directory, "does-not-exist-" + Guid.NewGuid().ToString("N") + ".json")).Success,
            "Missing reload file was accepted");
        File.WriteAllText(path, "{broken");
        Require(!service.TryReloadFile(path).Success && ReferenceEquals(service.Current, previous), "Failed file reload replaced content");
        File.WriteAllText(path, service.ExportJson());
    }

    sealed class Fixture : IDisposable
    {
        readonly BeetleSpeciesAsset species = ScriptableObject.CreateInstance<BeetleSpeciesAsset>();
        readonly BeetleCombatProfile profile = ScriptableObject.CreateInstance<BeetleCombatProfile>();
        public readonly BeetleMatchSettings Settings = ScriptableObject.CreateInstance<BeetleMatchSettings>();
        public SpeciesDefinition Definition { get; }
        readonly ContentCatalog initial;
        public Fixture()
        {
            species.speciesId = "validation.content";
            Definition = species.ToDefinition();
            profile.species = species;
            profile.supportFootOffsets = new[]
            {
                new Vector3(-1, 0, 1), new Vector3(-1, 0, 0), new Vector3(-1, 0, -1),
                new Vector3(1, 0, 1), new Vector3(1, 0, 0), new Vector3(1, 0, -1)
            };
            Settings.countdownSeconds = 0;
            initial = new ContentCatalog(1, new[] { Definition }, new[]
            {
                new HabitatDefinition("calm", "Calm", new HabitatConditions(), new[] { LifeStage.Adult }),
                new HabitatDefinition("harsh", "Harsh", new HabitatConditions(35, 40, 50, .5f), new[] { LifeStage.Adult }),
                new HabitatDefinition("nursery", "Nursery", new HabitatConditions(), new[] { LifeStage.Egg, LifeStage.Larva, LifeStage.Pupa })
            });
        }
        public BeetleRosterSession Session() => new BeetleRosterSession(initial,
            new Dictionary<string, BeetleCombatProfile> { { Definition.Id, profile } });
        public BeetleRosterSession Pair()
        {
            var session = Session();
            session.Add(BeetleIndividual.Create(Definition, "female", "Female", BeetleSex.Female));
            session.Add(BeetleIndividual.Create(Definition, "male", "Male", BeetleSex.Male));
            return session;
        }
        public void Dispose()
        {
            UnityEngine.Object.DestroyImmediate(Settings);
            UnityEngine.Object.DestroyImmediate(profile);
            UnityEngine.Object.DestroyImmediate(species);
        }
    }
}
