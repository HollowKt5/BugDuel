using System;
using System.Linq;
using UnityEngine;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        void DrawLeague()
        {
            Title("五级联赛 · 从树桩走向王座",$"体长决定参赛级别。默认 {Session.Catalog.seasonWins} 连胜夺冠，战败重置赛季进度；已有战斗系统负责胜负。");
            var tiers=Column(40,265,760,550);var roster=Column(820,265,740,550);
            foreach(var t in Session.Catalog.leagues)
            {
                var c=Card(tiers,150);Label(c,$"Lv.{t.tier}  {t.name}",22,16,710,38,28,accent);
                Label(c,$"{t.minimum:0}–{(t.tier==5?"∞":t.maximum.ToString("0"))} mm（上限不含）\n报名 {t.fee:N0} G  /  胜场 {t.win:N0} G  /  冠军 {t.champion:N0} G",22,69,710,70,22);
            }
            if(State.battle!=null)
            {
                var pending=Card(roster,210);Label(pending,"未完成赛事 · 报名费已支付",22,22,690,45,28,accent);
                Label(pending,$"{CampaignLocalization.Name(Session.Beetle(State.battle.beetleId))} 对阵 林间挑战者\n从头重赛，同一对手与种子，不再扣报名费。",22,80,690,75,23);
                Btn(pending,"重返赛场",22,160,330,40,()=>app.Go(CampaignPage.Arena));Btn(pending,"弃权",370,160,330,40,()=>Confirm("弃权按落败结算，报名费不退还。",()=>app.Execute(s=>s.Forfeit())));
            }
            foreach(var beetle in State.beetles.Where(b=>b.Adult(State.week)))
            {
                var b=beetle;var tier=Session.Catalog.Tier(b.length);var c=Card(roster,190);
                Label(c,$"{CampaignLocalization.Name(b)}  ·  {b.length:0.0} mm  ·  Lv.{tier.tier}",22,15,690,40,27,accent);
                Label(c,$"健康 {b.hearts:0.0}/{b.maxHearts}  ·  饱食 {b.nutrition:0}%\n赛季 {b.seasonWins}/{Session.Catalog.seasonWins}  ·  身价 {b.value:N0} G",22,67,690,70,22);
                Btn(c,$"报名  {tier.fee:N0} G",22,140,690,38,()=>app.Execute(s=>s.EnterLeague(b.id),()=>app.Go(CampaignPage.Arena)),State.battle==null&&State.auction==null&&State.pupaAuction==null&&!b.listed&&!b.overturned);
            }
        }
        void DrawAuction(Transform parent=null)
        {
            if(!parent)Title("冠军拍卖","");
            var champions=Column(16,parent?164:265,754,parent?710:550,parent,name:"Champions");var stage=Column(790,parent?164:265,790,parent?710:550,parent,name:"ChampionHistory");
            if(State.auction==null)
            {
                bool canStartAuction=State.battle==null&&State.pupaAuction==null;
                foreach(var beetle in State.beetles.Where(b=>b.championships>0&&!b.dead))
                {
                    var b=beetle;var c=Card(champions,220);Label(c,$"{CampaignLocalization.Name(b)}   {b.length:0.0} mm",22,18,680,40,29,accent);
                    Label(c,$"冠军身价 {b.value:N0} G\n{b.mutation}  ·  首配权 {(b.rightsSold?"已出售":"可出售")}",22,75,680,75,23);
                    Btn(c,"拍卖冠军本体",22,160,325,42,()=>Confirm("本体成交后会离开温室，并获得持续血统分红。开始拍卖？",()=>app.Execute(s=>s.BeginAuction(b.id,false))),canStartAuction);
                    Btn(c,"只拍卖一次首配权",365,160,335,42,()=>app.Execute(s=>s.BeginAuction(b.id,true)),canStartAuction&&!b.rightsSold);
                }
                if(!State.beetles.Any(b=>b.championships>0&&!b.dead)){var c=Card(champions,225);Label(c,"红毯还在等你。\n先带一只甲虫赢下联赛总冠军吧！",24,35,670,100,29);Btn(c,"去联赛",24,160,670,43,()=>app.Go(CampaignPage.League));}
            }
            else
            {
                var a=State.auction;var b=Session.Beetle(a.beetleId);var c=Card(champions,405);
                Label(c,(a.rights?"冠军首配权":"冠军本体")+"  /  "+CampaignLocalization.Name(b),25,25,670,70,31,accent);
                Label(c,$"第 {a.round} / {a.rounds} 轮\n\n{a.bidder??"等待第一位买家"}\n当前举牌  {a.bid:N0} G",25,110,670,195,32);
                Btn(c,a.round<a.rounds?"下一轮举牌":"落槌成交",25,335,670,50,()=>app.Execute(s=>{if(s.State.auction.round<s.State.auction.rounds)s.AdvanceAuction();else s.CloseAuction();}));
            }
            var header=Card(stage,120);Label(header,"历代标王名人堂",24,20,710,40,30,accent);Label(header,"每份血统每周参与1次模拟NPC赛事，分红为该级胜场奖10%。",24,73,710,32,20,muted);
            foreach(var h in State.hall.AsEnumerable().Reverse())
            {
                var c=Card(stage,155);Label(c,$"{CampaignLocalization.Name(h)}  ·  {h.length:0.0} mm",22,18,710,38,27);
                Label(c,$"第 {h.week} 周 · {(h.rights?"首配权":"冠军本体")} · {h.price:N0} G\n累计血统分红 {h.royalties:N0} G  /  {h.mutation}",22,75,710,70,23,muted);
            }
        }
        void DrawArena()
        {
            if(!leagueBridge) return;
            var left=Rect("Player stamina",root,30,24,460,48);var right=Rect("Opponent stamina",root,1110,24,460,48);
            leagueBridge.playerLabel=Label(left,"",0,0,460,28,20,Color.white);leagueBridge.opponentLabel=Label(right,"",0,0,460,28,20,Color.white);
            leagueBridge.playerBar=Bar(left,0,36,460,new Color(.35f,.82f,.48f));leagueBridge.opponentBar=Bar(right,0,36,460,new Color(.35f,.82f,.48f));
            Btn(root,"倍速 "+leagueBridge.Speed+"×",1260,815,145,48,()=>{leagueBridge.CycleSpeed();QueueRefresh();});Btn(root,"镜头",1420,815,145,48,leagueBridge.CycleCamera);
            if(leagueBridge.IsFinished)
            {
                var card=ArenaResultPanel();Label(card,leagueBridge.ResultTitle,30,24,640,55,38,accent);
                Label(card,app.Message,30,95,640,135,24);
                Btn(card,"返回温室",30,250,200,55,()=>app.Go(CampaignPage.Greenhouse));Btn(card,"继续联赛",250,250,200,55,()=>app.Go(CampaignPage.League));Btn(card,"冠军拍卖",470,250,200,55,()=>app.Go(CampaignPage.Auction));
            }
            else if(leagueBridge.SettlementPending)
            {
                var card=ArenaResultPanel();
                Label(card,app.Busy?"正在保存比赛结果…":"比赛结果尚未保存",30,24,640,55,34,accent);
                Label(card,app.Message+"\n结果保存成功后才会发放奖励。系统会自动重试，也可手动重试。",30,95,640,135,23);
                Btn(card,"重试保存",30,250,640,55,leagueBridge.RetrySettlement,!app.Busy);
            }
        }
        RectTransform ArenaResultPanel()
        {
            var panel=Panel(root,450,290,700,340,name:"ArenaResult");
            panel.pivot=new Vector2(.5f,.5f);panel.anchoredPosition=new Vector2(800,-460);
            BeetlePanelMotion.Attach(panel.gameObject).SetVisible(true);
            return panel;
        }
        UnityEngine.UI.Image Bar(RectTransform parent,float x,float y,float width,Color color)
        {
            var rt=Rect("Meter",parent,x,y,width,5);var image=Component<UnityEngine.UI.Image>(rt);image.color=color;image.sprite=rounded;image.type=UnityEngine.UI.Image.Type.Filled;image.fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal;image.raycastTarget=false;return image;
        }
    }
}
