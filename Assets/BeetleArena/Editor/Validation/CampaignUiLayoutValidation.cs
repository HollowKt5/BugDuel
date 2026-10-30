using System;
using System.Linq;
using System.Reflection;
using BeetleArena.Campaign;
using BugDuel.Campaign;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CampaignUiLayoutValidation
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;

    [MenuItem("Tools/甲壳虫斗/验证/界面与画面/UI 布局参数",false,618)]
    public static void Run()=>Debug.Log(RunChecks());

    // Inactive preview hierarchies only: no Play, input simulation, saves or scene writes.
    public static string RunChecks()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before layout logic validation.");
        int checks=0;
        void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks++;}
        void Near(float actual,float expected,string name)=>Check(Mathf.Abs(actual-expected)<.02f,name+": "+actual+" / "+expected);
        using(var f=new Fixture(false))
        {
            f.Call("DrawMenu");
            var settings=(RectTransform)f.Root.Find("设置");
            Near(settings.anchoredPosition.x,100,"default menu x");Near(settings.anchoredPosition.y,-448,"default menu settings row without session");
            Near(settings.rect.width,440,"default menu button width");
            f.Call("UpdateLayoutConfiguration");f.Set("refreshQueued",false);f.Call("UpdateLayoutConfiguration");
            Check(!(bool)f.Get("refreshQueued"),"unchanged layout does not request another rebuild");
            f.Profile.menu.buttonGap=25;
            typeof(CampaignUiLayoutProfile).GetMethod("OnValidate",Private).Invoke(f.Profile,null);
            f.Call("UpdateLayoutConfiguration");
            Check((bool)f.Get("refreshQueued"),"Inspector revision requests a rebuild");
        }
        using(var f=new Fixture(true))
        {
            f.Call("DrawMenu");Near(((RectTransform)f.Root.Find("设置")).anchoredPosition.y,-526,"default menu with continue button");
        }
        using(var f=new Fixture(false))
        {
            f.Profile.menu.buttonsPosition=new Vector2(160,330);f.Profile.menu.buttonSize=new Vector2(360,64);f.Profile.menu.buttonGap=12;
            f.Call("DrawMenu");var settings=(RectTransform)f.Root.Find("设置");
            Near(settings.anchoredPosition.x,160,"configured menu x");Near(settings.anchoredPosition.y,-406,"configured menu spacing");Near(settings.rect.width,360,"configured button width");
        }
        using(var f=new Fixture(true))
        {
            var m=f.Profile.market;m.topLeft=new Vector2(24,16);m.bottomRight=new Vector2(20,18);m.categoryWidth=320;m.cardHeight=340;m.thumbnailSize=130;m.cardPadding=20;m.gap=new Vector2(16,18);
            f.Set("marketCategory",2);f.Call("DrawMarket");
            var window=(RectTransform)f.Root.Find("MarketWindow");Near(window.anchoredPosition.x,24,"market window inset");Near(window.rect.width,1556,"market right inset");
            var goods=(RectTransform)window.Find("Goods");Near(goods.anchoredPosition.x,329,"goods follow category width");
            var grid=goods.GetComponentInChildren<UnityEngine.UI.GridLayoutGroup>(true);
            Check(grid.childAlignment==TextAnchor.UpperLeft,"goods remain left aligned");Near(grid.cellSize.y,340,"grid reads card height");Near(grid.spacing.x,16,"grid reads gap");
            var card=grid.transform.Cast<Transform>().First(t=>t.name=="GoodsCard") as RectTransform;
            var thumbnail=(RectTransform)card.Find("Thumbnail");Near(thumbnail.rect.width,130,"card reads thumbnail size");Near(thumbnail.anchoredPosition.x,20,"card reads inner padding");
            var price=(RectTransform)card.Find("PriceRow");Near(-price.anchoredPosition.y+price.rect.height,340-(CampaignRetroTheme.Paper?13:3),"price row stays inside the card frame");
            m.topLeft=new Vector2(9999,9999);m.bottomRight=new Vector2(9999,9999);
            var rect=(Rect)typeof(CampaignView).GetProperty("MarketWindowRect",Private).GetValue(f.View);
            Check(rect.xMax<=1600 && rect.yMax<=900,"market excessive margins remain inside canvas");
            Check((rect.width-652)/2>=272,"market menu clears currency at minimum window width");
        }
        using(var f=new Fixture(true))
        {
            f.Profile.greenhouse.sidebarFraction=.22f;f.Profile.warehouse.detailWidthScale=1.5f;
            f.Profile.warehouse.padding=16;f.Profile.warehouse.cardPadding=14;f.Profile.warehouse.thumbnailHeight=110;f.Profile.warehouse.cardHeight=220;
            f.Set("warehouseCategory",5);f.Set("warehouseProgress",1f);f.Call("DrawWarehouseSidebar",352f,900f);
            var details=(RectTransform)f.Get("warehouseDetails");Near(details.rect.width,528,"warehouse width follows shared sidebar");Near(details.anchoredPosition.x,352,"warehouse second column origin");
            float fraction=(float)typeof(CampaignView).GetProperty("ExpandedSidebarFraction",Private).GetValue(f.View);
            Near(fraction,.55f,"scene viewport and warehouse use one width formula");
            var button=details.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single();
            Near(((RectTransform)button.transform).rect.height,220,"warehouse card height");
            var thumbnail=(RectTransform)button.transform.Find("ItemThumbnail");Near(thumbnail.rect.height,110,"warehouse thumbnail height");Near(thumbnail.anchoredPosition.x,14,"warehouse card padding");
            var text=button.GetComponentsInChildren<TMPro.TMP_Text>(true).Where(t=>t.text.Length>0).ToArray();
            Check(text.All(t=>-t.rectTransform.anchoredPosition.y+t.rectTransform.rect.height<=220-9),"warehouse text clears paper bottom edge");
        }
        using(var f=new Fixture(false))
        {
            var s=f.Profile.settings;s.size=new Vector2(1200,800);s.padding=50;s.labelFraction=.45f;s.columnGap=24;s.rowsTop=185;s.rowStep=68;
            f.Root.sizeDelta=new Vector2(s.size.x,s.Height);
            f.Call("SettingRow",f.Root,"label",590f,"value",(Action)(()=>{}),true);
            var button=(RectTransform)f.Root.Find("value");Near(button.anchoredPosition.x,569,"settings label column and gap");Near(button.anchoredPosition.y,-661,"settings row spacing");Near(button.anchoredPosition.x+button.rect.width,1150,"settings right padding");
            f.Root.sizeDelta=new Vector2(1050,760);f.Profile.common.dialogOffset=new Vector2(120,30);s.offset=new Vector2(20,-10);
            f.Call("PlaceDialog",f.Root,"OpenSettings");Near(f.Root.anchoredPosition.x,140,"settings-specific offset");Near(f.Root.anchoredPosition.y,-20,"offset y convention");
            f.Profile.common.dialogOffset=new Vector2(9999,9999);f.Call("PlaceDialog",f.Root,"Confirm");
            Near(f.Root.anchoredPosition.x,263,"dialog horizontal boundary");Near(f.Root.anchoredPosition.y,-58,"dialog vertical boundary");
            string json=EditorJsonUtility.ToJson(f.Profile);
            var copy=ScriptableObject.CreateInstance<CampaignUiLayoutProfile>();
            try{EditorJsonUtility.FromJsonOverwrite(json,copy);Near(copy.settings.padding,50,"layout asset serialization");Near(copy.settings.rowStep,68,"nested settings serialization");}
            finally{UnityEngine.Object.DestroyImmediate(copy);}
            f.Profile.warehouse.cardHeight=1;f.Profile.warehouse.thumbnailHeight=160;f.Profile.market.cardHeight=1;f.Profile.market.thumbnailSize=160;f.Profile.Validate();
            Check(f.Profile.warehouse.TileHeight>=f.Profile.warehouse.thumbnailHeight+80,"tall thumbnail expands warehouse card");
            Check(f.Profile.market.CardHeight>280,"tall thumbnail expands market card");
        }
        return "PASS: "+checks+" UI layout logic checks (Edit mode, no gameplay or save writes).";
    }

    public static string RunMarketChecks()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before market layout checks.");
        const string styleKey="BugDuel.UI.Style",modeKey="BugDuel.Retro.Dark";
        bool hadStyle=PlayerPrefs.HasKey(styleKey),hadMode=PlayerPrefs.HasKey(modeKey);
        int oldStyle=PlayerPrefs.GetInt(styleKey),oldMode=PlayerPrefs.GetInt(modeKey),checks=0,pages=0;
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);checks++;}
        try
        {
            for(int style=0;style<2;style++)for(int dark=0;dark<2;dark++)
            for(int variant=0;variant<15;variant++)
            using(var f=new Fixture(true))
            {
                PlayerPrefs.SetInt(styleKey,style);PlayerPrefs.SetInt(modeKey,dark);
                f.Root.sizeDelta=new Vector2(variant%2==0?1280:1600,900);
                f.View.page=CampaignPage.Market;
                var state=((CampaignApp)f.Get("app")).Session.State;var beetle=state.beetles[0];
                int tab=variant<6?0:variant==6?1:variant<12?2:3;
                f.Set("marketTab",tab);f.Set("marketCategory",Mathf.Min(variant,5));
                f.Set("championAuction",variant>=9&&variant<12);f.Set("marketSellCategory",Mathf.Max(0,variant-12));
                if(variant==6)beetle.listed=true;
                if(variant==8)
                {
                    foreach(var lot in state.marketAuctions){lot.closed=true;lot.won=true;lot.result="竞得拍品，已寄送至邮箱。";}
                    state.pupaAuction=new PupaAuction{round=2,bid=300,beetle=beetle};
                }
                if(variant==10)beetle.championships=1;
                if(variant==11)state.auction=new AuctionState{beetleId=beetle.id,round=4,rounds=4,bid=1560,bidder="收藏家"};
                if(variant>=9&&variant<12)state.hall.Add(new HallRecord{name="冠军记录",speciesId=beetle.speciesId,length=68,price=1550,week=1,royalties=100,mutation="普通"});
                if(variant==6||variant==13)state.specimens.Add(new BugDuel.Campaign.CampaignSpecimen{id="layout-specimen",beetle=beetle,value=300,preparedWeek=1,listed=variant==6});
                if(variant==8){f.Profile.market.cardPadding=24;f.Profile.market.thumbnailSize=160;f.Profile.market.auctionCardHeight=360;}
                f.Call("DrawMarket");pages++;
                var window=(RectTransform)f.Root.Find("MarketWindow");
                Check(window.rect.width<=f.Root.rect.width+.1f,"Market must fit its design canvas.");
                var tabs=window.Find("Tabs");string[] labels={"商店","交易市场","拍卖","出售"};
                for(int i=0;i<4;i++)
                {
                    var image=tabs.Find(labels[i]).GetComponent<UnityEngine.UI.Image>();
                    Check(image.color==(i==tab?CampaignRetroTheme.Window:CampaignRetroTheme.InactiveTab),"All primary tab states must share the theme.");
                }
                foreach(var grid in window.GetComponentsInChildren<UnityEngine.UI.GridLayoutGroup>(true))
                {
                    Check(grid.childAlignment==TextAnchor.UpperLeft,"Every market grid must align to the upper left.");
                    Check(grid.padding.left==f.Profile.market.gridPadding&&grid.spacing==f.Profile.market.gap,"Market grids must read the shared Inspector settings.");
                }
                foreach(var footer in window.GetComponentsInChildren<RectTransform>(true).Where(t=>t.name=="PriceRow"))
                {
                    var card=(RectTransform)footer.parent;var header=(RectTransform)card.Find("CardHeader");
                    float edge=CampaignRetroTheme.Paper?8:3,shadow=CampaignRetroTheme.Paper?5:0;
                    Check(header&&header.anchoredPosition.x==edge&&footer.anchoredPosition.x==edge,"Every card header and footer must share the safe border inset.");
                    Check(header.rect.width==footer.rect.width&&header.rect.width<=card.rect.width-edge*2-shadow+.1f,"Card bands must not paint over the outside frame.");
                    Check(Mathf.Abs(-footer.anchoredPosition.y+footer.rect.height-(card.rect.height-edge-shadow))<.1f,"Card actions must clear the paper shadow.");
                    Check(!(bool)typeof(RetroBevel).GetField("foldCorner",Private).GetValue(card.GetComponent<RetroBevel>()),"All market cards must use the continuous outline.");
                    foreach(var text in card.GetComponentsInChildren<TMPro.TMP_Text>(true))
                    {
                        Check(text.fontSize>=13,"Market cards must not retain the old tiny auction font.");
                        if(text.transform.parent!=card)continue;
                        float bottom=-text.rectTransform.anchoredPosition.y+text.rectTransform.rect.height;
                        Check(bottom<=-footer.anchoredPosition.y+.1f,"Card text must stop before the action bar: "+card.name);
                    }
                    foreach(var button in footer.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                    {
                        var rect=(RectTransform)button.transform;
                        Check(rect.anchoredPosition.x>=4&&rect.anchoredPosition.x+rect.rect.width<=footer.rect.width-4+.1f,"Card actions must stay inside the footer.");
                    }
                }
                foreach(var button in window.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                    Check(button.targetGraphic&&button.targetGraphic.raycastTarget&&((RectTransform)button.transform).rect.width>0,"Market buttons must retain their clickable target.");
            }
            return "PASS: "+checks+" market checks across "+pages+" page/state/theme combinations; inactive UI only.";
        }
        finally
        {
            if(hadStyle)PlayerPrefs.SetInt(styleKey,oldStyle);else PlayerPrefs.DeleteKey(styleKey);
            if(hadMode)PlayerPrefs.SetInt(modeKey,oldMode);else PlayerPrefs.DeleteKey(modeKey);
        }
    }

    sealed class Fixture : IDisposable
    {
        readonly Scene scene;
        readonly GameObject host;
        public readonly CampaignUiLayoutProfile Profile;
        public readonly CampaignView View;
        public readonly RectTransform Root;
        public Fixture(bool session)
        {
            scene=EditorSceneManager.NewPreviewScene();
            host=new GameObject("Layout logic fixture");host.SetActive(false);SceneManager.MoveGameObjectToScene(host,scene);
            var app=host.AddComponent<CampaignApp>();
            if(session)typeof(CampaignApp).GetProperty("Session").SetValue(app,CampaignSession.New(CampaignCatalog.Defaults(),731));
            View=host.AddComponent<CampaignView>();View.page=CampaignPage.MainMenu;
            Profile=ScriptableObject.CreateInstance<CampaignUiLayoutProfile>();Profile.Validate();
            var canvas=NewRect("Canvas",host.transform);Root=NewRect("Page",canvas);
            Set("app",app);Set("canvas",canvas);Set("root",Root);Set("layoutProfile",Profile);
        }
        static RectTransform NewRect(string name,Transform parent)
        {var rt=(RectTransform)new GameObject(name,typeof(RectTransform)).transform;rt.SetParent(parent,false);rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(.5f,.5f);rt.sizeDelta=new Vector2(1600,900);return rt;}
        public void Set(string field,object value)=>typeof(CampaignView).GetField(field,Private).SetValue(View,value);
        public object Get(string field)=>typeof(CampaignView).GetField(field,Private).GetValue(View);
        public object Call(string method,params object[] args)=>typeof(CampaignView).GetMethod(method,Private).Invoke(View,args);
        public void Dispose(){EditorSceneManager.ClosePreviewScene(scene);UnityEngine.Object.DestroyImmediate(Profile);}
    }
}
