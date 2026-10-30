using System;
using BugDuel.Campaign;
using TMPro;
using UnityEngine;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        void DrawMarketAuctions(RectTransform window)
        {
            MarketChoice(window,"公开竞价",14,104,240,40,!championAuction,()=>{championAuction=false;QueueRefresh();});
            MarketChoice(window,"冠军拍卖",264,104,240,40,championAuction,()=>{championAuction=true;QueueRefresh();});
            if(championAuction){DrawAuction(window);return;}
            float width=window.rect.width-6,height=window.rect.height-157,inside=width-4;
            var panel=MBox("PublicAuctions",window,3,154,width,height,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,2);
            var heading=MBox("AuctionHeading",panel,2,2,inside,36,CampaignRetroTheme.Window);
            MText(heading,"公开竞价",12,3,inside-230,30,21,CampaignRetroTheme.Accent,true);
            MText(heading,$"第 {State.week:00} 周",inside-210,3,194,30,18,CampaignRetroTheme.Muted).alignment=TextAlignmentOptions.MidlineRight;
            var content=MarketScroll(panel,2,38,inside,height-67,CampaignRetroTheme.Grid);
            float cardWidth=MarketCardGrid(content,Layout.market.AuctionCardHeight);
            foreach(var lot in State.marketAuctions)DrawMarketAuctionCard(content,lot,cardWidth);
            var blind=MarketCardSurface("BlindAuction",content,cardWidth,Layout.market.AuctionCardHeight,out var blindHeader,out var blindFooter);
            MarketCardHeading(blindHeader,"特别场",State.pupaAuction!=null?$"{State.pupaAuction.round}/3 轮":State.week%2==0?"本周开放":"下周开放");
            float pad=Layout.market.cardPadding;
            MText(blind,"神秘蛹盲拍",pad,42,cardWidth-pad*2,48,21,CampaignRetroTheme.Ink,true);
            MText(blind,"三轮竞价 · 双数周开放\n退出不扣款，本周不能再参加。",pad,104,cardWidth-pad*2,90,18,CampaignRetroTheme.Muted);
            MText(blindFooter,$"{(State.pupaAuction?.bid??150):N0} G",4,5,blindFooter.rect.width-176,32,24,CampaignRetroTheme.Price,true);
            MarketCardAction(blindFooter,State.pupaAuction==null?"进入盲拍":"继续竞拍",OpenMarketPupa,State.pupaAuction!=null||State.week%2==0&&State.lastPupaWeek!=State.week);
            notice=MStatus(panel,10,height-25,width-20,20,17);
            Canvas.ForceUpdateCanvases();
        }

        void DrawMarketAuctionCard(Transform parent,MarketAuctionLot lot,float width)
        {
            var layout=Layout.market;float pad=layout.cardPadding,top=30+pad,textX=pad*2+layout.thumbnailSize;
            var card=MarketCardSurface("AuctionLot",parent,width,layout.AuctionCardHeight,out var header,out var footer);
            MarketCardHeading(header,lot.kind=="specimen"?"装框标本":"活体专场",lot.closed?(lot.won?"已竞得":"已结束"):$"{lot.round}/{lot.rounds} 轮");
            MarketCardArtwork(card,lot.kind=="specimen"?CampaignMarketArtwork.Kind.Specimen:lot.Beetle!=null?BeetleMarketArtwork(lot.Beetle):CampaignMarketArtwork.Kind.Horn,top);
            MText(card,lot.name,textX,top-2,width-textX-pad,52,21,CampaignRetroTheme.Ink,true);
            MText(card,$"期望 {lot.expectedPrice:N0} G",textX,top+54,width-textX-pad,28,18,CampaignRetroTheme.Price,true);
            MText(card,Session.AuctionBidderName(lot),textX,top+86,width-textX-pad,26,18,CampaignRetroTheme.Muted);
            string status=lot.closed?lot.result:lot.playerBid>0?$"我的托管 {lot.playerBid:N0} G · 接受率 {Session.AuctionChance(lot.currentBid,lot.expectedPrice):P0}":$"最低举牌 {Session.AuctionMinimumBid(lot):N0} G";
            float statusY=top+Mathf.Max(112,layout.thumbnailSize)+12;
            MText(card,status,pad,statusY,width-pad*2,60,18,lot.won?CampaignRetroTheme.Accent:CampaignRetroTheme.Muted);
            MText(footer,$"{lot.currentBid:N0} G",4,5,footer.rect.width-176,32,24,CampaignRetroTheme.Price,true);
            MarketCardAction(footer,lot.closed?"查看结果":lot.playerBid>0?"继续竞拍":"查看 / 出价",()=>OpenMarketAuction(lot.id));
        }

        void OpenMarketAuction(string lotId)
        {
            var lot=Session.GetMarketAuction(lotId);var dialog=MarketDialog("公开竞拍 · "+lot.name,820,610);
            MText(dialog,lot.name,22,48,776,33,20,CampaignRetroTheme.Accent,true);
            var insect=lot.kind=="specimen"?lot.Specimen?.beetle:lot.Beetle;
            string detail=insect==null?(lot.kind=="specimen"?"装框标本":"活体甲虫"):$"{Session.Catalog.Species(insect.speciesId).name} · {(insect.female?"雌":"雄")} · "+(insect.stage<CampaignLifeStage.DormantAdult?CampaignSession.StageName(insect.stage):$"{insect.length:0.0} mm")+(lot.kind=="beetle"?$" · 健康 {insect.hearts:0.#} 心":" · 装框标本");
            MText(dialog,detail+$" · 第 {lot.round}/{lot.rounds} 轮",22,86,776,28,13,CampaignRetroTheme.Muted);
            var pricePanel=MBox("AuctionPrices",dialog,22,125,776,116,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,1);
            MText(pricePanel,"卖家期望价",16,12,250,23,12,CampaignRetroTheme.Muted);MText(pricePanel,$"{lot.expectedPrice:N0} G",16,42,250,33,24,CampaignRetroTheme.Price,true);
            MText(pricePanel,"当前最高出价",282,12,250,23,12,CampaignRetroTheme.Muted);MText(pricePanel,$"{lot.currentBid:N0} G",282,42,250,33,24,CampaignRetroTheme.Ink,true);MText(pricePanel,"最高出价者："+Session.AuctionBidderName(lot),16,83,744,22,12,CampaignRetroTheme.Muted);
            MText(pricePanel,"我的托管",560,12,200,23,12,CampaignRetroTheme.Muted);MText(pricePanel,$"{lot.playerBid:N0} G",560,42,200,33,22,CampaignRetroTheme.Accent,true);
            if(lot.closed)
            {
                MText(dialog,lot.won?Session.PlayerSignature+"获得了这件拍品":"本场拍卖已结束",22,272,776,37,23,CampaignRetroTheme.Accent,true);
                MText(dialog,lot.result,22,325,776,80,17);
                MText(dialog,lot.won?"拍品已寄至邮箱。":"托管金币已退回。",22,425,776,58,13,CampaignRetroTheme.Muted);
                if(lot.won)MButton(dialog,"打开邮箱领取",22,552,242,37,OpenMailbox,true,14);
                MButton(dialog,"返回大厅",624,552,174,37,CloseModal,true,14);return;
            }
            long minimum=Session.AuctionMinimumBid(lot);long available=State.coins+lot.playerBid;
            MText(dialog,"我的出价 (G)",22,265,157,32,14);
            var amount=MNumberInput(dialog,183,260,204,39,minimum.ToString());
            var probability=MText(dialog,"",411,263,387,34,17,CampaignRetroTheme.Price,true);
            MButton(dialog,"最低加价",183,309,125,30,()=>amount.text=minimum.ToString(),true,12);
            long threeQuarters=(long)Math.Ceiling(lot.expectedPrice*.75);
            MButton(dialog,"期望价 75%",320,309,133,30,()=>amount.text=threeQuarters.ToString(),threeQuarters>=minimum,12);
            MButton(dialog,"期望价 100%",465,309,146,30,()=>amount.text=lot.expectedPrice.ToString(),lot.expectedPrice>=minimum,12);
            MText(dialog,$"本轮最低出价 {minimum:N0} G · 我的可用额度（含已托管）{available:N0} G",22,354,776,26,12,CampaignRetroTheme.Muted);
            var payment=MText(dialog,"",22,385,776,28,13,CampaignRetroTheme.Accent);
            MText(dialog,"落槌时最高出价者参与成交判定。\n被超价或流拍，托管金币退回。",22,430,776,44,13,CampaignRetroTheme.Muted);
            var feedback=MText(dialog,"",22,480,776,35,12,CampaignRetroTheme.Price);
            void Submit(Action<CampaignSession> command)
            {
                if(!app.Execute(command,()=>OpenMarketAuction(lotId)))CampaignLocalization.Bind(feedback,app.Message);
                else CampaignLocalization.Bind(feedback,"正在保存竞拍记录…");
            }
            var bid=MButton(dialog,"确认举牌",22,552,208,37,()=>Submit(s=>s.BidMarketAuction(lotId,long.Parse(amount.text))),true,14);
            MButton(dialog,lot.round==lot.rounds-1?"最后一轮 / 落槌":"下一轮 / 买家回应",244,552,254,37,()=>Submit(s=>s.AdvanceMarketAuction(lotId)),true,14);
            MButton(dialog,"返回大厅",624,552,174,37,CloseModal,true,14);
            void RefreshBid()
            {
                bool parsed=long.TryParse(amount.text,out long value);bool valid=parsed&&value>=minimum&&value<=available;
                CampaignLocalization.Bind(probability,parsed&&value>0?$"卖家接受率 {Session.AuctionChance(value,lot.expectedPrice):P0}":"请输入出价");
                CampaignLocalization.Bind(payment,valid?$"确认后新增托管 {value-lot.playerBid:N0} G，余款 {State.coins-(value-lot.playerBid):N0} G。":!parsed||value<minimum?$"出价须至少 {minimum:N0} G。":"可用金币不足，请降低出价或出售收藏。");
                bid.interactable=valid;
                bid.GetComponentInChildren<TMP_Text>().color=valid?CampaignRetroTheme.Ink:CampaignRetroTheme.Muted;
            }
            amount.onValueChanged.AddListener(_=>RefreshBid());RefreshBid();
        }
    }
}
