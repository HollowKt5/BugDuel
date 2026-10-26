using UnityEngine;
using UnityEngine.Rendering;

namespace BeetleArena
{
    /// <summary>Toggleable weapon spark presentation. Contains no lights, damage, or physics; material explicitly referenced to preserve build dependencies.</summary>
    public sealed class HornClashSparks : MonoBehaviour
    {
        public const string PreferenceKey = "BugDuel.HornClashSparks.v1";
        public Material sparkMaterial;
        [Range(3, 12)] public int maximumBurst = 8;
        ParticleSystem particles;
        System.Random random = new System.Random(1857);
        bool initialized, sparksEnabled;
        public bool SparksEnabled { get { Initialize(); return sparksEnabled; } }
        public int BurstCount { get; private set; }
        public int LiveParticles => particles ? particles.particleCount : 0;
        public ParticleSystem Particles => particles;
        void Awake() => Initialize();
        public void Initialize()
        {
            if (initialized) return;
            initialized = true; sparksEnabled = PlayerPrefs.GetInt(PreferenceKey, 1) != 0;
            var go = new GameObject("Horn Collision Sparks"); go.transform.SetParent(transform, false);
            particles = go.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main; main.playOnAwake = false; main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 48;
            main.startLifetime = .20f; main.startSpeed = 0; main.startSize = .027f; main.gravityModifier = .55f;
            particles.useAutoRandomSeed = false; particles.randomSeed = 1857;
            var emission = particles.emission; emission.enabled = false;
            var shape = particles.shape; shape.enabled = false;
            var size = particles.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, .15f));
            var color = particles.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(new Color(1, .96f, .73f), 0),
                new GradientColorKey(new Color(1, .53f, .12f), .35f), new GradientColorKey(new Color(.8f, .17f, .02f), 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.8f, .45f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = sparkMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Stretch; renderer.lengthScale = 2.2f; renderer.velocityScale = .055f;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        }
        public void SetEnabled(bool value, bool persist = true)
        {
            Initialize(); sparksEnabled = value;
            if (!value) Clear(); // Disabling clears existing sparks immediately rather than merely halting the next emission.
            if (persist) { PlayerPrefs.SetInt(PreferenceKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }
        public void Tick(float dt)
        {
            if (isActiveAndEnabled && particles && dt > 0) particles.Simulate(dt, true, false, true);
        }
        public void Clear() { if (particles) particles.Clear(true); }
        public void Burst(Vector3 point, Vector3 normal, float strength)
        {
            if (!isActiveAndEnabled) return;
            Initialize(); if (!sparksEnabled || !sparkMaterial) return;
            // Unity 6 may drop the first EmitParams on a freshly stopped/cleared ParticleSystem; establishes a paused simulation state first.
            if (particles.isStopped) particles.Simulate(0, true, true, false);
            normal = normal.sqrMagnitude > .001f ? normal.normalized : Vector3.right;
            Vector3 tangent = Vector3.Cross(normal, Vector3.up);
            if (tangent.sqrMagnitude < .01f) tangent = Vector3.forward;
            tangent.Normalize(); Vector3 second = Vector3.Cross(normal, tangent).normalized;
            int count = Mathf.RoundToInt(Mathf.Lerp(3, Mathf.Clamp(maximumBurst, 3, 12), Mathf.Clamp01(strength)));
            for (int i = 0; i < count; i++)
            {
                float angle = Next() * Mathf.PI * 2;
                Vector3 v = (tangent * Mathf.Cos(angle) + second * Mathf.Sin(angle)) * Mathf.Lerp(.8f, 1.9f, Next());
                v += normal * Mathf.Lerp(-.35f, .35f, Next()) + Vector3.up * .45f;
                var p = new ParticleSystem.EmitParams { position = point, velocity = v,
                    startLifetime = Mathf.Lerp(.11f, .24f, Next()), startSize = Mathf.Lerp(.018f, .034f, Next()),
                    startColor = Color.white };
                particles.Emit(p, 1);
            }
            BurstCount++;
        }
        void OnDisable() => Clear();
        void OnDestroy() => ReleaseResources();
        /// <summary>Explicit Editor validation decoupled from MonoBehaviour lifecycle callbacks; disposal is safe to invoke repeatedly.</summary>
        public void ReleaseResources()
        {
            initialized = false;
            if (!particles) return;
            Clear();
            if (Application.isPlaying) Destroy(particles.gameObject);
            else DestroyImmediate(particles.gameObject);
            particles = null;
        }
        float Next() => (float)random.NextDouble();
    }
}
