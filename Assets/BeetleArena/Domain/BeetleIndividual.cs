using System;
using System.Collections.Generic;

namespace BugDuel.Domain
{
    public sealed class BeetleTraits
    {
        public float Strength { get; }
        public float Defense { get; }
        public float Speed { get; }
        public float Stamina { get; }
        public BeetleTraits(float strength = 0.5f, float defense = 0.5f, float speed = 0.5f, float stamina = 0.5f)
        {
            Strength = (float)DomainGuard.Range(strength, 0, 1, nameof(strength));
            Defense = (float)DomainGuard.Range(defense, 0, 1, nameof(defense));
            Speed = (float)DomainGuard.Range(speed, 0, 1, nameof(speed));
            Stamina = (float)DomainGuard.Range(stamina, 0, 1, nameof(stamina));
        }
    }

    /// <summary>Saturating 0–100 meters. Callers supply raw deltas; non-finite values remain programming errors.</summary>
    public sealed class BeetleCondition
    {
        public double Health { get; }
        public double Nutrition { get; }
        public double Hydration { get; }
        public double Stress { get; }
        public BeetleCondition(double health = 100, double nutrition = 100, double hydration = 100, double stress = 0)
        {
            Health = Percentage(health, nameof(health));
            Nutrition = Percentage(nutrition, nameof(nutrition));
            Hydration = Percentage(hydration, nameof(hydration));
            Stress = Percentage(stress, nameof(stress));
        }

        static double Percentage(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name, "Condition values must be finite.");
            return Math.Max(0, Math.Min(100, value));
        }
    }

    public sealed class BeetlePedigree
    {
        public string MotherId { get; }
        public string FatherId { get; }
        public IReadOnlyList<string> AncestorIds { get; }
        internal BeetlePedigree(string motherId = null, string fatherId = null, IEnumerable<string> ancestors = null)
        {
            if ((motherId == null) != (fatherId == null) || motherId != null && motherId == fatherId)
                throw new ArgumentException("Both distinct parent IDs, or neither, are required.");
            MotherId = motherId == null ? null : DomainGuard.Text(motherId, nameof(motherId));
            FatherId = fatherId == null ? null : DomainGuard.Text(fatherId, nameof(fatherId));
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            if (ancestors != null)
                foreach (string id in ancestors) ids.Add(DomainGuard.Text(id, nameof(ancestors)));
            if (motherId != null) { ids.Add(motherId); ids.Add(fatherId); }
            if (motherId == null && ids.Count != 0) throw new ArgumentException("Ancestry requires parent IDs.");
            AncestorIds = Array.AsReadOnly(new List<string>(ids).ToArray());
        }
    }

    /// <summary>Persistent identity. Mutations occur only through domain services; value objects are immutable.</summary>
    public sealed class BeetleIndividual
    {
        public string Id { get; }
        public string SpeciesId { get; }
        public string DisplayName { get; }
        public BeetleSex Sex { get; }
        public LifeStage Stage { get; internal set; }
        public BeetleTraits Traits { get; }
        public BeetleCondition Condition { get; internal set; }
        public double TotalAgeDays { get; internal set; }
        public double StageAgeDays { get; internal set; }
        public double BreedingCooldownDays { get; internal set; }
        public bool IsOverturned { get; internal set; }
        public double OverturnedDays { get; internal set; }
        public bool IsStruggling => IsOverturned && Stage == LifeStage.Adult;
        public BeetlePedigree Pedigree { get; }

        internal BeetleIndividual(string id, string speciesId, string displayName, BeetleSex sex, LifeStage stage,
            BeetleTraits traits, BeetleCondition condition, BeetlePedigree pedigree, double totalAgeDays,
            double stageAgeDays, double breedingCooldownDays, bool isOverturned = false, double overturnedDays = 0)
        {
            Id = DomainGuard.Text(id, nameof(id));
            SpeciesId = DomainGuard.Text(speciesId, nameof(speciesId));
            DisplayName = DomainGuard.DisplayText(displayName, nameof(displayName));
            if (!Enum.IsDefined(typeof(BeetleSex), sex) || !Enum.IsDefined(typeof(LifeStage), stage))
                throw new ArgumentOutOfRangeException(nameof(stage));
            Sex = sex;
            Stage = stage;
            Traits = traits ?? throw new ArgumentNullException(nameof(traits));
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            Pedigree = pedigree ?? throw new ArgumentNullException(nameof(pedigree));
            foreach (string ancestor in pedigree.AncestorIds)
                if (ancestor == id) throw new ArgumentException("An individual cannot be its own ancestor.");
            TotalAgeDays = DomainGuard.Range(totalAgeDays, 0, 1000000, nameof(totalAgeDays));
            StageAgeDays = DomainGuard.Range(stageAgeDays, 0, totalAgeDays, nameof(stageAgeDays));
            BreedingCooldownDays = DomainGuard.Range(breedingCooldownDays, 0, 100000, nameof(breedingCooldownDays));
            if ((stage == LifeStage.Dead) != (condition.Health == 0))
                throw new ArgumentException("Death stage and zero health must agree.");
            OverturnedDays = Math.Min(StageAgeDays, DomainGuard.Range(overturnedDays, 0, double.MaxValue, nameof(overturnedDays)));
            if ((!isOverturned && overturnedDays != 0) || (isOverturned && stage != LifeStage.Adult && stage != LifeStage.Dead))
                throw new ArgumentException("Only adult or dead beetles can retain an overturned pose; upright beetles have no active overturn timer.");
            IsOverturned = isOverturned;
        }

        public static BeetleIndividual Create(SpeciesDefinition species, string id, string displayName,
            BeetleSex sex, LifeStage stage = LifeStage.Adult, BeetleTraits traits = null)
        {
            if (species == null) throw new ArgumentNullException(nameof(species));
            if (stage == LifeStage.Dead) throw new ArgumentException("Create a living individual, or restore a saved record.", nameof(stage));
            return new BeetleIndividual(id, species.Id, displayName, sex, stage, traits ?? new BeetleTraits(),
                new BeetleCondition(), new BeetlePedigree(), species.Lifecycle.StartAge(stage), 0, 0);
        }

        public BeetleIndividual Clone() => new BeetleIndividual(Id, SpeciesId, DisplayName, Sex, Stage,
            Traits, Condition, Pedigree, TotalAgeDays, StageAgeDays, BreedingCooldownDays, IsOverturned, OverturnedDays);
    }

    public enum HusbandryEventKind { Fed, StageChanged, Died, BreedingCompleted, Born, ContestApplied, Overturned, Righted }
    public sealed class HusbandryEvent
    {
        public HusbandryEventKind Kind { get; }
        public string IndividualId { get; }
        public LifeStage Stage { get; }
        public double AgeDays { get; }
        public string Detail { get; }
        internal HusbandryEvent(HusbandryEventKind kind, BeetleIndividual individual, string detail = null)
        { Kind = kind; IndividualId = individual.Id; Stage = individual.Stage; AgeDays = individual.TotalAgeDays; Detail = detail; }
    }

    public class DomainResult
    {
        public bool Success => ErrorCode == null;
        public string ErrorCode { get; }
        public string ErrorMessage { get; }
        public IReadOnlyList<HusbandryEvent> Events { get; }
        internal DomainResult(string code = null, string message = null, IEnumerable<HusbandryEvent> events = null)
        { ErrorCode = code; ErrorMessage = message; Events = new List<HusbandryEvent>(events ?? Array.Empty<HusbandryEvent>()).AsReadOnly(); }
        internal static DomainResult Fail(string code, string message) => new DomainResult(code, message);
        public static DomainResult Rejected(string code, string message) =>
            new DomainResult(DomainGuard.Text(code, nameof(code)), DomainGuard.Text(message, nameof(message)));
        public static DomainResult Accepted() => new DomainResult();
    }
}
