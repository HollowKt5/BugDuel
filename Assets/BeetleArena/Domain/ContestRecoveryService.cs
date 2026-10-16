using System;
using System.Collections.Generic;

namespace BugDuel.Domain
{
    /// <summary>Explicit game consequences. A fall is not itself an injury or a health penalty.</summary>
    public sealed class RecoveryPolicy
    {
        public float HealthCost { get; }
        public float NutritionCost { get; }
        public float HydrationCost { get; }
        public float StressGain { get; }
        public RecoveryPolicy(float healthCost = 0, float nutritionCost = 8, float hydrationCost = 6, float stressGain = 10)
        {
            HealthCost = (float)DomainGuard.Range(healthCost, 0, 100, nameof(healthCost));
            NutritionCost = (float)DomainGuard.Range(nutritionCost, 0, 100, nameof(nutritionCost));
            HydrationCost = (float)DomainGuard.Range(hydrationCost, 0, 100, nameof(hydrationCost));
            StressGain = (float)DomainGuard.Range(stressGain, 0, 100, nameof(stressGain));
        }
    }

    public sealed class ContestAftermath
    {
        public string MatchId { get; }
        public string IndividualId { get; }
        public float Exertion01 { get; }
        public ContestAftermath(string matchId, string individualId, float exertion01)
        {
            MatchId = DomainGuard.Text(matchId, nameof(matchId));
            IndividualId = DomainGuard.Text(individualId, nameof(individualId));
            Exertion01 = (float)DomainGuard.Range(exertion01, 0, 1, nameof(exertion01));
        }
    }

    public sealed class ContestRecoveryService
    {
        /// <summary>The caller owns match authorization, concurrency, and the durable idempotency ledger.</summary>
        public DomainResult Apply(BeetleIndividual individual, ContestAftermath aftermath, RecoveryPolicy policy)
        {
            if (individual == null || aftermath == null || policy == null)
                return DomainResult.Fail("MissingRecoveryInput", "Individual, aftermath, and recovery policy are required.");
            if (individual.Id != aftermath.IndividualId)
                return DomainResult.Fail("IndividualMismatch", "The aftermath does not belong to this individual.");
            if (individual.Stage != LifeStage.Adult)
                return DomainResult.Fail("NotAdult", "Only a living adult can receive contest consequences.");
            if (individual.IsOverturned)
                return DomainResult.Fail("NeedsRighting", "An overturned beetle cannot receive a contest settlement.");
            BeetleCondition condition = individual.Condition;
            double effort = aftermath.Exertion01;
            individual.Condition = new BeetleCondition(Math.Max(0, condition.Health - policy.HealthCost * effort),
                Math.Max(0, condition.Nutrition - policy.NutritionCost * effort),
                Math.Max(0, condition.Hydration - policy.HydrationCost * effort),
                Math.Min(100, condition.Stress + policy.StressGain * effort));
            var events = new List<HusbandryEvent>();
            if (individual.Condition.Health == 0)
            {
                individual.Stage = LifeStage.Dead;
                events.Add(new HusbandryEvent(HusbandryEventKind.Died, individual, "ContestPolicy"));
            }
            events.Add(new HusbandryEvent(HusbandryEventKind.ContestApplied, individual, aftermath.MatchId));
            return new DomainResult(events: events);
        }
    }
}
