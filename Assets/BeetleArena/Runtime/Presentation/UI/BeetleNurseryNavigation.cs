using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BeetleArena
{
    /// <summary>场景切换入口；养殖档案的保存由养殖应用控制器负责。</summary>
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
