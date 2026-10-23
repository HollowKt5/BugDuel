using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BugDuel.Domain;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>Pure domain contracts: executable without Unity, assets, scenes, clocks or external packages.</summary>
public static class BeetleDomainValidation
{
    const string ReportPath = "Artifacts/Framework/DomainValidation.txt";

#if UNITY_EDITOR
    [MenuItem("Tools/Beetle Arena/Validate Husbandry Domain")]
#endif
    public static void Run()
    {
        var report = new StringBuilder("HUSBANDRY DOMAIN CONTRACTS\n");
        var failures = new List<Exception>();
        Check("Definition and input guards", Definitions, report, failures);
        Check("Display text cleanup preserves stable identities", DisplayNames, report, failures);
        Check("Overturn timer normalization and invalid records", OverturnTimers, report, failures);
        Check("Immutable content catalogs and habitat choices", ContentDefinitions, report, failures);
        Check("Lifecycle boundaries and partition invariance", Lifecycle, report, failures);
        Check("Balance-patch save and lifecycle compatibility", LifecycleCompatibility, report, failures);
        Check("Care thresholds and mortality", CareThresholds, report, failures);
        Check("Food suitability and transactional rejection", Feeding, report, failures);
        Check("Breeding, identity, ancestry and cooldown", Breeding, report, failures);
        Check("Save version, invalid records and clone isolation", Persistence, report, failures);
        Check("Explicit contest consequences", ContestConsequences, report, failures);
        report.AppendLine(failures.Count == 0 ? "ALL DOMAIN CHECKS PASSED" : "FAILED GROUPS: " + failures.Count);
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, report.ToString());
        if (failures.Count != 0) throw new AggregateException("Domain contracts failed; see " + ReportPath, failures);
    }

    static void Check(string name, Action action, StringBuilder report, List<Exception> failures)
    {
        try { action(); report.AppendLine("PASS: " + name); }
        catch (Exception error)
        {
            failures.Add(new Exception(name, error));
            report.AppendLine("FAIL: " + name + " — " + error.Message);
        }
    }

    static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    static void Near(double actual, double expected, string message, double tolerance = 1e-8)
    {
        Require(!double.IsNaN(actual) && !double.IsInfinity(actual) && Math.Abs(actual - expected) <= tolerance,
            message + ": expected " + expected + ", actual " + actual);
    }

    static void Rejects(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new Exception(message);
    }

    static SpeciesDefinition Species(string id = "validation.horn", double adultDays = 200)
        => new SpeciesDefinition(id, "Validation beetle", 55, 8,
            care: new CareProfile(nutritionLossPerDay: 0, hydrationLossPerDay: 0,
                recoveryPerDay: 0, stressChangePerDay: 0, healthLossPerDay: 0),
            lifecycle: new LifecycleProfile(.25, .5, .25, adultDays),
            breeding: new BreedingProfile(cooldownDays: .5));

    static BeetleIndividual Individual(SpeciesDefinition species, string id = "adult", BeetleSex sex = BeetleSex.Female,
        LifeStage stage = LifeStage.Adult) => BeetleIndividual.Create(species, id, id, sex, stage);

    static BeetleIndividual Restore(BeetleSaveData data, SpeciesDefinition species)
    {
        var result = BeetleSaveMapper.Restore(data, new SpeciesCatalog(new[] { species }));
        Require(result.Success && result.Individual != null, "Valid fixture failed restore: " + result.ErrorCode);
        return result.Individual;
    }

    static void Same(BeetleIndividual actual, BeetleIndividual expected, string message)
    {
        Require(actual.Id == expected.Id && actual.SpeciesId == expected.SpeciesId && actual.DisplayName == expected.DisplayName
            && actual.Sex == expected.Sex && actual.Stage == expected.Stage, message + " (identity/stage)");
        Near(actual.TotalAgeDays, expected.TotalAgeDays, message + " (total age)");
        Near(actual.StageAgeDays, expected.StageAgeDays, message + " (stage age)");
        Near(actual.BreedingCooldownDays, expected.BreedingCooldownDays, message + " (cooldown)");
        Near(actual.Condition.Health, expected.Condition.Health, message + " (health)");
        Near(actual.Condition.Nutrition, expected.Condition.Nutrition, message + " (nutrition)");
        Near(actual.Condition.Hydration, expected.Condition.Hydration, message + " (hydration)");
        Near(actual.Condition.Stress, expected.Condition.Stress, message + " (stress)");
        Near(actual.Traits.Strength, expected.Traits.Strength, message + " (strength)");
        Near(actual.Traits.Defense, expected.Traits.Defense, message + " (defense)");
        Near(actual.Traits.Speed, expected.Traits.Speed, message + " (speed)");
        Near(actual.Traits.Stamina, expected.Traits.Stamina, message + " (stamina)");
        Require(actual.Pedigree.MotherId == expected.Pedigree.MotherId && actual.Pedigree.FatherId == expected.Pedigree.FatherId
            && actual.Pedigree.AncestorIds.Count == expected.Pedigree.AncestorIds.Count, message + " (pedigree)");
        for (int i = 0; i < actual.Pedigree.AncestorIds.Count; i++)
            Require(actual.Pedigree.AncestorIds[i] == expected.Pedigree.AncestorIds[i], message + " (ancestor)");
    }

    static void Definitions()
    {
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Rejects(() => new BeetleTraits(invalid), "Non-finite trait accepted");
            Rejects(() => new BeetleCondition(health: invalid), "Non-finite condition accepted");
            Rejects(() => new SpeciesDefinition("s", "Species", invalid, 8), "Non-finite morphology accepted");
            Rejects(() => new CareProfile(nutritionLossPerDay: invalid), "Non-finite care rate accepted");
            Rejects(() => new HabitatConditions(temperatureCelsius: invalid), "Non-finite habitat accepted");
            Rejects(() => new FoodPortion(FoodKind.Fruit, invalid, 1), "Non-finite food accepted");
            Rejects(() => new LifecycleProfile(eggDays: invalid), "Non-finite lifecycle accepted");
        }
        Rejects(() => new BeetleTraits(-.01f), "Negative trait accepted");
        Rejects(() => new BeetleTraits(stamina: 1.01f), "Trait above normalized range accepted");
        var saturated = new BeetleCondition(120, -10, 140, -2);
        Require(saturated.Health == 100 && saturated.Nutrition == 0 && saturated.Hydration == 100 && saturated.Stress == 0,
            "Finite condition deltas did not saturate at the value-object boundary");
        Rejects(() => new LifecycleProfile(eggDays: 0), "Zero-duration stage accepted");
        Rejects(() => new CareProfile(minimumTemperature: 30, maximumTemperature: 20), "Reversed habitat interval accepted");
        Rejects(() => new SpeciesDefinition(" s ", "Species", 55, 8), "Unstable whitespace identity accepted");
        Rejects(() => new FoodPortion((FoodKind)128, 1, 1), "Unknown food flag accepted");
        var species = Species();
        Rejects(() => Individual(species, sex: (BeetleSex)99), "Unknown sex accepted");
        Rejects(() => Individual(species, stage: (LifeStage)99), "Unknown lifecycle stage accepted");
        Rejects(() => new SpeciesCatalog(new[] { species, species }), "Duplicate catalog identity accepted");
        var source = new[] { species };
        var catalog = new SpeciesCatalog(source);
        source[0] = Species("other");
        Require(catalog.TryGet(species.Id, out var found) && found == species && !catalog.TryGet("other", out _),
            "Catalog retained mutable input collection");
    }

    static void DisplayNames()
    {
        var species = new SpeciesDefinition("names", " \t大兜虫\u3000", 55, 8);
        var individual = BeetleIndividual.Create(species, "beetle-1", "\u3000 小 栗子 \r\n", BeetleSex.Female);
        var food = new FoodDefinition("jelly", "  果冻\t", new FoodPortion(FoodKind.BeetleJelly, 1, 1));
        var habitat = new HabitatDefinition("box", "\n 饲养箱  ", new HabitatConditions());
        Require(species.DisplayName == "大兜虫" && individual.DisplayName == "小 栗子" &&
            food.DisplayName == "果冻" && habitat.DisplayName == "饲养箱", "Display whitespace was not normalized at construction");
        Require(individual.Id == "beetle-1" && individual.SpeciesId == "names", "Display cleanup changed identity");
        foreach (string blank in new[] { null, "", " \t\u3000" })
            Rejects(() => BeetleIndividual.Create(species, "beetle-1", blank, BeetleSex.Female), "Blank name accepted");
        Rejects(() => Individual(species, " beetle-1 "), "Individual identity was silently normalized");
        var data = BeetleSaveMapper.Export(individual);
        data.DisplayName = "  存档昵称\t";
        var restored = Restore(data, species);
        Require(restored.DisplayName == "存档昵称" && restored.Clone().DisplayName == "存档昵称" &&
            BeetleSaveMapper.Export(restored).DisplayName == "存档昵称", "Cleaned name did not survive clone/export");
        Require(data.DisplayName == "  存档昵称\t", "Restore mutated its source record");
        data.MotherId = " mother "; data.FatherId = "father";
        data.AncestorIds = new[] { "mother", "father" };
        Require(!BeetleSaveMapper.Restore(data, new SpeciesCatalog(new[] { species })).Success,
            "Parent identity was silently normalized");
        data.MotherId = "mother";
        data.AncestorIds = new[] { "mother", "father", " ancestor " };
        Require(!BeetleSaveMapper.Restore(data, new SpeciesCatalog(new[] { species })).Success,
            "Ancestor identity was silently normalized");
    }

    static void OverturnTimers()
    {
        var species = Species();
        var catalog = new SpeciesCatalog(new[] { species });
        var data = BeetleSaveMapper.Export(Individual(species));
        data.TotalAgeDays += .5; data.StageAgeDays = .5; data.IsOverturned = true;
        foreach (double timer in new[] { 0, .25, .5, .5 + 1e-12, 2d })
        {
            data.OverturnedDays = timer;
            var restored = Restore(data, species);
            Near(restored.OverturnedDays, Math.Min(.5, timer), "Overturn timer normalization");
            Same(restored.Clone(), restored, "Normalized overturn clone");
            Near(BeetleSaveMapper.Export(restored).OverturnedDays, Math.Min(.5, timer), "Normalized timer export");
            Require(data.OverturnedDays == timer, "Timer cleanup mutated source record");
        }
        foreach (double timer in new[] { -.01, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            data.OverturnedDays = timer;
            Require(!BeetleSaveMapper.Restore(data, catalog).Success, "Invalid elapsed timer accepted");
        }
        data.OverturnedDays = .25; data.IsOverturned = false;
        Require(!BeetleSaveMapper.Restore(data, catalog).Success, "Upright beetle retained an overturn timer");
        data.IsOverturned = true; data.Stage = (int)LifeStage.Larva;
        Require(!BeetleSaveMapper.Restore(data, catalog).Success, "Larva retained an adult overturn pose");
    }

    static void Lifecycle()
    {
        var service = new HusbandryService();
        var habitat = new HabitatConditions();
        var species = Species(adultDays: 2);
        var whole = Individual(species, stage: LifeStage.Egg);
        var parts = whole.Clone();
        var result = service.Advance(whole, species, habitat, 1.25);
        Require(result.Success && whole.Stage == LifeStage.Adult, "Offline advancement skipped a lifecycle transition");
        Near(whole.TotalAgeDays, 1.25, "Cross-stage total age");
        Near(whole.StageAgeDays, .25, "Cross-stage remainder");
        var changed = new List<LifeStage>();
        foreach (var item in result.Events)
            if (item.Kind == HusbandryEventKind.StageChanged) changed.Add(item.Stage);
        Require(changed.Count == 3 && changed[0] == LifeStage.Larva && changed[1] == LifeStage.Pupa
            && changed[2] == LifeStage.Adult, "Stage events are incomplete or unordered");
        for (int i = 0; i < 10; i++) Require(service.Advance(parts, species, habitat, .125).Success, "Split advancement failed");
        Same(whole, parts, "Cross-stage advancement depends on caller tick size");
        var snapshot = whole.Clone();
        result = service.Advance(whole, species, habitat, 0);
        Require(result.Success && result.Events.Count == 0, "Zero elapsed time produced side effects");
        Same(whole, snapshot, "Zero elapsed time changed individual");
        result = service.Advance(whole, species, habitat, 1000);
        Require(result.Success && whole.Stage == LifeStage.Dead && whole.Condition.Health == 0, "Natural death not applied");
        Near(whole.TotalAgeDays, 3, "Dead individual continued aging through offline remainder");
        int deaths = 0;
        foreach (var item in result.Events) if (item.Kind == HusbandryEventKind.Died) deaths++;
        Require(deaths == 1, "Natural death produced duplicate/missing death events");
        snapshot = whole.Clone();
        result = service.Advance(whole, species, habitat, 1000);
        Require(result.Events.Count == 0, "Advancing dead individual repeated events");
        Same(whole, snapshot, "Dead individual was mutated");
    }

    static void ContentDefinitions()
    {
        var species = Species();
        var stages = new[] { LifeStage.Adult };
        var tags = new[] { "rest" };
        var habitat = new HabitatDefinition("habitat.test", "Test enclosure", new HabitatConditions(), stages, tags);
        stages[0] = LifeStage.Egg; tags[0] = "outside";
        Require(habitat.Allows(LifeStage.Adult) && !habitat.Allows(LifeStage.Egg) && habitat.Tags[0] == "rest",
            "Habitat retained mutable stage/tag collections");
        var habitats = new[] { habitat };
        var catalog = new ContentCatalog(1, new[] { species }, habitats);
        habitats[0] = null;
        Require(catalog.TryGetHabitat(habitat.Id, out var found) && found == habitat && catalog.Habitats.Count == 1,
            "Content catalog retained caller-owned habitat collection");
        Require(!catalog.TryGetHabitat("unknown", out _) && !catalog.TryGetHabitat(null, out _), "Unknown habitat lookup succeeded");
        Rejects(() => new ContentCatalog(0, new[] { species }, new[] { habitat }), "Nonpositive content revision accepted");
        Rejects(() => new ContentCatalog(1, Array.Empty<SpeciesDefinition>(), new[] { habitat }), "Empty species catalog accepted");
        Rejects(() => new ContentCatalog(1, new[] { species }, Array.Empty<HabitatDefinition>()), "Empty habitat catalog accepted");
        Rejects(() => new ContentCatalog(1, new[] { species }, new[] { habitat, habitat }), "Duplicate habitat identity accepted");
        Rejects(() => new HabitatDefinition("dead", "Dead", new HabitatConditions(), new[] { LifeStage.Dead }), "Dead-only habitat accepted");
        Rejects(() => new HabitatDefinition("empty", "Empty", new HabitatConditions(), Array.Empty<LifeStage>()), "Habitat with no allowed stages accepted");
        var defaults = HabitatDefinition.CreateDefaults();
        Require(defaults.Count >= 3, "Expected distinct adult, development and recovery habitat templates");
        bool adultOnly = false, developmentOnly = false;
        foreach (var option in defaults)
        {
            adultOnly |= option.Allows(LifeStage.Adult) && !option.Allows(LifeStage.Larva);
            developmentOnly |= option.Allows(LifeStage.Larva) && !option.Allows(LifeStage.Adult);
        }
        Require(adultOnly && developmentOnly, "Default environments do not distinguish adult and developmental stages");
    }

    static void CareThresholds()
    {
        var species = new SpeciesDefinition("care", "Care fixture", 55, 8,
            lifecycle: new LifecycleProfile(.25, .5, .25, 500));
        var data = BeetleSaveMapper.Export(Individual(species));
        data.Health = 83; data.Nutrition = 33; data.Hydration = 31; data.Stress = 15;
        var service = new HusbandryService();
        foreach (var habitat in new[] { new HabitatConditions(), new HabitatConditions(32, 90, 20, 1, false) })
        {
            var whole = Restore(data, species);
            var parts = whole.Clone();
            Require(service.Advance(whole, species, habitat, 20).Success, "Long care advancement failed");
            for (int i = 0; i < 160; i++) service.Advance(parts, species, habitat, .125);
            Same(whole, parts, "Care/health threshold crossing depends on caller tick size");
            var replay = Restore(data, species);
            service.Advance(replay, species, habitat, 20);
            Same(whole, replay, "Care advancement is not deterministic");
        }
        var original = Restore(data, species);
        foreach (double invalid in new[] { -1, double.NaN, double.PositiveInfinity })
        {
            var result = service.Advance(original, species, new HabitatConditions(), invalid);
            Require(!result.Success && result.Events.Count == 0, "Invalid duration accepted or emitted events");
            Same(original, Restore(data, species), "Rejected duration partially advanced state");
        }
        var mismatch = service.Advance(original, Species("other"), new HabitatConditions(), 1);
        Require(!mismatch.Success, "Mismatched species was allowed to advance individual");
        Same(original, Restore(data, species), "Rejected species mismatch changed state");

        // Include both sides of each discontinuity. An equality-only test can miss a residual time underflow.
        foreach (double nutrition in new[] { 0, 25, 100d })
            foreach (double hydration in new[] { 0, 25, 100d })
                foreach (double stress in new[] { 59.99999999999, 60, 60.00000000001 })
                {
                    var boundary = BeetleSaveMapper.Export(Individual(species));
                    boundary.Health = 50; boundary.Nutrition = nutrition; boundary.Hydration = hydration; boundary.Stress = stress;
                    var whole = Restore(boundary, species); var parts = whole.Clone();
                    service.Advance(whole, species, new HabitatConditions(), 1.5);
                    for (int i = 0; i < 96; i++) service.Advance(parts, species, new HabitatConditions(), 1.0 / 64);
                    Same(whole, parts, "Recovery/deprivation boundary is not partition invariant");
                }

        // Regression: a tiny positive hydration remainder used to skip deprivation for one entire interval.
        var residualSpecies = new SpeciesDefinition("residual", "Threshold regression", 55, 8,
            care: new CareProfile(nutritionLossPerDay: 7.0902486f, hydrationLossPerDay: 6.919541f,
                recoveryPerDay: 5.942038f, stressChangePerDay: 9.686356f, healthLossPerDay: 3.3327124f),
            lifecycle: new LifecycleProfile(.25, .5, .25, 2));
        var residual = BeetleSaveMapper.Export(Individual(residualSpecies, stage: LifeStage.Egg));
        residual.Health = 18.34807733043473; residual.Nutrition = 45.49884751695154;
        residual.Hydration = 10.827972605278703; residual.Stress = 70.62926929007716;
        var continuous = Restore(residual, residualSpecies); var subdivided = continuous.Clone();
        const double duration = 2.2422335139672427;
        service.Advance(continuous, residualSpecies, new HabitatConditions(), duration);
        for (int i = 0; i < 80; i++) service.Advance(subdivided, residualSpecies, new HabitatConditions(), duration / 80);
        Same(continuous, subdivided, "Tiny hydration remainder skipped a deprivation interval");
    }

    static void Feeding()
    {
        var species = Species();
        var service = new HusbandryService();
        var data = BeetleSaveMapper.Export(Individual(species));
        data.Nutrition = 30; data.Hydration = 20;
        var adult = Restore(data, species);
        Require(service.Feed(adult, species, new FoodPortion(FoodKind.Fruit, 20, 30)).Success, "Adult refused suitable food");
        Near(adult.Condition.Nutrition, 50, "Food nutrition");
        Near(adult.Condition.Hydration, 50, "Food hydration");
        var previous = adult.Clone();
        var wrong = service.Feed(adult, species, new FoodPortion(FoodKind.DecayingWood, 20, 20));
        Require(!wrong.Success && wrong.Events.Count == 0, "Adult accepted larval food");
        Same(adult, previous, "Rejected food partially changed condition");
        Require(service.Feed(adult, species, new FoodPortion(FoodKind.BeetleJelly, 100, 100)).Success, "Jelly rejected");
        Near(adult.Condition.Nutrition, 100, "Food overfilled nutrition");
        Near(adult.Condition.Hydration, 100, "Food overfilled hydration");
        var larva = Individual(species, "larva", stage: LifeStage.Larva);
        Require(service.Feed(larva, species, new FoodPortion(FoodKind.DecayingWood, 1, 1)).Success, "Larva refused suitable food");
        foreach (LifeStage stage in new[] { LifeStage.Egg, LifeStage.Pupa })
        {
            var inactive = Individual(species, stage.ToString(), stage: stage);
            previous = inactive.Clone();
            Require(!service.Feed(inactive, species, new FoodPortion(FoodKind.Fruit, 10, 10)).Success,
                "Non-feeding lifecycle stage accepted food");
            Same(inactive, previous, "Rejected stage feeding changed state");
        }
        var deadSpecies = Species(adultDays: .125);
        var dead = Individual(deadSpecies);
        service.Advance(dead, deadSpecies, new HabitatConditions(), 1);
        previous = dead.Clone();
        Require(!service.Feed(dead, deadSpecies, new FoodPortion(FoodKind.Fruit, 100, 100)).Success, "Feeding resurrected dead beetle");
        Same(dead, previous, "Dead individual accepted food effects");
    }

    static void Breeding()
    {
        var species = Species();
        var service = new BreedingService();
        var mother = Individual(species, "mother");
        var father = Individual(species, "father", BeetleSex.Male);
        var referenceMother = mother.Clone();
        var referenceFather = father.Clone();
        string[] ids = { "child-a", "child-b", "child-c" };
        var first = service.Breed(species, mother, father, ids, new FixedRandom(.5));
        var repeated = service.Breed(species, referenceMother, referenceFather, ids, new FixedRandom(.5));
        Require(first.Success && first.Offspring.Count == 3 && repeated.Success, "Valid adult pair failed breeding");
        Require(mother.BreedingCooldownDays > 0 && father.BreedingCooldownDays > 0, "Breeding did not reserve both parents");
        for (int i = 0; i < ids.Length; i++)
        {
            var child = first.Offspring[i];
            Require(child.Id == ids[i] && child.SpeciesId == species.Id && child.Stage == LifeStage.Egg,
                "Offspring identity/species/stage not preserved");
            Require(child.Pedigree.MotherId == mother.Id && child.Pedigree.FatherId == father.Id,
                "Offspring lost parentage");
            Same(child, repeated.Offspring[i], "Same parent snapshots and random stream produced different offspring");
        }
        RejectBreeding(species, mother, father, ids, "Cooldown bypassed");
        var husbandry = new HusbandryService();
        husbandry.Advance(mother, species, new HabitatConditions(), .5);
        husbandry.Advance(father, species, new HabitatConditions(), .5);
        Require(service.Breed(species, mother, father, new[] { "next-a", "next-b", "next-c" }, new FixedRandom(.5)).Success,
            "Expired cooldown still blocks breeding");

        mother = Individual(species, "mother"); father = Individual(species, "father", BeetleSex.Male);
        RejectBreeding(species, mother, Individual(species, "same-sex"), ids, "Same-sex breeding accepted");
        RejectBreeding(species, mother, Individual(species, "unknown-sex", BeetleSex.Unknown), ids, "Unknown-sex breeding accepted");
        RejectBreeding(species, mother, Individual(species, "larva", BeetleSex.Male, LifeStage.Larva), ids, "Juvenile breeding accepted");
        RejectBreeding(species, mother, Individual(Species("other"), "foreign", BeetleSex.Male), ids, "Cross-species breeding accepted");
        RejectBreeding(species, mother, father, new[] { "a", "a", "b" }, "Duplicate offspring IDs accepted");
        RejectBreeding(species, mother, father, new[] { "mother", "b", "c" }, "Offspring reused parent identity");
        RejectBreeding(species, mother, father, new[] { "a" }, "Incorrect clutch size accepted");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -.01, 1 })
        {
            var beforeMother = mother.Clone(); var beforeFather = father.Clone();
            var result = service.Breed(species, mother, father, ids, new FixedRandom(invalid));
            Require(!result.Success && result.Offspring.Count == 0 && result.Events.Count == 0, "Invalid random source produced a partial clutch");
            Same(mother, beforeMother, "Invalid random source consumed mother");
            Same(father, beforeFather, "Invalid random source consumed father");
        }
        var daughterData = BeetleSaveMapper.Export(first.Offspring[0]);
        daughterData.Sex = (int)BeetleSex.Female; daughterData.Stage = (int)LifeStage.Adult;
        daughterData.TotalAgeDays = species.Lifecycle.StartAge(LifeStage.Adult); daughterData.StageAgeDays = 0;
        var daughter = Restore(daughterData, species);
        var sonData = BeetleSaveMapper.Export(first.Offspring[1]);
        sonData.Sex = (int)BeetleSex.Male; sonData.Stage = (int)LifeStage.Adult;
        sonData.TotalAgeDays = species.Lifecycle.StartAge(LifeStage.Adult); sonData.StageAgeDays = 0;
        var son = Restore(sonData, species);
        RejectBreeding(species, daughter, father, ids, "Parent-child breeding accepted");
        RejectBreeding(species, daughter, son, ids, "Sibling breeding accepted");
        var deadData = BeetleSaveMapper.Export(mother);
        deadData.Stage = (int)LifeStage.Dead; deadData.Health = 0;
        RejectBreeding(species, Restore(deadData, species), father, ids, "Dead parent accepted for breeding");

        var beforeCustomMother = mother.Clone(); var beforeCustomFather = father.Clone();
        var custom = new BreedingService(new[] { new MutatingRule() }).Breed(species, mother, father, ids, new FixedRandom(.5));
        Require(!custom.Success && custom.Events.Count == 0 && custom.Offspring.Count == 0, "Rejecting extension rule produced offspring");
        Same(mother, beforeCustomMother, "Extension rule mutated original mother");
        Same(father, beforeCustomFather, "Extension rule mutated original father");
    }

    static void RejectBreeding(SpeciesDefinition species, BeetleIndividual mother, BeetleIndividual father,
        string[] ids, string message)
    {
        var beforeMother = mother.Clone(); var beforeFather = father.Clone();
        var result = new BreedingService().Breed(species, mother, father, ids, new FixedRandom(.5));
        Require(!result.Success && result.Offspring.Count == 0 && result.Events.Count == 0, message);
        Same(mother, beforeMother, message + " (mother mutated)");
        Same(father, beforeFather, message + " (father mutated)");
    }

    static void LifecycleCompatibility()
    {
        var care = new CareProfile(nutritionLossPerDay: 0, hydrationLossPerDay: 0,
            recoveryPerDay: 0, stressChangePerDay: 0, healthLossPerDay: 0);
        var oldSpecies = new SpeciesDefinition("compat", "Compatibility", 55, 8, care: care,
            lifecycle: new LifecycleProfile(3, 30, 10, 90), breeding: new BreedingProfile(cooldownDays: 10));
        var service = new HusbandryService();
        var habitat = new HabitatConditions();
        var adult = Individual(oldSpecies);
        service.Advance(adult, oldSpecies, habitat, 8);
        var saved = BeetleSaveMapper.Export(adult);
        saved.BreedingCooldownDays = 8;
        var revised = new SpeciesDefinition("compat", "Compatibility", 55, 8, care: care,
            lifecycle: new LifecycleProfile(4, 40, 10, 90), breeding: new BreedingProfile(cooldownDays: 7));
        var restored = Restore(saved, revised);
        Near(restored.BreedingCooldownDays, 7, "Lowered cooldown cap rejected or extended an old save");
        Near(saved.BreedingCooldownDays, 8, "Restore mutated source transport data");
        Near(restored.TotalAgeDays, adult.TotalAgeDays, "New preceding-stage durations rewrote historical age");
        Near(restored.StageAgeDays, 8, "Hot reload reset stage progress");
        Require(service.Feed(restored, revised, new FoodPortion(FoodKind.Fruit, 10, 10)).Success,
            "Revised lifecycle blocked feeding");
        Require(service.MarkOverturned(restored, revised).Success && service.HelpRight(restored, revised).Success,
            "Revised lifecycle blocked overturn assistance");
        Require(service.Feed(adult, revised, new FoodPortion(FoodKind.Fruit, 10, 10)).Success,
            "Direct live-definition update requires a save round trip before feeding");
        Require(service.Advance(restored, revised, habitat, .5).Success, "Revised lifecycle blocked time advance");
        Near(restored.TotalAgeDays, adult.TotalAgeDays + .5, "Historical age did not advance monotonically");
        Same(Restore(BeetleSaveMapper.Export(restored), revised), restored, "Migrated save is not stable on a second restore");

        var shortened = new SpeciesDefinition("compat", "Compatibility", 55, 8, care: care,
            lifecycle: new LifecycleProfile(1, 5, 4, 7), breeding: new BreedingProfile(cooldownDays: 7));
        var overdue = Restore(saved, shortened);
        Require(overdue.Stage == LifeStage.Adult && overdue.Condition.Health > 0, "Load killed an adult after a balance patch");
        var boundaryOnly = overdue.Clone();
        Require(service.Advance(boundaryOnly, shortened, habitat, 0).Success && boundaryOnly.Stage == LifeStage.Dead,
            "Explicit zero-time advance cannot settle an overdue stage");
        Near(boundaryOnly.TotalAgeDays, overdue.TotalAgeDays, "Boundary-only advance consumed time");
        Require(service.Feed(overdue, shortened, new FoodPortion(FoodKind.Fruit, 1, 1)).Success,
            "Overdue stage blocked ordinary interaction");
        var result = service.Advance(overdue, shortened, habitat, .5);
        Require(result.Success && overdue.Stage == LifeStage.Dead && result.Events.Count == 1 &&
            result.Events[0].Kind == HusbandryEventKind.Died, "Overdue adult did not end once at next time advance");
        Near(overdue.TotalAgeDays, adult.TotalAgeDays, "Shortened lifespan ran time backwards or re-aged a dead beetle");
        Same(Restore(BeetleSaveMapper.Export(overdue), shortened), overdue, "Historical dead beetle cannot round trip");

        var larva = Individual(oldSpecies, "larva", stage: LifeStage.Larva);
        service.Advance(larva, oldSpecies, habitat, 8);
        double priorAge = larva.TotalAgeDays;
        var parts = larva.Clone();
        result = service.Advance(larva, shortened, habitat, .5);
        service.Advance(parts, shortened, habitat, .25);
        service.Advance(parts, shortened, habitat, .25);
        Require(result.Success && larva.Stage == LifeStage.Pupa && result.Events.Count == 1,
            "Overdue juvenile did not transition exactly once");
        Near(larva.TotalAgeDays, priorAge + .5, "Shortened juvenile stage consumed negative time");
        Near(larva.StageAgeDays, .5, "Old-stage historical time leaked into next-stage progress");
        Same(parts, larva, "Shortened-stage migration depends on frame partitioning");
    }

    static void Persistence()
    {
        var species = Species();
        var original = Individual(species);
        var exported = BeetleSaveMapper.Export(original);
        Same(Restore(exported, species), original, "Save round trip lost state");
        var clone = original.Clone();
        new HusbandryService().Advance(clone, species, new HabitatConditions(), .5);
        Near(original.StageAgeDays, 0, "Clone mutation leaked to source");
        var restored = Restore(exported, species);
        exported.Health = 1; exported.DisplayName = "changed DTO";
        Same(restored, original, "Restore retained mutable DTO");
        var catalog = new SpeciesCatalog(new[] { species });
        Action<Action<BeetleSaveData>, string> reject = (mutate, message) =>
        {
            var data = BeetleSaveMapper.Export(original); mutate(data);
            var result = BeetleSaveMapper.Restore(data, catalog);
            Require(!result.Success && result.Individual == null && !string.IsNullOrEmpty(result.ErrorCode), message);
        };
        reject(d => d.Version++, "Future save version accepted silently");
        reject(d => d.Version = 0, "Unsupported old save version accepted silently");
        reject(d => d.SpeciesId = "missing", "Unknown species save accepted");
        reject(d => d.Id = " ", "Empty save identity accepted");
        reject(d => d.Health = double.NaN, "NaN health accepted");
        reject(d => d.TotalAgeDays = double.PositiveInfinity, "Infinite age accepted");
        reject(d => d.Nutrition = double.NegativeInfinity, "Infinite nutrition accepted");
        reject(d => d.BreedingCooldownDays = -1, "Negative cooldown accepted");
        reject(d => d.Strength = 1.1f, "Out-of-range inherited trait accepted");
        reject(d => d.Sex = 99, "Unknown serialized sex accepted");
        reject(d => d.Stage = 99, "Unknown serialized stage accepted");
        reject(d => d.Health = 0, "Living stage with zero health accepted");
        reject(d => d.Stage = (int)LifeStage.Dead, "Dead stage with living health accepted");
        reject(d => d.StageAgeDays = d.TotalAgeDays + 1, "Stage age beyond total age accepted");
        reject(d => { d.MotherId = "parent"; d.FatherId = "parent"; }, "Same parent identity accepted twice");
        reject(d => { d.MotherId = d.Id; d.FatherId = "other"; }, "Self ancestor accepted");
        reject(d => { d.MotherId = "mother"; d.FatherId = null; }, "Incomplete parentage accepted");
        var oldMeters = BeetleSaveMapper.Export(original);
        oldMeters.Nutrition = -1; oldMeters.Hydration = 101; oldMeters.Stress = -2;
        var cleaned = Restore(oldMeters, species);
        Require(cleaned.Condition.Nutrition == 0 && cleaned.Condition.Hydration == 100 && cleaned.Condition.Stress == 0,
            "Finite legacy condition values were not cleaned at restore");
        var pedigreeData = BeetleSaveMapper.Export(original);
        pedigreeData.MotherId = "mother"; pedigreeData.FatherId = "father";
        pedigreeData.AncestorIds = new[] { "grandmother", "mother", "father" };
        var descendant = Restore(pedigreeData, species);
        pedigreeData.AncestorIds[0] = "corrupted";
        var roundTrip = BeetleSaveMapper.Export(descendant);
        Require(Array.IndexOf(roundTrip.AncestorIds, "grandmother") >= 0 && Array.IndexOf(roundTrip.AncestorIds, "corrupted") < 0,
            "Pedigree retained caller-owned ancestor array");
        roundTrip.AncestorIds[0] = "export-corruption";
        Require(Array.IndexOf(BeetleSaveMapper.Export(descendant).AncestorIds, "export-corruption") < 0,
            "Export exposed pedigree storage");
    }

    static void ContestConsequences()
    {
        var species = Species();
        var beetle = Individual(species);
        var recovery = new ContestRecoveryService();
        var original = beetle.Clone();
        var wrong = recovery.Apply(beetle, new ContestAftermath("contest", "someone-else", 1), new RecoveryPolicy());
        Require(!wrong.Success && wrong.Events.Count == 0, "Aftermath for a different individual was accepted");
        Same(beetle, original, "Rejected aftermath mutated individual");
        var result = recovery.Apply(beetle, new ContestAftermath("contest", beetle.Id, 1), new RecoveryPolicy());
        Require(result.Success && beetle.Condition.Health == original.Condition.Health
            && beetle.Condition.Nutrition < original.Condition.Nutrition && beetle.Condition.Hydration < original.Condition.Hydration,
            "Default contest consequences did not preserve health while applying resource cost");
        var beforeZero = beetle.Clone();
        Require(recovery.Apply(beetle, new ContestAftermath("zero-effort", beetle.Id, 0), new RecoveryPolicy()).Success,
            "Zero-exertion result failed");
        Same(beetle, beforeZero, "Zero exertion charged nonzero consequences");
        result = recovery.Apply(beetle, new ContestAftermath("fatal-policy", beetle.Id, 1), new RecoveryPolicy(healthCost: 100));
        Require(result.Success && beetle.Stage == LifeStage.Dead && beetle.Condition.Health == 0,
            "Explicit lethal consequence left a living zero-health individual");
        var dead = beetle.Clone();
        Require(!recovery.Apply(beetle, new ContestAftermath("later", beetle.Id, 1), new RecoveryPolicy()).Success,
            "Dead individual accepted additional contest consequences");
        Same(beetle, dead, "Rejected dead aftermath mutated individual");
        foreach (float invalid in new[] { -.01f, 1.01f, float.NaN, float.PositiveInfinity })
            Rejects(() => new ContestAftermath("contest", "adult", invalid), "Invalid exertion accepted");
        Rejects(() => new RecoveryPolicy(healthCost: float.NaN), "Non-finite recovery policy accepted");
    }

    sealed class FixedRandom : IRandomSource
    {
        readonly double value;
        public FixedRandom(double value) { this.value = value; }
        public double NextUnit() => value;
    }

    sealed class MutatingRule : IBreedingRule
    {
        public string RejectionReason(SpeciesDefinition species, BeetleIndividual mother, BeetleIndividual father)
        {
            new HusbandryService().Advance(mother, species, new HabitatConditions(), .25);
            new HusbandryService().Advance(father, species, new HabitatConditions(), .25);
            return "Validation-only rejection after modifying supplied snapshots";
        }
    }
}
