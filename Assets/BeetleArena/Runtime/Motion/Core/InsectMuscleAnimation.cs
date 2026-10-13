using UnityEngine;
namespace BeetleArena
{
    /// <summary>
    /// 肌肉招募启发的动画曲线，不计算肌肉力：慢支撑、短促快速招募、较长卸力。
    /// 参考 Azevedo et al. 2020 eLife 56754（果蝇）、Tóth et al. 2013 PLOS e78246（竹节虫模型）。
    /// 时间均为艺术参数，不能解释为独角仙实测肌肉时长。纯采样不消耗战斗随机数。
    /// </summary>
    public static class InsectMuscleAnimation
    {
        static float Ramp(float a, float b, float t) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, t));

        // 首段保持，缓慢加载到小幅度；快运动单位加入后迅速达峰，最后卸力而非匀速往返。
        public static float Recruit(float phase)
        {
            float slow = .18f * Ramp(.06f, .24f, phase);
            float fast = .82f * Ramp(.27f, .35f, phase);
            return (slow + fast) * (1 - Ramp(.47f, 1, phase));
        }

        // 摆腿先解除支撑、快速前送、末段减速寻触。端点位置与速度连续，不让脚瞬移。
        public static float Swing(float phase) => phase < .16f ? .06f * Ramp(0, .16f, phase) :
            phase < .64f ? Mathf.Lerp(.06f, .90f, Ramp(.16f, .64f, phase)) : Mathf.Lerp(.90f, 1, Ramp(.64f, 1, phase));

        public static float Strike(float phase) => Ramp(.025f, .24f, phase);

        // 刺探/寻角的行进不是持续匀速爬行：可见短停顿后再启动，承诺动作与外部冲量不走此门控。
        public static float Locomotion(BeetleState b)
        {
            if (b.Action != BeetleAction.Approach && b.Action != BeetleAction.Circle && b.Action != BeetleAction.Probe) return 1;
            float window = Mathf.Lerp(.46f, .66f, 1 - b.Traits.aggression);
            int cycle = Mathf.FloorToInt(b.ActionTime / window);
            float t = b.ActionTime - cycle * window;
            float hold = Mathf.Lerp(.025f, .095f, InsectMotionTiming.Value(cycle, 317 + b.Index * 13));
            float active = Ramp(hold, hold + .065f, t) * (1 - Ramp(window - .13f, window, t));
            // 主动压迫保持更多前进量；中性探查停顿更清楚，不因此丢失对手。
            float floor = b.Intent == BeetleIntent.Pressure || b.Intent == BeetleIntent.Counter ? .58f : .22f;
            return Mathf.Lerp(floor, 1.18f, active);
        }

        public static float Response(BeetleState b, float fallback)
        {
            bool strike = b.Action == BeetleAction.Shove || b.Action == BeetleAction.Charge || b.Action == BeetleAction.Probe;
            return strike ? Mathf.Lerp(18, 29, b.BurstReserve) : fallback;
        }
    }
}
