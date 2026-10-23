using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BeetleArena.Campaign;
using BugDuel.Campaign;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UI = UnityEngine.UI;

[InitializeOnLoad]
public static class CampaignGreenhouseUiValidation
{
    const string Key = "BugDuel.GreenhouseUiValidation";
    public const string Output = "Artifacts/GreenhouseUi/Play";

    static CampaignGreenhouseUiValidation()
    {
        string path = SessionState.GetString(Key, "");
        if (path.Length > 0) CampaignApp.StorageOverride = path;
        EditorApplication.playModeStateChanged += OnPlay;
        EditorApplication.update += () =>
        {
            if (SessionState.GetString(Key, "").Length > 0 && EditorApplication.isPlaying)
                EditorApplication.QueuePlayerLoopUpdate();
        };
    }

    [MenuItem("Tools/Beetle Arena/Validate Greenhouse UI Play Flow")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before the isolated greenhouse UI validation.");
        Directory.CreateDirectory(Output);
        var session = CampaignSession.New(CampaignCatalog.Defaults(), 81543);
        session.RenamePlayer("验收玩家");
        session.ConfigureClock(600, 1, true);
        foreach (var beetle in session.State.beetles) beetle.habitat = "recovery";
        session.Validate();
        string path = Path.GetFullPath(Output + "/Save-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        new CampaignSaveRepository(path).Write(0, new CampaignSave { state = session.State, catalog = session.Catalog });
        File.WriteAllText(Output + "/Result.txt", "RUNNING\nIsolated saves: " + path + "\n");
        SessionState.SetString(Key + ".storage", CampaignApp.StorageOverride ?? "");
        SessionState.SetString(Key, path); CampaignApp.StorageOverride = path;
        SessionState.SetString(Key + ".start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Campaign/CampaignMainMenu.unity");
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Show();
        EditorApplication.isPlaying = true;
    }

    static void OnPlay(PlayModeStateChange change)
    {
        if (SessionState.GetString(Key, "").Length == 0) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var go = new GameObject("Greenhouse UI acceptance");
            UnityEngine.Object.DontDestroyOnLoad(go); go.AddComponent<CampaignGreenhouseUiDriver>();
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key + ".start", ""));
        string storage = SessionState.GetString(Key + ".storage", "");
        CampaignApp.StorageOverride = storage.Length == 0 ? null : storage;
        SessionState.EraseString(Key); SessionState.EraseString(Key + ".start"); SessionState.EraseString(Key + ".storage");
    }
}

public sealed class CampaignGreenhouseUiDriver : MonoBehaviour
{
    const string Output = CampaignGreenhouseUiValidation.Output;
    CampaignApp app;
    Mouse mouse;
    InputSettings originalInput, testInput;
    bool originalBackground, cleaned;
    readonly List<string> errors = new List<string>();
    CampaignView View => FindFirstObjectByType<CampaignView>();
    CampaignRescuePrompt Prompt => FindFirstObjectByType<CampaignRescuePrompt>();
    CampaignConstructionController Construction => FindFirstObjectByType<CampaignConstructionController>();
    BeetleArena.Campaign.CampaignSpecimen Specimen => FindObjectsByType<BeetleArena.Campaign.CampaignSpecimen>(FindObjectsSortMode.None)
        .Single(value => value.selected && value.gameObject.scene == View.gameObject.scene);
    RectTransform CanvasRect => View.backdrop.output.canvas.GetComponent<RectTransform>();
    RectTransform PageRect => (RectTransform)CanvasRect.Find("Page");
    RectTransform ToggleRect => (RectTransform)CanvasRect.Find("Sidebar toggle");

    void Start()
    {
        originalBackground = Application.runInBackground; Application.runInBackground = true;
        Application.logMessageReceived += Log;
        originalInput = InputSystem.settings; testInput = Instantiate(originalInput); InputSystem.settings = testInput;
        testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        mouse = InputSystem.AddDevice<Mouse>("Greenhouse UI validation mouse");
        StartCoroutine(Guard(Run()));
    }

    void Log(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + trace); }

    IEnumerator Guard(IEnumerator routine)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(routine);
        while (stack.Count > 0)
        {
            bool moved = false; object current = null; Exception failure = null;
            try { moved = stack.Peek().MoveNext(); if (moved) current = stack.Peek().Current; }
            catch (Exception error) { failure = error; }
            if (failure != null)
            {
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
                Finish(failure); yield break;
            }
            if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
            if (current is IEnumerator nested) stack.Push(nested); else yield return current;
        }
        Finish(null);
    }

    void Check(bool pass, string message)
    {
        if (!pass) throw new InvalidOperationException(message + " | " + app?.Message);
        File.AppendAllText(Output + "/Result.txt", "PASS " + message + "\n");
    }

    IEnumerator Wait(Func<bool> condition, string label)
    {
        float until = Time.realtimeSinceStartup + 40;
        while (!condition())
        { if (Time.realtimeSinceStartup > until) throw new TimeoutException(label + " | " + app?.Message); yield return null; }
        yield return null; yield return null;
    }

    UI.Button Button(string name) => FindObjectsByType<UI.Button>(FindObjectsSortMode.None)
        .Single(button => button.gameObject.activeInHierarchy && button.name == name && button.IsInteractable());

    IEnumerator Click(UI.Button button)
    {
        if (!button || !button.IsInteractable()) throw new InvalidOperationException("Missing or disabled UI button");
        var rect = button.GetComponent<RectTransform>(); var canvas = button.GetComponentInParent<Canvas>();
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, hits);
        if (hits.Count == 0 || hits[0].gameObject.GetComponentInParent<UI.Button>() != button)
            throw new InvalidOperationException("UI button is blocked at its actual screen position: " + button.name);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position }); yield return null; yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position }.WithButton(MouseButton.Left)); yield return null; yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position }); yield return null;
    }

    void InvokeView(string method) => typeof(CampaignView).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(View, null);

    Rect CanvasBounds(RectTransform rect)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector2 low = Vector2.positiveInfinity, high = Vector2.negativeInfinity;
        foreach (var corner in corners)
        { Vector2 point = CanvasRect.InverseTransformPoint(corner); low = Vector2.Min(low, point); high = Vector2.Max(high, point); }
        return Rect.MinMaxRect(low.x, low.y, high.x, high.y);
    }

    static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - 1 && inner.xMax <= outer.xMax + 1 &&
        inner.yMin >= outer.yMin - 1 && inner.yMax <= outer.yMax + 1;

    bool PromptAnchored()
    {
        if (!Prompt || !Prompt.Visible) return false;
        var output = View.backdrop.output.rectTransform; var subject = Specimen.LifeStageDisplay.ObservationBounds;
        Vector2 low = Vector2.positiveInfinity, high = Vector2.negativeInfinity;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = subject.center + Vector3.Scale(subject.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector3 viewport = View.backdrop.sceneCamera.WorldToViewportPoint(corner);
            if (viewport.z <= 0) return false;
            Vector2 point = CanvasRect.InverseTransformPoint(output.TransformPoint(new Vector3(output.rect.xMin + viewport.x * output.rect.width, output.rect.yMin + viewport.y * output.rect.height, 0)));
            low = Vector2.Min(low, point); high = Vector2.Max(high, point);
        }
        Rect prompt = CanvasBounds(Prompt.GetComponent<RectTransform>());
        float gapX = Mathf.Max(0, Mathf.Max(low.x - prompt.xMax, prompt.xMin - high.x));
        float gapY = Mathf.Max(0, Mathf.Max(low.y - prompt.yMax, prompt.yMin - high.y));
        return Contains(CanvasBounds(output), prompt) && new Vector2(gapX, gapY).magnitude < 28;
    }

    IEnumerator Overturn(string id)
    {
        if (!app.Execute(session => { var beetle = session.Beetle(id); beetle.overturned = true; beetle.overturnSeconds = 12; }))
            throw new InvalidOperationException("Overturn fixture rejected: " + app.Message);
        yield return Wait(() => !app.Busy && app.Session.Beetle(id).overturned, "overturned fixture committed");
    }

    bool RescuedAndSaved(string id) => !app.Session.Beetle(id).overturned && app.Session.Beetle(id).overturnSeconds == 0 &&
        !app.ReadSlot(0).state.beetles.Single(beetle => beetle.id == id).overturned && !Prompt.Visible;

    IEnumerator Capture(string name)
    {
        var game = EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")); game.Focus(); game.Repaint();
        yield return new WaitForSecondsRealtime(.15f); yield return new WaitForEndOfFrame();
        RenderTexture target = null;
        for (Type type = game.GetType(); type != null && !target; type = type.BaseType)
        {
            var field = type.GetField("m_TargetTexture", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) ??
                type.GetField("m_RenderTexture", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) target = field.GetValue(game) as RenderTexture;
        }
        if (!target) throw new InvalidOperationException("Game View has no composed render texture");
        var previous = RenderTexture.active; var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
            var pixels = texture.GetPixels32();
            Check(PromptAnchored() && pixels.Where((pixel, index) => index % 47 == 0).Select(pixel => (int)pixel.r + (pixel.g << 8) + (pixel.b << 16)).Distinct().Take(33).Count() > 32,
                "composited Game View includes visible rescue prompt: " + name);
            if (SystemInfo.graphicsUVStartsAtTop)
            {
                var upright = new Color32[pixels.Length];
                for (int y = 0; y < target.height; y++) Array.Copy(pixels, y * target.width, upright, (target.height - 1 - y) * target.width, target.width);
                texture.SetPixels32(upright); texture.Apply();
            }
            File.WriteAllBytes(Output + "/" + name + ".png", texture.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; Destroy(texture); }
    }

    IEnumerator Run()
    {
        yield return Wait(() => CampaignApp.Instance && !CampaignApp.Instance.Busy, "Main Menu startup");
        app = CampaignApp.Instance; app.LoadSlot(0);
        yield return Wait(() => !app.Busy && SceneManager.GetActiveScene().name == CampaignApp.SceneName(CampaignPage.Greenhouse) && Prompt, "isolated greenhouse");
        // Pause only this editor pose fixture; player saves resume their calendar on load.
        Check(app.Execute(s => s.ConfigureClock(s.State.clock.realSecondsPerDay, s.State.clock.speed, true)), "apply the editor-only paused pose fixture");
        yield return Wait(() => !app.Busy, "paused fixture commit");
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();
        yield return Wait(() => Application.isFocused && Specimen.LifeStageDisplay, "focused Game View and specimen presentation");
        yield return new WaitForSecondsRealtime(.7f); Canvas.ForceUpdateCanvases();
        string first = app.Session.State.beetles[0].id, second = app.Session.State.beetles[1].id;
        Quaternion upright = Specimen.rig.bodyPivot.localRotation;
        double elapsedDays = app.Session.State.clock.elapsedDays;
        yield return Overturn(first); yield return Wait(() => Prompt.Visible, "paused overturned presentation");
        Check(app.Session.State.clock.paused && Path.GetFullPath(app.SaveDirectory) == Path.GetFullPath(CampaignApp.StorageOverride), "fixture uses paused husbandry and isolated saves");
        Check(!PageRect.GetComponentsInChildren<UI.Button>(true).Any(button => button.name.Contains("帮助翻身")) && !Prompt.transform.IsChildOf(PageRect), "rescue action is outside the collapsible sidebar with no old sidebar button");
        Rect page = CanvasBounds(PageRect), toggle = CanvasBounds(ToggleRect);
        Check(Mathf.Abs(PageRect.rect.width / CanvasRect.rect.width - 1f / 3) < .001f && Contains(page, toggle) && page.xMax - toggle.xMax <= 8 && page.yMax - toggle.yMax <= 24,
            "one-third sidebar contains the collapse button at its title's top right");
        var scroll = PageRect.GetComponentsInChildren<UI.ScrollRect>().Single();
        Check(scroll.content.childCount > 0 && Mathf.Abs(scroll.content.rect.width - scroll.viewport.rect.width) < 1 &&
            scroll.content.Cast<Transform>().All(child => Mathf.Abs(((RectTransform)child).rect.width - scroll.viewport.rect.width) < 1), "care content and cards fill the scroll viewport width");
        Check(PromptAnchored() && Quaternion.Angle(upright, Specimen.rig.bodyPivot.localRotation) > 150 && app.Session.State.clock.elapsedDays == elapsedDays,
            "paused beetle visibly overturns and its expanded rescue prompt anchors beside it inside the RawImage without advancing time");
        yield return Capture("01-Expanded-Rescue");
        yield return Click(Prompt.Button); yield return Wait(() => !app.Busy && !app.Session.Beetle(first).overturned, "expanded rescue commit");
        Check(RescuedAndSaved(first) && Quaternion.Angle(upright, Specimen.rig.bodyPivot.localRotation) < 5 && app.Session.State.clock.elapsedDays == elapsedDays,
            "expanded scene button saves rescue, restores upright pose while paused, hides its prompt, and preserves elapsed time");

        yield return Overturn(first); yield return Click(ToggleRect.GetComponent<UI.Button>());
        yield return Wait(() => View.SidebarCollapsed && !View.SidebarAnimationActive, "sidebar collapse"); yield return new WaitForSecondsRealtime(.5f);
        Check(Mathf.Abs(View.backdrop.Viewport.x) < .001f && Mathf.Abs(View.backdrop.Viewport.width - 1) < .001f && PromptAnchored(), "collapsed sidebar leaves rescue visible inside the full-width scene");
        yield return Capture("02-Collapsed-Rescue");
        yield return Click(Prompt.Button); yield return Wait(() => !app.Busy && !app.Session.Beetle(first).overturned, "collapsed rescue commit");
        Check(RescuedAndSaved(first) && Quaternion.Angle(upright, Specimen.rig.bodyPivot.localRotation) < 5 && app.Session.State.clock.elapsedDays == elapsedDays,
            "collapsed scene button remains clickable and durably restores the upright pose without advancing paused time");

        yield return Click(ToggleRect.GetComponent<UI.Button>()); yield return Wait(() => !View.SidebarCollapsed && !View.SidebarAnimationActive, "sidebar expansion");
        yield return Overturn(first); yield return Click(Button("甲虫近观")); yield return new WaitForSecondsRealtime(.8f);
        Check(View.backdrop.GetComponent<CampaignObservationCamera>().Mode == CampaignObservationMode.Specimen && PromptAnchored(), "close observation keeps the prompt beside the rendered beetle");
        yield return Click(Button("场景全景")); yield return new WaitForSecondsRealtime(.8f);
        Check(View.backdrop.GetComponent<CampaignObservationCamera>().Mode == CampaignObservationMode.Environment && PromptAnchored(), "environment observation remaps the prompt to the actual scene viewport");
        app.Select(second); yield return Wait(() => app.Session.State.selectedId == second && !app.Busy, "second beetle selected");
        Check(!Prompt.Visible && app.Session.Beetle(first).overturned, "selecting an upright beetle hides another beetle's rescue prompt");
        yield return Overturn(second); yield return new WaitForSecondsRealtime(.4f);
        Check(PromptAnchored(), "newly selected overturned beetle gets its own scene anchor");
        yield return Click(Prompt.Button); yield return Wait(() => !app.Busy && !app.Session.Beetle(second).overturned, "second beetle rescue commit");
        Check(RescuedAndSaved(second) && app.Session.Beetle(first).overturned, "scene action rescues and saves only the currently selected beetle");

        app.Select(first); yield return Wait(() => app.Session.State.selectedId == first && Prompt.Visible, "first beetle reselected");
        InvokeView("OpenGreenhouseRoster"); yield return null; yield return null;
        Check(app.Modal && !Prompt.Visible && !Prompt.GetComponent<CanvasGroup>().blocksRaycasts, "modal hides rescue and disables its pointer interception");
        InvokeView("CloseModal"); yield return Wait(() => !app.Modal && Prompt.Visible, "modal closed");
        yield return Click(Button("景观建造")); yield return Wait(() => Construction.Editing && app.Constructing, "construction opened");
        Check(!Prompt.Visible && !Prompt.GetComponent<CanvasGroup>().blocksRaycasts, "construction hides rescue and disables its pointer interception");
        yield return Click(Button("日常照料")); yield return Wait(() => !app.Constructing && Prompt.Visible, "care restored");
        Check(PromptAnchored(), "leaving construction restores the scene rescue anchor");
        Check(errors.Count == 0, "no runtime errors or exceptions");
    }

    void Cleanup()
    {
        if (cleaned) return; cleaned = true; Application.logMessageReceived -= Log;
        if (mouse != null) InputSystem.RemoveDevice(mouse);
        if (originalInput) InputSystem.settings = originalInput;
        if (testInput) Destroy(testInput);
        Application.runInBackground = originalBackground;
    }

    void OnDestroy() => Cleanup();
    void Finish(Exception failure)
    {
        Cleanup(); File.AppendAllText(Output + "/Result.txt", (failure == null ? "COMPLETE PASS" : "FAILED " + failure) + "\n" + string.Join("\n", errors));
        EditorApplication.isPlaying = false;
    }
}
