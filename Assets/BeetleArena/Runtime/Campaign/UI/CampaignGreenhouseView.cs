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

        bool greenhouseInfoOpen;
        RectTransform greenhouseInfo,greenhouseFooter;
        UnityEngine.UI.Button greenhouseMail;
        CampaignSpecimen observedSpecimen;
        readonly System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult> greenhouseHits=new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
        float GreenhouseColumnWidth=>canvas.rect.width/6f;

        void DrawGreenhouse()
        {
            float width=GreenhouseColumnWidth,height=canvas.rect.height;
            Component<UnityEngine.UI.RectMask2D>(root);
            var frame=Panel(root,0,0,width,height,name:"GreenhousePanel");Stretch(frame);
            var title=Panel(root,3,3,width-6,34,name:"GreenhouseTitle");
            title.anchorMax=new Vector2(1,1);title.sizeDelta=new Vector2(-6,34);
            Component<RetroBevel>(title).Configure(0,Color.clear,Color.clear,true,CampaignRetroTheme.TitleTop,CampaignRetroTheme.TitleBottom);
            Label(title,warehouseOpen?"仓库":"虫箱",9,2,width-24,30,21,Color.white);
            var clock=DrawCalendarClock(root,7,44,width-14,56);
            var clockRect=(RectTransform)clock.transform;clockRect.anchorMax=new Vector2(1,1);clockRect.sizeDelta=new Vector2(-14,56);
            var clockLabel=clock.GetComponentInChildren<TMPro.TMP_Text>();clockLabel.rectTransform.anchorMax=new Vector2(1,1);clockLabel.rectTransform.sizeDelta=new Vector2(-20,56);

            if(warehouseOpen)DrawWarehouseSidebar(width,height);
            else
            {
                var nav=Column(7,108,width-14,height-200,root,"GreenhouseNavigation");nav.rightInset=0;nav.spacing=7;
                warehouseBody=(RectTransform)nav.content.parent.parent;Component<UnityEngine.CanvasGroup>(warehouseBody).alpha=warehouseAlpha;
                void Link(string text,CampaignMarketArtwork.Kind art,Action action)
                {
                    var b=MButton(nav.content,text,0,nav.y,nav.width,48,action,true,21);
                    var label=b.GetComponentInChildren<TMPro.TMP_Text>();label.alignment=TMPro.TextAlignmentOptions.MidlineLeft;
                    label.rectTransform.anchoredPosition=new Vector2(48,0);label.rectTransform.sizeDelta=new Vector2(nav.width-58,48);
                    Component<CampaignMarketArtwork>(Rect("NavigationIcon",b.transform,10,10,28,28)).SetKind(art);
                    nav.y+=55;nav.content.sizeDelta=new Vector2(nav.width,nav.y);
                }
                Link("市场",CampaignMarketArtwork.Kind.Container,()=>app.Go(CampaignPage.Market));
                Link("仓库",CampaignMarketArtwork.Kind.Wood,OpenWarehouse);
                Link("联赛",CampaignMarketArtwork.Kind.Horn,()=>app.Go(CampaignPage.League));
                Link("捕虫队",CampaignMarketArtwork.Kind.Lamp,()=>app.Go(CampaignPage.Catching));
                Link("标本室",CampaignMarketArtwork.Kind.Specimen,()=>app.Go(CampaignPage.SpecimenRoom));
                Link("育种中心",CampaignMarketArtwork.Kind.Rainbow,()=>app.Go(CampaignPage.Breeding));
                Link("虫箱",CampaignMarketArtwork.Kind.Container,ToggleSidebar);
                nav.y+=10;
                Link("照料",CampaignMarketArtwork.Kind.Sap,()=>OpenGreenhouseInfo(0));
                Link("甲虫名册",CampaignMarketArtwork.Kind.Stag,OpenGreenhouseRoster);
                Link("主菜单",CampaignMarketArtwork.Kind.Leaves,()=>app.Go(CampaignPage.MainMenu));
            }
            var footer=greenhouseFooter=Panel(root,0,0,width,46,name:"GreenhouseFooter");
            footer.anchorMin=Vector2.zero;footer.anchorMax=new Vector2(1,0);footer.pivot=Vector2.zero;footer.anchoredPosition=Vector2.zero;footer.sizeDelta=new Vector2(0,46);
            var mail=greenhouseMail=MButton(footer,"邮箱",7,8,35,30,OpenMailbox,true,16);CampaignLocalization.Bind(mail.GetComponentInChildren<TMPro.TMP_Text>(),"信");
            if(Session.UnreadMail>0)
            {
                float size=Session.UnreadMail>99?28:24;
                var badge=Rect("UnreadMailBadge",mail.transform,32-size*.5f,-size*.5f+3,size,size);
                var disc=Component<RetroRoundBadge>(badge);disc.color=CampaignRetroTheme.Hex(0xc83131);disc.raycastTarget=false;
                var count=MText(badge,Session.UnreadMail>99?"99+":Session.UnreadMail.ToString(),2,1,size-4,size-2,13,Color.white,true);
                count.alignment=TMPro.TextAlignmentOptions.Center;count.enableAutoSizing=true;CampaignLocalization.SetFontSize(count,13,10);
            }
            notice=Label(root,"",7,height-83,width-14,32,14,muted);notice.name="GreenhouseNotice";
            if(greenhouseInfoOpen)DrawGreenhouseInfo();
        }

        void OpenGreenhouseInfo(int tab=0)
        {
            greenhouseInfoOpen=true;SetGreenhouseTab(tab);QueueRefresh();
        }
        void CloseGreenhouseInfo()
        {
            greenhouseInfoOpen=false;construction?.SetEditing(false);greenhouseTab=0;
            if(greenhouseInfo)greenhouseInfo.gameObject.SetActive(false);
            if(UnityEngine.EventSystems.EventSystem.current)UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            QueueRefresh();
        }
        void DrawGreenhouseInfo()
        {
            float width=480,height=Mathf.Min(610,canvas.rect.height-70);
            greenhouseInfo=Panel(canvas,0,0,width,height,name:"GreenhouseInfo");
            greenhouseInfo.SetAsLastSibling();
            greenhouseInfo.anchorMin=greenhouseInfo.anchorMax=greenhouseInfo.pivot=Vector2.one;greenhouseInfo.anchoredPosition=new Vector2(-24,-48);
            Component<RetroBevel>(greenhouseInfo).Configure(3,CampaignRetroTheme.LightEdge,CampaignRetroTheme.DarkEdge);
            var shadow=Component<UnityEngine.UI.Shadow>(greenhouseInfo);shadow.effectColor=new Color(0,0,0,.3f);shadow.effectDistance=new Vector2(4,-5);
            MTitle(greenhouseInfo,"甲虫信息",width-6);
            MButton(greenhouseInfo,"×",width-35,5,28,26,CloseGreenhouseInfo,true,18,false,CampaignRetroTheme.Hex(0xc83131),Color.white);
            var selected=State.beetles.FirstOrDefault(x=>x.id==State.selectedId)??State.beetles.FirstOrDefault();
            if(selected!=null)
            {
                Component<CampaignMarketArtwork>(Rect("BeetlePortrait",greenhouseInfo,12,43,66,66)).SetKind(BeetleMarketArtwork(selected));
                Label(greenhouseInfo,CampaignLocalization.Name(selected),90,43,width-180,30,23,accent);
                Label(greenhouseInfo,$"{selected.length:0.0} mm · {Session.Catalog.Species(selected.speciesId).name}",90,79,width-104,30,18,muted);
                MButton(greenhouseInfo,"命名",width-80,45,66,30,()=>OpenBeetleNaming(selected.id),true,17);
            }
            else Label(greenhouseInfo,"暂无甲虫",14,48,width-28,55,23,muted);
            float tabWidth=(width-36)/3;string[] tabs={"照料","管理","建造"};int[] ids={0,2,3};
            for(int i=0;i<tabs.Length;i++){int tab=ids[i];GreenhouseButton(greenhouseInfo,tabs[i],12+i*(tabWidth+6),123,tabWidth,34,()=>SetGreenhouseTab(tab),greenhouseTab!=tab);}
            string scrollName=greenhouseTab switch{0=>"CareScroll",2=>"ManagementScroll",_=>"ConstructionScroll"};
            var detail=Column(10,166,width-20,height-222,greenhouseInfo,scrollName);detail.rightInset=0;detail.spacing=7;
            if(greenhouseTab==0)DrawGreenhouseCare(detail,selected);else if(greenhouseTab==2)DrawGreenhouseManagement(detail,selected);else DrawConstructionInventory(detail);
            GreenhouseButton(greenhouseInfo,"场景全景",12,height-44,148,32,()=>observation?.SetMode(CampaignObservationMode.Environment));
            GreenhouseButton(greenhouseInfo,"甲虫近观",166,height-44,148,32,()=>observation?.SetMode(CampaignObservationMode.Specimen),selected!=null);
            GreenhouseButton(greenhouseInfo,"重置视角",320,height-44,148,32,()=>observation?.ResetView());
        }
        void ReadGreenhousePointer()
        {
            var mouse=UnityEngine.InputSystem.Mouse.current;
            if(mouse==null||!mouse.leftButton.wasPressedThisFrame||app.Busy||modal||!construction||construction.Editing)return;
            var position=mouse.position.ReadValue();var events=UnityEngine.EventSystems.EventSystem.current;
            if(events)
            {
                greenhouseHits.Clear();events.RaycastAll(new UnityEngine.EventSystems.PointerEventData(events){position=position},greenhouseHits);
                if(greenhouseHits.Count>0)return;
            }
            if(!construction.TryPointerRay(position,out var ray))return;
            if(!observedSpecimen)observedSpecimen=FindObjectsByType<CampaignSpecimen>(FindObjectsSortMode.None).FirstOrDefault(s=>s.selected&&s.gameObject.scene==gameObject.scene);
            if(!observedSpecimen)return;
            foreach(var renderer in observedSpecimen.GetComponentsInChildren<Renderer>())
                if(renderer.enabled&&renderer.bounds.IntersectRay(ray)){OpenGreenhouseInfo();break;}
        }
        bool HandleGreenhouseBack()
        {
            if(HandleConstructionBack())return true;
            if(greenhouseInfoOpen){CloseGreenhouseInfo();return true;}
            if(warehouseOpen){if(warehouseCategory>=0)SelectWarehouseCategory(warehouseCategory);else ExitWarehouse();return true;}
            return false;
        }
        UnityEngine.UI.Button GreenhouseButton(Transform parent,string text,float x,float y,float width,float height,Action action,bool enabled=true)
        {
            var button=Btn(parent,text,x,y,width,height,action,enabled);
            var label=button.GetComponentInChildren<TMPro.TMP_Text>();
            if(label)CampaignLocalization.SetFontSize(label,17,13);
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
                GreenhouseButton(card,"选择观察",398,34,180,36,()=>{CloseModal();app.Select(b.id);OpenGreenhouseInfo();});
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
            string status=b.dead?"已死亡":b.overturned?$"翻身求助！已挣扎 {b.overturnSeconds:0} 秒":b.listed?"正在挂牌":"";
            string age=b.stage==CampaignLifeStage.ActiveAdult?$"活动寿命余 {Session.RemainingLifeDays(b):0.0} 天":"基质营养 "+b.substrateNutrition.ToString("0")+"% · 洁净 "+b.substrateCleanliness.ToString("0")+"%";
            string size=b.stage<CampaignLifeStage.DormantAdult?"预估成虫":"体长";
            if(status.Length>0)status+=" · ";
            return $"{CampaignSession.StageName(b.stage)} · {b.stageAgeDays:0.0}/{Session.StageDuration(b):0.0} 天\n{age}\n{hearts}  {b.hearts:0.0}/{b.maxHearts} 心    饱食 {b.nutrition:0}%\n{status}养殖记录 {b.totalAgeDays:0.0} 天\n{Session.Catalog.Housing(b.habitat).name} · {(b.female?"雌":"雄")}\n{size} {b.length:0.0} mm · 身价 {b.value:N0} G\n基因 {string.Join(" / ",b.genes.Select(g=>g.id+new string('★',g.stars)))}\n突变 {(string.IsNullOrEmpty(b.mutation)?"无":b.mutation)} · 胜场 {b.wins}";
        }
        void DrawGreenhouseCare(Flow flow,CampaignBeetle b)
        {
            float w=flow.CardWidth,pad=12,inside=w-pad*2,gap=6;
            if(b==null)
            {
                var empty=Card(flow,155);Label(empty,"暂无甲虫",pad,15,inside,82,20);
                GreenhouseButton(empty,"去市场",pad,106,inside,36,()=>app.Go(CampaignPage.Market));return;
            }
            var status=Card(flow,196);
            careLabel=Label(status,Details(b),pad,10,inside,175,17);
            if(b.dead)
            {
                var memorial=Card(flow,112);
                GreenhouseButton(memorial,$"制作装框标本 · {CampaignSession.SpecimenPreparationCost} G",pad,12,inside,40,()=>PrepareSpecimen(b.id),State.coins>=CampaignSession.SpecimenPreparationCost);
                GreenhouseButton(memorial,"遗弃遗体并腾出栏位",pad,64,inside,36,()=>Confirm("遗弃后无法恢复，也不能再制作标本。确定清理这只甲虫？",()=>app.Execute(s=>s.ClearDead(b.id))));return;
            }
            DrawStageCare(flow,b);
            var care=Card(flow,62);float controlWidth=(inside-gap)/2;
            GreenhouseButton(care,"更换环境",pad,12,controlWidth,38,()=>OpenHousing(b.id));
            GreenhouseButton(care,"轻触 / 互动",pad+controlWidth+gap,12,controlWidth,38,()=>{if(!careController)careController=FindFirstObjectByType<CampaignCareController>();if(careController)careController.Disturb(b.id);},b.Adult(State.week));
            if(b.Adult(State.week))
            {
                var food=Card(flow,102);
                Label(food,b.meal==null?"成虫喂食":"食盆有食物",pad,11,inside,35,18,accent);
                string[] foods=Enumerable.Range(0,3).Select(i=>CampaignItems.Name(CampaignItems.Food(i))+" ×"+Session.InventoryCount(CampaignItems.Food(i))).ToArray();float third=(inside-gap*2)/3;
                for(int i=0;i<foods.Length;i++)
                {
                    int index=i;GreenhouseButton(food,foods[i],pad+i*(third+gap),53,third,38,()=>app.Execute(s=>s.Feed(b.id,index)),Session.InventoryCount(CampaignItems.Food(i))>0&&!b.listed&&!b.overturned&&b.meal==null&&State.battle?.beetleId!=b.id&&State.auction?.beetleId!=b.id);
                }
            }
            else if(b.stage<=CampaignLifeStage.Larva3)
            {
                var growth=Card(flow,253);Label(growth,"体型养成 · 可选饲料与温控",pad,10,inside,32,21,accent);
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
                GreenhouseButton(growth,b.feed<0?"选用营养与温控方案":"方案已选定",pad,201,inside,40,()=>app.Execute(s=>s.Grow(b.id,feedChoice,climateChoice)),b.feed<0);
            }
        }

        void DrawGreenhouseManagement(Flow flow,CampaignBeetle selected)
        {
            float w=flow.CardWidth,pad=12,inside=w-pad*2;
            var housing=Card(flow,183);Label(housing,"虫舍与养殖环境",pad,11,inside,33,23,accent);
            Label(housing,$"入住 {State.beetles.Count}/{State.capacity}  ·  回程预留 {Session.Reserved}\n最大容量 20 格",pad,51,inside,55,18);
            GreenhouseButton(housing,State.capacity<20?$"扩建虫舍  {(State.capacity==6?Session.Catalog.upgrade12:Session.Catalog.upgrade20):N0} G":"虫舍已满级",pad,115,inside,40,()=>app.Execute(s=>s.Upgrade()),State.capacity<20);
            var current=Card(flow,110);Label(current,selected==null?"尚未选择甲虫":$"当前环境：{Session.Catalog.Housing(selected.habitat).name}",pad,10,inside,32,18);
            GreenhouseButton(current,selected!=null&&selected.dead?"处理遗体 / 制作标本":"更换当前甲虫的环境",pad,54,inside,38,()=>{if(selected.dead){greenhouseTab=0;QueueRefresh();}else OpenHousing(selected.id);},selected!=null);
            var tools=Card(flow,110);Label(tools,"虫舍管理",pad,12,inside,33,23,accent);
            GreenhouseButton(tools,"帮工一周 +150 G",pad,58,inside,38,()=>app.Execute(s=>s.NextWeek(true)));
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
