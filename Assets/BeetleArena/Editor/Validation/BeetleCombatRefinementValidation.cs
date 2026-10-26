using System;
using System.IO;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEngine;

public static class BeetleCombatRefinementValidation
{
    const string Folder = "Artifacts/CombatRefinement";
    static void Require(bool value, string why) { if (!value) throw new Exception(why); }
    public static void Batch()
    {
        try
        {
            BeetleCombatUpgrade.Apply();
            BeetleIndividualityValidation.VerifyRules(UnityEditor.AssetDatabase.LoadAssetAtPath<BeetleMatchSettings>("Assets/BeetleArena/Generated/MatchSettings.asset"));
            CaptureReview();
            Run();
            BeetleContactEscapeValidation.Run();
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    public static void DiagnoseMeshBatch()
    {
        try
        {
            using var fixture = new BeetleValidationScene("VisibleMeshDiagnosis");
            var m = fixture.Match; var report = new StringBuilder();
            m.SelectArena(1); m.StartMatch(7319); BeetleImportedValidation.ResetVisibleMeasurement();
            float worst = 0;
            for (int tick = 0; tick < 4500; tick++)
            {
                m.AdvanceFixedStep(); m.Present(1, m.Simulation.MotionTime, .02f);
                if (tick % 250 != 0) continue;
                float depth = BeetleImportedValidation.MeasureRenderedMesh(m);
                report.AppendLine($"t={m.Simulation.Elapsed:F2} depth={depth:F6} {BeetleImportedValidation.WorstVisiblePart}");
                File.WriteAllText(Folder + "/MeshDiagnosis.txt", report.ToString());
                if (depth > worst) { worst = depth; Shot(m, "Mesh-diagnosis.png"); }
            }
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    public static void CaptureReview()
    {
        Directory.CreateDirectory(Folder);
        using var fixture = new BeetleValidationScene("CombatReview");
        var m = fixture.Match;
        for (int arena = 0; arena < m.arenas.Length; arena++)
        {
            m.SelectArena(arena); m.StartMatch(7319);
            for (int i = 0; i < 260; i++) { m.AdvanceFixedStep(); m.Present(1, m.Simulation.MotionTime, .02f); }
            m.hud.Refresh(); Shot(m, $"Arena-{arena}.png");
            if (arena == 0)
            {
                m.CycleView(); for (int i = 0; i < 80; i++) m.spectatorCamera.Tick(m.Simulation, .02f);
                Shot(m, "Follow-Amber.png");
                m.CycleView(); for (int i = 0; i < 80; i++) m.spectatorCamera.Tick(m.Simulation, .02f);
                Shot(m, "Follow-Ink.png"); m.CycleView();
            }
        }
        m.SelectArena(0); m.StartMatch(7319);
        var a = m.actors[0]; a.State.Action = BeetleAction.PryWindup; a.State.ActionDuration = 1; a.State.ActionTime = 1;
        for (int i = 0; i < 40; i++) a.animator.Pose(a.State, i * .02f, .02f);
        var camera = m.spectatorCamera.GetComponent<Camera>();
        camera.orthographicSize = 1.5f;
        Vector3 focus = a.animator.headPivot.position;
        camera.transform.position = focus + new Vector3(3, .8f, 2); camera.transform.LookAt(focus);
        m.hud.gameObject.SetActive(false); Shot(m, "Head-down-seam.png");
    }
    static void Shot(BeetleMatchController m, string name)
    {
        var camera = m.spectatorCamera.GetComponent<Camera>();
        var objects = new System.Collections.Generic.List<GameObject>();
        foreach (var root in m.gameObject.scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) objects.Add(t.gameObject);
        var layers = new int[objects.Count]; int mask = camera.cullingMask;
        var rt = new RenderTexture(1600, 1000, 24); var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
        var old = RenderTexture.active; var oldTarget = camera.targetTexture;
        bool async = ShaderUtil.allowAsyncCompilation;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            for (int i = 0; i < objects.Count; i++) { layers[i] = objects[i].layer; objects[i].layer = 31; }
            camera.cullingMask = 1 << 31; camera.targetTexture = rt; Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply();
            File.WriteAllBytes(Folder + "/" + name, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = old; camera.targetTexture = oldTarget; camera.cullingMask = mask;
            ShaderUtil.allowAsyncCompilation = async;
            for (int i = 0; i < objects.Count; i++) objects[i].layer = layers[i];
            UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(rt);
        }
    }
    [MenuItem("Tools/甲壳虫斗/验证/战斗/战斗调优", false, 503)]
    public static void RunMenu() => Run();
    public static void Run(int firstArena = 0)
    {
        using var fixture = new BeetleValidationScene("CombatRefinementValidation");
        var match = fixture.Match; var report = new StringBuilder("COMBAT REFINEMENTS\n");
        Directory.CreateDirectory(Folder);
        try
        {
            int low = 0, high = 0; double sum = 0, squares = 0;
            var random = new System.Random(10142026);
            for (int i = 0; i < 20000; i++)
            {
                float size = Mathf.Lerp(match.settings.minimumSize, match.settings.maximumSize, (float)random.NextDouble());
                var t = BeetleTraits.Create(size, random, match.settings);
                if (t.QualityBand == "低") low++; if (t.QualityBand == "高") high++;
                sum += t.TotalScore; squares += t.TotalScore * t.TotalScore;
                foreach (float score in new[] { t.aggressionScore, t.lengthScore, t.hornScore, t.agilityScore })
                    Require(score >= 0 && score <= 100, "Trait score out of range");
            }
            double mean = sum / 20000, sd = Math.Sqrt(squares / 20000 - mean * mean);
            report.AppendLine($"Population n=20000 mean={mean:F2} sd={sd:F2} low={low / 200f:F2}% high={high / 200f:F2}%");
            Require(low > 1700 && low < 2300 && high > 1700 && high < 2300 && sd > 26 && sd < 30, "Trait distribution drift");
            match.StartMatch(7319);
            int originalView = match.spectatorCamera.ViewMode;
            for (int i = 0; i < 3; i++) match.hud.viewButton.onClick.Invoke();
            Require(originalView == match.spectatorCamera.ViewMode, "Camera cycle does not wrap");
            bool option = match.defeatSlowMotion;
            match.hud.slowMotionButton.onClick.Invoke(); Require(match.defeatSlowMotion != option, "Slow motion button failed");
            match.hud.slowMotionButton.onClick.Invoke(); Require(match.defeatSlowMotion == option, "Slow motion toggle failed");
            // Real ring-out activates the close-up once; disabling cancels it without advancing rules.
            match.defeatSlowMotion = true;
            match.Simulation.Fighters[0].Position = new Vector2(match.settings.arenaRadius + 2, 0);
            match.Simulation.Fighters[0].Airborne = true;
            for (int i = 0; i < 250 && !match.IsDefeatSlowMotion; i++) match.AdvanceFixedStep();
            Require(match.IsDefeatSlowMotion && match.spectatorCamera.DefeatFocus == 0, "Ring-out did not trigger slow motion");
            match.spectatorCamera.Tick(match.Simulation, 1);
            Require(match.spectatorCamera.GetComponent<Camera>().orthographicSize < match.spectatorCamera.overviewSize - 1, "Defeat close-up failed");
            float elapsed = match.Simulation.Elapsed, globalScale = Time.timeScale;
            match.ToggleDefeatSlowMotion();
            Require(!match.IsDefeatSlowMotion && match.spectatorCamera.DefeatFocus < 0 && match.Simulation.Elapsed == elapsed && Time.timeScale == globalScale, "Slow motion changed rules or failed to cancel");
            match.defeatSlowMotion = option; match.StartMatch(7319);
            var push = ScriptableObject.CreateInstance<DrivingPushMotion>();
            var context = new BeetleMotionContext(match.Simulation, match.Simulation.Fighters[0]);
            for (int i = 0; i <= 100; i++)
                Require(!(push.SampleLeg(context, i / 100f, 2, out _) && push.SampleLeg(context, i / 100f, 5, out _)), "Both hind legs driven together");
            UnityEngine.Object.DestroyImmediate(push);
            report.AppendLine("PASS: camera cycle, slow-motion toggle/real ring-out/zoom/cancellation, alternating hind drive.");
            File.WriteAllText(Folder + "/Validation.txt", report.ToString());
            for (int arena = firstArena; arena < match.arenas.Length; arena++)
            {
                match.SelectArena(arena); match.StartMatch(7319);
                BeetleImportedValidation.ResetVisibleMeasurement();
                float overlap = 0, longest = 0, stalled = 0, viewError = 0, meshDepth = 0;
                int hindConflicts = 0;
                var moduleStarts = new System.Collections.Generic.Dictionary<string, int>();
                Vector2 a = match.Simulation.Fighters[0].Position, b = match.Simulation.Fighters[1].Position;
                for (int tick = 0; tick < 9000 && match.Simulation.Phase != MatchPhase.Finished; tick++)
                {
                    int previousA = match.Simulation.Fighters[0].Motion.Starts, previousB = match.Simulation.Fighters[1].Motion.Starts;
                    match.AdvanceFixedStep(); match.Present(1, match.Simulation.MotionTime, .02f);
                    var sim = match.Simulation;
                    bool progress = Vector2.Distance(a, sim.Fighters[0].Position) + Vector2.Distance(b, sim.Fighters[1].Position) > .10f;
                    if (progress) { a = sim.Fighters[0].Position; b = sim.Fighters[1].Position; stalled = 0; }
                    else if (sim.Phase == MatchPhase.Fighting) stalled += .02f;
                    longest = Mathf.Max(longest, stalled);
                    if (stalled > 5)
                    {
                        report.AppendLine($"STALL arena={arena} tick={tick} accepted={match.Contacts.AcceptedMotionFraction} pair={match.Contacts.LastSeparationPair}");
                        foreach (var state in sim.Fighters) report.AppendLine($"{state.Index}: action={state.Action} pos={state.Position} height={state.Height} ground={state.SurfaceHeight} airborne={state.Airborne} blocked={state.ContactBlockedSeconds} velocity={state.Velocity}");
                        break;
                    }
                    if (tick % 10 == 0) overlap = Mathf.Max(overlap, match.Contacts.MeasurePenetration());
                    if (tick % 500 == 0) meshDepth = Mathf.Max(meshDepth, BeetleImportedValidation.MeasureRenderedMesh(match));
                    foreach (var actor in match.actors)
                    {
                        var state = actor.State;
                        if (state.Motion.Active && state.Motion.Starts > (state.Index == 0 ? previousA : previousB))
                        {
                            string key = state.Index + ":" + state.Motion.Active.name;
                            moduleStarts[key] = moduleStarts.TryGetValue(key, out int n) ? n + 1 : 1;
                        }
                        viewError = Mathf.Max(viewError, Vector3.Distance(actor.transform.position, new Vector3(state.Position.x, state.Height, state.Position.y)));
                        if (!state.Airborne && !state.HasFallen && actor.animator.legs[2].stepping && actor.animator.legs[5].stepping) hindConflicts++;
                        if (!state.Airborne && !state.HasFallen && state.Motion.SampleLeg(2, out _) && state.Motion.SampleLeg(5, out _)) hindConflicts++;
                    }
                    if (tick % 500 == 0) File.WriteAllText(Folder + "/Progress.txt", $"arena={arena} seconds={sim.Elapsed:F1} overlap={overlap:F6} longestStall={longest:F2}");
                }
                report.AppendLine($"arena={arena}: seconds={match.Simulation.Elapsed:F2}, phase={match.Simulation.Phase}, penetration={overlap:F6}, visibleMeshDepth={meshDepth:F5}, longestStationary={longest:F2}, viewError={viewError:F4}, hindConflicts={hindConflicts}");
                report.AppendLine("Visible sample: " + BeetleImportedValidation.WorstVisiblePart);
                foreach (var entry in moduleStarts) report.AppendLine($"  moduleStarts {entry.Key}={entry.Value}");
                foreach (var f in match.Simulation.Fighters)
                    report.AppendLine($"  fighter={f.Index} shoves={f.Shoves} charges={f.Charges} pries={f.Pries} throws={f.Throws} contacts={f.Contacts}");
                File.WriteAllText(Folder + "/Validation.txt", report.ToString());
                Require(overlap < .006f && meshDepth < .025f && longest < 5 && hindConflicts == 0 && viewError < .6f, "Combat/terrain regression on arena " + arena);
            }
            report.AppendLine("ALL COMBAT REFINEMENT CHECKS PASSED");
        }
        finally { File.WriteAllText(Folder + "/Validation.txt", report.ToString()); }
    }
}
