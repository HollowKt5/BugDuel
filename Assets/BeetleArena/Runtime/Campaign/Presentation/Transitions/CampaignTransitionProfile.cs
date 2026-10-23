using UnityEngine;

namespace BeetleArena.Campaign
{
    public enum CampaignTransitionMode { Television, BlackFade }

    [CreateAssetMenu(menuName="BugDuel/Campaign Transition Profile")]
    public sealed class CampaignTransitionProfile : ScriptableObject
    {
        public const string ResourceName="CampaignTransition";
        public const string ShutterResourceName="CampaignTransitionShutter";
        [Header("Close: current picture to black")]
        [Min(.05f)] public float closeDuration=.3f;
        [Tooltip("X = normalized time, Y = closing progress. Start at (0,0), end at (1,1).")]
        public AnimationCurve closeCurve=AnimationCurve.EaseInOut(0,0,1,1);
        [Header("Open: black to the next picture")]
        [Min(.05f)] public float openDuration=.55f;
        [Tooltip("X = normalized time, Y = reveal progress. Drives shutters or fading, according to the mode.")]
        public AnimationCurve openCurve=AnimationCurve.EaseInOut(0,0,1,1);
        [Header("Black fade")]
        [Tooltip("Scales both close and open durations for Black Fade only. Television retains the base durations.")]
        [Range(1,10)] public float blackFadeDurationMultiplier=3;
        [Header("Television shutters")]
        [Tooltip("Feather half-width as a fraction of the full screen height. Zero gives a sharp edge.")]
        [Range(0,.15f)] public float shutterSoftness=.025f;
        public Shader shutterShader;
        [Header("Fully black hold")]
        [Tooltip("Minimum fully black hold. Loading and first-frame preparation can extend it.")]
        [Range(0,10)] public float blackHoldDuration=2.06f;
        public float CloseSeconds => Duration(closeDuration,.3f);
        public float OpenSeconds => Duration(openDuration,.55f);
        public float BlackFadeMultiplier => Finite(blackFadeDurationMultiplier)?Mathf.Clamp(blackFadeDurationMultiplier,1,10):3;
        public float ShutterSoftness => Finite(shutterSoftness)?Mathf.Clamp(shutterSoftness,0,.15f):.025f;
        public float BlackHoldSeconds => Finite(blackHoldDuration)?Mathf.Clamp(blackHoldDuration,0,10):2.06f;
        public float CloseSecondsFor(CampaignTransitionMode mode) => CloseSeconds*(mode==CampaignTransitionMode.BlackFade?BlackFadeMultiplier:1);
        public float OpenSecondsFor(CampaignTransitionMode mode) => OpenSeconds*(mode==CampaignTransitionMode.BlackFade?BlackFadeMultiplier:1);
        public float ShutterOpacity(float screenY,float reveal)
        {
            if(reveal<=0)return 1;if(reveal>=1)return 0;
            float soft=ShutterSoftness,distance=Mathf.Abs(screenY-.5f),edge=Mathf.Lerp(-soft,.5f+soft,reveal);
            return soft>0?Mathf.SmoothStep(0,1,Mathf.InverseLerp(edge-soft,edge+soft,distance)):(distance>=edge?1:0);
        }
        static bool Finite(float value) => !float.IsNaN(value)&&!float.IsInfinity(value);
        static float Duration(float value,float fallback) => Finite(value)?Mathf.Clamp(value,.05f,5):fallback;
        public static float Evaluate(AnimationCurve curve,float time)
        {
            if(time<=0)return 0;if(time>=1)return 1;
            float value=curve!=null&&curve.length>0?curve.Evaluate(time):Mathf.SmoothStep(0,1,time);
            return Finite(value)?Mathf.Clamp01(value):Mathf.SmoothStep(0,1,time);
        }
    }
}
