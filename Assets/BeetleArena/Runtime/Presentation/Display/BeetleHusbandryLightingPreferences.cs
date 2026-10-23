using System;
using UnityEngine;

namespace BeetleArena
{
    public static class BeetleHusbandryLightingPreferences
    {
        public const string EnabledKey = "BugDuel.Husbandry.SpotlightEnabled";
        public static bool Enabled => PlayerPrefs.GetInt(EnabledKey, 1) != 0;
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => Changed = null;

        public static void SetEnabled(bool enabled)
        {
            if (Enabled == enabled) return;
            PlayerPrefs.SetInt(EnabledKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
