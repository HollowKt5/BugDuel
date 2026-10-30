using System.Linq;
using UnityEngine;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        void DrawMarketChampionAuction(RectTransform window)
        {
            float padding=Layout.market.gridPadding,gap=Layout.market.gap.x,top=154;
            var panel=MBox("ChampionAuctions",window,3,top,window.rect.width-6,window.rect.height-top-3,CampaignRetroTheme.Grid,CampaignRetroTheme.Border,2);
            float width=(panel.rect.width-padding*2-gap)/2,height=panel.rect.height-padding*2;
            var champions=Column(padding,padding,width,height,panel,name:"Champions");
            var history=Column(padding+width+gap,padding,width,height,panel,name:"ChampionHistory");
            champions.rightInset=history.rightInset=0;
            float pad=Layout.market.cardPadding,inside=width-pad*2;
            if(State.auction==null)
            {
                bool canStart=State.battle==null&&State.pupaAuction==null;int count=0;
                foreach(var beetle in State.beetles.Where(b=>b.championships>0&&!b.dead))
                {
                    count++;var b=beetle;
                    var card=MarketFlowCard("ChampionCard",champions,Layout.market.AuctionCardHeight,out var header,out var footer);
                    MarketCardHeading(header,"冠军甲虫",b.rightsSold?"权利已售":"可拍卖");
                    float textX=pad*2+Layout.market.thumbnailSize,bodyTop=30+pad;
                    MarketCardArtwork(card,BeetleMarketArtwork(b),bodyTop);
                    MText(card,CampaignLocalization.Name(b),textX,bodyTop,width-textX-pad,52,21,CampaignRetroTheme.Ink,true);
                    MText(card,$"{b.length:0.0} mm · 身价 {b.value:N0} G",textX,bodyTop+56,width-textX-pad,60,18);
                    MText(card,b.mutation,pad,bodyTop+Mathf.Max(112,Layout.market.thumbnailSize)+12,inside,60,18,CampaignRetroTheme.Muted);
                    float buttonWidth=(footer.rect.width-18)/2;
                    MButton(footer,"拍卖本体",5,5,buttonWidth,32,()=>Confirm("本体成交后会离开温室，并获得持续血统分红。开始拍卖？",()=>app.Execute(s=>s.BeginAuction(b.id,false))),canStart,18);
                    MButton(footer,"拍卖首配权",13+buttonWidth,5,buttonWidth,32,()=>app.Execute(s=>s.BeginAuction(b.id,true)),canStart&&!b.rightsSold,18);
                }
                if(count==0)
                {
                    var empty=MarketFlowCard("ChampionEmpty",champions,200,out var header,out var footer);
                    MarketCardHeading(header,"冠军拍卖");MText(empty,"暂无冠军甲虫",pad,64,inside,44,21);
                    MarketCardAction(footer,"去联赛",()=>app.Go(CampaignPage.League));
                }
            }
            else
            {
                var auction=State.auction;var beetle=Session.Beetle(auction.beetleId);
                var card=MarketFlowCard("ChampionLive",champions,Layout.market.AuctionCardHeight,out var header,out var footer);
                MarketCardHeading(header,auction.rights?"冠军首配权":"冠军本体",$"{auction.round}/{auction.rounds} 轮");
                MText(card,CampaignLocalization.Name(beetle),pad,48,inside,52,21,CampaignRetroTheme.Ink,true);
                MText(card,auction.bidder??"等待第一位买家",pad,112,inside,60,18,CampaignRetroTheme.Muted);
                MText(footer,$"{auction.bid:N0} G",4,5,footer.rect.width-176,32,24,CampaignRetroTheme.Price,true);
                MarketCardAction(footer,auction.round<auction.rounds?"下一轮举牌":"落槌成交",()=>app.Execute(s=>{if(s.State.auction.round<s.State.auction.rounds)s.AdvanceAuction();else s.CloseAuction();}));
            }
            var historyHeader=MarketFlowCard("ChampionHistoryHeading",history,142,out var historyTitle,out var historyFooter);
            MarketCardHeading(historyTitle,"拍卖记录");
            MText(historyHeader,"血统分红",pad,48,inside,30,21,CampaignRetroTheme.Accent,true);
            MText(historyFooter,"每周一场 · 联赛胜场奖励的 10%",4,6,historyFooter.rect.width-8,30,18,CampaignRetroTheme.Muted);
            foreach(var entry in State.hall.AsEnumerable().Reverse())
            {
                var card=MarketFlowCard("ChampionHistoryCard",history,220,out var header,out var footer);
                MarketCardHeading(header,entry.rights?"首配权":"冠军本体",$"第 {entry.week} 周");
                MText(card,CampaignLocalization.Name(entry)+$" · {entry.length:0.0} mm",pad,48,inside,50,21,CampaignRetroTheme.Ink,true);
                MText(card,entry.mutation,pad,106,inside,44,18,CampaignRetroTheme.Muted);
                MText(footer,$"成交 {entry.price:N0} G · 累计分红 {entry.royalties:N0} G",4,6,footer.rect.width-8,30,18,CampaignRetroTheme.Price,true);
            }
        }
    }
}
