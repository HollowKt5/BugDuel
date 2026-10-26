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
        void DrawMarket()
        {
            float width=root.rect.width,height=root.rect.height;
            var w=MBox("MarketWindow",root,0,0,width,height,CampaignRetroTheme.Window,CampaignRetroTheme.Frame,3);
            var title=MBox("MarketTitle",w,3,3,width-6,29,CampaignRetroTheme.Frame);
            Component<RetroBevel>(title).Configure(0,Color.clear,Color.clear,true,CampaignRetroTheme.TitleTop,CampaignRetroTheme.TitleBottom);
            Component<CampaignMarketArtwork>(Rect("WindowIcon",title,8,5,18,18)).SetKind(CampaignMarketArtwork.Kind.Rainbow);
            MText(title,"甲壳虫斗 · 市场",34,1,width-104,27,20,Color.white,true);
            MButton(title,"×",width-40,3,30,24,()=>app.Go(CampaignPage.Greenhouse),true,17,false,CampaignRetroTheme.Hex(0xd73720),Color.white);
            var menu=MBox("MenuBar",w,3,32,width-6,32,CampaignRetroTheme.Window);
            MButton(menu,"主题 ▾",294,1,100,30,()=>SetMarketThemeOpen(!marketThemeOpen),true,17,true);
            MButton(menu,"明暗",402,1,90,30,()=>SetMarketMode(!CampaignRetroTheme.Dark),true,17,true);
            MButton(menu,"温室",500,1,100,30,()=>app.Go(CampaignPage.Greenhouse),true,17,true);
            MButton(menu,"邮箱",608,1,100,30,OpenMailbox,true,17,true);
            MButton(menu,"仓库",716,1,100,30,OpenWarehouse,true,17,true);
            MButton(menu,"标本室",824,1,116,30,()=>app.Go(CampaignPage.SpecimenRoom),true,17,true);
            MBox("MenuDivider",menu,0,30,width-6,2,CampaignRetroTheme.DarkEdge);
            var tabs=MBox("Tabs",w,3,64,width-6,36,CampaignRetroTheme.Window);
            MBox("TabRule",tabs,0,34,width-6,2,CampaignRetroTheme.LightEdge);
            float tabWidth=(width-460)/4;
            MarketTab(tabs,"商店","6大分类",0,tabWidth,0);
            MarketTab(tabs,"交易市场",$"{Session.ListedCount} 帖",tabWidth,tabWidth,1);
            MarketTab(tabs,"拍卖",$"{State.marketAuctions.Count(a=>!a.closed)} 场",tabWidth*2,tabWidth,2);
            MarketTab(tabs,"出售","即时回收",tabWidth*3,tabWidth,3);
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
        void MarketTab(Transform parent,string title,string count,float x,float width,int index)
        {
            bool selected=marketTab==index;
            var tab=MButton(parent,title,x+3,selected?3:6,width-6,selected?33:30,()=>{marketTab=index;SetMarketThemeOpen(false);QueueRefresh();},true,20,true,
                selected?CampaignRetroTheme.Window:MColor(0xd8d4c4,0x1a1e22),selected?CampaignRetroTheme.Ink:CampaignRetroTheme.Muted);
            Component<RetroBevel>(tab.transform).Configure(2,CampaignRetroTheme.LightEdge,CampaignRetroTheme.DarkEdge);
            var label=tab.GetComponentInChildren<TMP_Text>();label.rectTransform.anchoredPosition=new Vector2(14,0);
            label.rectTransform.sizeDelta=new Vector2(width-112,30);label.alignment=TextAlignmentOptions.MidlineLeft;label.fontStyle=selected?FontStyles.Bold:FontStyles.Normal;
            var badge=MBox("TabCount",tab.transform,width-92,8,74,20,CampaignRetroTheme.Hex(index==0?0x316ac5u:0xcc6600u));
            MText(badge,count,3,0,68,20,14,Color.white).alignment=TextAlignmentOptions.Center;
            if(selected)MBox("OpenTabEdge",tab.transform,2,31,width-10,3,CampaignRetroTheme.Window);
        }
        void DrawMarketShop(RectTransform window)
        {
            float width=window.rect.width,height=window.rect.height-100;
            var categories=MBox("CategoryTree",window,3,100,280,height-3,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,2);
            Component<RetroBevel>(categories).Configure(2,CampaignRetroTheme.DarkEdge,CampaignRetroTheme.LightEdge);
            var heading=MBox("TreeHeading",categories,2,2,276,32,CampaignRetroTheme.Frame);
            Component<RetroBevel>(heading).Configure(0,Color.clear,Color.clear,true,CampaignRetroTheme.TitleTop,CampaignRetroTheme.TitleBottom);
            MText(heading,"甲虫与用品单类目录",8,3,260,26,18,Color.white,true);
            for(int i=0;i<marketCategories.Length;i++)
            {
                int category=i;bool selected=marketCategory==i;
                var node=MButton(categories,marketCategories[i],7,43+i*52,266,46,()=>{marketCategory=category;QueueRefresh();},true,19,true,
                    selected?CampaignRetroTheme.Hex(0x316ac5):CampaignRetroTheme.Panel,selected?Color.white:CampaignRetroTheme.Ink);
                var label=node.GetComponentInChildren<TMP_Text>();label.rectTransform.anchoredPosition=new Vector2(31,0);
                label.rectTransform.sizeDelta=new Vector2(195,46);label.alignment=TextAlignmentOptions.MidlineLeft;
                var line=selected?Color.white:CampaignRetroTheme.Muted;
                MBox("TreeBranch",node.transform,10,22,14,1,line);MBox("TreeTrunk",node.transform,10,0,1,i==marketCategories.Length-1?23:46,line);
                MText(node.transform,$"({MarketItems(i).Count})",230,0,32,46,16,selected?Color.white:CampaignRetroTheme.Muted).alignment=TextAlignmentOptions.MidlineRight;
            }
            float goodsWidth=width-292;
            var goods=MBox("Goods",window,289,100,goodsWidth,height-3,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,2);
            Component<RetroBevel>(goods).Configure(2,CampaignRetroTheme.DarkEdge,CampaignRetroTheme.LightEdge);
            var items=MarketItems(marketCategory);
            var header=MBox("GoodsHeader",goods,2,2,goodsWidth-4,36,MColor(0xece9d8,0x23272d));
            MText(header,marketCategories[marketCategory],12,3,goodsWidth-162,30,21,CampaignRetroTheme.Accent,true);
            MText(header,$"{items.Count} 件",goodsWidth-140,3,122,30,18,CampaignRetroTheme.Muted).alignment=TextAlignmentOptions.MidlineRight;
            var content=MarketScroll(goods,2,38,goodsWidth-4,height-74,CampaignRetroTheme.Grid);
            int columns=Mathf.Max(1,Mathf.FloorToInt((goodsWidth-29)/400));
            float cardWidth=(goodsWidth-45-(columns-1)*10)/columns;
            var grid=Component<UI.GridLayoutGroup>(content);grid.constraint=UI.GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=columns;
            grid.cellSize=new Vector2(cardWidth,230);grid.spacing=new Vector2(10,10);grid.padding=new RectOffset(12,12,12,12);grid.childAlignment=TextAnchor.UpperLeft;
            Component<UI.ContentSizeFitter>(content).verticalFit=UI.ContentSizeFitter.FitMode.PreferredSize;
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
                    items.Add(SupplyMarketItem(CampaignItems.Food(i),"成虫食物","单份食物 · 领取后投喂",(CampaignMarketArtwork.Kind)((int)CampaignMarketArtwork.Kind.Potato+i)));
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
                    items.Add(SupplyMarketItem(item.id,"养殖箱景观",item.description,DecorationArtwork(item.id)));
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
            return new MarketItem{itemId=id,category=category,title=CampaignItems.Name(id),species=category,size="按件订购 · 邮箱领取",parameter=parameter,price=$"{Session.SupplyBuyPrice(id):N0} G",artwork=artwork,actionLabel="选择数量订购",enabled=State.coins>=Session.SupplyBuyPrice(id)};
        }
        CampaignMarketArtwork.Kind BeetleMarketArtwork(CampaignBeetle b)
        {
            if(b.speciesId=="rainbow")return CampaignMarketArtwork.Kind.Rainbow;
            return Session.Catalog.Species(b.speciesId).name.Contains("锹")||new[]{"flat","red","giant-stag","palawan","giraffe"}.Contains(b.speciesId)?CampaignMarketArtwork.Kind.Stag:CampaignMarketArtwork.Kind.Horn;
        }
        void DrawMarketCard(Transform parent,MarketItem item,float width)
        {
            var card=MBox("GoodsCard",parent,0,0,width,230,MColor(0xffffff,0x1f2429),CampaignRetroTheme.Border,1);
            Component<RetroBevel>(card).Configure(2,CampaignRetroTheme.LightEdge,CampaignRetroTheme.DarkEdge);
            var shadow=Component<UI.Shadow>(card);shadow.effectColor=new Color(0,0,0,CampaignRetroTheme.Dark ? .32f : .18f);shadow.effectDistance=new Vector2(2,-2);
            var header=MBox("CardHeader",card,1,1,width-2,29,MColor(0xece9d8,0x282e35));
            MText(header,item.category,10,3,width-20,23,17,CampaignRetroTheme.Accent,true);
            var thumb=MBox("Thumbnail",card,12,42,94,94,CampaignRetroTheme.Hex(0x111a24),CampaignRetroTheme.DarkEdge,1);
            Component<RetroBevel>(thumb).Configure(2,CampaignRetroTheme.DarkEdge,CampaignRetroTheme.LightEdge);
            Component<CampaignMarketArtwork>(Rect("Artwork",thumb,2,2,90,90)).SetKind(item.artwork);
            MText(card,item.title,118,40,width-130,52,21,CampaignRetroTheme.Ink,true);
            MText(card,item.size,118,98,width-130,42,18);
            MText(card,item.parameter,12,142,width-24,40,18,CampaignRetroTheme.Muted);
            var price=MBox("PriceRow",card,1,187,width-2,42,MColor(0xf7f7f9,0x252b31),MColor(0xe5e5e5,0x374151),1);
            Component<RetroBevel>(price).Configure(1,CampaignRetroTheme.LightEdge,CampaignRetroTheme.DarkEdge);
            MText(price,item.price,10,5,width-188,32,24,CampaignRetroTheme.Price,true);
            MButton(price,"查看 / 订购",width-170,5,158,32,()=>OpenMarketItem(item),true,18);
        }
        void DrawMarketBbs(RectTransform window)
        {
            var bbs=MBox("TradingBBS",window,11,100,1562,773,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,2);var banner=MBox("BBSBanner",bbs,2,2,1558,37,CampaignRetroTheme.Frame);
            MText(banner,"卖方："+Session.PlayerSignature+" · 挂牌免费 / 成交收取 5% 手续费",10,7,820,24,13,Color.white,true);MText(banner,$"挂牌展柜 {Session.ListedCount}/{State.cabinet}",985,8,160,22,11,Color.white);
            MButton(banner,"+ 发表转让帖",1370,7,174,25,OpenMarketListing);MButton(banner,State.cabinet==3?$"展柜扩展包 {Session.Catalog.cabinetUpgrade:N0} G":"展柜已满级",1150,7,210,25,()=>app.Execute(s=>s.ExpandCabinet()),!Session.CabinetOrdered);
            var header=MBox("BBSColumns",bbs,2,39,1558,28,MColor(0xece9d8,0x252a30),MColor(0xd0ccbe,0x181b1f),1);string[] titles={"性质","转让收藏 / 买家报价","出虫玩家","状态","估价","操作"};float[] xs={8,82,1058,1178,1278,1400};float[] widths={68,968,112,92,114,150};for(int i=0;i<titles.Length;i++)MText(header,titles[i],xs[i],5,widths[i],19,12);
            var content=MarketScroll(bbs,2,67,1558,677,CampaignRetroTheme.Panel);var layout=Component<UI.VerticalLayoutGroup>(content);layout.spacing=0;layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;Component<UI.ContentSizeFitter>(content).verticalFit=UI.ContentSizeFitter.FitMode.PreferredSize;
            int row=0;
            foreach(var beetle in State.beetles.Where(b=>b.listed))
            {
                var b=beetle;var offers=State.offers.Where(o=>o.beetleId==b.id).ToArray();var listing=MarketBbsRow(content,ref row,63);MarketBadge(listing,"[活体转让]",false);
                MText(listing,$"【挂牌】{CampaignLocalization.Name(b)}  {b.length:0.0}mm {(b.female?"雌虫":"雄虫")}",82,9,960,22,12,CampaignRetroTheme.Ink,true);MText(listing,$"{string.Join(" / ",b.genes.Select(g=>g.id+new string('★',g.stars)))}   ·   {offers.Length} 条有效报价",82,34,960,20,11,CampaignRetroTheme.Muted);MText(listing,Session.PlayerSignature,1058,20,112,23,12);MText(listing,"本周挂牌",1178,20,92,23,11);MText(listing,$"{Session.Asking(b):N0} G",1278,20,114,23,12,CampaignRetroTheme.Price,true);MButton(listing,"查看 / 成交",1400,19,134,25,()=>OpenMarketOffers(b.id));
            }
            foreach(var specimen in State.specimens.Where(p=>p.listed))
            {
                var p=specimen;var offers=State.specimenOffers.Where(o=>o.specimenId==p.id).ToArray();var listing=MarketBbsRow(content,ref row,63);MarketBadge(listing,"[标本转让]",false);
                MText(listing,$"【装框标本】{CampaignDisplayText.SpecimenName(p)}  "+(p.beetle.stage<CampaignLifeStage.DormantAdult?CampaignSession.StageName(p.beetle.stage):$"{p.beetle.length:0.0} mm"),82,9,960,22,12,CampaignRetroTheme.Ink,true);MText(listing,$"{CampaignDisplayText.SpecimenSpecies(p)} · {CampaignDisplayText.SpecimenDate(p)} · {offers.Length} 条有效报价",82,34,960,20,11,CampaignRetroTheme.Muted);MText(listing,Session.PlayerSignature,1058,20,112,23,12);MText(listing,"本周挂牌",1178,20,92,23,11);MText(listing,$"{Session.SpecimenAsking(p):N0} G",1278,20,114,23,12,CampaignRetroTheme.Price,true);MButton(listing,"查看 / 成交",1400,19,134,25,()=>OpenMarketSpecimenOffers(p.id));
            }
            if(row==0){var empty=MarketBbsRow(content,ref row,85);MText(empty,"还没有转让帖。点击「发表转让帖」选择健康成虫或收藏标本；有报价后，由你决定是否成交。",18,20,1498,25,12,CampaignRetroTheme.Muted);MText(empty,"需要立即变现时，可在「售出收藏与库存」中按明确回收价出售。",18,48,1498,23,11,CampaignRetroTheme.Muted);}
            notice=MStatus(bbs,10,748,1542,20);Canvas.ForceUpdateCanvases();
        }
        RectTransform MarketBbsRow(RectTransform parent,ref int index,float height)
        {
            var row=MBox("BBSRow",parent,0,0,1540,height,index++%2==0?CampaignRetroTheme.Panel:MColor(0xfbfbfb,0x1b1f24));Component<UI.LayoutElement>(row).preferredHeight=height;MBox("RowDivider",row,0,height-1,1540,1,MColor(0xeeeeee,0x2b3036));return row;
        }
        void MarketBadge(Transform parent,string title,bool auction)
        {
            var b=MBox("PostBadge",parent,8,20,67,19,CampaignRetroTheme.Hex(auction?0xffeeddu:0xe6f7ffu),CampaignRetroTheme.Hex(auction?0xff9933u:0x91d5ffu),1);MText(b,title,2,1,63,17,10,CampaignRetroTheme.Hex(auction?0xcc3300u:0x0050b3u)).alignment=TextAlignmentOptions.Center;
        }
    }
}
