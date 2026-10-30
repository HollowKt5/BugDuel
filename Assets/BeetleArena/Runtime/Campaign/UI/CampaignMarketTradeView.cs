using System;
using System.Linq;
using BugDuel.Campaign;
using TMPro;
using UnityEngine;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        int marketSellCategory;
        static readonly string[] TradableSupplies=new[]{"food-potato","food-banana","food-apple",CampaignItems.Sap,CampaignItems.Lamp,CampaignItems.Cabinet}.Concat(CampaignRearingItems.All).Concat(CampaignDecorations.All.Select(x=>x.id)).ToArray();

        void DrawMarketSell(RectTransform window)
        {
            float width=window.rect.width-6,height=window.rect.height-103,inside=width-4;
            var panel=MBox("InventorySales",window,3,100,width,height,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,2);
            var heading=MBox("SellHeading",panel,2,2,inside,40,CampaignRetroTheme.Window);
            MText(heading,"商店回收",12,3,inside-190,32,21,CampaignRetroTheme.Accent,true);
            MButton(heading,"打开仓库",inside-170,3,158,32,OpenWarehouse,true,18);
            string[] categories={"活体收藏","标本收藏","用品库存"};
            for(int i=0;i<categories.Length;i++)
            {
                int category=i;
                MarketChoice(panel,categories[i],12+i*185,51,175,36,marketSellCategory==i,()=>{marketSellCategory=category;QueueRefresh();});
            }
            MText(panel,"标本估价的 60% · 用品零售价的 40% · 活体按阶段估价",12,95,width-24,27,18,CampaignRetroTheme.Muted);
            var content=MarketScroll(panel,2,129,inside,height-158,CampaignRetroTheme.Panel);
            float actionX=content.rect.width-236,priceX=actionX-224,detailWidth=priceX-112;
            var layout=Component<UnityEngine.UI.VerticalLayoutGroup>(content);layout.spacing=0;layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
            Component<UnityEngine.UI.ContentSizeFitter>(content).verticalFit=UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            int rowIndex=0;
            if(marketSellCategory==0)
            {
                foreach(var beetle in State.beetles.Concat(State.storedBeetles))
                {
                    var b=beetle;bool stored=State.storedBeetles.Contains(b);
                    bool eligible=!b.dead&&!b.listed&&State.battle?.beetleId!=b.id&&State.auction?.beetleId!=b.id;
                    var row=MarketBbsRow(content,ref rowIndex,88);MarketRowArtwork(row,BeetleMarketArtwork(b));
                    MText(row,CampaignLocalization.Name(b)+"  ·  "+(b.stage<CampaignLifeStage.DormantAdult?"预估成虫 ":"")+$"{b.length:0.0} mm  ·  {(b.female?"雌虫":"雄虫")}",92,13,detailWidth,27,21,CampaignRetroTheme.Ink,true);
                    string status=b.dead?"已死亡 · 请回温室制作标本或遗弃":b.listed?"交易 BBS 挂牌中 · 撤回后可直接回收":!eligible?"比赛或冠军拍卖中 · 活动结束后可交易":CampaignSession.StageName(b.stage)+" · 可以立即出售";
                    MText(row,(stored?"仓库暂存":"温室入住")+" / "+status,92,48,detailWidth,26,18,CampaignRetroTheme.Muted);
                    MText(row,$"{Session.BeetleSellPrice(b):N0} G",priceX,29,195,31,19,CampaignRetroTheme.Price,true).alignment=TextAlignmentOptions.MidlineRight;
                    MButton(row,"出售给商店",actionX,29,214,31,()=>Confirm($"出售 {CampaignLocalization.Name(b)}，立即收到 {Session.BeetleSellPrice(b):N0} G？\n这只甲虫将离开你的收藏。",()=>app.Execute(s=>s.SellBeetle(b.id))),eligible,18);
                }
            }
            else if(marketSellCategory==1)
            {
                foreach(var specimen in State.specimens)
                {
                    var p=specimen;var row=MarketBbsRow(content,ref rowIndex,88);MarketRowArtwork(row,CampaignMarketArtwork.Kind.Specimen);
                    MText(row,CampaignDisplayText.SpecimenName(p)+"  ·  "+(p.beetle.stage<CampaignLifeStage.DormantAdult?CampaignSession.StageName(p.beetle.stage):$"{p.beetle.length:0.0} mm"),92,13,detailWidth,27,21,CampaignRetroTheme.Ink,true);
                    MText(row,$"{CampaignDisplayText.SpecimenSpecies(p)} · {CampaignDisplayText.SpecimenDate(p)} · "+(p.listed?"交易 BBS 挂牌中":p.wallSlot>=0?"墙面展示中 · 出售后自动取下":"收藏库保存"),92,48,detailWidth,26,18,CampaignRetroTheme.Muted);
                    MText(row,$"{Session.SpecimenSellPrice(p):N0} G",priceX,29,195,31,19,CampaignRetroTheme.Price,true).alignment=TextAlignmentOptions.MidlineRight;
                    MButton(row,p.listed?"查看挂牌报价":"出售给收藏店",actionX,29,214,31,()=>
                    {
                        if(p.listed)OpenMarketSpecimenOffers(p.id);
                        else Confirm($"出售标本「{CampaignDisplayText.SpecimenName(p)}」，立即收到 {Session.SpecimenSellPrice(p):N0} G？\n若正在墙上展示，相框会一起移出标本室。",()=>app.Execute(s=>s.SellSpecimen(p.id)));
                    },true,18);
                }
            }
            else
            {
                foreach(string supply in TradableSupplies)
                {
                    string id=supply;int owned=Session.InventoryCount(id);var row=MarketBbsRow(content,ref rowIndex,88);MarketRowArtwork(row,SupplyArtwork(id));
                    MText(row,$"{CampaignItems.Name(id)}  × {owned:N0}",92,13,detailWidth,27,21,CampaignRetroTheme.Ink,true);
                    MText(row,$"零售 {Session.SupplyBuyPrice(id):N0} G / 件 · 每笔出售 1–99 件",92,48,detailWidth,26,18,CampaignRetroTheme.Muted);
                    MText(row,$"{Session.SupplySellPrice(id):N0} G / 件",priceX,29,195,31,19,CampaignRetroTheme.Price,true).alignment=TextAlignmentOptions.MidlineRight;
                    MButton(row,"选择出售数量",actionX,29,214,31,()=>OpenMarketSupply(id,true),owned>0,18);
                }
            }
            if(rowIndex==0)
            {
                var empty=MarketBbsRow(content,ref rowIndex,75);MText(empty,marketSellCategory==0?"暂无活体收藏":"暂无标本",24,24,content.rect.width-48,31,14,CampaignRetroTheme.Muted);
            }
            notice=MStatus(panel,10,height-25,width-20,20,17);
            Canvas.ForceUpdateCanvases();
        }

        void MarketRowArtwork(Transform row,CampaignMarketArtwork.Kind kind)
        {
            var frame=MBox("SaleThumbnail",row,14,10,68,68,CampaignRetroTheme.Window,CampaignRetroTheme.Border,1);
            Component<CampaignMarketArtwork>(Rect("Artwork",frame,2,2,64,64)).SetKind(kind);
        }

        static CampaignMarketArtwork.Kind SupplyArtwork(string id)
        {
            if(CampaignDecorations.Known(id))return DecorationArtwork(id);
            int food=CampaignItems.FoodIndex(id);if(food>=0)return (CampaignMarketArtwork.Kind)((int)CampaignMarketArtwork.Kind.Potato+food);
            if(id==CampaignRearingItems.EggMedium||id==CampaignRearingItems.Humus||id==CampaignRearingItems.FermentedWood)return CampaignMarketArtwork.Kind.Wood;
            if(id==CampaignRearingItems.AdultJelly)return CampaignMarketArtwork.Kind.Sap;
            return id==CampaignItems.Sap?CampaignMarketArtwork.Kind.Sap:id==CampaignItems.Lamp?CampaignMarketArtwork.Kind.Lamp:CampaignMarketArtwork.Kind.Container;
        }

        void OpenMarketSupply(string itemId,bool sell)
        {
            bool rearing=CampaignRearingItems.All.Contains(itemId),decoration=CampaignDecorations.Known(itemId);float extra=rearing||decoration?60:0;
            var dialog=MarketDialog((sell?"出售库存 · ":"订购补给 · ")+CampaignItems.Name(itemId),620,432+extra);
            long unit=sell?Session.SupplySellPrice(itemId):Session.SupplyBuyPrice(itemId);
            int maximum=sell?Math.Min(99,Session.InventoryCount(itemId)):(int)Math.Min(99,State.coins/unit);
            if(!sell&&itemId==CampaignItems.Cabinet)maximum=Session.CabinetOrdered?0:Math.Min(1,maximum);
            Component<CampaignMarketArtwork>(Rect("SupplyArtwork",dialog,20,52,84,84)).SetKind(SupplyArtwork(itemId));
            MText(dialog,CampaignItems.Name(itemId),122,53,478,31,18,CampaignRetroTheme.Accent,true);
            MText(dialog,$"{(sell?"回收":"购买")}单价 {unit:N0} G · 仓库库存 {Session.InventoryCount(itemId):N0} 件",122,93,478,28,13);
            if(rearing)MText(dialog,CampaignRearingItems.Description(itemId),20,149,580,52,14,CampaignRetroTheme.Accent);
            if(decoration)MText(dialog,CampaignDecorations.Get(itemId).description,20,149,580,52,14,CampaignRetroTheme.Accent);
            MText(dialog,sell?"出售后立即到账。":"购买后在邮箱领取。",20,153+extra,580,46,12,CampaignRetroTheme.Muted);
            MText(dialog,"数量",20,212+extra,72,30,14);
            var quantity=MNumberInput(dialog,92,207+extra,134,36,"1");
            var total=MText(dialog,"",20,269+extra,580,33,20,CampaignRetroTheme.Price,true);
            var detail=MText(dialog,"",20,307+extra,580,31,12,CampaignRetroTheme.Muted);
            var submit=MButton(dialog,sell?"确认出售":"确认购买",344,373+extra,146,32,()=>
            {
                int count=int.Parse(quantity.text);CloseModal();app.Execute(s=>{if(sell)s.SellItem(itemId,count);else s.BuyItem(itemId,count);});
            },false,13);
            void RefreshQuantity()
            {
                bool valid=int.TryParse(quantity.text,out int count)&&count>=1&&count<=maximum;
                CampaignLocalization.Bind(total,valid?$"{(sell?"实收":"应付")}合计：{unit*count:N0} G":"请输入有效数量");
                CampaignLocalization.Bind(detail,maximum==0?(sell?"没有已领取库存，请先检查邮箱。":itemId==CampaignItems.Cabinet&&Session.CabinetOrdered?"扩展包已购买或已安装，请检查邮箱和仓库。":"金币不足，无法购买。"):$"本次可{(sell?"出售":"购买")} 1–{maximum} 件 · 当前可用金币 {State.coins:N0} G");
                submit.interactable=valid;
                submit.GetComponentInChildren<TMP_Text>().color=valid?CampaignRetroTheme.Ink:CampaignRetroTheme.Muted;
            }
            void SetQuantity(int value){quantity.SetTextWithoutNotify(Mathf.Clamp(value,1,Math.Max(1,maximum)).ToString());RefreshQuantity();}
            int CurrentQuantity()=>int.TryParse(quantity.text,out int value)?value:1;
            MButton(dialog,"−",238,208+extra,48,34,()=>SetQuantity(CurrentQuantity()-1),maximum>0,15);
            MButton(dialog,"+",296,208+extra,48,34,()=>SetQuantity(CurrentQuantity()+1),maximum>0,15);
            MButton(dialog,"+5",358,208+extra,70,34,()=>SetQuantity(CurrentQuantity()+5),maximum>0,13);
            MButton(dialog,"最大数量",440,208+extra,160,34,()=>SetQuantity(maximum),maximum>0,13);
            MButton(dialog,"取消",502,373+extra,98,32,CloseModal,true,13);
            quantity.onValueChanged.AddListener(_=>RefreshQuantity());RefreshQuantity();
        }

        TMP_InputField MNumberInput(Transform parent,float x,float y,float width,float height,string value)
        {
            var box=MBox("NumberInput",parent,x,y,width,height,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,2);
            var background=box.GetComponent<UnityEngine.UI.Image>();background.raycastTarget=true;
            var viewport=Rect("TextArea",box,8,4,width-16,height-8);Component<UnityEngine.UI.RectMask2D>(viewport);
            var text=Component<TextMeshProUGUI>(Rect("Value",viewport,0,0,width-16,height-8));text.font=font;text.fontSize=CampaignLocalization.PointSize(18);text.color=CampaignRetroTheme.Ink;text.alignment=TextAlignmentOptions.MidlineLeft;text.textWrappingMode=TextWrappingModes.NoWrap;text.raycastTarget=false;
            var input=Component<TMP_InputField>(box);input.targetGraphic=background;input.textViewport=viewport;input.textComponent=text;input.contentType=TMP_InputField.ContentType.IntegerNumber;input.lineType=TMP_InputField.LineType.SingleLine;input.characterLimit=15;input.caretColor=CampaignRetroTheme.Ink;input.customCaretColor=true;var selection=CampaignRetroTheme.Selection;selection.a=.4f;input.selectionColor=selection;input.text=value;return input;
        }

        void OpenMarketSpecimenOffers(string specimenId)
        {
            var p=Session.Specimen(specimenId);var dialog=MarketDialog("标本转让帖 - 本周收藏报价",700,535);
            MText(dialog,$"{CampaignDisplayText.SpecimenName(p)} · 市场估价 {Session.SpecimenAsking(p):N0} G",18,45,664,28,13,CampaignRetroTheme.Accent,true);
            var flow=Column(18,84,664,374,dialog);
            foreach(var offer in State.specimenOffers.Where(o=>o.specimenId==specimenId))
            {
                var o=offer;long net=Session.SaleProceeds(o.price);var row=MBox("SpecimenOffer",flow.content,0,flow.y,646,100,CampaignRetroTheme.Panel,CampaignRetroTheme.Border,1);flow.y+=106;flow.content.sizeDelta=new Vector2(664,flow.y);
                MText(row,o.buyer,10,9,320,23,12,CampaignRetroTheme.Ink,true);MText(row,$"报价 {o.price:N0} G",334,8,190,25,15,CampaignRetroTheme.Price,true);
                MText(row,$"手续费 {o.price-net:N0} G · 实收 {net:N0} G",10,39,510,22,12,CampaignRetroTheme.Price);MText(row,"报价本周有效",10,69,510,22,11,CampaignRetroTheme.Muted);
                MButton(row,"接受报价",538,35,96,29,()=>Confirm($"将标本「{CampaignDisplayText.SpecimenName(p)}」转让给{o.buyer}？\n报价 {o.price:N0} G，手续费 {o.price-net:N0} G，实收 {net:N0} G。",()=>app.Execute(s=>s.AcceptSpecimenOffer(o.id))));
            }
            MButton(dialog,"撤回挂牌",18,484,112,29,()=>{CloseModal();app.Execute(s=>s.UnlistSpecimen(specimenId));});MButton(dialog,"返回",586,484,96,29,CloseModal);
        }
    }
}
