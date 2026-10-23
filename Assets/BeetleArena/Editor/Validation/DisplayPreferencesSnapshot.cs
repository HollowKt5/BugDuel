using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BeetleArena;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Validation must leave both legacy preferences and every scene override unchanged.
public sealed class DisplayPreferencesSnapshot : IDisposable
{
    readonly Dictionary<string,int?> values = new Dictionary<string,int?>();
    public DisplayPreferencesSnapshot()
    {
        Capture(BeetleDisplayPreferences.StyleKey);
        Capture(BeetleDisplayPreferences.PixelSizeKey);
        Capture(BeetleGlassPreferences.QualityKey);
        Capture(BeetleHusbandryLightingPreferences.EnabledKey);
        foreach (var scene in EditorBuildSettings.scenes.Select(s=>Path.GetFileNameWithoutExtension(s.path))
                     .Append(SceneManager.GetActiveScene().name).Distinct())
        {
            Capture(BeetleDisplayPreferences.SceneStyleKey(scene));
            Capture(BeetleDisplayPreferences.ScenePixelSizeKey(scene));
            Capture(BeetleDisplayPreferences.SceneDensePixelSizeKey(scene));
        }
    }
    void Capture(string key) => values[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
    public void Dispose()
    {
        foreach (var pair in values)
            if (pair.Value.HasValue) PlayerPrefs.SetInt(pair.Key,pair.Value.Value);
            else PlayerPrefs.DeleteKey(pair.Key);
        PlayerPrefs.Save();
    }
}
