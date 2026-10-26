using UnityEngine;
namespace BeetleArena
{
    /// <summary>Pure functional rhythm utility: fixed-step clock sampling, pauses, and bounded variance; stateless and free of per-frame RNG.</summary>
    public static class InsectMotionTiming
    {
        // Integer hash provides aesthetic variation without consuming match RNG; multiple samplings at the same timestamp yield identical results.
        public static float Value(int n, int salt)
        {
            unchecked { uint x = (uint)n * 747796405u + (uint)salt * 2891336453u;
                x = ((x >> (int)((x >> 28) + 4)) ^ x) * 277803737u;
                x = (x >> 22) ^ x; return (x & 0xffffff) / 16777215f; }
        }
        // At most one short motion per window with distinct start, duration, and amplitude; window boundaries are stationary to avoid discontinuities.
        public static float Bout(float time, float window, int salt)
        {
            int n = Mathf.FloorToInt(time / window);
            float t = time - n * window;
            float start = window * Mathf.Lerp(.08f, .22f, Value(n, salt));
            float span = window * Mathf.Lerp(.30f, .53f, Value(n, salt + 1));
            float p = Mathf.Clamp01((t - start) / span);
            return Mathf.Pow(Mathf.Sin(Mathf.PI * p), 2) * Mathf.Lerp(.6f, 1, Value(n, salt + 2));
        }
    }
}
