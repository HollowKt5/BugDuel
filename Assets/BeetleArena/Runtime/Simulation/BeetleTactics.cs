using UnityEngine;

namespace BeetleArena
{
    public enum BeetleIntent { Assess, Pressure, Counter, Defend, Reposition, Recover }

    /// <summary>决策只读快照。双方在本步 Think 之前同时取样，避免后一只读到前一只刚选的新动作。</summary>
    public readonly struct BeetleTacticalSnapshot
    {
        public readonly float Advantage, Opening, Distance, Facing, Edge;
        public readonly bool Threatened;
        public BeetleTacticalSnapshot(BeetleState self, BeetleState other, float radius, float reach)
        {
            Vector2 delta = other.Position - self.Position;
            Distance = delta.magnitude; Facing = Vector2.Dot(self.Forward, delta.normalized);
            Edge = self.Position.magnitude / radius;
            // 只看已发生的动作/支撑状态；不预测随机数，也不读取未来命中结果。
            Threatened = other.Committed && Distance < reach + .25f &&
                Vector2.Dot(other.Forward, -delta.normalized) > .55f && other.ActionProgress < .78f;
            Opening = other.ExposureRemaining;
            if (other.Action == BeetleAction.Stagger) Opening = Mathf.Max(Opening, .65f);
            if (BeetleRecoveryPolicy.IsRest(other.Action)) Opening = Mathf.Max(Opening, .38f);
            float condition = .40f * (self.Stamina01 - other.Stamina01) +
                .20f * (self.BurstReserve - other.BurstReserve) + .22f * (self.Balance - other.Balance) +
                .12f * (self.Footing - other.Footing) + .12f * (other.Position.magnitude / radius - Edge);
            Advantage = Mathf.Clamp(condition + .10f * (self.ContactConfidence - other.ContactConfidence), -1, 1);
        }
    }

    /// <summary>
    /// 交锋战术是游戏抽象：借鉴格斗游戏的距离、承诺、收招与反击窗口，不声称甲虫有人类战术思考。
    /// 所有方法无随机源、无场景依赖；只返回建议，由 Simulation 拥有动作与状态写入权。
    /// </summary>
    public static class BeetleTactics
    {
        public static BeetleIntent Evaluate(BeetleState b, in BeetleTacticalSnapshot view)
        {
            if (b.ContactBlockedSeconds > .35f) return BeetleIntent.Reposition;
            if (view.Threatened && (b.Balance < .65f || b.Readiness01 < .30f || b.BurstReserve < .18f))
                return BeetleIntent.Defend;
            if (b.Balance < .40f || view.Edge > .84f && b.Footing < .84f) return BeetleIntent.Reposition;
            if (b.Readiness01 < .27f || b.BurstReserve < .22f)
                return view.Threatened ? BeetleIntent.Defend : BeetleIntent.Recover;
            if (view.Opening > .10f && b.Readiness01 > .31f && b.BurstReserve > .28f && view.Facing > .72f)
                return BeetleIntent.Counter;
            // 滞回让接近临界点时不反复切换攻防；优势只影响选择，不额外增加伤害。
            float threshold = b.Intent == BeetleIntent.Pressure ? .025f : .09f;
            if (!view.Threatened && b.TacticalAdvantage > threshold && b.Balance > .58f && b.BurstReserve > .33f)
                return BeetleIntent.Pressure;
            if (view.Threatened) return BeetleIntent.Defend;
            if (b.TacticalAdvantage < -.10f && b.Readiness01 < .45f) return BeetleIntent.Reposition;
            return BeetleIntent.Assess;
        }

        public static bool CanCommit(BeetleState b, float stamina, float reserve) =>
            b.Stamina >= stamina && b.BurstReserve >= reserve && b.Balance >= .42f;

        // 原随机 choice 仍负责同一情境里的变化；局势先限定可用行为，不靠抽签决定攻守。
        public static BeetleAction Choose(BeetleState b, in BeetleTacticalSnapshot v, ExchangePhase exchange,
            float reach, float choice)
        {
            if (v.Facing < .70f) return BeetleAction.Assess;
            if (b.Intent == BeetleIntent.Defend)
                return v.Edge < .72f && choice < .20f ? BeetleAction.Disengage : BeetleAction.Brace;
            if (b.Intent == BeetleIntent.Reposition)
                return b.Balance < .32f ? BeetleAction.Brace : BeetleAction.Circle;
            // 恢复冷却中也应卸力换位，不能因暂时不能 Rest 就再次承诺高消耗攻击。
            if (b.Intent == BeetleIntent.Recover)
                return BeetleAction.Circle;
            if (v.Distance > reach) return BeetleAction.Approach;
            bool opening = b.Intent == BeetleIntent.Counter;
            bool pressure = b.Intent == BeetleIntent.Pressure;
            if (!CanCommit(b, 18, .20f)) return choice < .40f ? BeetleAction.Circle : BeetleAction.Probe;
            if (!opening && !pressure && exchange != ExchangePhase.Clash)
                return choice < .18f ? BeetleAction.Circle : BeetleAction.Probe;
            if (!opening && choice < (pressure ? .08f : .15f)) return BeetleAction.Circle;
            // 抓收招先用短推；顶举需要更多余力与已建立的接触，不每次都选最贵攻击。
            if (opening && choice < .40f) return b.TouchingOpponent || v.Distance < reach - .2f ? BeetleAction.ShoveWindup : BeetleAction.Approach;
            if (b.PryRetryRemaining <= 0 && b.Contacts >= 2 && v.Distance < reach - .06f && CanCommit(b, 26, .35f) && choice < .78f)
                return BeetleAction.PryWindup;
            if (!b.TouchingOpponent && CanCommit(b, 32, .48f) && choice > .48f)
                return BeetleAction.ChargeWindup;
            return BeetleAction.ShoveWindup;
        }
    }
}
