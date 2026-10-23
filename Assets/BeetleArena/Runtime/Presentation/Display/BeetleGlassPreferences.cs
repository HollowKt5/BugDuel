using System;
using UnityEngine;

namespace BeetleArena
{
    public enum BeetleGlassQuality { Realistic=0, Low=1 }

    // Glass quality is global; scene-specific filters retain their independent preferences.
    public static class BeetleGlassPreferences
    {
        public const string QualityKey="BugDuel.Glass.Quality";
        public static BeetleGlassQuality Quality=>(BeetleGlassQuality)PlayerPrefs.GetInt(QualityKey,(int)BeetleGlassQuality.Realistic);
        public static event Action Changed;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()=>Changed=null;
        public static void SetQuality(BeetleGlassQuality quality)
        {
            if(Quality==quality)return;
            PlayerPrefs.SetInt(QualityKey,(int)quality);PlayerPrefs.Save();Changed?.Invoke();
        }
        public static string QualityName(BeetleGlassQuality quality)=>quality==BeetleGlassQuality.Low?"低配":"拟真";
        public static string Description(BeetleGlassQuality quality)=>quality==BeetleGlassQuality.Low?
            "全局生效 · 简化反光，不做折射。":"全局生效 · 反射与轻微折射。";
    }
}
