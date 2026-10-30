using System;
using BeetleArena.Campaign;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>
    /// Presents the campaign's 360-day year on the tree outside the window. Calendar time controls
    /// the season; unscaled presentation time keeps the breeze and weather alive while time is paused.
    /// This component never advances or writes to the campaign clock.
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(50)]
    public sealed class HusbandrySeasons : MonoBehaviour
    {
        public HusbandryDaylight daylight;
        public Renderer[] foliage = Array.Empty<Renderer>();
        public Renderer[] snowCaps = Array.Empty<Renderer>();
        public Renderer[] buds = Array.Empty<Renderer>();
        public ParticleSystem fallingLeaves, snowflakes, petals;
        [Range(0, 360)] public float previewDay = 20;

        /// <summary>0 spring, 1 summer, 2 autumn, 3 winter, matching the campaign calendar.</summary>
        public int Season { get; private set; }
        /// <summary>Zero-based day, wrapped into [0, 360), including the time of day.</summary>
        public float DayOfYear { get; private set; }
        public float LeafCoverage { get; private set; }
        public float SnowAmount { get; private set; }
        public float FogAmount { get; private set; }

        static readonly int TintId = Shader.PropertyToID("_SeasonTint");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int LegacyColorId = Shader.PropertyToID("_Color");
        static readonly int CoverageId = Shader.PropertyToID("_LeafCoverage");
        static readonly int SnowId = Shader.PropertyToID("_SnowAmount");
        static readonly int WindId = Shader.PropertyToID("_WindStrength");
        static readonly int DaylightId = Shader.PropertyToID("_Daylight");
        static readonly int WindTimeId = Shader.PropertyToID("_WindTime");
        static readonly int OutdoorFogId = Shader.PropertyToID("_OutdoorFog");

        static readonly Color Spring = new Color(.6764f, .89f, .4984f);
        static readonly Color Summer = new Color(.4272f, .7031f, .3827f);
        static readonly Color Autumn = new Color(.89f, .4717f, .1513f);
        static readonly Color LateAutumn = new Color(.6141f, .2848f, .1068f);
        static readonly Color Winter = new Color(.5429f, .4272f, .267f);
        static readonly Color Snow = new Color(.91f, .96f, 1f);
        static readonly Color Blossom = new Color(1f, .77f, .80f);

        readonly RendererGroup canopy = new RendererGroup();
        readonly RendererGroup branchSnow = new RendererGroup();
        readonly RendererGroup springBuds = new RendererGroup();
        readonly WeatherEffect leavesEffect = new WeatherEffect();
        readonly WeatherEffect snowEffect = new WeatherEffect();
        readonly WeatherEffect petalEffect = new WeatherEffect();
        Renderer fogSky;
        MaterialPropertyBlock fogProperties;
        float originalFog;
        bool ownsFog;

        void OnEnable() => Update();

        void Update()
        {
            var clock = CampaignApp.Instance?.Session?.State.clock;
            double days = clock != null ? clock.elapsedDays : previewDay + (daylight ? daylight.ElapsedDays : 0);
            PresentDay(days);
        }

        /// <summary>Refreshes per-material baselines after replacing a model or editing its material slots.</summary>
        public void RefreshModelBindings()
        {
            canopy.Restore(); branchSnow.Restore(); springBuds.Restore();
            canopy.Bind(foliage); branchSnow.Bind(snowCaps); springBuds.Bind(buds);
        }

        /// <summary>
        /// Samples a date without modifying game state. Useful for scene previews and deterministic
        /// seasonal checks; the next runtime Update resumes reading the live calendar.
        /// </summary>
        public void PresentDay(double elapsedDays)
        {
            if (double.IsNaN(elapsedDays) || double.IsInfinity(elapsedDays)) elapsedDays = 0;
            FogAmount = FogAtDay(elapsedDays);
            PresentFog();
            DayOfYear = Mathf.Repeat((float)(elapsedDays % 360d), 360);
            Season = Mathf.Min(3, (int)(DayOfYear / 90));
            float day = DayOfYear;

            // The year boundary is a continuous transition from winter buds into fresh spring growth.
            if (day < 36) LeafCoverage = Mathf.Lerp(.55f, 1, Smooth(0, 36, day));
            else if (day < 180) LeafCoverage = 1;
            else if (day < 270) LeafCoverage = Mathf.Lerp(1, .035f, Smooth(180, 270, day));
            else LeafCoverage = Mathf.Lerp(.035f, .55f, Smooth(338, 360, day));

            if (day < 260) SnowAmount = 0;
            else if (day < 303) SnowAmount = Smooth(260, 303, day);
            else SnowAmount = 1 - Smooth(330, 360, day);

            Color tint = CanopyTint(day);
            float light = daylight ? Mathf.Clamp01(daylight.Daylight) : 1;
            float windTime = Application.isPlaying ? Time.unscaledTime : 0;
            float wind = (.10f + .03f * Mathf.Cos((day - 220) * Mathf.PI * 2 / 360)) * (1 - SnowAmount * .35f);
            // Buds return before the year wraps; tiny branch clusters then fade as the canopy fills in.
            float bloom = day < 70 ? 1 - Smooth(30, 70, day) : Smooth(344, 360, day);

            canopy.Bind(foliage);
            branchSnow.Bind(snowCaps);
            springBuds.Bind(buds);
            canopy.Present(tint, LeafCoverage, SnowAmount * .6f, wind, light, windTime, false);
            branchSnow.Present(Snow, SnowAmount, SnowAmount, 0, light, windTime, true, .15f);
            springBuds.Present(Blossom, bloom, 0, wind * .45f, light, windTime, true);

            float leafRate = 3.8f * Envelope(day, 180, 199, 242, 270);
            float snowRate = 13f * Envelope(day, 270, 286, 339, 360);
            float petalRate = 1.5f * Envelope(day, 3, 16, 43, 67);
            leavesEffect.Present(fallingLeaves, leafRate, Color.Lerp(Autumn, LateAutumn, Smooth(210, 265, day)), light);
            snowEffect.Present(snowflakes, snowRate, Snow, light);
            petalEffect.Present(petals, petalRate, Blossom, light);
        }

        /// <summary>
        /// Deterministic morning mist on roughly one day in six. The complete date participates in the
        /// hash, so weather need not repeat each year. No random generator or saved state is involved.
        /// </summary>
        public static float FogAtDay(double elapsedDays)
        {
            if (double.IsNaN(elapsedDays) || double.IsInfinity(elapsedDays) || elapsedDays < 0 || elapsedDays >= long.MaxValue) return 0;
            double wholeDay = Math.Floor(elapsedDays);
            uint hash;
            unchecked
            {
                long date = (long)wholeDay;
                hash = (uint)date ^ (uint)(date >> 32) ^ 0x9e3779b9u;
                hash ^= hash >> 16; hash *= 0x7feb352du;
                hash ^= hash >> 15; hash *= 0x846ca68bu;
                hash ^= hash >> 16;
            }
            if (hash % 6 != 0) return 0;
            float hour = (float)((elapsedDays - wholeDay) * 24);
            float strength = .52f + .22f * ((hash >> 8) & 255) / 255;
            // Exactly zero around midnight prevents a discontinuity when tomorrow's weather differs.
            return strength * Envelope(hour, 3.5f, 6, 7.25f, 11.5f);
        }

        void PresentFog()
        {
            var sky = daylight ? daylight.sky : null;
            if (fogSky != sky)
            {
                RestoreFog();
                fogSky = sky;
            }
            if (!fogSky) return;
            if (fogProperties == null) fogProperties = new MaterialPropertyBlock();
            // HusbandryDaylight also writes this renderer. Merge its latest hour/light values every frame.
            fogSky.GetPropertyBlock(fogProperties);
            if (!ownsFog)
            {
                var material = fogSky.sharedMaterial;
                originalFog = fogProperties.HasFloat(OutdoorFogId) ? fogProperties.GetFloat(OutdoorFogId) :
                    material && material.HasProperty(OutdoorFogId) ? material.GetFloat(OutdoorFogId) : 0;
                ownsFog = true;
            }
            fogProperties.SetFloat(OutdoorFogId, FogAmount);
            fogSky.SetPropertyBlock(fogProperties);
        }

        void RestoreFog()
        {
            if (ownsFog && fogSky)
            {
                fogSky.GetPropertyBlock(fogProperties);
                fogProperties.SetFloat(OutdoorFogId, originalFog);
                fogSky.SetPropertyBlock(fogProperties);
            }
            fogSky = null; ownsFog = false;
        }

        static float Smooth(float from, float to, float value) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(from, to, value));
        static float Envelope(float day, float begin, float full, float fade, float end) =>
            Smooth(begin, full, day) * (1 - Smooth(fade, end, day));

        static Color CanopyTint(float day)
        {
            if (day < 120) return Color.Lerp(Spring, Summer, Smooth(38, 120, day));
            if (day < 218) return Color.Lerp(Summer, Autumn, Smooth(164, 218, day));
            if (day < 267) return Color.Lerp(Autumn, LateAutumn, Smooth(218, 267, day));
            if (day < 320) return Color.Lerp(LateAutumn, Winter, Smooth(267, 300, day));
            return Color.Lerp(Winter, Spring, Smooth(338, 360, day));
        }

        void OnDisable()
        {
            canopy.Restore(); branchSnow.Restore(); springBuds.Restore();
            leavesEffect.Stop(); snowEffect.Stop(); petalEffect.Stop();
            RestoreFog();
        }

        // Each group owns only the renderers explicitly assigned to it. Material assets remain shared;
        // original property blocks and renderer visibility are restored when the component is disabled.
        sealed class RendererGroup
        {
            Renderer[] source;
            Entry[] entries = Array.Empty<Entry>();

            struct Entry
            {
                public Renderer renderer;
                public bool enabled;
                public bool coverageInShader;
                public float visibilityThreshold;
                public Slot[] slots;
            }

            sealed class Slot
            {
                public MaterialPropertyBlock original, current;
                public bool indexed, seasonTint, coverage;
                public int colorId = -1;
                public Color baseColor;
            }

            public void Bind(Renderer[] renderers)
            {
                if (BindingsMatch(renderers)) return;
                Restore();
                source = renderers;
                if (renderers == null || renderers.Length == 0) return;
                entries = new Entry[renderers.Length];
                int count = 0, ordinal = 0;
                for (int i = 0; i < renderers.Length; i++) if (renderers[i]) count++;
                for (int i = 0; i < renderers.Length; i++)
                {
                    var renderer = renderers[i];
                    if (!renderer) continue;
                    // sharedMaterials allocates only when bindings change, never during Present.
                    var materials = renderer.sharedMaterials;
                    var entry = new Entry
                    {
                        renderer = renderer,
                        enabled = renderer.enabled,
                        coverageInShader = materials.Length > 0,
                        visibilityThreshold = count <= 1 ? .08f : Mathf.Lerp(.08f, .92f, (float)ordinal / (count - 1)),
                        slots = new Slot[materials.Length]
                    };
                    ordinal++;
                    for (int slotIndex = 0; slotIndex < materials.Length; slotIndex++)
                    {
                        var material = materials[slotIndex];
                        var slot = new Slot { original = new MaterialPropertyBlock(), current = new MaterialPropertyBlock() };
                        renderer.GetPropertyBlock(slot.original, slotIndex);
                        // Keep the common single-material path renderer-wide, while respecting an existing
                        // per-material block (which takes precedence over the renderer-wide block in Unity).
                        slot.indexed = materials.Length > 1 || !slot.original.isEmpty;
                        if (!slot.indexed) renderer.GetPropertyBlock(slot.original);
                        if (slot.indexed && !slot.original.isEmpty) renderer.GetPropertyBlock(slot.current, slotIndex);
                        else renderer.GetPropertyBlock(slot.current);
                        slot.seasonTint = material && material.HasProperty(TintId);
                        slot.coverage = material && material.HasProperty(CoverageId);
                        entry.coverageInShader &= slot.coverage;
                        if (material && material.HasProperty(BaseColorId)) slot.colorId = BaseColorId;
                        else if (material && material.HasProperty(LegacyColorId)) slot.colorId = LegacyColorId;
                        if (slot.colorId != -1)
                            slot.baseColor = slot.current.HasColor(slot.colorId) ? slot.current.GetColor(slot.colorId) : material.GetColor(slot.colorId);
                        entry.slots[slotIndex] = slot;
                    }
                    entries[i] = entry;
                }
            }

            bool BindingsMatch(Renderer[] renderers)
            {
                if (!ReferenceEquals(source, renderers)) return false;
                if (renderers == null) return true;
                if (entries.Length != renderers.Length) return false;
                for (int i = 0; i < renderers.Length; i++) if (entries[i].renderer != renderers[i]) return false;
                return true;
            }

            public void Present(Color tint, float coverage, float snow, float wind, float light, float time, bool hideWhenEmpty, float visibilityThreshold = .005f)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    var entry = entries[i];
                    if (!entry.renderer) continue;
                    for (int slotIndex = 0; slotIndex < entry.slots.Length; slotIndex++)
                    {
                        var slot = entry.slots[slotIndex];
                        var block = slot.current;
                        if (slot.seasonTint) block.SetColor(TintId, tint);
                        if (slot.colorId != -1 && (hideWhenEmpty || !slot.seasonTint))
                            block.SetColor(slot.colorId, hideWhenEmpty ? tint : slot.baseColor * tint);
                        if (slot.coverage) block.SetFloat(CoverageId, coverage);
                        block.SetFloat(SnowId, snow); block.SetFloat(WindId, wind);
                        block.SetFloat(DaylightId, light); block.SetFloat(WindTimeId, time);
                        if (slot.indexed) entry.renderer.SetPropertyBlock(block, slotIndex);
                        else entry.renderer.SetPropertyBlock(block);
                    }
                    // A scene saved in summer can legitimately contain disabled snow-cap renderers;
                    // seasonal visibility must be able to turn those back on in winter.
                    if (hideWhenEmpty) entry.renderer.enabled = coverage > visibilityThreshold;
                    else if (!entry.coverageInShader) entry.renderer.enabled = entry.enabled && coverage > entry.visibilityThreshold;
                }
            }

            public void Restore()
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    var entry = entries[i];
                    if (!entry.renderer) continue;
                    int materialCount = entry.renderer.sharedMaterials.Length;
                    for (int slotIndex = 0; slotIndex < entry.slots.Length; slotIndex++)
                    {
                        var slot = entry.slots[slotIndex];
                        var original = slot.original.isEmpty ? null : slot.original;
                        if (!slot.indexed) entry.renderer.SetPropertyBlock(original);
                        else if (slotIndex < materialCount) entry.renderer.SetPropertyBlock(original, slotIndex);
                    }
                    entry.renderer.enabled = entry.enabled;
                }
                source = null;
                entries = Array.Empty<Entry>();
            }
        }

        sealed class WeatherEffect
        {
            ParticleSystem source;
            ParticleSystemRenderer renderer;
            MaterialPropertyBlock original, current;
            float lastRate = -1;
            float lastLight = -1;
            Color lastColor;

            public void Present(ParticleSystem particles, float rate, Color color, float light)
            {
                if (source != particles)
                {
                    Stop(); source = particles; lastRate = -1;
                    if (source)
                    {
                        var setup = source.main;
                        setup.useUnscaledTime = true;
                        renderer = source.GetComponent<ParticleSystemRenderer>();
                        if (renderer)
                        {
                            original = new MaterialPropertyBlock();
                            current = new MaterialPropertyBlock();
                            renderer.GetPropertyBlock(original);
                            renderer.GetPropertyBlock(current);
                        }
                    }
                }
                if (!source) return;
                // Lighting is a renderer uniform so already-emitted particles change with the sky too.
                // Baking daylight into startColor would leave bright daytime snow alive all night at high speeds.
                if (renderer && lastLight != light)
                {
                    current.SetFloat(DaylightId, light);
                    renderer.SetPropertyBlock(current);
                    lastLight = light;
                }
                bool active = rate > .01f;
                var emission = source.emission;
                if (emission.enabled != active) emission.enabled = active;
                if (Mathf.Abs(lastRate - rate) > .001f)
                {
                    emission.rateOverTime = rate;
                    lastRate = rate;
                }
                if (lastColor != color)
                {
                    var main = source.main;
                    main.startColor = color;
                    lastColor = color;
                }
                if (active)
                {
                    if (Application.isPlaying && !source.isPlaying) source.Play(false);
                }
                else if (source.isPlaying || source.particleCount > 0)
                    source.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            public void Stop()
            {
                if (source) source.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (renderer) renderer.SetPropertyBlock(original.isEmpty ? null : original);
                source = null; renderer = null; original = null; current = null;
                lastRate = -1; lastLight = -1; lastColor = default;
            }
        }
    }
}
