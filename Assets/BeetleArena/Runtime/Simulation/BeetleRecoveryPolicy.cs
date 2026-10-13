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
        // 个体气质只造成小幅错峰，不把索引当作攻防优先权。疲劳后的上限仍可达到。
        public static float Budget(BeetleState b, BeetleMatchSettings s) =>
            Mathf.Lerp(s.recoveryMaximumSeconds, Mathf.Max(s.recoveryMinimumSeconds, s.recoveryMaximumSeconds * .75f), b.Traits.aggression);
        public static bool Ready(BeetleState b, BeetleMatchSettings s, bool pressure)
        {
            if (!IsRest(b.Action)) return false;
            if (pressure && b.RecoveryTime >= .18f) return true;
            return b.RecoveryTime >= Budget(b, s) ||
                (b.RecoveryTime >= s.recoveryMinimumSeconds && b.Stamina >= b.StaminaLimit * s.recoveryResumeFraction && b.BurstReserve >= .44f);
        }
        public static bool WantsRest(BeetleState b, BeetleMatchSettings s, bool regrouping, bool pressure)
        {
            if (pressure || b.RecoveryCooldown > 0) return false;
            return b.Stamina < Mathf.Min(32, b.StaminaLimit * .42f) || b.BurstReserve < .22f ||
                (regrouping && b.Intent != BeetleIntent.Pressure && b.Intent != BeetleIntent.Counter &&
                 b.Stamina < b.StaminaLimit * s.recoveryResumeFraction);
        }
    }
}
