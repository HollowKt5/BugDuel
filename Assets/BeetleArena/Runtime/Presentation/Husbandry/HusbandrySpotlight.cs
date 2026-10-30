using System.Collections;
using BeetleArena.Campaign;
using DG.Tweening;
using UnityEngine;

namespace BeetleArena
{
    // The real light illuminates the enclosure. Its ground projection preserves the
    // broad, soft pool without overexposing the soil to brighten a dark backdrop.
    [ExecuteAlways, RequireComponent(typeof(Light))]
    public sealed class HusbandrySpotlight : MonoBehaviour
    {
        public Renderer backdrop;
        public MeshRenderer groundPool;
        [Min(0),Tooltip("Zero uses the full light cone; a desk can limit the visible projection.")]
        public float poolDiameterLimit;
        Light source;
        MaterialPropertyBlock properties;
        Sequence flicker;
        float brightness, authoredIntensity;
        bool entered;

        void OnEnable()
        {
            source = GetComponent<Light>();
            authoredIntensity = source.intensity;
            entered = false;
            BeetleHusbandryLightingPreferences.Changed += ApplyPreference;
            Apply();
            if (Application.isPlaying) StartCoroutine(Enter());
        }
        void OnDisable()
        {
            BeetleHusbandryLightingPreferences.Changed -= ApplyPreference;
            StopAllCoroutines();
            flicker?.Kill(); flicker = null;
            SetBrightness(0);
            source.intensity = authoredIntensity;
        }
        // ExecuteAlways updates on scene edits; no polling is needed during gameplay.
        void Update() { if (!Application.isPlaying) Apply(); }
        public void Apply()
        {
            if (!groundPool || !backdrop) return; // The editor assigns these after AddComponent.
            float groundY = backdrop.bounds.max.y + .005f;
            float height = source.transform.position.y - groundY;
            float outer = Mathf.Tan(source.spotAngle * .5f * Mathf.Deg2Rad);
            float inner = Mathf.Tan(source.innerSpotAngle * .5f * Mathf.Deg2Rad);
            groundPool.transform.SetPositionAndRotation(new Vector3(transform.position.x, groundY, transform.position.z), Quaternion.Euler(90, 0, 0));
            float diameter=height*outer*2;
            if(poolDiameterLimit>0)diameter=Mathf.Min(diameter,poolDiameterLimit);
            groundPool.transform.localScale = new Vector3(diameter,diameter,1);
            properties ??= new MaterialPropertyBlock();
            properties.SetFloat("_InnerRadius", Mathf.Min(inner / outer, .999f));
            if (!Application.isPlaying) authoredIntensity = source.intensity;
            SetBrightness(Application.isPlaying ? brightness : 1);
        }
        IEnumerator Enter()
        {
            SetBrightness(0);
            yield return null;
            var transition = FindFirstObjectByType<CampaignSceneTransition>();
            var world = FindFirstObjectByType<CampaignBackdrop>();
            // Wait once for the cover to clear, so both flashes are actually visible.
            while ((transition && transition.Busy) || (world && !world.Ready)) yield return null;
            entered = true;
            if (!BeetleHusbandryLightingPreferences.Enabled) yield break;
            flicker = DOTween.Sequence().SetUpdate(true).SetTarget(this);
            for (int i = 0; i < 2; i++)
            {
                flicker.Append(DOTween.To(() => brightness, SetBrightness, .85f, .07f).SetEase(Ease.OutQuad));
                flicker.Append(DOTween.To(() => brightness, SetBrightness, 0, .08f).SetEase(Ease.InQuad));
                flicker.AppendInterval(.12f);
            }
            flicker.Append(DOTween.To(() => brightness, SetBrightness, 1, .24f).SetEase(Ease.OutQuad));
            flicker.OnComplete(() => flicker = null);
        }
        void ApplyPreference()
        {
            flicker?.Kill(); flicker = null;
            SetBrightness(entered && BeetleHusbandryLightingPreferences.Enabled ? 1 : 0);
        }
        void SetBrightness(float value)
        {
            brightness = value;
            source.enabled = value > 0;
            source.intensity = authoredIntensity * value;
            if (!groundPool) return;
            groundPool.enabled = value > 0;
            properties ??= new MaterialPropertyBlock();
            properties.SetFloat("_Fade", value);
            groundPool.SetPropertyBlock(properties);
        }
    }
}
