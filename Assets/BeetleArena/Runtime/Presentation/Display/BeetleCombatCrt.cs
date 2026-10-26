using UnityEngine;

namespace BeetleArena
{
    // One world surface owns one selected treatment. HUD text and hit areas stay untouched.
    // Keep the component name so existing scenes and external tooling retain their bindings.
    [DisallowMultipleComponent]
    public sealed class BeetleCombatCrt : MonoBehaviour
    {
        public BeetleCrtProfile profile;
        public UnityEngine.UI.RawImage output;
        Material effectMaterial, ditherMaterial, ditherSource, customMaterial, customSource, originalMaterial;
        string sceneName;
        BeetleDisplayStyle ditherStyle;
        bool ownsBinding;
        static readonly int Tube = Shader.PropertyToID("_Tube");
        static readonly int Signal = Shader.PropertyToID("_Signal");
        static readonly int Phosphor = Shader.PropertyToID("_Phosphor");
        static readonly int PixelSize = Shader.PropertyToID("_PixelSize");

        public bool CrtActive => isActiveAndEnabled && BeetleDisplayPreferences.GetStyle(sceneName) == BeetleDisplayStyle.Crt && profile && profile.effectEnabled && profile.intensity > 0;
        public bool DitherActive => isActiveAndEnabled && BeetleDisplayPreferences.IsDither(BeetleDisplayPreferences.GetStyle(sceneName));
        public bool CustomActive => isActiveAndEnabled && BeetleDisplayPreferences.GetStyle(sceneName) == BeetleDisplayStyle.Custom && BeetleDisplayPreferences.GetCustomMaterial(sceneName);
        public bool EffectActive => CrtActive || DitherActive || CustomActive;

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
            if(sceneName==null)sceneName=gameObject.scene.name;
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
                RestoreMaterial();
                return;
            }
            if(CustomActive)
            {
                var source=BeetleDisplayPreferences.GetCustomMaterial(sceneName);
                if(customSource!=source || !customMaterial || customMaterial.shader!=source.shader)
                {
                    if(customMaterial)Destroy(customMaterial);
                    customSource=source;customMaterial=new Material(source){name="World effect (runtime)",hideFlags=HideFlags.HideAndDontSave};
                }
                // Inspector edits update the live clone without changing the authored material.
                customMaterial.CopyPropertiesFromMaterial(source);
                if(customMaterial.HasProperty(PixelSize))customMaterial.SetFloat(PixelSize,BeetleDisplayPreferences.GetPixelSize(sceneName));
                output.material=customMaterial;return;
            }
            if (DitherActive)
            {
                var style=BeetleDisplayPreferences.GetStyle(sceneName);
                var source=BeetleDisplayPreferences.GetDitherMaterial(sceneName);
                if (!ditherMaterial || ditherStyle!=style || ditherSource!=source || (source&&ditherMaterial.shader!=source.shader))
                {
                    var shader = source?source.shader:Resources.Load<Shader>(style==BeetleDisplayStyle.DenseDither?"BeetleDenseDither":"BeetleCombatDither");
                    if (!shader || !shader.isSupported) return;
                    if(ditherMaterial)Destroy(ditherMaterial);
                    ditherSource=source;ditherStyle=style;
                    ditherMaterial = source?new Material(source):new Material(shader);
                    ditherMaterial.name="World ordered dither (runtime)";ditherMaterial.hideFlags=HideFlags.HideAndDontSave;
                }
                if(source)ditherMaterial.CopyPropertiesFromMaterial(source);
                // Direct selection, never a second pass through the CRT material.
                output.material = ditherMaterial;
                ditherMaterial.SetFloat(PixelSize, BeetleDisplayPreferences.DitherPixelScale(BeetleDisplayPreferences.GetPixelSize(sceneName)));
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
            RestoreMaterial();
            if (effectMaterial) Destroy(effectMaterial);
            if (ditherMaterial) Destroy(ditherMaterial);
            if (customMaterial) Destroy(customMaterial);
            effectMaterial = null; ditherMaterial = null;ditherSource=null;customMaterial=null;customSource=null;
            ownsBinding = false;
        }

        void RestoreMaterial()
        {
            if (output && ((effectMaterial && output.material == effectMaterial) || (ditherMaterial && output.material == ditherMaterial) || (customMaterial && output.material==customMaterial)))
                output.material = originalMaterial;
        }
    }
}
