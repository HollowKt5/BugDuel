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
        public CampaignBackdrop backdrop;
        public CampaignLeagueBridge leagueBridge;
        CampaignApp app;
        RectTransform canvas,root,modal;
        TMP_Text notice,careLabel;
        CampaignCareController careController;
        readonly CampaignUiTree tree=new CampaignUiTree();
        CanvasGroup pageInteraction;
        GameObject focusBeforeModal;
        bool closingModal;
        Action modalBack;
        Sprite rounded;
        Texture2D roundTexture;
        readonly List<Texture2D> thumbnails=new List<Texture2D>();
        bool refreshQueued;
        GameObject lastFocus;
        float careLabelClock;
        Color ink,muted,accent;
        CampaignObservationCamera observation;
        Vector2 lastCanvasSize;
        CampaignSession Session=>app.Session;
        CampaignState State=>Session.State;

        void Start()
        {
            app=CampaignApp.Ensure();
            if(page==CampaignPage.Auction) { page=CampaignPage.Market;RequestChampionAuction=true; }
            if(page==CampaignPage.Market&&RequestChampionAuction) {marketTab=2;championAuction=true;RequestChampionAuction=false;}
            var retroFont=Resources.Load<TMP_FontAsset>("RetroUiFont");if(retroFont)font=retroFont;
            if(page!=CampaignPage.MainMenu && page!=CampaignPage.Saves && Session==null) { app.Go(CampaignPage.MainMenu);return; }
            if(page==CampaignPage.Arena)CreateRounded();
            var go=new GameObject("CampaignCanvas",typeof(RectTransform),typeof(Canvas),typeof(UI.CanvasScaler),typeof(UI.GraphicRaycaster)); go.transform.SetParent(transform,false);
            canvas=(RectTransform)go.transform; go.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=go.GetComponent<UI.CanvasScaler>(); scaler.uiScaleMode=UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution=new Vector2(1600,900);scaler.screenMatchMode=UI.CanvasScaler.ScreenMatchMode.Expand;
            if(backdrop)
            {
                var background=Rect("World",canvas,0,0,1600,900); Stretch(background);
                backdrop.output=background.gameObject.AddComponent<UI.RawImage>(); backdrop.output.raycastTarget=false; backdrop.output.texture=backdrop.Frame;
                BeetleCombatCrt.Attach(backdrop.output);
                if(page==CampaignPage.Greenhouse)
                {
                    backdrop.SetViewport(new Rect(1f/6f,0,5f/6f,1));
                    observation=CampaignObservationCamera.Ensure(backdrop);
                    SetupConstruction();
                }
                if(page==CampaignPage.SpecimenRoom)
                {
                    backdrop.SetViewport(new Rect(.28f,0,.72f,1));
                    specimenRoom=GetComponent<CampaignSpecimenRoom>();
                    specimenRoom.Bind(backdrop);
                    specimenRoom.SlotClicked=SelectWallSlot;
                    specimenRoom.MoveRequested=(id,slot)=>app.Execute(s=>s.PlaceSpecimen(id,slot));
                }
                backdrop.SetUiFocus(page==CampaignPage.Saves || page==CampaignPage.Catching || page==CampaignPage.Market || page==CampaignPage.League || page==CampaignPage.Auction);
            }
            if(!EventSystem.current) { var e=new GameObject("CampaignEvents",typeof(EventSystem),typeof(InputSystemUIInputModule)); e.GetComponent<InputSystemUIInputModule>().AssignDefaultActions(); }
            app.Changed+=QueueRefresh;
            CampaignLocalization.Changed+=QueueRefresh;
            if(leagueBridge) leagueBridge.StateChanged+=QueueRefresh;
            Rebuild();
        }
        void QueueRefresh() { refreshQueued=true; }
        void LateUpdate()
        {
            // Keep keyboard/controller navigation inside the visible portion of scrollable lists.
            var focus=EventSystem.current?EventSystem.current.currentSelectedGameObject:null;
            if(!RetroUiNavigation.PointerMode && modal && (!focus || !focus.transform.IsChildOf(modal))) { SelectFirst(modal); return; }
            if(!focus||focus==lastFocus)return;lastFocus=focus;
            var scroll=focus.GetComponentInParent<UI.ScrollRect>();if(!scroll||!scroll.viewport||!scroll.content)return;
            Canvas.ForceUpdateCanvases();var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport,focus.transform);
            float delta=bounds.min.y<scroll.viewport.rect.yMin?scroll.viewport.rect.yMin-bounds.min.y:bounds.max.y>scroll.viewport.rect.yMax?scroll.viewport.rect.yMax-bounds.max.y:0;
            var position=scroll.content.anchoredPosition;position.y=Mathf.Clamp(position.y+delta,0,Mathf.Max(0,scroll.content.rect.height-scroll.viewport.rect.height));scroll.content.anchoredPosition=position;
        }
        void Update()
        {
            if(!app || !canvas) return;
            HandlePlayerIdentityPrompt();
            if(page==CampaignPage.Greenhouse){ApplyConstructionRequest();ReadGreenhousePointer();}
            if(dialogNotice)CampaignLocalization.Bind(dialogNotice,app.Message);
            if(lastCanvasSize!=canvas.rect.size) { lastCanvasSize=canvas.rect.size;QueueRefresh(); }
            if(pageInteraction)pageInteraction.interactable=!app.Busy&&!modal&&!(page==CampaignPage.Greenhouse&&sidebarCollapsed);
            if(refreshQueued && !modal && !app.Busy && !CampaignDecorationDragSource.Dragging) { refreshQueued=false;Rebuild(); }
            if(notice) CampaignLocalization.Bind(notice,app.Message.Length>0?app.Message:Session?.State.notice??"");
            careLabelClock+=Time.unscaledDeltaTime;
            if(careLabel && Session!=null && careLabelClock>=.25f)
            {
                careLabelClock=0;
                var b=State.beetles.Find(x=>x.id==State.selectedId); if(b!=null) CampaignLocalization.Bind(careLabel,Details(b));
            }
            if(app.Busy || rebind!=null) return;
            if(page==CampaignPage.Market && !modal && HandleMarketShortcuts())return;
            if(app.Controls["Back"].WasPressedThisFrame())
            { if(RetroFilterDropdown.ConsumeBack())return;if(modal) { if(modalBack!=null)modalBack();else CloseModal(); } else if(page==CampaignPage.Greenhouse&&HandleGreenhouseBack()){} else OpenSettings(); }
            if(app.Controls["Help"].WasPressedThisFrame()) app.Manage(page,"Help");
        }
        void Rebuild()
        {
            if(CampaignLocalization.Font)font=CampaignLocalization.Font;
            ink=CampaignRetroTheme.Ink;muted=CampaignRetroTheme.Muted;accent=CampaignRetroTheme.Accent;
            var selected=EventSystem.current?EventSystem.current.currentSelectedGameObject:null;
            if(!root)
            {
                root=Rect("Page",canvas,0,0,1600,900); CenterFrame(root);
                pageInteraction=root.gameObject.AddComponent<CanvasGroup>();
            }
            if(page==CampaignPage.Greenhouse || page==CampaignPage.SpecimenRoom)
            { root.anchorMin=Vector2.zero;root.anchorMax=new Vector2(page==CampaignPage.Greenhouse?(1+warehouseProgress)/6f:.28f,1);root.pivot=new Vector2(0,1);root.offsetMin=root.offsetMax=Vector2.zero;Canvas.ForceUpdateCanvases(); }
            if(page==CampaignPage.Market){Stretch(root);Canvas.ForceUpdateCanvases();}
            foreach(var t in thumbnails) if(t) Destroy(t); thumbnails.Clear(); careLabel=null;
            tree.Begin();
            if(page==CampaignPage.Arena) { DrawArena(); EndRefresh(selected); return; }
            if(page==CampaignPage.Market) { DrawMarket();EndRefresh(selected);return; }
            if(page==CampaignPage.Greenhouse) { DrawGreenhouse();EndRefresh(selected);return; }
            if(page==CampaignPage.SpecimenRoom) { DrawSpecimenRoom();EndRefresh(selected);return; }
            if(page==CampaignPage.MainMenu) { DrawMenu();EndRefresh(selected);return; }
            else if(page==CampaignPage.Saves) DrawSaves();
            else
            {
                Navigation();
                switch(page)
                {
                    case CampaignPage.Catching: DrawCatching();break;
                    case CampaignPage.League: DrawLeague();break;
                    case CampaignPage.Auction: DrawAuction();break;
                    case CampaignPage.Breeding: DrawBreedingCenter();break;
                }
            }
            notice=Label(root,"",50,845,1500,30,17,muted);
            notice.name="PageNotice";
            EndRefresh(selected);
        }
        void EndRefresh(GameObject selected)
        {
            if(Session!=null&&page==CampaignPage.SpecimenRoom)DrawCalendarHud();
            tree.End();
            EnsurePresentationHud();
            if(!sidebarCollapsed&&(!selected || !selected.activeInHierarchy || (selected.GetComponent<UI.Selectable>() is UI.Selectable control && !control.IsInteractable())))SelectFirst(root);
            foreach(var scroll in root.GetComponentsInChildren<UI.ScrollRect>())
            {
                var position=scroll.content.anchoredPosition;
                position.y=Mathf.Clamp(position.y,0,Mathf.Max(0,scroll.content.rect.height-scroll.viewport.rect.height));
                scroll.content.anchoredPosition=position;
            }
        }
        void Navigation()
        {
            Panel(root,32,100,1536,780,name:"ManagementPage");
            var nav=Panel(root,32,24,1536,64,name:"Navigation");
            Label(nav,"甲壳虫斗",16,18,242,36,24,accent);
            var pages=new[]{CampaignPage.Greenhouse,CampaignPage.Catching,CampaignPage.Market,CampaignPage.League,CampaignPage.Breeding};
            string[] names={"温室","捕虫队","市场","联赛","育种中心"};
            for(int i=0;i<pages.Length;i++) { var p=pages[i]; Btn(nav,names[i],270+i*126,10,116,44,()=>app.Go(p),page!=p); }
            Btn(nav,"主菜单",1090,10,110,44,()=>app.Go(CampaignPage.MainMenu));
            DrawCalendarClock(nav,1210,4,314,56);
        }
        void Title(string title,string subtitle) { Label(root,title,42,163,1180,48,32,ink); Label(root,subtitle,44,212,1480,32,18,muted); }
        void CreateRounded()
        {
            const int n=64;roundTexture=new Texture2D(n,n,TextureFormat.RGBA32,false);roundTexture.wrapMode=TextureWrapMode.Clamp;var pixels=new Color[n*n];
            for(int y=0;y<n;y++)for(int x=0;x<n;x++){float dx=Mathf.Max(Mathf.Abs(x-31.5f)-7.5f,0),dy=Mathf.Max(Mathf.Abs(y-31.5f)-7.5f,0);float a=Mathf.Clamp01(24-Mathf.Pow(Mathf.Pow(dx,2.7f)+Mathf.Pow(dy,2.7f),1/2.7f));pixels[y*n+x]=new Color(1,1,1,a);}
            roundTexture.SetPixels(pixels);roundTexture.Apply(false,true);rounded=Sprite.Create(roundTexture,new Rect(0,0,n,n),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(25,25,25,25));
        }
        void OnDestroy()
        {
            DisposeConstructionUi();
            CampaignLocalization.Changed-=QueueRefresh;
            if(app)app.Changed-=QueueRefresh;if(leagueBridge)leagueBridge.StateChanged-=QueueRefresh;CloseModal();
            if(rounded)Destroy(rounded);if(roundTexture)Destroy(roundTexture);foreach(var t in thumbnails)if(t)Destroy(t);
        }
    }
}
