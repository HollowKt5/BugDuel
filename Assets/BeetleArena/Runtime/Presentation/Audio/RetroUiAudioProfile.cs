using UnityEngine;

namespace BeetleArena
{
    [CreateAssetMenu(menuName = "Beetle Arena/UI Click Sound Profile")]
    public sealed class RetroUiAudioProfile : ScriptableObject
    {
        public const string ResourceName = "UiClickSound";
        [Range(0, 1), Tooltip("点击音量；游戏中还会乘以主音量。")]
        public float volume = MouseClickSettings.Default.Volume;
        [Range(.65f, 2), Tooltip("越大越尖；只改变音色频率，不改变回弹间隔。")]
        public float pitch = MouseClickSettings.Default.Pitch;
        [Range(0, 1), Tooltip("提高高频敲击成分，让声音更明亮、清脆。")]
        public float brightness = MouseClickSettings.Default.Brightness;
        [Range(.02f, .4f), Tooltip("起音时间（毫秒）。越小，第一下敲击越硬。")]
        public float attackMs = MouseClickSettings.Default.AttackMs;
        [Range(.4f, 4), Tooltip("共振衰减时间（毫秒）。越小越干脆，越大尾音越明显。")]
        public float decayMs = MouseClickSettings.Default.DecayMs;
        [Range(0, 1), Tooltip("摩擦噪声比例。小值更像硬质敲击，大值更粗糙。")]
        public float noise = MouseClickSettings.Default.Noise;
        [Range(0, 150), Tooltip("“咯”到“哒”的起音间隔（毫秒）。默认 60；间隔太小会听成一声，增大后两段更分明。")]
        public float reboundMs = MouseClickSettings.Default.ReboundMs;
        [Range(0, 1), Tooltip("第二下回弹的力度；0 为单次敲击。")]
        public float reboundStrength = MouseClickSettings.Default.ReboundStrength;

        public MouseClickSettings Settings => new MouseClickSettings(volume, pitch, brightness, attackMs, decayMs,
            noise, reboundMs, reboundStrength);

        public void ResetToDefaults() => Apply(MouseClickSettings.Default);
        void OnValidate() => Apply(Settings);
        void Apply(MouseClickSettings settings)
        {
            volume = settings.Volume; pitch = settings.Pitch; brightness = settings.Brightness;
            attackMs = settings.AttackMs; decayMs = settings.DecayMs; noise = settings.Noise;
            reboundMs = settings.ReboundMs; reboundStrength = settings.ReboundStrength;
        }
    }
}
