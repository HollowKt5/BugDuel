using System;
using TMPro;
using UnityEngine;
using UI=UnityEngine.UI;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        float MarketCardGrid(RectTransform content,float height)
        {
            var layout=Layout.market;
            int columns=Mathf.Max(1,Mathf.FloorToInt((content.rect.width-layout.gridPadding*2+layout.gap.x)/(layout.MinimumWidth+layout.gap.x)));
            float width=(content.rect.width-layout.gridPadding*2-(columns-1)*layout.gap.x)/columns;
            var grid=Component<UI.GridLayoutGroup>(content);grid.constraint=UI.GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=columns;
            grid.cellSize=new Vector2(width,height);grid.spacing=layout.gap;grid.padding=new RectOffset(layout.gridPadding,layout.gridPadding,layout.gridPadding,layout.gridPadding);grid.childAlignment=TextAnchor.UpperLeft;
            Component<UI.ContentSizeFitter>(content).verticalFit=UI.ContentSizeFitter.FitMode.PreferredSize;
            return width;
        }

        RectTransform MarketCardSurface(string name,Transform parent,float width,float height,out RectTransform header,out RectTransform footer)
        {
            float edge=CampaignRetroTheme.Paper?8:3,shadowInset=CampaignRetroTheme.Paper?5:0,inside=width-edge*2-shadowInset;
            var card=MBox(name,parent,0,0,width,height,CampaignRetroTheme.SurfaceRaised);
            Component<RetroBevel>(card).Configure(2,CampaignRetroTheme.LightEdge,CampaignRetroTheme.DarkEdge,paperFold:false);
            var shadow=Component<UI.Shadow>(card);shadow.enabled=!CampaignRetroTheme.Paper;shadow.effectColor=new Color(0,0,0,CampaignRetroTheme.Dark ? .32f : .18f);shadow.effectDistance=new Vector2(2,-2);
            header=MBox("CardHeader",card,edge,edge,inside,26,CampaignRetroTheme.Window);
            footer=MBox("PriceRow",card,edge,height-edge-shadowInset-42,inside,42,CampaignRetroTheme.SurfaceInset);
            MBox("PriceDivider",footer,0,0,inside,1,CampaignRetroTheme.Rule);
            return card;
        }

        void MarketCardHeading(RectTransform header,string title,string status="")
        {
            float reserved=string.IsNullOrEmpty(status)?0:96;
            MText(header,title,4,1,header.rect.width-8-reserved,23,17,CampaignRetroTheme.Accent,true);
            if(reserved>0)MText(header,status,header.rect.width-reserved,1,reserved-4,23,17,CampaignRetroTheme.Muted).alignment=TextAlignmentOptions.MidlineRight;
        }

        void MarketCardArtwork(Transform card,CampaignMarketArtwork.Kind kind,float y)
        {
            float padding=Layout.market.cardPadding,size=Layout.market.thumbnailSize;
            var thumb=MBox("Thumbnail",card,padding,y,size,size,CampaignRetroTheme.Thumbnail);
            Component<RetroBevel>(thumb).Configure(2,CampaignRetroTheme.DarkEdge,CampaignRetroTheme.LightEdge,paperFold:false);
            Component<CampaignMarketArtwork>(Rect("Artwork",thumb,2,2,size-4,size-4)).SetKind(kind);
        }

        void MarketCardAction(RectTransform footer,string title,Action action,bool enabled=true)
            =>MButton(footer,title,footer.rect.width-164,5,158,32,action,enabled,18);

        RectTransform MarketFlowCard(string name,Flow flow,float height,out RectTransform header,out RectTransform footer)
        {
            var card=MarketCardSurface(name,flow.content,flow.CardWidth,height,out header,out footer);
            card.anchoredPosition=new Vector2(0,-flow.y);flow.y+=height+Layout.market.gap.y;
            flow.content.sizeDelta=new Vector2(flow.width,flow.y);return card;
        }

        UI.Button MarketChoice(Transform parent,string title,float x,float y,float width,float height,bool selected,Action action)
        {
            var tab=MButton(parent,title,x,y,width,height,action,true,20,true,
                selected?CampaignRetroTheme.Window:CampaignRetroTheme.InactiveTab,selected?CampaignRetroTheme.Ink:CampaignRetroTheme.Muted);
            Component<RetroBevel>(tab.transform).Configure(2,CampaignRetroTheme.LightEdge,CampaignRetroTheme.DarkEdge,paperFold:false);
            var label=tab.GetComponentInChildren<TMP_Text>();label.alignment=TextAlignmentOptions.MidlineLeft;
            label.rectTransform.anchoredPosition=new Vector2(14,0);label.rectTransform.sizeDelta=new Vector2(width-28,height);
            label.fontStyle=selected?FontStyles.Bold:FontStyles.Normal;
            return tab;
        }
    }
}
