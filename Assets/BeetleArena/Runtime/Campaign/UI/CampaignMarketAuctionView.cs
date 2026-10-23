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
            var panel=MBox("PublicAuctions",window,11,100,1562,773,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,2);
            var heading=MBox("AuctionHeading",panel,2,2,1558,40,CampaignRetroTheme.Frame);
            MText(heading,"公开拍卖大厅 · 活体 / 装框标本",12,9,1000,25,14,Color.white,true);
            MText(heading,$"第 {State.week:00} 周拍品 · 每场 4 轮",1170,10,372,23,12,Color.white).alignment=TextAlignmentOptions.MidlineRight;
            MText(panel,"价高者优先：最高出价达到期望价，100% 成交；低于期望价时，越接近越容易成交。",16,53,1528,26,13,CampaignRetroTheme.Accent,true);
            MText(panel,"举牌金额暂存托管，被超价或流拍即退还。你决定何时推进轮次；进入下一周会结算未结束拍卖。",16,85,1528,24,12,CampaignRetroTheme.Muted);
            var content=MarketScroll(panel,2,120,1558,624,CampaignRetroTheme.Grid);
            var grid=Component<UnityEngine.UI.GridLayoutGroup>(content);grid.constraint=UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=2;grid.cellSize=new Vector2(751,264);grid.spacing=new Vector2(12,12);grid.padding=new RectOffset(12,12,12,12);grid.childAlignment=TextAnchor.UpperLeft;
            Component<UnityEngine.UI.ContentSizeFitter>(content).verticalFit=UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            foreach(var lot in State.marketAuctions)DrawMarketAuctionCard(content,lot);
            var blind=MBox("BlindAuction",content,0,0,751,264,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,1);
            MText(blind,"特别场 · 神秘蛹盲拍",18,18,710,30,17,CampaignRetroTheme.Accent,true);
            MText(blind,"经典三轮举牌，成交后揭晓物种。\n此场采用固定举牌规则，每逢双数周开放。\n中途退出不扣金币，本周不能重新参加。",18,65,710,91,13,CampaignRetroTheme.Muted);
            MText(blind,State.pupaAuction==null?$"起拍 150 G · {(State.week%2==0?"本周开放":"下周开放")}":$"当前 {State.pupaAuction.bid:N0} G · 第 {State.pupaAuction.round}/3 轮",18,165,710,29,15,CampaignRetroTheme.Price,true);
            MButton(blind,State.pupaAuction==null?"进入神秘蛹盲拍":"继续神秘蛹盲拍",18,216,715,30,OpenMarketPupa,State.pupaAuction!=null||State.week%2==0&&State.lastPupaWeek!=State.week,12);
            notice=MStatus(panel,10,748,1542,20);
            Canvas.ForceUpdateCanvases();
        }

        void DrawMarketAuctionCard(Transform parent,MarketAuctionLot lot)
        {
            var card=MBox("AuctionLot",parent,0,0,751,264,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,1);
            var header=MBox("LotTitle",card,1,1,749,32,MColor(0xece9d8,0x282e35));
            MText(header,lot.kind=="specimen"?"装框标本 / 收藏专场":"健康成虫 / 活体专场",12,7,480,22,12,CampaignRetroTheme.Accent,true);
            MText(header,lot.closed?(lot.won?"已竞得":"已结束"):$"第 {lot.round}/{lot.rounds} 轮",565,7,169,22,12,CampaignRetroTheme.Muted).alignment=TextAlignmentOptions.MidlineRight;
            var art=MBox("LotArtwork",card,16,48,116,116,CampaignRetroTheme.Grid,CampaignRetroTheme.Border,1);
            Component<CampaignMarketArtwork>(Rect("Artwork",art,2,2,112,112)).SetKind(lot.kind=="specimen"?CampaignMarketArtwork.Kind.Specimen:lot.Beetle!=null?BeetleMarketArtwork(lot.Beetle):CampaignMarketArtwork.Kind.Horn);
            MText(card,lot.name,150,48,581,29,16,CampaignRetroTheme.Ink,true);
            MText(card,$"期望成交价  {lot.expectedPrice:N0} G",150,87,581,27,14,CampaignRetroTheme.Price,true);
            MText(card,$"最高出价  {lot.currentBid:N0} G  /  {Session.AuctionBidderName(lot)}",150,122,581,26,13);
            string status=lot.closed?lot.result:lot.playerBid>0?$"{Session.PlayerSignature}正领先 · 托管 {lot.playerBid:N0} G · 若保持最高，卖家接受率 {Session.AuctionChance(lot.currentBid,lot.expectedPrice):P0}":$"最低举牌 {Session.AuctionMinimumBid(lot):N0} G · 出价步幅至少为期望价的 5%";
            MText(card,status,16,174,717,35,12,lot.won?CampaignRetroTheme.Accent:CampaignRetroTheme.Muted);
            MButton(card,lot.closed?"查看成交结果":lot.playerBid>0?"继续竞拍 / 推进轮次":"查看拍品 / 自由出价",16,216,717,30,()=>OpenMarketAuction(lot.id),true,12);
        }

        void OpenMarketAuction(string lotId)
        {
            var lot=Session.GetMarketAuction(lotId);var dialog=MarketDialog("公开竞拍 · "+lot.name,820,654);
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
                MText(dialog,lot.won?"拍品已发送至邮箱，领取后可安排入住温室或布置标本室。":"未成交的托管金币已经全部退回。下周会补充新的拍品。",22,425,776,58,13,CampaignRetroTheme.Muted);
                if(lot.won)MButton(dialog,"打开邮箱领取",22,588,242,37,OpenMailbox,true,14);
                MButton(dialog,"返回大厅",624,588,174,37,CloseModal,true,14);return;
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
            MText(dialog,"落槌时仅最高出价者参与成交判定。达到期望价时必定成交；\n低于期望价：出到 50% → 25% 接受率，75% → 56%，90% → 81%。\n最后一轮仍可能被其他买家超价；被超价或流拍，托管金币自动退还。",22,430,776,87,13,CampaignRetroTheme.Muted);
            var feedback=MText(dialog,"",22,526,776,35,12,CampaignRetroTheme.Price);
            MText(dialog,"竞拍人："+Session.PlayerSignature,22,562,776,22,12,CampaignRetroTheme.Muted);
            void Submit(Action<CampaignSession> command)
            {
                if(!app.Execute(command,()=>OpenMarketAuction(lotId)))CampaignLocalization.Bind(feedback,app.Message);
                else CampaignLocalization.Bind(feedback,"正在保存竞拍记录…");
            }
            var bid=MButton(dialog,"确认举牌",22,588,208,37,()=>Submit(s=>s.BidMarketAuction(lotId,long.Parse(amount.text))),true,14);
            MButton(dialog,lot.round==lot.rounds-1?"最后一轮 / 落槌":"下一轮 / 买家回应",244,588,254,37,()=>Submit(s=>s.AdvanceMarketAuction(lotId)),true,14);
            MButton(dialog,"返回大厅",624,588,174,37,CloseModal,true,14);
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
