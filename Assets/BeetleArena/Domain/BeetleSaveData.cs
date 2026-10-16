using System;
using System.Collections.Generic;

namespace BugDuel.Domain
{
    /// <summary>Serializer-friendly transport data, never the live domain model. File IO belongs to the application layer.</summary>
    [Serializable]
    public sealed class BeetleSaveData
    {
        public int Version = BeetleSaveMapper.CurrentVersion;
        public string Id;
        public string SpeciesId;
        public string DisplayName;
        public int Sex;
        public int Stage;
        public float Strength;
        public float Defense;
        public float Speed;
        public float Stamina;
        public double Health;
        public double Nutrition;
        public double Hydration;
        public double Stress;
        public double TotalAgeDays;
        public double StageAgeDays;
        public double BreedingCooldownDays;
        // Version 1 additions: omitted fields in earlier saves mean an upright beetle with no pending rescue.
        public bool IsOverturned;
        public double OverturnedDays;
        public string MotherId;
        public string FatherId;
        public string[] AncestorIds = Array.Empty<string>();
    }

    public sealed class RestoreResult : DomainResult
    {
        public BeetleIndividual Individual { get; }
        internal RestoreResult(BeetleIndividual individual = null, string code = null, string message = null)
            : base(code, message) { Individual = individual; }
    }

    public static class BeetleSaveMapper
    {
        public const int CurrentVersion = 1;
        public static BeetleSaveData Export(BeetleIndividual individual)
        {
            if (individual == null) throw new ArgumentNullException(nameof(individual));
            return new BeetleSaveData
            {
                Id = individual.Id, SpeciesId = individual.SpeciesId, DisplayName = individual.DisplayName,
                Sex = (int)individual.Sex, Stage = (int)individual.Stage,
                Strength = individual.Traits.Strength, Defense = individual.Traits.Defense,
                Speed = individual.Traits.Speed, Stamina = individual.Traits.Stamina,
                Health = individual.Condition.Health, Nutrition = individual.Condition.Nutrition,
                Hydration = individual.Condition.Hydration, Stress = individual.Condition.Stress,
                TotalAgeDays = individual.TotalAgeDays, StageAgeDays = individual.StageAgeDays,
                BreedingCooldownDays = individual.BreedingCooldownDays,
                IsOverturned = individual.IsOverturned, OverturnedDays = individual.OverturnedDays,
                MotherId = individual.Pedigree.MotherId, FatherId = individual.Pedigree.FatherId,
                AncestorIds = new List<string>(individual.Pedigree.AncestorIds).ToArray()
            };
        }

        public static RestoreResult Restore(BeetleSaveData data, SpeciesCatalog catalog)
        {
            if (data == null || catalog == null) return new RestoreResult(code: "MissingSaveOrCatalog", message: "Save data and species catalog are required.");
            if (data.Version != CurrentVersion) return new RestoreResult(code: "UnsupportedSaveVersion", message: "Migrate the save explicitly before restoring it.");
            if (!catalog.TryGet(data.SpeciesId, out SpeciesDefinition species))
                return new RestoreResult(code: "UnknownSpecies", message: "The saved species ID is not present in the catalog.");
            try
            {
                if (data.AncestorIds == null) throw new ArgumentException("An ancestry array is required, even when empty.");
                var ancestors = new HashSet<string>(StringComparer.Ordinal);
                foreach (string ancestor in data.AncestorIds)
                    if (!ancestors.Add(DomainGuard.Text(ancestor, nameof(data.AncestorIds))))
                        throw new ArgumentException("Ancestor IDs must be unique.");
                var individual = new BeetleIndividual(data.Id, data.SpeciesId, data.DisplayName,
                    (BeetleSex)data.Sex, (LifeStage)data.Stage,
                    new BeetleTraits(data.Strength, data.Defense, data.Speed, data.Stamina),
                    new BeetleCondition(data.Health, data.Nutrition, data.Hydration, data.Stress),
                    new BeetlePedigree(data.MotherId, data.FatherId, data.AncestorIds),
                    data.TotalAgeDays, data.StageAgeDays, data.BreedingCooldownDays, data.IsOverturned, data.OverturnedDays);
                DomainResult invalid = HusbandryService.Check(individual, species);
                if (invalid != null) return new RestoreResult(code: invalid.ErrorCode, message: invalid.ErrorMessage);
                if (individual.TotalAgeDays > species.Lifecycle.StartAge(LifeStage.Dead) + 0.000001 ||
                    individual.StageAgeDays > Math.Max(Math.Max(species.Lifecycle.EggDays, species.Lifecycle.LarvaDays),
                        Math.Max(species.Lifecycle.PupaDays, species.Lifecycle.AdultDays)) + 0.000001 ||
                    individual.BreedingCooldownDays > species.Breeding.CooldownDays)
                    throw new ArgumentException("Saved age or breeding cooldown is incompatible with this species definition.");
                return new RestoreResult(individual);
            }
            catch (ArgumentException exception) { return new RestoreResult(code: "InvalidSave", message: exception.Message); }
        }
    }
}
