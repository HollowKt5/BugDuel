using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BeetleArena
{
    [DefaultExecutionOrder(-200)]
    public sealed class RetroUiNavigation : MonoBehaviour
    {
        public static bool PointerMode { get; private set; } = true;
        static RetroUiNavigation instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { instance = null; PointerMode = true; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Ensure()
        {
            if (instance) return;
            instance = new GameObject("UI Input Mode").AddComponent<RetroUiNavigation>();
            DontDestroyOnLoad(instance.gameObject);
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > .01f ||
                mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.scroll.ReadValue().sqrMagnitude > .01f))
                PointerMode = true;
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            bool navigate = keyboard != null && (keyboard.tabKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame ||
                keyboard.downArrowKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame) ||
                pad != null && (pad.dpad.ReadValue().sqrMagnitude > .1f || pad.leftStick.ReadValue().sqrMagnitude > .25f || pad.buttonSouth.wasPressedThisFrame);
            if (!navigate) return;
            PointerMode = false;
            var events = EventSystem.current;
            if (!events || events.currentSelectedGameObject) return;
            foreach (var control in Selectable.allSelectablesArray)
                if (control is Button && control.IsActive() && control.IsInteractable())
                { events.SetSelectedGameObject(control.gameObject); break; }
        }

        void LateUpdate()
        {
            var events = EventSystem.current;
            // Text fields and sliders retain editing focus. Mouse clicks do not leave
            // an action button latched after a modal or page has closed.
            if (PointerMode && events && events.currentSelectedGameObject && events.currentSelectedGameObject.GetComponent<Button>())
                events.SetSelectedGameObject(null);
        }
    }
}
