using System;
using System.Linq;
using BugDuel.Campaign;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        int warehouseCategory=-1,lastWarehouseCategory;
        bool warehouseOpen;
        static bool requestedWarehouse;
        RectTransform warehouseDetails,warehouseBody;
        Tween warehouseTween,warehouseModeTween;
        float warehouseProgress,warehouseAlpha=1;
        readonly string[] warehouseCategories={"甲虫","成虫食物","养殖用品","景观","捕虫与扩展","标本"};
        public bool WarehouseOpen=>warehouseOpen;
        public int WarehouseCategory=>warehouseCategory;
        public bool GreenhouseInfoOpen=>greenhouseInfoOpen;

        void OpenWarehouse()
        {
            if(Session==null)return;
            CloseModal();
            if(page!=CampaignPage.Greenhouse){requestedWarehouse=true;app.Go(CampaignPage.Greenhouse);return;}
            if(warehouseOpen){QueueRefresh();return;}
            CloseGreenhouseInfo();CloseFeedingDish();warehouseOpen=true;warehouseCategory=-1;warehouseProgress=0;
            if(sidebarCollapsed)SetSidebarCollapsed(false);
            FadeGreenhouseNavigation();QueueRefresh();
        }
        void ExitWarehouse()
        {
            warehouseOpen=false;warehouseCategory=-1;AnimateWarehouseWidth(0);FadeGreenhouseNavigation();QueueRefresh();
            if(UnityEngine.EventSystems.EventSystem.current)UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        }
        void FadeGreenhouseNavigation()
        {
            warehouseModeTween?.Kill();warehouseAlpha=0;
            warehouseModeTween=DOTween.To(()=>warehouseAlpha,value=>
            {
                warehouseAlpha=value;
                if(warehouseBody)Component<CanvasGroup>(warehouseBody).alpha=value;
            },1,.18f).SetEase(Ease.OutCubic).SetUpdate(true).SetLink(gameObject).OnComplete(()=>warehouseModeTween=null);
        }
        void DrawWarehouseSidebar(float unit,float height)
        {
            warehouseBody=Rect("WarehouseBody",root,0,86,unit*2,height-170);
            Component<CanvasGroup>(warehouseBody).alpha=warehouseAlpha;
            var categories=MBox("WarehouseCategories",warehouseBody,0,0,unit,height-170,CampaignRetroTheme.Window);
            var list=Column(4,4,unit-8,height-228,categories,"WarehouseCategoryScroll");list.rightInset=0;
            for(int i=0;i<warehouseCategories.Length;i++)
            {
                int category=i;bool active=warehouseCategory==i;
                var b=MButton(list.content,warehouseCategories[i]+"  ›",0,i*44,list.width,43,()=>SelectWarehouseCategory(category),true,20,true,
                    active?CampaignRetroTheme.Selection:CampaignRetroTheme.Window,active?CampaignRetroTheme.SelectionInk:ink);
                b.GetComponentInChildren<TMPro.TMP_Text>().alignment=TMPro.TextAlignmentOptions.MidlineLeft;
            }
            list.content.sizeDelta=new Vector2(list.width,warehouseCategories.Length*44);
            MButton(categories,"退出",7,height-216,unit-14,38,ExitWarehouse,true,20);
            MBox("WarehouseDivider",warehouseBody,unit-2,0,2,height-170,CampaignRetroTheme.Rule);
            warehouseDetails=MBox("WarehouseDetails",warehouseBody,unit,0,unit,height-170,CampaignRetroTheme.Panel);
            Component<RetroBevel>(warehouseDetails).Configure(2,CampaignRetroTheme.DarkEdge,CampaignRetroTheme.LightEdge);
            var group=Component<CanvasGroup>(warehouseDetails);group.alpha=warehouseProgress;
            group.interactable=group.blocksRaycasts=warehouseCategory>=0&&warehouseProgress>.98f;
            bool visible=warehouseCategory>=0||warehouseProgress>.001f;warehouseDetails.gameObject.SetActive(visible);
            if(visible)BuildWarehouseCategory(unit,height-170);
        }
        void SelectWarehouseCategory(int category)
        {
            if(category<0||category>=warehouseCategories.Length)return;
            bool show=warehouseCategory!=category;
            warehouseCategory=show?category:-1;if(show)lastWarehouseCategory=category;
            AnimateWarehouseWidth(show?1:0);QueueRefresh();
            if(UnityEngine.EventSystems.EventSystem.current)UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        }
        void AnimateWarehouseWidth(float target)
        {
            warehouseTween?.Kill();if(backdrop)backdrop.DeferViewportResize=true;
            warehouseTween=DOTween.To(()=>warehouseProgress,value=>
            {
                warehouseProgress=value;ApplySidebar();
                if(warehouseDetails)
                {
                    var group=warehouseDetails.GetComponent<CanvasGroup>();group.alpha=value;
                    group.interactable=group.blocksRaycasts=warehouseCategory>=0&&value>.98f;
                }
            },target,.22f).SetEase(Ease.OutCubic).SetUpdate(true).SetLink(gameObject).OnComplete(()=>
            {
                warehouseTween=null;if(backdrop)backdrop.DeferViewportResize=SidebarAnimationActive;
                ApplySidebar();QueueRefresh();
                if(warehouseCategory>=0&&warehouseDetails)SelectFirst(warehouseDetails);
            });
        }
        void BuildWarehouseCategory(float width,float height)
        {
            int category=warehouseCategory>=0?warehouseCategory:lastWarehouseCategory;
            const float padding=12,gap=10,tileHeight=174,thumbnailHeight=86;
            // Paper thickness belongs to the frame, not the image or text's usable area.
            float paperRight=CampaignRetroTheme.Paper?5:0,paperBottom=CampaignRetroTheme.Paper?9:0;
            var list=Column(padding,padding,width-padding*2-paperRight,height-padding*2-paperBottom,warehouseDetails,"WarehouseItems");list.rightInset=0;
            int columns=Mathf.Max(1,Mathf.FloorToInt((list.width+gap)/(108+gap)));
            float cell=(list.width-(columns-1)*gap)/columns,innerWidth=cell-padding*2-paperRight;
            int count=0;
            void Tile(string title,string detail,CampaignMarketArtwork.Kind art,Action action)
            {
                float x=(count%columns)*(cell+gap),y=(count/columns)*(tileHeight+gap);count++;
                var button=MButton(list.content,"",x,y,cell,tileHeight,action,true,17);
                var shadow=Component<UnityEngine.UI.Shadow>(button.transform);shadow.enabled=!CampaignRetroTheme.Paper;shadow.effectColor=new Color(0,0,0,CampaignRetroTheme.Dark?.32f:.18f);shadow.effectDistance=new Vector2(2,-2);
                button.gameObject.name="Warehouse item · "+title;
                var thumb=MBox("ItemThumbnail",button.transform,padding,padding,innerWidth,thumbnailHeight,CampaignRetroTheme.SurfaceInset,CampaignRetroTheme.Border,1);
                // Keep a single paper frame; the inset illustration must not become another raised card.
                thumb.GetComponent<RetroBevel>().enabled=!CampaignRetroTheme.Paper;
                float size=Mathf.Min(78,innerWidth-8);
                Component<CampaignMarketArtwork>(Rect("Artwork",thumb,(innerWidth-size)/2,(thumbnailHeight-size)/2,size,size)).SetKind(art);
                var name=Label(button.transform,title,padding,104,innerWidth,24,18);
                name.textWrappingMode=TMPro.TextWrappingModes.NoWrap;name.alignment=TMPro.TextAlignmentOptions.MidlineLeft;name.margin=Vector4.zero;
                name.enableAutoSizing=true;CampaignLocalization.SetFontSize(name,18,15);
                var quantity=Label(button.transform,detail,padding,132,innerWidth,22,17,CampaignRetroTheme.Price);
                quantity.alignment=TMPro.TextAlignmentOptions.MidlineRight;quantity.textWrappingMode=TMPro.TextWrappingModes.NoWrap;quantity.margin=Vector4.zero;
                quantity.enableAutoSizing=true;CampaignLocalization.SetFontSize(quantity,17,14);
            }
            foreach(string supply in TradableSupplies)
            {
                int itemCategory=CampaignDecorations.Known(supply)?3:CampaignRearingItems.All.Contains(supply)?2:CampaignItems.FoodIndex(supply)>=0?1:4;
                int owned=Session.InventoryCount(supply);if(itemCategory!=category||owned<=0)continue;
                string id=supply;Tile(CampaignItems.Name(id),"×"+owned,SupplyArtwork(id),()=>OpenWarehouseSupply(id));
            }
            if(category==0)foreach(var beetle in State.storedBeetles)
            {
                var b=beetle;Tile(CampaignLocalization.Name(b),$"{b.length:0.0} mm",BeetleMarketArtwork(b),()=>OpenWarehouseBeetle(b.id));
            }
            if(category==5)Tile("标本室","×"+State.specimens.Count,CampaignMarketArtwork.Kind.Specimen,()=>app.Go(CampaignPage.SpecimenRoom));
            int rows=Mathf.CeilToInt(count/(float)columns);
            list.content.sizeDelta=new Vector2(list.width,rows>0?rows*(tileHeight+gap)-gap:58);
            if(count==0)Label(list.content,"暂无物品",8,18,list.width-16,40,21,muted);
        }
        RectTransform WarehouseItemDialog(string title)
        {
            var p=OpenModal(540,360,false,false);
            Component<RetroBevel>(p).Configure(3,CampaignRetroTheme.LightEdge,CampaignRetroTheme.DarkEdge);MTitle(p,title,534);
            MButton(p,"×",505,5,28,26,CloseModal,true,18,false,CampaignRetroTheme.Danger,Color.white);return p;
        }
        void WarehouseCommand(Action<CampaignSession> command)
        {
            CloseModal();app.Execute(command);
        }
        void OpenWarehouseSupply(string item)
        {
            var p=WarehouseItemDialog(CampaignItems.Name(item));
            Component<CampaignMarketArtwork>(Rect("ItemArtwork",p,18,54,104,104)).SetKind(SupplyArtwork(item));
            Label(p,CampaignItems.Name(item)+" ×"+Session.InventoryCount(item),138,54,382,40,25,accent);
            var selected=State.beetles.FirstOrDefault(b=>b.id==State.selectedId);int food=CampaignItems.FoodIndex(item);
            string detail=CampaignRearingItems.All.Contains(item)?CampaignRearingItems.Description(item):CampaignDecorations.Known(item)?$"{CampaignDecorations.Get(item).width}×{CampaignDecorations.Get(item).depth} 格":selected==null?"未选择甲虫":CampaignLocalization.Name(selected);
            Label(p,detail,138,104,380,112,20,muted);
            Action use;string label;bool available=true;
            if(CampaignDecorations.Known(item)){label="取出并布置";use=()=>UseWarehouseDecoration(item);available=selected!=null;}
            else if(CampaignRearingItems.All.Contains(item)){label="选择甲虫";use=()=>OpenRearingSupplyTargets(item);}
            else if(food>=0)
            {
                label="投喂";use=()=>WarehouseCommand(s=>s.Feed(selected.id,food));
                available=selected!=null&&selected.Adult(State.week)&&!selected.listed&&!selected.overturned&&selected.meal==null&&State.battle?.beetleId!=selected.id&&State.auction?.beetleId!=selected.id;
            }
            else if(item==CampaignItems.Cabinet){label="安装展柜";use=()=>WarehouseCommand(s=>s.InstallCabinetUpgrade());available=State.cabinet==3;}
            else{label="前往派遣";use=()=>{CloseModal();app.Go(CampaignPage.Catching);};}
            GreenhouseButton(p,label,18,242,504,44,use,available);
            GreenhouseButton(p,"回售",18,302,244,40,()=>OpenMarketSupply(item,true));GreenhouseButton(p,"返回",278,302,244,40,CloseModal);
        }
        void OpenWarehouseBeetle(string id)
        {
            var beetle=State.storedBeetles.FirstOrDefault(b=>b.id==id);if(beetle==null)return;
            var p=WarehouseItemDialog(CampaignLocalization.Name(beetle));
            Component<CampaignMarketArtwork>(Rect("BeetleArtwork",p,18,54,104,104)).SetKind(BeetleMarketArtwork(beetle));
            Label(p,$"{Session.Catalog.Species(beetle.speciesId).name}\n{(beetle.female?"雌":"雄")} · {CampaignSession.StageName(beetle.stage)}\n{beetle.length:0.0} mm",138,54,380,140,22,accent);
            GreenhouseButton(p,"命名",18,194,504,40,()=>OpenBeetleNaming(id,()=>OpenWarehouseBeetle(id)));
            if(beetle.dead)
            {
                GreenhouseButton(p,$"制作标本 {CampaignSession.SpecimenPreparationCost} G",18,247,504,40,()=>PrepareSpecimen(id),State.coins>=CampaignSession.SpecimenPreparationCost);
                GreenhouseButton(p,"遗弃",18,302,244,40,()=>Confirm("遗弃这只死亡甲虫？此后无法再制作标本。",()=>app.Execute(s=>s.ClearDead(id))));
            }
            else GreenhouseButton(p,"入住温室",18,247,504,40,()=>WarehouseCommand(s=>s.HouseStoredBeetle(id)),State.beetles.Count+Session.Reserved<State.capacity);
            GreenhouseButton(p,"返回",278,302,244,40,CloseModal);
        }
    }
}
