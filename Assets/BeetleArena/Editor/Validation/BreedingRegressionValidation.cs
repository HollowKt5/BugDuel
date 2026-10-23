using System;
using BugDuel.Domain;

/// <summary>Focused contracts for breeding policy isolation, rejection and exception propagation.</summary>
public static class BreedingRegressionValidation
{
    static readonly string[] ChildIds = { "child-a", "child-b", "child-c" };

    public static string Run()
    {
        RuleSnapshots();
        BusinessRejection();
        UnexpectedExceptions();
        InvalidRandomValues();
        return "PASS: breeding reuses isolated rule snapshots; business rejection and invalid random values are atomic; implementation exceptions retain their identity.";
    }

    static void RuleSnapshots()
    {
        var family = new Family();
        var mutator = new MutatingRule();
        var observer = new ObservingRule(mutator, family.Mother, family.Father);
        var result = new BreedingService(new IBreedingRule[] { mutator, observer })
            .Breed(family.Species, family.Mother, family.Father, ChildIds, new FixedRandom(.5));
        Require(result.Success && result.Offspring.Count == ChildIds.Length && observer.Called,
            "Valid breeding with multiple rules failed.");
        Require(family.Mother.StageAgeDays == 0 && family.Father.StageAgeDays == 0
            && !family.Mother.IsOverturned && !family.Father.IsOverturned,
            "Policy snapshot mutation leaked into committed parents.");
        Require(family.Mother.BreedingCooldownDays == family.Species.Breeding.CooldownDays
            && family.Mother.Condition.Nutrition == 90, "Successful breeding did not commit its costs.");
    }

    static void BusinessRejection()
    {
        var family = new Family();
        var result = new BreedingService(new[] { new RejectingRule() })
            .Breed(family.Species, family.Mother, family.Father, ChildIds, new FixedRandom(.5));
        Require(!result.Success && result.ErrorCode == "CustomRuleRejected" && result.ErrorMessage == "Policy not satisfied"
            && result.Offspring.Count == 0 && result.Events.Count == 0, "Expected policy rejection was not preserved.");
        family.RequireUnchanged();
    }

    static void UnexpectedExceptions()
    {
        var family = new Family();
        var ruleError = new NullReferenceException("Regression rule failure");
        RequireSameException(() => new BreedingService(new[] { new ThrowingRule(ruleError) })
            .Breed(family.Species, family.Mother, family.Father, ChildIds, new FixedRandom(.5)), ruleError);
        family.RequireUnchanged();

        var randomError = new IndexOutOfRangeException("Regression random source failure");
        // Fail after a child has been constructed to exercise the generation transaction boundary.
        RequireSameException(() => new BreedingService().Breed(family.Species, family.Mother, family.Father,
            ChildIds, new FailingRandom(randomError)), randomError);
        family.RequireUnchanged();
    }

    static void InvalidRandomValues()
    {
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -.01, 1 })
        {
            var family = new Family();
            var result = new BreedingService().Breed(family.Species, family.Mother, family.Father,
                ChildIds, new InvalidLateRandom(invalid));
            Require(!result.Success && result.ErrorCode == "InvalidRandomSource" && result.Offspring.Count == 0
                && result.Events.Count == 0, "An invalid random value returned a partial brood or the wrong error.");
            family.RequireUnchanged();
        }
    }

    static void RequireSameException(Action action, Exception expected)
    {
        try { action(); }
        catch (Exception actual)
        {
            Require(ReferenceEquals(actual, expected), "Unexpected exception was replaced or misclassified.");
            return;
        }
        throw new Exception("Unexpected implementation exception was swallowed.");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    sealed class Family
    {
        public readonly SpeciesDefinition Species = new SpeciesDefinition("regression.breeding", "Test beetle", 55, 8);
        public readonly BeetleIndividual Mother;
        public readonly BeetleIndividual Father;
        public Family()
        {
            Mother = BeetleIndividual.Create(Species, "mother", "Mother", BeetleSex.Female);
            Father = BeetleIndividual.Create(Species, "father", "Father", BeetleSex.Male);
        }
        public void RequireUnchanged()
        {
            foreach (var parent in new[] { Mother, Father })
                Require(parent.StageAgeDays == 0 && parent.BreedingCooldownDays == 0 && parent.Condition.Nutrition == 100
                    && parent.Condition.Stress == 0 && !parent.IsOverturned, "Rejected/failed breeding mutated a parent.");
        }
    }

    sealed class MutatingRule : IBreedingRule
    {
        public BeetleIndividual Mother;
        public BeetleIndividual Father;
        public string RejectionReason(SpeciesDefinition species, BeetleIndividual mother, BeetleIndividual father)
        {
            Mother = mother;
            Father = father;
            var husbandry = new HusbandryService();
            husbandry.Advance(mother, species, new HabitatConditions(), .1);
            husbandry.Advance(father, species, new HabitatConditions(), .1);
            husbandry.MarkOverturned(mother, species);
            husbandry.MarkOverturned(father, species);
            return null;
        }
    }

    sealed class ObservingRule : IBreedingRule
    {
        readonly MutatingRule previous;
        readonly BeetleIndividual originalMother;
        readonly BeetleIndividual originalFather;
        public bool Called;
        public ObservingRule(MutatingRule previous, BeetleIndividual mother, BeetleIndividual father)
        { this.previous = previous; originalMother = mother; originalFather = father; }
        public string RejectionReason(SpeciesDefinition species, BeetleIndividual mother, BeetleIndividual father)
        {
            Called = true;
            Require(ReferenceEquals(mother, previous.Mother) && ReferenceEquals(father, previous.Father),
                "Each rule still allocates its own parent snapshots.");
            Require(!ReferenceEquals(mother, originalMother) && !ReferenceEquals(father, originalFather),
                "Rule received original parent objects.");
            Require(mother.StageAgeDays == 0 && father.StageAgeDays == 0 && !mother.IsOverturned && !father.IsOverturned
                && mother.Condition.Nutrition == 100 && father.Condition.Nutrition == 100,
                "Previous rule polluted the next rule's input.");
            return null;
        }
    }

    sealed class RejectingRule : IBreedingRule
    {
        public string RejectionReason(SpeciesDefinition species, BeetleIndividual mother, BeetleIndividual father)
            => "Policy not satisfied";
    }

    sealed class ThrowingRule : IBreedingRule
    {
        readonly Exception error;
        public ThrowingRule(Exception error) { this.error = error; }
        public string RejectionReason(SpeciesDefinition species, BeetleIndividual mother, BeetleIndividual father) => throw error;
    }

    sealed class FixedRandom : IRandomSource
    {
        readonly double value;
        public FixedRandom(double value) { this.value = value; }
        public double NextUnit() => value;
    }

    sealed class FailingRandom : IRandomSource
    {
        readonly Exception error;
        int count;
        public FailingRandom(Exception error) { this.error = error; }
        public double NextUnit() => ++count == 7 ? throw error : .5;
    }

    sealed class InvalidLateRandom : IRandomSource
    {
        readonly double invalid;
        int count;
        public InvalidLateRandom(double invalid) { this.invalid = invalid; }
        public double NextUnit() => ++count == 7 ? invalid : .5;
    }
}
