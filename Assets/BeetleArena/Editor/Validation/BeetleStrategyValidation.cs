using System;
using System.IO;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEngine;

/// <summary>战略/体力合约：用真实 Step 验证状态机、打空窗口、低体力选择及暂停式零步采样。</summary>
public static class BeetleStrategyValidation
{
    static void Require(bool value, string reason) { if (!value) throw new Exception(reason); }
    static BeetleSimulation Ready(BeetleMatchSettings settings)
    {
        var sim = new BeetleSimulation(settings, 71);
        while (sim.Phase == MatchPhase.Countdown) sim.Step(.02f);
        var a = sim.Fighters[0]; var b = sim.Fighters[1];
        a.Position = new Vector2(-1.25f, 0); b.Position = new Vector2(1.25f, 0);
        a.Heading = 90; b.Heading = -90;
        a.Action = b.Action = BeetleAction.Assess;
        a.ActionTime = b.ActionTime = 0; a.ActionDuration = 0; b.ActionDuration = 10;
        a.IntentTime = b.IntentTime = 1;
        return sim;
    }
    [MenuItem("Tools/Beetle Arena/Validate Strategy And Energy")]
    public static void Run()
    {
        using var fixture = new BeetleValidationScene("StrategyValidation");
        var s = fixture.Settings; s.motionModules = Array.Empty<BeetleMotionModule>();
        var report = new StringBuilder("STRATEGY / ENERGY CONTRACTS\n" + DateTimeOffset.Now.ToString("o") + "\n");
        try
        {
            // 给对手一个已经可见的收招窗口；无需等待全局 Clash，也不直接指定胜者。
            var sim = Ready(s); var a = sim.Fighters[0]; var b = sim.Fighters[1];
            b.ExposureRemaining = .6f; b.Stamina = 25; a.Stamina = 85;
            sim.Step(.02f);
            Require(a.Intent == BeetleIntent.Counter && (a.Action == BeetleAction.ShoveWindup || a.Action == BeetleAction.ChargeWindup), "Fresh actor ignores visible opening during Probing");
            report.AppendLine("PASS: visible recovery opening permits a counter during Probing.");

            sim = Ready(s); a = sim.Fighters[0]; b = sim.Fighters[1];
            a.Stamina = 12; a.BurstReserve = .12f;
            b.Action = BeetleAction.ChargeWindup; b.ActionDuration = .9f;
            sim.Step(.02f);
            Require(a.Intent == BeetleIntent.Defend && (a.Action == BeetleAction.Brace || a.Action == BeetleAction.Disengage), "Exhausted defender chooses expensive attack");
            report.AppendLine("PASS: exhausted threatened actor braces or unloads instead of attacking.");

            sim = Ready(s); a = sim.Fighters[0]; b = sim.Fighters[1];
            a.Stamina = 23; a.BurstReserve = .60f; a.RecoveryCooldown = .5f;
            var recoveringView = new BeetleTacticalSnapshot(a, b, s.arenaRadius, 2.85f);
            a.Intent = BeetleTactics.Evaluate(a, recoveringView);
            Require(a.Intent == BeetleIntent.Recover &&
                BeetleTactics.Choose(a, recoveringView, ExchangePhase.Clash, 2.85f, .4f) == BeetleAction.Circle,
                "Recovery cooldown permits an expensive attack while trying to unload");
            report.AppendLine("PASS: recovery intent unloads during rest cooldown without bypassing its budget.");

            a.Fatigue = 100; a.Stamina = a.StaminaLimit; a.BurstReserve = .8f;
            b.ExposureRemaining = .6f;
            var tiredView = new BeetleTacticalSnapshot(a, b, s.arenaRadius, 2.85f);
            Require(BeetleTactics.Evaluate(a, tiredView) == BeetleIntent.Counter && a.Stamina01 < .31f,
                "Fully recovered tired actor can never recognize a counter window");
            report.AppendLine("PASS: fatigue retains weaker force without permanently disabling readiness decisions.");

            sim = Ready(s); a = sim.Fighters[0]; b = sim.Fighters[1];
            a.Action = BeetleAction.Pry; a.ActionDuration = a.ActionTime = 2.15f;
            sim.Step(.02f);
            Require(a.Action == BeetleAction.Circle && a.PryRetryRemaining > 3 && a.ExposureRemaining > .5f,
                "Failed pry does not change angle or record its exposed retry interval");
            a.Contacts = 5; a.Intent = BeetleIntent.Pressure; a.Stamina = 80; a.BurstReserve = 1;
            var retryView = new BeetleTacticalSnapshot(a, b, s.arenaRadius, 2.85f);
            Require(BeetleTactics.Choose(a, retryView, ExchangePhase.Clash, 2.85f, .5f) != BeetleAction.PryWindup,
                "Failed support immediately retries the same pry");
            report.AppendLine("PASS: failed pry exposes recovery, changes angle and temporarily excludes another pry.");

            sim = Ready(s); a = sim.Fighters[0];
            a.Action = BeetleAction.ShoveWindup; a.ActionDuration = 1; a.ContactBlockedSeconds = .7f;
            sim.Step(.02f);
            Require(a.Intent == BeetleIntent.Reposition && a.Action == BeetleAction.Disengage,
                "Sustained geometric blocking cannot request an unloading step");
            report.AppendLine("PASS: blocked contact abandons the stalled drive through normal locomotion.");

            sim = Ready(s); a = sim.Fighters[0];
            a.Action = BeetleAction.Shove; a.ActionDuration = .34f; a.ActionTime = .34f; a.AttackApplied = false;
            sim.Step(.02f);
            Require(a.ExposureRemaining >= s.missedAttackExposure - .03f && a.Action == BeetleAction.Brace, "Missed strike lacks recovery window");
            report.AppendLine("PASS: a missed strike retains an exposed recovery window.");

            // 连续强发力耗尽爆发余量；卸载才可重新招募。只调动作，不向体力强行灌值。
            sim = Ready(s); a = sim.Fighters[0]; b = sim.Fighters[1];
            b.Position = new Vector2(50, 0); a.Position = Vector2.zero;
            a.Action = BeetleAction.HornLock; a.ActionTime = 0; a.ActionDuration = 5;
            // 以 Charge 测实际每秒消耗；大擂台避免测试被落台结算截断。
            s.arenaRadius = 100; a.Action = BeetleAction.Charge; a.ActionDuration = 3;
            for (int i = 0; i < 100; i++) sim.Step(.02f);
            float used = a.BurstReserve;
            Require(used < .4f && a.Stamina < 55, "Sustained attack does not consume reserve and stamina");
            a.Action = BeetleAction.Recover; a.ActionDuration = 3; a.ActionTime = 0; a.RecoveryTime = 0;
            float energyBefore = a.Stamina;
            for (int i = 0; i < 85; i++) sim.Step(.02f);
            Require(a.BurstReserve > used + .15f && a.Stamina > energyBefore, "Unloaded recovery fails to restore resources");
            Require(a.Stamina <= a.StaminaLimit + .001f && a.BurstReserve <= 1, "Energy exceeded bounds");
            report.AppendLine("PASS: real attack chain spends both resources; unloading restores them within bounds.");

            var v0 = new BeetleTacticalSnapshot(a, b, s.arenaRadius, 2.85f);
            var v1 = new BeetleTacticalSnapshot(b, a, s.arenaRadius, 2.85f);
            Require(Mathf.Abs(v0.Advantage + v1.Advantage) < .00001f, "Relative assessment has actor-index bias");
            float oldEnergy = a.Stamina, oldReserve = a.BurstReserve;
            for (int i = 0; i <= 100; i++)
            {
                float p = i / 100f;
                float swing = InsectMuscleAnimation.Swing(p), recruitment = InsectMuscleAnimation.Recruit(p);
                Require(swing >= 0 && swing <= 1 && recruitment >= 0 && recruitment <= 1.001f, "Unbounded muscle animation");
            }
            Require(a.Stamina == oldEnergy && a.BurstReserve == oldReserve, "Animation modified energy");
            Require(InsectMuscleAnimation.Recruit(.35f) - InsectMuscleAnimation.Recruit(.27f) > .7f, "Fast recruitment has no distinct burst");
            report.AppendLine("PASS: symmetric assessment, bounded burst curves, animation sampling cannot spend energy.");
            report.AppendLine("ALL STRATEGY CHECKS PASSED");
        }
        finally
        {
            Directory.CreateDirectory("Artifacts/StrategyReview");
            File.WriteAllText("Artifacts/StrategyReview/Contracts.txt", report.ToString());
        }
    }
}
