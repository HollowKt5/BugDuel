using UnityEngine;

namespace BeetleArena
{
    /// <summary>
    /// 耐力与短时爆发分开：不是肌肉生理仿真，而是慢支撑/短促强发力的游戏近似。
    /// Simulation 调用才推进状态；动画与模块只能读取，避免显示帧率改变体力。
    /// </summary>
    public static class BeetleEnergyPolicy
    {
        public static float AttackEffort(BeetleState b) =>
            Mathf.Lerp(.42f, 1, b.Stamina01) * Mathf.Lerp(.82f, 1, b.BurstReserve);

        public static void TickClocks(BeetleState b, float dt)
        {
            b.ExertionCooldown = Mathf.Max(0, b.ExertionCooldown - dt);
            b.ExposureRemaining = Mathf.Max(0, b.ExposureRemaining - dt);
            b.PryRetryRemaining = Mathf.Max(0, b.PryRetryRemaining - dt);
            b.ContactConfidence = Mathf.MoveTowards(b.ContactConfidence, 0, dt * .23f);
            b.IntentTime += dt;
            b.TimeSinceAttack += dt;
            b.ContactReleaseRemaining = Mathf.Max(0, b.ContactReleaseRemaining - dt);
            b.ContactReleaseCooldown = Mathf.Max(0, b.ContactReleaseCooldown - dt);
        }

        public static void Apply(BeetleState b, BeetleMatchSettings s, float cost, bool threatened, float dt)
        {
            bool working = cost >= 5 || b.Action == BeetleAction.Lifting;
            if (working) b.ExertionCooldown = s.exertionRecoveryDelay;
            // 撤开和换位才有较好的恢复；贴身撑地仍可小幅恢复，避免双低体力永久僵持。
            if (cost < 0)
            {
                float factor = b.TouchingOpponent ? s.contactRecoveryMultiplier : 1;
                // 真正卸力恢复比原地撑住有效；否则贴身耗到低能量后无法挣脱恢复死循环。
                if (!threatened && BeetleRecoveryPolicy.IsRest(b.Action)) factor = Mathf.Max(factor, .85f);
                if (threatened) factor *= .55f;
                if (b.ExertionCooldown > 0) factor *= .18f;
                cost *= factor;
            }
            b.Stamina = Mathf.Clamp(b.Stamina - cost * dt, 0, b.StaminaLimit);
            float burstRate = working ? -s.burstDrainPerSecond * Mathf.Clamp(cost / 14, .40f, 1.8f) :
                s.burstRecoveryPerSecond * (b.ExertionCooldown > 0 ? .10f : threatened ? .25f : .85f);
            b.BurstReserve = Mathf.Clamp01(b.BurstReserve + burstRate * dt);
        }

        // 命中结算的一次性受压；绝不每个碰撞帧扣除，鼓励也不直接填满爆发储备。
        public static void ReceiveContact(BeetleState attacker, BeetleState defender, float intensity)
        {
            attacker.ContactConfidence = Mathf.Clamp(attacker.ContactConfidence + intensity * .30f, -1, 1);
            defender.ContactConfidence = Mathf.Clamp(defender.ContactConfidence - intensity * .36f, -1, 1);
            defender.BurstReserve = Mathf.Max(0, defender.BurstReserve - intensity * .07f);
            defender.ExertionCooldown = Mathf.Max(defender.ExertionCooldown, intensity * .18f);
        }
    }
}
