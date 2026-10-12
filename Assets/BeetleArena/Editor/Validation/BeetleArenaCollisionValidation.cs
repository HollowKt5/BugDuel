using System;
using System.IO;
using System.Linq;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 实际场景接触验证：覆盖强制重叠、挑飞概率与边界、落地/落台、完整自动对局。
// 输出到 Artifacts/BeetleArena；采样通过不代表所有随机种子和极端姿态都绝对无穿透。
public static class BeetleArenaCollisionValidation
{
    [MenuItem("Tools/Beetle Arena/Validate Clash Actions")]
    // 菜单入口：复制游戏场景并以 Additive 加载，在副本上运行测试，finally 恢复原活动场景。
    // 测试还会调用 RefreshPresentation 保存参数约束和字形资源，因此不是完全只读操作。
    public static void RunInEditor()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop play before validation.");
        // 临时场景资产路径；结束时通过 AssetDatabase 删除，保持资源数据库与磁盘一致。
        const string temporary = "Assets/BeetleArena/Generated/ClashValidationScene.unity";
        var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!AssetDatabase.CopyAsset(BeetleArenaSceneBuilder.ScenePath, temporary))
            throw new InvalidOperationException("Could not create isolated validation scene.");
        var scene = EditorSceneManager.OpenScene(temporary, OpenSceneMode.Additive);
        try
        {
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            BeetleArenaSceneBuilder.RefreshPresentation();
            var match = BeetleSceneHierarchy.FindMatch(scene);
            if (!match) throw new InvalidOperationException("Validation scene has no match controller.");
            RunChecks(match);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
            AssetDatabase.DeleteAsset(temporary);
        }
    }
    // 批处理入口：直接打开游戏场景并验证；会替换当前加载场景，交互使用应选 RunInEditor。
    public static void Run()
    {
        EditorSceneManager.OpenScene(BeetleArenaSceneBuilder.ScenePath);
        var match = UnityEngine.Object.FindFirstObjectByType<BeetleMatchController>();
        RunChecks(match);
    }
    // 验证主流程：绑定 HUD，先做静态重叠与挑飞专项，再做四场真实接触对局。
    static void RunChecks(BeetleMatchController match)
    {
        match.hud.Bind(match);
        var report = new StringBuilder("ARTICULATED COLLISION VALIDATION\n");
        Directory.CreateDirectory("Artifacts/BeetleArena");
        string path = "Artifacts/BeetleArena/CollisionValidation.txt";
        int shapeCount = match.actors[0].GetComponent<BeetleContactRig>().shapes.Length;
        Require(shapeCount >= 24 && match.actors.All(a => a.animator.importedRig), "Missing imported model collision rig");
        report.AppendLine($"Imported-model collision proxies per beetle: {shapeCount}; arena colliders: {match.arenaContacts.Length}.");
        // 四个相对朝向×十种动作构造 40 组明显重叠，要求重复求解后深度小于 0.003 世界单位。
        foreach (float angle in new[] { 0f, 45f, 90f, 180f })
        foreach (var action in new[] { BeetleAction.Brace, BeetleAction.PryWindup, BeetleAction.Pry, BeetleAction.Stagger,
            BeetleAction.Probe, BeetleAction.ChargeWindup, BeetleAction.Charge, BeetleAction.HornLock, BeetleAction.Toss, BeetleAction.Launched })
        {
            match.StartMatch(71);
            for (int i = 0; i < 2; i++)
            {
                var b = match.Simulation.Fighters[i];
                b.Position = new Vector2(i == 0 ? -.75f : .75f, 0);
                b.Heading = i == 0 ? 90 : -90 + angle;
                b.Action = action; b.ActionTime = .5f; b.ActionDuration = 1;
                b.RollVisual = 20;
            }
            for (int tick = 0; tick < 20; tick++) match.Contacts.Solve(match.Simulation, .02f);
            Require(match.Contacts.MeasurePenetration() < .003f, "Forced overlap not resolved: " + angle + " / " + action);
        }
        report.AppendLine("PASS: 40 forced overlaps across four orientations, including toss/airborne poses.");
        ValidateToss(match, report);
        File.WriteAllText(path, report.ToString());
        float min = 300, max = 0, total = 0, penetration = 0;
        int completed = 0, contacts = 0;
        int probes = 0, charges = 0, locks = 0;
        // 统计普通 AI 对局里的合格挑飞机会，防止功能只在强制测试设置下可触发。
        int naturalTossOpportunities = 0;
        bool capturedProbe = false, capturedCharge = false, capturedLock = false;
        // 完整场次用于有限回归覆盖；验证墙钟耗时包含加速循环、截图和检查，不能当作游戏帧率。
        const int samples = 4;
        for (int sample = 0; sample < samples; sample++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int seed = sample == 0 ? 7319 : sample * 7919 + 31;
            match.StartMatch(seed);
            while (match.Simulation.Phase == MatchPhase.Countdown) match.Simulation.Step(.02f);
            for (int tick = 0; tick < 15000 && match.Simulation.Phase != MatchPhase.Finished; tick++)
            {
                if (sample % 4 != 0 && tick % 310 == 0) match.Simulation.Encourage(sample % 2);
                foreach (var actor in match.actors) actor.BeforeStep();
                match.Simulation.Step(.02f);
                if (sample == 0)
                    foreach (var b in match.Simulation.Fighters)
                    {
                        if (!capturedProbe && b.Action == BeetleAction.Probe && b.AttackApplied)
                        { Capture(match, "Exchange-Probe.png"); capturedProbe = true; }
                        if (!capturedCharge && b.Action == BeetleAction.Charge && b.AttackApplied)
                        { Capture(match, "Exchange-Charge.png"); capturedCharge = true; }
                        if (!capturedLock && b.Action == BeetleAction.HornLock && b.ActionProgress > .5f)
                        { Capture(match, "Exchange-Lock.png"); capturedLock = true; }
                    }
                if (tick % 1000 == 0)
                    File.WriteAllText("Artifacts/BeetleArena/CollisionProgress.txt", $"sample={sample}; elapsed={match.Simulation.Elapsed:F1}; actions={match.Simulation.Fighters[0].Action}/{match.Simulation.Fighters[1].Action}; distance={Vector2.Distance(match.Simulation.Fighters[0].Position, match.Simulation.Fighters[1].Position):F2}; contacts={match.Contacts.ContactFrames}; CPU wall={watch.Elapsed.TotalSeconds:F1}s");
                if (sample == 0 && (tick == 450 || tick == 1800 || tick == 3500))
                    Capture(match, "ContactCloseup-" + tick + ".png");
                // Check the rendered interpolation, not just fixed-step endpoints.
                // 每十步抽样检查 alpha=0.5 的插值姿态；不是穷举所有插值系数和每个渲染帧。
                if (tick % 10 == 0)
                {
                    foreach (var actor in match.actors) actor.Render(.5f, match.Simulation.Elapsed, .02f);
                    match.Contacts.ProjectView(match.Simulation);
                    float measured = match.Contacts.MeasurePenetration();
                    if (measured > .003f) report.AppendLine($"Overlap at {seed}/{tick}: {measured:F6} ({match.Contacts.LastWorstContact})");
                    penetration = Mathf.Max(penetration, measured);
                }
            }
            var sim = match.Simulation;
            if (sim.Phase == MatchPhase.Finished) completed++;
            min = Mathf.Min(min, sim.Elapsed); max = Mathf.Max(max, sim.Elapsed); total += sim.Elapsed;
            contacts += match.Contacts.ContactFrames;
            foreach (var b in sim.Fighters) { probes += b.ProbeTouches; charges += b.Charges; locks += b.Locks; }
            foreach (var b in sim.Fighters) naturalTossOpportunities += b.TossOpportunities;
            penetration = Mathf.Max(penetration, match.Contacts.MaximumResidual);
            report.AppendLine($"Seed {seed}: {sim.Phase}, {sim.Elapsed:F1}s, winner={sim.Winner}, shoves={sim.Fighters[0].Shoves + sim.Fighters[1].Shoves}, pries={sim.Fighters[0].Pries + sim.Fighters[1].Pries}, residual={match.Contacts.MaximumResidual:F6}, validation wall={watch.Elapsed.TotalSeconds:F1}s.");
            report.AppendLine($"  Probe touches={sim.Fighters[0].ProbeTouches + sim.Fighters[1].ProbeTouches}, charges={sim.Fighters[0].Charges + sim.Fighters[1].Charges}, horn locks={sim.Fighters[0].Locks + sim.Fighters[1].Locks}.");
            report.AppendLine($"  Tosses={sim.Fighters[0].Tosses + sim.Fighters[1].Tosses}, eligible pries={sim.Fighters[0].TossOpportunities + sim.Fighters[1].TossOpportunities}.");
            File.WriteAllText(path, report.ToString());
        }
        report.AppendLine($"Actual articulated matches: {completed}/{samples} finished. Duration min={min:F1}s mean={total / samples:F1}s max={max:F1}s. Contact frames={contacts}. Worst residual={penetration:F6} world units.");
        File.WriteAllText(path, report.ToString());
        Require(completed == samples, "Contact-constrained matches failed to finish");
        Require(contacts > 0 && penetration < .003f, "Visible inter-beetle penetration");
        Require(probes > 0 && charges > 0 && locks > 0, "New exchanges never made actual contact");
        // 这里只要求自然对局存在判定机会；小概率事件不保证四场里必然抽中。
        Require(naturalTossOpportunities > 0, "Ordinary matches never allow a toss opportunity");
        report.AppendLine("ALL COLLISION CHECKS PASSED"); File.WriteAllText(path, report.ToString());
    }
    // 复制设置后执行概率和极端条件测试；对临时副本将概率改为 0/1，避免污染实际比赛资产。
    // finally 恢复原设置并释放副本，即使断言失败也执行清理。
    static void ValidateToss(BeetleMatchController match, StringBuilder report)
    {
        var original = match.settings;
        var settings = UnityEngine.Object.Instantiate(original);
        try
        {
            int successes = 0;
            // 构造 512 次合格抬举，检查低概率分布和单次攻击只抽一次；范围按默认 8% 调参设计。
            // 如果人为大改资产里的概率，频率断言需要相应调整，它不是通用概率参数测试。
            for (int seed = 0; seed < 512; seed++)
            {
                var sim = new BeetleSimulation(settings, seed);
                while (sim.Phase == MatchPhase.Countdown) sim.Step(.02f);
                PreparePry(sim, false);
                sim.Step(.02f);
                Require(sim.Fighters[0].TossOpportunities == 1, "Missing qualified pry");
                FinishSupportedLift(sim);
                successes += sim.Fighters[0].Tosses;
                sim.Step(.02f);
                Require(sim.Fighters[0].TossOpportunities == 1, "Toss rerolled on a later frame");
            }
            Require(successes > 10 && successes < 75, "Default toss frequency is not rare");
            report.AppendLine($"PASS: default odds produced {successes}/512 tosses on qualified pries; one roll per attack.");
            settings.tossChance = 0;
            var disabled = new BeetleSimulation(settings, 71);
            while (disabled.Phase == MatchPhase.Countdown) disabled.Step(.02f);
            PreparePry(disabled, false); disabled.Step(.02f);
            FinishSupportedLift(disabled);
            Require(disabled.Fighters[0].Tosses == 0, "Zero probability still tossed");
            Require(disabled.Fighters[0].Throws > 0, "Zero burst probability incorrectly disabled ordinary throws");
            settings.tossChance = 1;
            // 把概率设为 1 后逐项破坏条件，验证轻触、腾空、低体力、自身不稳、冷却、无接触均不能挑飞。
            foreach (string blocked in new[] { "probe", "airborne", "tired", "unstable", "cooldown", "no contact" })
            {
                var sim = new BeetleSimulation(settings, 71);
                while (sim.Phase == MatchPhase.Countdown) sim.Step(.02f);
                PreparePry(sim, false);
                var a = sim.Fighters[0]; var b = sim.Fighters[1];
                if (blocked == "probe") a.Action = BeetleAction.Probe;
                if (blocked == "airborne") b.Airborne = true;
                if (blocked == "tired") a.Stamina = 5;
                if (blocked == "unstable") a.Balance = .2f;
                if (blocked == "cooldown") a.TossCooldown = 10;
                if (blocked == "no contact") sim.ContactSolver = new NoContact();
                sim.Step(.02f);
                FinishSupportedLift(sim);
                Require(a.Tosses == 0, "Invalid toss: " + blocked);
            }
            report.AppendLine("PASS: burst gates remain valid; zero burst probability still allows ordinary lever throws.");
            match.settings = settings;
            // 在真实碰撞模型中分别设置台中央与台边的抬举，检验落回台面和飞出台边两条路径。
            foreach (bool edge in new[] { false, true })
            {
                match.StartMatch(71);
                while (match.Simulation.Phase == MatchPhase.Countdown) match.Simulation.Step(.02f);
                PreparePry(match.Simulation, true);
                if (edge) foreach (var b in match.Simulation.Fighters) b.Position += Vector2.right * 2;
                match.Simulation.SetGround(match.ActiveSurface);
                for(int i=0;i<2;i++)match.actors[i].Bind(match.Simulation.Fighters[i],settings,match.ActiveSurface);
                match.Contacts.Solve(match.Simulation, .02f);
                // 先完整低头入角、建立承重，再等待释放；成功猛挑不再发生在普通接触的第一帧。
                for(int tick=0;tick<300 && match.Simulation.Fighters[0].Tosses==0;tick++)
                {
                    foreach(var actor in match.actors)actor.BeforeStep();
                    match.Simulation.Step(.02f);
                }
                var victim = match.Simulation.Fighters[1];
                Require(match.Simulation.Fighters[0].Tosses == 1 && victim.Airborne, "Connected forced pry did not launch");
                // 刚腾空不能直接判负；必须等实际高度跌到落台阈值才结束比赛。
                Require(!victim.HasFallen && match.Simulation.Phase == MatchPhase.Fighting, "Launch incorrectly awarded victory");
                float peak = 0, depth = 0;
                for (int tick = 0; tick < 150; tick++)
                {
                    foreach (var actor in match.actors) actor.BeforeStep();
                    match.Simulation.Step(.02f);
                    foreach (var actor in match.actors) actor.Render(.5f, match.Simulation.Elapsed, .02f);
                    match.Contacts.ProjectView(match.Simulation);
                    depth = Mathf.Max(depth, match.Contacts.MeasurePenetration());
                    peak = Mathf.Max(peak, victim.Height);
                    if (!edge && tick == 18) Capture(match, "RareToss-Airborne.png");
                }
                Require(peak > .5f && depth < .003f, "Launch height or airborne collision failed");
                // 出手方向由杠杆接触决定，边缘也可能向台内反抛，中央也可能飞出台外。
                Require(victim.HasFallen || !victim.Airborne, "Throw never completed flight");
                if(victim.HasFallen) Require(victim.Height < victim.LastSupportHeight-settings.fallDepth, "Ring-out ignored height threshold");
                report.AppendLine($"PASS: origin={(edge ? "edge" : "center")}, fallen={victim.HasFallen}, peak={peak:F2}, overlap={depth:F6}.");
            }
        }
        finally { match.settings = original; UnityEngine.Object.DestroyImmediate(settings); }
    }
    // 测试夹具：把攻击者放到抬举生效阶段，把防守者设为弱支撑；跳过正常 AI 寻找机会过程。
    // physical 区分真实长角模型与简化身体模型所需的初始距离。
    static void PreparePry(BeetleSimulation sim, bool physical)
    {
        var a = sim.Fighters[0]; var b = sim.Fighters[1];
        a.Position = new Vector2(physical ? -3.05f : -1, 0); b.Position = new Vector2(physical ? 1.05f : 1, 0);
        a.Heading = 90; b.Heading = -90;
        a.Action = physical ? BeetleAction.PryWindup : BeetleAction.Pry;
        a.ActionTime = physical ? 0 : .5f; a.ActionDuration = physical ? .55f : 1;
        b.Action = BeetleAction.Brace; b.ActionTime = 0; b.ActionDuration = 10;
        b.Balance = .3f; b.Stamina = 35;
    }
    // 一次成功托举只抽一次概率，猛挑冲量在脱角时施加；数据测试必须推进完整的承重阶段。
    static void FinishSupportedLift(BeetleSimulation sim)
    {
        for(int tick=0;tick<200 && sim.Fighters[1].Lift.Supported;tick++)sim.Step(.02f);
    }
    // 空求解器测试替身：让模拟走需要接触标记的分支，但不提供接触，用来验证禁止隔空挑飞。
    sealed class NoContact : IBeetleContactSolver { public void Solve(BeetleSimulation simulation, float dt) { } }
    // 统一抛出带原因的测试异常，由菜单桥接或批处理入口负责记录。
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    // 拍摄当前测试姿态的近景。临时设为第 31 层，使相机只看测试场景中的对象。
    // finally 恢复所有层、相机状态、HUD 和渲染目标，并释放临时纹理，避免影响用户场景。
    static void Capture(BeetleMatchController match, string filename)
    {
        var camera = match.spectatorCamera.GetComponent<Camera>();
        var oldPosition = camera.transform.position; var oldRotation = camera.transform.rotation;
        float oldSize = camera.orthographicSize;
        bool hudActive = match.hud.gameObject.activeSelf;
        var a = match.actors[0].transform.position; var b = match.actors[1].transform.position;
        Vector3 focus = (a + b) * .5f + Vector3.up * .55f;
        var target = new RenderTexture(1600, 900, 24);
        var pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        int oldMask = camera.cullingMask;
        var sceneObjects = match.gameObject.scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToArray();
        var oldLayers = sceneObjects.Select(go => go.layer).ToArray();
        try
        {
            // The validation scene is additive: exclude the user's open scene from captures.
            foreach (var go in sceneObjects) go.layer = 31;
            camera.cullingMask = 1 << 31;
            match.hud.gameObject.SetActive(false);
            camera.transform.position = focus + new Vector3(1.2f, 3.2f, 8);
            camera.transform.LookAt(focus); camera.orthographicSize = 2.6f;
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); pixels.Apply();
            File.WriteAllBytes("Artifacts/BeetleArena/" + filename, pixels.EncodeToPNG());
        }
        finally
        {
            camera.cullingMask = oldMask;
            for (int i = 0; i < sceneObjects.Length; i++) sceneObjects[i].layer = oldLayers[i];
            camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
            camera.transform.SetPositionAndRotation(oldPosition, oldRotation); camera.orthographicSize = oldSize;
            match.hud.gameObject.SetActive(hudActive);
            UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
