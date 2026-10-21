using UnityEngine;

namespace BeetleArena
{
    // The CRT samples the world RawImage only. HUD text and button hit areas are not distorted.
    [DisallowMultipleComponent]
    public sealed class BeetleCombatCrt : MonoBehaviour
    {
        public BeetleCrtProfile profile;
        public UnityEngine.UI.RawImage output;
        Material effectMaterial, originalMaterial;
        bool ownsBinding;
        static readonly int Tube = Shader.PropertyToID("_Tube");
        static readonly int Signal = Shader.PropertyToID("_Signal");
        static readonly int Phosphor = Shader.PropertyToID("_Phosphor");

        public bool EffectActive => isActiveAndEnabled && profile && profile.effectEnabled && profile.intensity > 0;

        public static BeetleCombatCrt Attach(UnityEngine.UI.RawImage image)
        {
            if (!image) return null;
            var effect = image.GetComponent<BeetleCombatCrt>() ?? image.gameObject.AddComponent<BeetleCombatCrt>();
            effect.output = image;
            effect.Initialize();
            return effect;
        }

        void OnEnable() => Initialize();

        void Initialize()
        {
            if (!profile) profile = Resources.Load<BeetleCrtProfile>("CombatCrt");
            if (!output) output = GetComponent<UnityEngine.UI.RawImage>();
            if (!output || ownsBinding) return;
            originalMaterial = output.material;
            ownsBinding = true;
        }

        void LateUpdate()
        {
            Initialize();
            if (!output) return;
            if (!EffectActive)
            {
                if (effectMaterial && output.material == effectMaterial) output.material = originalMaterial;
                return;
            }
            if (!effectMaterial)
            {
                // A Resources reference keeps this runtime-created shader in player builds.
                var shader = Resources.Load<Shader>("BeetleCombatCrt");
                if (!shader || !shader.isSupported) return;
                effectMaterial = new Material(shader) { name = "Combat CRT (runtime)", hideFlags = HideFlags.HideAndDontSave };
            }
            output.material = effectMaterial;
            float strength = Mathf.Clamp01(profile.intensity);
            effectMaterial.SetVector(Tube, new Vector4(Mathf.Clamp(profile.curvature, 0, .1f) * strength,
                Mathf.Clamp01(profile.vignette) * strength, Mathf.Clamp(profile.chromaticPixels, 0, 2) * strength,
                Mathf.Clamp01(profile.scanlineStrength) * strength));
            effectMaterial.SetVector(Signal, new Vector4(Mathf.Clamp(profile.scanlineCount, 180, 720),
                Mathf.Clamp(profile.grain, 0, .04f) * strength, Mathf.Clamp(profile.flicker, 0, .025f) * strength,
                profile.flicker > 0 ? Time.unscaledTime : 0));
            effectMaterial.SetFloat(Phosphor, Mathf.Clamp(profile.phosphorStrength, 0, .2f) * strength);
        }

        void OnDisable()
        {
            if (output && effectMaterial && output.material == effectMaterial) output.material = originalMaterial;
            if (effectMaterial) Destroy(effectMaterial);
            effectMaterial = null;
            ownsBinding = false;
        }
    }
}
