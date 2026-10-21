using System;
using System.Linq;
using BugDuel.Campaign;
using UnityEngine;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        int greenhouseTab;

        void DrawGreenhouse()
        {
            // Root is anchored to the screen's left third; all persistent controls stay
            // inside this column. The scene has its own independent right-side viewport.
            float width=root.rect.width,height=root.rect.height;
            float margin=12,inner=width-margin*2,gap=6,third=(inner-gap*2)/3;
            Panel(root,0,0,width,height);
            var title=Panel(root,4,4,width-8,64);
            title.GetComponent<UnityEngine.UI.Image>().color=CampaignRetroTheme.Frame;
            Component<RetroBevel>(title).Configure(0,Color.clear,Color.clear,true,CampaignRetroTheme.TitleTop,CampaignRetroTheme.TitleBottom);
            Label(title,L("虫舍日记 · 温室","BUG DUEL / GREENHOUSE"),12,4,width-164,35,21,Color.white);
            Label(title,CampaignRetroTheme.SeasonName,12,35,width-164,23,15,Color.white);
            var clock=Label(title,"",width-148,8,128,48,17,Color.white);
            clock.name="LocalClock";clock.alignment=TMPro.TextAlignmentOptions.Center;
            Component<Canvas>(clock.transform);Component<CampaignLocalClock>(clock.transform).Bind(clock);

            CampaignPage[] destinations={CampaignPage.Catching,CampaignPage.Market,CampaignPage.League,CampaignPage.Auction,CampaignPage.Saves};
            string[] links={L("捕虫队","Catching"),L("市场","Market"),L("联赛","Leagues"),L("冠军拍卖","Auction"),L("存档","Saves"),$"邮箱 ({Session.UnreadMail})","仓库",L("设置","Settings")};
            float navWidth=(inner-gap*3)/4;
            for(int i=0;i<links.Length;i++)
            {
                int index=i;
                GreenhouseButton(root,links[i],margin+(i%4)*(navWidth+gap),77+(i/4)*38,navWidth,32,
                    ()=>{if(index==5)OpenMailbox();else if(index==6)OpenWarehouse();else if(index==7)OpenSettings();else app.Go(destinations[index]);});
            }
            Label(root,$"第 {State.week:00} 周  |  {State.coins:N0} G  |  口碑 {State.reputation}\n入住 {State.beetles.Count}/{State.capacity}  ·  回程预留 {Session.Reserved}",margin,157,inner,44,17,ink);
            GreenhouseButton(root,"场景全景",margin,207,third,34,()=>{observation?.SetMode(CampaignObservationMode.Environment);QueueRefresh();},!observation||observation.Mode!=CampaignObservationMode.Environment);
            GreenhouseButton(root,"甲虫近观",margin+third+gap,207,third,34,()=>{observation?.SetMode(CampaignObservationMode.Specimen);QueueRefresh();},State.beetles.Count>0&&(!observation||observation.Mode!=CampaignObservationMode.Specimen));
            GreenhouseButton(root,"重置视角",margin+(third+gap)*2,207,third,34,()=>observation?.ResetView());
            Label(root,"右侧：滚轮缩放 · 按住右键转动视角",margin,245,inner,25,15,muted);

            var selected=State.beetles.FirstOrDefault(x=>x.id==State.selectedId)??State.beetles.FirstOrDefault();
            GreenhouseButton(root,selected==null?"甲虫名册 · 尚未入住":$"当前：{selected.name}  ·  切换甲虫…",margin,275,inner,40,OpenGreenhouseRoster);
            string[] tabs={"日常照料","育种","虫舍管理"};
            for(int i=0;i<tabs.Length;i++)
            {
                int index=i;GreenhouseButton(root,tabs[i],margin+i*(third+gap),324,third,34,()=>{greenhouseTab=index;QueueRefresh();},greenhouseTab!=i);
            }
            var detail=Column(margin,367,inner,Mathf.Max(100,height-483));
            if(greenhouseTab==0)DrawGreenhouseCare(detail,selected);
            else if(greenhouseTab==1)DrawGreenhouseBreeding(detail,selected);
            else DrawGreenhouseManagement(detail,selected);

            float footer=height-105,half=(inner-gap)/2;
            GreenhouseButton(root,L("下一周  N","Next week  N"),margin,footer,half,36,()=>app.Execute(s=>s.NextWeek()));
            GreenhouseButton(root,L("保存并回主菜单","Save and return"),margin+half+gap,footer,half,36,()=>{app.SaveSlot(0);app.Go(CampaignPage.MainMenu);});
            var status=Panel(root,margin,footer+44,inner,52);status.GetComponent<UnityEngine.UI.Image>().color=CampaignRetroTheme.Notice;
            notice=Label(status,"",8,5,inner-16,42,15,ink);
        }

        UnityEngine.UI.Button GreenhouseButton(Transform parent,string text,float x,float y,float width,float height,Action action,bool enabled=true)
        {
            var button=Btn(parent,text,x,y,width,height,action,enabled);
            var label=button.GetComponentInChildren<TMPro.TMP_Text>();
            if(label){label.fontSize=17;label.fontSizeMax=17;label.fontSizeMin=13;}
            return button;
        }

        void OpenGreenhouseRoster()
        {
            var p=OpenModal(650,680);
            Label(p,$"甲虫名册 · {State.beetles.Count}/{State.capacity}",22,28,606,40,27,accent);
            var list=Column(22,84,606,506,p);
            foreach(var beetle in State.beetles)
            {
                var b=beetle;var card=Card(list,104);
                Label(card,b.name+(b.id==State.selectedId?"  ◂ 当前":""),14,10,572,28,23,b.dead?muted:accent);
                Label(card,$"{(b.female?"雌 ♀":"雄 ♂")}  {b.length:0.0} mm  ·  {(b.dead?"死亡":b.overturned?"需要帮助！":b.Adult(State.week)?"成虫":"养成中")}",14,42,350,40,18);
                GreenhouseButton(card,"选择观察",398,48,180,39,()=>{CloseModal();app.Select(b.id);});
            }
            if(State.beetles.Count==0)
            {
                var empty=Card(list,120);Label(empty,"虫舍还没有甲虫。\n去市场购买，或派遣捕虫队。",18,16,564,90,22,muted);
            }
            GreenhouseButton(p,"返回温室",22,612,606,44,CloseModal);
        }

        void DrawGreenhouseCare(Flow flow,CampaignBeetle b)
        {
            float w=flow.width-6,pad=12,inside=w-pad*2,gap=6;
            if(b==null)
            {
                var empty=Card(flow,155);Label(empty,"虫舍有点安静。\n先去市场买一只，或派捕虫队出发。",pad,15,inside,82,20);
                GreenhouseButton(empty,"去市场",pad,106,inside,36,()=>app.Go(CampaignPage.Market));return;
            }
            var status=Card(flow,202);
            careLabel=Label(status,Details(b),pad,10,inside,154,17);
            behaviorLabel=Label(status,"观察中",pad,169,inside,25,17,accent);
            var care=Card(flow,92);float controlWidth=(inside-gap*2)/3;
            GreenhouseButton(care,"帮助翻身 H",pad,12,controlWidth,38,()=>app.Execute(s=>s.Rescue(b.id)),b.overturned&&!b.dead);
            GreenhouseButton(care,b.dead?"清理栏位":"更换环境",pad+controlWidth+gap,12,controlWidth,38,()=>{if(b.dead)app.Execute(s=>s.ClearDead(b.id));else OpenHousing(b.id);});
            GreenhouseButton(care,"轻触 / 互动",pad+(controlWidth+gap)*2,12,controlWidth,38,()=>{if(!careController)careController=FindFirstObjectByType<CampaignCareController>();if(careController)careController.Disturb(b.id);},!b.dead);
            Label(care,"翻倒后需扶正；持续 60 秒会开始受伤。",pad,60,inside,25,15,muted);
            if(b.dead)return;
            if(b.Adult(State.week))
            {
                var food=Card(flow,132);
                Label(food,b.meal==null?"成虫喂食 · 吃完恢复饱食与健康":"食盆已备好 · 等待甲虫自行进食",pad,11,inside,35,18,accent);
                string[] foods=Enumerable.Range(0,3).Select(i=>CampaignItems.Name(CampaignItems.Food(i))+" ×"+Session.InventoryCount(CampaignItems.Food(i))).ToArray();float third=(inside-gap*2)/3;
                for(int i=0;i<foods.Length;i++)
                {
                    int index=i;GreenhouseButton(food,foods[i],pad+i*(third+gap),53,third,38,()=>app.Execute(s=>s.Feed(b.id,index)),Session.InventoryCount(CampaignItems.Food(i))>0&&!b.listed&&!b.overturned&&b.meal==null&&State.battle?.beetleId!=b.id&&State.auction?.beetleId!=b.id);
                }
                Label(food,"购买后先在邮箱领取；投喂只消耗仓库库存。",pad,98,inside,25,15,muted);
            }
            else
            {
                var growth=Card(flow,285);Label(growth,"幼虫养成 · 选好后按周成长",pad,10,inside,32,21,accent);
                float third=(inside-gap*2)/3;
                for(int i=0;i<Session.Catalog.feeds.Length&&i<3;i++)
                {
                    int choice=i;var f=Session.Catalog.feeds[i];
                    GreenhouseButton(growth,$"{(feedChoice==i?"● ":"")}{f.name}\n{f.cost} G ×{f.multiplier:0.00}",pad+i*(third+gap),51,third,66,()=>{feedChoice=choice;QueueRefresh();},feedChoice!=i&&b.feed<0);
                }
                string[] climates={"暖气\n2周 ×0.88","常温\n3周 ×1.00","冷柜\n4–5周 ×1.22"};
                for(int i=0;i<climates.Length;i++)
                {
                    int choice=i;GreenhouseButton(growth,(climateChoice==i?"● ":"")+climates[i],pad+i*(third+gap),129,third,60,()=>{climateChoice=choice;QueueRefresh();},climateChoice!=i&&b.feed<0);
                }
                GreenhouseButton(growth,b.feed<0?"确认饲料和温度 · 开始养成":"养成中 · 等待出栏",pad,201,inside,40,()=>app.Execute(s=>s.Grow(b.id,feedChoice,climateChoice)),b.feed<0);
                Label(growth,"饲料与温度在开始养成后锁定。",pad,248,inside,27,15,muted);
            }
        }

        void DrawGreenhouseBreeding(Flow flow,CampaignBeetle selected)
        {
            float w=flow.width-6,pad=12,inside=w-pad*2;
            if(!State.beetles.Any(x=>x.id==parentMale&&!x.dead))parentMale=State.beetles.FirstOrDefault(x=>!x.female&&!x.dead&&x.Adult(State.week))?.id;
            if(!State.beetles.Any(x=>x.id==parentFemale&&!x.dead))parentFemale=State.beetles.FirstOrDefault(x=>x.female&&!x.dead&&x.Adult(State.week))?.id;
            var male=State.beetles.Find(x=>x.id==parentMale);var female=State.beetles.Find(x=>x.id==parentFemale);
            var breed=Card(flow,308);Label(breed,"配种实验室",pad,12,inside,36,24,accent);
            Label(breed,$"父本：{male?.name??"未选择"}\n母本：{female?.name??"未选择"}\n使用上方名册切换甲虫，再设为亲本。\n需同种异性成虫；母本基因从右侧入槽。",pad,60,inside,119,18,muted);
            GreenhouseButton(breed,"将当前甲虫设为亲本",pad,188,inside,39,()=>{if(selected.female)parentFemale=selected.id;else parentMale=selected.id;QueueRefresh();},selected!=null&&!selected.dead&&selected.Adult(State.week));
            GreenhouseButton(breed,"配种 100 G · 1 个后代",pad,239,inside,42,()=>app.Execute(s=>s.Breed(parentMale,parentFemale)),male!=null&&female!=null);
        }

        void DrawGreenhouseManagement(Flow flow,CampaignBeetle selected)
        {
            float w=flow.width-6,pad=12,inside=w-pad*2;
            var housing=Card(flow,183);Label(housing,"虫舍与养殖环境",pad,11,inside,33,23,accent);
            Label(housing,$"入住 {State.beetles.Count}/{State.capacity}  ·  回程预留 {Session.Reserved}\n最大容量 20 格",pad,51,inside,55,18);
            GreenhouseButton(housing,State.capacity<20?$"扩建虫舍  {(State.capacity==6?Session.Catalog.upgrade12:Session.Catalog.upgrade20):N0} G":"虫舍已满级",pad,115,inside,40,()=>app.Execute(s=>s.Upgrade()),State.capacity<20);
            var current=Card(flow,110);Label(current,selected==null?"尚未选择甲虫":$"当前环境：{Session.Catalog.Housing(selected.habitat).name}",pad,10,inside,32,18);
            GreenhouseButton(current,selected!=null&&selected.dead?"清理死亡甲虫栏位":"更换当前甲虫的环境",pad,54,inside,38,()=>{if(selected.dead)app.Execute(s=>s.ClearDead(selected.id));else OpenHousing(selected.id);},selected!=null);
            var tools=Card(flow,224);Label(tools,"虫舍小账本",pad,12,inside,33,23,accent);
            GreenhouseButton(tools,"帮工一周 +150 G",pad,58,inside,38,()=>app.Execute(s=>s.NextWeek(true)));
            GreenhouseButton(tools,"保存当前日记",pad,108,inside,38,()=>app.SaveSlot(0));
            GreenhouseButton(tools,"重载物种 / 环境配置",pad,158,inside,38,app.ReloadContent);
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
                Btn(card,open?"派遣捕虫队":$"需赢得 Lv.{c.unlock} 联赛{(c.id=="amazon"?" / 科学家许可":"")}",22,160,770,35,()=>app.Execute(s=>s.Dispatch(c.id,withSap,withLamp)),open);
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
