using UnityEngine;
namespace BeetleArena
{
    /// <summary>
    /// Animation curves inspired by muscle recruitment without calculating muscle forces: slow bracing, brief rapid recruitment, extended unloading.
    /// References Azevedo et al. 2020 eLife 56754 (Drosophila), Toth et al. 2013 PLOS e78246 (stick insect models).
    /// Timing values are artistic parameters, not empirical beetle muscle durations. Pure sampling consumes no combat RNG.
    /// </summary>
    public static class InsectMuscleAnimation
    {
        static float Ramp(float a, float b, float t) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, t));

        // Initial stance hold, ramping gradually to low amplitude; peaks rapidly as fast motor units recruit, followed by unloading rather than linear oscillation.
        public static float Recruit(float phase)
        {
            float slow = .18f * Ramp(.06f, .24f, phase);
            float fast = .82f * Ramp(.27f, .35f, phase);
            return (slow + fast) * (1 - Ramp(.47f, 1, phase));
        }

        // Leg swing unloads support, drives forward rapidly, and decelerates near touchdown to search for contact. Continuous endpoint position and velocity prevent teleportation.
        public static float Swing(float phase) => phase < .16f ? .06f * Ramp(0, .16f, phase) :
            phase < .64f ? Mathf.Lerp(.06f, .90f, Ramp(.16f, .64f, phase)) : Mathf.Lerp(.90f, 1, Ramp(.64f, 1, phase));

        public static float Strike(float phase) => Ramp(.025f, .24f, phase);

        // Probing and horn-seeking locomotion is not continuous uniform crawling: distinct brief pauses precede re-starts; committed actions and external impulses bypass this gate.
        public static float Locomotion(BeetleState b)
        {
            if (b.Action != BeetleAction.Approach && b.Action != BeetleAction.Circle && b.Action != BeetleAction.Probe) return 1;
            float window = Mathf.Lerp(.46f, .66f, 1 - b.Traits.aggression);
            int cycle = Mathf.FloorToInt(b.ActionTime / window);
            float t = b.ActionTime - cycle * window;
            float hold = Mathf.Lerp(.025f, .095f, InsectMotionTiming.Value(cycle, 317 + b.Index * 13));
            float active = Ramp(hold, hold + .065f, t) * (1 - Ramp(window - .13f, window, t));
            // Active pressure maintains higher forward progress; neutral probing exhibits clearer pauses without losing target alignment.
            float floor = b.Intent == BeetleIntent.Pressure || b.Intent == BeetleIntent.Counter ? .58f : .22f;
            return Mathf.Lerp(floor, 1.18f, active);
        }

        public static float Response(BeetleState b, float fallback)
        {
            bool strike = b.Action == BeetleAction.Shove || b.Action == BeetleAction.Charge || b.Action == BeetleAction.Probe;
            return strike ? Mathf.Lerp(18, 29, b.BurstReserve) : fallback;
        }
    }
}
