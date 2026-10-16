using System;
using System.Collections.Generic;

namespace BugDuel.Domain
{
    public interface IRandomSource { double NextUnit(); }

    /// <summary>Optional additional eligibility policy. Inputs are isolated snapshots. Null means eligible.</summary>
    public interface IBreedingRule
    {
        string RejectionReason(SpeciesDefinition species, BeetleIndividual mother, BeetleIndividual father);
    }

    public sealed class BreedingResult : DomainResult
    {
        public IReadOnlyList<BeetleIndividual> Offspring { get; }
        internal BreedingResult(string code = null, string message = null, IEnumerable<BeetleIndividual> offspring = null,
            IEnumerable<HusbandryEvent> events = null) : base(code, message, events)
        { Offspring = new List<BeetleIndividual>(offspring ?? Array.Empty<BeetleIndividual>()).AsReadOnly(); }
        public static new BreedingResult Rejected(string code, string message) =>
            new BreedingResult(DomainGuard.Text(code, nameof(code)), DomainGuard.Text(message, nameof(message)));
    }

    public sealed class BreedingService
    {
        private readonly IBreedingRule[] additionalRules;
        public BreedingService(IEnumerable<IBreedingRule> additionalRules = null)
        {
            this.additionalRules = new List<IBreedingRule>(additionalRules ?? Array.Empty<IBreedingRule>()).ToArray();
            foreach (IBreedingRule rule in this.additionalRules)
                if (rule == null) throw new ArgumentException("Breeding rules cannot contain null.", nameof(additionalRules));
        }

        /// <summary>The application reserves globally unique child IDs before calling; this method checks the supplied family.</summary>
        public BreedingResult Breed(SpeciesDefinition species, BeetleIndividual mother, BeetleIndividual father,
            IReadOnlyList<string> offspringIds, IRandomSource random)
        {
            DomainResult invalid = HusbandryService.Check(mother, species) ?? HusbandryService.Check(father, species);
            if (invalid != null) return new BreedingResult(invalid.ErrorCode, invalid.ErrorMessage);
            if (mother.Id == father.Id || mother.Sex != BeetleSex.Female || father.Sex != BeetleSex.Male)
                return new BreedingResult("InvalidParents", "Two distinct female and male parents are required in that order.");
            if (mother.Stage != LifeStage.Adult || father.Stage != LifeStage.Adult)
                return new BreedingResult("NotAdult", "Both parents must be living adults.");
            if (mother.IsOverturned || father.IsOverturned)
                return new BreedingResult("NeedsRighting", "Help overturned parents upright before breeding.");
            BreedingProfile profile = species.Breeding;
            if (!Ready(mother, profile) || !Ready(father, profile))
                return new BreedingResult("NotReady", "Parents need sufficient health, nutrition, hydration, low stress, and no breeding cooldown.");
            var ancestors = new HashSet<string>(mother.Pedigree.AncestorIds, StringComparer.Ordinal);
            if (ancestors.Contains(father.Id)) return new BreedingResult("RelatedParents", "Ancestor and descendant cannot breed.");
            foreach (string ancestor in father.Pedigree.AncestorIds)
                if (ancestor == mother.Id || ancestors.Contains(ancestor))
                    return new BreedingResult("RelatedParents", "Parents cannot share recorded ancestry.");
            ancestors.UnionWith(father.Pedigree.AncestorIds);
            ancestors.Add(mother.Id);
            ancestors.Add(father.Id);
            if (offspringIds == null || offspringIds.Count != profile.ClutchSize)
                return new BreedingResult("InvalidOffspringIds", "Provide exactly one ID per configured offspring.");
            var reserved = new HashSet<string>(ancestors, StringComparer.Ordinal);
            var ids = new string[offspringIds.Count];
            for (int index = 0; index < ids.Length; index++)
            {
                string id = offspringIds[index];
                if (string.IsNullOrWhiteSpace(id) || id != id.Trim() || !reserved.Add(id))
                    return new BreedingResult("InvalidOffspringIds", "Child IDs must be non-empty, trimmed, unique, and distinct from their ancestry.");
                ids[index] = id;
            }
            if (random == null) return new BreedingResult("MissingRandomSource", "An explicit random source is required.");
            try
            {
                foreach (IBreedingRule rule in additionalRules)
                {
                    string rejection = rule.RejectionReason(species, mother.Clone(), father.Clone());
                    if (rejection != null) return new BreedingResult("CustomRuleRejected", rejection);
                }
            }
            catch (Exception exception) { return new BreedingResult("CustomRuleFailed", exception.Message); }
            var children = new List<BeetleIndividual>();
            var events = new List<HusbandryEvent>();
            try
            {
                foreach (string id in ids)
                {
                    var traits = new BeetleTraits(Inherit(mother.Traits.Strength, father.Traits.Strength, profile, random),
                        Inherit(mother.Traits.Defense, father.Traits.Defense, profile, random),
                        Inherit(mother.Traits.Speed, father.Traits.Speed, profile, random),
                        Inherit(mother.Traits.Stamina, father.Traits.Stamina, profile, random));
                    var child = new BeetleIndividual(id, species.Id, species.DisplayName, Unit(random) < 0.5 ? BeetleSex.Female : BeetleSex.Male,
                        LifeStage.Egg, traits, new BeetleCondition(), new BeetlePedigree(mother.Id, father.Id, ancestors), 0, 0, 0);
                    children.Add(child);
                    events.Add(new HusbandryEvent(HusbandryEventKind.Born, child));
                }
            }
            catch (Exception exception) { return new BreedingResult("InvalidRandomSource", exception.Message); }
            // Commit only after validation and generation succeed; rejected commands leave both parents unchanged.
            mother.BreedingCooldownDays = profile.CooldownDays;
            father.BreedingCooldownDays = profile.CooldownDays;
            SpendNutrition(mother);
            SpendNutrition(father);
            events.Add(new HusbandryEvent(HusbandryEventKind.BreedingCompleted, mother, father.Id));
            return new BreedingResult(offspring: children, events: events);
        }

        private static bool Ready(BeetleIndividual beetle, BreedingProfile profile) => beetle.Condition.Health >= profile.MinimumHealth &&
            beetle.Condition.Nutrition >= profile.MinimumNutrition && beetle.Condition.Hydration >= profile.MinimumNutrition &&
            beetle.Condition.Stress <= profile.MaximumStress && beetle.BreedingCooldownDays == 0;
        private static void SpendNutrition(BeetleIndividual beetle) => beetle.Condition = new BeetleCondition(beetle.Condition.Health,
            Math.Max(0, beetle.Condition.Nutrition - 10), beetle.Condition.Hydration, Math.Min(100, beetle.Condition.Stress + 10));
        private static float Inherit(float first, float second, BreedingProfile profile, IRandomSource random) =>
            DomainGuard.Clamp((first + second) * 0.5 + (Unit(random) * 2 - 1) * profile.TraitVariation, 1);
        private static double Unit(IRandomSource random)
        {
            double value = random.NextUnit();
            if (double.IsNaN(value) || value < 0 || value >= 1) throw new InvalidOperationException("Random values must be finite and in [0, 1).");
            return value;
        }
    }
}
