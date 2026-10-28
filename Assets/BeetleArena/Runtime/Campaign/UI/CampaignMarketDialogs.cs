using BugDuel.Campaign;
using System.Linq;
using TMPro;
using UnityEngine;
using UI=UnityEngine.UI;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        void OpenMarketItem(MarketItem item)
        {
            if(!string.IsNullOrEmpty(item.itemId)){OpenMarketSupply(item.itemId,false);return;}
            var dialog=MarketDialog("商品详情",460,335);
            Component<CampaignMarketArtwork>(Rect("DetailArtwork",dialog,18,53,52,52)).SetKind(item.artwork);
            MText(dialog,item.title,84,49,352,29,13,CampaignRetroTheme.Accent,true);
            string[] names={"类别：","物种：","尺寸：","属性：","价格："};string[] values={item.category,item.species,item.size,item.parameter,item.price};
            for(int i=0;i<5;i++)
            {
                MText(dialog,names[i],84,88+i*30,77,25,11,CampaignRetroTheme.Muted);MText(dialog,values[i],161,88+i*30,275,25,i==4?14:11,i==4?CampaignRetroTheme.Price:CampaignRetroTheme.Ink,i==4);
                if(i<4)MBox("Rule",dialog,84,114+i*30,352,1,CampaignRetroTheme.Rule);
            }
            MText(dialog,item.enabled?"购买后在邮箱领取。":"金币不足",18,247,420,23,11,CampaignRetroTheme.Muted);
            MBox("DialogRule",dialog,18,278,422,1,CampaignRetroTheme.Border);
            MButton(dialog,item.actionLabel,218,291,132,27,()=>{CloseModal();item.action?.Invoke();},item.enabled);MButton(dialog,"取消",360,291,80,27,CloseModal);
        }
        void OpenMarketListing()
        {
            var dialog=MarketDialog("发表转让帖 - 选择活体或标本",760,590);
            MText(dialog,$"挂牌展柜 {Session.ListedCount}/{State.cabinet} · 挂牌免费，成交收取 5% 手续费。",18,46,724,28,12,CampaignRetroTheme.Muted);
            MText(dialog,"报价本周有效 · 可撤回 · 仓库活体需先入住",18,75,724,25,11,CampaignRetroTheme.Muted);
            var flow=Column(18,110,724,404,dialog);
            foreach(var beetle in State.beetles.Where(b=>!b.listed))
            {
                var b=beetle;bool eligible=!b.dead&&b.Adult(State.week)&&b.hearts>=System.Math.Min(3,b.maxHearts)&&b.nutrition>=40&&!b.overturned&&State.battle?.beetleId!=b.id&&State.auction?.beetleId!=b.id;
                var row=MBox("ListingChoice",flow.content,0,flow.y,706,62,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,1);flow.y+=68;flow.content.sizeDelta=new Vector2(724,flow.y);
                MText(row,$"[活体] {CampaignLocalization.Name(b)}   {b.length:0.0} mm  /  {b.hearts:0.#} 心",10,7,570,23,12);MText(row,eligible?$"健康成虫 · 估价 {Session.Asking(b):N0} G":"需要健康成虫：饱食 ≥ 40，健康达标，且未参与比赛或拍卖。",10,33,570,20,11,CampaignRetroTheme.Muted);
                MButton(row,"挂牌",598,16,96,29,()=>{CloseModal();app.Execute(s=>s.List(b.id));},eligible&&Session.ListedCount<State.cabinet);
            }
            foreach(var specimen in State.specimens.Where(p=>!p.listed))
            {
                var p=specimen;var row=MBox("SpecimenListingChoice",flow.content,0,flow.y,706,62,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,1);flow.y+=68;flow.content.sizeDelta=new Vector2(724,flow.y);
                MText(row,$"[标本] {CampaignDisplayText.SpecimenName(p)}   "+(p.beetle.stage<BugDuel.Campaign.CampaignLifeStage.DormantAdult?BugDuel.Campaign.CampaignSession.StageName(p.beetle.stage):$"{p.beetle.length:0.0} mm"),10,7,570,23,12);MText(row,$"{CampaignDisplayText.SpecimenSpecies(p)} · 估价 {Session.SpecimenAsking(p):N0} G",10,33,570,20,11,CampaignRetroTheme.Muted);
                MButton(row,"挂牌",598,16,96,29,()=>{CloseModal();app.Execute(s=>s.ListSpecimen(p.id));},Session.ListedCount<State.cabinet);
            }
            if(flow.y==0)MText(flow.content,"暂无可挂牌收藏",10,12,650,30,12,CampaignRetroTheme.Muted);MButton(dialog,"返回",646,539,96,29,CloseModal);
        }
        void OpenMarketOffers(string beetleId)
        {
            var b=State.beetles.FirstOrDefault(x=>x.id==beetleId);if(b==null)return;
            var dialog=MarketDialog("转让帖 - 本周收购报价",700,535);MText(dialog,$"{CampaignLocalization.Name(b)}  {b.length:0.0}mm · 市场估价 {Session.Asking(b):N0} G",18,45,664,28,13,CampaignRetroTheme.Accent,true);
            var flow=Column(18,84,664,374,dialog);
            foreach(var offer in State.offers.Where(o=>o.beetleId==beetleId))
            {
                var o=offer;var row=MBox("Offer",flow.content,0,flow.y,646,100,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,1);flow.y+=106;flow.content.sizeDelta=new Vector2(664,flow.y);
                MText(row,o.buyer,10,9,320,23,12,CampaignRetroTheme.Ink,true);MText(row,$"报价 {o.price:N0} G",334,8,190,25,15,CampaignRetroTheme.Price,true);
                MText(row,$"手续费 {o.price-Session.SaleProceeds(o.price):N0} G · 实收 {Session.SaleProceeds(o.price):N0} G",10,39,510,22,12,CampaignRetroTheme.Price);
                MText(row,"本周有效"+(o.femaleBonus?" · 附赠母虫":"")+(o.recipe?" · 解锁配方":"")+(o.permit?" · 附带许可":""),10,69,510,22,11,CampaignRetroTheme.Muted);
                MButton(row,"接受报价",538,35,96,29,()=>Confirm($"出售 {CampaignLocalization.Name(b)} 给{o.buyer}？\n报价 {o.price:N0} G，手续费 {o.price-Session.SaleProceeds(o.price):N0} G，实收 {Session.SaleProceeds(o.price):N0} G。",()=>app.Execute(s=>s.AcceptOffer(o.id))));
            }
            MButton(dialog,"撤回挂牌",18,484,112,29,()=>{CloseModal();app.Execute(s=>s.Unlist(beetleId));});MButton(dialog,"返回",586,484,96,29,CloseModal);
        }
        void OpenMarketPupa()
        {
            var dialog=MarketDialog("周末 · 神秘蛹盲拍",460,280);
            if(State.pupaAuction==null)
            {
                MText(dialog,"起拍 150 G · 共三轮\n退出不扣款，本周不能再参加。",24,59,412,95,13);
                MButton(dialog,"进入本周拍卖",174,212,150,29,()=>{CloseModal();app.Execute(s=>s.StartPupaAuction(),()=>{if(State.pupaAuction!=null)OpenMarketPupa();});},State.week%2==0&&State.lastPupaWeek!=State.week);
            }
            else
            {
                MText(dialog,$"当前 {State.pupaAuction.bid:N0} G  ·  第 {State.pupaAuction.round}/3 轮",24,59,412,31,15,CampaignRetroTheme.Price,true);MText(dialog,"每轮加价 50–149 G · 第三轮成交\n退出不扣款，本周不能再参加。",24,103,412,65,12);
                MButton(dialog,"举牌",24,212,125,29,()=>{CloseModal();app.Execute(s=>s.BidPupa(),()=>{if(State.pupaAuction!=null)OpenMarketPupa();});});MButton(dialog,"退出盲拍",159,212,125,29,()=>{CloseModal();app.Execute(s=>s.LeavePupaAuction());});
            }
            MText(dialog,"竞拍人："+Session.PlayerSignature,24,174,412,27,12,CampaignRetroTheme.Muted);
            MButton(dialog,"关闭窗口",334,212,102,29,CloseModal);
        }
        void SetMarketThemeOpen(bool open)
        {
            marketThemeOpen=open;
            if(marketThemeDismiss)
            {
                marketThemeDismiss.interactable=open;
                marketThemeDismiss.GetComponent<UI.Image>().raycastTarget=open;
            }
            if(marketThemeMotion)marketThemeMotion.SetVisible(open,hidden:QueueRefresh);
            QueueRefresh();
        }
        void SetMarketMode(bool dark){CampaignRetroTheme.SetMode(dark);SetMarketThemeOpen(false);}
        void DrawMarketThemeMenu()
        {
            var dismiss=Rect("MenuDismiss",root,0,0,root.rect.width,root.rect.height);var hit=Component<UI.Image>(dismiss);hit.color=Color.clear;hit.raycastTarget=marketThemeOpen;var click=Component<UI.Button>(dismiss);click.onClick.RemoveAllListeners();click.onClick.AddListener(()=>SetMarketThemeOpen(false));click.transition=UI.Selectable.Transition.None;click.interactable=marketThemeOpen;marketThemeDismiss=click;
            var dropdown=MBox("ThemeDropdown",root,MarketMenuStart+3,64,300,238,CampaignRetroTheme.SurfaceRaised,CampaignRetroTheme.Border,1);dropdown.GetComponent<UI.Image>().raycastTarget=true;
            MText(dropdown,"当前季节（随游戏日历）",12,8,276,27,17,CampaignRetroTheme.Muted);
            MText(dropdown,CampaignRetroTheme.SeasonName,12,42,276,32,21,CampaignRetroTheme.Ink,true);
            MBox("ModeRule",dropdown,1,88,298,1,CampaignRetroTheme.Rule);MText(dropdown,"色彩模式 (明暗)：",12,99,276,27,18,CampaignRetroTheme.Muted);
            MButton(dropdown,(!CampaignRetroTheme.Dark?"✓  ":"    ")+"明亮浅色模式 (Light)",3,134,294,42,()=>SetMarketMode(false),true,18,true,CampaignRetroTheme.SurfaceRaised).GetComponentInChildren<TMP_Text>().alignment=TextAlignmentOptions.MidlineLeft;
            MButton(dropdown,(CampaignRetroTheme.Dark?"✓  ":"    ")+"经典暗色模式 (Dark)",3,184,294,42,()=>SetMarketMode(true),true,18,true,CampaignRetroTheme.SurfaceRaised).GetComponentInChildren<TMP_Text>().alignment=TextAlignmentOptions.MidlineLeft;
            marketThemeMotion=BeetlePanelMotion.Attach(dropdown.gameObject);
            marketThemeMotion.SetVisible(marketThemeOpen,hidden:QueueRefresh);
        }
        void OpenMarketHelp()
        {
            SetMarketThemeOpen(false);var dialog=MarketDialog("市场交易与竞拍指南",760,448);
            MText(dialog,"购买：商品寄至邮箱，领取后入库。\n\n挂牌：免费发布，成交收取 5% 手续费，成交前可撤回。\n\n回收：成虫估价的 55%，幼虫 35%，标本 60%，用品零售价的 40%。\n\n拍卖：每场 4 轮。最高出价达到期望价时必定成交；低于期望价时，成交率为出价比例的平方。被超价或流拍退回托管。\n\n每周更新商品与报价，并结算未结束拍卖。\nAlt+T 主题 · Alt+M 明暗 · Alt+H 帮助",20,54,720,320,15);
            MButton(dialog,"确定(O)",628,396,112,31,CloseModal,true,13);
        }
    }
}

