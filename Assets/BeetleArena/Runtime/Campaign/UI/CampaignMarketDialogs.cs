using System.Linq;
using BugDuel.Campaign;
using TMPro;
using UnityEngine;
using UI=UnityEngine.UI;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        void OpenMarketItem(MarketItem item)
        {
            var dialog=MarketDialog("商品详细参数档案 - Windows 信息",460,335);
            Component<CampaignMarketArtwork>(Rect("DetailArtwork",dialog,18,53,52,52)).SetKind(item.artwork);
            MText(dialog,item.title,84,49,352,29,13,CampaignRetroTheme.Accent,true);
            string[] names={"所属类别：","种类品名：","尺寸规格：","参数属性：","核准价格："};string[] values={item.category,item.species,item.size,item.parameter,item.price};
            for(int i=0;i<5;i++)
            {
                MText(dialog,names[i],84,88+i*30,77,25,11,CampaignRetroTheme.Muted);MText(dialog,values[i],161,88+i*30,275,25,i==4?14:11,i==4?CampaignRetroTheme.Price:CampaignRetroTheme.Ink,i==4);
                if(i<4)MBox("Rule",dialog,84,114+i*30,352,1,MColor(0xd0ccbe,0x374151));
            }
            if(item.actionLabel!="前往温室配置")MText(dialog,"订单将寄送至邮箱，领取后加入仓库。",18,247,420,23,11,CampaignRetroTheme.Muted);
            MBox("DialogFooter",dialog,3,278,454,54,CampaignRetroTheme.Window,MColor(0xd0ccbe,0x181b1f),1);
            MButton(dialog,item.actionLabel,218,291,132,27,()=>{CloseModal();item.action?.Invoke();},item.enabled);MButton(dialog,"取消",360,291,80,27,CloseModal);
        }
        void OpenMarketListing()
        {
            var dialog=MarketDialog("发表转让帖 - 选择挂牌甲虫",580,495);
            MText(dialog,$"挂牌展柜 {State.beetles.Count(b=>b.listed)}/{State.cabinet} · 报价锁定到本周末，成交前可以撤回。",18,46,544,32,12,CampaignRetroTheme.Muted);
            var flow=Column(18,88,544,332,dialog);
            foreach(var beetle in State.beetles.Where(b=>!b.listed))
            {
                var b=beetle;bool eligible=b.Adult(State.week)&&b.hearts>=System.Math.Min(3,b.maxHearts)&&b.nutrition>=40&&!b.overturned&&State.battle?.beetleId!=b.id&&State.auction?.beetleId!=b.id;
                var row=MBox("ListingChoice",flow.content,0,flow.y,526,57,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,1);flow.y+=63;flow.content.sizeDelta=new Vector2(544,flow.y);
                MText(row,$"{b.name}   {b.length:0.0} mm  /  {b.hearts:0.#} 心",10,7,386,23,12);MText(row,eligible?"健康成虫 · 可挂牌":"当前状态不可挂牌",10,31,386,18,11,CampaignRetroTheme.Muted);
                MButton(row,"挂牌",418,14,96,27,()=>{CloseModal();app.Execute(s=>s.List(b.id));},eligible&&State.beetles.Count(x=>x.listed)<State.cabinet);
            }
            if(flow.y==0)MText(flow.content,"当前没有可选择的甲虫。",10,12,500,30,12,CampaignRetroTheme.Muted);MButton(dialog,"返回",466,445,96,29,CloseModal);
        }
        void OpenMarketOffers(string beetleId)
        {
            var b=State.beetles.FirstOrDefault(x=>x.id==beetleId);if(b==null)return;
            var dialog=MarketDialog("转让帖 - 本周收购报价",580,505);MText(dialog,$"{b.name}  {b.length:0.0}mm · 参考身价 {b.value:N0} G",18,45,544,28,13,CampaignRetroTheme.Accent,true);
            var flow=Column(18,84,544,336,dialog);
            foreach(var offer in State.offers.Where(o=>o.beetleId==beetleId))
            {
                var o=offer;var row=MBox("Offer",flow.content,0,flow.y,526,82,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,1);flow.y+=88;flow.content.sizeDelta=new Vector2(544,flow.y);
                MText(row,o.buyer,10,9,280,23,12,CampaignRetroTheme.Ink,true);MText(row,$"{o.price:N0} G",299,8,118,25,15,CampaignRetroTheme.Price,true);
                MText(row,"本周有效"+(o.femaleBonus?" · 附赠母虫":"")+(o.recipe?" · 解锁配方":"")+(o.permit?" · 附带许可":""),10,43,390,26,11,CampaignRetroTheme.Muted);
                MButton(row,"接受报价",418,28,96,28,()=>Confirm($"出售 {b.name} 给{o.buyer}，收到 {o.price:N0} G？",()=>app.Execute(s=>s.AcceptOffer(o.id))));
            }
            MButton(dialog,"撤回挂牌",18,450,112,29,()=>{CloseModal();app.Execute(s=>s.Unlist(beetleId));});MButton(dialog,"返回",466,450,96,29,CloseModal);
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
            MButton(dialog,"关闭窗口",334,212,102,29,CloseModal);
        }
        void SetMarketAppearance(int season,bool dark){CampaignRetroTheme.SetAppearance(season,dark);marketThemeOpen=false;QueueRefresh();}
        void DrawMarketThemeMenu()
        {
            var dismiss=Rect("MenuDismiss",root,0,0,1600,900);var hit=Component<UI.Image>(dismiss);hit.color=Color.clear;hit.raycastTarget=true;var click=Component<UI.Button>(dismiss);click.onClick.RemoveAllListeners();click.onClick.AddListener(()=>{marketThemeOpen=false;QueueRefresh();});click.transition=UI.Selectable.Transition.None;
            var dropdown=MBox("ThemeDropdown",root,17,65,268,238,MColor(0xffffff,0x252a30),MColor(0x716f64,0x4b5563),1);dropdown.GetComponent<UI.Image>().raycastTarget=true;
            MText(dropdown,"请选择整体背景四季主题：",12,5,244,18,10,CampaignRetroTheme.Muted);
            string[] names={"春季主题 · 生机绿意 (绿色背景)","夏季主题 · 暖阳炽金 (黄色背景)","秋季主题 · 落叶枫红 (落叶背景)","冬季主题 · 纯白霜雪 (白色背景)"};
            for(int i=0;i<4;i++){int season=i;MButton(dropdown,(CampaignRetroTheme.Season==i?"✓  ":"    ")+names[i],3,26+i*27,262,26,()=>SetMarketAppearance(season,CampaignRetroTheme.Dark),true,12,true,MColor(0xffffff,0x252a30)).GetComponentInChildren<TMP_Text>().alignment=TextAlignmentOptions.MidlineLeft;}
            MBox("ModeRule",dropdown,1,138,266,1,MColor(0xd0ccbe,0x181b1f));MText(dropdown,"色彩模式 (明暗)：",12,144,244,18,10,CampaignRetroTheme.Muted);
            MButton(dropdown,(!CampaignRetroTheme.Dark?"✓  ":"    ")+"明亮浅色模式 (Light)",3,169,262,27,()=>SetMarketAppearance(CampaignRetroTheme.Season,false),true,12,true,MColor(0xffffff,0x252a30)).GetComponentInChildren<TMP_Text>().alignment=TextAlignmentOptions.MidlineLeft;
            MButton(dropdown,(CampaignRetroTheme.Dark?"✓  ":"    ")+"经典暗色模式 (Dark)",3,201,262,27,()=>SetMarketAppearance(CampaignRetroTheme.Season,true),true,12,true,MColor(0xffffff,0x252a30)).GetComponentInChildren<TMP_Text>().alignment=TextAlignmentOptions.MidlineLeft;
        }
        void OpenMarketHelp()
        {
            marketThemeOpen=false;QueueRefresh();var dialog=MarketDialog("系统使用说明与四季养殖指南 - Windows 帮助",580,492);
            MText(dialog,"甲虫商店 (BEETLE STORE 2003) 系统操作说明",18,48,544,26,13,CampaignRetroTheme.Accent,true);
            MText(dialog,"1. 甲虫商店\n左侧按单类浏览，右侧查看现货和价格。所有付费商品购买后会寄送至邮箱，领取后加入仓库。\n\n2. 邮箱与仓库\n温室和市场均可打开邮箱。食物领取后再投喂；活体从仓库安排入住温室。树液蜜和紫外灯领取后可用于派遣。展柜扩展包领取后在仓库安装。\n\n3. 交易 BBS\n自己的健康成虫可以挂牌出售。双数周开放神秘蛹盲拍，成交后的蛹也通过邮箱领取。生态箱和景观仍是免费的温室配置入口。\n\n4. 四季主题\nAlt+T 切换主题菜单，Alt+M 切换明暗，Alt+H 打开帮助。",18,88,544,306,12);
            MText(dialog,"翻倒请及时扶正；成虫投喂红薯或水果，幼虫选择饲料瓶。",18,402,544,29,11,CampaignRetroTheme.Muted);MButton(dialog,"确定(O)",466,444,96,29,CloseModal);
        }
        RectTransform MarketDialog(string title,float width,float height)
        {
            var outer=OpenModal(width,height);var surface=MBox("ClassicDialog",outer,0,0,width,height,CampaignRetroTheme.Window,CampaignRetroTheme.Frame,3);MTitle(surface,title,width-6);MButton(surface,"×",width-30,7,20,20,CloseModal,true,11,false,CampaignRetroTheme.Hex(0xd73720),Color.white);return surface;
        }
    }
}

