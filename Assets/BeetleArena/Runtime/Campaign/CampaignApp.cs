using System;
using System.IO;
using System.Collections;
using BugDuel.Campaign;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

namespace BeetleArena.Campaign
{
    public enum CampaignPage { MainMenu, Saves, Greenhouse, Catching, Market, League, Arena, Auction }
    [Serializable] public sealed class CampaignSave
    {
        public int version = 1;
        public string savedAt, thumbnail;
        public CampaignState state;
        public CampaignCatalog catalog;
        public bool hasBattle, hasAuction, hasPupaAuction;
    }
    [Serializable] public sealed class CampaignPreferences
    {
        public int language, autoSaveSeconds = 60, battleSpeed = 1, fps = 60, resolution;
        public int windowMode = 1;
        public bool vsync = true, depthOfField = true;
        public float master = .8f, music = .35f, nature = .5f, carapace = .7f;
        public string inputOverrides;
    }
    public sealed class CampaignApp : MonoBehaviour
    {
        public static CampaignApp Instance { get; private set; }
        public CampaignSession Session { get; private set; }
        public CampaignPreferences Preferences { get; private set; } = new CampaignPreferences();
        public string Message { get; private set; } = "";
        public bool Busy { get; private set; }
        public bool Modal { get; set; }
        public InputActionMap Controls { get; private set; }
        public event Action Changed;
        public string SaveDirectory => Path.Combine(Application.persistentDataPath,"Campaign");
        public string ContentPath => Path.Combine(SaveDirectory,"campaign-content.json");
        public string SlotPath(int slot) { if (slot < 0 || slot > 3) throw new ArgumentOutOfRangeException(nameof(slot)); return Path.Combine(SaveDirectory,"slot-"+slot+".json"); }
        CanvasGroup fade;
        float careClock, saveClock;
        CampaignCatalog defaultCatalog;
        string latestThumbnail;
        bool dirty;
        public static CampaignApp Ensure()
        {
            if (!Instance) new GameObject("CampaignApp").AddComponent<CampaignApp>();
            return Instance;
        }
        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance=this; DontDestroyOnLoad(gameObject); Directory.CreateDirectory(SaveDirectory);
            defaultCatalog=CampaignCatalog.Defaults();
            try
            {
                string builtIn=Path.Combine(Application.streamingAssetsPath,"campaign-content.json");
                if (File.Exists(builtIn)) { var c=JsonUtility.FromJson<CampaignCatalog>(File.ReadAllText(builtIn)); c.Validate(); defaultCatalog=c; }
                if (!File.Exists(ContentPath)) AtomicWrite(ContentPath,JsonUtility.ToJson(defaultCatalog,true));
                string path=Path.Combine(SaveDirectory,"settings.json");
                if (File.Exists(path)) Preferences=JsonUtility.FromJson<CampaignPreferences>(File.ReadAllText(path)) ?? new CampaignPreferences();
            }
            catch(Exception e) { Message="配置读取失败，使用内置设置："+e.Message; }
            Controls=new InputActionMap("Campaign");
            Controls.AddAction("Back",InputActionType.Button,"<Keyboard>/escape").AddBinding("<Gamepad>/buttonEast");
            Controls.AddAction("NextWeek",InputActionType.Button,"<Keyboard>/n").AddBinding("<Gamepad>/rightShoulder");
            Controls.AddAction("Help",InputActionType.Button,"<Keyboard>/h").AddBinding("<Gamepad>/buttonWest");
            if (!string.IsNullOrEmpty(Preferences.inputOverrides)) try { Controls.LoadBindingOverridesFromJson(Preferences.inputOverrides); } catch { Message="按键配置无效，已使用默认按键"; }
            Controls.Enable(); ApplyPreferences(); gameObject.AddComponent<CampaignAudio>();
            var canvas=new GameObject("SceneFade",typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster)); canvas.transform.SetParent(transform);
            canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay; canvas.GetComponent<Canvas>().sortingOrder=1000;
            var go=new GameObject("Fade",typeof(RectTransform),typeof(UnityEngine.UI.Image),typeof(CanvasGroup)); go.transform.SetParent(canvas.transform,false);
            var rt=(RectTransform)go.transform; rt.anchorMin=Vector2.zero; rt.anchorMax=Vector2.one; rt.offsetMin=rt.offsetMax=Vector2.zero;
            go.GetComponent<UnityEngine.UI.Image>().color=new Color(.025f,.055f,.047f); fade=go.GetComponent<CanvasGroup>(); fade.alpha=0; fade.blocksRaycasts=false;
        }
        public bool Execute(Action<CampaignSession> command)
        {
            if (Session == null || Busy) return false;
            try
            {
                var candidate=new CampaignSession(CloneState(Session.State),Session.Catalog);
                command(candidate); candidate.Validate(); WriteSlot(0,candidate);
                Session=candidate; Message=candidate.State.notice; dirty=false; Changed?.Invoke(); return true;
            }
            catch(Exception e) { Message=e.Message; Changed?.Invoke(); return false; }
        }
        public void NewGame()
        {
            try
            {
                var c=JsonUtility.FromJson<CampaignCatalog>(File.ReadAllText(ContentPath)); c.Validate();
                var candidate=CampaignSession.New(c,unchecked((uint)DateTime.UtcNow.Ticks)); candidate.Validate(); latestThumbnail=null; WriteSlot(0,candidate); Session=candidate;
                Message=Session.State.notice; Go(CampaignPage.Greenhouse);
            }
            catch(Exception e) { Message="无法新建游戏："+e.Message; Changed?.Invoke(); }
        }
        public CampaignSave ReadSlot(int slot)
        {
            var path=SlotPath(slot); if (!File.Exists(path)) return null;
            var info=new FileInfo(path); if (info.Length > 8000000) throw new IOException("存档过大");
            var save=JsonUtility.FromJson<CampaignSave>(File.ReadAllText(path));
            if (save == null || save.version != 1 || save.state == null || save.catalog == null) throw new InvalidDataException("不支持或损坏的存档");
            RestoreOptional(save.state,save.hasBattle,save.hasAuction,save.hasPupaAuction);
            new CampaignSession(save.state,save.catalog).Validate(); return save;
        }
        public void LoadSlot(int slot)
        {
            try
            {
                var save=ReadSlot(slot); if (save == null) throw new IOException("这是一个空槽位");
                var candidate=new CampaignSession(save.state,save.catalog); latestThumbnail=save.thumbnail; WriteSlot(0,candidate); Session=candidate; dirty=false;
                Message="已读取第 "+Session.State.week+" 周存档。"; Go(CampaignPage.Greenhouse);
            }
            catch(Exception e) { Message="读取失败，原文件保留："+e.Message; Changed?.Invoke(); }
        }
        public bool SaveSlot(int slot)
        {
            bool saved=false;
            try { if (Session == null) throw new InvalidOperationException("请先开始游戏"); CaptureThumbnail(); WriteSlot(slot,Session); dirty=false; saved=true; Message="已保存到 "+(slot==0?"自动槽":"手动槽 "+slot); }
            catch(Exception e) { Message="保存失败："+e.Message; }
            Changed?.Invoke();return saved;
        }
        public void DeleteSlot(int slot)
        {
            try { string path=SlotPath(slot); if (File.Exists(path)) File.Delete(path); if (File.Exists(path+".bak")) File.Delete(path+".bak"); Message="槽位已删除"; }
            catch(Exception e) { Message="删除失败："+e.Message; }
            Changed?.Invoke();
        }
        void WriteSlot(int slot,CampaignSession session)
        {
            var save=new CampaignSave { savedAt=DateTime.Now.ToString("yyyy-MM-dd HH:mm"),state=session.State,catalog=session.Catalog,thumbnail=latestThumbnail,
                hasBattle=session.State.battle!=null,hasAuction=session.State.auction!=null,hasPupaAuction=session.State.pupaAuction!=null };
            AtomicWrite(SlotPath(slot),JsonUtility.ToJson(save,true));
        }
        public static void AtomicWrite(string path,string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)); string temp=path+".tmp";
            using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None))
            { var bytes=System.Text.Encoding.UTF8.GetBytes(text); stream.Write(bytes,0,bytes.Length); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temp,path,path+".bak"); else File.Move(temp,path);
        }
        public void ReloadContent()
        {
            try
            {
                if (Session == null) throw new InvalidOperationException("先进入温室");
                if (Session.State.battle != null || Session.State.auction != null || Session.State.pupaAuction != null) throw new InvalidOperationException("请先完成赛事与拍卖再热更");
                var info=new FileInfo(ContentPath); if (info.Length>1000000) throw new IOException("内容包超过1MB");
                var c=JsonUtility.FromJson<CampaignCatalog>(File.ReadAllText(ContentPath)); c.Validate();
                if (c.revision <= Session.Catalog.revision) throw new InvalidOperationException("请提高内容 revision 后再重载");
                var candidate=new CampaignSession(CloneState(Session.State),c); candidate.Validate();
                WriteSlot(0,candidate); Session=candidate; Message="内容已原子更新至 r"+c.revision+"；占用中的物种与环境均已校验。";
            }
            catch(Exception e) { Message="热更未应用："+e.Message; }
            Changed?.Invoke();
        }
        public void Select(string id) { if (Session == null) return; Session.Beetle(id); Session.State.selectedId=id; Changed?.Invoke(); }
        public static CampaignState CloneState(CampaignState source)
        {
            var state=JsonUtility.FromJson<CampaignState>(JsonUtility.ToJson(source));
            RestoreOptional(state,source.battle!=null,source.auction!=null,source.pupaAuction!=null);return state;
        }
        public static void RestoreOptional(CampaignState state,bool battle,bool auction,bool pupa)
        {
            // Unity's inline class serialization does not reliably preserve null references.
            if(!battle)state.battle=null;if(!auction)state.auction=null;if(!pupa)state.pupaAuction=null;
        }
        void Update()
        {
            if (Session == null || Busy || Modal) return;
            string scene=SceneManager.GetActiveScene().name;
            if (scene == "CampaignArena" || scene == "CampaignMainMenu" || scene == "CampaignSaves") return;
            careClock+=Time.unscaledDeltaTime; saveClock+=Time.unscaledDeltaTime;
            if (careClock>=1) { string previous=Session.State.notice; Session.CareSeconds(1); careClock=0; dirty=true; if(previous!=Session.State.notice) { Message=Session.State.notice;Changed?.Invoke(); } }
            if (saveClock>=Mathf.Clamp(Preferences.autoSaveSeconds,30,300)) { saveClock=0; if (dirty) SaveSlot(0); }
        }
        public static string SceneName(CampaignPage page) => "Campaign"+page;
        public void Go(CampaignPage page) { if (!Busy) StartCoroutine(Transition(page)); }
        IEnumerator Transition(CampaignPage page)
        {
            Busy=true; Modal=false; fade.blocksRaycasts=true;
            if (Session != null && SceneManager.GetActiveScene().name==SceneName(CampaignPage.Greenhouse)) { CaptureThumbnail(); try { WriteSlot(0,Session); } catch(Exception e) { Message=e.Message; Busy=false; fade.blocksRaycasts=false; yield break; } }
            var menuCamera=SceneManager.GetActiveScene().name==SceneName(CampaignPage.MainMenu)?Camera.main:null;
            float startSize=menuCamera?menuCamera.orthographicSize:0;
            for(float t=0;t<.22f;t+=Time.unscaledDeltaTime) { fade.alpha=t/.22f;if(menuCamera)menuCamera.orthographicSize=Mathf.Lerp(startSize,startSize*.88f,Mathf.SmoothStep(0,1,t/.22f));yield return null; }
            fade.alpha=1;
            var operation=SceneManager.LoadSceneAsync(SceneName(page));
            if (operation != null) while (!operation.isDone) yield return null;
            for(float t=0;t<.24f;t+=Time.unscaledDeltaTime) { fade.alpha=1-t/.24f; yield return null; }
            fade.alpha=0; fade.blocksRaycasts=false; Busy=false;
        }
        public void CaptureThumbnail()
        {
            var view=FindFirstObjectByType<CampaignBackdrop>(); if (!view || !view.sceneCamera || SceneManager.GetActiveScene().name != SceneName(CampaignPage.Greenhouse)) return;
            var rt=RenderTexture.GetTemporary(320,180,0); var old=RenderTexture.active; Texture2D tex=null;
            try { Graphics.Blit(view.Frame,rt); RenderTexture.active=rt; tex=new Texture2D(320,180,TextureFormat.RGB24,false); tex.ReadPixels(new Rect(0,0,320,180),0,0); tex.Apply(); latestThumbnail=Convert.ToBase64String(tex.EncodeToJPG(65)); }
            finally { RenderTexture.active=old; RenderTexture.ReleaseTemporary(rt); if (tex) Destroy(tex); }
        }
        public void ApplyPreferences()
        {
            AudioListener.volume=Mathf.Clamp01(Preferences.master); QualitySettings.vSyncCount=Preferences.vsync?1:0; Application.targetFrameRate=Preferences.fps;
            if (!Application.isEditor)
            {
                int[,] sizes={{1280,720},{1600,900},{1920,1080},{2560,1440}}; int r=Mathf.Clamp(Preferences.resolution,0,3);
                Screen.SetResolution(sizes[r,0],sizes[r,1],Preferences.windowMode==0?FullScreenMode.Windowed:Preferences.windowMode==1?FullScreenMode.FullScreenWindow:FullScreenMode.ExclusiveFullScreen);
            }
            var audio=FindFirstObjectByType<ArenaAudio>(); if (audio) audio.volume=Preferences.carapace;
        }
        public void SavePreferences()
        {
            try { Preferences.inputOverrides=Controls.SaveBindingOverridesAsJson(); AtomicWrite(Path.Combine(SaveDirectory,"settings.json"),JsonUtility.ToJson(Preferences,true)); ApplyPreferences(); Message="设置已保存"; }
            catch(Exception e) { Message="设置保存失败："+e.Message; }
        }
        void OnApplicationPause(bool paused) { if (paused && Session != null) SaveSlot(0); }
        void OnApplicationQuit() { if (Session != null) SaveSlot(0); }
        void OnDestroy() { if (Instance==this) { Controls?.Dispose(); Instance=null; } }
    }
}
