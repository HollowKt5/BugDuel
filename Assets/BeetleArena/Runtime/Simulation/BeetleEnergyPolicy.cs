using UnityEngine;

namespace BeetleArena
{
    /// <summary>
    /// Decouples endurance from short-burst output: a gameplay approximation of sustained posture vs. explosive impulse, not a muscle physiology simulation.
    /// State advances strictly when invoked by Simulation; animations and modules are read-only, preventing render frame rate from altering stamina.
    /// </summary>
    public static class BeetleEnergyPolicy
    {
        public static float AttackEffort(BeetleState b) =>
            Mathf.Lerp(.42f, 1, b.Stamina01) * Mathf.Lerp(.82f, 1, b.BurstReserve);

        public static void TickClocks(BeetleState b, float dt)
        {
            b.ExertionCooldown = Mathf.Max(0, b.ExertionCooldown - dt);
            b.ExposureRemaining = Mathf.Max(0, b.ExposureRemaining - dt);
            b.PryRetryRemaining = Mathf.Max(0, b.PryRetryRemaining - dt);
            b.ContactConfidence = Mathf.MoveTowards(b.ContactConfidence, 0, dt * .23f);
            b.IntentTime += dt;
            b.TimeSinceAttack += dt;
            b.ContactReleaseRemaining = Mathf.Max(0, b.ContactReleaseRemaining - dt);
            b.ContactReleaseCooldown = Mathf.Max(0, b.ContactReleaseCooldown - dt);
        }

        public static void Apply(BeetleState b, BeetleMatchSettings s, float cost, bool threatened, float dt)
        {
            bool working = cost >= 5 || b.Action == BeetleAction.Lifting;
            if (working) b.ExertionCooldown = s.exertionRecoveryDelay;
            // Disengaging and repositioning yield better recovery; close-range stance holding still grants minor recovery to prevent mutual low-stamina deadlock.
            if (cost < 0)
            {
                float factor = b.TouchingOpponent ? s.contactRecoveryMultiplier : 1;
                // Genuine unloading recovery is more effective than holding ground in place; otherwise exhausted fighters get trapped in low-energy attrition.
                if (!threatened && BeetleRecoveryPolicy.IsRest(b.Action)) factor = Mathf.Max(factor, .85f);
                if (threatened) factor *= .55f;
                if (b.ExertionCooldown > 0) factor *= .18f;
                cost *= factor;
            }
            b.Stamina = Mathf.Clamp(b.Stamina - cost * dt, 0, b.StaminaLimit);
            float burstRate = working ? -s.burstDrainPerSecond * Mathf.Clamp(cost / 14, .40f, 1.8f) :
                s.burstRecoveryPerSecond * (b.ExertionCooldown > 0 ? .10f : threatened ? .25f : .85f);
            b.BurstReserve = Mathf.Clamp01(b.BurstReserve + burstRate * dt);
        }

        // One-off pressure applied upon hit resolution; never deducted per collision frame, and encouragement does not instantly refill burst reserves.
        public static void ReceiveContact(BeetleState attacker, BeetleState defender, float intensity)
        {
            attacker.ContactConfidence = Mathf.Clamp(attacker.ContactConfidence + intensity * .30f, -1, 1);
            defender.ContactConfidence = Mathf.Clamp(defender.ContactConfidence - intensity * .36f, -1, 1);
            defender.BurstReserve = Mathf.Max(0, defender.BurstReserve - intensity * .07f);
            defender.ExertionCooldown = Mathf.Max(defender.ExertionCooldown, intensity * .18f);
        }
    }
}
