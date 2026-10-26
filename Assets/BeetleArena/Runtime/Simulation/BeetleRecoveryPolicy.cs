using UnityEngine;

namespace BeetleArena
{
    /// <summary>
    /// Stateless rules for individual recovery: does not touch Transforms, draw random numbers, or mutate state.
    /// Literature supports contact assessment, yielding, and chasing; specific stamina thresholds and durations are gameplay parameters (TechnicalGuide §22).
    /// </summary>
    public static class BeetleRecoveryPolicy
    {
        public static bool IsRest(BeetleAction action) => action == BeetleAction.Recover || action == BeetleAction.Retreat;
        // Stable individual tempo and caution preferences determine regrouping duration, avoiding using array index as combat precedence.
        public static float Budget(BeetleState b, BeetleMatchSettings s) =>
            Mathf.Max(s.recoveryMinimumSeconds, s.recoveryMaximumSeconds * Mathf.Lerp(.55f, 1.10f, b.Traits.lateralPreference) / b.Traits.tempo);
        public static bool Ready(BeetleState b, BeetleMatchSettings s, bool pressure)
        {
            if (!IsRest(b.Action)) return false;
            if (pressure && b.RecoveryTime >= .18f) return true;
            return b.RecoveryTime >= Budget(b, s) ||
                (b.RecoveryTime >= s.recoveryMinimumSeconds && b.Stamina >= b.StaminaLimit *
                    Mathf.Clamp(s.recoveryResumeFraction + (b.Traits.lateralPreference - .5f) * .32f, .35f, .75f) && b.BurstReserve >= .40f);
        }
        public static bool WantsRest(BeetleState b, bool regrouping, bool pressure)
        {
            if (pressure || b.RecoveryCooldown > 0) return false;
            return b.Readiness01 < Mathf.Lerp(.39f, .23f, b.Traits.aggressionScore / 100) || b.BurstReserve < .20f ||
                (regrouping && b.Intent != BeetleIntent.Pressure && b.Intent != BeetleIntent.Counter &&
                 b.Readiness01 < Mathf.Lerp(.48f, .33f, b.Traits.aggression));
        }
    }
}
