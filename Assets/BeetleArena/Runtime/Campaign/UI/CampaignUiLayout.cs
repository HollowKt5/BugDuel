using System;
using TMPro;
using UnityEngine;
using UI=UnityEngine.UI;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        RectTransform Rect(string name,Transform parent,float x,float y,float w,float h)
        {
            bool created=true;
            var rt=tree.Building?tree.Take(name,parent,out created):new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
            if(!tree.Building)rt.SetParent(parent,false);
            rt.anchorMin=rt.anchorMax=new Vector2(0,1);rt.pivot=new Vector2(0,1);
            if(created || name!="Content")rt.anchoredPosition=new Vector2(x,-y);
            rt.sizeDelta=new Vector2(w,h);return rt;
        }
        static T Component<T>(Transform target) where T:UnityEngine.Component => target.TryGetComponent<T>(out var component) ? component : target.gameObject.AddComponent<T>();
        static void Stretch(RectTransform rt) { rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;rt.offsetMin=rt.offsetMax=Vector2.zero; }
        static void CenterFrame(RectTransform rt) { rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(.5f,.5f);rt.anchoredPosition=Vector2.zero;rt.sizeDelta=new Vector2(1600,900); }
        RectTransform Panel(Transform parent,float x,float y,float w,float h,bool clear=false,string name="Panel")
        {
            var rt=Rect(name,parent,x,y,w,h);var im=Component<UI.Image>(rt);im.sprite=null;im.type=UI.Image.Type.Simple;im.material=null;
            im.color=clear?CampaignRetroTheme.Button:CampaignRetroTheme.Window;
            Component<RetroBevel>(rt).Configure(clear?2:1,CampaignRetroTheme.LightEdge,CampaignRetroTheme.DarkEdge);return rt;
        }
        TMP_Text Label(Transform parent,string text,float x,float y,float w,float h,int size=22,Color? color=null)
        {
            var rt=Rect("Text",parent,x,y,w,h);var t=Component<TextMeshProUGUI>(rt);t.font=CampaignLocalization.Font?CampaignLocalization.Font:font;t.color=color??ink;t.textWrappingMode=TextWrappingModes.Normal;t.overflowMode=TextOverflowModes.Ellipsis;t.raycastTarget=false;
            t.enableAutoSizing=CampaignLocalization.English;CampaignLocalization.SetFontSize(t,size,Mathf.Min(size,Mathf.Max(9,size*.75f)));
            t.richText=false;CampaignLocalization.Bind(t,text);return t;
        }
        UI.Button Btn(Transform parent,string text,float x,float y,float w,float h,Action action,bool enabled=true)
        {
            var rt=Panel(parent,x,y,w,h,true);rt.name=text;var b=Component<UI.Button>(rt);b.onClick.RemoveAllListeners();b.targetGraphic=rt.GetComponent<UI.Image>();b.interactable=enabled;
            var colors=b.colors;colors.normalColor=Color.white;colors.highlightedColor=CampaignRetroTheme.HoverTint;colors.selectedColor=CampaignRetroTheme.HoverTint;colors.pressedColor=CampaignRetroTheme.PressedTint;colors.disabledColor=CampaignRetroTheme.DisabledTint;b.colors=colors;
            var label=Label(rt,text,10,0,w-20,h,19,enabled?ink:muted);label.alignment=TextAlignmentOptions.Center;
            label.fontStyle=CampaignRetroTheme.Paper?FontStyles.Bold:FontStyles.Normal;
            float depth=Mathf.Min(5,h*.09f);
            label.margin=CampaignRetroTheme.Paper?new Vector4(0,0,depth,depth*1.8f):Vector4.zero;
            b.onClick.AddListener(()=>{if(app.Busy || (modal&&!rt.IsChildOf(modal)))return;RetroUiAudio.PlayClick();action();});return b;
        }
        sealed class Flow
        {
            public RectTransform content;
            public float y,width,rightInset=6,spacing=14;
            public float CardWidth=>width-rightInset;
        }
        Flow Column(float x,float y,float width,float height,Transform parent=null,string name="Scroll")
        {
            var view=Rect(name,parent?parent:root,x,y,width,height);var scroll=Component<UI.ScrollRect>(view); scroll.horizontal=false;scroll.movementType=UI.ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=35;
            var vp=Rect("Viewport",view,0,0,width,height);Component<UI.RectMask2D>(vp);var hit=Component<UI.Image>(vp);hit.color=Color.clear;
            var content=Rect("Content",vp,0,0,width,1);scroll.viewport=vp;scroll.content=content;
            return new Flow{content=content,width=width};
        }
        RectTransform Card(Flow flow,float height)
        {
            var card=Panel(flow.content,0,flow.y,flow.CardWidth,height,name:"Card");flow.y+=height+flow.spacing;flow.content.sizeDelta=new Vector2(flow.width,flow.y);return card;
        }
    }
}
