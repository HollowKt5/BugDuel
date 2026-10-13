using System;
using System.IO;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEngine;

/// <summary>恢复出口回归。测试真实 Step/状态链，避免只验证一个策略布尔函数。</summary>
public static class BeetleRecoveryValidation
{
    static void Require(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    static BeetleSimulation Ready(BeetleMatchSettings s)
    {
        var sim = new BeetleSimulation(s, 71);
        while (sim.Phase == MatchPhase.Countdown) sim.Step(.02f);
        var a = sim.Fighters[0]; var b = sim.Fighters[1];
        a.Position = new Vector2(-2, 0); b.Position = new Vector2(2, 0);
        a.Heading = 90; b.Heading = -90;
        a.Action = BeetleAction.Recover; a.ActionTime = 0; a.ActionDuration = .25f;
        b.Action = BeetleAction.Assess; b.ActionTime = 0; b.ActionDuration = 10;
        return sim;
    }
    [MenuItem("Tools/Beetle Arena/Validate Recovery Pacing")]
    public static void Run()
    {
        using var fixture = new BeetleValidationScene("RecoveryValidation");
        var s = fixture.Settings;
        var report = new StringBuilder("RECOVERY CONTRACTS\n" + DateTimeOffset.Now.ToString("o") + "\n");
        Directory.CreateDirectory("Artifacts/PacingReview");
        try
        {
            // 关闭体力恢复，确保退出来自上限，而不是偶然回满体力。
            s.staminaRecovery = 0;
            var sim = Ready(s); var a = sim.Fighters[0]; a.Stamina = 1;
            float longest = 0;
            for (int i = 0; i < 250; i++)
            { sim.Step(.02f); longest = Mathf.Max(longest, a.RecoveryTime); }
            Require(longest <= s.recoveryMaximumSeconds + .04f, "Recovery renewals bypass the duration budget");
            report.AppendLine($"PASS: repeated Recover/Retreat decisions stay within individual budget; max={longest:F2}s.");

            sim = Ready(s); a = sim.Fighters[0]; a.Stamina = a.StaminaLimit * .6f;
            for (int i = 0; i < 24; i++) sim.Step(.02f);
            Require(!BeetleRecoveryPolicy.IsRest(a.Action), "Healthy beetle waits for old 84% threshold");
            Require(a.RecoveryCooldown > 0, "Recovery exit has no retry cooldown");
            report.AppendLine("PASS: partial energy recovery exits promptly and starts a retry cooldown.");

            sim = Ready(s); a = sim.Fighters[0]; var b = sim.Fighters[1]; a.Stamina = 1;
            a.Position = new Vector2(-1.3f, 0); b.Position = new Vector2(1.3f, 0);
            b.Action = BeetleAction.ChargeWindup; b.ActionTime = 0; b.ActionDuration = 1;
            for (int i = 0; i < 12; i++) sim.Step(.02f);
            Require(a.Action == BeetleAction.Brace && a.RecoveryCooldown > 0, "Threat cannot interrupt rest");
            report.AppendLine("PASS: near windup interrupts resting into Brace in <=0.24s, without forced attack.");

            // 状态机可以改变 AI 决策，但不能抢占已经开始的托举/冲顶链。
            sim = Ready(s); a = sim.Fighters[0]; a.Action = BeetleAction.ChargeWindup;
            a.ActionDuration = .6f; a.ActionTime = 0; a.Stamina = 1; a.RecoveryTime = 100;
            sim.Step(.02f);
            Require(a.Action == BeetleAction.ChargeWindup, "Rest policy interrupted a committed attack");
            report.AppendLine("PASS: committed windup remains owned by the attack state machine.");
            report.AppendLine("ALL RECOVERY CHECKS PASSED");
        }
        finally { File.WriteAllText("Artifacts/PacingReview/Contracts.txt", report.ToString()); }
    }
}
