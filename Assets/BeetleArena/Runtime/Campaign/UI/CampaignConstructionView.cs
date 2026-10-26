using System;
using System.Linq;
using BugDuel.Campaign;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UI=UnityEngine.UI;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        CampaignConstructionController construction;
        RectTransform sidebarHandle,currencyHost;
        UI.Button sidebarButton;
        Tween sidebarTween;
        float sidebarProgress;
        bool sidebarCollapsed;
        static string requestedDecoration;
        public bool SidebarCollapsed=>sidebarCollapsed;
        public float SidebarProgress=>sidebarProgress;
        public bool SidebarAnimationActive=>sidebarTween!=null&&sidebarTween.IsActive();

        void SetupConstruction()
        {
            construction=gameObject.AddComponent<CampaignConstructionController>();construction.Bind(backdrop);
            construction.Changed+=ConstructionChanged;
        }
        void ConstructionChanged()
        {
            if(greenhouseTab==3&&!construction.Editing)greenhouseTab=0;
            QueueRefresh();
        }
        void SetGreenhouseTab(int tab)
        {
            greenhouseInfoOpen=true;greenhouseTab=tab;construction?.SetEditing(tab==3);QueueRefresh();
        }
        void OpenConstructionWarehouse()
        {
            SetGreenhouseTab(3);
            if(sidebarCollapsed)ToggleSidebar();
        }
        void UseWarehouseDecoration(string itemId)
        {
            CloseModal();
            if(page==CampaignPage.Greenhouse){ExitWarehouse();OpenConstructionWarehouse();construction.BeginPlace(itemId);}
            else {requestedDecoration=itemId;app.Go(CampaignPage.Greenhouse);}
        }
        void ApplyConstructionRequest()
        {
            if(requestedWarehouse&&!app.Busy){requestedWarehouse=false;OpenWarehouse();}
            if(requestedDecoration==null||!construction||construction.CurrentBeetleId==null||app.Busy)return;
            string item=requestedDecoration;requestedDecoration=null;OpenConstructionWarehouse();construction.BeginPlace(item);
        }
        bool HandleConstructionBack()
        {
            if(!construction||!construction.Editing)return false;
            if(construction.HasPreview)construction.Cancel();else SetGreenhouseTab(0);
            return true;
        }
        void DrawConstructionInventory(Flow flow)
        {
            float w=flow.CardWidth,inside=w-24;
            var status=Card(flow,construction.HasPreview?160:145);
            Label(status,"景观仓库",12,9,inside,30,23,accent);
            Label(status,"每只甲虫独立布景 · 拖出卡片或点击取出",12,43,inside,25,15,muted);
            if(construction.HasPreview)
            {
                Label(status,(construction.PreviewValid?"可放置 · ":"不可放置 · ")+construction.PreviewReason,12,72,inside,40,16,construction.PreviewValid?new Color(.35f,.9f,.55f):new Color(1,.4f,.35f));
                GreenhouseButton(status,"Q ↶",12,119,(inside-12)/3,30,()=>construction.RotatePreview(-90));
                GreenhouseButton(status,"E ↷",18+(inside-12)/3,119,(inside-12)/3,30,()=>construction.RotatePreview(90));
                GreenhouseButton(status,"取消  Esc",24+2*(inside-12)/3,119,(inside-12)/3,30,()=>construction.Cancel());
            }
            else
            {
                Label(status,"左键放置 / 选中 · Q / E 旋转 · 右键转动视角",12,72,inside,24,14,muted);
                GreenhouseButton(status,"完成布置 · 放回甲虫",12,105,inside,30,()=>SetGreenhouseTab(0));
            }
            var selection=construction.Selected;
            if(selection!=null&&!construction.HasPreview)
            {
                var row=Card(flow,135);
                Label(row,"已选中 · "+CampaignDecorations.Name(selection.itemId),12,10,inside,29,19,accent);
                Label(row,$"位置 {selection.x+1}, {selection.z+1} · 朝向 {selection.rotation}°",12,43,inside,25,15,muted);
                float third=(inside-12)/3;
                GreenhouseButton(row,"移动",12,81,third,37,()=>construction.BeginMove(selection.id));
                GreenhouseButton(row,"放回仓库",18+third,81,third,37,()=>construction.ReturnSelected());
                GreenhouseButton(row,$"卖出 +{Session.SupplySellPrice(selection.itemId)} G",24+2*third,81,third,37,()=>construction.SellSelected());
            }
            var inventory=CampaignDecorations.All.Where(d=>Session.InventoryCount(d.id)>0).ToArray();
            for(int i=0;i<inventory.Length;i+=2)
            {
                var row=Card(flow,169);float cell=(inside-8)/2;
                for(int column=0;column<2&&i+column<inventory.Length;column++)
                {
                    var item=inventory[i+column];float x=12+column*(cell+8);int quantity=Session.InventoryCount(item.id);
                    var button=GreenhouseButton(row,"取出 · "+item.name,x,7,cell,152,()=>construction.BeginPlace(item.id),construction.CurrentBeetleId!=null);
                    CampaignLocalization.Bind(button.GetComponentInChildren<TMP_Text>(),"");
                    Component<CampaignMarketArtwork>(Rect("Decoration artwork",button.transform,8,7,cell-16,81)).SetKind(DecorationArtwork(item.id));
                    Label(button.transform,item.name,6,92,cell-12,26,18).alignment=TextAlignmentOptions.Center;
                    Label(button.transform,$"×{quantity}  ·  {item.width}×{item.depth} 格",6,121,cell-12,24,15,muted).alignment=TextAlignmentOptions.Center;
                    Component<CampaignDecorationDragSource>(button.transform).Bind(construction,item.id);
                }
            }
            if(inventory.Length==0)
            {var empty=Card(flow,90);Label(empty,"景观仓库暂时为空。\n可到市场补充，或将已摆放的景观收回。",12,12,inside,64,18,muted);}
            var shops=Card(flow,100);
            GreenhouseButton(shops,"购买更多景观",12,10,inside,35,()=>{construction.SetEditing(false);app.Go(CampaignPage.Market);});
            GreenhouseButton(shops,"查看全部仓库",12,55,inside,35,OpenWarehouse);
        }
        static CampaignMarketArtwork.Kind DecorationArtwork(string id)
        {
            switch(id)
            {
                case "decor-stones":return CampaignMarketArtwork.Kind.Stones;
                case "decor-fern":return CampaignMarketArtwork.Kind.Fern;
                case "decor-moss":return CampaignMarketArtwork.Kind.Moss;
                case "decor-leaf":return CampaignMarketArtwork.Kind.Leaves;
                case "decor-driftwood":return CampaignMarketArtwork.Kind.Driftwood;
                case "decor-water":return CampaignMarketArtwork.Kind.Water;
                case "decor-arch":return CampaignMarketArtwork.Kind.Arch;
                default:return CampaignMarketArtwork.Kind.GardenLamp;
            }
        }
        void EnsurePresentationHud()
        {
            if(Session!=null&&page!=CampaignPage.MainMenu&&page!=CampaignPage.Saves&&page!=CampaignPage.Arena)
            {
                if(!currencyHost)currencyHost=Panel(root,0,0,280,42);
                currencyHost.name="Player currency";
                currencyHost.anchorMin=currencyHost.anchorMax=currencyHost.pivot=new Vector2(0,1);
                currencyHost.anchoredPosition=new Vector2(1280,-119);
                currencyHost.sizeDelta=new Vector2(250,35);
                if(page==CampaignPage.SpecimenRoom)
                {
                    currencyHost.anchoredPosition=new Vector2(8,-57);
                    currencyHost.sizeDelta=new Vector2(root.rect.width*.5f-12,page==CampaignPage.SpecimenRoom?28:42);
                }
                if(page==CampaignPage.Market)
                {
                    currencyHost.anchoredPosition=new Vector2(12,-34);currencyHost.sizeDelta=new Vector2(260,28);
                }
                CampaignCurrencyHud.Attach(currencyHost,font,ink,new Color(.35f,.95f,.55f),new Color(1,.43f,.35f),true);currencyHost.SetAsLastSibling();
                if(page==CampaignPage.Greenhouse)
                {
                    currencyHost.SetParent(greenhouseFooter,false);
                    currencyHost.anchorMin=Vector2.zero;currencyHost.anchorMax=new Vector2(1,0);currencyHost.pivot=new Vector2(0,1);
                    currencyHost.anchoredPosition=new Vector2(56,38);currencyHost.sizeDelta=new Vector2(-100,30);
                    Component<RetroBevel>(currencyHost).Configure(2,CampaignRetroTheme.DarkEdge,CampaignRetroTheme.LightEdge);
                    currencyHost.GetComponent<CampaignCurrencyHud>().FloatAbove=true;
                    greenhouseMail.transform.SetAsLastSibling();
                }
            }
            if(page!=CampaignPage.Greenhouse)return;
            if(!sidebarHandle)
            {
                sidebarButton=Btn(canvas,"收起",0,0,30,30,ToggleSidebar);sidebarHandle=sidebarButton.GetComponent<RectTransform>();
                sidebarHandle.name="Sidebar toggle";
                var arrow=sidebarButton.GetComponentInChildren<TMP_Text>();CampaignLocalization.SetFontSize(arrow,17);
                arrow.rectTransform.anchoredPosition=new Vector2(2,0);arrow.rectTransform.sizeDelta=new Vector2(26,30);
                sidebarHandle.anchorMin=sidebarHandle.anchorMax=sidebarHandle.pivot=Vector2.zero;
            }
            CampaignLocalization.Bind(sidebarButton.GetComponentInChildren<TMP_Text>(),sidebarCollapsed?"▶":"◀");
            sidebarHandle.SetAsLastSibling();ApplySidebar();
            CampaignRescuePrompt.Attach(canvas,backdrop,font);
        }
        public void ToggleSidebar()
        {
            bool collapsed=!sidebarCollapsed;
            if(!collapsed&&warehouseOpen)ExitWarehouse();
            SetSidebarCollapsed(collapsed);
        }
        void SetSidebarCollapsed(bool collapsed)
        {
            sidebarCollapsed=collapsed;sidebarTween?.Kill();
            if(EventSystem.current)EventSystem.current.SetSelectedGameObject(null);
            if(backdrop)backdrop.DeferViewportResize=true;
            sidebarTween=DOTween.To(()=>sidebarProgress,v=>{sidebarProgress=v;ApplySidebar();},sidebarCollapsed?1:0,.24f)
                .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(gameObject)
                .OnComplete(()=>{sidebarTween=null;if(backdrop)backdrop.DeferViewportResize=warehouseTween!=null;ApplySidebar();});
            QueueRefresh();
        }
        void ApplySidebar()
        {
            if(!root||!backdrop)return;
            root.anchorMax=new Vector2((1+warehouseProgress)/6f,1);
            root.anchoredPosition=new Vector2(-root.rect.width*sidebarProgress,0);
            float left=Mathf.Clamp01(1-sidebarProgress)*(1+warehouseProgress)/6f;
            backdrop.SetViewport(new Rect(left,0,1-left,1));
            if(sidebarHandle)sidebarHandle.anchoredPosition=new Vector2(Mathf.Max(7,root.rect.width*(1-sidebarProgress)-37),8);
            if(pageInteraction){pageInteraction.blocksRaycasts=!sidebarCollapsed&&!modal;pageInteraction.interactable=!sidebarCollapsed&&!app.Busy&&!modal;}
        }
        void DisposeConstructionUi()
        {sidebarTween?.Kill();warehouseTween?.Kill();warehouseModeTween?.Kill();if(construction)construction.Changed-=ConstructionChanged;if(backdrop)backdrop.DeferViewportResize=false;}
    }
}
