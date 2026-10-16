using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BeetleArena;
using BugDuel.Domain;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>In-memory species contracts and isolated controller scene lifecycle checks.</summary>
public static class BeetleSpeciesIntegrationValidation
{
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    [MenuItem("Tools/Beetle Arena/Validate Species Integration")]
    public static void Run()
    {
        var scene = SceneManager.GetActiveScene();
        int sceneCount = SceneManager.sceneCount;
        bool wasDirty = scene.isDirty;
        var report = new StringBuilder("SPECIES / CONTEST INTEGRATION\n");
        var assets = new List<ScriptableObject>();
        try
        {
            var speciesA = Make<BeetleSpeciesAsset>(assets);
            speciesA.speciesId = "validation.horn.a";
            var speciesB = Make<BeetleSpeciesAsset>(assets);
            speciesB.speciesId = "validation.horn.b";
            speciesB.adultLengthMm = 62; speciesB.adultMassGrams = 10;
            var profileA = Profile(assets, speciesA);
            var profileB = Profile(assets, speciesB);
            var settings = Make<BeetleMatchSettings>(assets);
            settings.countdownSeconds = 0;
            var first = BeetleIndividual.Create(speciesA.ToDefinition(), "individual-a", "甲", BeetleSex.Female,
                traits: new BugDuel.Domain.BeetleTraits(.1f, .3f, .7f, .9f));
            var second = BeetleIndividual.Create(speciesB.ToDefinition(), "individual-b", "乙", BeetleSex.Male);
            Eligibility(first, profileA, profileB);
            report.AppendLine("PASS: living adult eligibility, condition thresholds, species/rig/profile validation.");

            var module = Make<BeetleSpeciesValidationModule>(assets);
            settings.motionModules = new BeetleMotionModule[] { module };
            profileA.overrideMotionModules = true;
            profileA.motionModules = new BeetleMotionModule[] { module };
            profileB.overrideMotionModules = true;
            profileB.motionModules = Array.Empty<BeetleMotionModule>();
            var entrantA = BeetleMatchEntrant.FromIndividual(first, profileA);
            var entrantB = BeetleMatchEntrant.FromIndividual(second, profileB);
            var simulation = new BeetleSimulation(settings, 101, entrantA, entrantB, "validation-match");
            Require(simulation.Fighters[0].Entrant == entrantA && simulation.Fighters[1].Entrant == entrantB,
                "Roster was not carried through to fighter states");
            Require(simulation.FighterName(0) == first.DisplayName && simulation.FighterName(1) == second.DisplayName,
                "Persistent roster lost display names");
            var groundBefore = new GroundProbe();
            simulation.SetGround(groundBefore);
            var referenceTraits = simulation.Fighters[0].Traits.Copy();
            float referenceScale = simulation.Fighters[0].VisualScale(settings);
            // Authoring edits after matchmaking must not rewrite an in-flight entrant snapshot.
            profileA.strength = 9; profileA.visualScale = .9f; profileA.size = 1.15f;
            profileA.supportFootOffsets[0] = new Vector3(100, 0, 100);
            profileA.motionModules[0] = null;
            speciesA.adultLengthMm = 500;
            simulation.Fighters[0].Traits.strength = 999;
            var another = new BeetleSimulation(settings, 982, entrantA, entrantB);
            SameTraits(another.Fighters[0].Traits, referenceTraits, "Snapshot traits changed with seed, authoring edits or another match");
            Require(another.Fighters[0].VisualScale(settings) == referenceScale, "Snapshot retained profile scale reference");
            var groundAfter = new GroundProbe();
            another.SetGround(groundAfter);
            Require(groundAfter.Samples.Count == groundBefore.Samples.Count, "Ground sampling shape changed");
            for (int i = 0; i < groundBefore.Samples.Count; i++)
                Require(groundBefore.Samples[i] == groundAfter.Samples[i], "Snapshot retained profile foot array");
            Require(another.Fighters[0].Motion.TryStart(.5f), "Snapshot lost copied module array");
            Require(!another.Fighters[1].Motion.TryStart(.5f), "Explicit empty species module list inherited global modules");
            Require(simulation.MatchId != another.MatchId, "Different contests reused settlement identity");
            Rejects(() => new BeetleSimulation(settings, 1, entrantA, null), "Half-specified roster accepted");
            Rejects(() => new BeetleSimulation(settings, 1, entrantA, entrantA), "Same individual occupied both roster slots");
            Rejects(() => new BeetleSimulation(settings, 1, entrantA, entrantB, " "), "Blank match identity accepted");
            report.AppendLine("PASS: immutable entrant snapshots, per-match trait copies, species feet/modules, stable names and match identity.");

            // Default exhibitions must remain deterministic and carry no persistent individual IDs.
            var exhibitionA = new BeetleSimulation(settings, 713);
            var exhibitionB = new BeetleSimulation(settings, 713);
            for (int i = 0; i < 2; i++)
            {
                SameTraits(exhibitionA.Fighters[i].Traits, exhibitionB.Fighters[i].Traits, "Legacy exhibition lost seeded generation");
                Require(exhibitionA.Fighters[i].Entrant == null, "Exhibition invented persistent individual identity");
            }
            Completion(settings, entrantA, entrantB, first, second, report);
            ControllerStartAndSwitch(assets, report);
            report.AppendLine("ALL SPECIES INTEGRATION CHECKS PASSED");
        }
        finally
        {
            for (int i = assets.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(assets[i]);
            Directory.CreateDirectory("Artifacts/Framework");
            File.WriteAllText("Artifacts/Framework/SpeciesIntegrationValidation.txt", report.ToString());
        }
        Require(SceneManager.sceneCount == sceneCount && SceneManager.GetActiveScene() == scene && scene.isDirty == wasDirty,
            "In-memory validation changed the user's scene state");
    }

    static T Make<T>(List<ScriptableObject> assets) where T : ScriptableObject
    {
        var value = ScriptableObject.CreateInstance<T>();
        assets.Add(value);
        return value;
    }

    static void ControllerStartAndSwitch(List<ScriptableObject> assets, StringBuilder report)
    {
        using var fixture = new BeetleValidationScene("SpeciesControllerValidation");
        var match = fixture.Match;
        Require(match.arenas != null && match.arenas.Length > 1, "Controller fixture needs multiple arenas");
        var species = Make<BeetleSpeciesAsset>(assets);
        species.speciesId = "validation.controller";
        var profile = Profile(assets, species);
        profile.supportFootOffsets = (Vector3[])match.settings.supportFootOffsets.Clone();
        profile.rigId = match.actors[0].rigId;
        var first = BeetleMatchEntrant.FromIndividual(
            BeetleIndividual.Create(species.ToDefinition(), "controller-first", "First", BeetleSex.Female), profile);
        var second = BeetleMatchEntrant.FromIndividual(
            BeetleIndividual.Create(species.ToDefinition(), "controller-second", "Second", BeetleSex.Male), profile);
        match.SelectArena(1);
        var original = new BeetleSimulation(match.settings, 71, first, second);
        match.StartMatch(original);
        int originalRound = match.RoundNumber;
        var originalSolver = match.Contacts;
        var originalContacts = match.arenaContacts;
        var start = typeof(BeetleMatchController).GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance);
        Require(start != null, "Unity Start entry could not be located");
        start.Invoke(match, null);
        Require(ReferenceEquals(match.Simulation, original) && match.RoundNumber == originalRound
            && ReferenceEquals(match.Contacts, originalSolver) && match.ArenaIndex == 1
            && ReferenceEquals(original.Ground, match.ActiveSurface),
            "Unity Start replaced or moved a simulation bound by application Awake");

        original.ReachTimeLimit();
        int requests = 0;
        Action<int> ignored = seed => requests++;
        match.PersistentMatchRequested += ignored;
        try { match.SwitchArena(); }
        finally { match.PersistentMatchRequested -= ignored; }
        Require(requests == 1, "Persistent arena switch did not request a fresh match");
        RequireUnchangedArena(match, original, originalRound, originalSolver, originalContacts);

        var rejection = new InvalidOperationException("Validation-only eligibility rejection");
        Action<int> declined = seed => throw rejection;
        match.PersistentMatchRequested += declined;
        bool propagated = false;
        try { match.SwitchArena(); }
        catch (InvalidOperationException error) { propagated = ReferenceEquals(error, rejection); }
        finally { match.PersistentMatchRequested -= declined; }
        Require(propagated, "Persistent arena switch swallowed or replaced eligibility failure");
        RequireUnchangedArena(match, original, originalRound, originalSolver, originalContacts);

        Action<int> accepted = seed => match.StartMatch(new BeetleSimulation(match.settings, seed, first, second));
        match.PersistentMatchRequested += accepted;
        try { match.SwitchArena(); }
        finally { match.PersistentMatchRequested -= accepted; }
        Require(!ReferenceEquals(match.Simulation, original) && match.RoundNumber == originalRound + 1
            && match.ArenaIndex == 2 % match.arenas.Length && ReferenceEquals(match.Simulation.Ground, match.ActiveSurface),
            "Successful persistent arena switch did not retain the new arena and fresh simulation");

        // A pre-bound exhibition also belongs to the application; Start must not reroll its seeded roster.
        match.StartExhibition(19);
        var exhibition = match.Simulation;
        int exhibitionRound = match.RoundNumber, exhibitionArena = match.ArenaIndex;
        start.Invoke(match, null);
        Require(ReferenceEquals(match.Simulation, exhibition) && match.RoundNumber == exhibitionRound
            && match.ArenaIndex == exhibitionArena, "Unity Start rerolled a pre-bound exhibition");
        report.AppendLine("PASS: pre-bound Unity Start, failed/ignored persistent arena requests roll back, successful switches commit.");
    }

    static void RequireUnchangedArena(BeetleMatchController match, BeetleSimulation original, int round,
        BeetleContactSolver solver, Collider[] contacts)
    {
        Require(ReferenceEquals(match.Simulation, original) && match.RoundNumber == round && match.ArenaIndex == 1
            && ReferenceEquals(match.Contacts, solver) && ReferenceEquals(match.arenaContacts, contacts)
            && ReferenceEquals(original.Ground, match.ActiveSurface),
            "Rejected or unanswered persistent request changed the finished match's arena/bindings");
        for (int i = 0; i < match.arenas.Length; i++)
            if (match.arenas[i]) Require(match.arenas[i].gameObject.activeSelf == (i == 1), "Arena activation was not rolled back");
    }

    static BeetleCombatProfile Profile(List<ScriptableObject> assets, BeetleSpeciesAsset species)
    {
        var profile = Make<BeetleCombatProfile>(assets);
        profile.species = species;
        profile.supportFootOffsets = new[]
        {
            new Vector3(-1, 0, .9f), new Vector3(-1, 0, 0), new Vector3(-1, 0, -.9f),
            new Vector3(1, 0, .9f), new Vector3(1, 0, 0), new Vector3(1, 0, -.9f)
        };
        return profile;
    }

    static void Rejects(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        catch (InvalidOperationException) { return; }
        throw new Exception(message);
    }

    static BeetleIndividual Changed(BeetleIndividual source, BeetleCombatProfile profile, Action<BeetleSaveData> change)
    {
        var data = BeetleSaveMapper.Export(source);
        change(data);
        var restored = BeetleSaveMapper.Restore(data, new SpeciesCatalog(new[] { profile.species.ToDefinition() }));
        Require(restored.Success, "Eligibility fixture could not be restored: " + restored.ErrorCode);
        return restored.Individual;
    }

    static void Eligibility(BeetleIndividual adult, BeetleCombatProfile profile, BeetleCombatProfile other)
    {
        Require(BeetleMatchEntrant.FromIndividual(adult, profile).IndividualId == adult.Id, "Healthy adult cannot enter combat");
        Rejects(() => BeetleMatchEntrant.FromIndividual(adult, other), "Profile/species mismatch accepted");
        foreach (LifeStage stage in new[] { LifeStage.Egg, LifeStage.Larva, LifeStage.Pupa })
        {
            var juvenile = BeetleIndividual.Create(profile.species.ToDefinition(), "young", "Young", BeetleSex.Unknown, stage);
            Rejects(() => BeetleMatchEntrant.FromIndividual(juvenile, profile), "Juvenile entered combat");
        }
        foreach (var change in new Action<BeetleSaveData>[]
        {
            d => { d.Stage = (int)LifeStage.Dead; d.Health = 0; },
            d => d.Health = profile.minimumCombatHealth - 1,
            d => d.Stress = profile.maximumCombatStress + 1,
            d => d.Nutrition = 0,
            d => d.Hydration = 0
        })
        {
            var ineligible = Changed(adult, profile, change);
            Rejects(() => BeetleMatchEntrant.FromIndividual(ineligible, profile), "Dead/unfit individual entered combat");
        }
        profile.species.combatEnabled = false;
        Rejects(() => BeetleMatchEntrant.FromIndividual(adult, profile), "Non-combat species entered combat");
        profile.species.combatEnabled = true;
        var feet = profile.supportFootOffsets;
        profile.supportFootOffsets = new Vector3[5];
        Rejects(() => BeetleMatchEntrant.FromIndividual(adult, profile), "Uncalibrated foot layout accepted");
        profile.supportFootOffsets = feet;
        feet[0].x = float.NaN;
        Rejects(() => BeetleMatchEntrant.FromIndividual(adult, profile), "NaN foot position accepted");
        feet[0].x = -1;
        profile.strength = float.PositiveInfinity;
        Rejects(() => BeetleMatchEntrant.FromIndividual(adult, profile), "Infinite combat stat accepted");
        profile.strength = 1;
    }

    static void SameTraits(BeetleArena.BeetleTraits actual, BeetleArena.BeetleTraits expected, string message)
    {
        foreach (var field in typeof(BeetleArena.BeetleTraits).GetFields(BindingFlags.Public | BindingFlags.Instance))
            Require(Equals(field.GetValue(actual), field.GetValue(expected)), message + " (" + field.Name + ")");
    }

    static void Completion(BeetleMatchSettings settings, BeetleMatchEntrant first, BeetleMatchEntrant second,
        BeetleIndividual sourceA, BeetleIndividual sourceB, StringBuilder report)
    {
        var savedA = BeetleSaveMapper.Export(sourceA);
        var savedB = BeetleSaveMapper.Export(sourceB);
        var simulation = new BeetleSimulation(settings, 17, first, second, "settlement-once");
        int notifications = 0;
        BeetleMatchResult observed = null;
        simulation.Completed += result =>
        {
            notifications++;
            Require(simulation.Phase == MatchPhase.Finished && ReferenceEquals(result, simulation.Result),
                "Completion observed partially finalized result");
            observed = result;
            simulation.ReachTimeLimit(); // Reentrant listeners must not publish the same result twice.
        };
        simulation.Fighters[0].Fatigue = 9;
        simulation.ReachTimeLimit();
        Require(notifications == 1 && observed != null && observed.MatchId == "settlement-once"
            && observed.Reason == BeetleResultReason.TimeLimit && observed.Winner == -1, "Deadline result was not published once");
        Require(observed.Fighters.Count == 2 && observed.Fighters[0].IndividualId == sourceA.Id
            && observed.Fighters[1].IndividualId == sourceB.Id && observed.Fighters[0].SpeciesId == sourceA.SpeciesId,
            "Result cannot be associated with persistent individuals");
        float fatigue = observed.Fighters[0].Fatigue;
        float stamina = observed.Fighters[0].RemainingStaminaFraction;
        simulation.Fighters[0].Fatigue = 99; simulation.Fighters[0].Stamina = 0;
        for (int i = 0; i < 20; i++) simulation.Step(BeetleSimulation.FixedStep);
        simulation.ReachTimeLimit();
        Require(notifications == 1 && ReferenceEquals(observed, simulation.Result)
            && observed.Fighters[0].Fatigue == fatigue && observed.Fighters[0].RemainingStaminaFraction == stamina,
            "Presentation/repeated completion changed frozen settlement");
        Require(sourceA.Condition.Health == savedA.Health && sourceA.Condition.Nutrition == savedA.Nutrition
            && sourceA.Condition.Stress == savedA.Stress && sourceA.StageAgeDays == savedA.StageAgeDays
            && sourceB.Condition.Health == savedB.Health && sourceB.StageAgeDays == savedB.StageAgeDays,
            "Contest wrote directly to persistent husbandry state");
        var ringOut = new BeetleSimulation(settings, 18, first, second);
        notifications = 0;
        ringOut.Completed += result => notifications++;
        ringOut.Fighters[0].HasFallen = true;
        ringOut.ReachTimeLimit();
        Require(notifications == 1 && ringOut.Result.Reason == BeetleResultReason.RingOut && ringOut.Result.Winner == 1,
            "Ring-out completion bypassed the common result contract");
        report.AppendLine("PASS: legacy seeded exhibitions; single/reentrant completion, immutable result, ring-out/deadline paths, no husbandry writes.");
    }

    sealed class GroundProbe : IBeetleGround
    {
        public readonly List<Vector2> Samples = new List<Vector2>();
        public bool Sample(Vector2 position, out BeetleGroundSample sample)
        {
            Require(float.IsFinite(position.x) && float.IsFinite(position.y), "Non-finite ground probe");
            Samples.Add(position);
            sample = new BeetleGroundSample { height = 0, normal = Vector3.up, grip = 1 };
            return true;
        }
    }
}

public sealed class BeetleSpeciesValidationModule : BeetleMotionModule
{
    public override string DisplayName => "species-validation-only";
    public override BeetleMotionChannel Channel => BeetleMotionChannel.Combat;
    public override bool CanStart(in BeetleMotionContext context) => true;
    public override bool CanContinue(in BeetleMotionContext context) => true;
    public override BeetleMotionSample Sample(in BeetleMotionContext context, float phase) => default;
}
