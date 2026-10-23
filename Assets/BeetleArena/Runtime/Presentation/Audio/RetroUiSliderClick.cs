using UnityEngine;
using UnityEngine.EventSystems;

namespace BeetleArena
{
    /// <summary>One tick on slider press, never on every drag/value-change frame.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(UnityEngine.UI.Slider))]
    public sealed class RetroUiSliderClick : MonoBehaviour, IPointerDownHandler
    {
        UnityEngine.UI.Slider slider;
        void Awake() => slider = GetComponent<UnityEngine.UI.Slider>();
        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && slider && slider.IsActive() && slider.IsInteractable())
                RetroUiAudio.PlayClick();
        }
    }
}
