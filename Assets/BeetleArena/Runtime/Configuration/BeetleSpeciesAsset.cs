using BugDuel.Domain;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>Authoring container for Inspector; domain services consume immutable ToDefinition copies rather than reading shared assets directly.</summary>
    [CreateAssetMenu(menuName = "BugDuel/Species Definition", fileName = "BeetleSpecies")]
    public sealed class BeetleSpeciesAsset : ScriptableObject
    {
        [Tooltip("Save foreign key. Invariant post-release; cannot substitute display name, resource path, or array index.")]
        public string speciesId = "example-horn-beetle";
        public string displayName = "示例角斗甲虫";
        [Min(1)] public float adultLengthMm = 55, adultMassGrams = 8;
        public bool combatEnabled = true;
        [Header("Life cycle / game days, not biological measurements")]
        [Min(.01f)] public float eggDays = 3, larvaDays = 30, pupaDays = 10, adultDays = 90;
        [Header("Habitat / temperature C, relative humidity percent")]
        public Vector2 temperature = new Vector2(20, 28);
        public Vector2 humidity = new Vector2(60, 80);
        [Min(0)] public float nutritionLossPerDay = 3, hydrationLossPerDay = 4;
        [Min(0)] public float recoveryPerDay = 4, stressChangePerDay = 8, healthLossPerDay = 8;
        public FoodKind larvalFoods = FoodKind.DecayingWood | FoodKind.LeafLitter;
        public FoodKind adultFoods = FoodKind.Fruit | FoodKind.BeetleJelly | FoodKind.SweetPotato;
        [Header("Righting assistance / game days, not biological measurements")]
        [Min(0)] public double overturnedGraceDays = .05;
        [Min(0)] public float overturnedHealthLossPerDay = 500;
        [Header("Breeding / game tuning")]
        [Min(1)] public int clutchSize = 3;
        [Min(0)] public float breedingCooldownDays = 7;
        [Range(0, 100)] public float minimumHealth = 70, minimumNutrition = 60, maximumStress = 40;
        [Range(0, 1)] public float traitVariation = .1f;

        // Fail explicitly instead of silently repairing a published species/save contract.
        public SpeciesDefinition ToDefinition() => new SpeciesDefinition(speciesId, displayName,
            adultLengthMm, adultMassGrams, combatEnabled,
            new CareProfile(temperature.x, temperature.y, humidity.x, humidity.y, nutritionLossPerDay,
                hydrationLossPerDay, recoveryPerDay, stressChangePerDay, healthLossPerDay, larvalFoods, adultFoods,
                overturnedGraceDays, overturnedHealthLossPerDay),
            new LifecycleProfile(eggDays, larvaDays, pupaDays, adultDays),
            new BreedingProfile(clutchSize, breedingCooldownDays, minimumHealth, minimumNutrition, maximumStress, traitVariation));
    }
}
