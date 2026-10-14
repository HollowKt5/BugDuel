using UnityEngine;

namespace BeetleArena
{
    /// <summary>
    /// 个体恢复的无状态规则：不碰 Transform、不抽随机数、不修改状态。
    /// 文献支持接触评估/退让/追赶；具体体力阈值和秒数是游戏参数（TechnicalGuide §22）。
    /// </summary>
    public static class BeetleRecoveryPolicy
    {
        public static bool IsRest(BeetleAction action) => action == BeetleAction.Recover || action == BeetleAction.Retreat;
        // 稳定的个体节奏/谨慎偏好决定重整时长，不把索引当作攻防优先权。
        public static float Budget(BeetleState b, BeetleMatchSettings s) =>
            Mathf.Max(s.recoveryMinimumSeconds, s.recoveryMaximumSeconds * Mathf.Lerp(.55f, 1.10f, b.Traits.lateralPreference) / b.Traits.tempo);
        public static bool Ready(BeetleState b, BeetleMatchSettings s, bool pressure)
        {
            if (!IsRest(b.Action)) return false;
            if (pressure && b.RecoveryTime >= .18f) return true;
            return b.RecoveryTime >= Budget(b, s) ||
                (b.RecoveryTime >= s.recoveryMinimumSeconds && b.Stamina >= b.StaminaLimit *
                    Mathf.Clamp(s.recoveryResumeFraction + (b.Traits.lateralPreference - .5f) * .32f, .35f, .75f) && b.BurstReserve >= .40f);
        }
        public static bool WantsRest(BeetleState b, BeetleMatchSettings s, bool regrouping, bool pressure)
        {
            if (pressure || b.RecoveryCooldown > 0) return false;
            return b.Readiness01 < Mathf.Lerp(.39f, .23f, b.Traits.aggressionScore / 100) || b.BurstReserve < .20f ||
                (regrouping && b.Intent != BeetleIntent.Pressure && b.Intent != BeetleIntent.Counter &&
                 b.Readiness01 < Mathf.Lerp(.48f, .33f, b.Traits.aggression));
        }
    }
}
