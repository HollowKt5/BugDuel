using System;
using BeetleArena.Campaign;
using UnityEngine;

namespace BeetleArena
{
    public static class RetroSettingsSlider
    {
        public static UnityEngine.UI.Slider Create(RectTransform root, float value, Color trackColor, Color fillColor, Color handleColor, Action<float> changed)
        {
            // Only the full-height root receives pointer events. Decorative children must
            // not consume pointer-down before it reaches Slider (e.g. a bevel handler).
            var hit = root.gameObject.AddComponent<UnityEngine.UI.Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;
            var track = Child("Track", root, new Vector2(14, 22), new Vector2(-14, -22));
            var background = track.gameObject.AddComponent<UnityEngine.UI.Image>();
            background.color = trackColor; background.raycastTarget = false;
            var fill = Child("Fill", track, Vector2.zero, Vector2.zero);
            var fillImage = fill.gameObject.AddComponent<UnityEngine.UI.Image>();
            fillImage.color = fillColor; fillImage.raycastTarget = false;
            if(CampaignRetroTheme.Paper)
            {
                track.gameObject.AddComponent<RetroBevel>().Configure(1.5f,CampaignRetroTheme.PaperOutline,CampaignRetroTheme.PaperOutline);
                fill.offsetMin=new Vector2(2,2);fill.offsetMax=new Vector2(-2,-2);
            }
            var area = Child("Handle Area", root, new Vector2(14, 10), new Vector2(-14, -10));
            var handle = Child("Handle", area, Vector2.zero, Vector2.zero);
            handle.sizeDelta = new Vector2(24, 0);
            var handleImage = handle.gameObject.AddComponent<UnityEngine.UI.Image>();
            handleImage.color = handleColor; handleImage.raycastTarget = false;
            if(CampaignRetroTheme.Paper)handle.gameObject.AddComponent<RetroBevel>().Configure(2,CampaignRetroTheme.LightEdge,CampaignRetroTheme.DarkEdge);
            var slider = root.gameObject.AddComponent<UnityEngine.UI.Slider>();
            slider.minValue = 0; slider.maxValue = 1;
            slider.fillRect = fill; slider.handleRect = handle; slider.targetGraphic = handleImage;
            var colors = slider.colors;
            colors.normalColor = Color.white; colors.highlightedColor = CampaignRetroTheme.HoverTint;
            colors.selectedColor = colors.highlightedColor; colors.pressedColor = CampaignRetroTheme.PressedTint;
            colors.disabledColor = CampaignRetroTheme.DisabledTint;
            slider.colors = colors;
            slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(v => changed(v));
            root.gameObject.AddComponent<RetroUiSliderClick>();
            return slider;
        }

        static RectTransform Child(string name, Transform parent, Vector2 minimum, Vector2 maximum)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = minimum; rect.offsetMax = maximum;
            return rect;
        }
    }
}
