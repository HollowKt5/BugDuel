using System;
using System.Collections.Generic;

namespace BeetleArena
{
    /// <summary>Frozen upon match decision; post-match settlement presentation cannot alter results. Consumers process idempotently by MatchId.</summary>
    public sealed class BeetleMatchResult
    {
        public string MatchId { get; }
        public int Seed { get; }
        public int Winner { get; }
        public BeetleResultReason Reason { get; }
        public float ElapsedSeconds { get; }
        public IReadOnlyList<BeetleFighterResult> Fighters { get; }
        internal BeetleMatchResult(BeetleSimulation simulation)
        {
            MatchId = simulation.MatchId;
            Seed = simulation.Seed;
            Winner = simulation.Winner;
            Reason = simulation.ResultReason;
            ElapsedSeconds = simulation.Elapsed;
            Fighters = Array.AsReadOnly(new[] { new BeetleFighterResult(simulation.Fighters[0]), new BeetleFighterResult(simulation.Fighters[1]) });
        }
    }

    public sealed class BeetleFighterResult
    {
        public int Index { get; }
        // Exhibition matches lack persistent entity IDs; resolving husbandry individuals from contestant slots is prohibited.
        public string IndividualId { get; }
        public string SpeciesId { get; }
        public float RemainingStaminaFraction { get; }
        public float Fatigue { get; }
        public bool Fell { get; }
        internal BeetleFighterResult(BeetleState state)
        {
            Index = state.Index;
            IndividualId = state.Entrant?.IndividualId;
            SpeciesId = state.Entrant?.SpeciesId;
            RemainingStaminaFraction = state.Stamina01;
            Fatigue = state.Fatigue;
            Fell = state.HasFallen;
        }
    }
}
