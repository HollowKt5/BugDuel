using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using BeetleArena;

// 供 Unity -batchmode -executeMethod 调用的入口集合，成功/失败分别用退出码 0/1。
// CollisionOnly/ValidateOnly 不构建玩家程序；只有显式调用 BuildWindows 才会打包。
public static class BeetleArenaBatch
{
    // 准备场景后运行实际网格接触、挑飞与完整对局验证，然后退出编辑器。
    public static void CollisionOnly()
    {
        try { Prepare(); BeetleArenaCollisionValidation.Run(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    // 遍历开局时间、升级时长和后期推力组合，每组跑 64 个纯数据种子，输出节奏统计 CSV。
    // 不接入场景碰撞，因此这些结果只能用于粗调，不能代替实际模型对局时长。
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
    // 较早的半径/推顶/抬举组合扫描工具；参数网格是实验值，不会直接修改共享比赛资产。
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
    // 生成/刷新场景后只运行纯数据规则验证，适合检查体型、鼓励、数值边界和落台规则。
    public static void ValidateOnly()
    {
        try { Prepare(); BeetleArenaValidation.Run(); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
    // 显式的 Windows 打包入口：先跑数据与碰撞验证，再构建开发版并保存构建报告。
    // 会修改 PlayerSettings；--beetleOutput 可指定 exe 输出位置。
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
    // 准备过程会把已有 MatchSettings 的部分旧参数恢复为此处基准值，并重建游戏场景。
    // 因此它有资源写入副作用，不是只读验证；批处理先打开 SampleScene 避免重建已加载的目标场景。
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
