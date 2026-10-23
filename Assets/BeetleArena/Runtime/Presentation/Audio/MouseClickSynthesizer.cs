using System;
using UnityEngine;

namespace BeetleArena
{
    public readonly struct MouseClickSettings : IEquatable<MouseClickSettings>
    {
        public readonly float Volume, Pitch, Brightness, AttackMs, DecayMs, Noise, ReboundMs, ReboundStrength;
        public static MouseClickSettings Default => new MouseClickSettings(.45f, 1.3f, .8f, .04f, 1.1f, .18f, 60f, .8f);

        public MouseClickSettings(float volume, float pitch, float brightness, float attackMs, float decayMs,
            float noise, float reboundMs, float reboundStrength)
        {
            Volume = Safe(volume, 0, 1, .45f);
            Pitch = Safe(pitch, .65f, 2, 1.3f);
            Brightness = Safe(brightness, 0, 1, .8f);
            AttackMs = Safe(attackMs, .02f, .4f, .04f);
            DecayMs = Safe(decayMs, .4f, 4, 1.1f);
            Noise = Safe(noise, 0, 1, .18f);
            ReboundMs = Safe(reboundMs, 0, 150, 60f);
            ReboundStrength = Safe(reboundStrength, 0, 1, .8f);
        }

        static float Safe(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));

        public bool Equals(MouseClickSettings other) => Volume == other.Volume && Pitch == other.Pitch &&
            Brightness == other.Brightness && AttackMs == other.AttackMs && DecayMs == other.DecayMs &&
            Noise == other.Noise && ReboundMs == other.ReboundMs && ReboundStrength == other.ReboundStrength;
    }

    // The editor preview and the game use identical samples; only the game's master volume is external.
    public static class MouseClickSynthesizer
    {
        public const int SampleRate = 44100;

        public static AudioClip CreateClip(MouseClickSettings settings)
        {
            var data = Synthesize(settings);
            var clip = AudioClip.Create("Mechanical mouse click", data.Length, 1, SampleRate, false);
            clip.hideFlags = HideFlags.HideAndDontSave;
            clip.SetData(data, 0);
            return clip;
        }

        public static float[] Synthesize(MouseClickSettings settings)
        {
            double attack = settings.AttackMs * .001, decay = settings.DecayMs * .001;
            double delay = settings.ReboundMs * .001;
            var data = new float[(int)Math.Ceiling((delay + decay * 10 + .002) * SampleRate)];
            var random = new System.Random(32017);
            double lowNoise = 0, midNoise = 0, peak = 0;
            double lowMix = 1 - Math.Exp(-2 * Math.PI * (700 + 900 * settings.Brightness) / SampleRate);
            double midMix = 1 - Math.Exp(-2 * Math.PI * (5000 + 4000 * settings.Brightness) / SampleRate);
            double Impact(double t) => t < 0 ? 0 : (1 - Math.Exp(-t / attack)) * Math.Exp(-t / (decay * .45));
            double Knock(double t, double frequency)
            {
                if (t < 0) return 0;
                return Math.Min(1, t / attack) *
                    (.70 * Math.Sin(2 * Math.PI * frequency * t) * Math.Exp(-t / decay)
                    + (.12 + .30 * settings.Brightness) * Math.Sin(2 * Math.PI * frequency * 2.37 * t) * Math.Exp(-t / (decay * .40)));
            }
            for (int i = 0; i < data.Length; i++)
            {
                double t = i / (double)SampleRate;
                double noise = random.NextDouble() * 2 - 1;
                lowNoise += (noise - lowNoise) * lowMix;
                midNoise += (noise - midNoise) * midMix;
                double snap = Impact(t) + settings.ReboundStrength * Impact(t - delay);
                double shell = Knock(t, 1260 * settings.Pitch) + settings.ReboundStrength * Knock(t - delay, 2180 * settings.Pitch);
                double sample = settings.Noise * ((midNoise - lowNoise) + .12 * settings.Brightness * (noise - midNoise)) * snap + shell;
                sample *= Math.Min(1, t / attack) * Math.Min(1, (data.Length - 1 - i) / (SampleRate * .001));
                data[i] = (float)sample;
                peak = Math.Max(peak, Math.Abs(sample));
            }
            float gain = (float)(.65 * settings.Volume / Math.Max(.001, peak));
            for (int i = 0; i < data.Length; i++) data[i] *= gain;
            return data;
        }
    }
}
