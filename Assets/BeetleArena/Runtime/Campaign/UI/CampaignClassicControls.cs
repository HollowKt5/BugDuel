using System;
using TMPro;
using UnityEngine;
using UI=UnityEngine.UI;

namespace BeetleArena.Campaign
{
    // Shared retro window controls used by the market, mailbox, warehouse and item dialogs.
    public sealed partial class CampaignView
    {
        RectTransform MBox(string name,Transform parent,float x,float y,float width,float height,Color color,Color border=default,float borderWidth=0)
        {
            var rt=Rect(name,parent,x,y,width,height);var im=Component<UI.Image>(rt);im.sprite=null;im.type=UI.Image.Type.Simple;im.material=null;im.color=color;im.raycastTarget=false;Component<RetroBevel>(rt).Configure(borderWidth,border,border);return rt;
        }
        TMP_Text MText(Transform parent,string text,float x,float y,float width,float height,int size=12,Color? color=null,bool bold=false)
        {
            var t=Label(parent,text,x,y,width,height,size,color??CampaignRetroTheme.Ink);t.fontStyle=bold?FontStyles.Bold:FontStyles.Normal;t.alignment=TextAlignmentOptions.TopLeft;t.margin=Vector4.zero;return t;
        }
        TMP_Text MStatus(Transform parent,float x,float y,float width,float height,int size=11,string text="")
        {
            var label=MText(parent,text,x,y,width,height,size,CampaignRetroTheme.Ink);
            label.name="StatusMessage";return label;
        }
        UI.Button MButton(Transform parent,string text,float x,float y,float width,float height,Action action,bool enabled=true,int size=11,bool flat=false,Color? background=null,Color? foreground=null)
        {
            var rt=MBox("MarketButton",parent,x,y,width,height,background??CampaignRetroTheme.Button);rt.name=text;if(!flat)Component<RetroBevel>(rt).Configure(2,CampaignRetroTheme.LightEdge,CampaignRetroTheme.DarkEdge);var im=rt.GetComponent<UI.Image>();im.raycastTarget=true;var b=Component<UI.Button>(rt);b.interactable=enabled;b.targetGraphic=im;b.transition=UI.Selectable.Transition.ColorTint;
            var c=b.colors;c.normalColor=Color.white;c.highlightedColor=CampaignRetroTheme.HoverTint;c.pressedColor=CampaignRetroTheme.PressedTint;c.selectedColor=CampaignRetroTheme.HoverTint;c.disabledColor=CampaignRetroTheme.DisabledTint;b.colors=c;
            if(text=="×")
            {
                // Draw the close mark independently of font glyphs, ellipsis and paper text margins.
                float depth=CampaignRetroTheme.Paper?Mathf.Min(5,height*.09f):0;
                float length=Mathf.Min(width,height)*.55f,stroke=Mathf.Max(1.5f,Mathf.Min(width,height)*.085f);
                for(int i=0;i<2;i++)
                {
                    var slash=MBox(i==0?"CloseSlash":"CloseBackslash",rt,(width-depth)*.5f,(height-depth*1.8f)*.5f,length,stroke,foreground??CampaignRetroTheme.Ink);
                    slash.pivot=new Vector2(.5f,.5f);slash.localRotation=Quaternion.Euler(0,0,i==0?45:-45);
                }
            }
            else
            {
                var label=MText(rt,text,flat?8:4,0,width-(flat?16:8),height,size,enabled?(foreground??CampaignRetroTheme.Ink):CampaignRetroTheme.Muted,CampaignRetroTheme.Paper&&size>=16);
                label.alignment=TextAlignmentOptions.Center;
                if(CampaignRetroTheme.Paper){float depth=Mathf.Min(5,height*.09f);label.margin=new Vector4(0,0,depth,depth*1.8f);}
            }
            b.onClick.RemoveAllListeners();b.onClick.AddListener(()=>{if(app.Busy||(modal&&!rt.IsChildOf(modal)))return;RetroUiAudio.PlayClick();action?.Invoke();});return b;
        }
        void MTitle(Transform parent,string title,float width,float height=28,int fontSize=19)
        {
            var bar=MBox("WindowTitle",parent,3,3,width,height,CampaignRetroTheme.Frame);
            Component<RetroBevel>(bar).Configure(1,CampaignRetroTheme.TitleHighlight,CampaignRetroTheme.TitleShadow,true,CampaignRetroTheme.TitleTop,CampaignRetroTheme.TitleBottom);
            MText(bar,title,12,3,width-54,height-4,fontSize,CampaignRetroTheme.TitleInk,CampaignRetroTheme.Paper);
        }
        RectTransform MarketScroll(Transform parent,float x,float y,float width,float height,Color background)
        {
            var sr=MBox("MarketScroll",parent,x,y,width,height,background);var scroll=Component<UI.ScrollRect>(sr);scroll.horizontal=false;scroll.vertical=true;scroll.movementType=UI.ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=35;
            var vp=Rect("Viewport",sr,0,0,width-17,height);Component<UI.Image>(vp).color=background;Component<UI.Mask>(vp).showMaskGraphic=true;var content=Rect("Content",vp,0,0,width-17,1);content.anchorMin=new Vector2(0,1);content.anchorMax=new Vector2(1,1);content.sizeDelta=new Vector2(0,content.sizeDelta.y);scroll.viewport=vp;scroll.content=content;
            var track=MBox("Scrollbar",sr,width-17,0,17,height,CampaignRetroTheme.Window,CampaignRetroTheme.Border,1);var bar=Component<UI.Scrollbar>(track);bar.direction=UI.Scrollbar.Direction.BottomToTop;var slide=Rect("SlidingArea",track,2,2,13,height-4);var handle=MBox("Handle",slide,0,0,13,32,CampaignRetroTheme.Button);Component<RetroBevel>(handle).Configure(2,CampaignRetroTheme.LightEdge,CampaignRetroTheme.DarkEdge);handle.GetComponent<UI.Image>().raycastTarget=true;Stretch(handle);bar.handleRect=handle;bar.targetGraphic=handle.GetComponent<UI.Image>();scroll.verticalScrollbar=bar;scroll.verticalScrollbarVisibility=UI.ScrollRect.ScrollbarVisibility.Permanent;return content;
        }
        RectTransform MarketDialog(string title,float width,float height,float titleHeight=28,int titleSize=19)
        {
            var dialog=OpenModal(width,height,false,content:title);dialog.name="ClassicDialog";
            dialog.GetComponent<UI.Image>().color=CampaignRetroTheme.Window;
            Component<RetroBevel>(dialog).Configure(3,CampaignRetroTheme.Frame,CampaignRetroTheme.Frame);
            MTitle(dialog,title,width-6,titleHeight,titleSize);
            float closeSize=Mathf.Min(30,titleHeight-8);
            MButton(dialog,"×",width-closeSize-10,3+(titleHeight-closeSize)*.5f,closeSize,closeSize,CloseModal,true,11,false,CampaignRetroTheme.Danger,Color.white);
            return dialog;
        }
    }
}
