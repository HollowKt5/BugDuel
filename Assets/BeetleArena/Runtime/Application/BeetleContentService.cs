using System;
using System.Collections.Generic;
using System.IO;
using BugDuel.Domain;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>Full data packages only. No downloads, type activation, scripts, prefab or rig replacement.</summary>
    public sealed class BeetleContentService
    {
        readonly BeetleRosterSession session;
        public ContentCatalog Current => session.Content;
        public BeetleContentService(BeetleRosterSession session)
        { this.session = session ?? throw new ArgumentNullException(nameof(session)); }

        public DomainResult TryApplyJson(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return DomainResult.Rejected("InvalidContentJson", "A non-empty JSON data package is required.");
            try
            {
                // A missing schema marker must not inherit the DTO constructor's authoring default.
                var data = new BeetleContentData { SchemaVersion = 0 };
                JsonUtility.FromJsonOverwrite(payload, data);
                return session.TryUpdateContent(data.ToCatalog());
            }
            catch (ArgumentException exception) { return DomainResult.Rejected("InvalidContentJson", exception.Message); }
        }

        public DomainResult TryReloadFile(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath) || !Path.IsPathFullyQualified(absolutePath))
                return DomainResult.Rejected("InvalidContentPath", "An explicit absolute JSON file path is required.");
            try { return TryApplyJson(File.ReadAllText(absolutePath)); }
            catch (IOException exception) { return DomainResult.Rejected("ContentReadFailed", exception.Message); }
            catch (UnauthorizedAccessException exception) { return DomainResult.Rejected("ContentReadFailed", exception.Message); }
            catch (ArgumentException exception) { return DomainResult.Rejected("InvalidContentPath", exception.Message); }
        }

        public string ExportJson(bool prettyPrint = true) => JsonUtility.ToJson(BeetleContentData.FromCatalog(Current), prettyPrint);
    }

    [Serializable]
    public sealed class BeetleContentData
    {
        public const int CurrentSchemaVersion = 2;
        public int SchemaVersion = CurrentSchemaVersion;
        public int Version;
        public BeetleSpeciesContentData[] Species;
        public BeetleHabitatContentData[] Habitats;

        public ContentCatalog ToCatalog()
        {
            if ((SchemaVersion != 1 && SchemaVersion != CurrentSchemaVersion) || Species == null || Habitats == null)
                throw new ArgumentException("Supported schema and complete species/habitat arrays are required.");
            var species = new List<SpeciesDefinition>();
            foreach (BeetleSpeciesContentData data in Species)
            {
                if (data == null) throw new ArgumentException("Species entries cannot be null.");
                species.Add(data.ToDefinition(SchemaVersion));
            }
            var habitats = new List<HabitatDefinition>();
            foreach (BeetleHabitatContentData data in Habitats)
            {
                if (data == null) throw new ArgumentException("Habitat entries cannot be null.");
                habitats.Add(data.ToDefinition());
            }
            return new ContentCatalog(Version, species, habitats);
        }

        public static BeetleContentData FromCatalog(ContentCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            var species = new List<BeetleSpeciesContentData>();
            foreach (SpeciesDefinition definition in catalog.Species.Definitions) species.Add(BeetleSpeciesContentData.FromDefinition(definition));
            var habitats = new List<BeetleHabitatContentData>();
            foreach (HabitatDefinition definition in catalog.Habitats) habitats.Add(BeetleHabitatContentData.FromDefinition(definition));
            return new BeetleContentData { Version = catalog.Version, Species = species.ToArray(), Habitats = habitats.ToArray() };
        }
    }

    [Serializable]
    public sealed class BeetleSpeciesContentData
    {
        public string Id, DisplayName;
        public float AdultLengthMm, AdultMassGrams;
        public bool CombatEnabled;
        public double EggDays, LarvaDays, PupaDays, AdultDays;
        public float MinimumTemperature, MaximumTemperature, MinimumHumidity, MaximumHumidity;
        public float NutritionLossPerDay, HydrationLossPerDay, RecoveryPerDay, StressChangePerDay, HealthLossPerDay;
        public int LarvalFoods, AdultFoods, ClutchSize;
        public double CooldownDays;
        public float MinimumHealth, MinimumNutrition, MaximumStress, TraitVariation;
        public double OverturnedGraceDays;
        public float OverturnedHealthLossPerDay;

        public SpeciesDefinition ToDefinition(int schemaVersion)
        {
            if (schemaVersion != 1 && schemaVersion != BeetleContentData.CurrentSchemaVersion)
                throw new ArgumentException("Unsupported species content schema.", nameof(schemaVersion));
            // Schema 1 predates rescue tuning: explicitly migrate missing values instead of treating them as zero.
            double grace = schemaVersion == 1 ? 0.05 : OverturnedGraceDays;
            float loss = schemaVersion == 1 ? 500 : OverturnedHealthLossPerDay;
            FoodKind diet = (FoodKind)AdultFoods;
            if (schemaVersion == 1 && diet == (FoodKind.Fruit | FoodKind.BeetleJelly)) diet |= FoodKind.SweetPotato;
            return new SpeciesDefinition(Id, DisplayName, AdultLengthMm, AdultMassGrams, CombatEnabled,
                new CareProfile(MinimumTemperature, MaximumTemperature, MinimumHumidity, MaximumHumidity, NutritionLossPerDay,
                    HydrationLossPerDay, RecoveryPerDay, StressChangePerDay, HealthLossPerDay, (FoodKind)LarvalFoods, diet, grace, loss),
                new LifecycleProfile(EggDays, LarvaDays, PupaDays, AdultDays),
                new BreedingProfile(ClutchSize, CooldownDays, MinimumHealth, MinimumNutrition, MaximumStress, TraitVariation));
        }

        public static BeetleSpeciesContentData FromDefinition(SpeciesDefinition species) => new BeetleSpeciesContentData
        {
            Id = species.Id, DisplayName = species.DisplayName, AdultLengthMm = species.AdultLengthMm, AdultMassGrams = species.AdultMassGrams,
            CombatEnabled = species.CombatEnabled, EggDays = species.Lifecycle.EggDays, LarvaDays = species.Lifecycle.LarvaDays,
            PupaDays = species.Lifecycle.PupaDays, AdultDays = species.Lifecycle.AdultDays,
            MinimumTemperature = species.Care.MinimumTemperature, MaximumTemperature = species.Care.MaximumTemperature,
            MinimumHumidity = species.Care.MinimumHumidity, MaximumHumidity = species.Care.MaximumHumidity,
            NutritionLossPerDay = species.Care.NutritionLossPerDay, HydrationLossPerDay = species.Care.HydrationLossPerDay,
            RecoveryPerDay = species.Care.RecoveryPerDay, StressChangePerDay = species.Care.StressChangePerDay,
            HealthLossPerDay = species.Care.HealthLossPerDay, LarvalFoods = (int)species.Care.LarvalFoods, AdultFoods = (int)species.Care.AdultFoods,
            ClutchSize = species.Breeding.ClutchSize, CooldownDays = species.Breeding.CooldownDays, MinimumHealth = species.Breeding.MinimumHealth,
            MinimumNutrition = species.Breeding.MinimumNutrition, MaximumStress = species.Breeding.MaximumStress, TraitVariation = species.Breeding.TraitVariation,
            OverturnedGraceDays = species.Care.OverturnedGraceDays, OverturnedHealthLossPerDay = species.Care.OverturnedHealthLossPerDay
        };
    }

    [Serializable]
    public sealed class BeetleHabitatContentData
    {
        public string Id, DisplayName;
        public float TemperatureCelsius, HumidityPercent, CleanlinessPercent, Crowding;
        public bool Resting;
        public int[] AllowedStages;
        public string[] Tags;

        public HabitatDefinition ToDefinition()
        {
            if (AllowedStages == null || Tags == null) throw new ArgumentException("Habitat stage and tag arrays are required.");
            var stages = new List<LifeStage>();
            foreach (int stage in AllowedStages) stages.Add((LifeStage)stage);
            return new HabitatDefinition(Id, DisplayName, new HabitatConditions(TemperatureCelsius, HumidityPercent, CleanlinessPercent, Crowding, Resting), stages, Tags);
        }

        public static BeetleHabitatContentData FromDefinition(HabitatDefinition habitat)
        {
            var stages = new List<int>();
            foreach (LifeStage stage in habitat.AllowedStages) stages.Add((int)stage);
            return new BeetleHabitatContentData
            {
                Id = habitat.Id, DisplayName = habitat.DisplayName, TemperatureCelsius = habitat.Environment.TemperatureCelsius,
                HumidityPercent = habitat.Environment.HumidityPercent, CleanlinessPercent = habitat.Environment.CleanlinessPercent,
                Crowding = habitat.Environment.Crowding, Resting = habitat.Environment.Resting,
                AllowedStages = stages.ToArray(), Tags = new List<string>(habitat.Tags).ToArray()
            };
        }
    }
}
