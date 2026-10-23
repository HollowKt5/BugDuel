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
            var dialog=MarketDialog("商品详细参数档案",460,335);
            Component<CampaignMarketArtwork>(Rect("DetailArtwork",dialog,18,53,52,52)).SetKind(item.artwork);
            MText(dialog,item.title,84,49,352,29,13,CampaignRetroTheme.Accent,true);
            string[] names={"所属类别：","种类品名：","尺寸规格：","参数属性：","核准价格："};string[] values={item.category,item.species,item.size,item.parameter,item.price};
            for(int i=0;i<5;i++)
            {
                MText(dialog,names[i],84,88+i*30,77,25,11,CampaignRetroTheme.Muted);MText(dialog,values[i],161,88+i*30,275,25,i==4?14:11,i==4?CampaignRetroTheme.Price:CampaignRetroTheme.Ink,i==4);
                if(i<4)MBox("Rule",dialog,84,114+i*30,352,1,MColor(0xd0ccbe,0x374151));
            }
            MText(dialog,item.enabled?"订单将寄送至邮箱，领取后加入收藏。":"金币不足，可先出售库存或参加联赛赚取金币。",18,247,420,23,11,CampaignRetroTheme.Muted);
            MBox("DialogRule",dialog,18,278,422,1,CampaignRetroTheme.Border);
            MButton(dialog,item.actionLabel,218,291,132,27,()=>{CloseModal();item.action?.Invoke();},item.enabled);MButton(dialog,"取消",360,291,80,27,CloseModal);
        }
        void OpenMarketListing()
        {
            var dialog=MarketDialog("发表转让帖 - 选择活体或标本",760,590);
            MText(dialog,$"挂牌展柜 {Session.ListedCount}/{State.cabinet} · 挂牌免费，成交收取 5% 手续费。",18,46,724,28,12,CampaignRetroTheme.Muted);
            MText(dialog,"报价锁定到本周末，可随时撤回。仓库活体需先入住温室才能挂牌。",18,75,724,25,11,CampaignRetroTheme.Muted);
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
            if(flow.y==0)MText(flow.content,"当前没有可选择的收藏。",10,12,650,30,12,CampaignRetroTheme.Muted);MButton(dialog,"返回",646,539,96,29,CloseModal);
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
                MText(dialog,"起拍 150 G，共三轮加价。\n落槌前物种保密，退出不扣金币，每场仅可参加一次。",24,59,412,95,13);
                MButton(dialog,"进入本周拍卖",174,212,150,29,()=>{CloseModal();app.Execute(s=>s.StartPupaAuction(),()=>{if(State.pupaAuction!=null)OpenMarketPupa();});},State.week%2==0&&State.lastPupaWeek!=State.week);
            }
            else
            {
                MText(dialog,$"当前 {State.pupaAuction.bid:N0} G  ·  第 {State.pupaAuction.round}/3 轮",24,59,412,31,15,CampaignRetroTheme.Price,true);MText(dialog,"再举牌加价 50–149 G，第三轮成交。\n退出不扣金币，本周将无法再次参加。",24,103,412,65,12);
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
            var dismiss=Rect("MenuDismiss",root,0,0,1600,900);var hit=Component<UI.Image>(dismiss);hit.color=Color.clear;hit.raycastTarget=marketThemeOpen;var click=Component<UI.Button>(dismiss);click.onClick.RemoveAllListeners();click.onClick.AddListener(()=>SetMarketThemeOpen(false));click.transition=UI.Selectable.Transition.None;click.interactable=marketThemeOpen;marketThemeDismiss=click;
            var dropdown=MBox("ThemeDropdown",root,17,65,268,166,MColor(0xffffff,0x252a30),MColor(0x716f64,0x4b5563),1);dropdown.GetComponent<UI.Image>().raycastTarget=true;
            MText(dropdown,"当前季节（随游戏日历）",12,5,244,18,10,CampaignRetroTheme.Muted);
            MText(dropdown,CampaignRetroTheme.SeasonName,12,29,244,24,13,CampaignRetroTheme.Ink,true);
            MBox("ModeRule",dropdown,1,66,266,1,MColor(0xd0ccbe,0x181b1f));MText(dropdown,"色彩模式 (明暗)：",12,72,244,18,10,CampaignRetroTheme.Muted);
            MButton(dropdown,(!CampaignRetroTheme.Dark?"✓  ":"    ")+"明亮浅色模式 (Light)",3,97,262,27,()=>SetMarketMode(false),true,12,true,MColor(0xffffff,0x252a30)).GetComponentInChildren<TMP_Text>().alignment=TextAlignmentOptions.MidlineLeft;
            MButton(dropdown,(CampaignRetroTheme.Dark?"✓  ":"    ")+"经典暗色模式 (Dark)",3,129,262,27,()=>SetMarketMode(true),true,12,true,MColor(0xffffff,0x252a30)).GetComponentInChildren<TMP_Text>().alignment=TextAlignmentOptions.MidlineLeft;
            marketThemeMotion=BeetlePanelMotion.Attach(dropdown.gameObject);
            marketThemeMotion.SetVisible(marketThemeOpen,hidden:QueueRefresh);
        }
        void OpenMarketHelp()
        {
            SetMarketThemeOpen(false);var dialog=MarketDialog("市场交易与竞拍指南",760,648);
            MText(dialog,"甲虫商店 / 收藏市场 / 公开拍卖",20,48,720,29,16,CampaignRetroTheme.Accent,true);
            MText(dialog,"1. 商店与邮箱\n活体、装框标本与用品均有明码标价。用品可按 1–99 件批量购买。所有订单通过邮箱领取：活体存入仓库，标本存入收藏库。\n\n2. 出售收藏与库存\n商店即时回收价：成虫估价的 55%（幼虫 35%），标本估价的 60%，用品零售价的 40%。显示的回收价就是实收金币。\n\n3. 交易 BBS\n健康成虫与标本共用挂牌展柜。挂牌免费，每周刷新买家报价；成交收取 5% 手续费。成交前可以撤回。标本挂牌时会从展示墙取下。\n\n4. 公开拍卖\n每场四轮，自定义举牌后点击「下一轮」查看其他买家回应。只有落槌时最高出价者参与成交判定。达到期望价时必定成交；低于期望价时，接受率为价格比例的平方。出价暂存托管，被超价或流拍自动退款。\n\n5. 每周补货与保存\n进入下一周会结算未结束拍卖，再更新货架和拍品。每次成功交易都会保存。Alt+T 查看主题，Alt+M 切换明暗，Alt+H 查看帮助。",20,91,720,473,13);
            MButton(dialog,"确定(O)",628,596,112,31,CloseModal,true,13);
        }
    }
}

