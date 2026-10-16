using System;
using System.Linq;

namespace BugDuel.Campaign
{
    public sealed partial class CampaignSession
    {
        public void EnterLeague(string id)
        {
            Require(State.battle == null && State.auction == null,"请先完成当前赛事或拍卖"); var b = Beetle(id); HealthyAdult(b);
            var tier = Catalog.Tier(b.length); Spend(tier.fee);
            double max = tier.tier == 5 ? Math.Max(170,b.length+8) : tier.maximum-.1;
            double min = Math.Max(30,tier.minimum);
            var opponent = Create(b.speciesId,Math.Max(min,Math.Min(max,b.length-6+Roll()*15)),1);
            opponent.name="林间挑战者"; opponent.female=false;
            State.battle = new PendingBattle { id=Id(),beetleId=id,opponent=opponent,tier=tier.tier,fee=tier.fee,seed=(int)(Roll()*int.MaxValue) };
            Tell("报名成功：" + tier.name + "。赛事已保存，中途退出可继续或弃权。");
        }
        public void SettleBattle(string matchId,bool won)
        {
            var match = State.battle; Require(match != null && match.id == matchId,"赛事已结算或记录不匹配"); var b = Beetle(match.beetleId); var tier = Catalog.leagues[match.tier-1];
            b.nutrition=Math.Max(0,b.nutrition-15); b.hearts=Math.Max(1,b.hearts-(won ? .2 : .6));
            State.battle=null;
            if (!won) { b.value=CampaignEconomy.Lose(b); b.streak=0; b.seasonWins=0; Tell("比赛落败，身价-8%，保留初始身价80%的底价。休息好再来！"); return; }
            b.wins++; b.streak++; b.seasonWins++; b.value=CampaignEconomy.Win(b,match.opponent.length); Earn(tier.win);
            if (b.seasonWins < Catalog.seasonWins) { Tell("胜利！奖金 " + tier.win + " G；赛季进度 " + b.seasonWins + "/" + Catalog.seasonWins + "。身价 " + b.value + " G。"); return; }
            b.seasonWins=0; Earn(tier.champion); State.highestLeague=Math.Max(State.highestLeague,tier.tier);
            int bit = 1 << (tier.tier-1); bool first = (b.championships & bit) == 0; b.championships |= bit;
            if (first)
            {
                b.value=CampaignEconomy.Money(b.value*2.5);
                if (!string.IsNullOrEmpty(b.brood)) foreach (var sibling in State.beetles.Where(x=>x.id!=b.id&&x.brood==b.brood))
                { sibling.initialValue=CampaignEconomy.Money(sibling.initialValue*2); sibling.value=CampaignEconomy.Money(sibling.value*2); }
            }
            Tell("联赛总冠军！奖励 " + tier.champion + " G。" + (first ? "身价×2.5，同腹弟妹底价×2，冠军拍卖已解锁！" : "卫冕成功，获得赛季奖金！"));
        }
        public void Forfeit() { Require(State.battle != null,"没有未完成赛事"); SettleBattle(State.battle.id,false); }
        public void DrawBattle(string matchId)
        {
            var match=State.battle;Require(match!=null&&match.id==matchId,"赛事已结算或记录不匹配");
            var b=Beetle(match.beetleId);b.nutrition=Math.Max(0,b.nutrition-10);State.battle=null;
            Tell("时间到，双方平局。身价、连胜与赛季进度保留，本场不发胜场奖，报名费已消耗。");
        }
        public void BeginAuction(string id,bool rights)
        {
            Require(State.auction == null && State.battle == null,"请先完成当前活动"); var b=Beetle(id); HealthyAdult(b); Require(b.championships>0,"仅限联赛总冠军入场");
            Require(!rights || !b.rightsSold,"首配权已拍卖过，可以拍卖冠军本体");
            State.auction=new AuctionState { beetleId=id,rights=rights,rounds=5+(int)(Roll()*4),bid=b.value,finalBid=CampaignEconomy.Money(b.value*(2+Roll()*2.5)),bidder="财阀代表" };
            Tell(rights ? "冠军首配权专场开始，冠军留在温室。" : "冠军本体拍卖开始，成交后将离开温室。");
        }
        public void AdvanceAuction()
        {
            var a=State.auction; Require(a != null && a.round < a.rounds,"竞拍已经结束"); var b=Beetle(a.beetleId); a.round++;
            a.bid=CampaignEconomy.Money(b.value+(a.finalBid-b.value)*(double)a.round/a.rounds);
            a.bidder=new[]{"琥珀财团","栎木俱乐部总裁","金翅收藏家","世界虫王基金"}[(int)(Roll()*4)];
            Tell(a.bidder + " 举牌 " + a.bid + " G！（第 " + a.round + "/" + a.rounds + " 轮）");
        }
        public void CloseAuction()
        {
            var a=State.auction; Require(a != null && a.round == a.rounds,"请完成所有举牌轮次"); var b=Beetle(a.beetleId);
            State.hall.Add(new HallRecord { name=b.name,speciesId=b.speciesId,length=b.length,mutation=b.mutation,week=State.week,price=a.bid,rights=a.rights });
            Earn(a.bid); if (a.rights) b.rightsSold=true; else State.beetles.Remove(b); State.auction=null;
            Tell("落槌！" + a.bid + " G。已入名人堂；血统每周参加一次NPC赛事，胜场奖金的10%作为分红。");
        }
    }
}
