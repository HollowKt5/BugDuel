using System;
using System.Collections.Generic;

namespace BeetleArena
{
    /// <summary>胜负确定时冻结；结束后的落地展示不能改变结算。消费者按 MatchId 幂等处理。</summary>
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
        // 随机展示对局没有持久个体 ID；禁止通过选手槽位反查养殖个体。
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
