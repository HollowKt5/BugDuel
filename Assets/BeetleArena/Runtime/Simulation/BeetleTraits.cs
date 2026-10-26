using System;
using UnityEngine;

namespace BeetleArena
{
    [Serializable]
    // Immutable per-match fighter attributes treated as read-only data post-instantiation; decoupled from per-step mutable BeetleState.
    public sealed class BeetleTraits
    {
        // size is base scale multiplier; lengthMm/massGrams are display attributes; mass is normalized kinematic mass.
        // strength and grip control force exertion and traction; endurance is max stamina; aggression dictates offensive bias;
        // agility affects movement and turning; hornReach simplifies reach in simplified contact mode.
        public float size, lengthMm, massGrams, mass, strength, grip, endurance, aggression, agility, hornReach;
        public float aggressionScore, lengthScore, hornScore, agilityScore, hornLengthMm;
        public float hornLengthFactor = 1;
        // Independent style affects cadence and choices, not an extra strength bonus.
        public float tempo = 1, drivePreference = .5f, liftPreference = .5f, lateralPreference = .5f;
        public float TotalScore => aggressionScore + lengthScore + hornScore + agilityScore;
        // N(240, 28²): the outer deciles start at z = ±1.28155.
        public string QualityBand => TotalScore >= 275.88f ? "高" : TotalScore <= 204.12f ? "低" : "中";
        // All members are value types; entrance snapshots and individual rematches hold isolated trait instances.
        public BeetleTraits Copy() => (BeetleTraits)MemberwiseClone();
        // Procedurally derives attributes from body size and a shared in-match RNG source. Mass scales with size^3, strength with size^2,
        // followed by minor individual variance; these are gameplay approximations, not species empirical data.
        public static BeetleTraits Create(float size, System.Random random, BeetleMatchSettings settings)
        {
            float Vary() => .95f + (float)random.NextDouble() * .10f;
            float normal = Mathf.Sqrt(-2 * Mathf.Log(Mathf.Max(1e-7f, (float)random.NextDouble()))) * Mathf.Cos(2 *
                Mathf.PI * (float)random.NextDouble());
            float total = Mathf.Round(Mathf.Clamp(240 + 28 * normal, 150, 310));
            float length = Mathf.Round(Mathf.Lerp(35, 75, Mathf.InverseLerp(settings.minimumSize, settings.maximumSize, size)));
            float mean = (total - length) / 3;
            float aggressionRating = Mathf.Round(Mathf.Clamp(mean + ((float)random.NextDouble() - .5f) * 24, 10, 100));
            float hornRating = Mathf.Round(Mathf.Clamp(mean + ((float)random.NextDouble() - .5f) * 24, 10, 100));
            float agilityRating = total - length - aggressionRating - hornRating;
            // Redistribute any capped rating; the actual four-stat sum remains the sampled total.
            float excess = agilityRating - Mathf.Clamp(agilityRating, 10, 100);
            agilityRating -= excess;
            float adjusted = Mathf.Clamp(aggressionRating + excess, 10, 100);
            hornRating += excess - (adjusted - aggressionRating);
            aggressionRating = adjusted;
            float hornFactor = Mathf.Lerp(.84f, 1.20f, hornRating / 100);
            return new BeetleTraits
            {
                size = size,
                lengthMm = settings.referenceLengthMm * size,
                massGrams = settings.referenceMassGrams * size * size * size,
                mass = size * size * size,
                strength = size * size * Vary(),
                grip = size * Vary(),
                endurance = 100 * Vary(),
                aggression = Mathf.Lerp(.32f, .95f, aggressionRating / 100),
                agility = Mathf.Lerp(.72f, 1.40f, agilityRating / 100) / Mathf.Sqrt(size),
                hornReach = 1.65f * size * hornFactor,
                aggressionScore = aggressionRating,
                lengthScore = length,
                hornScore = hornRating,
                agilityScore = agilityRating,
                hornLengthFactor = hornFactor,
                hornLengthMm = settings.referenceLengthMm * size * .48f * hornFactor
            };
        }
    }

}
