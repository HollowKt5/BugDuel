using System.Linq;
using BugDuel.Campaign;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        void DrawMarket()
        {
            Title(L("市场 · 小生意，大眼光","Market / A good eye goes a long way"),$"本月热搜：{Session.Catalog.Species(State.trendSpecies).name}  收购价 +200%   ·   现货与报价每周刷新   ·   盲拍每逢双数周开放");
            var shop=Column(40,265,745,550);var cabinet=Column(805,265,755,550);
            var supplies=Card(shop,165);Label(supplies,"耗材采购",22,18,700,40,27,accent);
            Btn(supplies,$"树液蜜 120 G  ·  库存 {State.sap}",22,76,340,50,()=>app.Execute(s=>s.BuySupply(false)));
            Btn(supplies,$"紫外灯 200 G  ·  库存 {State.lamp}",380,76,335,50,()=>app.Execute(s=>s.BuySupply(true)));
            foreach(var stock in State.stock)
            {
                var b=stock;var card=Card(shop,145);Label(card,$"{b.name}  {(b.female?"♀":"♂")}  {b.length:0.0} mm",22,16,690,40,26);
                Label(card,$"健康5心 · {string.Join(" / ",b.genes.Select(g=>g.id+"★"))}",22,60,690,30,20,muted);
                Btn(card,$"购入  {CampaignEconomy.Money(Session.Asking(b)*1.1):N0} G",22,100,690,32,()=>app.Execute(s=>s.Buy(b.id)));
            }
            var pupa=Card(shop,240);Label(pupa,"周末 · 神秘蛹盲拍",22,18,690,40,28,accent);
            if(State.pupaAuction==null)
            {
                Label(pupa,"起拍150 G；共三轮加价。\n落槌前物种保密，退出不扣金币，每场仅可参加一次。",22,75,690,85,22);
                Btn(pupa,"进入本周拍卖",22,175,690,45,()=>app.Execute(s=>s.StartPupaAuction()),State.week%2==0&&State.lastPupaWeek!=State.week);
            }
            else
            {
                Label(pupa,$"当前 {State.pupaAuction.bid:N0} G   ·   第 {State.pupaAuction.round}/3 轮\n再举牌加价 50–149 G，第三轮成交。",22,70,690,85,23);
                Btn(pupa,"举牌",22,175,330,45,()=>app.Execute(s=>s.BidPupa()));Btn(pupa,"退出盲拍",370,175,340,45,()=>app.Execute(s=>s.LeavePupaAuction()));
            }
            var header=Card(cabinet,165);Label(header,$"挂牌展柜  {State.beetles.Count(b=>b.listed)}/{State.cabinet}",22,18,700,40,28,accent);
            Btn(header,"挂牌当前选择的甲虫",22,75,700,40,()=>app.Execute(s=>s.List(State.selectedId)),State.beetles.Any(b=>b.id==State.selectedId&&!b.listed));
            Btn(header,$"扩展至6格  {Session.Catalog.cabinetUpgrade} G",22,120,700,32,()=>app.Execute(s=>s.ExpandCabinet()),State.cabinet==3);
            foreach(var beetle in State.beetles.Where(b=>b.listed))
            {
                var b=beetle;var offers=State.offers.Where(o=>o.beetleId==b.id).ToArray();var card=Card(cabinet,110+offers.Length*105);
                Label(card,$"{b.name}   {b.length:0.0} mm  /  {b.value:N0} G",22,15,700,38,25);
                Btn(card,"撤回挂牌",22,58,700,35,()=>app.Execute(s=>s.Unlist(b.id)));
                for(int i=0;i<offers.Length;i++)
                {
                    var o=offers[i];float y=112+i*105;
                    Label(card,$"{o.buyer}  ·  {o.price:N0} G"+(o.femaleBonus?" + 母虫":"")+(o.recipe?" + 配方":"")+(o.permit?" + 许可":""),22,y,700,40,21,accent);
                    Btn(card,"接受报价",22,y+45,700,42,()=>Confirm($"出售 {b.name} 给{o.buyer}，收到 {o.price:N0} G？",()=>app.Execute(s=>s.AcceptOffer(o.id))));
                }
            }
            var notes=Card(cabinet,275);Label(notes,"来逛展柜的五种买家",22,18,700,40,27);
            Label(notes,"小学生：低价收购，增加口碑。\n俱乐部球探：冠军或高星基因，可能附送母虫。\n变异大富豪：稀有变异，出价2–5倍。\n育种老匠人：纯血或冠军同腹母虫，附赠配方。\n科学家：稀缺亚种，解锁禁猎区与突变图谱。",22,75,700,190,22,muted);
        }
    }
}
