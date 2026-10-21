using UnityEngine;

namespace BeetleArena
{
    // Shared presentation settings. Match simulation and input never depend on this asset.
    [CreateAssetMenu(menuName = "Beetle Arena/Combat CRT Profile")]
    public sealed class BeetleCrtProfile : ScriptableObject
    {
        public bool effectEnabled = true;
        [Range(0, 1)] public float intensity = .85f;
        [Range(0, .4f)] public float scanlineStrength = .12f;
        [Range(180, 720)] public float scanlineCount = 300;
        [Tooltip("Stable horizontal red/blue displacement in output pixels; weaker in the center.")]
        [Range(0, 2)] public float chromaticPixels = .85f;
        [Tooltip("Stationary RGB phosphor texture. Does not displace the image or animate.")]
        [Range(0, .2f)] public float phosphorStrength = .06f;
        [Range(0, .1f)] public float curvature = .025f;
        [Range(0, .5f)] public float vignette = .12f;
        [Tooltip("Static multiplicative grain; never lifts black levels or changes between frames.")]
        [Range(0, .04f)] public float grain = .002f;
        [Tooltip("Optional slow brightness variation; zero keeps the screen completely steady.")]
        [Range(0, .025f)] public float flicker = 0;
        [Tooltip("Standalone combat capture cap; campaign uses its existing backdrop resolution.")]
        [Range(640, 2560)] public int maximumWidth = 1600;
    }
}
