using System;
using System.Linq;

namespace BugDuel.Campaign
{
    public sealed partial class CampaignSession
    {
        public long Asking(CampaignBeetle b) => CampaignEconomy.Money(b.value * (b.speciesId == State.trendSpecies ? 3 : 1));
        public void RefreshMarket()
        {
            if (State.marketWeek == State.week) return;
            State.marketWeek = State.week;
            if (string.IsNullOrEmpty(State.trendSpecies) || State.week % 4 == 1)
                State.trendSpecies = Catalog.species[(int)(Roll()*Catalog.species.Length)].id;
            State.stock.Clear();
            var unlocked = Catalog.catches.Where(Unlocked).SelectMany(c => c.species).Distinct().ToArray();
            for (int i=0;i<4;i++) { string id = unlocked[(int)(Roll()*unlocked.Length)]; var s = Catalog.Species(id); State.stock.Add(Create(id,s.length*(.7+Roll()*.3))); }
            State.offers.Clear();
            foreach (var b in State.beetles.Where(x => x.listed && !x.dead)) GenerateOffers(b);
        }
        public void Buy(string stockId)
        {
            var b = State.stock.FirstOrDefault(x=>x.id==stockId); Require(b != null,"本周现货已售出"); Spend(CampaignEconomy.Money(Asking(b)*1.1));
            State.stock.Remove(b); DeliverBeetle("store",b);
        }
        public void BuySupply(bool lamp) { Spend(lamp ? 200 : 120); DeliverItem(lamp?CampaignItems.Lamp:CampaignItems.Sap); }
        public void ExpandCabinet() { Require(!CabinetOrdered,"展柜已升级或扩展包已购买，请检查邮箱和仓库"); Spend(Catalog.cabinetUpgrade); DeliverItem(CampaignItems.Cabinet); }
        public void List(string id)
        {
            var b = Beetle(id); HealthyAdult(b); Require(State.beetles.Count(x=>x.listed) < State.cabinet,"挂牌展柜已满"); b.listed = true;
            GenerateOffers(b); Tell("挂牌成功。报价锁定到本周末，成交前可以撤回。");
        }
        public void Unlist(string id) { var b = Beetle(id); Require(b.listed,"尚未挂牌"); b.listed = false; State.offers.RemoveAll(o=>o.beetleId==id); Tell("已撤回挂牌"); }
        void GenerateOffers(CampaignBeetle b)
        {
            // Withdrawing/relisting must not reroll the same buyer's price during a week.
            uint seed=2166136261;foreach(char ch in b.id)seed=unchecked((seed^ch)*16777619);seed^=(uint)State.week;
            var quotes=new Random(unchecked((int)seed));
            void Offer(string buyer,double min,double max,bool female=false,bool recipe=false,bool permit=false)
            {
                State.offers.Add(new MarketOffer { id=Id(),beetleId=b.id,buyer=buyer,price=CampaignEconomy.Money(Asking(b)*(min+(max-min)*quotes.NextDouble())),week=State.week,femaleBonus=female,recipe=recipe,permit=permit });
            }
            Offer("狂热小学生",.7,.9);
            if (b.championships > 0 || b.genes.Any(g=>g.stars >= 2)) Offer("职业俱乐部球探",1.2,1.6,quotes.NextDouble()<.5);
            if (!string.IsNullOrEmpty(b.mutation)) Offer("猎奇变异大富豪",2,5);
            bool sibling = b.female && !string.IsNullOrEmpty(b.brood) && State.beetles.Any(x=>x.brood==b.brood&&x.championships>0);
            if (b.genes.Any(g=>g.stars==3) || sibling) Offer("名门育种老匠人",1.2,1.8,false,true);
            if (b.mutation.Contains("亚种")) Offer("昆虫学科学家",1.8,3,false,true,true);
        }
        public void AcceptOffer(string offerId)
        {
            var o = State.offers.FirstOrDefault(x=>x.id==offerId); Require(o != null && o.week == State.week,"报价已失效"); var b = Beetle(o.beetleId);
            Require(b.listed && !b.dead,"甲虫无法成交"); RemoveBeetle(b); State.offers.RemoveAll(x=>x.beetleId==b.id); Earn(o.price);
            if (o.buyer == "狂热小学生") State.reputation += 20;
            if (o.recipe) State.feedRecipe = true;
            if (o.permit) { State.scientistPermit = true; State.mutationRecipe = true; }
            if (o.femaleBonus) { var female = Create(b.speciesId,Catalog.Species(b.speciesId).length,2); female.female=true; DeliverBeetle("market",female); }
            Tell("成交 " + o.price + " G！买家：" + o.buyer + (o.femaleBonus ? "，附赠母虫已发送至邮箱。" : "。") + (o.recipe ? "解锁菌瓶配方：此后绿瓶体长+5%。" : "") + (o.permit ? "禁猎区许可证与突变合成图谱已解锁。" : ""));
        }
        public void StartPupaAuction()
        {
            Require(State.week % 2 == 0,"周末盲盒拍卖每逢双数周开放"); Require(State.lastPupaWeek != State.week && State.pupaAuction == null,"本周拍卖已参加");
            var choices = Catalog.catches.Where(Unlocked).SelectMany(c=>c.species).Distinct().ToArray(); string id = choices[(int)(Roll()*choices.Length)];
            var b = Create(id,Catalog.Species(id).length,Roll()<.25 ? 2 : 1); b.feed=-1; b.readyWeek=int.MaxValue; b.name="神秘蛹";
            State.pupaAuction=new PupaAuction { beetle=b,bid=150 }; State.lastPupaWeek=State.week; Tell("拍卖开场！每次举牌加价，三轮后揭晓蛹的物种。可以随时退出。");
        }
        public void BidPupa()
        {
            var a = State.pupaAuction; Require(a != null,"拍卖未开始"); long next = a.bid+50+(long)(Roll()*100); Require(State.coins >= next,"金币不足以继续举牌"); a.bid=next; a.round++;
            if (a.round >= 3) { Spend(a.bid); State.pupaAuction=null; a.beetle.name=Catalog.Species(a.beetle.speciesId).name+"蛹";DeliverBeetle("auction",a.beetle); }
            else Tell("NPC加价了，再举一次牌？当前 " + a.bid + " G。");
        }
        public void LeavePupaAuction() { Require(State.pupaAuction != null,"没有进行中的盲拍"); State.pupaAuction=null; Tell("已退出本周盲拍，未扣金币。"); }
    }
}
