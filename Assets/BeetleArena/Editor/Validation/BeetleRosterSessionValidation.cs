using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BeetleArena;
using BugDuel.Domain;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Application transactions and durable settlement identity; operates entirely on in-memory fixtures.</summary>
public static class BeetleRosterSessionValidation
{
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    [MenuItem("Tools/Beetle Arena/Validate Roster Session")]
    public static void Run()
    {
        var scene = SceneManager.GetActiveScene();
        int sceneCount = SceneManager.sceneCount;
        bool wasDirty = scene.isDirty;
        var report = new StringBuilder("ROSTER APPLICATION CONTRACTS\n");
        try
        {
            using (var fixture = new Fixture())
            {
                IsolationAndBreeding(fixture);
                report.AppendLine("PASS: input/read/export isolation and transactional breeding identity checks.");
                ReservationsAndSettlement(fixture);
                report.AppendLine("PASS: occupied individuals, forged results, two-person settlement, durable duplicate prevention and fresh rematch snapshots.");
                RejectionIsAtomic(fixture);
                report.AppendLine("PASS: invalid second settlement participant, invalid restore, ineligible entrants and cancellation preserve roster integrity.");
            }
            Require(SceneManager.sceneCount == sceneCount && SceneManager.GetActiveScene() == scene && scene.isDirty == wasDirty,
                "Roster validation changed the user's scene state");
            report.AppendLine("ALL ROSTER CHECKS PASSED");
        }
        finally
        {
            Directory.CreateDirectory("Artifacts/Framework");
            File.WriteAllText("Artifacts/Framework/RosterSessionValidation.txt", report.ToString());
        }
    }

    static void Rejects(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        catch (InvalidOperationException) { return; }
        throw new Exception(message);
    }

    static BeetleIndividual Read(BeetleRosterSession session, string id)
    {
        Require(session.TryGet(id, out var value), "Roster lost individual: " + id);
        return value;
    }

    static void SameRecord(BeetleIndividual actual, BeetleIndividual expected, string message)
    {
        string actualJson = JsonUtility.ToJson(BeetleSaveMapper.Export(actual));
        string expectedJson = JsonUtility.ToJson(BeetleSaveMapper.Export(expected));
        Require(actualJson == expectedJson, message);
    }

    static void IsolationAndBreeding(Fixture fixture)
    {
        var session = fixture.Session();
        var original = fixture.Individual("female", BeetleSex.Female);
        session.Add(original);
        session.Add(fixture.Individual("male", BeetleSex.Male));
        new HusbandryService().Advance(original, fixture.Definition, new HabitatConditions(), .125);
        Require(Read(session, "female").StageAgeDays == 0, "Add retained caller-owned individual");
        var read = Read(session, "female");
        new HusbandryService().Advance(read, fixture.Definition, new HabitatConditions(), .125);
        Require(Read(session, "female").StageAgeDays == 0, "TryGet exposed owned individual");
        var snapshot = session.Snapshot();
        new HusbandryService().Advance(snapshot[0], fixture.Definition, new HabitatConditions(), .125);
        Require(Read(session, snapshot[0].Id).StageAgeDays == 0, "Snapshot exposed owned individual");
        Rejects(() => session.Add(fixture.Individual("female", BeetleSex.Female)), "Duplicate individual was registered");
        var mother = Read(session, "female"); var father = Read(session, "male");
        var invalid = session.Breed("female", "male", new[] { "male", "child-b", "child-c" }, new FixedRandom());
        Require(!invalid.Success && session.Count == 2, "Existing roster identity reused by offspring");
        SameRecord(Read(session, "female"), mother, "Rejected clutch consumed mother");
        SameRecord(Read(session, "male"), father, "Rejected clutch consumed father");
        var result = session.Breed("female", "male", new[] { "child-a", "child-b", "child-c" }, new FixedRandom());
        Require(result.Success && session.Count == 5, "Successful clutch was not committed to roster");
        new HusbandryService().Advance(result.Offspring[0], fixture.Definition, new HabitatConditions(), .125);
        Require(Read(session, "child-a").StageAgeDays == 0, "Breeding return value exposed roster child storage");
        var export = session.Export();
        export.Individuals[0].DisplayName = "outside mutation";
        Require(Read(session, export.Individuals[0].Id).DisplayName != "outside mutation", "Export exposed roster state");
    }

    static void ReservationsAndSettlement(Fixture fixture)
    {
        var session = fixture.Pair();
        var first = Read(session, "female"); var second = Read(session, "male");
        var match = session.BeginMatch(first.Id, second.Id, fixture.Settings, 901);
        Require(session.ActiveMatchCount == 1 && session.IsOccupied(first.Id) && session.IsOccupied(second.Id), "Match did not reserve both individuals");
        Require(!session.Feed(first.Id, new FoodPortion(FoodKind.Fruit, 10, 10)).Success, "Feeding modified an occupied individual");
        Require(!session.Advance(second.Id, new HabitatConditions(), 1).Success, "Care advanced an occupied individual");
        Require(!session.Breed(first.Id, second.Id, new[] { "a", "b", "c" }, new FixedRandom()).Success, "Occupied parents were bred");
        Rejects(() => session.BeginMatch(first.Id, second.Id, fixture.Settings, 902), "Concurrent match reused participants");
        Rejects(() => session.Export(), "Active contest exported without reservation state");
        Rejects(() => session.Restore(new BeetleRosterSaveData()), "Active contest was replaced by restore");
        SameRecord(Read(session, first.Id), first, "Rejected occupied operation changed first individual");
        SameRecord(Read(session, second.Id), second, "Rejected occupied operation changed second individual");

        var forged = new BeetleSimulation(fixture.Settings, 901, match.Fighters[0].Entrant, match.Fighters[1].Entrant, match.MatchId);
        forged.ReachTimeLimit();
        Rejects(() => session.Complete(forged.Result), "Unregistered simulation forged an authorized result using its match ID");
        Require(session.ActiveMatchCount == 1, "Rejected forged result released the legitimate match");

        float oldEndurance = match.Fighters[0].Traits.endurance;
        match.Fighters[0].Stamina = match.Fighters[0].StaminaLimit * .25f;
        match.Fighters[1].Stamina = match.Fighters[1].StaminaLimit * .5f;
        match.ReachTimeLimit();
        Require(session.Complete(match.Result), "Authorized frozen result did not settle");
        Require(session.ActiveMatchCount == 0 && !session.IsOccupied(first.Id) && !session.IsOccupied(second.Id), "Settlement did not release both participants");
        var settledFirst = Read(session, first.Id); var settledSecond = Read(session, second.Id);
        Require(settledFirst.Condition.Nutrition < first.Condition.Nutrition && settledSecond.Condition.Nutrition < second.Condition.Nutrition,
            "Settlement did not apply consequences to both individuals");
        Require(settledFirst.Condition.Health == first.Condition.Health && settledSecond.Condition.Health == second.Condition.Health,
            "Default contest policy converted competition into health damage");
        Require(!session.Complete(match.Result), "Same match was settled twice");
        SameRecord(Read(session, first.Id), settledFirst, "Repeated settlement charged first individual twice");
        SameRecord(Read(session, second.Id), settledSecond, "Repeated settlement charged second individual twice");

        var rematch = session.BeginMatch(first.Id, second.Id, fixture.Settings, 901);
        Require(rematch.MatchId != match.MatchId && rematch.Fighters[0].Traits.endurance < oldEndurance,
            "Rematch reused identity or stale pre-contest condition snapshot");
        Require(session.Cancel(rematch.MatchId) && !session.Cancel(rematch.MatchId), "Cancel is not idempotent");
        SameRecord(Read(session, first.Id), settledFirst, "Cancelled match applied competition consequences");

        var data = session.Export();
        Require(data.AppliedMatchIds.Length == 1 && data.AppliedMatchIds[0] == match.MatchId, "Settlement ledger was not exported");
        var restored = fixture.Session();
        restored.Restore(data);
        data.Individuals[0].Nutrition = 0; data.AppliedMatchIds[0] = "outside mutation";
        Require(!restored.Complete(match.Result), "Restored ledger allowed duplicate settlement");
        SameRecord(Read(restored, first.Id), settledFirst, "Restore retained caller-owned save data");
        SameRecord(Read(restored, second.Id), settledSecond, "Roster round trip lost second individual");
    }

    static void RejectionIsAtomic(Fixture fixture)
    {
        var session = fixture.Pair();
        var first = Read(session, "female"); var second = Read(session, "male");
        var match = session.BeginMatch(first.Id, second.Id, fixture.Settings, 27);
        match.Fighters[0].Stamina = 0;
        match.Fighters[1].Stamina = float.NaN;
        match.ReachTimeLimit();
        Rejects(() => session.Complete(match.Result), "Invalid second result participant was settled");
        SameRecord(Read(session, first.Id), first, "Second participant validation failure partially committed first participant");
        SameRecord(Read(session, second.Id), second, "Invalid participant altered roster");
        Require(session.ActiveMatchCount == 1 && session.IsOccupied(first.Id), "Failed settlement released reservations");
        Require(session.Cancel(match.MatchId), "Failed settlement could not be cancelled");

        var malformed = session.Export();
        malformed.Individuals[0].Health = 70;
        malformed.Individuals[1].Id = malformed.Individuals[0].Id;
        Rejects(() => session.Restore(malformed), "Duplicate save identity accepted");
        SameRecord(Read(session, first.Id), first, "Invalid restore partially replaced first record");
        SameRecord(Read(session, second.Id), second, "Invalid restore partially replaced second record");
        malformed = session.Export();
        malformed.AppliedMatchIds = new[] { "duplicate", "duplicate" };
        Rejects(() => session.Restore(malformed), "Duplicate settlement ledger identity accepted");
        SameRecord(Read(session, first.Id), first, "Invalid ledger restore modified roster");
        Rejects(() => session.BeginMatch(first.Id, first.Id, fixture.Settings, 1), "One individual accepted in both match seats");
        Require(session.ActiveMatchCount == 0 && !session.IsOccupied(first.Id), "Failed match start leaked reservations");

        foreach (bool dead in new[] { false, true })
        {
            var rejected = fixture.Pair();
            var data = rejected.Export();
            data.Individuals[0].Health = dead ? 0 : 20;
            if (dead) data.Individuals[0].Stage = (int)LifeStage.Dead;
            rejected.Restore(data);
            Rejects(() => rejected.BeginMatch("female", "male", fixture.Settings, 1), "Sick/dead roster individual entered combat");
            Require(rejected.ActiveMatchCount == 0 && !rejected.IsOccupied("female") && !rejected.IsOccupied("male"),
                "Eligibility failure leaked participant reservations");
        }
    }

    sealed class FixedRandom : IRandomSource { public double NextUnit() => .5; }

    sealed class Fixture : IDisposable
    {
        readonly BeetleSpeciesAsset species = ScriptableObject.CreateInstance<BeetleSpeciesAsset>();
        readonly BeetleCombatProfile profile = ScriptableObject.CreateInstance<BeetleCombatProfile>();
        public readonly BeetleMatchSettings Settings = ScriptableObject.CreateInstance<BeetleMatchSettings>();
        public SpeciesDefinition Definition { get; }
        public Fixture()
        {
            species.speciesId = "validation.session";
            Definition = species.ToDefinition();
            profile.species = species;
            profile.supportFootOffsets = new[]
            {
                new Vector3(-1, 0, 1), new Vector3(-1, 0, 0), new Vector3(-1, 0, -1),
                new Vector3(1, 0, 1), new Vector3(1, 0, 0), new Vector3(1, 0, -1)
            };
            Settings.countdownSeconds = 0;
        }
        public BeetleIndividual Individual(string id, BeetleSex sex) => BeetleIndividual.Create(Definition, id, id, sex);
        public BeetleRosterSession Session() => new BeetleRosterSession(new SpeciesCatalog(new[] { Definition }),
            new Dictionary<string, BeetleCombatProfile> { { Definition.Id, profile } });
        public BeetleRosterSession Pair()
        {
            var session = Session();
            session.Add(Individual("female", BeetleSex.Female));
            session.Add(Individual("male", BeetleSex.Male));
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
