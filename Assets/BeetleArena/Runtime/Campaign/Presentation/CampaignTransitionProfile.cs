using UnityEngine;

namespace BeetleArena.Campaign
{
    public enum CampaignTransitionMode { Television, BlackFade }

    [CreateAssetMenu(menuName="BugDuel/Campaign Transition Profile")]
    public sealed class CampaignTransitionProfile : ScriptableObject
    {
        public const string ResourceName="CampaignTransition";
        [Header("Close: current picture to black")]
        [Min(.05f)] public float closeDuration=.3f;
        [Tooltip("X = normalized time, Y = black opacity. Start at (0,0), end at (1,1).")]
        public AnimationCurve closeCurve=AnimationCurve.EaseInOut(0,0,1,1);
        [Header("Open: black to the next picture")]
        [Min(.05f)] public float openDuration=.55f;
        [Tooltip("X = normalized time, Y = reveal progress. Drives shutters or fading, according to the mode.")]
        public AnimationCurve openCurve=AnimationCurve.EaseInOut(0,0,1,1);
        [Tooltip("Minimum fully black hold. Loading and first-frame preparation can extend it.")]
        [Range(0,2)] public float blackHoldDuration=.06f;
        public float CloseSeconds => Duration(closeDuration,.3f);
        public float OpenSeconds => Duration(openDuration,.55f);
        public float BlackHoldSeconds => Finite(blackHoldDuration)?Mathf.Clamp(blackHoldDuration,0,2):.06f;
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
