using System;
using System.Collections.Generic;
using BeetleArena.Campaign;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BeetleArena
{
    // A real popup shared by campaign and standalone combat settings. Opening never writes a preference.
    public sealed class RetroFilterDropdown : MonoBehaviour, ICancelHandler
    {
        static readonly int[] filterValues={0,1,2,4},glassValues={0,1};
        static RetroFilterDropdown active;
        static int cancelledFrame=-1;
        readonly List<UnityEngine.UI.Button> options=new List<UnityEngine.UI.Button>();
        readonly List<TMP_Text> marks=new List<TMP_Text>();
        readonly Vector3[] corners=new Vector3[4];
        RectTransform host,overlay;
        CanvasGroup popupGroup;
        BeetlePanelMotion motion;
        TMP_Text triggerLabel,arrow;
        Action<BeetleDisplayStyle> selected;
        Action<BeetleGlassQuality> glassSelected;
        public bool IsGlassQualityDropdown { get; private set; }
        int[] Values=>IsGlassQualityDropdown?glassValues:filterValues;
        int CurrentValue=>IsGlassQualityDropdown?(int)SelectedQuality:(int)SelectedStyle;
        string ChoiceName(int value)=>IsGlassQualityDropdown?BeetleGlassPreferences.QualityName((BeetleGlassQuality)value):BeetleDisplayPreferences.StyleName((BeetleDisplayStyle)value);
        string ChoiceId(int value)=>IsGlassQualityDropdown?((BeetleGlassQuality)value).ToString():((BeetleDisplayStyle)value).ToString();
        public UnityEngine.UI.Button Trigger { get; private set; }
        public RectTransform PopupRect { get; private set; }
        public IReadOnlyList<UnityEngine.UI.Button> OptionButtons=>options;
        public bool IsOpen { get; private set; }
        public bool IsAnimating=>motion && motion.IsAnimating;
        public BeetleDisplayStyle SelectedStyle=>BeetleDisplayPreferences.Style;
        public BeetleGlassQuality SelectedQuality=>BeetleGlassPreferences.Quality;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics(){active=null;cancelledFrame=-1;}

        public static RetroFilterDropdown Attach(UnityEngine.UI.Button trigger,Action<BeetleDisplayStyle> onSelected,RectTransform popupHost=null)
        {
            var dropdown=Configure(trigger,false,popupHost);dropdown.selected=onSelected;return dropdown;
        }
        public static RetroFilterDropdown AttachGlass(UnityEngine.UI.Button trigger,Action<BeetleGlassQuality> onSelected,RectTransform popupHost=null)
        {
            var dropdown=Configure(trigger,true,popupHost);dropdown.glassSelected=onSelected;return dropdown;
        }
        static RetroFilterDropdown Configure(UnityEngine.UI.Button trigger,bool glassQuality,RectTransform popupHost)
        {
            if(!trigger.TryGetComponent<RetroFilterDropdown>(out var dropdown))dropdown=trigger.gameObject.AddComponent<RetroFilterDropdown>();
            dropdown.Trigger=trigger;dropdown.IsGlassQualityDropdown=glassQuality;
            dropdown.host=popupHost?popupHost:(RectTransform)trigger.GetComponentInParent<Canvas>().rootCanvas.transform;
            dropdown.triggerLabel=trigger.GetComponentInChildren<TMP_Text>();
            var labelRect=dropdown.triggerLabel.rectTransform;labelRect.anchorMin=Vector2.zero;labelRect.anchorMax=Vector2.one;
            labelRect.offsetMin=new Vector2(10,0);labelRect.offsetMax=new Vector2(-44,0);
            trigger.onClick.RemoveListener(dropdown.Toggle);trigger.onClick.AddListener(dropdown.Toggle);
            if(!dropdown.arrow)
            {
                dropdown.arrow=dropdown.Text("DropdownArrow",trigger.transform,"▼",18,CampaignRetroTheme.Ink);
                var rect=dropdown.arrow.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(1,.5f);rect.pivot=new Vector2(1,.5f);
                rect.anchoredPosition=new Vector2(-12,0);rect.sizeDelta=new Vector2(24,30);dropdown.arrow.alignment=TextAlignmentOptions.Center;
            }
            dropdown.RefreshLabel();return dropdown;
        }

        public void RefreshLabel()
        {
            CampaignLocalization.Bind(triggerLabel,ChoiceName(CurrentValue));
            for(int i=0;i<marks.Count;i++)marks[i].text=Values[i]==CurrentValue?"✓":"";
        }
        public void Toggle(){if(IsOpen)Close();else Open();}
        public void Open()
        {
            if(IsOpen||!Trigger.IsInteractable())return;
            if(active&&active!=this)active.Close(false);
            DisposePopup();active=this;IsOpen=true;
            overlay=Rect(IsGlassQualityDropdown?"GlassDropdownOverlay":"FilterDropdownOverlay",host);overlay.anchorMin=Vector2.zero;overlay.anchorMax=Vector2.one;overlay.offsetMin=overlay.offsetMax=Vector2.zero;
            var parentCanvas=host.GetComponentInParent<Canvas>();var canvas=overlay.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting=true;canvas.sortingLayerID=parentCanvas.sortingLayerID;canvas.sortingOrder=parentCanvas.sortingOrder+50;
            overlay.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            popupGroup=overlay.gameObject.AddComponent<CanvasGroup>();
            var blocker=overlay.gameObject.AddComponent<UnityEngine.UI.Image>();blocker.color=Color.clear;blocker.raycastTarget=true;
            var dismiss=overlay.gameObject.AddComponent<UnityEngine.UI.Button>();dismiss.targetGraphic=blocker;dismiss.transition=Selectable.Transition.None;dismiss.onClick.AddListener(()=>Close());
            PopupRect=Rect(IsGlassQualityDropdown?"GlassDropdown":"FilterDropdown",overlay);PopupRect.anchorMin=PopupRect.anchorMax=new Vector2(.5f,.5f);PopupRect.pivot=new Vector2(0,1);
            var background=PopupRect.gameObject.AddComponent<UnityEngine.UI.Image>();background.color=CampaignRetroTheme.Panel;background.raycastTarget=true;
            PopupRect.gameObject.AddComponent<RetroBevel>().Configure(2,CampaignRetroTheme.LightEdge,CampaignRetroTheme.DarkEdge);
            for(int i=0;i<Values.Length;i++)
            {
                var value=Values[i];var row=Rect((IsGlassQualityDropdown?"GlassOption-":"FilterOption-")+ChoiceId(value),PopupRect);row.anchorMin=new Vector2(0,1);row.anchorMax=Vector2.one;row.pivot=new Vector2(.5f,1);
                row.anchoredPosition=new Vector2(0,-4-i*44);row.sizeDelta=new Vector2(-8,44);
                var image=row.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=CampaignRetroTheme.Button;image.raycastTarget=true;
                var button=row.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;
                var colors=button.colors;colors.normalColor=Color.white;colors.highlightedColor=CampaignRetroTheme.Hex(0xc4d5f2);colors.selectedColor=colors.highlightedColor;colors.pressedColor=CampaignRetroTheme.Hex(0x9fb9e6);button.colors=colors;
                var mark=Text(IsGlassQualityDropdown?"CurrentGlassQuality":"CurrentFilter",row,value==CurrentValue?"✓":"",20,CampaignRetroTheme.Ink);PlaceText(mark,10,26);
                var label=Text(IsGlassQualityDropdown?"GlassLabel":"FilterLabel",row,ChoiceName(value),20,CampaignRetroTheme.Ink);PlaceText(label,40,-50);label.rectTransform.anchorMax=new Vector2(1,.5f);
                var cancel=row.gameObject.AddComponent<RetroFilterDropdownCancel>();cancel.Owner=this;
                button.onClick.AddListener(()=>
                {
                    RetroUiAudio.PlayClick();
                    if(IsGlassQualityDropdown)BeetleGlassPreferences.SetQuality((BeetleGlassQuality)value);else BeetleDisplayPreferences.Style=(BeetleDisplayStyle)value;
                    RefreshLabel();Close();
                    if(IsGlassQualityDropdown)glassSelected?.Invoke((BeetleGlassQuality)value);else selected?.Invoke((BeetleDisplayStyle)value);
                });
                options.Add(button);marks.Add(mark);
            }
            for(int i=0;i<options.Count;i++)
                options[i].navigation=new Navigation {mode=Navigation.Mode.Explicit,selectOnUp=options[(i+options.Count-1)%options.Count],selectOnDown=options[(i+1)%options.Count]};
            Canvas.ForceUpdateCanvases();PlacePopup();
            motion=BeetlePanelMotion.Attach(PopupRect.gameObject);motion.SetVisible(true);
            SelectCurrent();
        }
        public void Close(bool restoreFocus=true)
        {
            if(!IsOpen)return;
            IsOpen=false;if(active==this)active=null;
            // Keep the outside blocker until the closing tween ends; the dismiss click cannot hit the parent window.
            popupGroup.interactable=false;
            motion.SetVisible(false,true,DisposePopup);
            if(restoreFocus&&Trigger&&Trigger.IsInteractable()&&EventSystem.current)EventSystem.current.SetSelectedGameObject(Trigger.gameObject);
        }
        public static bool ConsumeBack()
        {
            if(cancelledFrame==Time.frameCount)return true;
            if(!active||!active.IsOpen)return false;
            cancelledFrame=Time.frameCount;active.Close();return true;
        }
        public void OnCancel(BaseEventData eventData){if(ConsumeBack())eventData.Use();}
        void Update(){if(IsOpen&&Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame)ConsumeBack();}
        void LateUpdate()
        {
            if(!IsOpen)return;
            PlacePopup();
            var current=EventSystem.current?EventSystem.current.currentSelectedGameObject:null;
            if(!current||!current.transform.IsChildOf(PopupRect))SelectCurrent();
        }
        void SelectCurrent()
        {
            int index=Array.IndexOf(Values,CurrentValue);
            if(EventSystem.current)EventSystem.current.SetSelectedGameObject(options[Mathf.Max(0,index)].gameObject);
        }
        void PlacePopup()
        {
            ((RectTransform)Trigger.transform).GetWorldCorners(corners);
            Vector3 lower=overlay.InverseTransformPoint(corners[0]),upper=overlay.InverseTransformPoint(corners[2]);
            float width=Mathf.Min(upper.x-lower.x,overlay.rect.width-16),height=Values.Length*44+8;
            PopupRect.sizeDelta=new Vector2(width,height);
            float x=Mathf.Clamp(lower.x,overlay.rect.xMin+8,overlay.rect.xMax-width-8),y=lower.y-4;
            if(y-height<overlay.rect.yMin+8)y=upper.y+height+4;
            PopupRect.anchoredPosition=new Vector2(x,Mathf.Clamp(y,overlay.rect.yMin+height+8,overlay.rect.yMax-8));
        }
        TMP_Text Text(string name,Transform parent,string source,int size,Color color)
        {
            var rect=Rect(name,parent);var text=rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font=triggerLabel.font;text.fontSize=size;text.color=color;text.raycastTarget=false;text.richText=false;
            text.textWrappingMode=TextWrappingModes.NoWrap;text.overflowMode=TextOverflowModes.Ellipsis;text.alignment=TextAlignmentOptions.MidlineLeft;
            CampaignLocalization.Bind(text,source);return text;
        }
        static void PlaceText(TMP_Text text,float x,float width)
        {
            var rect=text.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(0,.5f);rect.pivot=new Vector2(0,.5f);rect.anchoredPosition=new Vector2(x,0);rect.sizeDelta=new Vector2(width,40);
        }
        static RectTransform Rect(string name,Transform parent)
        {var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);return rect;}
        void DisposePopup()
        {
            if(overlay){overlay.gameObject.SetActive(false);Destroy(overlay.gameObject);}
            overlay=null;PopupRect=null;popupGroup=null;motion=null;options.Clear();marks.Clear();
        }
        void OnDisable(){IsOpen=false;if(active==this)active=null;DisposePopup();}
        void OnDestroy(){if(Trigger)Trigger.onClick.RemoveListener(Toggle);}

        public static string Description(BeetleDisplayStyle style)
        {
            switch(style)
            {
                case BeetleDisplayStyle.Crt:return "显像管曲面、扫描线与轻微色散。";
                case BeetleDisplayStyle.OrderedDither:return "柔和的有序抖动，保留更多颜色层次。";
                case BeetleDisplayStyle.DenseDither:return "细密网点与更明显的色阶，保留场景原色。";
                case BeetleDisplayStyle.Custom:return "使用当前场景在编辑器中配置的自定义画面材质。";
                default:return "保留场景原始画面，不添加滤镜。";
            }
        }
    }
    sealed class RetroFilterDropdownCancel : MonoBehaviour, ICancelHandler
    {
        public RetroFilterDropdown Owner;
        public void OnCancel(BaseEventData eventData)=>Owner.OnCancel(eventData);
    }
}
