using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BugDuel.Campaign
{
    [Serializable] public sealed class CampaignSpecimen
    {
        public string id, labelName, speciesLabel, dateLabel;
        public CampaignBeetle beetle;
        public int preparedWeek, wallSlot = -1, frameStyle, plaqueVersion;
        public double rotation;
        public bool listed,customLabelName,customSpeciesLabel,customDateLabel;
        public long value;
    }

    [Serializable] public sealed class SpecimenOffer
    {
        public string id, specimenId, buyer;
        public long price;
        public int week;
    }

    [Serializable] public sealed class MarketAuctionLot
    {
        public string id, kind, name, highBidder = "暂无", result = "";
        public long expectedPrice, currentBid, playerBid, rivalLimit;
        public int round, rounds = 4;
        public bool closed, won, playerParticipated, highBidderIsPlayer;
        // Lists preserve absent assets through Unity's inline save serialization.
        public List<CampaignBeetle> beetles = new List<CampaignBeetle>();
        public List<CampaignSpecimen> specimens = new List<CampaignSpecimen>();
        public CampaignBeetle Beetle => beetles.FirstOrDefault();
        public CampaignSpecimen Specimen => specimens.FirstOrDefault();
    }

    public sealed partial class CampaignSession
    {
        public const int SpecimenWallCapacity = 12, SpecimenPreparationCost = 40;
        public int ListedCount => State.beetles.Count(b=>b.listed) + State.specimens.Count(p=>p.listed);
        public long PurchasePrice(CampaignBeetle beetle) => CampaignEconomy.Money(Asking(beetle)*1.1);
        public long BeetleSellPrice(CampaignBeetle beetle) => CampaignEconomy.Money(Asking(beetle)*(beetle.Adult(State.week)?.55:.35));
        public long SaleProceeds(long gross) => gross - Math.Max(1,(long)Math.Ceiling(gross*.05));
        public CampaignSpecimen Specimen(string id) => State.specimens.FirstOrDefault(p=>p.id==id) ?? throw new CampaignRuleException("标本已离开收藏库");
        public long SpecimenAsking(CampaignSpecimen p) => CampaignEconomy.Money(p.value*(p.beetle.speciesId==State.trendSpecies?1.35:1));
        public long SpecimenPurchasePrice(CampaignSpecimen p) => CampaignEconomy.Money(SpecimenAsking(p)*1.25);
        public long SpecimenSellPrice(CampaignSpecimen p) => CampaignEconomy.Money(SpecimenAsking(p)*.6);

        void UpgradeTrading()
        {
            if(State.tradingVersion==0)
            {
                State.specimens=State.specimens??new List<CampaignSpecimen>();
                State.specimenStock=State.specimenStock??new List<CampaignSpecimen>();
                State.specimenOffers=State.specimenOffers??new List<SpecimenOffer>();
                State.marketAuctions=State.marketAuctions??new List<MarketAuctionLot>();
                foreach(var a in State.mail.SelectMany(m=>m.attachments))a.specimens=a.specimens??new List<CampaignSpecimen>();
                State.tradingVersion=1;
                if(State.marketWeek>0)RefreshTradingStock();
            }
            foreach(var p in AllSpecimens())
            {
                if(p==null||p.beetle==null||p.plaqueVersion!=0)continue;
                p.customLabelName=p.beetle.customName||p.labelName!=p.beetle.name;
                p.customSpeciesLabel=p.speciesLabel!=Catalog.Species(p.beetle.speciesId).name;
                p.customDateLabel=!DefaultSpecimenDate(p.dateLabel,p.preparedWeek);
                p.plaqueVersion=1;
            }
            foreach(var mail in State.mail)
                foreach(var attachment in mail.attachments)
                    if(attachment.specimens.Count>0)
                    {
                        var p=attachment.specimens[0];attachment.literalName=p.customLabelName;
                        if(!p.customLabelName&&mail.subject=="标本配送 · "+CampaignDisplayText.Literal(p.labelName))mail.subject="标本配送 · "+p.labelName;
                    }
        }

        static bool DefaultSpecimenDate(string text,int week)
        {
            if(text=="第 "+week+" 周")return true;
            var match=Regex.Match(text??"",@"\A第 (\d+) 年 (\d+) 月 (\d+) 日\z");
            if(!match.Success||!int.TryParse(match.Groups[1].Value,out int year)||!int.TryParse(match.Groups[2].Value,out int month)||!int.TryParse(match.Groups[3].Value,out int day))return false;
            return year>0&&month>=1&&month<=12&&day>=1&&day<=30&&((year-1L)*360+(month-1)*30+day-1)/7+1==week;
        }

        CampaignSpecimen CreateSpecimen(CampaignBeetle beetle,string date)
        {
            beetle.dead=true;beetle.hearts=0;beetle.overturned=false;beetle.listed=false;beetle.meal=null;beetle.hasMeal=false;
            return new CampaignSpecimen { id=Id(),beetle=beetle,labelName=beetle.name,speciesLabel=Catalog.Species(beetle.speciesId).name,
                dateLabel=date,preparedWeek=State.week,value=CampaignEconomy.Money(beetle.value*.38),plaqueVersion=1,
                customLabelName=beetle.customName,customDateLabel=!DefaultSpecimenDate(date,State.week) };
        }

        public string PreserveDead(string id,string dateLabel)
        {
            var b=State.beetles.Concat(State.storedBeetles).FirstOrDefault(x=>x.id==id);Require(b!=null&&b.dead,"只有自有死亡甲虫可以制作标本");
            Require(!string.IsNullOrWhiteSpace(dateLabel)&&dateLabel.Length<=40,"日期需要填写，最多40字");
            Spend(SpecimenPreparationCost);RemoveBeetle(b);State.storedBeetles.Remove(b);State.offers.RemoveAll(o=>o.beetleId==id);
            var p=CreateSpecimen(b,dateLabel.Trim());State.specimens.Add(p);
            Tell(CampaignDisplayText.Name(b)+"已装框保存。可前往标本室编辑铭牌、布置展示或出售。");return p.id;
        }

        public void EditSpecimen(string id,string name,string speciesLabel,string dateLabel,int frameStyle,double rotation)
        {
            var p=Specimen(id);Require(!p.listed,"请先撤回挂牌，再编辑标本");
            Require(!string.IsNullOrWhiteSpace(name)&&name.Length<=40&&!string.IsNullOrWhiteSpace(speciesLabel)&&speciesLabel.Length<=60&&!string.IsNullOrWhiteSpace(dateLabel)&&dateLabel.Length<=40,"铭牌内容不能为空，名称/日期最多40字，种类最多60字");
            Require(frameStyle>=0&&frameStyle<=2&&Finite(rotation,-180,180),"相框或旋转角度无效");
            name=name.Trim();speciesLabel=speciesLabel.Trim();dateLabel=dateLabel.Trim();
            p.customLabelName|=name!=p.labelName;p.customSpeciesLabel|=speciesLabel!=p.speciesLabel;p.customDateLabel|=dateLabel!=p.dateLabel;
            p.labelName=name;p.speciesLabel=speciesLabel;p.dateLabel=dateLabel;p.frameStyle=frameStyle;p.rotation=rotation;
            Tell("标本铭牌与相框已保存。");
        }

        public void PlaceSpecimen(string id,int slot)
        {
            var p=Specimen(id);Require(!p.listed,"挂牌标本需先撤回，才能布置");Require(slot>=0&&slot<SpecimenWallCapacity,"墙面只有12个展示位");
            var other=State.specimens.FirstOrDefault(x=>x.wallSlot==slot&&x.id!=id);
            if(other!=null)other.wallSlot=p.wallSlot;
            p.wallSlot=slot;Tell(other==null?"标本已挂上墙面。":"两件标本已交换位置。");
        }
        public void UnmountSpecimen(string id) { var p=Specimen(id);Require(!p.listed,"请先撤回挂牌");p.wallSlot=-1;Tell("标本已收回收藏库。"); }

        public void SellSpecimen(string id)
        {
            var p=Specimen(id);Require(!p.listed,"请先撤回挂牌再卖给商店");long price=SpecimenSellPrice(p);State.specimens.Remove(p);Earn(price);
            Tell(CampaignDisplayText.SpecimenName(p)+"已售予收藏店，收到 "+price+" G。");
        }
        public void BuySpecimen(string stockId)
        {
            var p=State.specimenStock.FirstOrDefault(x=>x.id==stockId);Require(p!=null,"这件标本已售出");Spend(SpecimenPurchasePrice(p));
            State.specimenStock.Remove(p);DeliverSpecimen("store",p);
        }
        public void ListSpecimen(string id)
        {
            var p=Specimen(id);Require(!p.listed,"标本已挂牌");Require(ListedCount<State.cabinet,"挂牌展柜已满");
            p.listed=true;p.wallSlot=-1;GenerateSpecimenOffers(p);Tell("标本已寄存集市，成交收取5%佣金，撤回后可重新挂墙。");
        }
        public void UnlistSpecimen(string id) { var p=Specimen(id);Require(p.listed,"标本尚未挂牌");p.listed=false;State.specimenOffers.RemoveAll(o=>o.specimenId==id);Tell("标本已撤回收藏库。"); }
        void GenerateSpecimenOffers(CampaignSpecimen p)
        {
            var random=WeeklyQuotes(p.id);
            State.specimenOffers.Add(new SpecimenOffer { id=Id(),specimenId=p.id,buyer="自然博物馆",price=CampaignEconomy.Money(SpecimenAsking(p)*(.85+random.NextDouble()*.15)),week=State.week });
            if(p.beetle.championships>0||!string.IsNullOrEmpty(p.beetle.mutation))
                State.specimenOffers.Add(new SpecimenOffer { id=Id(),specimenId=p.id,buyer="私人标本收藏家",price=CampaignEconomy.Money(SpecimenAsking(p)*(1+random.NextDouble()*.15)),week=State.week });
        }
        public void AcceptSpecimenOffer(string offerId)
        {
            var o=State.specimenOffers.FirstOrDefault(x=>x.id==offerId);Require(o!=null&&o.week==State.week,"报价已失效");var p=Specimen(o.specimenId);Require(p.listed,"标本未挂牌");
            long net=SaleProceeds(o.price);State.specimens.Remove(p);State.specimenOffers.RemoveAll(x=>x.specimenId==p.id);Earn(net);
            Tell(CampaignDisplayText.SpecimenName(p)+"由"+o.buyer+"收藏。成交 "+o.price+" G，扣除5%佣金后收到 "+net+" G。");
        }

        public long SupplyBuyPrice(string itemId)
        {
            Require(CampaignItems.Known(itemId),"用品不存在");
            if(CampaignRearingItems.All.Contains(itemId))return CampaignRearingItems.Price(itemId);
            if(CampaignDecorations.Known(itemId))return CampaignDecorations.Price(itemId);
            if(itemId==CampaignItems.Cabinet)return Catalog.cabinetUpgrade;
            if(itemId==CampaignItems.Sap)return 120;
            if(itemId==CampaignItems.Lamp)return 200;
            return CampaignItems.FoodIndex(itemId)==0?15:20;
        }
        public long SupplySellPrice(string itemId) => Math.Max(1,(long)Math.Floor(SupplyBuyPrice(itemId)*.4));
        public void BuyItem(string itemId,int quantity=1)
        {
            Require(quantity>=1&&quantity<=99,"每次购买1至99件");long price=SupplyBuyPrice(itemId);
            if(itemId==CampaignItems.Cabinet)Require(quantity==1&&!CabinetOrdered,"展柜已升级或扩展包已购买，请检查邮箱和仓库");
            long pending=State.mail.Where(m=>m.Pending).SelectMany(m=>m.attachments).Where(a=>a.itemId==itemId).Sum(a=>(long)a.quantity);
            Require(OwnedSupplyCount(itemId)+pending+quantity<=1000000,"仓库数量已达上限");Spend(price*quantity);
            Deliver("supplies","订单配送 · "+CampaignItems.Name(itemId),new CampaignAttachment {itemId=itemId,name=CampaignItems.Name(itemId),quantity=quantity});
        }
        public void SellItem(string itemId,int quantity=1)
        {
            Require(quantity>=1&&quantity<=99,"每次出售1至99件");long price=SupplySellPrice(itemId);Require(InventoryCount(itemId)>=quantity,"仓库库存不足，请先领取邮件附件");
            if(itemId==CampaignItems.Sap)State.sap-=quantity;
            else if(itemId==CampaignItems.Lamp)State.lamp-=quantity;
            else {var stack=State.inventory.First(x=>x.itemId==itemId);stack.quantity-=quantity;if(stack.quantity==0)State.inventory.Remove(stack);}
            Earn(price*quantity);Tell(CampaignItems.Name(itemId)+" ×"+quantity+" 已回售，收到 "+price*quantity+" G。");
        }
        public void SellBeetle(string id)
        {
            var b=State.beetles.Concat(State.storedBeetles).FirstOrDefault(x=>x.id==id);Require(b!=null,"没有这只甲虫");Available(b);
            long price=BeetleSellPrice(b);
            RemoveBeetle(b);State.storedBeetles.Remove(b);Earn(price);Tell(CampaignDisplayText.Name(b)+"已售予甲虫商店，收到 "+price+" G。");
        }

        Random WeeklyQuotes(string id)
        {
            uint seed=2166136261;foreach(char ch in id)seed=unchecked((seed^ch)*16777619);seed^=(uint)State.week;
            return new Random(unchecked((int)seed));
        }
        void RefreshTradingStock()
        {
            State.specimenStock.Clear();State.specimenOffers.Clear();
            var choices=Catalog.catches.Where(Unlocked).SelectMany(c=>c.species).Distinct().ToArray();
            for(int i=0;i<3;i++)
            {
                string species=choices[(int)(Roll()*choices.Length)];var b=Create(species,Catalog.Species(species).length*(.85+Roll()*.2));
                State.specimenStock.Add(CreateSpecimen(b,"第 "+State.week+" 周"));
            }
            foreach(var p in State.specimens.Where(x=>x.listed))GenerateSpecimenOffers(p);
            State.marketAuctions.RemoveAll(a=>a.closed);
            for(int i=0;i<3;i++)
            {
                string species=choices[(int)(Roll()*choices.Length)];var b=Create(species,Catalog.Species(species).length*(.9+Roll()*.22),Roll()<.2?2:1);
                var a=new MarketAuctionLot {id=Id(),kind=i==2?"specimen":"beetle",name=b.name+" "+b.length.ToString("0.0")+"mm"};
                if(i==2){var p=CreateSpecimen(b,"第 "+State.week+" 周");a.specimens.Add(p);a.expectedPrice=SpecimenPurchasePrice(p);}
                else {a.beetles.Add(b);a.expectedPrice=PurchasePrice(b);}
                a.rivalLimit=Math.Max(1,(long)Math.Floor(a.expectedPrice*(.4+Roll()*.55)));State.marketAuctions.Add(a);
            }
        }

        public MarketAuctionLot GetMarketAuction(string id) => State.marketAuctions.FirstOrDefault(a=>a.id==id) ?? throw new CampaignRuleException("拍品不存在或已换周");
        public long AuctionMinimumBid(MarketAuctionLot a) => Math.Max((long)Math.Ceiling(a.expectedPrice*.25),a.currentBid+Math.Max(10,(long)Math.Ceiling(a.expectedPrice*.05)));
        public double AuctionChance(long bid,long expected) => bid>=expected?1:Math.Pow(Math.Max(0,(double)bid/expected),2);
        public void BidMarketAuction(string id,long amount)
        {
            var a=GetMarketAuction(id);Require(!a.closed,"本场拍卖已落槌");Require(amount>=AuctionMinimumBid(a)&&amount<=CampaignEconomy.Maximum,"出价必须达到最低加价且不能超过金币上限");
            Spend(amount-a.playerBid);a.playerBid=amount;a.currentBid=amount;a.highBidder=State.playerName;a.highBidderIsPlayer=true;a.playerParticipated=true;
            Tell(PlayerSignature+"举牌 "+amount+" G，已托管出价。推进轮次后其他买家会回应；被超价或流拍会退还托管金币。");
        }
        public void AdvanceMarketAuction(string id)
        {
            var a=GetMarketAuction(id);Require(!a.closed,"本场拍卖已落槌");
            long next=AuctionMinimumBid(a);
            if(next<=a.rivalLimit&&(a.playerBid==0||Roll()<.68))
            {
                long rival=next+(long)((a.rivalLimit-next)*Roll()*.5);
                if(a.playerBid>0){long refund=a.playerBid;a.playerBid=0;Earn(refund);}
                a.currentBid=rival;a.highBidder=new[]{"琥珀收藏家","青苔虫舍","林间育种师"}[(int)(Roll()*3)];a.highBidderIsPlayer=false;
            }
            a.round++;
            if(a.round>=a.rounds)CloseMarketAuction(a);
            else Tell("第 "+a.round+"/"+a.rounds+" 轮，最高出价："+AuctionBidderName(a)+" "+a.currentBid+" G。"+(a.playerBid>0?"你仍领先。":"可以继续加价。"));
        }
        void CloseMarketAuction(MarketAuctionLot a)
        {
            a.closed=true;bool accepted=a.currentBid>0&&Roll()<AuctionChance(a.currentBid,a.expectedPrice);
            a.won=accepted&&a.playerBid>0;
            if(a.won)
            {
                if(a.kind=="beetle")DeliverBeetle("auction",a.Beetle);else DeliverSpecimen("auction",a.Specimen);
                a.beetles.Clear();a.specimens.Clear();a.result=PlayerSignature+"竞拍成功，成交 "+a.currentBid+" G；拍品已发送至邮箱。";
                State.mail.Last().body=a.result+"\n\n卖家期望价："+a.expectedPrice+" G。领取附件后可从仓库安排活体，或在标本室布置标本。";
            }
            else
            {
                if(a.playerBid>0){long refund=a.playerBid;a.playerBid=0;Earn(refund);}
                a.result=accepted?"落槌，由"+AuctionBidderName(a)+"以 "+a.currentBid+" G 获得。":"未达到卖家的本轮接受条件，拍品流拍，托管金币已退还。";
                if(a.playerParticipated)State.mail.Add(new CampaignMail {id=Id(),senderId="auction",week=State.week,subject="竞拍结果 · "+a.name,
                    body=a.result+"\n\n你未获得拍品，全部托管出价已退还。卖家期望价："+a.expectedPrice+" G。"});
            }
            a.playerBid=0;Tell(a.result);
        }
        void SettleWeeklyAuctions()
        {
            foreach(var a in State.marketAuctions.Where(x=>!x.closed).ToArray())
                while(!a.closed)AdvanceMarketAuction(a.id);
        }

        void DeliverSpecimen(string sender,CampaignSpecimen p)
        {
            p.wallSlot=-1;p.listed=false;
            Deliver(sender,"标本配送 · "+CampaignDisplayText.SpecimenName(p),new CampaignAttachment {itemId=CampaignItems.Specimen,name=p.labelName,literalName=p.customLabelName,specimens=new List<CampaignSpecimen>{p}});
            State.mail.Last().body="装框标本已送达。领取附件后，前往标本室收藏库，可编辑铭牌、放置墙面或重新交易。";
        }
        IEnumerable<CampaignSpecimen> AllSpecimens() => State.specimens.Concat(State.specimenStock).Concat(State.mail.SelectMany(m=>m.attachments).SelectMany(a=>a.specimens)).Concat(State.marketAuctions.SelectMany(a=>a.specimens));
        IEnumerable<CampaignBeetle> TradingBeetles() => AllSpecimens().Select(p=>p.beetle).Concat(State.marketAuctions.SelectMany(a=>a.beetles));
        void ValidateTrading()
        {
            RequireData(State.tradingVersion==1&&State.specimens!=null&&State.specimenStock!=null&&State.specimenOffers!=null&&State.marketAuctions!=null,"交易或标本数据缺失");
            var ids=new HashSet<string>();var slots=new HashSet<int>();
            foreach(var a in State.marketAuctions)
            {
                RequireData(a!=null&&!string.IsNullOrWhiteSpace(a.id)&&ids.Add(a.id)&&a.beetles!=null&&a.specimens!=null,"拍品数据无效");
                RequireData((a.kind=="beetle"||a.kind=="specimen")&&a.rounds==4&&a.round>=0&&a.round<=a.rounds&&a.closed==(a.round==a.rounds)&&a.expectedPrice>0&&a.expectedPrice<=CampaignEconomy.Maximum&&a.currentBid>=0&&a.currentBid<=CampaignEconomy.Maximum&&a.rivalLimit>0&&a.rivalLimit<a.expectedPrice,"竞拍数值无效");
                RequireData(a.playerBid>=0&&a.playerBid<=a.currentBid&&(a.playerBid==0||a.playerBid==a.currentBid&&a.highBidderIsPlayer)&&(!a.closed||a.playerBid==0)&&(!a.won||a.closed&&a.highBidderIsPlayer),"竞拍托管记录无效");
                RequireData(a.won?a.beetles.Count+a.specimens.Count==0:a.kind=="beetle"?a.beetles.Count==1&&a.specimens.Count==0:a.beetles.Count==0&&a.specimens.Count==1,"拍品所有权无效");
            }
            RequireData(State.coins+State.marketAuctions.Sum(a=>a.playerBid)<=CampaignEconomy.Maximum,"可用金币与拍卖托管合计超过上限");
            ids.Clear();
            foreach(var p in AllSpecimens())
            {
                RequireData(p!=null&&!string.IsNullOrWhiteSpace(p.id)&&ids.Add(p.id)&&p.beetle!=null&&p.beetle.dead&&!p.beetle.listed&&!p.beetle.hasMeal,"标本所有权无效");
                RequireData(!string.IsNullOrWhiteSpace(p.labelName)&&p.labelName.Length<=40&&!string.IsNullOrWhiteSpace(p.speciesLabel)&&p.speciesLabel.Length<=60&&!string.IsNullOrWhiteSpace(p.dateLabel)&&p.dateLabel.Length<=40&&p.preparedWeek>0&&p.preparedWeek<=State.week&&p.value>0&&p.value<=CampaignEconomy.Maximum,"标本铭牌或价值无效");
                RequireData(p.frameStyle>=0&&p.frameStyle<=2&&Finite(p.rotation,-180,180)&&p.wallSlot>=-1&&p.wallSlot<SpecimenWallCapacity&&(!p.listed||p.wallSlot==-1),"标本布置数据无效");
                RequireData(State.specimens.Contains(p)||p.wallSlot==-1&&!p.listed,"未拥有的标本不能展示或挂牌");
                if(p.wallSlot>=0)RequireData(slots.Add(p.wallSlot),"同一墙位不能挂两件标本");
            }
            ids.Clear();
            foreach(var o in State.specimenOffers)
                RequireData(o!=null&&!string.IsNullOrWhiteSpace(o.id)&&ids.Add(o.id)&&o.week==State.week&&o.price>0&&o.price<=CampaignEconomy.Maximum&&State.specimens.Any(p=>p.id==o.specimenId&&p.listed),"标本报价无效");
        }
    }
}
