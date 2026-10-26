using UnityEngine;

namespace BeetleArena
{
    public enum BeetleIntent { Assess, Pressure, Counter, Defend, Reposition, Recover }

    /// <summary>Read-only decision snapshot sampled simultaneously by both fighters prior to Think(), preventing the second fighter from reacting to actions chosen during the same tick.</summary>
    public readonly struct BeetleTacticalSnapshot
    {
        public readonly float Advantage, Opening, Distance, Facing, Edge;
        public readonly bool Threatened, OpponentYielding, CanPressRecovery;
        public BeetleTacticalSnapshot(BeetleState self, BeetleState other, float radius, float reach)
        {
            Vector2 delta = other.Position - self.Position;
            Distance = delta.magnitude; Facing = Vector2.Dot(self.Forward, delta.normalized);
            Edge = self.Position.magnitude / radius;
            // Evaluates only past actions and support states; does not predict RNG outcomes or look ahead to future hit results.
            Threatened = other.Committed && Distance < reach + .25f &&
                Vector2.Dot(other.Forward, -delta.normalized) > .55f && other.ActionProgress < .78f;
            Opening = other.ExposureRemaining;
            OpponentYielding = other.Action == BeetleAction.Disengage || BeetleRecoveryPolicy.IsRest(other.Action);
            float initiative = self.Readiness01 + self.BurstReserve * .35f + self.Traits.aggression * .15f;
            float otherInitiative = other.Readiness01 + other.BurstReserve * .35f + other.Traits.aggression * .15f;
            CanPressRecovery = OpponentYielding && self.Readiness01 > .20f && self.BurstReserve > .14f &&
                initiative > otherInitiative;
            if (other.Action == BeetleAction.Stagger) Opening = Mathf.Max(Opening, .65f);
            if (BeetleRecoveryPolicy.IsRest(other.Action)) Opening = Mathf.Max(Opening, .38f);
            float condition = .40f * (self.Stamina01 - other.Stamina01) +
                .20f * (self.BurstReserve - other.BurstReserve) + .22f * (self.Balance - other.Balance) +
                .12f * (self.Footing - other.Footing) + .12f * (other.Position.magnitude / radius - Edge);
            Advantage = Mathf.Clamp(condition + .10f * (self.ContactConfidence - other.ContactConfidence), -1, 1);
        }
    }

    /// <summary>
    /// Combat tactics serve as a gameplay abstraction: drawing from fighting game concepts of spacing, commitment, recovery, and whiff punish windows without claiming beetles possess human tactical cognition.
    /// All methods are free of RNG sources and scene dependencies; they only return recommendations, leaving action and state mutation authority to Simulation.
    /// </summary>
    public static class BeetleTactics
    {
        public static BeetleIntent Evaluate(BeetleState b, in BeetleTacticalSnapshot view)
        {
            if (b.ContactReleaseRemaining > 0) return BeetleIntent.Reposition;
            if (view.Threatened && (b.Balance < .65f || b.Readiness01 < .30f || b.BurstReserve < .18f))
                return BeetleIntent.Defend;
            if (b.Balance < .40f || view.Edge > .84f && b.Footing < .84f) return BeetleIntent.Reposition;
            if (view.CanPressRecovery && view.Facing > .65f) return BeetleIntent.Counter;
            if (b.Readiness01 < .27f || b.BurstReserve < .22f)
                return view.Threatened ? BeetleIntent.Defend : BeetleIntent.Recover;
            if (view.Opening > .10f && b.Readiness01 > .31f && b.BurstReserve > .28f && view.Facing > .72f)
                return BeetleIntent.Counter;
            if (!view.Threatened && b.Balance > .55f && b.Readiness01 > .38f && b.BurstReserve > .34f &&
                (view.OpponentYielding || b.TimeSinceAttack > Mathf.Lerp(2.3f, .7f, b.Traits.aggression)))
                return BeetleIntent.Pressure;
            // Hysteresis prevents rapid cycling between offensive and defensive stances near boundary thresholds; advantage biases choices without artificially amplifying damage.
            float threshold = b.Intent == BeetleIntent.Pressure ? .025f : .09f;
            threshold -= (b.Traits.aggression - .6f) * .22f;
            if (!view.Threatened && b.TacticalAdvantage > threshold && b.Balance > .58f && b.BurstReserve > .33f)
                return BeetleIntent.Pressure;
            if (view.Threatened) return BeetleIntent.Defend;
            if (b.TacticalAdvantage < -.10f && b.Readiness01 < .45f) return BeetleIntent.Reposition;
            return BeetleIntent.Assess;
        }

        public static bool CanCommit(BeetleState b, float stamina, float reserve) =>
            b.Stamina >= stamina && b.BurstReserve >= reserve && b.Balance >= .42f;

        // RNG choice still provides variation within identical contexts; the tactical situation first circumscribes available actions rather than deciding offense/defense via lottery.
        public static BeetleAction Choose(BeetleState b, in BeetleTacticalSnapshot v, ExchangePhase exchange,
            float reach, float choice, bool inspectThrows = false)
        {
            if (v.Facing < .70f) return BeetleAction.Assess;
            if (b.Intent == BeetleIntent.Defend)
            {
                if (b.Action == BeetleAction.Brace && CanCommit(b, 24, .40f) && choice < b.Traits.drivePreference * .55f)
                    return BeetleAction.ShoveWindup;
                return v.Edge < .72f && choice < b.Traits.lateralPreference * .22f ? BeetleAction.Circle : BeetleAction.Brace;
            }
            if (b.Intent == BeetleIntent.Reposition)
                return b.Balance < .32f ? BeetleAction.Brace : BeetleAction.Circle;
            // Recovery cooldown requires disengaging and repositioning; inability to Rest must not prompt re-committing to high-cost attacks.
            if (b.Intent == BeetleIntent.Recover)
                return BeetleAction.Circle;
            if (v.Distance > reach) return BeetleAction.Approach;
            bool opening = b.Intent == BeetleIntent.Counter;
            bool pressure = b.Intent == BeetleIntent.Pressure;
            bool aggressiveOpening = b.Traits.aggression > .72f && choice > .65f && b.BurstReserve > .55f;
            if (!CanCommit(b, 18, .20f)) return opening ? BeetleAction.Probe : choice < .40f ? BeetleAction.Circle : BeetleAction.Probe;
            if (!opening && !pressure && !aggressiveOpening && exchange != ExchangePhase.Clash)
                return choice < .18f ? BeetleAction.Circle : BeetleAction.Probe;
            if (!opening && choice < (pressure ? .08f : .15f)) return BeetleAction.Circle;
            // Evaluate the lifting opening before the short-push branch. Previously
            // both used the low end of the same roll, so counter openings consumed
            // the entire pry interval before this branch could be reached.
            if (b.PryRetryRemaining <= 0 && (b.TouchingOpponent || v.Distance < reach - .25f) && b.Contacts >= 2 && CanCommit(b, 26, .35f) &&
                choice < Mathf.Lerp(inspectThrows ? .35f : .16f, inspectThrows ? .85f : .55f, b.Traits.liftPreference))
                return BeetleAction.PryWindup;
            if (opening && choice < Mathf.Lerp(.12f, .48f, b.Traits.drivePreference))
                return b.TouchingOpponent || v.Distance < reach - .2f ? BeetleAction.ShoveWindup : BeetleAction.Approach;
            if (!b.TouchingOpponent && CanCommit(b, 32, .48f) && choice > Mathf.Lerp(.68f, .28f, b.Traits.drivePreference))
                return BeetleAction.ChargeWindup;
            return BeetleAction.ShoveWindup;
        }
    }
}
