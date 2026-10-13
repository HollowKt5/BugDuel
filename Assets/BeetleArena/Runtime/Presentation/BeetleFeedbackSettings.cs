using TMPro;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>独立设置面板；火星偏好持久保存，不写入比赛规则和种子。</summary>
    public sealed class BeetleFeedbackSettings : MonoBehaviour
    {
        public UnityEngine.UI.Button openButton, closeButton;
        public UnityEngine.UI.Toggle sparksToggle;
        public GameObject panel;
        public HornClashSparks sparks;
        void Awake()
        {
            openButton.onClick.AddListener(Open); closeButton.onClick.AddListener(Close);
            sparksToggle.onValueChanged.AddListener(Change);
            sparksToggle.SetIsOnWithoutNotify(sparks.SparksEnabled); panel.SetActive(false);
        }
        public void Open()
        { sparksToggle.SetIsOnWithoutNotify(sparks.SparksEnabled); panel.SetActive(true); panel.transform.SetAsLastSibling(); }
        public void Close() => panel.SetActive(false);
        void Change(bool value) => sparks.SetEnabled(value);
        void OnDestroy()
        {
            if (openButton) openButton.onClick.RemoveListener(Open);
            if (closeButton) closeButton.onClick.RemoveListener(Close);
            if (sparksToggle) sparksToggle.onValueChanged.RemoveListener(Change);
        }
    }
}
