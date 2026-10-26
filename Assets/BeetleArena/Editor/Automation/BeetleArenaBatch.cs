using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using BeetleArena;

// Entry point collection for Unity -batchmode -executeMethod; returns exit code 0 on success, 1 on failure.
// CollisionOnly and ValidateOnly do not build player executables; packaging occurs only upon explicit BuildWindows invocation.
public static class BeetleArenaBatch
{
    // Prepares the scene, runs mesh contact, throw, and full match validation, then exits editor.
    public static void CollisionOnly()
    {
        try { Prepare(); BeetleArenaCollisionValidation.Run(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    // Sweeps permutations of opening duration, escalation duration, and late push multipliers, running 64 headless seeds per group to output pacing CSV.
    // Bypasses scene collisions; results serve for rough tuning only and cannot substitute physical match durations.
    public static void PacingSweep()
    {
        try
        {
            var settings = ScriptableObject.CreateInstance<BeetleMatchSettings>();
            string results = "Opening,Ramp,FinalForce,Finished,Under60,In60to180,Mean,Minimum,Maximum\n";
            foreach (float opening in new[] { 75f, 90f })
            foreach (float ramp in new[] { 60f, 90f })
            foreach (float final in new[] { 1.8f, 2.4f })
            {
                settings.openingSeconds = opening; settings.escalationSeconds = ramp; settings.openingDisplacement = .10f; settings.finalDisplacement = final;
                int finished = 0, shortGames = 0, inRange = 0; float total = 0, minimum = 300, maximum = 0;
                for (int seed = 0; seed < 64; seed++)
                {
                    var sim = new BeetleSimulation(settings, seed * 7919 + 31);
                    while (sim.Phase == MatchPhase.Countdown) sim.Step(.02f);
                    for (int tick = 0; tick < 15000 && sim.Phase != MatchPhase.Finished; tick++)
                    {
                        if (seed % 3 == 1 && tick % 310 == 0) sim.Encourage(0);
                        if (seed % 3 == 2 && tick % 310 == 0) sim.Encourage(1);
                        sim.Step(.02f);
                    }
                    if (sim.Phase == MatchPhase.Finished) finished++;
                    if (sim.Elapsed < 60) shortGames++;
                    if (sim.Elapsed >= 60 && sim.Elapsed <= 180) inRange++;
                    total += sim.Elapsed; minimum = Mathf.Min(minimum, sim.Elapsed); maximum = Mathf.Max(maximum, sim.Elapsed);
                }
                results += $"{opening},{ramp},{final},{finished},{shortGames},{inRange},{total / 64:F1},{minimum:F1},{maximum:F1}\n";
            }
            Directory.CreateDirectory("Artifacts/BeetleArena");
            File.WriteAllText("Artifacts/BeetleArena/PacingSweep.csv", results);
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    // Early radius/push/lift permutation scanner; parameter grid contains experimental values and does not mutate shared match assets directly.
    public static void TuningSweep()
    {
        try
        {
            var settings = ScriptableObject.CreateInstance<BeetleMatchSettings>();
            string results = "Radius,Shove,Pry,Finished,Mean,Maximum\n";
            foreach (float radius in new[] { 3.8f, 3.6f })
            foreach (float shove in new[] { 2.7f, 3f })
            foreach (float pry in new[] { 4.3f, 4.8f })
            {
                settings.arenaRadius = radius; settings.shoveImpulse = shove; settings.pryImpulse = pry;
                int finished = 0; float total = 0, maximum = 0;
                for (int seed = 0; seed < 64; seed++)
                {
                    var sim = new BeetleSimulation(settings, seed * 7919 + 31);
                    for (int tick = 0; tick < 15160 && sim.Phase != MatchPhase.Finished; tick++)
                    {
                        if (seed % 3 == 1 && tick % 310 == 0) sim.Encourage(0);
                        if (seed % 3 == 2 && tick % 310 == 0) sim.Encourage(1);
                        sim.Step(.02f);
                    }
                    if (sim.Phase == MatchPhase.Finished) finished++;
                    total += sim.Elapsed; maximum = Mathf.Max(maximum, sim.Elapsed);
                }
                results += $"{radius},{shove},{pry},{finished},{total / 64:F1},{maximum:F1}\n";
            }
            Directory.CreateDirectory("Artifacts/BeetleArena");
            File.WriteAllText("Artifacts/BeetleArena/TuningSweep.csv", results);
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    // Generates/refreshes scene and executes headless rules validation only; validates sizing, encouragement, numerical bounds, and ring-out rules.
    public static void ValidateOnly()
    {
        try { Prepare(); BeetleArenaValidation.Run(); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
    // Explicit Windows build entry: executes data and collision validation, builds development player, and writes build report.
    // Mutates PlayerSettings; --beetleOutput specifies executable output path.
    public static void BuildWindows()
    {
        try
        {
            Prepare(); BeetleArenaValidation.Run(); BeetleArenaCollisionValidation.Run();
            string output = Path.GetFullPath("Builds/BeetleArena/BeetleArena.exe");
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-beetleOutput") output = args[i + 1];
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            PlayerSettings.productName = "甲角 - 林间擂台";
            PlayerSettings.defaultScreenWidth = 1600; PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { BeetleArenaSceneBuilder.ScenePath }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Windows build failed: " + report.summary.result);
            File.WriteAllText("Artifacts/BeetleArena/BuildValidation.txt", "Windows standalone build passed.\n" + output + "\n" + report.summary.totalSize + " bytes");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
    // Preparation resets select parameters in existing MatchSettings to baseline values and rebuilds the game scene.
    // Has asset-write side effects (not read-only validation); opens SampleScene first to avoid rebuilding an actively loaded target scene.
    static void Prepare()
    {
        if (Application.isBatchMode) EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var settings = AssetDatabase.LoadAssetAtPath<BeetleMatchSettings>(BeetleArenaSceneBuilder.AssetFolder + "/MatchSettings.asset");
        if (settings)
        {
            settings.arenaRadius = 5.2f; settings.spawnDistance = 2.3f;
            settings.shoveImpulse = 2.7f; settings.pryImpulse = 4.3f; settings.fatiguePerSecond = .12f;
            settings.openingSeconds = 90; settings.escalationSeconds = 90;
            settings.openingDisplacement = .10f; settings.finalDisplacement = 2.4f;
            settings.initialSeed = 0;
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
        }
        BeetleArenaSceneBuilder.Build();
    }
}
