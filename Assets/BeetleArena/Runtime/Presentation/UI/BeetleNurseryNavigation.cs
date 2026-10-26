using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BeetleArena
{
    /// <summary>Scene transition gateway; saving husbandry saves is handled by husbandry application controller.</summary>
    public sealed class BeetleNurseryNavigation : MonoBehaviour
    {
        public const string NurseryScenePath = "Assets/Scenes/BeetleNursery.unity";
        public const string ArenaScenePath = "Assets/Scenes/BeetleArena.unity";
        BeetleFeedbackSettings settings;
        void Awake()=>settings=GetComponent<BeetleFeedbackSettings>();

        public void OpenNursery() => Open(NurseryScenePath);
        public void OpenArena() => Open(ArenaScenePath);

        void Update()
        {
            if (gameObject.scene.path == ArenaScenePath && !(settings&&settings.IsOpen) && Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
                OpenNursery();
        }

        static void Open(string path)
        {
            if (!Application.CanStreamedLevelBeLoaded(path))
            {
                Debug.LogError("Scene is not installed in Build Settings: " + path);
                return;
            }
            SceneManager.LoadScene(path);
        }
    }
}
