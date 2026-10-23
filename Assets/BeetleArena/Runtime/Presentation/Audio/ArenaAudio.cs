using UnityEngine;

namespace BeetleArena
{
    /// <summary>接触声表现：宽频干枝碰撞瞬态 + 独立短摩擦。合成参数为听感设计，不是甲虫录音测量。</summary>
    public sealed class ArenaAudio : MonoBehaviour
    {
        [Range(0, 1)] public float volume = .48f;
        readonly AudioSource[] voices = new AudioSource[4];
        AudioClip[] knocks, scrapes;
        int voiceIndex, variation;
        public int PlayedContacts { get; private set; }
        void Awake() => Initialize();
        public void Initialize()
        {
            if (knocks != null) return;
            knocks = new AudioClip[6]; scrapes = new AudioClip[3];
            for (int i = 0; i < knocks.Length; i++) knocks[i] = MakeClip(i, false);
            for (int i = 0; i < scrapes.Length; i++) scrapes[i] = MakeClip(i, true);
            for (int i = 0; i < voices.Length; i++)
            {
                var go = new GameObject("Contact Voice " + (i + 1)); go.transform.SetParent(transform, false);
                var v = go.AddComponent<AudioSource>(); voices[i] = v;
                v.playOnAwake = false; v.spatialBlend = .65f; v.dopplerLevel = 0;
                v.rolloffMode = AudioRolloffMode.Linear; v.minDistance = 6; v.maxDistance = 35;
            }
        }
        public void PlayContact(Vector3 point, float strength, bool scrape)
        {
            if (!isActiveAndEnabled) return;
            Initialize(); strength = Mathf.Clamp01(strength);
            var v = voices[voiceIndex++ % voices.Length];
            var bank = scrape ? scrapes : knocks;
            // 独立轮换，不消费 Unity.Random 或比赛随机源；每个声音拥有自己的空间位置和音调。
            int index = variation++ % bank.Length;
            v.Stop(); v.transform.position = point;
            v.pitch = Mathf.Lerp(1.07f, .94f, strength) + (index % 3 - 1) * .017f;
            v.volume = volume * Mathf.Lerp(.22f, 1, strength) * (scrape ? .38f : 1);
            v.clip = bank[index]; v.Play(); PlayedContacts++;
        }
        public void StopAll() { foreach (var v in voices) if (v) v.Stop(); }
        public void SetPaused(bool paused)
        { foreach (var v in voices) if (v) { if (paused) v.Pause(); else v.UnPause(); } }
        static AudioClip MakeClip(int variant, bool scrape)
        {
            float[] data = Synthesize(variant, scrape);
            var clip = AudioClip.Create((scrape ? "Dry horn scrape " : "Woody horn knock ") + variant, data.Length, 1, 44100, false);
            clip.SetData(data, 0); return clip;
        }
        // 参考 Aramaki/Kronland-Martinet 2004 的模态与阻尼思路：高频比低频更快消退。
        // 约 0.105s 敲击，减弱窄带共鸣，以短瞬态和纤维二次碰触构成干枝质感。
        public static float[] Synthesize(int variant, bool scrape)
        {
            const int rate = 44100; float length = scrape ? .115f : .105f;
            var data = new float[Mathf.RoundToInt(rate * length)];
            var random = new System.Random(913 + variant * 107 + (scrape ? 701 : 0));
            float low = 0, lowSlow = 0, peak = 0;
            float[] modes = { 1130, 1877, 2933, 4211, 6173 };
            float[] gains = { .055f, .043f, .030f, .022f, .014f };
            float tuning = .94f + .023f * variant;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate, noise = (float)random.NextDouble() * 2 - 1;
                low += (noise - low) * .31f; lowSlow += (noise - lowSlow) * .045f;
                float band = low - lowSlow, sample = 0;
                if (scrape)
                {
                    float grain = .35f + .65f * Mathf.Pow(Mathf.Sin((t * (83 + variant * 11) + .1f) * Mathf.PI), 2);
                    sample = band * grain * Mathf.Sin(Mathf.PI * t / length) * Mathf.Exp(-t * 14);
                }
                else
                {
                    for (int m = 0; m < modes.Length; m++)
                        sample += gains[m] * Mathf.Sin(2 * Mathf.PI * modes[m] * tuning * t) * Mathf.Exp(-t * (135 + 47 * m));
                    sample += band * 1.15f * Mathf.Exp(-t * 155);
                    // Irregular secondary fibre contacts: dry branch clicks, no hollow pitched body.
                    float delay = .009f + variant * .0013f;
                    if (t > delay) sample += band * .30f * Mathf.Exp(-(t - delay) * 240);
                    // 少量不规则微擦，只作为干脆敲击后的纹理，不连续喷噪声。
                    sample += band * .10f * Mathf.Exp(-t * 55) * Mathf.Pow(Mathf.Sin(t * 270), 2);
                }
                sample *= Mathf.Min(1, t / .0006f) * Mathf.Clamp01((length - t) / .006f);
                data[i] = sample; peak = Mathf.Max(peak, Mathf.Abs(sample));
            }
            // 给多声部叠加留余量；不在播放时做每帧波形生成。
            float gain = (scrape ? .44f : .72f) / Mathf.Max(.001f, peak);
            for (int i = 0; i < data.Length; i++) data[i] *= gain;
            return data;
        }
        void OnDisable() => StopAll();
        void OnDestroy() => ReleaseResources();
        /// <summary>运行时销毁及 Editor 手动验证共用。幂等；释放后显式 Initialize 可重新创建。</summary>
        public void ReleaseResources()
        {
            StopAll();
            foreach (var voice in voices) if (voice) Release(voice.gameObject);
            if (knocks != null) foreach (var c in knocks) Release(c);
            if (scrapes != null) foreach (var c in scrapes) Release(c);
            knocks = scrapes = null;
            System.Array.Clear(voices, 0, voices.Length);
            voiceIndex = variation = 0;
        }
        static void Release(UnityEngine.Object c) { if (!c) return; if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }
    }
}
