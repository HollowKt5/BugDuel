using System.Linq;
using UnityEngine;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        bool withSap,withLamp;

        void DrawCatching()
        {
            Title("捕虫队","每队 2 只 · 出发时扣费 · 回程自动入住");
            var destinations=Column(40,265,825,550);var desk=Column(885,265,675,550);
            foreach(var rule in Session.Catalog.catches)
            {
                var c=rule;bool open=Session.Unlocked(c);var card=Card(destinations,205);
                Label(card,c.name,22,18,770,40,28,accent);
                Label(card,$"{c.min}–{c.max} mm  ·  {c.weeks} 周  ·  {c.cost:N0} G\n{string.Join("、",c.species.Select(id=>Session.Catalog.Species(id).name))}\n稀有 {c.rare:P0}  /  {(c.id=="amazon"?"3星":"2星")} {c.twoStar:P0}",22,65,770,90,21);
                Btn(card,open?"派遣捕虫队":$"需赢得 Lv.{c.unlock} 联赛{(c.id=="amazon"?" / 科学家许可":"")}",22,160,770,35,()=>app.Execute(s=>s.Dispatch(c.id,withSap,withLamp)),open);
            }
            var kit=Card(desk,211);Label(kit,"携带用品",24,20,610,40,30);
            Btn(kit,$"{(withSap?"✓":"○")} 发酵树液蜜 · 数量+2 · 库存 {State.sap}",24,80,610,50,()=>{withSap=!withSap;QueueRefresh();});
            Btn(kit,$"{(withLamp?"✓":"○")} 紫外灯 · 大型率+25% · 库存 {State.lamp}",24,145,610,50,()=>{withLamp=!withLamp;QueueRefresh();});
            foreach(var e in State.expeditions){var card=Card(desk,145);Label(card,Session.Catalog.Catch(e.habitat).name,22,18,620,40,26,accent);Label(card,$"第 {e.dueWeek} 周回程 · 剩余 {e.dueWeek-State.week} 周\n预留 {e.count} 格  {(e.large?"携带紫外灯":"轻装出发")}",22,70,620,65,22);}
            if(State.expeditions.Count==0){var card=Card(desk,90);Label(card,"暂无派遣",24,24,610,42,26,muted);}
        }
    }
}
