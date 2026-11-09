using System;
using System.Collections.Generic;

namespace BeetleArena
{
    public enum ArenaCard
    {
        None,
        // Basic cards are always carried and differ from each other; specials and events keep their
        // contiguous ranges because selection, cooldown and trigger logic reads them positionally.
        Shove, Probe, Brace, Sidestep,
        Rosin, Knock, Unhook, Emery, Pheromone, Camphor, DullAntennae,
        Honeydew, Groom, Breathe, PlayDead, Stridulate,
        LastStand, Groove, King, Rain, Jelly
    }

    public readonly struct ArenaCardInfo
    {
        public readonly string Name, EnglishName, Description, EnglishDescription;
        public readonly float Cooldown;

        public ArenaCardInfo(string name, string englishName, string description, string englishDescription, float cooldown)
        {
            Name = name;
            EnglishName = englishName;
            Description = description;
            EnglishDescription = englishDescription;
            Cooldown = cooldown;
        }
    }

    public static class ArenaCardCatalog
    {
        public const int SpecialSlots = 3, FirstSpecialSlot = 4, EventSlot = 7, SlotCount = 8;

        // Positional: index 0 is ArenaCard.None. Chinese text is the localization source; English text
        // is the fallback used when the GameText table has no entry for the current locale.
        static readonly ArenaCardInfo[] Cards =
        {
            default,
            new("普通进攻", "Shove", "消耗 10 耐力，短暂蓄力后向前冲撞。换步或后撤的对手更容易被推开。", "Spend 10 stamina to wind up and lunge. Moving or retreating rivals are easier to push.", 5),
            new("试探推", "Probe", "消耗 5 耐力，快速短推试探。推力较小，但冷却只有 3 秒。", "Spend 5 stamina on a quick probing shove. Weaker push, but only a 3 second cooldown.", 3),
            new("普通防守", "Brace", "沉身抓地 2 秒，提升平衡与抗推能力。", "Brace for 2 seconds, gaining balance and push resistance.", 6),
            new("侧步闪避", "Sidestep", "向场内快速让开半步，短暂脱离正面对顶。靠近边框时使用会把自己带向中央。", "Step quickly toward the arena centre, briefly leaving the head-on exchange.", 5),
            new("防滑松香粉", "Rosin", "8 秒内提升己方台面抓地力，踩空时抓地流失减缓。", "Improve traction and slow footing loss for 8 seconds.", 16),
            new("木桩重叩", "Wood Knock", "打断双方动作，让双方各自弹退并重新试探。", "Interrupt both beetles and push them apart to reset the exchange.", 14),
            new("草签断缠", "Unhook", "拆开角部纠缠，双方退让并重置交锋计时。", "Break the horn lock, back off and reset the exchange.", 12),
            new("金刚砂磨角", "Emery", "6 秒内稳定推角接触，压制侧滑脱角。", "Keep horns aligned and suppress sideways slip for 6 seconds.", 18),
            new("母虫信息素", "Pheromone", "6 秒内推力 +40%，爆发恢复翻倍，动作耐力消耗 +50%。", "For 6 seconds, +40% push, double burst recovery and +50% stamina cost.", 25),
            new("樟脑避让雾", "Camphor", "对手强制退让 3 秒。", "Force the opponent to retreat for 3 seconds.", 20),
            new("触角钝化剂", "Dull Antennae", "5 秒内降低对手的寻角转向与命中角容错。", "Reduce the opponent's tracking and attack angle tolerance for 5 seconds.", 18),
            new("高纯蜜露滴", "Honeydew", "恢复 35 耐力并减轻 20 疲劳，受耐力上限限制。", "Restore 35 stamina and clear 20 fatigue, respecting the stamina cap.", 20),
            new("倒钩梳理", "Groom", "清除打滑和受阻，恢复抓地。", "Clear sliding and obstruction, and restore footing.", 12),
            new("深呼吸通气", "Deep Breath", "12 秒内动作耐力消耗降低 40%。", "Reduce action stamina costs by 40% for 12 seconds.", 16),
            new("假死性瘫软", "Play Dead", "卸力 0.8 秒避开上挑，起身后 3 秒内推力 +35%。", "Go limp for 0.8 seconds to evade pries, then gain 35% push for 3 seconds.", 24),
            new("振翅摩擦音", "Stridulate", "惊住对手 0.9 秒，打断蓄势。", "Interrupt and startle the opponent for 0.9 seconds.", 15),
            new("背水怒吼", "Last Stand", "边缘出现。向前爆发，弹开对手。", "Appears at the rim. Surge forward and knock the rival away.", 15),
            new("落屑木痕", "Wood Groove", "正面对顶僵持 3 秒出现。扎根原地，3 秒内免疫推移。", "Appears after 3 seconds of head-on contact. Anchor against pushes for 3 seconds.", 15),
            new("王者昂首", "King's Lift", "对手露出大破绽时出现。下一次实质接触将撬飞对手，效果保留到接触发生。", "Appears when the opponent is exposed. Your next physical contact launches them; the charge is held until then.", 20),
            new("骤雨微凉", "Cool Rain", "60 秒后双方耐力均低于 30% 时出现。双方本场动作耐力消耗降低 60%。", "Appears after 60 seconds with both below 30% stamina. Cut both action costs by 60% for the rest of the bout.", 20),
            new("观众投蜜", "Crowd Jelly", "双方拉开时出现。中央落下果冻，先到者恢复 20 耐力并短暂加速。", "Appears when fighters separate. The first to reach the center gains 20 stamina and a speed boost.", 12)
        };

        public static ArenaCardInfo Get(ArenaCard card) => Cards[(int)card];

        public static bool IsBasic(ArenaCard card) => card >= ArenaCard.Shove && card <= ArenaCard.Sidestep;

        public static bool IsSpecial(ArenaCard card) => card >= ArenaCard.Rosin && card <= ArenaCard.Stridulate;

        public static bool ValidLoadout(IReadOnlyList<ArenaCard> cards)
        {
            if (cards == null || cards.Count != SpecialSlots)
                return false;
            for (int i = 0; i < cards.Count; i++)
            {
                if (!IsSpecial(cards[i]))
                    return false;
                for (int j = 0; j < i; j++)
                    if (cards[i] == cards[j])
                        return false;
            }
            return true;
        }
    }
}
