using System;
using System.Collections.Generic;
using System.Linq;
using BugDuel.Campaign;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UI=UnityEngine.UI;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        int marketTab,marketCategory;
        bool marketThemeOpen;
        BeetlePanelMotion marketThemeMotion;
        UI.Button marketThemeDismiss;
        readonly string[] marketCategories={"兜虫及其他活体","锹甲总科活体","食物与养殖补给","捕虫用品与扩展","装框标本收藏","养殖箱景观"};
        sealed class MarketItem
        {
            public string category,title,species,size,parameter,price,actionLabel="确定订购",itemId;
            public CampaignMarketArtwork.Kind artwork;
            public Action action;
            public bool enabled=true;
        }
        public static bool RequestChampionAuction;
        bool championAuction;
        Rect MarketWindowRect
        {
            get
            {
                var layout=Layout.market;
                float x=Mathf.Clamp(layout.topLeft.x,0,Mathf.Max(0,root.rect.width-1280));
                float y=Mathf.Clamp(layout.topLeft.y,0,Mathf.Max(0,root.rect.height-600));
                return new Rect(x,y,Mathf.Max(1280,root.rect.width-x-layout.bottomRight.x),Mathf.Max(600,root.rect.height-y-layout.bottomRight.y));
            }
        }
        float MarketMenuStart=>Mathf.Max(8,(MarketWindowRect.width-652)/2);
        void DrawMarket()
        {
            var windowRect=MarketWindowRect;
            float width=windowRect.width,height=windowRect.height;
            var w=MBox("MarketWindow",root,windowRect.x,windowRect.y,width,height,CampaignRetroTheme.Window,CampaignRetroTheme.Frame,3);
            var title=MBox("MarketTitle",w,3,3,width-6,29,CampaignRetroTheme.Frame);
            Component<RetroBevel>(title).Configure(1,CampaignRetroTheme.TitleHighlight,CampaignRetroTheme.TitleShadow,true,CampaignRetroTheme.TitleTop,CampaignRetroTheme.TitleBottom);
            Component<CampaignMarketArtwork>(Rect("WindowIcon",title,8,5,18,18)).SetKind(CampaignMarketArtwork.Kind.Rainbow);
            MText(title,"甲壳虫斗 · 市场",34,1,width-104,27,20,CampaignRetroTheme.TitleInk,true);
            MButton(title,"×",width-40,3,30,24,()=>app.Go(CampaignPage.Greenhouse),true,17,false,CampaignRetroTheme.Danger,Color.white);
            var menu=MBox("MenuBar",w,3,32,width-6,32,CampaignRetroTheme.Window);
            float menuX=MarketMenuStart;
            MButton(menu,"主题 ▾",menuX,1,100,30,()=>SetMarketThemeOpen(!marketThemeOpen),true,17,true);
            MButton(menu,"明暗",menuX+108,1,90,30,()=>SetMarketMode(!CampaignRetroTheme.Dark),true,17,true);
            MButton(menu,"温室",menuX+206,1,100,30,()=>app.Go(CampaignPage.Greenhouse),true,17,true);
            MButton(menu,"邮箱",menuX+314,1,100,30,OpenMailbox,true,17,true);
            MButton(menu,"仓库",menuX+422,1,100,30,OpenWarehouse,true,17,true);
            MButton(menu,"标本室",menuX+530,1,116,30,()=>app.Go(CampaignPage.SpecimenRoom),true,17,true);
            MBox("MenuDivider",menu,0,30,width-6,2,CampaignRetroTheme.Rule);
            var tabs=MBox("Tabs",w,3,64,width-6,36,CampaignRetroTheme.Window);
            MBox("TabRule",tabs,0,34,width-6,2,CampaignRetroTheme.Rule);
            float tabWidth=(width-460)/4;
            MarketTab(tabs,"商店",0,tabWidth,0);
            MarketTab(tabs,"交易市场",tabWidth,tabWidth,1);
            MarketTab(tabs,"拍卖",tabWidth*2,tabWidth,2);
            MarketTab(tabs,"出售",tabWidth*3,tabWidth,3);
            DrawCalendarClock(tabs,width-450,0,442,35,true);
            switch(marketTab){case 0:DrawMarketShop(w);break;case 1:DrawMarketBbs(w);break;case 2:DrawMarketAuctions(w);break;default:DrawMarketSell(w);break;}
            if(marketThemeOpen || marketThemeMotion && marketThemeMotion.IsAnimating)DrawMarketThemeMenu();
        }
        bool HandleMarketShortcuts()
        {
            if(page!=CampaignPage.Market||modal)return false;
            if(marketThemeOpen && app.Controls["Back"].WasPressedThisFrame()){SetMarketThemeOpen(false);return true;}
            if(Keyboard.current==null)return false;
            var k=Keyboard.current;
            if(marketThemeOpen&&k.escapeKey.wasPressedThisFrame){SetMarketThemeOpen(false);return true;}
            if(!k.leftAltKey.isPressed&&!k.rightAltKey.isPressed)return false;
            if(k.tKey.wasPressedThisFrame){SetMarketThemeOpen(!marketThemeOpen);return true;}
            if(k.mKey.wasPressedThisFrame){SetMarketMode(!CampaignRetroTheme.Dark);return true;}
            if(k.hKey.wasPressedThisFrame){OpenMarketHelp();return true;}return false;
        }
        void MarketTab(Transform parent,string title,float x,float width,int index)
        {
            bool selected=marketTab==index;
            var tab=MarketChoice(parent,title,x+3,selected?3:6,width-6,selected?33:30,selected,()=>{marketTab=index;SetMarketThemeOpen(false);QueueRefresh();});
            if(selected)MBox("OpenTabEdge",tab.transform,2,31,width-10,3,CampaignRetroTheme.Window);
        }
        void DrawMarketShop(RectTransform window)
        {
            var layout=Layout.market;
            float width=window.rect.width,height=window.rect.height-100;
            float categoryWidth=layout.categoryWidth;
            var categories=MBox("CategoryTree",window,3,100,categoryWidth,height-3,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,2);
            Component<RetroBevel>(categories).Configure(2,CampaignRetroTheme.DarkEdge,CampaignRetroTheme.LightEdge);
            var heading=MBox("TreeHeading",categories,2,2,categoryWidth-4,32,CampaignRetroTheme.Frame);
            Component<RetroBevel>(heading).Configure(1,CampaignRetroTheme.TitleHighlight,CampaignRetroTheme.TitleShadow,true,CampaignRetroTheme.TitleTop,CampaignRetroTheme.TitleBottom);
            MText(heading,"商品分类",8,3,categoryWidth-20,26,18,CampaignRetroTheme.TitleInk,true);
            for(int i=0;i<marketCategories.Length;i++)
            {
                int category=i;bool selected=marketCategory==i;
                var node=MButton(categories,marketCategories[i],7,43+i*52,categoryWidth-14,46,()=>{marketCategory=category;QueueRefresh();},true,19,true,
                    selected?CampaignRetroTheme.Selection:CampaignRetroTheme.Panel,selected?CampaignRetroTheme.SelectionInk:CampaignRetroTheme.Ink);
                var label=node.GetComponentInChildren<TMP_Text>();label.rectTransform.anchoredPosition=new Vector2(14,0);
                label.rectTransform.sizeDelta=new Vector2(categoryWidth-42,46);label.alignment=TextAlignmentOptions.MidlineLeft;
            }
            float goodsWidth=width-categoryWidth-12;
            var goods=MBox("Goods",window,categoryWidth+9,100,goodsWidth,height-3,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,2);
            Component<RetroBevel>(goods).Configure(2,CampaignRetroTheme.DarkEdge,CampaignRetroTheme.LightEdge);
            var items=MarketItems(marketCategory);
            var header=MBox("GoodsHeader",goods,2,2,goodsWidth-4,36,CampaignRetroTheme.Window);
            MText(header,marketCategories[marketCategory],12,3,goodsWidth-162,30,21,CampaignRetroTheme.Accent,true);
            MText(header,$"{items.Count} 件",goodsWidth-140,3,122,30,18,CampaignRetroTheme.Muted).alignment=TextAlignmentOptions.MidlineRight;
            var content=MarketScroll(goods,2,38,goodsWidth-4,height-74,CampaignRetroTheme.Grid);
            float cardWidth=MarketCardGrid(content,layout.CardHeight);
            foreach(var item in items)DrawMarketCard(content,item,cardWidth);
            if(items.Count==0)MText(content,"本周暂无现货",20,20,cardWidth,60,24,CampaignRetroTheme.Muted);
            var status=MBox("ShopStatus",goods,2,height-34,goodsWidth-4,29,CampaignRetroTheme.Window);
            notice=MStatus(status,10,3,goodsWidth-24,24,17);
            Canvas.ForceUpdateCanvases();
        }
        List<MarketItem> MarketItems(int category)
        {
            var items=new List<MarketItem>();
            if(category<2)
            {
                foreach(var stock in State.stock)
                {
                    var b=stock;var species=Session.Catalog.Species(b.speciesId);bool stag=species.name.Contains("锹")||new[]{"flat","red","giant-stag","rainbow","palawan","giraffe"}.Contains(species.id);if(stag!=(category==1))continue;
                    items.Add(new MarketItem{category=marketCategories[category],title=CampaignLocalization.Name(b),species=species.name,size=(b.stage<CampaignLifeStage.DormantAdult?"预估成虫 ":"")+$"{b.length:0.0} mm / {(b.female?"雌":"雄")}",parameter=$"阶段进度 {Session.StageProgress(b):P0}",price=$"{Session.PurchasePrice(b):N0} G",artwork=BeetleMarketArtwork(b),enabled=State.coins>=Session.PurchasePrice(b),action=()=>app.Execute(s=>s.Buy(b.id))});
                }
            }
            else if(category==2)
            {
                for(int i=0;i<3;i++)
                {
                    items.Add(SupplyMarketItem(CampaignItems.Food(i),"成虫食物","",(CampaignMarketArtwork.Kind)((int)CampaignMarketArtwork.Kind.Potato+i)));
                }
                foreach(string id in CampaignRearingItems.All)
                    items.Add(SupplyMarketItem(id,"阶段养殖用品",CampaignRearingItems.Description(id),SupplyArtwork(id)));
            }
            else if(category==3)
            {
                items.Add(SupplyMarketItem(CampaignItems.Lamp,"捕虫设备","派遣时大型率 +25%",CampaignMarketArtwork.Kind.Lamp));
                items.Add(SupplyMarketItem(CampaignItems.Sap,"捕虫诱饵","派遣时带回数量 +2",CampaignMarketArtwork.Kind.Sap));
                var cabinet=SupplyMarketItem(CampaignItems.Cabinet,"展柜扩展","活体与标本共用 · 3 → 6 帖",CampaignMarketArtwork.Kind.Container);cabinet.enabled=!Session.CabinetOrdered&&cabinet.enabled;cabinet.actionLabel=Session.CabinetOrdered?"已购买扩展包":"选择数量订购";items.Add(cabinet);
            }
            else if(category==5)
            {
                foreach(var item in CampaignDecorations.All)
                    items.Add(SupplyMarketItem(item.id,"养殖箱景观",$"{item.width}×{item.depth} 格",DecorationArtwork(item.id)));
            }
            else
            {
                foreach(var specimen in State.specimenStock)
                {
                    var p=specimen;items.Add(new MarketItem{category="装框标本",title=CampaignDisplayText.SpecimenName(p),species=CampaignDisplayText.SpecimenSpecies(p),size=(p.beetle.stage<CampaignLifeStage.DormantAdult?CampaignSession.StageName(p.beetle.stage):$"{p.beetle.length:0.0} mm")+$" / {(p.beetle.female?"雌":"雄")}",parameter=CampaignDisplayText.SpecimenDate(p),price=$"{Session.SpecimenPurchasePrice(p):N0} G",artwork=CampaignMarketArtwork.Kind.Specimen,enabled=State.coins>=Session.SpecimenPurchasePrice(p),action=()=>app.Execute(s=>s.BuySpecimen(p.id))});
                }
            }
            return items;
        }
        MarketItem SupplyMarketItem(string id,string category,string parameter,CampaignMarketArtwork.Kind artwork)
        {
            return new MarketItem{itemId=id,category=category,title=CampaignItems.Name(id),species=category,parameter=parameter,price=$"{Session.SupplyBuyPrice(id):N0} G",artwork=artwork,actionLabel="选择数量订购",enabled=State.coins>=Session.SupplyBuyPrice(id)};
        }
        CampaignMarketArtwork.Kind BeetleMarketArtwork(CampaignBeetle b)
        {
            if(b.speciesId=="rainbow")return CampaignMarketArtwork.Kind.Rainbow;
            return Session.Catalog.Species(b.speciesId).name.Contains("锹")||new[]{"flat","red","giant-stag","palawan","giraffe"}.Contains(b.speciesId)?CampaignMarketArtwork.Kind.Stag:CampaignMarketArtwork.Kind.Horn;
        }
        void DrawMarketCard(Transform parent,MarketItem item,float width)
        {
            var layout=Layout.market;
            float padding=layout.cardPadding,thumbSize=layout.thumbnailSize,thumbTop=30+padding,textX=padding*2+thumbSize;
            var card=MarketCardSurface("GoodsCard",parent,width,layout.CardHeight,out var header,out var price);
            MarketCardHeading(header,item.category);MarketCardArtwork(card,item.artwork,thumbTop);
            MText(card,item.title,textX,thumbTop-2,width-textX-padding,52,21,CampaignRetroTheme.Ink,true);
            if(!string.IsNullOrEmpty(item.size))MText(card,item.size,textX,thumbTop+56,width-textX-padding,42,18);
            if(!string.IsNullOrEmpty(item.parameter))MText(card,item.parameter,padding,Mathf.Max(thumbTop+96,thumbTop+thumbSize+2),width-padding*2,46,18,CampaignRetroTheme.Muted);
            MText(price,item.price,4,5,price.rect.width-176,32,24,CampaignRetroTheme.Price,true);
            MarketCardAction(price,"查看 / 订购",()=>OpenMarketItem(item));
        }
        void DrawMarketBbs(RectTransform window)
        {
            float width=window.rect.width-6,height=window.rect.height-103,inside=width-4;
            var bbs=MBox("TradingBBS",window,3,100,width,height,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,2);var banner=MBox("BBSBanner",bbs,2,2,inside,46,CampaignRetroTheme.Frame);
            Component<RetroBevel>(banner).Configure(1,CampaignRetroTheme.TitleHighlight,CampaignRetroTheme.TitleShadow,true,CampaignRetroTheme.TitleTop,CampaignRetroTheme.TitleBottom);
            MText(banner,Session.PlayerSignature,12,7,inside-540,32,20,CampaignRetroTheme.TitleInk,true);
            MButton(banner,"+ 发表转让帖",inside-190,5,178,36,OpenMarketListing,true,18);MButton(banner,State.cabinet==3?$"展柜扩展包 {Session.Catalog.cabinetUpgrade:N0} G":"展柜已满级",inside-508,5,304,36,()=>app.Execute(s=>s.ExpandCabinet()),!Session.CabinetOrdered,18);
            var content=MarketScroll(bbs,2,82,inside,height-111,CampaignRetroTheme.Panel);var layout=Component<UI.VerticalLayoutGroup>(content);layout.spacing=0;layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;Component<UI.ContentSizeFitter>(content).verticalFit=UI.ContentSizeFitter.FitMode.PreferredSize;
            float actionX=content.rect.width-188,priceX=actionX-160,statusX=priceX-110,ownerX=statusX-152,titleWidth=ownerX-138;
            var header=MBox("BBSColumns",bbs,2,48,inside,34,CampaignRetroTheme.Window,CampaignRetroTheme.Rule,1);string[] titles={"性质","转让收藏 / 买家报价","出虫玩家","状态","估价","操作"};float[] xs={8,126,ownerX,statusX,priceX,actionX};float[] widths={108,titleWidth,144,102,152,180};for(int i=0;i<titles.Length;i++)MText(header,titles[i],xs[i],5,widths[i],26,18);
            int row=0;
            foreach(var beetle in State.beetles.Where(b=>b.listed))
            {
                var b=beetle;var offers=State.offers.Where(o=>o.beetleId==b.id).ToArray();var listing=MarketBbsRow(content,ref row,88);MarketBadge(listing,"[活体转让]",false);
                MText(listing,$"【挂牌】{CampaignLocalization.Name(b)}  {b.length:0.0}mm {(b.female?"雌虫":"雄虫")}",126,8,titleWidth,30,20,CampaignRetroTheme.Ink,true);MText(listing,$"{string.Join(" / ",b.genes.Select(g=>g.id+new string('★',g.stars)))}   ·   {offers.Length} 条有效报价",126,46,titleWidth,28,18,CampaignRetroTheme.Muted);MText(listing,Session.PlayerSignature,ownerX,29,144,28,18);MText(listing,"本周挂牌",statusX,29,102,28,18);MText(listing,$"{Session.Asking(b):N0} G",priceX,29,152,28,18,CampaignRetroTheme.Price,true);MButton(listing,"查看 / 成交",actionX,25,176,36,()=>OpenMarketOffers(b.id),true,18);
            }
            foreach(var specimen in State.specimens.Where(p=>p.listed))
            {
                var p=specimen;var offers=State.specimenOffers.Where(o=>o.specimenId==p.id).ToArray();var listing=MarketBbsRow(content,ref row,88);MarketBadge(listing,"[标本转让]",false);
                MText(listing,$"【装框标本】{CampaignDisplayText.SpecimenName(p)}  "+(p.beetle.stage<CampaignLifeStage.DormantAdult?CampaignSession.StageName(p.beetle.stage):$"{p.beetle.length:0.0} mm"),126,8,titleWidth,30,20,CampaignRetroTheme.Ink,true);MText(listing,$"{CampaignDisplayText.SpecimenSpecies(p)} · {CampaignDisplayText.SpecimenDate(p)} · {offers.Length} 条有效报价",126,46,titleWidth,28,18,CampaignRetroTheme.Muted);MText(listing,Session.PlayerSignature,ownerX,29,144,28,18);MText(listing,"本周挂牌",statusX,29,102,28,18);MText(listing,$"{Session.SpecimenAsking(p):N0} G",priceX,29,152,28,18,CampaignRetroTheme.Price,true);MButton(listing,"查看 / 成交",actionX,25,176,36,()=>OpenMarketSpecimenOffers(p.id),true,18);
            }
            if(row==0){var empty=MarketBbsRow(content,ref row,65);MText(empty,"暂无挂牌。点击「发表转让帖」开始。",18,20,content.rect.width-36,30,20,CampaignRetroTheme.Muted);}
            notice=MStatus(bbs,10,height-25,width-20,20,17);Canvas.ForceUpdateCanvases();
        }
        RectTransform MarketBbsRow(RectTransform parent,ref int index,float height)
        {
            var row=MBox("BBSRow",parent,0,0,parent.rect.width,height,index++%2==0?CampaignRetroTheme.Panel:CampaignRetroTheme.SurfaceRaised);Component<UI.LayoutElement>(row).preferredHeight=height;MBox("RowDivider",row,0,height-1,parent.rect.width,1,CampaignRetroTheme.Rule);return row;
        }
        void MarketBadge(Transform parent,string title,bool auction)
        {
            var b=MBox("PostBadge",parent,8,28,108,30,CampaignRetroTheme.SurfaceInset,CampaignRetroTheme.Rule,1);MText(b,title,4,2,100,26,17,CampaignRetroTheme.Accent).alignment=TextAlignmentOptions.Center;
        }
    }
}
