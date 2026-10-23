using System;
using System.Collections.Generic;
using System.Linq;
using BugDuel.Domain;

namespace BugDuel.Campaign
{
    // Save DTOs deliberately contain no Unity objects. IDs, never display names, are foreign keys.
    [Serializable] public sealed class CampaignState
    {
        internal CampaignState ShallowCopy() => (CampaignState)MemberwiseClone();
        public int schema = 1, week = 1, capacity = 6, cabinet = 3, highestLeague, reputation;
        public long coins = 1800;
        public string playerName;
        public int playerIdentityVersion;
        public uint random = 18273;
        public bool scientistPermit, feedRecipe, mutationRecipe;
        public int sap, lamp;
        public int mailboxVersion;
        public List<CampaignMail> mail = new List<CampaignMail>();
        public List<CampaignInventoryStack> inventory = new List<CampaignInventoryStack>();
        public List<CampaignBeetle> storedBeetles = new List<CampaignBeetle>();
        public string selectedId, notice = "欢迎来到小小虫舍。先照顾甲虫，再去试试新秀联赛。";
        public List<CampaignBeetle> beetles = new List<CampaignBeetle>();
        public List<Expedition> expeditions = new List<Expedition>();
        public List<MarketOffer> offers = new List<MarketOffer>();
        public List<CampaignBeetle> stock = new List<CampaignBeetle>();
        public List<HallRecord> hall = new List<HallRecord>();
        public PendingBattle battle;
        public AuctionState auction;
        public PupaAuction pupaAuction;
        public int lastPupaWeek;
        public int marketWeek;
        public int tradingVersion;
        public int calendarVersion;
        public int constructionVersion;
        public List<CampaignDecorationPlacement> decorations = new List<CampaignDecorationPlacement>();
        public CampaignClock clock = new CampaignClock();
        public CampaignLifeRules lifeRules = new CampaignLifeRules();
        public List<CampaignSpecimen> specimens = new List<CampaignSpecimen>();
        public List<CampaignSpecimen> specimenStock = new List<CampaignSpecimen>();
        public List<SpecimenOffer> specimenOffers = new List<SpecimenOffer>();
        public List<MarketAuctionLot> marketAuctions = new List<MarketAuctionLot>();
        public string trendSpecies;
        public double recordLength;
    }
    [Serializable] public sealed class CampaignBeetle
    {
        internal CampaignBeetle ShallowCopy() => (CampaignBeetle)MemberwiseClone();
        public string id, speciesId, name, mother, father, brood, habitat = "woodland";
        public List<string> ancestors = new List<string>();
        public bool female, dead, overturned, listed, rightsSold, customName;
        public int lifeVersion;
        public CampaignLifeStage stage = CampaignLifeStage.ActiveAdult;
        public double stageAgeDays, totalAgeDays, adultAgeDays, developmentScale = 1;
        public double substrateNutrition = 85, substrateCleanliness = 100, pupalProtectionDays, growthQualityBonus;
        public double length, inheritance = 1, hearts = 5, nutrition = 85, overturnSeconds;
        public int maxHearts = 5, inbred, readyWeek, feed, climate = 1, cooldownWeek, wins, streak, championships, seasonWins;
        public int broodValueAwards;
        public long initialValue, value;
        public string mutation = "";
        public CampaignMeal meal;
        public bool hasMeal; // Preserve nullness through Unity inline serialization.
        public List<Gene> genes = new List<Gene>();
        public bool Adult(int week) => !dead && stage == CampaignLifeStage.ActiveAdult;
    }
    [Serializable] public sealed class CampaignMeal { public string id; public int food; }
    [Serializable] public sealed class Gene { public string id; public int stars; public Gene(string id, int stars = 1) { this.id = id; this.stars = stars; } }
    [Serializable] public sealed class Expedition { public string id, habitat; public int dueWeek, count; public bool large; }
    [Serializable] public sealed class MarketOffer { public string id, beetleId, buyer; public long price; public int week; public bool femaleBonus, recipe, permit; }
    [Serializable] public sealed class HallRecord { public string name, speciesId, mutation; public double length; public long price; public int week; public bool rights, customName; public long royalties; }
    [Serializable] public sealed class PendingBattle { public string id, beetleId; public CampaignBeetle opponent; public int seed, tier; public long fee; }
    [Serializable] public sealed class AuctionState { public string beetleId; public bool rights; public int round, rounds; public long bid, finalBid; public string bidder; }
    [Serializable] public sealed class PupaAuction { public CampaignBeetle beetle; public int round; public long bid; }

    [Serializable] public sealed class CampaignCatalog
    {
        public int schema = 1, revision = 1;
        public SpeciesRule[] species;
        public CatchRule[] catches;
        public HousingRule[] housing;
        public FeedRule[] feeds;
        public LeagueRule[] leagues;
        public int seasonWins = 3, upgrade12 = 2500, upgrade20 = 10000, cabinetUpgrade = 1800;
        public SpeciesRule Species(string id) => species.FirstOrDefault(x => x.id == id)
            ?? throw new CampaignDataException("内容表缺少物种：" + id);
        public CatchRule Catch(string id) => catches.FirstOrDefault(x => x.id == id)
            ?? throw new CampaignDataException("内容表缺少捕虫地点：" + id);
        public HousingRule Housing(string id) => housing.FirstOrDefault(x => x.id == id)
            ?? throw new CampaignDataException("内容表缺少养殖环境：" + id);
        public LeagueRule Tier(double length) => leagues.FirstOrDefault(x => length >= x.minimum && length < x.maximum)
            ?? throw new CampaignDataException("内容表缺少对应体长的联赛：" + length);
        public void Validate()
        {
            if (schema != 1 || revision < 1) throw new CampaignDataException("不支持的内容版本");
            if (species == null || species.Length == 0 || catches == null || catches.Length == 0 ||
                housing == null || housing.Length == 0 || feeds == null || feeds.Length != 3 || leagues == null || leagues.Length != 5)
                throw new CampaignDataException("内容表缺少必需分类");
            if (species.Any(x => x == null) || catches.Any(x => x == null) || housing.Any(x => x == null) ||
                feeds.Any(x => x == null) || leagues.Any(x => x == null))
                throw new CampaignDataException("内容表包含空条目");
            Unique(species.Select(x => x.id)); Unique(catches.Select(x => x.id)); Unique(housing.Select(x => x.id));
            if (!species.Any(x => x.id == "red") || !housing.Any(x => x.id == "woodland") || !catches.Any(x => x.unlock == 0))
                throw new CampaignDataException("内容表必须保留初始物种 red、初始环境 woodland 和至少一个初始捕虫地点");
            foreach (var x in species)
            {
                if (string.IsNullOrWhiteSpace(x.name) || !Finite(x.length, 10, 250) || x.price < 1 || x.price > 1000000 || !Finite(x.hue, 0, 1)) throw new CampaignDataException("物种参数无效");
                try { x.behavior?.Validate(); }
                catch (InvalidOperationException exception) { throw new CampaignDataException("物种养殖行为参数无效：" + x.id, exception); }
            }
            foreach (var x in catches)
                if (x.species == null || x.species.Length == 0 || x.species.Any(id => !species.Any(s => s.id == id)) || x.cost < 1 || x.cost > 10000000 || x.weeks < 1 || x.weeks > 2 || x.unlock < 0 || x.unlock > 5 || !Finite(x.min, 10, 250) || !Finite(x.max, x.min, 250) || !Finite(x.twoStar, 0, 1) || !Finite(x.rare, 0, 1) || x.twoStar + x.rare > 1) throw new CampaignDataException("捕虫表参数无效");
            foreach (var x in housing)
            {
                if (!Finite(x.healthRecovery, 0, 2) || !Finite(x.accidentPerMinute, 0, .25) || !Finite(x.temperature, 0, 40) || !Finite(x.humidity, 0, 100)) throw new CampaignDataException("环境参数无效");
                try { x.behavior?.Validate(); }
                catch (InvalidOperationException exception) { throw new CampaignDataException("养殖环境行为参数无效：" + x.id, exception); }
            }
            foreach (var x in feeds)
                if (x.cost < 0 || x.cost > 100000 || !Finite(x.multiplier, .5, 2) || !Finite(x.mutationChance, 0, 1)) throw new CampaignDataException("饲料参数无效");
            double end = 0;
            for (int i = 0; i < leagues.Length; i++)
            {
                var x = leagues[i];
                if (x.tier != i + 1 || x.minimum != end || !Finite(x.maximum, x.minimum + 1, 100000) || x.fee < 0 || x.win < 0 || x.champion < 0 || x.fee > 10000000 || x.win > 10000000 || x.champion > 10000000) throw new CampaignDataException("联赛分级不连续或奖励无效");
                end = x.maximum;
            }
            if (end < 1000 || seasonWins < 1 || seasonWins > 20 || upgrade12 < 1 || upgrade20 < 1 || cabinetUpgrade < 1) throw new CampaignDataException("经营配置无效");
        }
        static bool Finite(double x, double min, double max) => !double.IsNaN(x) && !double.IsInfinity(x) && x >= min && x <= max;
        static void Unique(IEnumerable<string> ids) { var a = ids.ToArray(); if (a.Any(string.IsNullOrWhiteSpace) || a.Distinct().Count() != a.Length) throw new CampaignDataException("内容 ID 重复或为空"); }
        public static CampaignCatalog Defaults() => new CampaignCatalog
        {
            species = new[] {
                new SpeciesRule("rhinoceros", "独角仙", 65, 280, .08), new SpeciesRule("flat", "本土扁锹", 62, 320, .04), new SpeciesRule("red", "鸡冠细身赤锹", 45, 200, .01),
                new SpeciesRule("giant-stag", "日本大锹", 78, 850, .65), new SpeciesRule("rainbow", "澳洲彩虹锹甲", 68, 1000, .38),
                new SpeciesRule("palawan", "巴拉望巨扁锹", 108, 2200, .12), new SpeciesRule("giraffe", "长颈鹿锯锹", 116, 2400, .07), new SpeciesRule("caucasus", "高卡萨斯大兜", 120, 2800, .32),
                new SpeciesRule("hercules", "赫克力士长戟大兜", 150, 6000, .15), new SpeciesRule("actaeon", "亚克提恩象兜", 135, 5000, .62), new SpeciesRule("mars", "战神象兜", 155, 7000, .02), new SpeciesRule("goliath", "大王花金龟", 105, 4500, .53) },
            catches = new[] {
                new CatchRule("oak", "夏日郊野栎木林", 0, 200, 1, 30, 65, .14, .01, "rhinoceros", "flat", "red"),
                new CatchRule("mist", "迷雾高山朽木林", 1, 800, 1, 55, 88, .28, .07, "giant-stag", "rainbow"),
                new CatchRule("islands", "东南亚季风群岛", 2, 2500, 2, 75, 125, .40, .15, "palawan", "giraffe", "caucasus"),
                new CatchRule("amazon", "亚马逊赤道禁猎区", 3, 8000, 2, 100, 170, .25, .20, "hercules", "actaeon", "mars", "goliath") },
            housing = new[] { new HousingRule("woodland", "林地生态箱", 24, 70, .35, .025), new HousingRule("humid", "湿润朽木箱", 22, 85, .25, .012), new HousingRule("recovery", "安静恢复箱", 25, 65, .75, 0) },
            feeds = new[] { new FeedRule("绿标安全瓶", 100, 1, 0), new FeedRule("黄标催肥瓶", 450, 1.18, 0), new FeedRule("紫标突变瓶", 1200, 1.12, .35) },
            leagues = new[] { new LeagueRule(1,"新秀微型树桩赛",0,55,50,200,1000), new LeagueRule(2,"轻量级林间联赛",55,80,200,600,3500), new LeagueRule(3,"中量级栎木杯",80,110,600,1800,12000), new LeagueRule(4,"重量级雨林大师赛",110,145,2000,5500,38000), new LeagueRule(5,"巨神级树液王座赛",145,10000,6000,16000,120000) }
        };
    }
    [Serializable] public sealed class SpeciesRule { public string id, name; public double length, hue; public int price; public CareBehaviorProfile behavior; public SpeciesRule(string id,string name,double length,int price,double hue) { this.id=id;this.name=name;this.length=length;this.price=price;this.hue=hue; behavior=new CareBehaviorProfile { nocturnal=id!="goliath",activity=id=="goliath"?.85:.7,walkSpeed=length>100?.18:.24 }; } }
    [Serializable] public sealed class CatchRule { public string id,name; public int unlock,cost,weeks; public double min,max,twoStar,rare; public string[] species; public CatchRule(string id,string name,int unlock,int cost,int weeks,double min,double max,double twoStar,double rare,params string[] species) { this.id=id;this.name=name;this.unlock=unlock;this.cost=cost;this.weeks=weeks;this.min=min;this.max=max;this.twoStar=twoStar;this.rare=rare;this.species=species; } }
    [Serializable] public sealed class HousingRule { public string id,name; public double temperature,humidity,healthRecovery,accidentPerMinute; public CareHabitatProfile behavior; public HousingRule(string id,string name,double t,double h,double recovery,double risk) { this.id=id;this.name=name;temperature=t;humidity=h;healthRecovery=recovery;accidentPerMinute=risk;behavior=new CareHabitatProfile { activityMultiplier=id=="recovery"?.6:1,restPreference=id=="recovery"?.9:.5 }; } }
    [Serializable] public sealed class FeedRule { public string name; public int cost; public double multiplier,mutationChance; public FeedRule(string name,int cost,double multiplier,double mutationChance) { this.name=name;this.cost=cost;this.multiplier=multiplier;this.mutationChance=mutationChance; } }
    [Serializable] public sealed class LeagueRule { public int tier,fee,win,champion; public string name; public double minimum,maximum; public LeagueRule(int tier,string name,double min,double max,int fee,int win,int champion) { this.tier=tier;this.name=name;minimum=min;maximum=max;this.fee=fee;this.win=win;this.champion=champion; } }
}
