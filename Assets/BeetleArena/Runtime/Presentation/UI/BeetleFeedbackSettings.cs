using UnityEngine;
using TMPro;
using BeetleArena.Campaign;
using L=BeetleArena.Campaign.CampaignLocalization;

namespace BeetleArena
{
    /// <summary>Independent combat settings: presentation preferences persist without mutating match rules or seeds.</summary>
    public sealed class BeetleFeedbackSettings : MonoBehaviour
    {
        public UnityEngine.UI.Button openButton, closeButton;
        public UnityEngine.UI.Toggle sparksToggle;
        public GameObject panel;
        public bool IsOpen=>panel.activeInHierarchy;
        public HornClashSparks sparks;
        UnityEngine.UI.Button styleButton, pixelButton, spotlightButton, uiStyleButton;
        RetroFilterDropdown filterDropdown,glassDropdown;
        TMP_Text filterDescription,glassDescription;
        BeetlePanelMotion panelMotion;
        void Awake()
        {
            RetroUiAudio.Bind(openButton); RetroUiAudio.Bind(closeButton);
            openButton.onClick.AddListener(Open); closeButton.onClick.AddListener(Close);
            sparksToggle.onValueChanged.AddListener(Change);
            BuildDisplayControls();
            RefreshTheme();
            L.BindHierarchy(panel.transform);
            L.Bind(openButton.GetComponentInChildren<TMP_Text>(),"设置");
            openButton.gameObject.SetActive(false);
            sparksToggle.SetIsOnWithoutNotify(sparks.SparksEnabled);
            panelMotion=BeetlePanelMotion.Attach(panel);panelMotion.SetVisible(false,false);
        }
        public void Open()
        { sparksToggle.SetIsOnWithoutNotify(sparks.SparksEnabled);RefreshTheme();panelMotion.SetVisible(true);panel.transform.SetAsLastSibling(); }
        void OnEnable()=>CampaignRetroTheme.Changed+=RefreshTheme;
        void OnDisable()=>CampaignRetroTheme.Changed-=RefreshTheme;
        void RefreshTheme()
        {
            if(!styleButton)return;
            CampaignRetroTheme.ApplyHierarchy(panel.transform);
            RefreshDisplayControls();
        }
        public void Close() {filterDropdown.Close(false);glassDropdown.Close(false);panelMotion.SetVisible(false);}
        void Update()
        {
            if(BeetleArena.Campaign.CampaignApp.Instance)return;
            if(UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame!=true)return;
            if(RetroFilterDropdown.ConsumeBack())return;
            if(IsOpen)Close();else Open();
        }
        void Change(bool value) { RetroUiAudio.PlayClick(); sparks.SetEnabled(value); }

        void BuildDisplayControls()
        {
            if(styleButton)return;
            ((RectTransform)panel.transform).sizeDelta=new Vector2(620,744);
            var font=Resources.Load<TMP_FontAsset>("RetroUiFont") ?? closeButton.GetComponentInChildren<TMP_Text>().font;
            AddLabel("游戏滤镜",180,font);
            AddLabel("像素档位",244,font);
            AddLabel("玻璃效果",308,font);
            styleButton=DisplayButton("GameFilter",180,()=>{},font);
            filterDropdown=RetroFilterDropdown.Attach(styleButton,_=>RefreshDisplayControls());
            pixelButton=DisplayButton("Pixel Size",244,()=>
            { BeetleDisplayPreferences.NextPixelSize();RefreshDisplayControls(); },font);
            var glassButton=DisplayButton("GlassQuality",308,()=>{},font);
            glassDropdown=RetroFilterDropdown.AttachGlass(glassButton,_=>RefreshDisplayControls());
            glassDescription=AddLabel(BeetleGlassPreferences.Description(BeetleGlassPreferences.Quality),370,font);
            glassDescription.name="GlassQualityDescription";L.SetFontSize(glassDescription,17);glassDescription.rectTransform.sizeDelta=new Vector2(570,42);
            AddLabel("养殖聚光灯",430,font);
            spotlightButton=DisplayButton("HusbandrySpotlight",430,()=>
            { BeetleHusbandryLightingPreferences.SetEnabled(!BeetleHusbandryLightingPreferences.Enabled);RefreshDisplayControls(); },font);
            var defaults=DisplayButton("Scene Defaults",494,()=>
            { BeetleDisplayPreferences.UseSceneDefault(gameObject.scene.name);RefreshDisplayControls(); },font);
            L.Bind(defaults.GetComponentInChildren<TMP_Text>(),"使用编辑器配置");
            AddLabel("界面风格",558,font);
            uiStyleButton=DisplayButton("UI Style",558,()=>
            { CampaignRetroTheme.SetStyle(CampaignRetroTheme.Paper?CampaignUiStyle.Classic:CampaignUiStyle.ComicPaper); },font);
            filterDescription=AddLabel(RetroFilterDropdown.Description(BeetleDisplayPreferences.Style),622,font);
            L.SetFontSize(filterDescription,17);filterDescription.rectTransform.sizeDelta=new Vector2(570,48);
            RefreshDisplayControls();
        }
        TMP_Text AddLabel(string text,float y,TMP_FontAsset font)
        {
            var go=new GameObject(text,typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(panel.transform,false);
            var rect=(RectTransform)go.transform;rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);
            rect.anchoredPosition=new Vector2(25,-y);rect.sizeDelta=new Vector2(150,48);
            var label=go.GetComponent<TMP_Text>();label.font=font;L.SetFontSize(label,20);L.Bind(label,text);
            label.color=new Color(.94f,.93f,.82f);label.raycastTarget=false;label.alignment=TextAlignmentOptions.MidlineLeft;
            return label;
        }
        UnityEngine.UI.Button DisplayButton(string name,float y,UnityEngine.Events.UnityAction action,TMP_FontAsset font)
        {
            var button=Instantiate(closeButton,panel.transform);button.name=name;
            button.onClick=new UnityEngine.UI.Button.ButtonClickedEvent();RetroUiAudio.Bind(button);button.onClick.AddListener(action);
            var rect=(RectTransform)button.transform;rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);
            rect.anchoredPosition=new Vector2(180,-y);rect.sizeDelta=new Vector2(415,48);
            var label=button.GetComponentInChildren<TMP_Text>();label.font=font;L.SetFontSize(label,20);
            var labelRect=label.rectTransform;labelRect.anchorMin=Vector2.zero;labelRect.anchorMax=Vector2.one;
            labelRect.offsetMin=new Vector2(10,0);labelRect.offsetMax=new Vector2(-10,0);
            return button;
        }
        void RefreshDisplayControls()
        {
            filterDropdown.RefreshLabel();glassDropdown.RefreshLabel();
            L.Bind(spotlightButton.GetComponentInChildren<TMP_Text>(),BeetleHusbandryLightingPreferences.Enabled?"开启":"关闭");
            L.Bind(glassDescription,BeetleGlassPreferences.Description(BeetleGlassPreferences.Quality));
            L.Bind(pixelButton.GetComponentInChildren<TMP_Text>(),BeetleDisplayPreferences.PixelSizeLabel);
            pixelButton.interactable=BeetleDisplayPreferences.IsDither(BeetleDisplayPreferences.Style);
            L.Bind(filterDescription,RetroFilterDropdown.Description(BeetleDisplayPreferences.Style));
            L.Bind(uiStyleButton.GetComponentInChildren<TMP_Text>(),CampaignRetroTheme.StyleName);
        }
        void OnDestroy()
        {
            if (openButton) openButton.onClick.RemoveListener(Open);
            if (closeButton) closeButton.onClick.RemoveListener(Close);
            if (sparksToggle) sparksToggle.onValueChanged.RemoveListener(Change);
        }
    }
}
