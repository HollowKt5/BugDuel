using System;
using UnityEngine;

namespace BeetleArena
{
    [Serializable]
    // 本局个体属性，在创建后按只读数据使用；与每步变化的 BeetleState 分离。
    public sealed class BeetleTraits
    {
        // size 为基准体型倍率；lengthMm/massGrams 只供展示；mass 为归一化运动质量。
        // strength/grip 分别控制发力和抓地，endurance 为耐力上限，aggression 为攻击倾向，
        // agility 影响移动/转向，hornReach 用于简化接触模式的触及距离。
        public float size, lengthMm, massGrams, mass, strength, grip, endurance, aggression, agility, hornReach;
        public float aggressionScore, lengthScore, hornScore, agilityScore, hornLengthMm;
        public float hornLengthFactor = 1;
        // Independent style affects cadence and choices, not an extra strength bonus.
        public float tempo = 1, drivePreference = .5f, liftPreference = .5f, lateralPreference = .5f;
        public float TotalScore => aggressionScore + lengthScore + hornScore + agilityScore;
        // N(240, 28²): the outer deciles start at z = ±1.28155.
        public string QualityBand => TotalScore >= 275.88f ? "高" : TotalScore <= 204.12f ? "低" : "中";
        // 全部成员均为值类型；入口快照和每次重赛各自持有独立属性实例。
        public BeetleTraits Copy() => (BeetleTraits)MemberwiseClone();
        // 依据体型与同一个局内随机源生成属性。质量按 size³、力量按 size² 变化，
        // 再给予小幅个体差异；这些是游戏近似，不是物种实验数据拟合。
        public static BeetleTraits Create(float size, System.Random random, BeetleMatchSettings settings)
        {
            float Vary() => .95f + (float)random.NextDouble() * .10f;
            float normal = Mathf.Sqrt(-2 * Mathf.Log(Mathf.Max(1e-7f, (float)random.NextDouble()))) * Mathf.Cos(2 *
                Mathf.PI * (float)random.NextDouble());
            float total = Mathf.Round(Mathf.Clamp(240 + 28 * normal, 150, 310));
            float length = Mathf.Round(Mathf.Lerp(35, 75, Mathf.InverseLerp(settings.minimumSize, settings.maximumSize, size)));
            float mean = (total - length) / 3;
            float aggressionRating = Mathf.Round(Mathf.Clamp(mean + ((float)random.NextDouble() - .5f) * 24, 10, 100));
            float hornRating = Mathf.Round(Mathf.Clamp(mean + ((float)random.NextDouble() - .5f) * 24, 10, 100));
            float agilityRating = total - length - aggressionRating - hornRating;
            // Redistribute any capped rating; the actual four-stat sum remains the sampled total.
            float excess = agilityRating - Mathf.Clamp(agilityRating, 10, 100);
            agilityRating -= excess;
            float adjusted = Mathf.Clamp(aggressionRating + excess, 10, 100);
            hornRating += excess - (adjusted - aggressionRating);
            aggressionRating = adjusted;
            float hornFactor = Mathf.Lerp(.84f, 1.20f, hornRating / 100);
            return new BeetleTraits
            {
                size = size,
                lengthMm = settings.referenceLengthMm * size,
                massGrams = settings.referenceMassGrams * size * size * size,
                mass = size * size * size,
                strength = size * size * Vary(),
                grip = size * Vary(),
                endurance = 100 * Vary(),
                aggression = Mathf.Lerp(.32f, .95f, aggressionRating / 100),
                agility = Mathf.Lerp(.72f, 1.40f, agilityRating / 100) / Mathf.Sqrt(size),
                hornReach = 1.65f * size * hornFactor,
                aggressionScore = aggressionRating,
                lengthScore = length,
                hornScore = hornRating,
                agilityScore = agilityRating,
                hornLengthFactor = hornFactor,
                hornLengthMm = settings.referenceLengthMm * size * .48f * hornFactor
            };
        }
    }

}
