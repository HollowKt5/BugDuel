using System;
using System.Collections.Generic;

namespace BugDuel.Domain
{
    public sealed class HabitatConditions
    {
        public float TemperatureCelsius { get; }
        public float HumidityPercent { get; }
        public float CleanlinessPercent { get; }
        public float Crowding { get; }
        public bool Resting { get; }
        public HabitatConditions(float temperatureCelsius = 24, float humidityPercent = 70,
            float cleanlinessPercent = 100, float crowding = 0, bool resting = true)
        {
            TemperatureCelsius = (float)DomainGuard.Range(temperatureCelsius, -100, 100, nameof(temperatureCelsius));
            HumidityPercent = (float)DomainGuard.Range(humidityPercent, 0, 100, nameof(humidityPercent));
            CleanlinessPercent = (float)DomainGuard.Range(cleanlinessPercent, 0, 100, nameof(cleanlinessPercent));
            Crowding = (float)DomainGuard.Range(crowding, 0, 1, nameof(crowding));
            Resting = resting;
        }
    }

    public sealed class FoodPortion
    {
        public FoodKind Kind { get; }
        public float Nutrition { get; }
        public float Hydration { get; }
        public FoodPortion(FoodKind kind, float nutrition, float hydration)
        {
            DomainGuard.Foods(kind, nameof(kind));
            // A portion has one food category. Species preferences may contain several categories.
            if (((int)kind & ((int)kind - 1)) != 0) throw new ArgumentException("Use one food category per portion.", nameof(kind));
            Kind = kind;
            Nutrition = (float)DomainGuard.Range(nutrition, 0, 100, nameof(nutrition));
            Hydration = (float)DomainGuard.Range(hydration, 0, 100, nameof(hydration));
        }
    }

    /// <summary>Explicit elapsed game days; no clocks, global state, randomness, or persistence IO.</summary>
    public sealed class HusbandryService
    {
        public DomainResult MarkOverturned(BeetleIndividual individual, SpeciesDefinition species)
        {
            DomainResult invalid = Check(individual, species);
            if (invalid != null) return invalid;
            if (individual.Stage != LifeStage.Adult) return DomainResult.Fail("NotAdult", "Only living adults can require righting assistance.");
            if (individual.IsOverturned) return DomainResult.Fail("AlreadyOverturned", "This beetle already needs assistance; its rescue timer is unchanged.");
            individual.IsOverturned = true;
            individual.OverturnedDays = 0;
            return new DomainResult(events: new[] { new HusbandryEvent(HusbandryEventKind.Overturned, individual) });
        }

        public DomainResult HelpRight(BeetleIndividual individual, SpeciesDefinition species)
        {
            DomainResult invalid = Check(individual, species);
            if (invalid != null) return invalid;
            if (individual.Stage != LifeStage.Adult) return DomainResult.Fail("NotAdult", "Only living adults can be helped upright.");
            if (!individual.IsOverturned) return DomainResult.Fail("NotOverturned", "This beetle is already upright.");
            individual.IsOverturned = false;
            individual.OverturnedDays = 0;
            return new DomainResult(events: new[] { new HusbandryEvent(HusbandryEventKind.Righted, individual) });
        }

        public DomainResult Feed(BeetleIndividual individual, SpeciesDefinition species, FoodPortion food)
        {
            DomainResult invalid = Check(individual, species);
            if (invalid != null) return invalid;
            if (food == null) return DomainResult.Fail("MissingFood", "A food portion is required.");
            if (individual.IsOverturned) return DomainResult.Fail("NeedsRighting", "Help the beetle upright before feeding.");
            FoodKind diet = individual.Stage == LifeStage.Larva ? species.Care.LarvalFoods :
                individual.Stage == LifeStage.Adult ? species.Care.AdultFoods : FoodKind.None;
            if ((diet & food.Kind) == 0) return DomainResult.Fail("IncompatibleFood", "This stage or species cannot eat that food.");
            BeetleCondition condition = individual.Condition;
            individual.Condition = new BeetleCondition(condition.Health, condition.Nutrition + food.Nutrition,
                condition.Hydration + food.Hydration, condition.Stress);
            return new DomainResult(events: new[] { new HusbandryEvent(HusbandryEventKind.Fed, individual, food.Kind.ToString()) });
        }

        public DomainResult Advance(BeetleIndividual individual, SpeciesDefinition species, HabitatConditions habitat, double elapsedDays)
        {
            DomainResult invalid = Check(individual, species);
            if (invalid != null) return invalid;
            if (habitat == null) return DomainResult.Fail("MissingHabitat", "Habitat conditions are required.");
            if (double.IsNaN(elapsedDays) || double.IsInfinity(elapsedDays) || elapsedDays < 0 || elapsedDays > 1000000)
                return DomainResult.Fail("InvalidElapsedTime", "Elapsed game days must be finite and between zero and one million.");
            var events = new List<HusbandryEvent>();
            if (individual.Stage == LifeStage.Dead) return new DomainResult(events: events);
            CareProfile care = species.Care;
            double pressure = DistanceOutside(habitat.TemperatureCelsius, care.MinimumTemperature, care.MaximumTemperature) / 10 +
                DistanceOutside(habitat.HumidityPercent, care.MinimumHumidity, care.MaximumHumidity) / 30 +
                (100 - habitat.CleanlinessPercent) / 100.0 + habitat.Crowding;
            double stressRate = care.StressChangePerDay * (pressure > 0 ? pressure : habitat.Resting ? -1 : -0.25);
            if (individual.IsOverturned) stressRate = Math.Max(care.StressChangePerDay, stressRate);
            double remaining = elapsedDays;
            // Rates change only at life-stage, deprivation, recovery, and death boundaries.
            // Splitting there makes offline and subdivided advances follow the same continuous model.
            while (individual.Stage != LifeStage.Dead &&
                (remaining > 0 || individual.StageAgeDays >= species.Lifecycle.Duration(individual.Stage)))
            {
                BeetleCondition condition = individual.Condition;
                double metabolism = individual.Stage == LifeStage.Egg ? 0.1 : individual.Stage == LifeStage.Pupa ? 0.15 : 1;
                double nutritionRate = -care.NutritionLossPerDay * metabolism;
                double hydrationRate = -care.HydrationLossPerDay * metabolism;
                double healthRate = -care.HealthLossPerDay * pressure;
                if (condition.Nutrition <= 0) healthRate -= care.HealthLossPerDay;
                if (condition.Hydration <= 0) healthRate -= care.HealthLossPerDay;
                bool calm = condition.Stress < 60 || condition.Stress == 60 && stressRate <= 0;
                if (!individual.IsOverturned && pressure == 0 && condition.Nutrition > 25 && condition.Hydration > 25 && calm)
                    healthRate += care.RecoveryPerDay * (habitat.Resting ? 1 : 0.25);
                if (individual.IsOverturned && individual.OverturnedDays >= care.OverturnedGraceDays)
                    healthRate -= care.OverturnedHealthLossPerDay;
                // Age is historical, not derived from today's tuning. A shortened stage ends at this
                // advance's first boundary (zero elapsed time), never by running the clock backwards.
                double stageRemaining = Math.Max(0, species.Lifecycle.Duration(individual.Stage) - individual.StageAgeDays);
                double step = Math.Min(remaining, stageRemaining);
                step = Boundary(step, condition.Nutrition, nutritionRate, 25);
                step = Boundary(step, condition.Nutrition, nutritionRate, 0);
                step = Boundary(step, condition.Hydration, hydrationRate, 25);
                step = Boundary(step, condition.Hydration, hydrationRate, 0);
                step = Boundary(step, condition.Stress, stressRate, 60);
                if (individual.IsOverturned) step = Boundary(step, individual.OverturnedDays, 1, care.OverturnedGraceDays);
                step = Boundary(step, condition.Health, healthRate, 0);
                double health = condition.Health + healthRate * step;
                // A mathematically exact death boundary can retain a tiny positive floating-point residue.
                if (healthRate < 0 && (step >= condition.Health / -healthRate || health <= 0.000000001)) health = 0;
                individual.Condition = new BeetleCondition(health, Integrate(condition.Nutrition, nutritionRate, step, 25, 0),
                    Integrate(condition.Hydration, hydrationRate, step, 25, 0), Integrate(condition.Stress, stressRate, step, 60, 60));
                individual.TotalAgeDays += step;
                individual.StageAgeDays += step;
                if (individual.IsOverturned)
                {
                    double graceRemaining = care.OverturnedGraceDays - individual.OverturnedDays;
                    individual.OverturnedDays = graceRemaining > 0 && step == graceRemaining
                        ? care.OverturnedGraceDays : individual.OverturnedDays + step;
                }
                individual.BreedingCooldownDays = Math.Max(0, Math.Min(species.Breeding.CooldownDays, individual.BreedingCooldownDays) - step);
                remaining = Math.Max(0, remaining - step);
                if (individual.Condition.Health == 0)
                {
                    Die(individual, events, individual.IsOverturned && individual.OverturnedDays > care.OverturnedGraceDays &&
                        care.OverturnedHealthLossPerDay > 0 ? "UnassistedOverturn" : "Condition");
                }
                else if (step >= stageRemaining)
                {
                    if (individual.Stage == LifeStage.Adult) Die(individual, events, "Lifespan");
                    else
                    {
                        individual.Stage = (LifeStage)((int)individual.Stage + 1);
                        individual.StageAgeDays = 0;
                        events.Add(new HusbandryEvent(HusbandryEventKind.StageChanged, individual));
                    }
                }
            }
            return new DomainResult(events: events);
        }

        internal static DomainResult Check(BeetleIndividual individual, SpeciesDefinition species)
        {
            if (individual == null || species == null) return DomainResult.Fail("MissingIndividualOrSpecies", "Individual and species are required.");
            if (individual.SpeciesId != species.Id) return DomainResult.Fail("SpeciesMismatch", "The definition does not match the individual's species ID.");
            return null;
        }
        private static double DistanceOutside(double value, double min, double max) => Math.Max(0, Math.Max(min - value, value - max));
        private static double Boundary(double step, double value, double rate, double threshold)
        {
            if (rate == 0) return step;
            double time = (threshold - value) / rate;
            return time > 0 ? Math.Min(step, time) : step;
        }
        private static double Integrate(double value, double rate, double step, double firstBoundary, double secondBoundary)
        {
            // Preserve exact selected boundaries rather than carrying tiny positive residues into the next segment.
            if (rate != 0)
            {
                double firstTime = (firstBoundary - value) / rate;
                if (firstTime > 0 && step == firstTime) return firstBoundary;
                double secondTime = (secondBoundary - value) / rate;
                if (secondTime > 0 && step == secondTime) return secondBoundary;
            }
            return value + rate * step;
        }
        private static void Die(BeetleIndividual individual, List<HusbandryEvent> events, string reason)
        {
            individual.Stage = LifeStage.Dead;
            individual.Condition = new BeetleCondition(0, individual.Condition.Nutrition, individual.Condition.Hydration, individual.Condition.Stress);
            events.Add(new HusbandryEvent(HusbandryEventKind.Died, individual, reason));
        }
    }
}
