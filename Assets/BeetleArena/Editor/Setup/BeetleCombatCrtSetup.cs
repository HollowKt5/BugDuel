using UnityEditor;
using BeetleArena;
using System;
using System.IO;
using System.Linq;
using UnityEngine;

public static class BeetleCombatCrtSetup
{
    public const string ProfilePath = "Assets/BeetleArena/Resources/CombatCrt.asset";

    [MenuItem("Tools/Beetle Arena/Combat CRT Settings")]
    static void SelectSettings()
    {
        Selection.activeObject = EnsureProfile();
        EditorGUIUtility.PingObject(Selection.activeObject);
    }

    public static BeetleCrtProfile EnsureProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<BeetleCrtProfile>(ProfilePath);
        if (!profile)
        {
            profile = UnityEngine.ScriptableObject.CreateInstance<BeetleCrtProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
            AssetDatabase.SaveAssets();
        }
        return profile;
    }

    [MenuItem("Tools/Beetle Arena/Apply Steady CRT Preset")]
    public static void ApplySteadyPreset()
    {
        var profile = EnsureProfile();
        Undo.RecordObject(profile, "Apply steady CRT screen treatment");
        // Preserve the user's accepted lens curvature, overall intensity and capture cap.
        profile.scanlineStrength = .12f; profile.scanlineCount = 300;
        profile.chromaticPixels = .85f; profile.phosphorStrength = .06f;
        profile.vignette = .12f; profile.grain = .002f; profile.flicker = 0;
        EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
    }

    // Render a frozen world frame offscreen; never navigate scenes or stop the user's current game.
    public static string PreviewSteadyScreen()
    {
        const string folder = "Artifacts/SteadyCrt";
        Directory.CreateDirectory(folder);
        var shader = Resources.Load<Shader>("BeetleCombatCrt");
        if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
            throw new InvalidOperationException("CRT shader failed to compile.");
        var profile = EnsureProfile();
        var backdrop = UnityEngine.Object.FindFirstObjectByType<BeetleArena.Campaign.CampaignBackdrop>();
        Texture source = backdrop ? backdrop.Frame : null;
        var active = RenderTexture.active;
        var material = new Material(shader);
        Texture2D pattern = null;
        string report = "PASS shader compiled\n";
        try
        {
            if (!source)
            {
                pattern = new Texture2D(640, 360, TextureFormat.RGBA32, false);
                var grid = new Color32[640 * 360];
                for (int y = 0; y < 360; y++) for (int x = 0; x < 640; x++)
                    grid[y * 640 + x] = x % 80 < 3 || y % 60 < 3 ? Color.white : new Color(.12f + x / 1600f, .12f + y / 900f, .18f);
                pattern.SetPixels32(grid); pattern.Apply(); source = pattern;
            }
            report += "Source: " + (pattern ? "generated calibration grid" : "current unfiltered world frame") + "\n";
            float strength = profile.intensity;
            material.SetVector("_Tube", new Vector4(profile.curvature, profile.vignette, profile.chromaticPixels, profile.scanlineStrength) * strength);
            material.SetFloat("_Phosphor", profile.phosphorStrength * strength);
            foreach (int height in new[] { 480, 720, 1080 })
            {
                int width = height * 16 / 9;
                var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                try
                {
                    material.SetVector("_Signal", new Vector4(profile.scanlineCount, profile.grain * strength, profile.flicker * strength, 0));
                    Graphics.Blit(source, target, material);
                    RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                    var first = pixels.GetPixels32();
                    if (first.Where((p, i) => i % 41 == 0).Select(p => (int)p.r + (p.g << 8) + (p.b << 16)).Distinct().Take(17).Count() < 17)
                        throw new InvalidOperationException("CRT preview is blank or invalid.");
                    if (height == 1080) File.WriteAllBytes(folder + "/SteadyScreen.png", pixels.EncodeToPNG());
                    material.SetVector("_Signal", new Vector4(profile.scanlineCount, profile.grain * strength, profile.flicker * strength, 1237.25f));
                    Graphics.Blit(source, target, material);
                    RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                    var second = pixels.GetPixels32();
                    int changed = first.Where((p, i) => !p.Equals(second[i])).Count();
                    if (changed != 0) throw new InvalidOperationException("Default CRT still changes over time: " + changed + " pixels.");
                    report += $"PASS {width}x{height}: rendered, temporal pixel changes = 0\n";
                    if (height == 1080)
                    {
                        Graphics.Blit(source, target); RenderTexture.active = target;
                        pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                        File.WriteAllBytes(folder + "/CleanWorld.png", pixels.EncodeToPNG());
                    }
                }
                finally { RenderTexture.active = active; UnityEngine.Object.DestroyImmediate(pixels); RenderTexture.ReleaseTemporary(target); }
            }
            report += "No gameplay, camera or scene changes.\n";
            File.WriteAllText(folder + "/Result.txt", report);
            return report;
        }
        finally
        {
            RenderTexture.active = active; UnityEngine.Object.DestroyImmediate(material);
            if (pattern) UnityEngine.Object.DestroyImmediate(pattern);
        }
    }
}
