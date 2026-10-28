using System;
using System.Linq;
using System.Collections.Generic;

namespace BugDuel.Campaign
{
    // Application owns transaction boundaries; domain methods never touch scenes, files or Unity's RNG.
    public sealed partial class CampaignSession
    {
        public CampaignState State { get; }
        public CampaignCatalog Catalog { get; }
        public CampaignSession(CampaignState state, CampaignCatalog catalog) { State = state; Catalog = catalog; UpgradeMailbox(); UpgradeCalendar(); UpgradeTrading(); UpgradeLifeHistory(); UpgradeConstruction(); UpgradePlayerIdentity(); }
        public int Reserved => State.expeditions.Sum(e => e.count);
        public CampaignBeetle Beetle(string id) => State.beetles.FirstOrDefault(x => x.id == id) ?? throw new CampaignRuleException("这只甲虫已离开温室");
        public double Roll() { uint x = State.random; x ^= x << 13; x ^= x >> 17; x ^= x << 5; State.random = x == 0 ? 18273u : x; return State.random / ((double)uint.MaxValue + 1); }
        public string Id() => Guid.NewGuid().ToString("N");
        public void Spend(long amount) { if(amount<0)throw new ArgumentOutOfRangeException(nameof(amount)); Require(State.coins >= amount, "金币不足"); State.coins -= amount; }
        public void Earn(long amount) { if(amount<0)throw new ArgumentOutOfRangeException(nameof(amount)); State.coins = Math.Min(CampaignEconomy.Maximum-State.marketAuctions.Sum(a=>a.playerBid), State.coins + amount); }
        public void Space(int count) => Require(State.beetles.Count + Reserved + count <= State.capacity, "温室已满（包含捕虫队预留栏位）");
        public void Available(CampaignBeetle b) { Require(!b.dead, "这只甲虫已死亡，请先清理栏位"); Require(!b.listed && State.battle?.beetleId != b.id && State.auction?.beetleId != b.id, "甲虫正在挂牌、参赛或拍卖"); }
        public void HealthyAdult(CampaignBeetle b) { Available(b); Require(b.Adult(State.week) && !b.overturned && b.hearts >= Math.Min(3,b.maxHearts) && b.nutrition >= 40, "需要健康、未翻身的成虫（至少3心与40饱食）"); }
        public static void Require(bool ok, string message) { if (!ok) throw new CampaignRuleException(message); }
        static void RequireData(bool ok, string message) { if (!ok) throw new CampaignDataException(message); }
        public void Tell(string message) => State.notice = message;

        public static CampaignSession New(CampaignCatalog catalog, uint seed)
        {
            catalog.Validate();
            var s = new CampaignSession(new CampaignState { random = seed == 0 ? 18273u : seed }, catalog);
            var a = s.Create("red", 48); a.name = "小栗子"; a.female = false;
            var b = s.Create("red", 46); b.name = "蜜桃"; b.female = true;
            s.State.beetles.Add(a); s.State.beetles.Add(b); s.State.selectedId = a.id;
            s.State.recordLength = 48; s.RefreshMarket(); return s;
        }
        public CampaignBeetle Create(string speciesId, double length, int stars = 1, string mutation = "")
        {
            var s = Catalog.Species(speciesId);
            var b = new CampaignBeetle { id = Id(), speciesId = speciesId, name = s.name, length = length, female = Roll() < .5, mutation = mutation, habitat = Catalog.housing[0].id, lifeVersion=1,stage=CampaignLifeStage.ActiveAdult };
            b.genes.Add(new Gene(CampaignGenetics.GeneIds[(int)(Roll()*5)], stars));
            b.initialValue = b.value = CampaignEconomy.Initial(b,s); return b;
        }
        public void Add(CampaignBeetle b) { Space(1); State.beetles.Add(b); State.selectedId = b.id; State.recordLength = Math.Max(State.recordLength,b.length); }
        void RemoveBeetle(CampaignBeetle b)
        {
            ReclaimDecorations(b.id);
            State.beetles.Remove(b);
            if(State.selectedId==b.id)State.selectedId=State.beetles.FirstOrDefault()?.id;
        }
        public void Upgrade()
        {
            Require(State.capacity < 20, "温室已经是20格"); Spend(State.capacity == 6 ? Catalog.upgrade12 : Catalog.upgrade20);
            State.capacity = State.capacity == 6 ? 12 : 20; Tell("温室扩建完成！现在有 " + State.capacity + " 个栏位。");
        }
        public void Feed(string id, int food)
        {
            var b = Beetle(id); Available(b); Require(b.Adult(State.week) && !b.overturned, "先帮助甲虫翻身；幼虫使用饲料瓶"); Require(food >= 0 && food <= 2, "食物不存在");
            Require(b.meal == null,"食盆里还有食物，请等它吃完");
            ConsumeItem(CampaignItems.Food(food)); b.meal = new CampaignMeal { id=Id(), food=food }; b.hasMeal=true;
            Tell("已放置" + new[] { "红薯", "香蕉", "苹果" }[food]);
        }
        public void CompleteMeal(string id,string mealId)
        {
            var b=Beetle(id); Available(b);
            Require(b.Adult(State.week) && !b.overturned && b.meal!=null && b.meal.id==mealId,"这份食物当前不能结算");
            b.nutrition=Math.Min(100,b.nutrition+(b.meal.food==0?35:25)); b.hearts=Math.Min(b.maxHearts,b.hearts+.5);
            b.meal=null;b.hasMeal=false;b.foodResidue=true;Tell("进食完成");
        }
        public void ClearFoodDish(string id)
        {
            var b=Beetle(id);Available(b);Require(b.Adult(State.week),"只有成虫使用食盆");
            Require(b.meal!=null||b.foodResidue,"食盆已经是干净的");
            b.meal=null;b.hasMeal=false;b.foodResidue=false;Tell("食盆已清理");
        }
        public void Rescue(string id)
        {
            var b = Beetle(id); Available(b); Require(b.overturned,"它已经站稳啦"); b.overturned = false; b.overturnSeconds = 0; Tell("轻轻扶正了 " + CampaignDisplayText.Name(b) + "。记得补充食物和休息。");
        }
        public void House(string id,string habitat) { var b = Beetle(id); Available(b); Catalog.Housing(habitat); b.habitat = habitat; Tell("已移入 " + Catalog.Housing(habitat).name); }
        public void ClearDead(string id) { var b = State.beetles.Concat(State.storedBeetles).FirstOrDefault(x=>x.id==id); Require(b!=null&&b.dead,"只有自有死亡甲虫可以清理；健康甲虫请在市场交易"); RemoveBeetle(b);State.storedBeetles.Remove(b); State.offers.RemoveAll(o=>o.beetleId==id); Tell("已遗弃遗体并清理栏位。"); }
        public void CareSeconds(double seconds, Func<string, bool> isEating = null)
        {
            if (State.battle != null || State.auction != null) return;
            foreach (var b in State.beetles.Where(x => x.Adult(State.week) && !x.listed))
            {
                if (!b.overturned)
                {
                    // The host supplies transient behavior facts; pending food alone is not eating.
                    if (b.meal != null && isEating?.Invoke(b.id) == true) continue;
                    if (Roll() < 1 - Math.Exp(-Catalog.Housing(b.habitat).accidentPerMinute * seconds / 60))
                    { b.overturned = true; b.overturnSeconds = 0; Tell(CampaignDisplayText.Name(b) + " 翻不过身了！请到温室帮忙（60秒后开始受伤）。"); }
                }
                else
                {
                    double previous = b.overturnSeconds; b.overturnSeconds += seconds;
                    b.hearts = Math.Max(0,b.hearts - (Math.Max(0,b.overturnSeconds-60)-Math.Max(0,previous-60))/24);
                    if (b.hearts <= 0)LifeDeath(b,"因长时间无法翻身而死亡");
                }
            }
        }
        public void Grow(string id,int feed,int climate)
        {
            var b = Beetle(id); Available(b); Require(b.stage<=CampaignLifeStage.Larva3,"饲料与温控方案用于卵和幼虫阶段"); Require(feed >= 0 && feed < 3 && climate >= 0 && climate < 3,"养成方案无效");
            Require(b.feed == -1,"已选好本次养成方案");double progress=StageProgress(b);Spend(Catalog.feeds[feed].cost); b.feed = feed; b.climate = climate;
            b.stageAgeDays=progress*StageDuration(b);b.substrateNutrition=Math.Min(100,b.substrateNutrition+35);b.substrateCleanliness=100;UpdateReadyWeek(b);
            Tell("营养与温控方案已保存，预计再过 "+Math.Ceiling(RemainingDevelopmentDays(b))+" 天进入活动成虫期。未选择方案也会自然成长。");
        }
        public void Breed(string fatherId,string motherId)
        {
            var father = Beetle(fatherId); var mother = Beetle(motherId); HealthyAdult(father); HealthyAdult(mother);
            Require(!father.female && mother.female && father.id != mother.id && father.speciesId == mother.speciesId,"需要同物种的雄虫与雌虫");
            Require(father.cooldownWeek <= State.week && mother.cooldownWeek <= State.week,"亲本需要休息到下一周"); Space(1); Spend(100);
            bool related = CampaignGenetics.Related(father,mother); int depth = related ? Math.Max(father.inbred,mother.inbred)+1 : 0;
            Require(depth < 5,"近亲世代已达到健康极限，请换血");
            var b = CreateOffspring(father,mother,related,depth);
            father.cooldownWeek = mother.cooldownWeek = State.week+1; father.nutrition -= 15; mother.nutrition -= 20; Add(b);
            Tell("配种成功，获得 1 枚卵。");
        }
        CampaignBeetle CreateOffspring(CampaignBeetle father,CampaignBeetle mother,bool related,int depth)
        {
            var b = Create(father.speciesId,Catalog.Species(father.speciesId).length);
            b.father = father.id; b.mother = mother.id; b.brood = father.id + ":" + mother.id;
            b.ancestors = new[] { father.id,mother.id }.Concat(father.ancestors).Concat(mother.ancestors).Distinct().Take(128).ToList();
            b.genes = CampaignGenetics.Inherit(father,mother); b.inbred = depth; b.maxHearts = 5 - depth; b.hearts = b.maxHearts;
            double parentFactor = (father.length + mother.length)/(2*Catalog.Species(b.speciesId).length);
            b.inheritance = Math.Max(.90,Math.Min(1.25,parentFactor * (.95+Roll()*.1))) * (related ? 1 : 1.05);
            b.mutation = CampaignGenetics.Combo(b.genes);SetInitialStage(b,CampaignLifeStage.Egg);b.initialValue=b.value=CampaignEconomy.Initial(b,Catalog.Species(b.speciesId));
            return b;
        }
        public bool Unlocked(CatchRule c) => State.highestLeague >= c.unlock || c.id == "amazon" && State.scientistPermit;
        public void Dispatch(string id,bool sap,bool lamp)
        {
            var c = Catalog.Catch(id); Require(Unlocked(c),"栖息地尚未解锁"); Require(!sap || State.sap > 0,"需要先在市场购买发酵树液"); Require(!lamp || State.lamp > 0,"需要先购买紫外灯");
            int count = sap ? 4 : 2; Space(count); Spend(c.cost); if (sap) State.sap--; if (lamp) State.lamp--;
            State.expeditions.Add(new Expedition { id = Id(), habitat = id, dueWeek = State.week+c.weeks, count = count, large = lamp }); Tell("捕虫队出发！已预留 " + count + " 个栏位。");
        }
        public void NextWeek(bool work = false)
        {
            Require(State.battle == null && State.auction == null && State.pupaAuction == null,"请先完成当前赛事或拍卖"); Require(!State.beetles.Any(b => b.overturned && !b.dead),"还有甲虫在挣扎，先帮它翻身再快进一周");
            foreach(var b in State.beetles.Where(x=>x.Adult(State.week)&&!x.listed&&x.meal!=null))CompleteMeal(b.id,b.meal.id);
            AdvanceDays(7);if(work){Earn(150);Tell(State.notice+"虫舍帮工收入150 G。");}
        }
        void AdvanceEconomicWeek()
        {
            SettleWeeklyAuctions();State.week++;int caught=0;
            foreach (var e in State.expeditions.Where(x => x.dueWeek <= State.week).ToArray())
            {
                var c = Catalog.Catch(e.habitat); State.expeditions.Remove(e);
                for (int i=0;i<e.count;i++)
                {
                    double roll = Roll(), size = Roll(); if (e.large && Roll() < .25) size = .75 + Roll()*.25;
                    int stars = c.id == "amazon" ? (roll < c.rare + c.twoStar ? 3 : 2) : roll < c.rare+c.twoStar ? 2 : 1;
                    string mutation = roll < c.rare ? c.id == "amazon" ? "传奇亚种" : c.id == "islands" ? "稀缺亚种" : c.id == "mist" ? "白眼" : "稀有色" : "";
                    Add(Create(c.species[(int)(Roll()*c.species.Length)],c.min+(c.max-c.min)*size,stars,mutation)); caught++;
                }
            }
            long royalty = 0;
            foreach (var h in State.hall)
            { int tier = Catalog.Tier(h.length).tier; long pay = Catalog.leagues[tier-1].win / 10; h.royalties += pay; royalty += pay; }
            Earn(royalty); RefreshMarket();
            Tell("第 " + State.week + " 周：捕回 " + caught + " 只，血统分红 " + royalty + " G。市场与拍品已更新，成长与年龄按自然日历推进。");
        }
        public void Validate()
        {
            Catalog.Validate();
            RequireData(State != null && State.schema == 1 && State.week > 0 && State.week < 1000000 && State.random != 0 && (State.capacity == 6 || State.capacity == 12 || State.capacity == 20) && (State.cabinet == 3 || State.cabinet == 6) && State.highestLeague >= 0 && State.highestLeague <= 5 && State.coins >= 0 && State.coins <= CampaignEconomy.Maximum,"存档主数据无效");
            RequireData(State.beetles != null && State.expeditions != null && State.offers != null && State.stock != null && State.hall != null,"存档集合缺失"); RequireData(State.beetles.Count + Reserved <= State.capacity,"温室占用超出容量");
            ValidateMailbox();ValidateTrading();ValidateLifecycle();ValidateConstruction();ValidatePlayerIdentity();var ids = new HashSet<string>();
            Catalog.Species(State.trendSpecies);
            foreach (var h in State.hall) { Catalog.Species(h.speciesId); RequireData(Finite(h.length,1,1000) && h.price > 0 && h.price <= CampaignEconomy.Maximum && h.royalties >= 0,"名人堂数据无效"); }
            foreach (var b in State.beetles.Concat(State.stock).Concat(State.storedBeetles).Concat(MailBeetles()).Concat(TradingBeetles()).Concat(State.battle == null ? new CampaignBeetle[0] : new[]{State.battle.opponent}).Concat(State.pupaAuction == null ? new CampaignBeetle[0] : new[]{State.pupaAuction.beetle}))
            {
                RequireData(b != null && !string.IsNullOrEmpty(b.id) && ids.Add(b.id),"甲虫 ID 重复或缺失"); Catalog.Species(b.speciesId); Catalog.Housing(b.habitat);
                RequireData(b.genes != null && b.genes.Count > 0 && b.genes.Count <= 5 && b.genes.All(g => g != null && CampaignGenetics.GeneIds.Contains(g.id) && g.stars >= 1 && g.stars <= 3) && b.ancestors != null,"基因或血统数据无效");
                RequireData((b.meal!=null)==b.hasMeal && (b.meal==null || !string.IsNullOrWhiteSpace(b.meal.id)&&b.meal.food>=0&&b.meal.food<=2),"待进食记录无效");
                RequireData(Finite(b.length,1,1000) && Finite(b.hearts,0,5) && Finite(b.nutrition,0,100) && Finite(b.inheritance,.1,2) && Finite(b.overturnSeconds,0,100000) && b.maxHearts >= 1 && b.maxHearts <= 5 && b.hearts <= b.maxHearts && b.feed >= -1 && b.feed < 3 && b.climate >= 0 && b.climate < 3 && b.readyWeek >= 0 && b.value >= 1 && b.value <= CampaignEconomy.Maximum && b.initialValue >= 1 && b.initialValue <= CampaignEconomy.Maximum,"甲虫数值无效");
            }
            foreach (var e in State.expeditions) { Catalog.Catch(e.habitat); RequireData((e.count == 2 || e.count == 4) && e.dueWeek > State.week,"捕虫队数据无效"); }
            foreach (var o in State.offers) RequireData(State.beetles.Any(b=>b.id==o.beetleId&&b.listed) && o.price > 0 && o.price <= CampaignEconomy.Maximum,"报价数据无效");
            RequireData(ListedCount<=State.cabinet && State.sap>=0 && State.lamp>=0,"库存或展柜数据无效");
            if(State.pupaAuction!=null) RequireData(State.pupaAuction.bid>=150 && State.pupaAuction.bid<=100000 && State.pupaAuction.round>=0 && State.pupaAuction.round<3,"盲拍记录无效");
            if (State.battle != null) { var b = State.beetles.FirstOrDefault(x=>x.id==State.battle.beetleId); RequireData(b!=null && !b.dead && b.Adult(State.week) && !b.listed && State.battle.tier >= 1 && State.battle.tier <= 5 && !string.IsNullOrEmpty(State.battle.id),"赛事预订无效"); }
            if (State.auction != null) { var b=State.beetles.FirstOrDefault(x=>x.id==State.auction.beetleId); RequireData(b!=null && b.championships > 0 && State.auction.rounds >= 5 && State.auction.rounds <= 8 && State.auction.round >= 0 && State.auction.round <= State.auction.rounds && State.auction.bid > 0 && State.auction.finalBid >= State.auction.bid,"拍卖数据无效"); }
        }
        static bool Finite(double v,double min,double max) => !double.IsNaN(v) && !double.IsInfinity(v) && v >= min && v <= max;
    }
}
