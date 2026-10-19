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
        public CampaignSession(CampaignState state, CampaignCatalog catalog) { State = state; Catalog = catalog; }
        public int Reserved => State.expeditions.Sum(e => e.count) + (State.pupaAuction == null ? 0 : 1);
        public CampaignBeetle Beetle(string id) => State.beetles.FirstOrDefault(x => x.id == id) ?? throw new InvalidOperationException("这只甲虫已离开温室");
        public double Roll() { uint x = State.random; x ^= x << 13; x ^= x >> 17; x ^= x << 5; State.random = x == 0 ? 18273u : x; return State.random / ((double)uint.MaxValue + 1); }
        public string Id() => Guid.NewGuid().ToString("N");
        public void Spend(long amount) { Require(amount >= 0 && State.coins >= amount, "金币不足"); State.coins -= amount; }
        public void Earn(long amount) { Require(amount >= 0, "金额无效"); State.coins = Math.Min(CampaignEconomy.Maximum, State.coins + amount); }
        public void Space(int count) => Require(State.beetles.Count + Reserved + count <= State.capacity, "温室已满（包含捕虫队预留栏位）");
        public void Available(CampaignBeetle b) { Require(!b.dead, "这只甲虫已死亡，请先清理栏位"); Require(!b.listed && State.battle?.beetleId != b.id && State.auction?.beetleId != b.id, "甲虫正在挂牌、参赛或拍卖"); }
        public void HealthyAdult(CampaignBeetle b) { Available(b); Require(b.Adult(State.week) && !b.overturned && b.hearts >= Math.Min(3,b.maxHearts) && b.nutrition >= 40, "需要健康、未翻身的成虫（至少3心与40饱食）"); }
        public static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
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
            var b = new CampaignBeetle { id = Id(), speciesId = speciesId, name = s.name, length = length, female = Roll() < .5, mutation = mutation, habitat = Catalog.housing[0].id };
            b.genes.Add(new Gene(CampaignGenetics.GeneIds[(int)(Roll()*5)], stars));
            b.initialValue = b.value = CampaignEconomy.Initial(b,s); return b;
        }
        public void Add(CampaignBeetle b) { Space(1); State.beetles.Add(b); State.selectedId = b.id; State.recordLength = Math.Max(State.recordLength,b.length); }
        void RemoveBeetle(CampaignBeetle b)
        {
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
            Spend(food == 0 ? 15 : 20); b.meal = new CampaignMeal { id=Id(), food=food }; b.hasMeal=true;
            Tell("已放下" + new[] { "红薯", "香蕉", "苹果" }[food] + "，" + b.name + "会走过去进食。");
        }
        public void CompleteMeal(string id,string mealId)
        {
            var b=Beetle(id); Available(b);
            Require(b.Adult(State.week) && !b.overturned && b.meal!=null && b.meal.id==mealId,"这份食物当前不能结算");
            b.nutrition=Math.Min(100,b.nutrition+(b.meal.food==0?35:25)); b.hearts=Math.Min(b.maxHearts,b.hearts+.5);
            b.meal=null;b.hasMeal=false;Tell(b.name+"吃好了，正在清理触角。");
        }
        public void Rescue(string id)
        {
            var b = Beetle(id); Available(b); Require(b.overturned,"它已经站稳啦"); b.overturned = false; b.overturnSeconds = 0; Tell("轻轻扶正了 " + b.name + "。记得补充食物和休息。");
        }
        public void House(string id,string habitat) { var b = Beetle(id); Available(b); Catalog.Housing(habitat); b.habitat = habitat; Tell("已移入 " + Catalog.Housing(habitat).name); }
        public void ClearDead(string id) { var b = Beetle(id); Require(b.dead,"只有死亡甲虫可以清理；健康甲虫请在市场交易"); RemoveBeetle(b); Tell("已清理栏位，留下了一份小小的纪念。"); }
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
                    { b.overturned = true; b.overturnSeconds = 0; Tell(b.name + " 翻不过身了！请到温室帮忙（60秒后开始受伤）。"); }
                }
                else
                {
                    double previous = b.overturnSeconds; b.overturnSeconds += seconds;
                    b.hearts = Math.Max(0,b.hearts - (Math.Max(0,b.overturnSeconds-60)-Math.Max(0,previous-60))/24);
                    if (b.hearts <= 0) { b.dead = true; b.overturned = false; Tell(b.name + " 因长时间无法翻身而死亡。"); }
                }
            }
        }
        public void Grow(string id,int feed,int climate)
        {
            var b = Beetle(id); Available(b); Require(!b.Adult(State.week),"成虫体长已定型，饲料瓶用于幼虫"); Require(feed >= 0 && feed < 3 && climate >= 0 && climate < 3,"养成方案无效");
            Require(b.feed == -1,"已选好本次养成方案"); Spend(Catalog.feeds[feed].cost); b.feed = feed; b.climate = climate;
            b.readyWeek = State.week + (climate == 0 ? 2 : climate == 1 ? 3 : 4 + (Roll() < .5 ? 0 : 1));
            Tell("养成开始，第 " + b.readyWeek + " 周出栏。幼虫会自动获得瓶内食物。");
        }
        public void Breed(string fatherId,string motherId)
        {
            var father = Beetle(fatherId); var mother = Beetle(motherId); HealthyAdult(father); HealthyAdult(mother);
            Require(!father.female && mother.female && father.id != mother.id && father.speciesId == mother.speciesId,"需要同物种的雄虫与雌虫");
            Require(father.cooldownWeek <= State.week && mother.cooldownWeek <= State.week,"亲本需要休息到下一周"); Space(1); Spend(100);
            bool related = CampaignGenetics.Related(father,mother); int depth = related ? Math.Max(father.inbred,mother.inbred)+1 : 0;
            Require(depth < 5,"近亲世代已达到健康极限，请换血");
            var b = Create(father.speciesId,Catalog.Species(father.speciesId).length);
            b.father = father.id; b.mother = mother.id; b.brood = father.id + ":" + mother.id;
            b.ancestors = new[] { father.id,mother.id }.Concat(father.ancestors).Concat(mother.ancestors).Distinct().Take(128).ToList();
            b.genes = CampaignGenetics.Inherit(father,mother); b.inbred = depth; b.maxHearts = 5 - depth; b.hearts = b.maxHearts;
            double parentFactor = (father.length + mother.length)/(2*Catalog.Species(b.speciesId).length);
            b.inheritance = Math.Max(.90,Math.Min(1.25,parentFactor * (.95+Roll()*.1))) * (related ? 1 : 1.05);
            b.readyWeek = int.MaxValue; b.feed = -1; b.mutation = CampaignGenetics.Combo(b.genes); b.name = Catalog.Species(b.speciesId).name + "幼虫";
            father.cooldownWeek = mother.cooldownWeek = State.week+1; father.nutrition -= 15; mother.nutrition -= 20; Add(b);
            Tell(related ? "近亲后代诞生：健康上限 " + b.maxHearts + " 心，请选择饲料与温控。" : "换血成功：5心上限，体长额外+5%。请选择饲料与温控。");
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
            // A week is ample time to finish placed food. No second payment and no scene dependency.
            foreach(var b in State.beetles.Where(x=>x.Adult(State.week)&&!x.listed&&x.meal!=null))CompleteMeal(b.id,b.meal.id);
            State.week++; if (work) Earn(150);
            int matured = 0, caught = 0;
            foreach (var b in State.beetles.Where(x => !x.dead))
            {
                if (b.readyWeek == State.week)
                {
                    var f = Catalog.feeds[Math.Max(0,b.feed)]; b.length = Catalog.Species(b.speciesId).length * b.inheritance * f.multiplier * (b.climate == 0 ? .88 : b.climate == 1 ? 1 : 1.22) * CampaignGenetics.LengthBonus(b.genes);
                    if (b.feed == 0 && State.feedRecipe) b.length *= 1.05;
                    if (State.mutationRecipe && string.IsNullOrEmpty(b.mutation)) b.mutation = CampaignGenetics.Combo(b.genes);
                    if (string.IsNullOrEmpty(b.mutation) && Roll() < f.mutationChance) b.mutation = Roll() < .5 ? "白眼" : "三叉角";
                    b.name = Catalog.Species(b.speciesId).name; b.initialValue = b.value = CampaignEconomy.Initial(b,Catalog.Species(b.speciesId)); State.recordLength = Math.Max(State.recordLength,b.length); matured++;
                }
                if (b.Adult(State.week))
                { b.nutrition = Math.Max(0,b.nutrition-20); b.hearts = Math.Max(0,Math.Min(b.maxHearts,b.hearts + (b.nutrition >= 40 ? Catalog.Housing(b.habitat).healthRecovery : -.75))); if (b.hearts <= 0) { b.dead = true; b.listed = false; } }
            }
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
            Tell("第 " + State.week + " 周：出栏 " + matured + " 只，捕回 " + caught + " 只，血统分红 " + royalty + " G。" + (work ? "虫舍帮工收入150 G。" : "记得给成虫添食。"));
        }
        public void Validate()
        {
            Catalog.Validate();
            Require(State != null && State.schema == 1 && State.week > 0 && State.week < 1000000 && State.random != 0 && (State.capacity == 6 || State.capacity == 12 || State.capacity == 20) && (State.cabinet == 3 || State.cabinet == 6) && State.highestLeague >= 0 && State.highestLeague <= 5 && State.coins >= 0 && State.coins <= CampaignEconomy.Maximum,"存档主数据无效");
            Require(State.beetles != null && State.expeditions != null && State.offers != null && State.stock != null && State.hall != null,"存档集合缺失"); Space(0);
            var ids = new HashSet<string>();
            Catalog.Species(State.trendSpecies);
            foreach (var h in State.hall) { Catalog.Species(h.speciesId); Require(Finite(h.length,1,1000) && h.price > 0 && h.price <= CampaignEconomy.Maximum && h.royalties >= 0,"名人堂数据无效"); }
            foreach (var b in State.beetles.Concat(State.stock).Concat(State.battle == null ? new CampaignBeetle[0] : new[]{State.battle.opponent}).Concat(State.pupaAuction == null ? new CampaignBeetle[0] : new[]{State.pupaAuction.beetle}))
            {
                Require(b != null && !string.IsNullOrEmpty(b.id) && ids.Add(b.id),"甲虫 ID 重复或缺失"); Catalog.Species(b.speciesId); Catalog.Housing(b.habitat);
                Require(b.genes != null && b.genes.Count > 0 && b.genes.Count <= 5 && b.genes.All(g => g != null && CampaignGenetics.GeneIds.Contains(g.id) && g.stars >= 1 && g.stars <= 3) && b.ancestors != null,"基因或血统数据无效");
                Require((b.meal!=null)==b.hasMeal && (b.meal==null || !string.IsNullOrWhiteSpace(b.meal.id)&&b.meal.food>=0&&b.meal.food<=2),"待进食记录无效");
                Require(Finite(b.length,1,1000) && Finite(b.hearts,0,5) && Finite(b.nutrition,0,100) && Finite(b.inheritance,.1,2) && Finite(b.overturnSeconds,0,100000) && b.maxHearts >= 1 && b.maxHearts <= 5 && b.hearts <= b.maxHearts && b.feed >= -1 && b.feed < 3 && b.climate >= 0 && b.climate < 3 && b.readyWeek >= 0 && b.value >= 1 && b.value <= CampaignEconomy.Maximum && b.initialValue >= 1 && b.initialValue <= CampaignEconomy.Maximum,"甲虫数值无效");
            }
            foreach (var e in State.expeditions) { Catalog.Catch(e.habitat); Require((e.count == 2 || e.count == 4) && e.dueWeek > State.week,"捕虫队数据无效"); }
            foreach (var o in State.offers) Require(State.beetles.Any(b=>b.id==o.beetleId&&b.listed) && o.price > 0 && o.price <= CampaignEconomy.Maximum,"报价数据无效");
            Require(State.beetles.Count(b=>b.listed)<=State.cabinet && State.sap>=0 && State.lamp>=0,"库存或展柜数据无效");
            if(State.pupaAuction!=null) Require(State.pupaAuction.bid>=150 && State.pupaAuction.bid<=100000 && State.pupaAuction.round>=0 && State.pupaAuction.round<3,"盲拍记录无效");
            if (State.battle != null) { var b = Beetle(State.battle.beetleId); Require(!b.dead && b.Adult(State.week) && !b.listed && State.battle.tier >= 1 && State.battle.tier <= 5 && !string.IsNullOrEmpty(State.battle.id),"赛事预订无效"); }
            if (State.auction != null) Require(Beetle(State.auction.beetleId).championships > 0 && State.auction.rounds >= 5 && State.auction.rounds <= 8 && State.auction.round >= 0 && State.auction.round <= State.auction.rounds && State.auction.bid > 0 && State.auction.finalBid >= State.auction.bid,"拍卖数据无效");
        }
        static bool Finite(double v,double min,double max) => !double.IsNaN(v) && !double.IsInfinity(v) && v >= min && v <= max;
    }
}
