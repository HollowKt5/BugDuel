using System;
using System.Collections.Generic;
using System.Linq;
using BugDuel.Campaign;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UI=UnityEngine.UI;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView : MonoBehaviour
    {
        public CampaignPage page;
        public TMP_FontAsset font;
        public Shader glassShader;
        public CampaignBackdrop backdrop;
        public CampaignLeagueBridge leagueBridge;
        CampaignApp app;
        RectTransform canvas,root,modal;
        TMP_Text notice,careLabel,behaviorLabel;
        CampaignCareController careController;
        readonly CampaignUiTree tree=new CampaignUiTree();
        CanvasGroup pageInteraction;
        GameObject focusBeforeModal;
        bool closingModal;
        bool settingsOpen;
        Sprite rounded;
        Texture2D roundTexture;
        readonly List<Texture2D> thumbnails=new List<Texture2D>();
        InputActionRebindingExtensions.RebindingOperation rebind;
        bool refreshQueued;
        GameObject lastFocus;
        float careLabelClock;
        int settingsTab,feedChoice=1,climateChoice=1;
        bool withSap,withLamp;
        string parentMale,parentFemale;
        Color ink,muted,accent;
        CampaignObservationCamera observation;
        Vector2 lastCanvasSize;
        CampaignSession Session=>app.Session;
        CampaignState State=>Session.State;
        // Existing callers retain their source text; the centralized language tables own translations.
        public string L(string zh,string en)=>zh;

        void Start()
        {
            app=CampaignApp.Ensure();
            var retroFont=Resources.Load<TMP_FontAsset>("RetroUiFont");if(retroFont)font=retroFont;
            if(page!=CampaignPage.MainMenu && page!=CampaignPage.Saves && Session==null) { app.Go(CampaignPage.MainMenu);return; }
            CreateRounded();
            var go=new GameObject("CampaignCanvas",typeof(RectTransform),typeof(Canvas),typeof(UI.CanvasScaler),typeof(UI.GraphicRaycaster)); go.transform.SetParent(transform,false);
            canvas=(RectTransform)go.transform; go.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=go.GetComponent<UI.CanvasScaler>(); scaler.uiScaleMode=UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution=new Vector2(1600,900);scaler.screenMatchMode=UI.CanvasScaler.ScreenMatchMode.Expand;
            if(backdrop)
            {
                var background=Rect("World",canvas,0,0,1600,900); Stretch(background);
                backdrop.output=background.gameObject.AddComponent<UI.RawImage>(); backdrop.output.raycastTarget=false; backdrop.output.texture=backdrop.Frame;
                if(page==CampaignPage.Arena)BeetleCombatCrt.Attach(backdrop.output);
                if(page==CampaignPage.Greenhouse)
                {
                    backdrop.SetViewport(new Rect(1f/3f,0,2f/3f,1));
                    observation=CampaignObservationCamera.Ensure(backdrop);
                }
                backdrop.SetUiFocus(page==CampaignPage.Saves || page==CampaignPage.Catching || page==CampaignPage.Market || page==CampaignPage.League || page==CampaignPage.Auction);
            }
            if(!EventSystem.current) { var e=new GameObject("CampaignEvents",typeof(EventSystem),typeof(InputSystemUIInputModule)); e.GetComponent<InputSystemUIInputModule>().AssignDefaultActions(); }
            app.Changed+=QueueRefresh;
            CampaignLocalization.Changed+=QueueRefresh;
            if(leagueBridge) leagueBridge.Finished+=QueueRefresh;
            Rebuild();
        }
        void QueueRefresh() { refreshQueued=true; }
        void LateUpdate()
        {
            // Keep keyboard/controller navigation inside the visible portion of scrollable lists.
            var focus=EventSystem.current?EventSystem.current.currentSelectedGameObject:null;
            if(modal && (!focus || !focus.transform.IsChildOf(modal))) { SelectFirst(modal); return; }
            if(!focus||focus==lastFocus)return;lastFocus=focus;
            var scroll=focus.GetComponentInParent<UI.ScrollRect>();if(!scroll||!scroll.viewport||!scroll.content)return;
            Canvas.ForceUpdateCanvases();var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport,focus.transform);
            float delta=bounds.min.y<scroll.viewport.rect.yMin?scroll.viewport.rect.yMin-bounds.min.y:bounds.max.y>scroll.viewport.rect.yMax?scroll.viewport.rect.yMax-bounds.max.y:0;
            var position=scroll.content.anchoredPosition;position.y=Mathf.Clamp(position.y+delta,0,Mathf.Max(0,scroll.content.rect.height-scroll.viewport.rect.height));scroll.content.anchoredPosition=position;
        }
        void Update()
        {
            if(!app || !canvas) return;
            if(mailboxNotice)CampaignLocalization.Bind(mailboxNotice,app.Message);
            if(lastCanvasSize!=canvas.rect.size) { lastCanvasSize=canvas.rect.size;QueueRefresh(); }
            if(pageInteraction)pageInteraction.interactable=!app.Busy&&!modal;
            if(refreshQueued && !modal && !app.Busy) { refreshQueued=false;Rebuild(); }
            if(notice) CampaignLocalization.Bind(notice,app.Message.Length>0?app.Message:Session?.State.notice??"");
            careLabelClock+=Time.unscaledDeltaTime;
            if(careLabel && Session!=null && careLabelClock>=.25f)
            {
                careLabelClock=0;
                var b=State.beetles.Find(x=>x.id==State.selectedId); if(b!=null) CampaignLocalization.Bind(careLabel,Details(b));
                if(!careController)careController=FindFirstObjectByType<CampaignCareController>();
                if(behaviorLabel&&b!=null)CampaignLocalization.Bind(behaviorLabel,careController?careController.Describe(b.id):"观察中");
            }
            if(app.Busy || rebind!=null) return;
            if(page==CampaignPage.Market && !modal && HandleMarketShortcuts())return;
            if(app.Controls["Back"].WasPressedThisFrame())
            { if(modal) CloseModal(); else if(page!=CampaignPage.MainMenu) Confirm("返回主菜单？当前进度会自动保存。",()=>{if(Session!=null)app.SaveSlot(0);app.Go(CampaignPage.MainMenu);}); }
            if(app.Controls["NextWeek"].WasPressedThisFrame()) app.Manage(page,"NextWeek");
            if(app.Controls["Help"].WasPressedThisFrame()) app.Manage(page,"Help");
        }
        void Rebuild()
        {
            ink=CampaignRetroTheme.Ink;muted=CampaignRetroTheme.Muted;accent=CampaignRetroTheme.Accent;
            var selected=EventSystem.current?EventSystem.current.currentSelectedGameObject:null;
            if(!root)
            {
                root=Rect("Page",canvas,0,0,1600,900); CenterFrame(root);
                pageInteraction=root.gameObject.AddComponent<CanvasGroup>();
            }
            if(page==CampaignPage.Greenhouse)
            { root.anchorMin=Vector2.zero;root.anchorMax=new Vector2(1f/3f,1);root.pivot=new Vector2(0,1);root.offsetMin=root.offsetMax=Vector2.zero;Canvas.ForceUpdateCanvases(); }
            foreach(var t in thumbnails) if(t) Destroy(t); thumbnails.Clear(); careLabel=null;behaviorLabel=null;
            tree.Begin();
            if(page==CampaignPage.Arena) { DrawArena(); EndRefresh(selected); return; }
            if(page==CampaignPage.Market) { DrawMarket();EndRefresh(selected);return; }
            if(page==CampaignPage.Greenhouse) { DrawGreenhouse();EndRefresh(selected);return; }
            if(page==CampaignPage.MainMenu) { DrawMenu();EndRefresh(selected);return; }
            else if(page==CampaignPage.Saves) DrawSaves();
            else
            {
                Navigation();
                switch(page)
                {
                    case CampaignPage.Greenhouse: DrawGreenhouse();break;
                    case CampaignPage.Catching: DrawCatching();break;
                    case CampaignPage.Market: DrawMarket();break;
                    case CampaignPage.League: DrawLeague();break;
                    case CampaignPage.Auction: DrawAuction();break;
                }
            }
            var footer=Panel(root,32,835,1536,45);notice=Label(footer,"",18,10,1500,30,17,muted);
            EndRefresh(selected);
        }
        void EndRefresh(GameObject selected)
        {
            tree.End();
            if(!selected || !selected.activeInHierarchy || (selected.GetComponent<UI.Selectable>() is UI.Selectable control && !control.IsInteractable()))SelectFirst(root);
            foreach(var scroll in root.GetComponentsInChildren<UI.ScrollRect>())
            {
                var position=scroll.content.anchoredPosition;
                position.y=Mathf.Clamp(position.y,0,Mathf.Max(0,scroll.content.rect.height-scroll.viewport.rect.height));
                scroll.content.anchoredPosition=position;
            }
        }
        void Navigation()
        {
            Panel(root,32,100,1536,720);
            var nav=Panel(root,32,24,1536,64);
            Label(nav,"BUG DUEL  /  虫舍日记",22,18,280,36,23,accent);
            var pages=new[]{CampaignPage.Greenhouse,CampaignPage.Catching,CampaignPage.Market,CampaignPage.League,CampaignPage.Auction};
            string[] names={L("温室","Greenhouse"),L("捕虫队","Catching"),L("市场","Market"),L("联赛","Leagues"),L("冠军拍卖","Auction")};
            for(int i=0;i<pages.Length;i++) { var p=pages[i]; Btn(nav,names[i],320+i*146,10,136,44,()=>app.Go(p),page!=p); }
            Btn(nav,L("存档","Saves"),1060,10,100,44,()=>app.Go(CampaignPage.Saves));
            Btn(nav,L("设置","Settings"),1170,10,100,44,OpenSettings);
            Btn(nav,L("主菜单","Menu"),1280,10,125,44,()=>{app.SaveSlot(0);app.Go(CampaignPage.MainMenu);});
            if(page==CampaignPage.Greenhouse)
            {
                var clock=Label(nav,"",1414,6,108,52,16,accent);clock.name="LocalClock";clock.alignment=TextAlignmentOptions.Center;
                Component<Canvas>(clock.transform);Component<CampaignLocalClock>(clock.transform).Bind(clock);
            }
            Label(root,$"第 {State.week:00} 周    ·    {State.coins:N0} G    ·    温室 {State.beetles.Count}+{Session.Reserved}/{State.capacity}    ·    口碑 {State.reputation}",42,110,1200,35,24,ink);
            Btn(root,L("下一周  N","Next week  N"),1350,105,210,48,()=>app.Execute(s=>s.NextWeek()));
        }
        void Title(string title,string subtitle) { Label(root,title,42,163,1180,48,32,ink); Label(root,subtitle,44,212,1480,32,18,muted); }
        RectTransform Rect(string name,Transform parent,float x,float y,float w,float h)
        {
            bool created=true;
            var rt=tree.Building?tree.Take(name,parent,out created):new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
            if(!tree.Building)rt.SetParent(parent,false);
            rt.anchorMin=rt.anchorMax=new Vector2(0,1);rt.pivot=new Vector2(0,1);
            if(created || name!="Content")rt.anchoredPosition=new Vector2(x,-y);
            rt.sizeDelta=new Vector2(w,h);return rt;
        }
        static T Component<T>(Transform target) where T:UnityEngine.Component => target.GetComponent<T>() ?? target.gameObject.AddComponent<T>();
        static void Stretch(RectTransform rt) { rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;rt.offsetMin=rt.offsetMax=Vector2.zero; }
        static void CenterFrame(RectTransform rt) { rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(.5f,.5f);rt.anchoredPosition=Vector2.zero;rt.sizeDelta=new Vector2(1600,900); }
        RectTransform Panel(Transform parent,float x,float y,float w,float h,bool clear=false)
        {
            var rt=Rect("Surface",parent,x,y,w,h);var im=Component<UI.Image>(rt);im.sprite=null;im.type=UI.Image.Type.Simple;im.material=null;
            im.color=clear?CampaignRetroTheme.Button:CampaignRetroTheme.Window;
            Component<RetroBevel>(rt).Configure(clear?2:1,CampaignRetroTheme.LightEdge,CampaignRetroTheme.DarkEdge);return rt;
        }
        TMP_Text Label(Transform parent,string text,float x,float y,float w,float h,int size=22,Color? color=null)
        {
            var rt=Rect("Text",parent,x,y,w,h);var t=Component<TextMeshProUGUI>(rt);t.font=font;t.fontSize=size;t.color=color??ink;t.textWrappingMode=TextWrappingModes.Normal;t.overflowMode=TextOverflowModes.Ellipsis;t.raycastTarget=false;
            t.enableAutoSizing=CampaignLocalization.English;t.fontSizeMin=Mathf.Min(size,Mathf.Max(9,size*.75f));t.fontSizeMax=size;
            CampaignLocalization.Bind(t,text);return t;
        }
        UI.Button Btn(Transform parent,string text,float x,float y,float w,float h,Action action,bool enabled=true)
        {
            var rt=Panel(parent,x,y,w,h,true);rt.name=text;var b=Component<UI.Button>(rt);b.onClick.RemoveAllListeners();b.targetGraphic=rt.GetComponent<UI.Image>();b.interactable=enabled;
            var colors=b.colors;colors.normalColor=Color.white;colors.highlightedColor=CampaignRetroTheme.Hex(0xe3ebfa);colors.selectedColor=colors.highlightedColor;colors.pressedColor=CampaignRetroTheme.Hex(0xc4d5f2);colors.disabledColor=new Color(.75f,.75f,.75f,1);b.colors=colors;
            var label=Label(rt,text,10,0,w-20,h,19,enabled?ink:muted);label.alignment=TextAlignmentOptions.Center;b.onClick.AddListener(()=>{if(app.Busy || (modal&&!rt.IsChildOf(modal)))return;action();});return b;
        }
        sealed class Flow { public RectTransform content; public float y,width; }
        Flow Column(float x,float y,float width,float height,Transform parent=null)
        {
            var view=Rect("Scroll",parent?parent:root,x,y,width,height);var scroll=Component<UI.ScrollRect>(view); scroll.horizontal=false;scroll.movementType=UI.ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=35;
            var vp=Rect("Viewport",view,0,0,width,height);Component<UI.RectMask2D>(vp);var hit=Component<UI.Image>(vp);hit.color=Color.clear;
            var content=Rect("Content",vp,0,0,width,1);scroll.viewport=vp;scroll.content=content;
            return new Flow{content=content,width=width};
        }
        RectTransform Card(Flow flow,float height)
        {
            var card=Panel(flow.content,0,flow.y,flow.width-6,height);flow.y+=height+14;flow.content.sizeDelta=new Vector2(flow.width,flow.y);return card;
        }
        string Details(CampaignBeetle b)
        {
            string hearts=new string('♥',Mathf.Clamp((int)Math.Ceiling(b.hearts),0,5))+new string('♡',5-Mathf.Clamp((int)Math.Ceiling(b.hearts),0,5));
            string status=b.dead?"已死亡":b.overturned?$"翻身求助！已挣扎 {b.overturnSeconds:0} 秒":!b.Adult(State.week)?b.feed<0?"等待选择饲料瓶":$"第 {b.readyWeek} 周出栏":b.listed?"正在挂牌":"成虫 · 状态安稳";
            return $"{hearts}  {b.hearts:0.0}/{b.maxHearts} 心    饱食 {b.nutrition:0}%\n{status}\n{Session.Catalog.Housing(b.habitat).name}   ·   {(b.female?"雌":"雄")}   ·   {b.length:0.0} mm\n身价 {b.value:N0} G   借种参考费 {CampaignEconomy.Money(b.value*.15):N0} G\n基因  {string.Join(" / ",b.genes.Select(g=>g.id+new string('★',g.stars)))}\n突变  {(string.IsNullOrEmpty(b.mutation)?"无":b.mutation)}   ·   胜场 {b.wins} / 连胜 {b.streak}";
        }
        void Confirm(string text,Action action) { var p=OpenModal(670,290);Label(p,text,32,32,606,140,25);Btn(p,"确定",32,205,290,52,()=>{CloseModal();action();});Btn(p,"取消",340,205,290,52,CloseModal); }
        RectTransform OpenModal(float w,float h)
        {
            ink=CampaignRetroTheme.Ink;muted=CampaignRetroTheme.Muted;accent=CampaignRetroTheme.Accent;
            CloseModal(); focusBeforeModal=EventSystem.current?EventSystem.current.currentSelectedGameObject:null;
            app.Modal=true;if(pageInteraction){pageInteraction.interactable=false;pageInteraction.blocksRaycasts=false;}
            if(EventSystem.current)EventSystem.current.SetSelectedGameObject(null);
            modal=Rect("Modal",canvas,0,0,1600,900);CenterFrame(modal);var dim=modal.gameObject.AddComponent<UI.Image>();dim.color=CampaignRetroTheme.Dark?CampaignRetroTheme.Hex(0x111820):CampaignRetroTheme.Hex(0x85958b);
            var panel=Panel(modal,(1600-w)/2,(900-h)/2,w,h);
            var title=Panel(panel,3,3,w-6,18);title.GetComponent<UI.Image>().color=CampaignRetroTheme.Frame;
            Component<RetroBevel>(title).Configure(0,Color.clear,Color.clear,true,CampaignRetroTheme.TitleTop,CampaignRetroTheme.TitleBottom);
            return panel;
        }
        void CloseModal()
        {
            storageOverlay=0;mailboxNotice=null;
            closingModal=true;
            var operation=rebind;rebind=null;operation?.Cancel();operation?.Dispose();
            if(settingsOpen && app){settingsOpen=false;app.SavePreferences();}
            if(modal){modal.gameObject.SetActive(false);Destroy(modal.gameObject);modal=null;}
            if(app)app.Modal=false;
            if(pageInteraction){pageInteraction.interactable=!app.Busy;pageInteraction.blocksRaycasts=true;}
            if(focusBeforeModal && focusBeforeModal.activeInHierarchy && EventSystem.current)EventSystem.current.SetSelectedGameObject(focusBeforeModal);
            focusBeforeModal=null;closingModal=false;
        }
        static void SelectFirst(Transform parent) { var b=parent.GetComponentsInChildren<UI.Button>().FirstOrDefault(x=>x.interactable);if(b&&EventSystem.current)EventSystem.current.SetSelectedGameObject(b.gameObject); }
        void CreateRounded()
        {
            const int n=64;roundTexture=new Texture2D(n,n,TextureFormat.RGBA32,false);roundTexture.wrapMode=TextureWrapMode.Clamp;var pixels=new Color[n*n];
            for(int y=0;y<n;y++)for(int x=0;x<n;x++){float dx=Mathf.Max(Mathf.Abs(x-31.5f)-7.5f,0),dy=Mathf.Max(Mathf.Abs(y-31.5f)-7.5f,0);float a=Mathf.Clamp01(24-Mathf.Pow(Mathf.Pow(dx,2.7f)+Mathf.Pow(dy,2.7f),1/2.7f));pixels[y*n+x]=new Color(1,1,1,a);}
            roundTexture.SetPixels(pixels);roundTexture.Apply(false,true);rounded=Sprite.Create(roundTexture,new Rect(0,0,n,n),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(25,25,25,25));
        }
        void OnDestroy()
        {
            CampaignLocalization.Changed-=QueueRefresh;
            if(app)app.Changed-=QueueRefresh;if(leagueBridge)leagueBridge.Finished-=QueueRefresh;CloseModal();
            if(rounded)Destroy(rounded);if(roundTexture)Destroy(roundTexture);foreach(var t in thumbnails)if(t)Destroy(t);
        }
    }
}
