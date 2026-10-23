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
        // Pixel measurements follow retro.html at the shared 1600 x 900 reference resolution.
        // Transactions continue to use the existing campaign domain, never the HTML demo data.
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
        void DrawMarket()
        {
            DrawMarketDesktop();
            var w=MBox("MarketWindow",root,8,8,1584,884,CampaignRetroTheme.Window,CampaignRetroTheme.Frame,3);
            MTitle(w,"甲虫秘境 2003 - 四季甲虫商店与集市 ["+CampaignRetroTheme.SeasonName+"]",1578);
            MButton(w,"×",1551,7,20,20,()=>app.Go(CampaignPage.Greenhouse),true,11,false,CampaignRetroTheme.Hex(0xd73720),Color.white);
            var menu=MBox("MenuBar",w,3,31,1578,26,CampaignRetroTheme.Window);
            MButton(menu,"主题(T) ▼",6,2,76,22,()=>SetMarketThemeOpen(!marketThemeOpen),true,12,true);
            MButton(menu,"明暗(M)",88,2,76,22,()=>SetMarketMode(!CampaignRetroTheme.Dark),true,12,true);
            MButton(menu,"帮助(H)",170,2,70,22,OpenMarketHelp,true,12,true);
            MButton(menu,"返回温室",246,2,76,22,()=>app.Go(CampaignPage.Greenhouse),true,12,true);
            MButton(menu,"邮箱",328,2,84,22,OpenMailbox,true,12,true);
            MButton(menu,"仓库",420,2,84,22,OpenWarehouse,true,12,true);
            MButton(menu,"标本室",512,2,84,22,()=>app.Go(CampaignPage.SpecimenRoom),true,12,true);
            MText(menu,$"第 {State.week:00} 周  |  当前主题: {CampaignRetroTheme.SeasonName} · {(CampaignRetroTheme.Dark?"暗色":"明亮")}模式",704,3,580,20,11,CampaignRetroTheme.Muted).alignment=TextAlignmentOptions.MidlineRight;
            MBox("MenuDivider",menu,0,24,1578,2,MColor(0xd0ccbe,0x181b1f));
            var tabs=MBox("Tabs",w,3,57,1578,35,CampaignRetroTheme.Window);MBox("TabRule",tabs,0,33,1578,2,CampaignRetroTheme.LightEdge);
            MarketTab(tabs,"甲虫商店 (自营与用品)","6大分类",8,236,0);
            MarketTab(tabs,"甲虫市场 (交易 BBS)",$"{Session.ListedCount} 帖",247,238,1);
            MarketTab(tabs,"公开拍卖 (自由竞价)",$"{State.marketAuctions.Count(a=>!a.closed)} 场",488,238,2);
            MarketTab(tabs,"售出收藏与库存","即时回收",729,238,3);
            DrawCalendarClock(tabs,1000,0,550,33,true);
            switch(marketTab){case 0:DrawMarketShop(w);break;case 1:DrawMarketBbs(w);break;case 2:DrawMarketAuctions(w);break;default:DrawMarketSell(w);break;}
            if(marketThemeOpen || marketThemeMotion && marketThemeMotion.IsAnimating)DrawMarketThemeMenu();
        }
        void DrawMarketDesktop()
        {
            int s=CampaignRetroTheme.Season;
            uint[] a=CampaignRetroTheme.Dark?new uint[]{0x064e3b,0x78350f,0x7c2d12,0x0c4a6e}:new uint[]{0x10b981,0xf59e0b,0xc2410c,0xffffff};
            uint[] b=CampaignRetroTheme.Dark?new uint[]{0x062817,0x261603,0x260d05,0x0b1320}:new uint[]{0x6ee7b7,0xfef08a,0xfed7aa,0xf1f5f9};
            uint[] c=CampaignRetroTheme.Dark?new uint[]{0x021a0d,0x150b01,0x170702,0x030712}:new uint[]{0x059669,0xd97706,0xea580c,0xe2e8f0};
            float split=CampaignRetroTheme.Dark||s==3?360:315;
            var top=MBox("DesktopTop",root,0,0,1600,split,Color.white);Component<RetroBevel>(top).Configure(0,Color.clear,Color.clear,true,CampaignRetroTheme.Hex(a[s]),CampaignRetroTheme.Hex(b[s]));
            var lower=MBox("DesktopLower",root,0,split,1600,900-split,Color.white);Component<RetroBevel>(lower).Configure(0,Color.clear,Color.clear,true,CampaignRetroTheme.Hex(b[s]),CampaignRetroTheme.Hex(c[s]));
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
            var tab=MButton(parent,title,x,selected?5:7,width,selected?30:27,()=>{marketTab=index;SetMarketThemeOpen(false);QueueRefresh();},true,12,true,selected?CampaignRetroTheme.Window:MColor(0xd8d4c4,0x1a1e22),selected?CampaignRetroTheme.Ink:CampaignRetroTheme.Muted);
            var edge=selected?CampaignRetroTheme.LightEdge:MColor(0x999484,0x374151);Component<RetroBevel>(tab.transform).Configure(selected?2:1,edge,edge);
            var text=tab.GetComponentInChildren<TMP_Text>();text.rectTransform.anchoredPosition=new Vector2(18,0);text.rectTransform.sizeDelta=new Vector2(width-75,27);text.alignment=TextAlignmentOptions.MidlineLeft;text.fontStyle=selected?FontStyles.Bold:FontStyles.Normal;
            var badge=MBox("TabCount",tab.transform,width-61,8,53,14,CampaignRetroTheme.Hex(index==0?0x316ac5u:0xcc6600u));MText(badge,count,2,0,49,14,10,Color.white).alignment=TextAlignmentOptions.Center;
            if(selected)MBox("OpenTabEdge",tab.transform,2,28,width-4,3,CampaignRetroTheme.Window);
        }
        void DrawMarketShop(RectTransform window)
        {
            var treePanel=MBox("CategoryTree",window,11,100,250,773,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,2);
            var heading=MBox("TreeHeading",treePanel,2,2,246,23,CampaignRetroTheme.Frame);Component<RetroBevel>(heading).Configure(0,Color.clear,Color.clear,true,CampaignRetroTheme.TitleTop,CampaignRetroTheme.TitleBottom);
            MText(heading,"甲虫与用品单类目录",6,2,174,19,11,Color.white,true);MText(heading,"[6大分类]",184,2,60,19,11,Color.white);
            for(int i=0;i<marketCategories.Length;i++)
            {
                int category=i;bool selected=marketCategory==i;
                var node=MButton(treePanel,(i==marketCategories.Length-1?"└─ ":"├─ ")+marketCategories[i],8,31+i*26,234,24,()=>{marketCategory=category;QueueRefresh();},true,12,true,selected?CampaignRetroTheme.Hex(0x316ac5):CampaignRetroTheme.Panel,selected?Color.white:CampaignRetroTheme.Ink);
                var t=node.GetComponentInChildren<TMP_Text>();t.alignment=TextAlignmentOptions.MidlineLeft;t.rectTransform.sizeDelta=new Vector2(204,24);t.fontStyle=FontStyles.Bold;
                MText(node.transform,$"({MarketItems(i).Count})",209,2,25,20,10,selected?CampaignRetroTheme.Hex(0xe0e8ff):CampaignRetroTheme.Muted).alignment=TextAlignmentOptions.MidlineRight;
            }
            var tip=Rect("SeasonTips",treePanel,2,665,246,106);
            string[] tips={"气温逐渐转暖，保持环境清洁。\n检查养殖箱湿度，为成虫放置红薯或水果；幼虫使用对应饲料瓶。","高温期注意遮阴通风，检查温湿度。及时更换水果，观察甲虫是否正常取食。","温差加大，保持箱内环境稳定。\n留意新成虫状态，提供适量食物与攀爬支撑。","保持养殖箱温湿度稳定。\n观察进食与活动状态，发现翻倒及时扶正。"};
            MText(tip,CampaignRetroTheme.SeasonName.Substring(0,2)+"甲虫养殖贴士：",6,6,234,17,11,CampaignRetroTheme.Muted,true);MText(tip,tips[CampaignRetroTheme.Season],6,25,234,74,11,CampaignRetroTheme.Muted);
            var goods=MBox("Goods",window,269,100,1304,773,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,2);
            notice=MStatus(goods,10,5,1284,19);
            var items=MarketItems(marketCategory);var header=MBox("GoodsHeader",goods,2,27,1300,25,MColor(0xf0f0f0,0x23272d));
            MText(header,"当前类别："+marketCategories[marketCategory],8,3,770,19,11,CampaignRetroTheme.Muted,true);MText(header,$"类目商品: {items.Count} 件 (单类浏览)",930,3,362,19,11,CampaignRetroTheme.Muted).alignment=TextAlignmentOptions.MidlineRight;
            var content=MarketScroll(goods,2,52,1300,719,CampaignRetroTheme.Grid);
            var grid=Component<UI.GridLayoutGroup>(content);grid.constraint=UI.GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=3;grid.cellSize=new Vector2(414,173);grid.spacing=new Vector2(10,10);grid.padding=new RectOffset(10,10,10,10);grid.childAlignment=TextAnchor.UpperLeft;
            Component<UI.ContentSizeFitter>(content).verticalFit=UI.ContentSizeFitter.FitMode.PreferredSize;
            foreach(var item in items)DrawMarketCard(content,item,414);
            if(items.Count==0)
            {
                var empty=MBox("NoStock",content,0,0,414,173,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,1);
                MText(empty,"本周该分类暂无现货",16,18,380,24,12,CampaignRetroTheme.Ink,true);MText(empty,"现货每周刷新。可先查看其他分类，\n或派遣捕虫队寻找新的甲虫。",16,52,380,48,11,CampaignRetroTheme.Muted);MButton(empty,"查看捕虫队",16,120,130,27,()=>app.Go(CampaignPage.Catching));
            }
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
                    items.Add(new MarketItem{category=marketCategories[category],title=CampaignLocalization.Name(b)+" · "+CampaignSession.StageName(b.stage),species=species.name,size=(b.stage<CampaignLifeStage.DormantAdult?"预估成虫 ":"")+$"{b.length:0.0} mm / {(b.female?"雌":"雄")}",parameter=$"阶段进度 {Session.StageProgress(b):P0}",price=$"{Session.PurchasePrice(b):N0} G",artwork=BeetleMarketArtwork(b),enabled=State.coins>=Session.PurchasePrice(b),action=()=>app.Execute(s=>s.Buy(b.id))});
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
            var card=MBox("GoodsCard",parent,0,0,width,173,MColor(0xffffff,0x1f2429),CampaignRetroTheme.Border,1);var header=MBox("CardHeader",card,1,1,width-2,24,MColor(0xece9d8,0x282e35));
            MText(header,"【"+item.category+"】",8,3,width-16,18,11,CampaignRetroTheme.Accent,true);
            var thumb=MBox("Thumbnail",card,9,33,95,95,CampaignRetroTheme.Hex(0x111a24),CampaignRetroTheme.Hex(0x808080),1);Component<CampaignMarketArtwork>(Rect("Artwork",thumb,1,1,93,93)).SetKind(item.artwork);
            MText(card,item.title,114,32,width-123,23,12,CampaignRetroTheme.Ink,true);
            string[] labels={"种类：","规格：","参数："};string[] values={item.species,item.size,item.parameter};for(int i=0;i<3;i++){MText(card,labels[i],114,59+i*21,48,20,11,CampaignRetroTheme.Muted);MText(card,values[i],162,59+i*21,width-171,20,11);}
            var price=MBox("PriceRow",card,1,137,width-2,35,MColor(0xf7f7f9,0x252b31),MColor(0xe5e5e5,0x374151),1);MText(price,"零售单价：",8,8,60,20,11,CampaignRetroTheme.Muted);MText(price,item.price,69,5,width-212,25,15,CampaignRetroTheme.Price,true);MButton(price,"查看参数档案...",width-137,6,126,23,()=>OpenMarketItem(item));
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
