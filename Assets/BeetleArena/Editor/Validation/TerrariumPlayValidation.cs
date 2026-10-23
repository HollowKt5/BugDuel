using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BeetleArena;
using BeetleArena.Campaign;
using BugDuel.Campaign;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using CampaignSpecimen = BeetleArena.Campaign.CampaignSpecimen;

/// <summary>Runs the actual care view with isolated saves; captures glass in clean and filtered rendering.</summary>
[InitializeOnLoad]
public static class TerrariumPlayValidation
{
    const string Key = "BugDuel.TerrariumReview";
    public const string Output = "Artifacts/Terrarium";
    public static bool GlassOnly => SessionState.GetBool(Key + ".glass", false);
    public static bool LightingOnly => SessionState.GetBool(Key + ".lighting", false);
    public static string Report => Output + (LightingOnly ? "/LightingPlay.txt" : GlassOnly ? "/GlassPlay.txt" : "/Play.txt");
    public static void RunGlassOnly() { SessionState.SetBool(Key + ".glass", true); Run(); }
    public static void RunLightingOnly() { SessionState.SetBool(Key + ".lighting", true); Run(); }
    static TerrariumPlayValidation()
    {
        var storage = SessionState.GetString(Key, "");
        if (storage.Length > 0) CampaignApp.StorageOverride = storage;
        EditorApplication.playModeStateChanged += OnPlay;
        EditorApplication.update += () => { if (SessionState.GetString(Key, "").Length > 0 && EditorApplication.isPlaying) EditorApplication.QueuePlayerLoopUpdate(); };
    }
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before isolated verification.");
        Directory.CreateDirectory(Output);
        var storage = Path.GetFullPath(Output + "/Save-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var session = CampaignSession.New(CampaignCatalog.Defaults(), 84517);
        session.RenamePlayer("林间饲育员");
        foreach (var beetle in session.State.beetles) { beetle.nutrition = 100; beetle.overturned = false; }
        new CampaignSaveRepository(storage).Write(0, new CampaignSave { state = session.State, catalog = session.Catalog });
        File.WriteAllText(Report, "TERRARIUM PLAY REVIEW\n");
        SessionState.SetString(Key + ".oldStorage", CampaignApp.StorageOverride ?? "");
        SessionState.SetString(Key + ".start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetString(Key, storage); CampaignApp.StorageOverride = storage;
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Campaign/CampaignMainMenu.unity");
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Show();
        EditorApplication.isPlaying = true;
    }
    static void OnPlay(PlayModeStateChange state)
    {
        if (SessionState.GetString(Key, "").Length == 0) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            var host = new GameObject("Terrarium review"); UnityEngine.Object.DontDestroyOnLoad(host); host.AddComponent<TerrariumPlayDriver>();
        }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key + ".start", ""));
        var old = SessionState.GetString(Key + ".oldStorage", ""); CampaignApp.StorageOverride = old.Length == 0 ? null : old;
        SessionState.EraseString(Key); SessionState.EraseString(Key + ".start"); SessionState.EraseString(Key + ".oldStorage");
        SessionState.EraseBool(Key + ".glass");
        SessionState.EraseBool(Key + ".lighting");
    }
}

public sealed class TerrariumPlayDriver : MonoBehaviour
{
    readonly List<string> errors = new List<string>();
    DisplayPreferencesSnapshot preferences;
    CampaignApp app;
    bool background, cleaned;
    int checks;
    CampaignView View => FindFirstObjectByType<CampaignView>();
    void Start()
    {
        background = Application.runInBackground; Application.runInBackground = true;
        preferences = new DisplayPreferencesSnapshot(); Application.logMessageReceived += Log;
        SceneManager.sceneLoaded += PrepareStandalone; StartCoroutine(Guard(Run()));
    }
    void PrepareStandalone(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "BeetleNursery") return;
        var controller = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BeetleHusbandryController>(true)).Single();
        controller.Initialize(Path.Combine(CampaignApp.StorageOverride, "Standalone", "husbandry-roster.json"), false);
        controller.simulateAccidents = false;
    }
    void Log(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + trace); }
    void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++; File.AppendAllText(TerrariumPlayValidation.Report, "PASS " + message + "\n");
    }
    IEnumerator Wait(Func<bool> condition, string label)
    {
        var until = Time.realtimeSinceStartup + 40;
        while (!condition()) { if (Time.realtimeSinceStartup > until) throw new TimeoutException(label); yield return null; }
        yield return null;
    }
    IEnumerator Run()
    {
        yield return Wait(() => CampaignApp.Instance && !CampaignApp.Instance.Busy, "startup"); app = CampaignApp.Instance;
        app.LoadSlot(0);
        yield return Wait(() => !app.Busy && View && View.page == CampaignPage.Greenhouse && View.backdrop.Ready, "greenhouse");
        int frame = Time.frameCount; yield return new WaitForSecondsRealtime(.3f);
        Check(Time.frameCount > frame + 2, "player loop advances");
        if (TerrariumPlayValidation.LightingOnly) { yield return LightingChecks(); yield break; }
        if (TerrariumPlayValidation.GlassOnly) { yield return GlassChecks(); yield break; }
        var specimen = FindObjectsByType<CampaignSpecimen>(FindObjectsSortMode.None).Single(s => s.selected);
        Check(specimen.Enclosure.Area.CanWalk && Mathf.Abs(specimen.Enclosure.GroundHeight + .02f) < .0001f, "glass leaves the care floor reachable at its original height");
        var glass = GameObject.Find("Terrarium/GlassEnclosure").GetComponentsInChildren<Renderer>().Where(r => r.sharedMaterial.shader.name == "BeetleArena/HusbandryGlass").ToArray();
        Check(glass.Length == 4 && glass.All(r => r.enabled), "all four glass sides visible in adult care");
        foreach (var style in new[] { BeetleDisplayStyle.Clean, BeetleDisplayStyle.OrderedDither, BeetleDisplayStyle.DenseDither })
        { BeetleDisplayPreferences.Style = style; yield return Capture("Greenhouse-" + style); }
        BeetleDisplayPreferences.Style = BeetleDisplayStyle.Clean;
        var camera = CampaignObservationCamera.Ensure(View.backdrop);
        var enclosure = glass[0].GetComponentInParent<HusbandryGlassEnclosure>();
        Check(enclosure && Mathf.Abs(enclosure.Height - 5.6f) < .001f, "default glass is twice the previous 2.8 height");
        var floorBounds = specimen.enclosureFloor.bounds; float fullDistance = camera.Distance;
        enclosure.SetHeight(4.2f); yield return new WaitForSecondsRealtime(.7f);
        Check(glass.All(r => Mathf.Abs(r.bounds.size.y - 4.2f) < .001f) && specimen.enclosureFloor.bounds == floorBounds,
            "height control resizes all panes without moving soil");
        Check(Mathf.Abs(camera.Distance - fullDistance) > .01f, "environment camera refits after a height change");
        Check(Mathf.Abs(enclosure.transform.Find("TopFront").localPosition.y - 4f) < .001f &&
            Mathf.Abs(enclosure.transform.Find("CornerSeam0").localScale.y - 4.2f) < .001f,
            "upper rails and corner seams follow the glass height");
        enclosure.SetHeight(5.6f); yield return new WaitForSecondsRealtime(.7f);
        foreach (var pose in new[] { new Vector2(35, 24), new Vector2(225, 48), new Vector2(315, 70) })
        {
            typeof(CampaignObservationCamera).GetField("azimuth", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(camera, pose.x);
            typeof(CampaignObservationCamera).GetField("elevation", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(camera, pose.y);
            yield return Capture("Glass-Angle-" + pose.x);
        }
        camera.ResetView();
        var construction = CampaignConstructionController.Ensure(View.backdrop);
        construction.SetEditing(true); yield return Capture("Construction");
        Check(specimen.ConstructionHidden && !app.CalendarRunning, "building hides animal and pauses time");
        Check(Mathf.Abs(construction.SurfaceCenter.y - specimen.enclosureFloor.bounds.max.y) < .08f, "construction grid stays aligned to soil top");
        construction.SetEditing(false); yield return null;
        Check(!specimen.ConstructionHidden, "finishing construction restores animal");
        var beetle = app.Session.State.beetles.Find(b => b.id == app.Session.State.selectedId);
        var adultStage = beetle.stage; beetle.stage = CampaignLifeStage.Egg;
        yield return new WaitForSecondsRealtime(.3f);
        Check(glass.All(r => !r.enabled), "stage cutaway correctly hides adult glass enclosure");
        beetle.stage = adultStage; yield return new WaitForSecondsRealtime(.3f);
        Check(glass.All(r => r.enabled) && specimen.Enclosure.Area.CanWalk, "returning to adult restores glass and walk area");
        Destroy(app.gameObject); yield return Wait(() => !CampaignApp.Instance, "campaign disposal"); app = null;
        SceneManager.LoadScene("BeetleNursery"); yield return new WaitForSecondsRealtime(1);
        BeetleDisplayPreferences.Style = BeetleDisplayStyle.Clean; yield return Capture("Standalone-Clean");
        BeetleDisplayPreferences.Style = BeetleDisplayStyle.OrderedDither; yield return Capture("Standalone-Dither");
        Check(GameObject.Find("Terrarium/GlassEnclosure"), "standalone nursery uses the glass prefab too");
        Check(errors.Count == 0, "no runtime or rendering errors");
    }
    IEnumerator LightingChecks()
    {
        var specimen = FindObjectsByType<CampaignSpecimen>(FindObjectsSortMode.None).Single(s => s.selected);
        specimen.enabled = false; specimen.Care.enabled = false;
        var observer = CampaignObservationCamera.Ensure(View.backdrop);
        BeetleDisplayPreferences.Style = BeetleDisplayStyle.OrderedDither;
        typeof(CampaignObservationCamera).GetField("azimuth", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(observer, 51.3f);
        var fill = FindObjectsByType<Light>(FindObjectsSortMode.None).Single(l => l.name == "SoftFill");
        var spot = FindObjectsByType<Light>(FindObjectsSortMode.None).Single(l => l.name == "EnclosureSpot");
        var lighting = spot.GetComponent<HusbandrySpotlight>();
        var rotation = spot.transform.rotation; var position = spot.transform.position;
        BeetleHusbandryLightingPreferences.SetEnabled(true);
        yield return Wait(() => !app.GetComponent<CampaignSceneTransition>().Busy && spot.intensity >= 119.9f, "entry light settles after reveal");
        yield return Capture("Lighting-Spot-On");
        typeof(CampaignView).GetField("settingsTab", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(View, 3);
        InvokeView("OpenSettings");
        yield return Capture("Lighting-Settings");
        var toggle = View.GetComponentsInChildren<UnityEngine.UI.Button>().Single(b => b.name == "HusbandrySpotlight");
        toggle.onClick.Invoke(); yield return null;
        Check(!spot.enabled && !lighting.groundPool.enabled && PlayerPrefs.GetInt(BeetleHusbandryLightingPreferences.EnabledKey) == 0,
            "settings toggle immediately disables both light and pool and saves the choice");
        InvokeView("CloseModal"); yield return Capture("Lighting-Spot-Off");
        BeetleHusbandryLightingPreferences.SetEnabled(true);
        Check(spot.enabled && lighting.groundPool.enabled, "enabling restores both the real light and the circular pool");
        var floor = specimen.enclosureFloor.bounds;
        bool covered = true;
        foreach (float height in new[] { 0f, 2.5f })
            foreach (int x in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 })
            {
                var point = new Vector3(floor.center.x + x * floor.extents.x, floor.max.y + height, floor.center.z + z * floor.extents.z);
                var ray = point - spot.transform.position;
                covered &= Vector3.Angle(spot.transform.forward, ray) < spot.innerSpotAngle * .5f && ray.magnitude < spot.range;
            }
        Check(spot.type == LightType.Spot && covered, "fixed spotlight fully covers the floor corners and adult height inside its inner cone");
        Check(AssetDatabase.LoadAssetAtPath<Material>(HusbandryLightingSetup.BackdropMaterialPath).GetFloat("_SpecStrength") == 0, "backdrop no longer produces a view-dependent highlight");
        Check(fill.type == LightType.Directional && fill.shadows == LightShadows.None, "fill covers the enclosure without self-shadowing");
        Check(FindObjectsByType<Light>(FindObjectsSortMode.None).Single(l => l.name == "WarmCanopy").shadows != LightShadows.None, "warm key retains natural shadows");
        var soil = AssetDatabase.LoadAssetAtPath<Material>(HusbandryTerrariumSetup.SoilTopMaterialPath);
        Check(Mathf.Abs(soil.GetColor("_BaseColor").r - .64f) < .001f, "soil top is independently darkened");
        specimen.transform.position = new Vector3(-2.5f, specimen.transform.position.y, 1.8f);
        specimen.transform.Rotate(0,180,0);
        typeof(CampaignObservationCamera).GetField("azimuth", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(observer, 315f);
        yield return Capture("Lighting-Spot-Corner");
        Check(Quaternion.Angle(spot.transform.rotation, rotation) < .01f && spot.transform.position == position, "spotlight stays fixed when the insect and camera move");
        Check(errors.Count == 0, "no runtime or rendering errors");
    }
    IEnumerator GlassChecks()
    {
        var surface = FindFirstObjectByType<HusbandryGlassSurface>();
        var camera = View.backdrop.sceneCamera;
        var data = camera.GetUniversalAdditionalCameraData();
        var originalColor = data.requiresColorOption; var originalDepth = data.requiresDepthOption;
        CameraOverrideOption duringColor = originalColor, duringDepth = originalDepth;
        void Observe(ScriptableRenderContext context, Camera rendered)
        { if (rendered == camera) { duringColor = data.requiresColorOption; duringDepth = data.requiresDepthOption; } }
        RenderPipelineManager.beginCameraRendering += Observe;
        try
        {
            BeetleDisplayPreferences.Style = BeetleDisplayStyle.Clean;
            BeetleGlassPreferences.SetQuality(BeetleGlassQuality.Realistic);
            yield return Capture("Glass-Realistic");
            Check(duringColor == CameraOverrideOption.On && duringDepth == CameraOverrideOption.On, "realistic glass requests scene color and depth on world camera");
            Check(data.requiresColorOption == originalColor && data.requiresDepthOption == originalDepth, "render scope restores original camera options");
            typeof(CampaignView).GetField("settingsTab", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(View, 3);
            InvokeView("OpenSettings"); yield return new WaitForSecondsRealtime(.3f);
            var dropdown = FindObjectsByType<RetroFilterDropdown>(FindObjectsSortMode.None).Single(d => d.isActiveAndEnabled && d.IsGlassQualityDropdown);
            dropdown.Open(); yield return Capture("Glass-Settings");
            dropdown.OptionButtons.Single(b => b.name == "GlassOption-Low").onClick.Invoke();
            yield return new WaitForSecondsRealtime(.25f); InvokeView("CloseModal");
            yield return Capture("Glass-Low");
            Check(BeetleGlassPreferences.Quality == BeetleGlassQuality.Low && PlayerPrefs.GetInt(BeetleGlassPreferences.QualityKey) == 1, "settings selection persists low quality immediately");
            Check(surface.ActiveQuality == BeetleGlassQuality.Low && surface.GetComponentsInChildren<MeshRenderer>().Count(r => r.sharedMaterial == surface.LowMaterial) == 4,
                "all four panes switch to the low shader");
            Check(duringColor == CameraOverrideOption.Off && duringDepth == originalDepth, "low quality skips opaque color copy while preserving other depth consumers");
            BeetleGlassPreferences.SetQuality(BeetleGlassQuality.Realistic);
            BeetleDisplayPreferences.Style = BeetleDisplayStyle.OrderedDither;
            yield return Capture("Glass-Realistic-Dither");
            Check(surface.ActiveQuality == BeetleGlassQuality.Realistic && duringColor == CameraOverrideOption.On, "switching back restores realistic glass independently of filter");
        }
        finally { RenderPipelineManager.beginCameraRendering -= Observe; }
        Destroy(app.gameObject); yield return Wait(() => !CampaignApp.Instance, "campaign disposal"); app = null;
        SceneManager.LoadScene("BeetleNursery"); yield return new WaitForSecondsRealtime(.7f);
        BeetleGlassPreferences.SetQuality(BeetleGlassQuality.Low); yield return Capture("Glass-Standalone-Low");
        Check(FindFirstObjectByType<HusbandryGlassSurface>().ActiveQuality == BeetleGlassQuality.Low, "glass quality follows the player into standalone care");
        Check(errors.Count == 0, "no runtime or rendering errors");
    }
    void InvokeView(string name) => typeof(CampaignView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null).Invoke(View, null);
    IEnumerator Capture(string name)
    {
        yield return new WaitForSecondsRealtime(.8f); yield return new WaitForEndOfFrame();
        var game = EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")); RenderTexture target = null;
        for (var type = game.GetType(); type != null && !target; type = type.BaseType)
        {
            var field = type.GetField("m_TargetTexture", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) ?? type.GetField("m_RenderTexture", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) target = field.GetValue(game) as RenderTexture;
        }
        if (!target) throw new InvalidOperationException("No composed Game View frame");
        var previous = RenderTexture.active; var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
            if (SystemInfo.graphicsUVStartsAtTop)
            {
                var pixels = texture.GetPixels32(); var upright = new Color32[pixels.Length];
                for (int y = 0; y < texture.height; y++) Array.Copy(pixels, y * texture.width, upright, (texture.height - 1 - y) * texture.width, texture.width);
                texture.SetPixels32(upright); texture.Apply();
            }
            File.WriteAllBytes(TerrariumPlayValidation.Output + "/" + name + ".png", texture.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; Destroy(texture); }
    }
    IEnumerator Guard(IEnumerator routine)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(routine);
        while (stack.Count > 0)
        {
            bool moved = false; object current = null; Exception failure = null;
            try { moved = stack.Peek().MoveNext(); if (moved) current = stack.Peek().Current; } catch (Exception error) { failure = error; }
            if (failure != null) { Finish(failure); yield break; }
            if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
            if (current is IEnumerator nested) stack.Push(nested); else yield return current;
        }
        Finish(null);
    }
    void Cleanup()
    {
        if (cleaned) return; cleaned = true;
        Application.logMessageReceived -= Log; SceneManager.sceneLoaded -= PrepareStandalone;
        preferences?.Dispose(); Application.runInBackground = background;
    }
    void OnDestroy() => Cleanup();
    void Finish(Exception error)
    {
        Cleanup(); File.AppendAllText(TerrariumPlayValidation.Report, (error == null ? "COMPLETE PASS" : "FAILED " + error) + " | " + checks + " checks\n" + string.Join("\n", errors));
        EditorApplication.isPlaying = false;
    }
}
