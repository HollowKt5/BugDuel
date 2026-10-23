using System;
using System.Collections.Generic;

namespace BugDuel.Domain
{
    public enum BeetleSex { Unknown, Female, Male }
    public enum LifeStage { Egg, Larva, Pupa, Adult, Dead }
    [Flags]
    public enum FoodKind { None = 0, Fruit = 1, DecayingWood = 2, LeafLitter = 4, Protein = 8, BeetleJelly = 16, SweetPotato = 32 }

    internal static class DomainGuard
    {
        internal static string Id(string value, string name)
        {
            Text(value, name);
            if (value.Length > 128) throw new ArgumentException("Stable IDs are limited to 128 characters.", name);
            foreach (char character in value)
                if (!(character >= 'a' && character <= 'z' || character >= 'A' && character <= 'Z' ||
                    character >= '0' && character <= '9' || character == '-' || character == '_' || character == '.' || character == ':'))
                    throw new ArgumentException("Stable IDs use ASCII letters, digits, hyphen, underscore, dot or colon.", name);
            return value;
        }
        internal static string Text(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
                throw new ArgumentException("A non-empty, trimmed value is required.", name);
            return value;
        }

        // Display text can be cleaned; identities and foreign keys must retain exact spelling.
        internal static string DisplayText(string value, string name) => Text(value?.Trim(), name);

        internal static double Range(double value, double minimum, double maximum, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < minimum || value > maximum)
                throw new ArgumentOutOfRangeException(name, "Value must be finite and within the declared range.");
            return value;
        }

        internal static float Clamp(double value, double maximum = 100) => (float)Math.Max(0, Math.Min(maximum, value));
        internal static void Foods(FoodKind value, string name)
        {
            if (value == FoodKind.None || (value & ~(FoodKind.Fruit | FoodKind.DecayingWood | FoodKind.LeafLitter | FoodKind.Protein | FoodKind.BeetleJelly | FoodKind.SweetPotato)) != 0)
                throw new ArgumentOutOfRangeException(name);
        }
    }

    /// <summary>Designer-authored game tuning, not real-world animal-care guidance.</summary>
    public sealed class CareProfile
    {
        public float MinimumTemperature { get; }
        public float MaximumTemperature { get; }
        public float MinimumHumidity { get; }
        public float MaximumHumidity { get; }
        public float NutritionLossPerDay { get; }
        public float HydrationLossPerDay { get; }
        public float RecoveryPerDay { get; }
        public float StressChangePerDay { get; }
        public float HealthLossPerDay { get; }
        public FoodKind LarvalFoods { get; }
        public FoodKind AdultFoods { get; }
        public double OverturnedGraceDays { get; }
        public float OverturnedHealthLossPerDay { get; }

        public CareProfile(float minimumTemperature = 20, float maximumTemperature = 28,
            float minimumHumidity = 60, float maximumHumidity = 80, float nutritionLossPerDay = 3,
            float hydrationLossPerDay = 4, float recoveryPerDay = 4, float stressChangePerDay = 8,
            float healthLossPerDay = 8, FoodKind larvalFoods = FoodKind.DecayingWood | FoodKind.LeafLitter,
            FoodKind adultFoods = FoodKind.Fruit | FoodKind.BeetleJelly | FoodKind.SweetPotato,
            double overturnedGraceDays = 0.05, float overturnedHealthLossPerDay = 500)
        {
            MinimumTemperature = (float)DomainGuard.Range(minimumTemperature, -100, 100, nameof(minimumTemperature));
            MaximumTemperature = (float)DomainGuard.Range(maximumTemperature, minimumTemperature, 100, nameof(maximumTemperature));
            MinimumHumidity = (float)DomainGuard.Range(minimumHumidity, 0, 100, nameof(minimumHumidity));
            MaximumHumidity = (float)DomainGuard.Range(maximumHumidity, minimumHumidity, 100, nameof(maximumHumidity));
            NutritionLossPerDay = (float)DomainGuard.Range(nutritionLossPerDay, 0, 100, nameof(nutritionLossPerDay));
            HydrationLossPerDay = (float)DomainGuard.Range(hydrationLossPerDay, 0, 100, nameof(hydrationLossPerDay));
            RecoveryPerDay = (float)DomainGuard.Range(recoveryPerDay, 0, 100, nameof(recoveryPerDay));
            StressChangePerDay = (float)DomainGuard.Range(stressChangePerDay, 0, 100, nameof(stressChangePerDay));
            HealthLossPerDay = (float)DomainGuard.Range(healthLossPerDay, 0, 100, nameof(healthLossPerDay));
            DomainGuard.Foods(larvalFoods, nameof(larvalFoods));
            DomainGuard.Foods(adultFoods, nameof(adultFoods));
            LarvalFoods = larvalFoods;
            AdultFoods = adultFoods;
            OverturnedGraceDays = DomainGuard.Range(overturnedGraceDays, 0, 100000, nameof(overturnedGraceDays));
            OverturnedHealthLossPerDay = (float)DomainGuard.Range(overturnedHealthLossPerDay, 0, 100000, nameof(overturnedHealthLossPerDay));
        }
    }

    public sealed class LifecycleProfile
    {
        public double EggDays { get; }
        public double LarvaDays { get; }
        public double PupaDays { get; }
        public double AdultDays { get; }

        public LifecycleProfile(double eggDays = 3, double larvaDays = 30, double pupaDays = 10, double adultDays = 90)
        {
            EggDays = DomainGuard.Range(eggDays, 0.001, 100000, nameof(eggDays));
            LarvaDays = DomainGuard.Range(larvaDays, 0.001, 100000, nameof(larvaDays));
            PupaDays = DomainGuard.Range(pupaDays, 0.001, 100000, nameof(pupaDays));
            AdultDays = DomainGuard.Range(adultDays, 0.001, 100000, nameof(adultDays));
        }

        public double Duration(LifeStage stage)
        {
            switch (stage)
            {
                case LifeStage.Egg: return EggDays;
                case LifeStage.Larva: return LarvaDays;
                case LifeStage.Pupa: return PupaDays;
                case LifeStage.Adult: return AdultDays;
                case LifeStage.Dead: return 0;
                default: throw new ArgumentOutOfRangeException(nameof(stage));
            }
        }

        public double StartAge(LifeStage stage)
        {
            if (!Enum.IsDefined(typeof(LifeStage), stage)) throw new ArgumentOutOfRangeException(nameof(stage));
            double age = 0;
            for (int index = 0; index < (int)stage; index++) age += Duration((LifeStage)index);
            return age;
        }
    }

    public sealed class BreedingProfile
    {
        public int ClutchSize { get; }
        public double CooldownDays { get; }
        public float MinimumHealth { get; }
        public float MinimumNutrition { get; }
        public float MaximumStress { get; }
        public float TraitVariation { get; }

        public BreedingProfile(int clutchSize = 3, double cooldownDays = 7, float minimumHealth = 70,
            float minimumNutrition = 60, float maximumStress = 40, float traitVariation = 0.1f)
        {
            ClutchSize = (int)DomainGuard.Range(clutchSize, 1, 100, nameof(clutchSize));
            CooldownDays = DomainGuard.Range(cooldownDays, 0, 100000, nameof(cooldownDays));
            MinimumHealth = (float)DomainGuard.Range(minimumHealth, 0, 100, nameof(minimumHealth));
            MinimumNutrition = (float)DomainGuard.Range(minimumNutrition, 0, 100, nameof(minimumNutrition));
            MaximumStress = (float)DomainGuard.Range(maximumStress, 0, 100, nameof(maximumStress));
            TraitVariation = (float)DomainGuard.Range(traitVariation, 0, 1, nameof(traitVariation));
        }
    }

    public sealed class SpeciesDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public float AdultLengthMm { get; }
        public float AdultMassGrams { get; }
        public bool CombatEnabled { get; }
        public CareProfile Care { get; }
        public LifecycleProfile Lifecycle { get; }
        public BreedingProfile Breeding { get; }

        public SpeciesDefinition(string id, string displayName, float adultLengthMm, float adultMassGrams,
            bool combatEnabled = true, CareProfile care = null, LifecycleProfile lifecycle = null, BreedingProfile breeding = null)
        {
            Id = DomainGuard.Id(id, nameof(id));
            DisplayName = DomainGuard.DisplayText(displayName, nameof(displayName));
            AdultLengthMm = (float)DomainGuard.Range(adultLengthMm, 0.01, 10000, nameof(adultLengthMm));
            AdultMassGrams = (float)DomainGuard.Range(adultMassGrams, 0.001, 10000, nameof(adultMassGrams));
            CombatEnabled = combatEnabled;
            Care = care ?? new CareProfile();
            Lifecycle = lifecycle ?? new LifecycleProfile();
            Breeding = breeding ?? new BreedingProfile();
        }

    }

    public sealed class SpeciesCatalog
    {
        private readonly Dictionary<string, SpeciesDefinition> definitions = new Dictionary<string, SpeciesDefinition>(StringComparer.Ordinal);
        public IReadOnlyList<SpeciesDefinition> Definitions { get; }
        public SpeciesCatalog(IEnumerable<SpeciesDefinition> species)
        {
            if (species == null) throw new ArgumentNullException(nameof(species));
            foreach (SpeciesDefinition definition in species)
            {
                if (definition == null || definitions.ContainsKey(definition.Id))
                    throw new ArgumentException("Species definitions must be non-null and have unique IDs.", nameof(species));
                definitions.Add(definition.Id, definition);
            }
            var values = new List<SpeciesDefinition>(definitions.Values);
            values.Sort((first, second) => StringComparer.Ordinal.Compare(first.Id, second.Id));
            Definitions = values.AsReadOnly();
        }
        public bool TryGet(string id, out SpeciesDefinition species)
        {
            species = null;
            return id != null && definitions.TryGetValue(id, out species);
        }
    }
}
