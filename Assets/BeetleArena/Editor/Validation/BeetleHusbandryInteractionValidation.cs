using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BugDuel.Domain;
#if UNITY_EDITOR
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
#endif

/// <summary>Food, prolonged overturning and rescue contracts; pure checks also run without Unity.</summary>
public static class BeetleHusbandryInteractionValidation
{
    const string ReportPath = "Artifacts/Framework/HusbandryInteractionValidation.txt";

#if UNITY_EDITOR
    [MenuItem("Tools/Beetle Arena/Validate Husbandry Interaction")]
#endif
    public static void Run()
    {
        var report = new StringBuilder("HUSBANDRY INTERACTION CONTRACTS\n");
        var failures = new List<Exception>();
        Check("Sweet potato and fruit diets", FoodChoices, report, failures);
        Check("Overturning eligibility and repeated commands", OverturningRules, report, failures);
        Check("Grace boundary, damage, rescue and irreversible death", RescueAndMortality, report, failures);
        Check("Continuous and subdivided overturning time", PartitionInvariance, report, failures);
        Check("Saved posture, timer, clone isolation and invalid records", Persistence, report, failures);
#if UNITY_EDITOR
        Check("Contest eligibility, occupancy and content revisions", SessionContracts, report, failures);
        Check("Real nursery buttons, pause, presentation and protected saves", ApplicationFlow, report, failures);
#endif
        report.AppendLine(failures.Count == 0 ? "ALL HUSBANDRY INTERACTION CHECKS PASSED" : "FAILED GROUPS: " + failures.Count);
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, report.ToString());
        if (failures.Count != 0) throw new AggregateException("Husbandry interaction contracts failed; see " + ReportPath, failures);
    }

    static void Check(string name, Action validation, StringBuilder report, List<Exception> failures)
    {
        try { validation(); report.AppendLine("PASS: " + name); }
        catch (Exception error)
        {
            failures.Add(new Exception(name, error));
            report.AppendLine("FAIL: " + name + " — " + error.Message);
        }
    }

    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    static void Near(double actual, double expected, string message)
    {
        Require(!double.IsNaN(actual) && !double.IsInfinity(actual) && Math.Abs(actual - expected) < 1e-8,
            message + ": expected " + expected + ", actual " + actual);
    }

    static SpeciesDefinition Species(float recovery = 0)
        => new SpeciesDefinition("validation.husbandry-interaction", "Interaction beetle", 55, 8,
            care: new CareProfile(nutritionLossPerDay: 0, hydrationLossPerDay: 0, recoveryPerDay: recovery,
                stressChangePerDay: 0, healthLossPerDay: 0, overturnedGraceDays: .25, overturnedHealthLossPerDay: 100),
            lifecycle: new LifecycleProfile(.25, .5, .25, 200));

    static BeetleIndividual Individual(SpeciesDefinition species, string id = "female", BeetleSex sex = BeetleSex.Female,
        LifeStage stage = LifeStage.Adult) => BeetleIndividual.Create(species, id, id, sex, stage);

    static BeetleIndividual Restore(BeetleSaveData data, SpeciesDefinition species)
    {
        var restored = BeetleSaveMapper.Restore(data, new SpeciesCatalog(new[] { species }));
        Require(restored.Success && restored.Individual != null, "Fixture restore failed: " + restored.ErrorCode);
        return restored.Individual;
    }

    static void Same(BeetleIndividual actual, BeetleIndividual expected, string message)
    {
        Require(actual.Id == expected.Id && actual.Stage == expected.Stage && actual.IsOverturned == expected.IsOverturned
            && actual.IsStruggling == expected.IsStruggling, message + " (identity/stage/posture)");
        Near(actual.TotalAgeDays, expected.TotalAgeDays, message + " (total age)");
        Near(actual.StageAgeDays, expected.StageAgeDays, message + " (stage age)");
        Near(actual.OverturnedDays, expected.OverturnedDays, message + " (overturn timer)");
        Near(actual.BreedingCooldownDays, expected.BreedingCooldownDays, message + " (breeding cooldown)");
        Near(actual.Condition.Health, expected.Condition.Health, message + " (health)");
        Near(actual.Condition.Nutrition, expected.Condition.Nutrition, message + " (nutrition)");
        Near(actual.Condition.Hydration, expected.Condition.Hydration, message + " (hydration)");
        Near(actual.Condition.Stress, expected.Condition.Stress, message + " (stress)");
    }

    static FoodDefinition Food(FoodCatalog foods, string id)
    {
        Require(foods.TryGet(id, out var food), "Missing named food: " + id);
        return food;
    }

    static void FoodChoices()
    {
        var foods = FoodCatalog.CreateDefaults();
        var species = Species();
        var service = new HusbandryService();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var food in foods.Definitions)
            Require(ids.Add(food.Id) && !string.IsNullOrWhiteSpace(food.DisplayName) && food.Portion != null,
                "Food catalog contains duplicate/incomplete choices");
        Require(!foods.TryGet("missing-food", out _) && !foods.TryGet(null, out _), "Unknown food lookup succeeded");
        foreach (string id in new[] { "food.sweet-potato", "food.banana", "food.apple", "food.beetle-jelly" })
        {
            var data = BeetleSaveMapper.Export(Individual(species));
            data.Nutrition = 10; data.Hydration = 10;
            var adult = Restore(data, species);
            var food = Food(foods, id);
            Require(service.Feed(adult, species, food.Portion).Success && adult.Condition.Nutrition > 10,
                "Adult could not eat the named suitable food: " + id);
        }
        Require(Food(foods, "food.banana").Portion.Kind == FoodKind.Fruit
            && Food(foods, "food.apple").Portion.Kind == FoodKind.Fruit, "Fruit choices bypass the fruit diet category");
        foreach (LifeStage stage in new[] { LifeStage.Egg, LifeStage.Larva, LifeStage.Pupa })
        {
            var immature = Individual(species, stage.ToString(), stage: stage);
            var before = immature.Clone();
            var result = service.Feed(immature, species, Food(foods, "food.sweet-potato").Portion);
            Require(!result.Success && result.Events.Count == 0, "Default immature diet accepted sweet potato");
            Same(immature, before, "Rejected food changed immature state");
        }
    }

    static void OverturningRules()
    {
        var species = Species();
        var service = new HusbandryService();
        var beetle = Individual(species);
        Require(service.MarkOverturned(beetle, species).Success && beetle.IsOverturned && beetle.IsStruggling,
            "Overturned adult did not enter continuous struggling state");
        service.Advance(beetle, species, new HabitatConditions(), .125);
        var before = beetle.Clone();
        var repeated = service.MarkOverturned(beetle, species);
        Require(!repeated.Success && repeated.Events.Count == 0, "Repeated overturning reset the grace period");
        Same(beetle, before, "Repeated overturning changed timer or condition");
        var food = Food(FoodCatalog.CreateDefaults(), "food.banana");
        var fed = service.Feed(beetle, species, food.Portion);
        Require(!fed.Success && fed.Events.Count == 0, "Overturned adult continued eating");
        Same(beetle, before, "Rejected overturned feeding changed state");
        var father = Individual(species, "male", BeetleSex.Male);
        var breeding = new BreedingService().Breed(species, beetle, father,
            new[] { "child-a", "child-b", "child-c" }, new FixedRandom());
        Require(!breeding.Success && breeding.Offspring.Count == 0 && father.BreedingCooldownDays == 0,
            "Overturned parent was bred or consumed the other parent's cooldown");
        Same(beetle, before, "Rejected breeding changed overturned parent");
        foreach (LifeStage stage in new[] { LifeStage.Egg, LifeStage.Larva, LifeStage.Pupa })
        {
            var immature = Individual(species, stage.ToString(), stage: stage);
            var snapshot = immature.Clone();
            Require(!service.MarkOverturned(immature, species).Success && !service.HelpRight(immature, species).Success,
                "Non-adult accepted adult overturning/rescue controls");
            Same(immature, snapshot, "Rejected immature interaction changed state");
        }
    }

    static void RescueAndMortality()
    {
        var species = Species();
        var service = new HusbandryService();
        var habitat = new HabitatConditions();
        var beetle = Individual(species);
        service.MarkOverturned(beetle, species);
        service.Advance(beetle, species, habitat, .25);
        Near(beetle.Condition.Health, 100, "Grace boundary inflicted early damage");
        Near(beetle.OverturnedDays, .25, "Grace elapsed time");
        Require(beetle.IsStruggling, "Struggling stopped during grace period");
        service.Advance(beetle, species, habitat, .25);
        Near(beetle.Condition.Health, 75, "Post-grace sustained damage");
        Require(service.HelpRight(beetle, species).Success && !beetle.IsOverturned && !beetle.IsStruggling,
            "Rescue did not clear overturning and struggle state");
        Near(beetle.OverturnedDays, 0, "Rescue did not clear overturn timer");
        Near(beetle.Condition.Health, 75, "Rescue silently healed existing damage");
        service.Advance(beetle, species, habitat, 2);
        Near(beetle.Condition.Health, 75, "Rescued beetle continued losing health without another cause");
        var rescued = beetle.Clone();
        Require(!service.HelpRight(beetle, species).Success, "Repeated rescue accepted an upright adult");
        Same(beetle, rescued, "Repeated rescue changed upright state");

        var recoverySpecies = Species(recovery: 100);
        var damaged = BeetleSaveMapper.Export(Individual(recoverySpecies)); damaged.Health = 50;
        var recovering = Restore(damaged, recoverySpecies);
        service.MarkOverturned(recovering, recoverySpecies);
        service.Advance(recovering, recoverySpecies, habitat, .25);
        Near(recovering.Condition.Health, 50, "Normal recovery healed an overturned beetle during grace");
        service.Advance(recovering, recoverySpecies, habitat, .25);
        Near(recovering.Condition.Health, 25, "Normal recovery cancelled overturning damage");

        var doomed = Individual(species, "doomed");
        service.MarkOverturned(doomed, species);
        var death = service.Advance(doomed, species, habitat, 100);
        Require(death.Success && doomed.Stage == LifeStage.Dead && doomed.Condition.Health == 0 && doomed.IsOverturned
            && !doomed.IsStruggling, "Unattended overturning did not produce irreversible death and a stopped pose");
        Near(doomed.OverturnedDays, 1.25, "Death did not freeze at grace plus lethal exposure");
        int died = 0;
        foreach (var item in death.Events) if (item.Kind == HusbandryEventKind.Died) died++;
        Require(died == 1, "Overturning death was not emitted exactly once");
        var dead = doomed.Clone();
        Require(!service.HelpRight(doomed, species).Success && !service.MarkOverturned(doomed, species).Success,
            "Dead beetle accepted rescue/overturn commands");
        Require(!service.Feed(doomed, species, Food(FoodCatalog.CreateDefaults(), "food.apple").Portion).Success,
            "Feeding resurrected dead beetle");
        service.Advance(doomed, species, habitat, 100);
        Same(doomed, dead, "Dead beetle continued aging, struggling or accepting treatment");
    }

    static void PartitionInvariance()
    {
        var species = Species(); var service = new HusbandryService(); var habitat = new HabitatConditions();
        foreach (double duration in new[] { .03, .25, .37, .9, 1.25, 20 })
        {
            var whole = Individual(species); service.MarkOverturned(whole, species);
            var parts = whole.Clone();
            service.Advance(whole, species, habitat, duration);
            for (int i = 0; i < 97; i++) service.Advance(parts, species, habitat, duration / 97);
            Same(whole, parts, "Overturning damage/death depends on caller tick size at duration " + duration);
        }
        var left = Individual(species); var right = left.Clone();
        service.MarkOverturned(left, species); service.MarkOverturned(right, species);
        service.Advance(left, species, habitat, .5);
        for (int i = 0; i < 8; i++) service.Advance(right, species, habitat, .0625);
        service.HelpRight(left, species); service.HelpRight(right, species);
        service.Advance(left, species, habitat, 2);
        for (int i = 0; i < 32; i++) service.Advance(right, species, habitat, .0625);
        Same(left, right, "Rescue sequence depends on caller tick size");
    }

    static void Persistence()
    {
        var species = Species(); var service = new HusbandryService(); var habitat = new HabitatConditions();
        var original = Individual(species);
        service.MarkOverturned(original, species); service.Advance(original, species, habitat, .375);
        var data = BeetleSaveMapper.Export(original);
        var restored = Restore(data, species);
        Same(restored, original, "Save round trip lost struggling posture/timer");
        data.IsOverturned = false; data.OverturnedDays = 0;
        Same(restored, original, "Restored posture retained mutable DTO storage");
        var clone = original.Clone(); service.HelpRight(clone, species);
        Require(original.IsOverturned && original.OverturnedDays == .375, "Rescuing clone changed original posture");
        service.Advance(original, species, habitat, .25); service.Advance(restored, species, habitat, .25);
        Same(restored, original, "Reload restarted grace or changed remaining fatal exposure");
        var catalog = new SpeciesCatalog(new[] { species });
        Action<Action<BeetleSaveData>> reject = mutate =>
        {
            var invalid = BeetleSaveMapper.Export(original); mutate(invalid);
            var result = BeetleSaveMapper.Restore(invalid, catalog);
            Require(!result.Success && result.Individual == null, "Invalid saved overturn posture/timer was accepted");
        };
        reject(d => d.OverturnedDays = -1);
        reject(d => d.OverturnedDays = double.NaN);
        reject(d => d.OverturnedDays = double.PositiveInfinity);
        reject(d => d.IsOverturned = false);
        reject(d => d.OverturnedDays = d.StageAgeDays + 1);
        var legacy = BeetleSaveMapper.Export(Individual(species));
        legacy.IsOverturned = false; legacy.OverturnedDays = 0;
        Require(!Restore(legacy, species).IsOverturned, "Default old upright save was reinterpreted as overturned");
        service.Advance(original, species, habitat, 100);
        Same(Restore(BeetleSaveMapper.Export(original), species), original, "Dead overturned pose cannot round trip");
    }

#if UNITY_EDITOR
    static void SessionContracts()
    {
        var authored = ScriptableObject.CreateInstance<BeetleSpeciesAsset>();
        var profile = ScriptableObject.CreateInstance<BeetleCombatProfile>();
        var settings = ScriptableObject.CreateInstance<BeetleMatchSettings>();
        try
        {
            var species = Species();
            authored.speciesId = species.Id;
            profile.species = authored;
            profile.supportFootOffsets = new[]
            {
                new Vector3(-1, 0, 1), new Vector3(-1, 0, 0), new Vector3(-1, 0, -1),
                new Vector3(1, 0, 1), new Vector3(1, 0, 0), new Vector3(1, 0, -1)
            };
            settings.countdownSeconds = 0;
            var content = new ContentCatalog(1, new[] { species }, HabitatDefinition.CreateDefaults());
            var profiles = new Dictionary<string, BeetleCombatProfile> { { species.Id, profile } };
            var session = new BeetleRosterSession(content, profiles);
            session.Add(Individual(species)); session.Add(Individual(species, "male", BeetleSex.Male));
            Require(session.MarkOverturned("female").Success, "Session did not expose overturning command");
            var flipped = Read(session, "female");
            bool rejected = false;
            try { session.BeginMatch("female", "male", settings, 91); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected && session.ActiveMatchCount == 0 && !session.IsOccupied("male"), "Overturned entrant reserved a contest");
            Require(!session.Feed("female", "food.banana").Success && !session.Breed("female", "male",
                new[] { "a", "b", "c" }, new FixedRandom()).Success, "Session bypassed overturned feeding/breeding rejection");
            Same(Read(session, "female"), flipped, "Rejected interaction changed occupied posture");
            Require(session.Advance("female", new HabitatConditions(), .375).Success, "Session did not advance overturn timer");
            flipped = Read(session, "female");
            var exported = session.Export();
            var restored = new BeetleRosterSession(content, profiles); restored.Restore(exported);
            Same(Read(restored, "female"), flipped, "Roster container lost overturning state");

            var service = new BeetleContentService(restored);
            var update = BeetleContentData.FromCatalog(service.Current); update.Version++;
            update.Species[0].OverturnedGraceDays = .5;
            update.Species[0].OverturnedHealthLossPerDay = 50;
            Require(service.TryApplyJson(JsonUtility.ToJson(update)).Success, "Valid rescue tuning revision was rejected");
            Same(Read(restored, "female"), flipped, "Content update reset existing overturning exposure");
            restored.Advance("female", new HabitatConditions(), .125);
            Near(Read(restored, "female").Condition.Health, flipped.Condition.Health, "Updated grace was not used at exact boundary");
            restored.Advance("female", new HabitatConditions(), .25);
            Near(Read(restored, "female").Condition.Health, flipped.Condition.Health - 12.5, "Updated overturn damage rate was ignored");

            var legacyUpdate = BeetleContentData.FromCatalog(service.Current); legacyUpdate.Version++; legacyUpdate.SchemaVersion = 1;
            legacyUpdate.Species[0].OverturnedGraceDays = 0;
            legacyUpdate.Species[0].OverturnedHealthLossPerDay = 0;
            legacyUpdate.Species[0].AdultFoods = 17;
            Require(service.TryApplyJson(JsonUtility.ToJson(legacyUpdate)).Success, "Supported schema-1 package was not migrated");
            Require(service.Current.Species.TryGet(species.Id, out var migrated), "Migrated species missing");
            Near(migrated.Care.OverturnedGraceDays, .05, "Legacy content inherited a zero grace field");
            Near(migrated.Care.OverturnedHealthLossPerDay, 500, "Legacy content inherited a zero fatal-damage field");
            var upright = Individual(migrated, "food-migration");
            Require(new HusbandryService().Feed(upright, migrated, Food(restored.Foods, "food.sweet-potato").Portion).Success,
                "Legacy default adult diet did not migrate to include sweet potato");

            Require(session.HelpRight("female").Success, "Session rescue failed");
            var contest = session.BeginMatch("female", "male", settings, 92);
            var beforeFirst = Read(session, "female"); var beforeSecond = Read(session, "male");
            Require(!session.MarkOverturned("female").Success && !session.HelpRight("male").Success
                && !session.Feed("female", "food.sweet-potato").Success,
                "Occupied individuals accepted husbandry interaction");
            Same(Read(session, "female"), beforeFirst, "Occupied interaction changed first participant");
            Same(Read(session, "male"), beforeSecond, "Occupied interaction changed second participant");
            Require(session.Cancel(contest.MatchId) && session.MarkOverturned("female").Success,
                "Cancelled contest did not release husbandry interaction");
            Require(!session.Feed("male", "missing-food").Success, "Unknown food identity was accepted by session");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(settings);
            UnityEngine.Object.DestroyImmediate(profile);
            UnityEngine.Object.DestroyImmediate(authored);
        }
    }

    static BeetleIndividual Read(BeetleRosterSession session, string id)
    {
        Require(session.TryGet(id, out var beetle), "Missing interaction fixture individual: " + id);
        return beetle;
    }

    static void ApplicationFlow()
    {
        string directory = Path.GetFullPath(Path.Combine("Artifacts/Framework", "HusbandryUi-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        string savePath = Path.Combine(directory, "roster.json");
        string futurePath = Path.Combine(directory, "future.json");
        string contentPath = Path.Combine(directory, "beetle-content.json");
        var previous = SceneManager.GetActiveScene();
        Scene scene = default;
        BeetleValidationScene fixture = null;
        try
        {
            // 复制已保存场景后清空自有副本，避免要求调用方保存未命名场景。
            fixture = new BeetleValidationScene("NurseryInteraction");
            scene = fixture.Scene;
            foreach (var root in scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(root);
            var controller = BeetleHusbandrySceneBuilder.BuildContents(scene);
            Require(SceneManager.GetActiveScene() == scene || SceneManager.SetActiveScene(scene), "Could not activate owned nursery validation scene");
            controller.simulateAccidents = false;
            controller.Initialize(savePath, false);
            var hud = ComponentIn<BeetleHusbandryHud>(scene);
            var view = ComponentIn<BeetleHusbandryView>(scene);
            Require(hud.controller == controller && view.controller == controller, "Generated UI/view is bound to a different controller");
            Require(ComponentCount<UnityEngine.EventSystems.EventSystem>(scene) == 1
                && hud.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null, "Nursery lacks a usable UI event path");
            hud.Bind(); hud.Bind(); // Repeated setup must not duplicate command listeners.
            Require(controller.SelectedId == "nursery.amber" && controller.Selected.IsOverturned,
                "First session does not expose the intended rescue interaction");
            var sweetPotato = Choice(hud.foodList, "红薯");
            Require(!sweetPotato.interactable && hud.helpButton.interactable, "Overturned UI permits eating or hides rescue");
            view.Present(.03f);
            var firstLegPose = LegPose(view);
            view.Present(.13f);
            Require(PoseChanged(view, firstLegPose), "Living overturned beetle has no continued leg struggle");
            CaptureNursery(scene, "Artifacts/Framework/Husbandry-Struggling.png");

            hud.pauseButton.onClick.Invoke();
            Require(controller.IsPaused, "Pause button did not reach the application");
            var paused = controller.Selected;
            var pausedPose = LegPose(view);
            controller.AdvanceSeconds(60); view.Present(1);
            Same(controller.Selected, paused, "Paused UI advanced husbandry time");
            Require(!PoseChanged(view, pausedPose), "Paused UI continued procedural leg motion");
            hud.pauseButton.onClick.Invoke();
            Require(!controller.IsPaused, "Resume button did not release pause");

            double nutrition = controller.Selected.Condition.Nutrition;
            hud.helpButton.onClick.Invoke();
            Require(!controller.Selected.IsOverturned && !hud.helpButton.interactable && sweetPotato.interactable,
                "Rescue button did not clear state and refresh available food");
            Near(controller.Selected.OverturnedDays, 0, "Rescue button retained the exposure counter");
            sweetPotato.onClick.Invoke();
            Near(controller.Selected.Condition.Nutrition, Math.Min(100, nutrition + 30),
                "Sweet-potato button failed to feed once, or duplicate Bind duplicated its action");
            Require(File.Exists(savePath), "Successful care action was not saved to the explicit test path");
            // Let the rescue roll settle while the eating response remains visible.
            for (int i = 0; i < 12; i++) view.Present(.1f);
            CaptureNursery(scene, "Artifacts/Framework/Husbandry-RescuedAndFed.png");

            Choice(hud.individualList, "墨甲").onClick.Invoke();
            Require(controller.SelectedId == "nursery.ink", "Roster button did not change the selected individual");
            nutrition = controller.Selected.Condition.Nutrition;
            var banana = Choice(hud.foodList, "香蕉");
            Require(banana.interactable, "Healthy adult cannot choose fruit in the UI");
            banana.onClick.Invoke();
            Near(controller.Selected.Condition.Nutrition, Math.Min(100, nutrition + 25), "Fruit button did not feed the selected individual");
            foreach (var habitat in controller.Session.AvailableHabitats(controller.SelectedId))
            {
                if (habitat.Id == controller.Session.GetSelectedHabitatId(controller.SelectedId)) continue;
                Choice(hud.habitatList, habitat.DisplayName).onClick.Invoke();
                Require(controller.Session.GetSelectedHabitatId(controller.SelectedId) == habitat.Id,
                    "Habitat button did not change the selected individual's environment");
                break;
            }

            var tuning = BeetleContentData.FromCatalog(controller.Session.Content);
            tuning.Version++;
            tuning.Species[0].DisplayName += " · 更新";
            File.WriteAllText(contentPath, JsonUtility.ToJson(tuning));
            var beforeReload = controller.Selected;
            hud.reloadButton.onClick.Invoke();
            Require(controller.Session.Content.Version == tuning.Version, "Reload button did not publish a valid explicit content file");
            Same(controller.Selected, beforeReload, "UI content reload reset the selected individual");
            Require(hud.selectedName.text.Contains("更新"), "HUD did not refresh changed species content");

            Choice(hud.individualList, "琥珀").onClick.Invoke();
            Require(controller.Session.MarkOverturned(controller.SelectedId).Success, "Could not set up a second rescue opportunity");
            controller.AdvanceSeconds(40);
            Require(controller.Selected.IsOverturned && controller.Selected.Condition.Health > 0 && controller.Selected.Condition.Health < 100,
                "Scene clock did not apply post-grace overturning damage");
            view.Present(.1f);
            var saved = controller.Selected;
            Require(controller.Save(), "Could not save exposure before reload");
            var loadedObject = new GameObject("HusbandryReloadValidation");
            var loaded = loadedObject.AddComponent<BeetleHusbandryController>();
            loaded.simulateAccidents = false;
            loaded.Initialize(savePath, true);
            Same(Read(loaded.Session, saved.Id), saved, "Controller reload lost exposure or advanced offline time");
            UnityEngine.Object.DestroyImmediate(loadedObject);

            controller.AdvanceSeconds(600);
            Require(controller.Selected.Stage == LifeStage.Dead && !hud.helpButton.interactable,
                "Unattended scene time failed to reach irreversible death");
            foreach (var button in hud.foodList.GetComponentsInChildren<UnityEngine.UI.Button>())
                Require(!button.interactable, "Dead individual retains an enabled feeding button");
            view.Present(.1f);
            var deadPose = LegPose(view);
            view.Present(.5f); view.Present(2);
            Require(!PoseChanged(view, deadPose), "Dead individual continued leg struggling");
            Require(!controller.HelpRight().Success && !controller.Feed("food.apple").Success
                && controller.Selected.Stage == LifeStage.Dead, "Application interaction revived a dead individual");

            const string futureSave = "{\"Version\":999,\"Individuals\":[],\"AppliedMatchIds\":[],\"HabitatSelections\":[]}";
            File.WriteAllText(futurePath, futureSave);
            var protectedObject = new GameObject("HusbandryFutureSaveValidation");
            var protectedController = protectedObject.AddComponent<BeetleHusbandryController>();
            protectedController.Initialize(futurePath, true);
            Require(!protectedController.CanSave && !protectedController.Save(), "Unknown-version save was left writable after startup rejection");
            UnityEngine.Object.DestroyImmediate(protectedObject);
            Require(File.ReadAllText(futurePath) == futureSave, "Rejected future save was overwritten by initialization or destruction");
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            fixture?.Dispose();
            foreach (string file in new[] { savePath, futurePath, contentPath }) if (File.Exists(file)) File.Delete(file);
            Directory.Delete(directory); // Empty, uniquely owned directory; never recurse into user saves.
        }
    }

    static T ComponentIn<T>(Scene scene) where T : Component
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var component = root.GetComponentInChildren<T>(true);
            if (component) return component;
        }
        throw new Exception("Generated nursery has no " + typeof(T).Name);
    }

    static void CaptureNursery(Scene scene, string path)
    {
        var camera = ComponentIn<Camera>(scene);
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        var target = RenderTexture.GetTemporary(1600, 900, 24, RenderTextureFormat.ARGB32);
        Texture2D pixels = null;
        try
        {
            camera.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            pixels.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            if (pixels) UnityEngine.Object.DestroyImmediate(pixels);
            RenderTexture.ReleaseTemporary(target);
        }
    }

    static int ComponentCount<T>(Scene scene) where T : Component
    {
        int count = 0;
        foreach (var root in scene.GetRootGameObjects()) count += root.GetComponentsInChildren<T>(true).Length;
        return count;
    }

    static UnityEngine.UI.Button Choice(Transform parent, string prefix)
    {
        foreach (var button in parent.GetComponentsInChildren<UnityEngine.UI.Button>())
        {
            var caption = button.GetComponentInChildren<TMPro.TMP_Text>();
            if (caption && caption.text.StartsWith(prefix, StringComparison.Ordinal)) return button;
        }
        throw new Exception("Generated nursery has no visible choice: " + prefix);
    }

    static Quaternion[] LegPose(BeetleHusbandryView view)
    {
        var pose = new Quaternion[view.sourceRig.legs.Length * 3];
        Require(pose.Length >= 18, "Nursery view has no complete six-leg rig");
        for (int i = 0; i < view.sourceRig.legs.Length; i++)
        {
            var leg = view.sourceRig.legs[i];
            pose[i * 3] = leg.hip.localRotation;
            pose[i * 3 + 1] = leg.knee.localRotation;
            pose[i * 3 + 2] = leg.ankle.localRotation;
        }
        return pose;
    }

    static bool PoseChanged(BeetleHusbandryView view, Quaternion[] previous)
    {
        var current = LegPose(view);
        for (int i = 0; i < current.Length; i++)
            if (Quaternion.Angle(current[i], previous[i]) > .05f) return true;
        return false;
    }
#endif

    sealed class FixedRandom : IRandomSource { public double NextUnit() => .5; }
}
