using System;
using System.Linq;
using BugDuel.Campaign;
using UnityEngine;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        void DrawGreenhouse()
        {
            Title(L("温室 · 今天也要好好吃饭","Greenhouse / A little care goes a long way"),L("红薯、水果、轻轻翻身。下一代的大梦想，从一瓶饲料开始。","Feed adults, rescue overturned beetles, and raise the next generation."));
            var roster=Column(40,265,365,550);
            foreach(var beetle in State.beetles)
            {
                var b=beetle;var c=Card(roster,135);
                Label(c,b.name+(b.id==State.selectedId?"  ◂":""),18,15,325,35,25,b.dead?muted:accent);
                Label(c,$"{(b.female?"雌 ♀":"雄 ♂")}  {b.length:0.0} mm   {(b.dead?"死亡":b.overturned?"需要帮助！":b.Adult(State.week)?"成虫":"养成中")}",18,55,325,30,19);
                Btn(c,"观察 / 选择",18,91,325,34,()=>app.Select(b.id));
            }
            var expand=Card(roster,128);Label(expand,$"容量 {State.capacity} / 20   回程预留 {Session.Reserved}",18,15,325,35,21);
            Btn(expand,State.capacity<20?$"扩建  {(State.capacity==6?Session.Catalog.upgrade12:Session.Catalog.upgrade20):N0} G":"已满级",18,65,325,44,()=>app.Execute(s=>s.Upgrade()),State.capacity<20);
            var selected=State.beetles.FirstOrDefault(x=>x.id==State.selectedId)??State.beetles.FirstOrDefault();
            var detail=Column(425,265,700,550);
            if(selected!=null)
            {
                var b=selected;
                var card=Card(detail,345);Label(card,b.name,24,20,640,42,31,accent);careLabel=Label(card,Details(b),24,76,645,220,23);
                Btn(card,"帮助翻身  H",24,290,310,40,()=>app.Execute(s=>s.Rescue(b.id)),b.overturned&&!b.dead);
                Btn(card,b.dead?"清理栏位":"更换养殖环境",350,290,315,40,()=>{if(b.dead)app.Execute(s=>s.ClearDead(b.id));else OpenHousing(b.id);});
                if(b.Adult(State.week))
                {
                    var food=Card(detail,145);Label(food,b.meal==null?"开饭啦 · 吃完恢复饱食与半颗心":"食盆已备好 · 等待甲虫吃完",22,18,640,40,25);
                    string[] names={"红薯 15 G","香蕉 20 G","苹果 20 G"};for(int i=0;i<3;i++){int index=i;Btn(food,names[i],22+i*218,77,207,45,()=>app.Execute(s=>s.Feed(b.id,index)),!b.listed&&!b.overturned&&b.meal==null&&State.battle?.beetleId!=b.id&&State.auction?.beetleId!=b.id);}
                }
                else if(!b.dead)
                {
                    var growth=Card(detail,270);Label(growth,"幼虫养成 · 一次选好，按周成长",22,18,640,35,26);
                    for(int i=0;i<3;i++){int choice=i;var f=Session.Catalog.feeds[i];Btn(growth,$"{f.name}\n{f.cost} G ×{f.multiplier:0.00}",22+i*218,70,207,68,()=>{feedChoice=choice;QueueRefresh();},feedChoice!=i&&b.feed<0);}
                    string[] climates={"暖气 2周 ×0.88","常温 3周 ×1.00","冷柜 4–5周 ×1.22"};for(int i=0;i<3;i++){int choice=i;Btn(growth,climates[i],22+i*218,151,207,44,()=>{climateChoice=choice;QueueRefresh();},climateChoice!=i&&b.feed<0);}
                    Btn(growth,b.feed<0?"开始养成":"养成中 · 等待出栏",22,210,640,44,()=>app.Execute(s=>s.Grow(b.id,feedChoice,climateChoice)),b.feed<0);
                }
                var breed=Card(detail,250);Label(breed,"配种实验室 · 母本基因从右侧入槽",22,18,640,40,26);
                if(!State.beetles.Any(x=>x.id==parentMale))parentMale=State.beetles.FirstOrDefault(x=>!x.female&&x.Adult(State.week))?.id;
                if(!State.beetles.Any(x=>x.id==parentFemale))parentFemale=State.beetles.FirstOrDefault(x=>x.female&&x.Adult(State.week))?.id;
                var male=State.beetles.Find(x=>x.id==parentMale);var female=State.beetles.Find(x=>x.id==parentFemale);
                Label(breed,$"父本：{male?.name??"未选择"}    母本：{female?.name??"未选择"}\n选择左侧甲虫，再设为对应亲本。必须同种异性。",22,70,640,75,21,muted);
                Btn(breed,"将当前甲虫设为亲本",22,155,640,40,()=>{if(b.female)parentFemale=b.id;else parentMale=b.id;QueueRefresh();},b.Adult(State.week));
                Btn(breed,"配种 100 G · 1个后代",22,205,640,35,()=>app.Execute(s=>s.Breed(parentMale,parentFemale)),male!=null&&female!=null);
            }
            else{var c=Card(detail,190);Label(c,"虫舍有点安静。\n去市场买一只，或派捕虫队出发吧。",24,25,630,100,27);Btn(c,"去市场",24,130,630,40,()=>app.Go(CampaignPage.Market));}
            var tools=Card(detail,215);Label(tools,"虫舍小账本",22,18,640,40,26);
            Btn(tools,"帮工一周 +150 G",22,75,310,45,()=>app.Execute(s=>s.NextWeek(true)));
            Btn(tools,"保存当前日记",348,75,310,45,()=>app.SaveSlot(0));
            Btn(tools,"重载物种 / 环境配置",22,135,636,45,app.ReloadContent);
            Label(root,"OBSERVATION · 微距观察",1170,265,390,42,24,accent);
            behaviorLabel=Label(root,"观察中",1170,605,390,40,23,accent);
            Btn(root,"轻触观察区",1170,655,350,40,()=>{if(!careController)careController=FindFirstObjectByType<CampaignCareController>();if(careController)careController.Disturb(State.selectedId);},selected!=null&&!selected.dead);
            Label(root,"投下食物后会自行靠近、进食。\n翻倒需扶正，60秒后开始受伤。\n开菜单时暂停，换对象仍继续。",1170,720,370,90,18,muted);
        }
        void OpenHousing(string id)
        {
            var p=OpenModal(800,550);Label(p,"选择养殖环境",30,25,740,50,32);var list=Column(30,95,740,355,p);
            for(int i=0;i<Session.Catalog.housing.Length;i++)
            {
                var h=Session.Catalog.housing[i];
                var card=Card(list,85);Btn(card,$"{h.name}  {h.temperature}°C / {h.humidity}% RH\n每周恢复 {h.healthRecovery:0.00} 心",5,5,720,75,()=>{CloseModal();app.Execute(s=>s.House(id,h.id));});
            }
            Btn(p,"返回",30,475,740,45,CloseModal);
        }
        void DrawCatching()
        {
            Title(L("捕虫队 · 远方有新的朋友","Catching squad / New friends await"),"与市场独立的派遣中心。每队基础带回2只；金币和道具出发时扣除。回程自动入住温室。");
            var destinations=Column(40,265,825,550);var desk=Column(885,265,675,550);
            foreach(var rule in Session.Catalog.catches)
            {
                var c=rule;bool open=Session.Unlocked(c);var card=Card(destinations,205);
                Label(card,c.name,22,18,770,40,28,accent);
                Label(card,$"{c.min}–{c.max} mm  ·  {c.weeks} 周  ·  {c.cost:N0} G\n{string.Join("、",c.species.Select(id=>Session.Catalog.Species(id).name))}\n稀有 {c.rare:P0}  /  {(c.id=="amazon"?"3星":"2星")} {c.twoStar:P0}",22,65,770,90,21);
                Btn(card,open?"派遣捕虫队":$"需赢得 Lv.{c.unlock} 联赛"+(c.id=="amazon"?" / 科学家许可":""),22,160,770,35,()=>app.Execute(s=>s.Dispatch(c.id,withSap,withLamp)),open);
            }
            var kit=Card(desk,245);Label(kit,"出发前打包",24,20,610,40,30);
            Btn(kit,$"{(withSap?"✓":"○")} 发酵树液蜜 · 数量+2 · 库存 {State.sap}",24,80,610,50,()=>{withSap=!withSap;QueueRefresh();});
            Btn(kit,$"{(withLamp?"✓":"○")} 紫外灯 · 大型率+25% · 库存 {State.lamp}",24,145,610,50,()=>{withLamp=!withLamp;QueueRefresh();});
            Label(kit,"道具在市场采购。派出队伍占用预留容量。",24,205,610,30,19,muted);
            foreach(var e in State.expeditions){var card=Card(desk,145);Label(card,Session.Catalog.Catch(e.habitat).name,22,18,620,40,26,accent);Label(card,$"第 {e.dueWeek} 周回程 · 剩余 {e.dueWeek-State.week} 周\n预留 {e.count} 格  {(e.large?"携带紫外灯":"轻装出发")}",22,70,620,65,22);}
            if(State.expeditions.Count==0){var card=Card(desk,160);Label(card,"暂时没有远行的队伍。\n选好栖息地，让探险开始吧！",24,35,610,100,26,muted);}
        }
    }
}
