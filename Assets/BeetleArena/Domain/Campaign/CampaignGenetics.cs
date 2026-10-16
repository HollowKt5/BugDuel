using System;
using System.Collections.Generic;
using System.Linq;

namespace BugDuel.Campaign
{
    public static class CampaignGenetics
    {
        public static readonly string[] GeneIds = { "力量", "甲壳", "敏捷", "巨型", "耐力" };
        public static List<Gene> Inherit(CampaignBeetle father, CampaignBeetle mother)
        {
            var genes = father.genes.Select(g => new Gene(g.id, g.stars)).ToList();
            var maternal = mother.genes.OrderByDescending(g => g.stars).First();
            genes.Add(new Gene(maternal.id, maternal.stars));
            if (genes.Count > 5) genes.RemoveAt(0);
            for (int i = genes.Count - 1; i > 0; i--)
                if (genes[i].id == genes[i-1].id && genes[i].stars == genes[i-1].stars && genes[i].stars < 3)
                { genes[i-1].stars++; genes.RemoveAt(i); }
            return genes;
        }
        public static double LengthBonus(List<Gene> genes) => genes.Any(g => g.stars == 3) ? 1.28 : genes.Any(g => g.stars == 2) ? 1.12 : 1;
        public static string Combo(List<Gene> genes)
        {
            bool Has(string id) => genes.Any(g => g.id == id && g.stars >= 2);
            if (Has("甲壳") && Has("巨型")) return "纯金壳";
            if (Has("力量") && Has("巨型")) return "极太巨剪";
            if (Has("敏捷") && Has("耐力")) return "紫绀色";
            return "";
        }
        public static bool Related(CampaignBeetle a, CampaignBeetle b)
        {
            var first = new HashSet<string>(a.ancestors) { a.id };
            return first.Contains(b.id) || b.ancestors.Any(first.Contains);
        }
    }
    public static class CampaignEconomy
    {
        public const long Maximum = 1000000000000;
        public static long Money(double value) => (long)Math.Max(1, Math.Min(Maximum, Math.Round(value)));
        public static double MutationMultiplier(string mutation) => string.IsNullOrEmpty(mutation) ? 1 : mutation == "纯金壳" || mutation == "传奇亚种" ? 8 : mutation == "稀缺亚种" ? 5 : 3;
        public static long Initial(CampaignBeetle b, SpeciesRule s) => Money(s.price * Math.Pow(b.length / s.length, 2.5) * Math.Min(2.5, 1 + b.genes.Sum(g => g.stars - 1) * .3) * MutationMultiplier(b.mutation));
        public static long Win(CampaignBeetle b, double opponentLength) => Money(b.value * (1 + (opponentLength - b.length > 5 ? .55 : .30) + Math.Max(0,b.streak - 1) * .1));
        public static long Lose(CampaignBeetle b) => Math.Max(Money(b.initialValue * .8), Money(b.value * .92));
    }
}
