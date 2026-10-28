using System;
using System.IO;
using System.Linq;
using BeetleArena;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

// Small project-local automation entry point. Commands use Unity APIs, never scene YAML edits.
[InitializeOnLoad]
// Lightweight in-project editor automation bridge: polls single JSON requests in Temp and invokes Unity Editor APIs.
// Compiles in Editor only; command file is not a queue; caller should await response for current id before dispatching next request.
public static class BeetleArenaEditorCommands
{
    // id matches response, action selects command, seconds supplies time parameter; no arbitrary code execution entry points.
    [Serializable] class Request { public string id, action; public float seconds; public int count; }
    // Polls at most ~every 0.4s; halts request processing during script compilation or asset refreshes.
    static double nextPoll;
    static UnityEditor.PackageManager.Requests.AddRequest localeInstall;
    // Resolves project root from parent of Application.dataPath without depending on launcher process working directory.
    static string Root => Directory.GetParent(Application.dataPath).FullName;
    // Requests reside in Temp; request file is deleted prior to execution to prevent duplicate execution across domain reloads.
    static string RequestPath => Path.Combine(Root, "Temp/BeetleArenaCommand.json");
    // Responses overwrite file with id and OK/ERROR on first line. Callers must verify id to avoid confusing stale results with current completion.
    static string ResponsePath => Path.Combine(Root, "Temp/BeetleArenaResponse.txt");
    // InitializeOnLoad triggers static setup: registers polling and error logger, writes readiness timestamp for external confirmation.
    static BeetleArenaEditorCommands()
    {
        EditorApplication.update += Poll;
        Application.logMessageReceived += Log;
        Directory.CreateDirectory(Path.Combine(Root, "Artifacts/BeetleArena"));
        File.WriteAllText(Path.Combine(Root, "Temp/BeetleArenaBridgeReady.txt"), DateTime.Now.ToString("O"));
    }
    // Gathers Error/Exception/Assert only, with timestamp and stack trace; standard Log messages are omitted from error file.
    static void Log(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        File.AppendAllText(Path.Combine(Root, "Artifacts/BeetleArena/EditorErrors.txt"), DateTime.Now.ToString("O") + " " + message + "\n" + stack + "\n");
    }
    // Read -> Deserialize -> Delete request -> Execute -> Write response; unreadable files defer to next polling cycle.
    // Lengthy validations block the editor main thread update loop; unsuitable for runtime background services.
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        nextPoll = EditorApplication.timeSinceStartup + .4;
        if (!File.Exists(RequestPath)) return;
        Request request;
        try { request = JsonUtility.FromJson<Request>(File.ReadAllText(RequestPath)); File.Delete(RequestPath); }
        catch (IOException) { return; }
        try
        {
            string answer = Execute(request);
            File.WriteAllText(ResponsePath, request.id + " OK\n" + answer);
        }
        catch (Exception ex) { File.WriteAllText(ResponsePath, request.id + " ERROR\n" + ex); Debug.LogException(ex); }
    }
    // Command dispatch: refresh/prepare handle imports, build/tune mutate assets, play/stop/open toggle scenes or playmode,
    // inspect/capture monitor state, advance/smoke/validate run tests; build refers to generating scene, not packaging player.
    static string Execute(Request request)
    {
        switch (request.action)
        {
            case "interface-setup": CampaignInterfaceSetup.Apply();return "Interface assets prepared.";
            case "interface-data": CampaignInterfaceValidation.Run();return File.ReadAllText("Artifacts/InterfaceRework/Data.txt");
            case "interface-play": CampaignInterfacePlayValidation.Run();return "Interface play acceptance started.";
            case "ui-polish-apply": HusbandryTerrariumSetup.Apply();CampaignLocalizationSetup.Apply();return "Terrarium and localized interface assets updated.";
            case "ui-polish-data": CampaignFoodDishValidation.Run();HusbandryTerrariumValidation.Run();CareAmbientValidation.Run();CareGroundingValidation.Run();CampaignConstructionValidation.Run();CampaignBeetleGrabValidation.Run();return "Food dish, terrarium, ambient motion, foot grounding, construction and beetle handling checks passed.";
            case "display-three-levels": BeetleArena.EditorTools.WorldDisplayEditorService.ApplyThreePixelLevelDefaults();return "Dither defaults set to the second of three pixel levels.";
            case "defensive-review": DefensiveProgrammingValidation.Run(); return File.ReadAllText("Artifacts/DefensiveReview/Verification.txt");
            case "ui-click-settings":
                var clickProfile = RetroUiAudioSetup.EnsureProfile();
                if (request.seconds > 0)
                {
                    Undo.RecordObject(clickProfile, "Change UI click interval");
                    clickProfile.reboundMs = Mathf.Clamp(request.seconds * 1000, 0, 150);
                    EditorUtility.SetDirty(clickProfile);
                    AssetDatabase.SaveAssetIfDirty(clickProfile);
                }
                RetroUiAudioSetup.SelectSettings();
                return RetroUiAudioSetup.ProfilePath + "; intervalMs=" + clickProfile.Settings.ReboundMs + "; preview=" + RetroUiAudioProfileEditor.CanPreview;
            case "camera-follow": BeetleCameraValidation.Run(); return File.ReadAllText("Artifacts/Framework/CameraValidation.txt");
            case "care-enclosure": CareEnclosureValidation.Run(); return File.ReadAllText("Artifacts/Campaign/Care/Enclosure.txt");
            case "mailbox-data": CampaignMailboxValidation.Run(); return File.ReadAllText("Artifacts/Mailbox/Data.txt");
            case "mailbox-play": CampaignMailboxValidation.RunPlay(); return "Mailbox UI and transaction checks started with isolated saves.";
            case "localization-install": localeInstall=UnityEditor.PackageManager.Client.Add("com.unity.localization"); return "Localization package installation requested.";
            case "localization-install-status": return localeInstall==null?"No request retained (check packages-lock after domain reload).":localeInstall.Status+" "+localeInstall.Error?.message;
            case "localization-setup": CampaignLocalizationSetup.Apply(); return File.ReadAllText("Artifacts/Localization/Setup.txt");
            case "localization-validate": RetroPresentationValidation.RunBilingual(); return "Bilingual presentation checks started using isolated saves.";
            case "crt-steady": BeetleCombatCrtSetup.ApplySteadyPreset(); return "Steady CRT screen preset saved; lens curvature preserved.";
            case "crt-screen-preview": return BeetleCombatCrtSetup.PreviewSteadyScreen();
            case "retro-setup": CampaignRetroFontSetup.Apply();return "Retro font and CRT profile ready.";
            case "retro-validate": RetroPresentationValidation.Run();return "Retro presentation check started; see Artifacts/RetroUi/Result.txt.";
            case "editor-menu-list":
                var menuEntries = TypeCache.GetMethodsWithAttribute<MenuItem>()
                    .Where(method => method.DeclaringType.Assembly == typeof(BeetleArenaEditorCommands).Assembly)
                    .SelectMany(method => method.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>()
                        .Where(menu => !menu.validate && menu.menuItem.StartsWith("Tools/", StringComparison.Ordinal))
                        .Select(menu => new { menu.menuItem, menu.priority, Binding = method.DeclaringType.FullName + "." + method.Name }))
                    .OrderBy(entry => entry.priority).ThenBy(entry => entry.menuItem).ToArray();
                string menuReport = "Editor menu entries=" + menuEntries.Length + "\n" + string.Join("\n",
                    menuEntries.Select(entry => entry.menuItem + "\t" + entry.priority + "\t" + entry.Binding));
                Directory.CreateDirectory("Artifacts/EditorMenus");
                File.WriteAllText("Artifacts/EditorMenus/Inventory.txt", menuReport);
                return menuReport;
            case "cleanup-generated-audit": return BeetleGeneratedAssetCleanup.Run(false);
            case "cleanup-generated-apply": return BeetleGeneratedAssetCleanup.Run(true);
            case "horn-inspect": BeetleHornBindingValidation.Inspect(); return File.ReadAllText("Artifacts/HornBinding/Inspection.txt");
            case "horn-upgrade": BeetleHornBindingUpgrade.Apply(); return "Horn anatomy bindings upgraded.";
            case "horn-validate": BeetleHornBindingValidation.Run(); return File.ReadAllText("Artifacts/HornBinding/Validation.txt");
            case "menu-iridescence": CampaignIridescenceSetup.Apply(); return "Menu iridescent shell installed.";
            case "menu-iridescence-validate": CampaignIridescenceValidation.Run(); return File.ReadAllText("Artifacts/IridescentShell/Validation.txt");
            case "care-behavior": CareBehaviorValidation.Run(); return File.ReadAllText("Artifacts/Campaign/Care/Contracts.txt");
            case "care-grounding": CareGroundingValidation.Run(); return File.ReadAllText("Artifacts/Campaign/Care/GroundContact.txt");
            case "care-ground-setup": CareGroundingSetup.Apply(); return "Care shell geometry prepared.";
            case "campaign-transition-profile": CampaignTransitionSetup.Apply(); return "Transition curve profile ready.";
            case "campaign-transition-baseline": CampaignTransitionValidation.Run(false); return "Transition baseline capture started.";
            case "campaign-transition-verify": CampaignTransitionValidation.Run(true); return "Transition frame acceptance started.";
            case "campaign-style": CampaignToonSetup.Apply(); return "Campaign toon materials applied.";
            case "campaign-fonts": CampaignFontSetup.Apply(); return "Campaign font coverage PASS.";
            case "low-throw-baseline": BeetleLowThrowValidation.Run(); return File.ReadAllText("Artifacts/LowThrow/Before/Report.txt");
            case "low-throw-verify": BeetleLowThrowValidation.Run(true); return File.ReadAllText("Artifacts/LowThrow/After/Report.txt");
            case "campaign-data": CampaignDataValidation.Run(); return "Campaign data PASS.";
            case "campaign-scenes": CampaignSceneValidation.Run(); return "Campaign scenes PASS.";
            case "campaign-status": return "Playing="+EditorApplication.isPlaying+"; paused="+EditorApplication.isPaused+"; focused="+Application.isFocused+"; frame="+Time.frameCount+"; scene="+SceneManager.GetActiveScene().path+"; dirty="+SceneManager.GetActiveScene().isDirty;
            case "campaign-play":
                var gameView=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));gameView.Show();gameView.Focus();
                EditorApplication.isPaused=false;EditorApplication.isPlaying=true;return "Normal game playback requested.";
            case "campaign-recover-input": return CampaignInterfacePlayValidation.RecoverPhysicalInput();
            case "campaign-input-status":
                var events=EventSystem.current;var app=BeetleArena.Campaign.CampaignApp.Instance;
                var devices=UnityEngine.InputSystem.InputSystem.devices.Select(d=>d.name+"; enabled="+d.enabled+"; native="+d.native+"; id="+d.deviceId);
                var buttons=UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None).Where(b=>b.gameObject.activeInHierarchy).ToArray();
                return "Playing="+EditorApplication.isPlaying+"; paused="+EditorApplication.isPaused+"; focused="+Application.isFocused+"; frame="+Time.frameCount+
                    "\nBusy="+(app?app.Busy:false)+"; modal="+(app?app.Modal:false)+"; eventSystem="+(events?events.name:"none")+"; module="+(events&&events.currentInputModule?events.currentInputModule.GetType().Name:"none")+
                    "\nButtons="+buttons.Length+"; interactable="+buttons.Count(b=>b.IsInteractable())+"\n"+string.Join("\n",devices);
            case "campaign-play-validation": CampaignPlayModeValidation.Run(); return "Campaign end-to-end acceptance started; see Artifacts/EndToEnd/Result.txt.";
            case "validate-landing-slide": BeetleThrowValidation.LandingAndEdge(); return File.ReadAllText("Artifacts/LandingSlide/After.txt") + File.ReadAllText("Artifacts/LandingSlide/Edge.txt");
            case "diagnose-replay": BeetleLiftCatchValidation.CompareReplay(); return File.ReadAllText("Artifacts/LiftCatch/Comparison.txt");
            case "replay-natural-throw": BeetleRoundPacingValidation.Preview(); return File.ReadAllText("Artifacts/RoundPacing/Preview.txt");
            case "validate-catch": BeetleLiftCatchValidation.CatchSteps(); return File.ReadAllText("Artifacts/LiftCatch/Recovery.txt");
            case "diagnose-lift-catch": BeetleLiftCatchValidation.Diagnose(); return File.ReadAllText("Artifacts/LiftCatch/Diagnosis.csv");
            case "validate-tumble-landing": BeetleThrowValidation.LandingOnly(); return File.ReadAllText("Artifacts/LeverThrows/Validation.txt");
            case "validate-table-righting": BeetleThrowValidation.TableRighting(); return File.ReadAllText("Artifacts/TumbleLimbs/TableRighting.txt");
            case "validate-supported-righting": BeetleThrowValidation.TableRighting(true); return File.ReadAllText("Artifacts/TumbleLimbs/SupportedRighting.txt");
            case "validate-throws": BeetleThrowValidation.Run(); return File.ReadAllText("Artifacts/LeverThrows/Validation.txt");
            case "validate-readability": BeetleReadabilityValidation.Run(); return File.ReadAllText("Artifacts/ActionReadability/Validation.txt");
            case "pacing-pilot": BeetleRoundPacingValidation.Pilot(); return File.ReadAllText("Artifacts/RoundPacing/Pilot.txt");
            case "pacing-final": BeetleRoundPacingValidation.Final(); return File.ReadAllText("Artifacts/RoundPacing/Final.txt");
            case "diagnose-foreleg-reach": BeetleRoundPacingValidation.DiagnoseReach(); return File.ReadAllText("Artifacts/RoundPacing/ReachDiagnosis.txt");
            case "validate-foreleg": BeetleForelegValidation.Final(); return File.ReadAllText("Artifacts/ForelegReview/Validation.txt");
            case "validate-refinements": BeetleCombatRefinementValidation.Run(); return File.ReadAllText("Artifacts/CombatRefinement/Validation.txt");
            case "validate-trough": BeetleCombatRefinementValidation.Run(3); return File.ReadAllText("Artifacts/CombatRefinement/Validation.txt");
            case "combat-upgrade": BeetleCombatUpgrade.Apply(); return "Combat refinements saved.";
            case "validate-escape": BeetleContactEscapeValidation.Run(); return File.ReadAllText("Artifacts/DeadlockFix/CapturedReplay.txt");
            case "validate-stalls": BeetleStallValidation.Run(); return File.ReadAllText("Artifacts/DeadlockFix/NaturalRounds.txt");
            case "validate-underbody": BeetleUnderbodyEscapeValidation.Run(); return File.ReadAllText("Artifacts/UnderbodyEscape/Report.txt");
            case "seed-sweep": return BeetleSeedSweepValidation.Start(Mathf.RoundToInt(request.seconds), request.count > 0 ? request.count : 48);
            case "seed-diagnose": BeetleSeedLockDiagnosis.Run(request.seconds > 0); return "Seed lock diagnosis saved.";
            case "seed-case": return BeetleSeedSweepValidation.Start(Mathf.RoundToInt(request.seconds), 1);
            case "seed-sweep-stop": BeetleSeedSweepValidation.Stop(); return "Seed sweep stopped.";
            case "refresh": AssetDatabase.Refresh(); return "Refresh requested.";
            // Updates game materials only; render validation runs in an isolated temporary scene copy.
            case "apply-toon": BeetleArenaToonSetup.Apply(); return "Toon materials saved.";
            case "install-terrain": BeetleArenaTerrainSetup.Install(); return "Switchable arenas saved.";
            case "validate-terrain": BeetleArenaTerrainValidation.Run(); return File.ReadAllText(Path.Combine(Root, "Artifacts/BeetleArena/TerrainValidation.txt"));
            case "validate-motion": BeetleMotionValidation.Run(); return File.ReadAllText(Path.Combine(Root, "Artifacts/BeetleArena/MotionValidation.txt"));
            case "switch-arena": Running().SwitchArena(); return Running().ArenaName;
            case "validate-toon": BeetleArenaToonSetup.Validate(); return File.ReadAllText(Path.Combine(Root, "Artifacts/BeetleArena/ToonValidation.txt"));
            // Validates new motions and physical contact in a temporary copy to avoid modifying active game scene instances.
            case "validate-clashes":
                BeetleArenaCollisionValidation.RunInEditor();
                return File.ReadAllText(Path.Combine(Root, "Artifacts/BeetleArena/CollisionValidation.txt"));
            case "prepare":
                if (!TMP_Settings.instance) TMP_PackageResourceImporter.ImportResources(true, false, false);
                return "TMP resource import requested.";
            case "build":
                if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop play before rebuilding.");
                BeetleArenaSceneBuilder.Build(); return "Scene and articulated prefab saved.";
            case "validate": BeetleArenaValidation.Run(); return File.ReadAllText(Path.Combine(Root, "Artifacts/BeetleArena/SimulationValidation.txt"));
            // Legacy tuning shortcut command: writes listed asset values directly; verify scope before calling.
            case "tune":
                var settings = AssetDatabase.LoadAssetAtPath<BeetleMatchSettings>(BeetleArenaSceneBuilder.AssetFolder + "/MatchSettings.asset");
                settings.shoveImpulse = 2.7f; settings.pryImpulse = 4.3f; settings.fatiguePerSecond = .12f;
                settings.arenaRadius = 5.2f; settings.spawnDistance = 2.3f;
                settings.initialSeed = 0;
                EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
                return "Contest force and recovery tuning saved.";
            case "play":
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(BeetleArenaSceneBuilder.ScenePath);
                EditorApplication.isPlaying = true; return "Play requested.";
            case "stop": EditorApplication.isPlaying = false; return "Stop requested.";
            // Rejects scene switching if any loaded scene has unsaved changes, preventing silent data loss.
            case "open":
                for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) return "Existing scene has unsaved edits. Play is configured to start BeetleArena.";
                EditorSceneManager.OpenScene(BeetleArenaSceneBuilder.ScenePath); return "Opened game scene.";
            case "inspect":
                var found = UnityEngine.Object.FindFirstObjectByType<BeetleMatchController>();
                return found && found.Simulation != null ? Describe(found) : "No running match. Playing=" + EditorApplication.isPlaying;
            case "capture": return Capture("GamePreview.png");
            case "advance":
                var controller = Running();
                Advance(controller, request.seconds > 0 ? request.seconds : 10);
                return Describe(controller);
            case "smoke": return Smoke();
            default: throw new ArgumentException("Unknown command: " + request.action);
        }
    }
    // Shared prerequisite checks for commands requiring active Play mode; throws immediately if no running simulation is found.
    static BeetleMatchController Running()
    {
        var controller = UnityEngine.Object.FindFirstObjectByType<BeetleMatchController>();
        if (!EditorApplication.isPlaying || !controller || controller.Simulation == null) throw new InvalidOperationException("Game is not running.");
        return controller;
    }
    // Generates compact status readout for external inspection of phase, time, winner, positions, and action metrics.
    static string Describe(BeetleMatchController c) => $"phase={c.Simulation.Phase}; elapsed={c.Simulation.Elapsed:F2}; winner={c.Simulation.Winner}; paused={c.IsPaused}\n" +
        string.Join("\n", c.Simulation.Fighters.Select(f => $"{f.Index}: {f.Action}, position={f.Position}, height={f.Height:F2}, stamina={f.Stamina:F1}, shoves={f.Shoves}, pries={f.Pries}"));
    // Fast-forwards simulation, presentation constraints, and camera by fixed steps within a single editor invocation, settling post-match.
    // Mutates active match directly; strictly for testing and debugging, not a player feature.
    static void Advance(BeetleMatchController c, float seconds)
    {
        int settling = 0;
        for (int i = 0; i < Mathf.CeilToInt(seconds / .02f); i++)
        {
            c.AdvanceFixedStep();
            c.Present(1, c.Simulation.MotionTime, BeetleSimulation.FixedStep);
            if (c.Simulation.Phase == MatchPhase.Finished && ++settling > 80) break;
        }
        c.hud.Refresh();
    }
    // Captures active camera and saves 1600x900 PNG; finally restores render target and releases temporary texture.
    static string Capture(string name)
    {
        var c = Running();
        var camera = c.spectatorCamera.GetComponent<Camera>();
        var texture = new RenderTexture(1600, 900, 24);
        var pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        string path = Path.Combine(Root, "Artifacts/BeetleArena/" + name);
        try
        {
            camera.targetTexture = texture; Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture.active = texture; pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
            UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(texture);
        }
        return path;
    }
    // Restarts match with fixed seed, validating event system, tarsal bindings, encouragement cooldown, pause, ring-out, and rematch.
    // Modifies active Play mode match and outputs screenshots/reports; not a read-only health check.
    static string Smoke()
    {
        var c = Running();
        c.StartMatch(7319); Advance(c, 3.2f);
        if (UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length != 1) throw new Exception("Expected one EventSystem");
        foreach (var actor in c.actors)
            if (actor.animator.legs.Length != 6 || actor.animator.legs.Any(l => !l.hip || !l.upper || !l.lower || !l.ankle)) throw new Exception("Invalid six-leg rig");
        c.hud.fighters[0].cheerButton.onClick.Invoke();
        if (c.Simulation.Fighters[0].CheerCooldown <= 0) throw new Exception("HUD cheer not wired");
        if (c.Cheer(0)) throw new Exception("Spam accepted");
        c.hud.pauseButton.onClick.Invoke(); if (!c.IsPaused) throw new Exception("Pause not wired");
        c.hud.pauseButton.onClick.Invoke(); if (c.IsPaused) throw new Exception("Resume not wired");
        Advance(c, 12); Capture("CombatPreview.png");
        Advance(c, 290);
        if (c.Simulation.Phase != MatchPhase.Finished) throw new Exception("Runtime match failed to finish");
        if (!c.hud.resultPanel.activeSelf) throw new Exception("Missing result UI");
        Capture("ResultPreview.png");
        c.hud.rematchButton.onClick.Invoke(); if (c.Simulation.Phase != MatchPhase.Countdown) throw new Exception("Rematch not wired");
        Advance(c, 8); Capture("GamePreview.png");
        string result = "PASS: two six-leg rigs, one input EventSystem, HUD cheer/cooldown, pause/resume, full autonomous ring-out, result panel and rematch.\n" + Describe(c);
        File.WriteAllText(Path.Combine(Root, "Artifacts/BeetleArena/RuntimeValidation.txt"), result);
        return result;
    }
}
