using System;
using System.Linq;
using BugDuel.Campaign;
using UnityEngine;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        int greenhouseTab;
        int feedChoice=1,climateChoice=1;
        string parentMale,parentFemale;

        void DrawGreenhouse()
        {
            // Root is anchored to the screen's left third; all persistent controls stay
            // inside this column. The scene has its own independent right-side viewport.
            float width=root.rect.width,height=root.rect.height;
            float margin=4,inner=width-margin*2,gap=6,third=(inner-gap*2)/3;
            Panel(root,0,0,width,height,name:"GreenhousePanel");
            var title=Panel(root,4,4,width-8,64,name:"GreenhouseTitle");
            title.GetComponent<UnityEngine.UI.Image>().color=CampaignRetroTheme.Frame;
            Component<RetroBevel>(title).Configure(0,Color.clear,Color.clear,true,CampaignRetroTheme.TitleTop,CampaignRetroTheme.TitleBottom);
            Label(title,"虫舍日记 · 温室",8,4,width-82,35,21,Color.white);
            Label(title,"卵 → 幼虫 → 蛹 → 成虫 · 自然成长",8,35,width-82,23,15,Color.white);

            CampaignPage[] destinations={CampaignPage.Catching,CampaignPage.Market,CampaignPage.League,CampaignPage.Auction,CampaignPage.Saves};
            string[] links={"捕虫队","市场","联赛","冠军拍卖","存档",$"邮箱 ({Session.UnreadMail})","仓库","设置"};
            float navWidth=(inner-gap*3)/4;
            for(int i=0;i<links.Length;i++)
            {
                int index=i;
                GreenhouseButton(root,links[i],margin+(i%4)*(navWidth+gap),77+(i/4)*38,navWidth,32,
                    ()=>{if(index==5)OpenMailbox();else if(index==6)OpenConstructionWarehouse();else if(index==7)OpenSettings();else app.Go(destinations[index]);});
            }
            Label(root,$"第 {State.week:00} 周  |  {State.coins:N0} G  |  口碑 {State.reputation}\n入住 {State.beetles.Count}/{State.capacity}  ·  回程预留 {Session.Reserved}",margin,157,inner,44,17,ink);
            GreenhouseButton(root,"场景全景",margin,207,third,34,()=>{observation?.SetMode(CampaignObservationMode.Environment);QueueRefresh();},!observation||observation.Mode!=CampaignObservationMode.Environment);
            GreenhouseButton(root,"甲虫近观",margin+third+gap,207,third,34,()=>{observation?.SetMode(CampaignObservationMode.Specimen);QueueRefresh();},State.beetles.Count>0&&(!observation||observation.Mode!=CampaignObservationMode.Specimen));
            GreenhouseButton(root,"标本室",margin+(third+gap)*2,207,third,34,()=>app.Go(CampaignPage.SpecimenRoom));
            Label(root,"右侧：滚轮缩放 · 按住右键转动视角",margin,245,inner,25,15,muted);

            var selected=State.beetles.FirstOrDefault(x=>x.id==State.selectedId)??State.beetles.FirstOrDefault();
            GreenhouseButton(root,selected==null?"甲虫名册 · 尚未入住":$"当前：{CampaignLocalization.Name(selected)}  ·  切换甲虫…",margin,275,inner-80,40,OpenGreenhouseRoster);
            GreenhouseButton(root,"命名",margin+inner-74,275,74,40,()=>OpenBeetleNaming(selected.id),selected!=null);
            string[] tabs={"日常照料","育种","虫舍管理","景观建造"};float tabWidth=(inner-gap*3)/4;
            for(int i=0;i<tabs.Length;i++)
            {
                int index=i;GreenhouseButton(root,tabs[i],margin+i*(tabWidth+gap),324,tabWidth,34,()=>SetGreenhouseTab(index),greenhouseTab!=i);
            }
            float footer=height-98,half=(inner-gap)/2;
            // Each tab has a distinct scroll tree: preserve positions during data refreshes,
            // but never reuse clipped text and drag state across different page layouts.
            string scrollName=greenhouseTab switch {0=>"CareScroll",1=>"BreedingScroll",2=>"ManagementScroll",_=>"ConstructionScroll"};
            var detail=Column(margin,364,inner,Mathf.Max(100,footer-370),name:scrollName);
            detail.rightInset=0;detail.spacing=6;
            if(greenhouseTab==0)DrawGreenhouseCare(detail,selected);
            else if(greenhouseTab==1)DrawGreenhouseBreeding(detail,selected);
            else if(greenhouseTab==2)DrawGreenhouseManagement(detail,selected);
            else DrawConstructionInventory(detail);

            GreenhouseButton(root,"重置视角",margin,footer,half,36,()=>observation?.ResetView(),observation);
            GreenhouseButton(root,"保存并回主菜单",margin+half+gap,footer,half,36,()=>{app.SaveSlot(0);app.Go(CampaignPage.MainMenu);});
            notice=Label(root,"",margin+8,footer+47,inner-16,42,15,ink);
            notice.name="GreenhouseNotice";
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
                var b=beetle;var card=Card(list,126);
                Label(card,CampaignLocalization.Name(b)+(b.id==State.selectedId?"  ◂ 当前":""),14,10,370,28,23,b.dead?muted:accent);
                Label(card,$"{Session.Catalog.Species(b.speciesId).name}\n{(b.female?"雌 ♀":"雄 ♂")}  ·  {(b.dead?"死亡":CampaignSession.StageName(b.stage))}",14,46,370,66,18);
                GreenhouseButton(card,"选择观察",398,34,180,36,()=>{CloseModal();app.Select(b.id);});
                GreenhouseButton(card,"命名",398,78,180,34,()=>OpenBeetleNaming(b.id,OpenGreenhouseRoster));
            }
            if(State.beetles.Count==0)
            {
                var empty=Card(list,120);Label(empty,"虫舍还没有甲虫。\n去市场购买，或派遣捕虫队。",18,16,564,90,22,muted);
            }
            GreenhouseButton(p,"返回温室",22,612,606,44,CloseModal);
        }

        string Details(CampaignBeetle b)
        {
            string hearts=new string('♥',Mathf.Clamp((int)Math.Ceiling(b.hearts),0,5))+new string('♡',5-Mathf.Clamp((int)Math.Ceiling(b.hearts),0,5));
            string status=b.dead?"已死亡":b.overturned?$"翻身求助！已挣扎 {b.overturnSeconds:0} 秒":b.listed?"正在挂牌":"自然成长中";
            string age=b.stage==CampaignLifeStage.ActiveAdult?$"活动寿命余 {Session.RemainingLifeDays(b):0.0} 天":"基质营养 "+b.substrateNutrition.ToString("0")+"% · 洁净 "+b.substrateCleanliness.ToString("0")+"%";
            string size=b.stage<CampaignLifeStage.DormantAdult?"预估成虫":"体长";
            return $"{CampaignSession.StageName(b.stage)} · {b.stageAgeDays:0.0}/{Session.StageDuration(b):0.0} 天\n{age}\n{hearts}  {b.hearts:0.0}/{b.maxHearts} 心    饱食 {b.nutrition:0}%\n{status} · 养殖记录 {b.totalAgeDays:0.0} 天\n{Session.Catalog.Housing(b.habitat).name} · {(b.female?"雌":"雄")}\n{size} {b.length:0.0} mm · 身价 {b.value:N0} G\n基因 {string.Join(" / ",b.genes.Select(g=>g.id+new string('★',g.stars)))}\n突变 {(string.IsNullOrEmpty(b.mutation)?"无":b.mutation)} · 胜场 {b.wins}";
        }
        void DrawGreenhouseCare(Flow flow,CampaignBeetle b)
        {
            float w=flow.CardWidth,pad=12,inside=w-pad*2,gap=6;
            if(b==null)
            {
                var empty=Card(flow,155);Label(empty,"虫舍有点安静。\n先去市场买一只，或派捕虫队出发。",pad,15,inside,82,20);
                GreenhouseButton(empty,"去市场",pad,106,inside,36,()=>app.Go(CampaignPage.Market));return;
            }
            var status=Card(flow,250);
            Label(status,Session.Catalog.Species(b.speciesId).name,pad,10,inside,25,18,accent);
            careLabel=Label(status,Details(b),pad,38,inside,175,17);
            behaviorLabel=Label(status,"观察中",pad,220,inside,25,17,accent);
            if(b.dead)
            {
                var memorial=Card(flow,188);
                Label(memorial,"为这段虫舍记忆留一席位置。",pad,12,inside,34,20,accent);
                GreenhouseButton(memorial,$"制作装框标本 · {CampaignSession.SpecimenPreparationCost} G",pad,54,inside,40,()=>PrepareSpecimen(b.id),State.coins>=CampaignSession.SpecimenPreparationCost);
                GreenhouseButton(memorial,"遗弃遗体并腾出栏位",pad,103,inside,36,()=>Confirm("遗弃后无法恢复，也不能再制作标本。确定清理这只甲虫？",()=>app.Execute(s=>s.ClearDead(b.id))));
                Label(memorial,"标本可保存、布展，或在市场出售。",pad,148,inside,27,16,muted);return;
            }
            DrawStageCare(flow,b);
            var care=Card(flow,92);float controlWidth=(inside-gap)/2;
            GreenhouseButton(care,"更换环境",pad,12,controlWidth,38,()=>OpenHousing(b.id));
            GreenhouseButton(care,"轻触 / 互动",pad+controlWidth+gap,12,controlWidth,38,()=>{if(!careController)careController=FindFirstObjectByType<CampaignCareController>();if(careController)careController.Disturb(b.id);},b.Adult(State.week));
            Label(care,b.Adult(State.week)?"翻倒后需扶正；持续 60 秒会开始受伤。":"观察场景随阶段切换；蛹与蛰伏成虫请静养。",pad,60,inside,25,15,muted);
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
            else if(b.stage<=CampaignLifeStage.Larva3)
            {
                var growth=Card(flow,285);Label(growth,"体型养成 · 可选饲料与温控",pad,10,inside,32,21,accent);
                float third=(inside-gap*2)/3;
                for(int i=0;i<Session.Catalog.feeds.Length&&i<3;i++)
                {
                    int choice=i;var f=Session.Catalog.feeds[i];
                    GreenhouseButton(growth,$"{(feedChoice==i?"● ":"")}{f.name}\n{f.cost} G ×{f.multiplier:0.00}",pad+i*(third+gap),51,third,66,()=>{feedChoice=choice;QueueRefresh();},feedChoice!=i&&b.feed<0);
                }
                string[] climates={"偏暖\n体型 ×0.88","常温\n体型 ×1.00","偏凉\n体型 ×1.22"};
                for(int i=0;i<climates.Length;i++)
                {
                    int choice=i;GreenhouseButton(growth,(climateChoice==i?"● ":"")+climates[i],pad+i*(third+gap),129,third,60,()=>{climateChoice=choice;QueueRefresh();},climateChoice!=i&&b.feed<0);
                }
                GreenhouseButton(growth,b.feed<0?"选用营养与温控方案":"方案已选定 · 自然成长中",pad,201,inside,40,()=>app.Execute(s=>s.Grow(b.id,feedChoice,climateChoice)),b.feed<0);
                Label(growth,"未购买方案也会成长；周期在时间设置中调整。",pad,248,inside,27,15,muted);
            }
        }

        void DrawGreenhouseBreeding(Flow flow,CampaignBeetle selected)
        {
            float w=flow.CardWidth,pad=12,inside=w-pad*2;
            if(!State.beetles.Any(x=>x.id==parentMale&&!x.dead))parentMale=State.beetles.FirstOrDefault(x=>!x.female&&!x.dead&&x.Adult(State.week))?.id;
            if(!State.beetles.Any(x=>x.id==parentFemale&&!x.dead))parentFemale=State.beetles.FirstOrDefault(x=>x.female&&!x.dead&&x.Adult(State.week))?.id;
            var male=State.beetles.Find(x=>x.id==parentMale);var female=State.beetles.Find(x=>x.id==parentFemale);
            var breed=Card(flow,308);Label(breed,"配种实验室",pad,12,inside,36,24,accent);
            Label(breed,$"父本：{(male==null?"未选择":CampaignLocalization.Name(male))}\n母本：{(female==null?"未选择":CampaignLocalization.Name(female))}\n使用上方名册切换甲虫，再设为亲本。\n需同种异性成虫；母本基因从右侧入槽。",pad,60,inside,119,18,muted);
            GreenhouseButton(breed,"将当前甲虫设为亲本",pad,188,inside,39,()=>{if(selected.female)parentFemale=selected.id;else parentMale=selected.id;QueueRefresh();},selected!=null&&!selected.dead&&selected.Adult(State.week));
            GreenhouseButton(breed,"配种 100 G · 1 个后代",pad,239,inside,42,()=>app.Execute(s=>s.Breed(parentMale,parentFemale)),male!=null&&female!=null);
        }

        void DrawGreenhouseManagement(Flow flow,CampaignBeetle selected)
        {
            float w=flow.CardWidth,pad=12,inside=w-pad*2;
            var housing=Card(flow,183);Label(housing,"虫舍与养殖环境",pad,11,inside,33,23,accent);
            Label(housing,$"入住 {State.beetles.Count}/{State.capacity}  ·  回程预留 {Session.Reserved}\n最大容量 20 格",pad,51,inside,55,18);
            GreenhouseButton(housing,State.capacity<20?$"扩建虫舍  {(State.capacity==6?Session.Catalog.upgrade12:Session.Catalog.upgrade20):N0} G":"虫舍已满级",pad,115,inside,40,()=>app.Execute(s=>s.Upgrade()),State.capacity<20);
            var current=Card(flow,110);Label(current,selected==null?"尚未选择甲虫":$"当前环境：{Session.Catalog.Housing(selected.habitat).name}",pad,10,inside,32,18);
            GreenhouseButton(current,selected!=null&&selected.dead?"处理遗体 / 制作标本":"更换当前甲虫的环境",pad,54,inside,38,()=>{if(selected.dead){greenhouseTab=0;QueueRefresh();}else OpenHousing(selected.id);},selected!=null);
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
    }
}
