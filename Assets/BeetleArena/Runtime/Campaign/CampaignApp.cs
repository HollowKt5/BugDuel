using System;
using System.IO;
using System.Collections;
using System.Threading.Tasks;
using BugDuel.Campaign;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

namespace BeetleArena.Campaign
{
    public enum CampaignPage { MainMenu, Saves, Greenhouse, Catching, Market, League, Arena, Auction }
    [Serializable] public sealed class CampaignPreferences
    {
        public int language, autoSaveSeconds = 60, battleSpeed = 1, fps = 60, resolution;
        public int windowMode = 1;
        public bool vsync = true, depthOfField = true;
        public CampaignTransitionMode transitionMode;
        public float master = .8f, music = .35f, nature = .5f, carapace = .7f;
        public string inputOverrides;
    }
    // Coordinates domain transactions. Storage, input policy and transitions have separate owners.
    public sealed class CampaignApp : MonoBehaviour
    {
        public static CampaignApp Instance { get; private set; }
        // Must be set before Ensure. Acceptance runs use an isolated directory inside Artifacts.
        public static string StorageOverride { get; set; }
        public CampaignSession Session { get; private set; }
        public CampaignPreferences Preferences { get; private set; } = new CampaignPreferences();
        public string Message { get; private set; } = "";
        public bool Busy => pendingWrite!=null || (transition && transition.Busy);
        public bool Modal { get; set; }
        public InputActionMap Controls { get; private set; }
        public event Action Changed;
        internal Func<string, bool> IsCareEating { get; set; }
        public string SaveDirectory => repository.DirectoryPath;
        public string ContentPath => Path.Combine(SaveDirectory,"campaign-content.json");
        public string SlotPath(int slot) => repository.SlotPath(slot);
        CampaignSaveRepository repository;
        CampaignSceneTransition transition;
        Task pendingWrite;
        CampaignSession pendingSession;
        Action afterCommit;
        bool notifyCommit;
        string commitMessage;
        float careClock, saveClock;
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
            Instance=this; DontDestroyOnLoad(gameObject);
            repository=new CampaignSaveRepository(StorageOverride ?? Path.Combine(Application.persistentDataPath,"Campaign"));
            var catalog=CampaignCatalog.Defaults();
            try
            {
                string builtIn=Path.Combine(Application.streamingAssetsPath,"campaign-content.json");
                if(File.Exists(builtIn)) { var c=JsonUtility.FromJson<CampaignCatalog>(File.ReadAllText(builtIn)); c.Validate(); catalog=c; }
                if(!File.Exists(ContentPath))AtomicWrite(ContentPath,JsonUtility.ToJson(catalog,true));
                string path=Path.Combine(SaveDirectory,"settings.json");
                if(File.Exists(path))Preferences=JsonUtility.FromJson<CampaignPreferences>(File.ReadAllText(path)) ?? new CampaignPreferences();
            }
            catch(Exception e) { Message="配置读取失败，使用内置设置："+e.Message; }
            CampaignLocalization.Initialize(Preferences.language);
            Controls=new InputActionMap("Campaign");
            Controls.AddAction("Back",InputActionType.Button,"<Keyboard>/escape").AddBinding("<Gamepad>/buttonEast");
            Controls.AddAction("NextWeek",InputActionType.Button,"<Keyboard>/n").AddBinding("<Gamepad>/rightShoulder");
            Controls.AddAction("Help",InputActionType.Button,"<Keyboard>/h").AddBinding("<Gamepad>/buttonWest");
            if(!string.IsNullOrEmpty(Preferences.inputOverrides))try { Controls.LoadBindingOverridesFromJson(Preferences.inputOverrides); } catch { Message="按键配置无效，已使用默认按键"; }
            transition=gameObject.AddComponent<CampaignSceneTransition>();
            Controls.Enable(); ApplyPreferences(); gameObject.AddComponent<CampaignAudio>();
        }
        // True means accepted. Dependent actions run only after the durable commit callback.
        public bool Execute(Action<CampaignSession> command,Action committed=null)
        {
            if(Session==null || Busy)return false;
            try
            {
                var candidate=new CampaignSession(CloneState(Session.State),Session.Catalog);
                command(candidate); candidate.Validate();
                BeginCommit(candidate,candidate.State.notice,committed); return true;
            }
            catch(Exception e) { Message=e.Message; return false; }
        }
        public bool Manage(CampaignPage page,string action)
        {
            if(!CampaignInputPolicy.CanManage(page,Session!=null,Busy,Modal))return false;
            if(action=="NextWeek")return Execute(s=>s.NextWeek());
            if(action=="Help")return Execute(s=>s.Rescue(s.State.selectedId));
            return false;
        }
        void BeginCommit(CampaignSession candidate,string message,Action completed=null,bool notify=true,int slot=0,bool newJourney=false)
        {
            if(Busy)throw new InvalidOperationException("请等待当前保存或转场完成");
            pendingSession=candidate; commitMessage=message; afterCommit=completed; notifyCommit=notify;
            var snapshot=Snapshot(candidate);if(newJourney)snapshot.thumbnail=null;
            pendingWrite=repository.WriteAsync(slot,snapshot);
        }
        void FinishCommit(bool invokeCallback)
        {
            if(pendingWrite==null)return;
            var write=pendingWrite; var candidate=pendingSession; var callback=afterCommit; bool notify=notifyCommit;
            pendingWrite=null; pendingSession=null; afterCommit=null;
            try { write.GetAwaiter().GetResult(); }
            catch(Exception e) { Message="保存失败，操作未提交："+e.Message; return; }
            Session=candidate; dirty=false; saveClock=0;
            if(notify) { Message=commitMessage; Changed?.Invoke(); }
            if(invokeCallback)callback?.Invoke();
        }
        CampaignSave Snapshot(CampaignSession session) => new CampaignSave
        {
            savedAt=DateTime.Now.ToString("yyyy-MM-dd HH:mm"), state=CloneState(session.State), catalog=session.Catalog, thumbnail=latestThumbnail,
            hasBattle=session.State.battle!=null, hasAuction=session.State.auction!=null, hasPupaAuction=session.State.pupaAuction!=null
        };
        public void NewGame(int slot=0)
        {
            if(Busy)return;
            try
            {
                if(File.Exists(SlotPath(slot)))throw new InvalidOperationException("此槽位已有存档，请选择空槽或先删除旧存档");
                var c=JsonUtility.FromJson<CampaignCatalog>(File.ReadAllText(ContentPath)); c.Validate();
                var candidate=CampaignSession.New(c,unchecked((uint)DateTime.UtcNow.Ticks)); candidate.Validate();
                BeginCommit(candidate,candidate.State.notice,()=>
                {
                    latestThumbnail=null;careClock=0;
                    // The chosen slot is durable before entering play. Subsequent saves use the auto slot.
                    dirty=slot!=0;Navigate(CampaignPage.Greenhouse);
                },slot:slot,newJourney:true);
            }
            catch(Exception e) { Message="无法新建游戏："+e.Message; }
        }
        public CampaignSave ReadSlot(int slot) => repository.Read(slot);
        public void LoadSlot(int slot)
        {
            if(Busy)return;
            try
            {
                var save=ReadSlot(slot); if(save==null)throw new IOException("这是一个空槽位");
                var candidate=new CampaignSession(save.state,save.catalog); latestThumbnail=save.thumbnail;
                BeginCommit(candidate,"已读取第 "+candidate.State.week+" 周存档。",()=>Navigate(CampaignPage.Greenhouse));
            }
            catch(Exception e) { Message="读取失败，原文件保留："+e.Message; }
        }
        // Explicit manual saves and shutdown flush synchronously; frequent transactions and autosaves do not.
        public bool SaveSlot(int slot)
        {
            if(Busy)return false;
            try
            {
                if(Session==null)throw new InvalidOperationException("请先开始游戏");
                CaptureThumbnail(); repository.Write(slot,Snapshot(Session)); dirty=false;
                Message="已保存到 "+(slot==0?"自动槽":"手动槽 "+slot);
                if(SceneManager.GetActiveScene().name==SceneName(CampaignPage.Saves))Changed?.Invoke();
                return true;
            }
            catch(Exception e) { Message="保存失败："+e.Message; return false; }
        }
        public void DeleteSlot(int slot)
        {
            if(Busy)return;
            try { repository.Delete(slot); Message="槽位已删除"; Changed?.Invoke(); }
            catch(Exception e) { Message="删除失败："+e.Message; }
        }
        public static void AtomicWrite(string path,string text) => CampaignSaveRepository.AtomicWrite(path,text);
        public void ReloadContent()
        {
            if(Busy)return;
            try
            {
                if(Session==null)throw new InvalidOperationException("先进入温室");
                if(Session.State.battle!=null || Session.State.auction!=null || Session.State.pupaAuction!=null)throw new InvalidOperationException("请先完成赛事与拍卖再热更");
                if(new FileInfo(ContentPath).Length>1000000)throw new IOException("内容包超过1MB");
                var c=JsonUtility.FromJson<CampaignCatalog>(File.ReadAllText(ContentPath)); c.Validate();
                if(c.revision<=Session.Catalog.revision)throw new InvalidOperationException("请提高内容 revision 后再重载");
                var candidate=new CampaignSession(CloneState(Session.State),c); candidate.Validate();
                BeginCommit(candidate,"内容已原子更新至 r"+c.revision+"；占用中的物种与环境均已校验。");
            }
            catch(Exception e) { Message="热更未应用："+e.Message; }
        }
        public void Select(string id)
        {
            if(Session==null || Busy)return; Session.Beetle(id); Session.State.selectedId=id; dirty=true; Changed?.Invoke();
        }
        public static CampaignState CloneState(CampaignState source) => CampaignSnapshots.Copy(source);
        void Update()
        {
            if(pendingWrite!=null && pendingWrite.IsCompleted)FinishCommit(true);
            if(Session==null || Busy || Modal)return;
            if(!Enum.TryParse(SceneManager.GetActiveScene().name.Replace("Campaign",""),out CampaignPage page) ||
                !CampaignInputPolicy.CanManage(page,true,Busy,Modal))return;
            careClock+=Time.unscaledDeltaTime; saveClock+=Time.unscaledDeltaTime;
            if(careClock>=1)
            {
                int seconds=Mathf.FloorToInt(careClock); careClock-=seconds;
                string previous=Session.State.notice; for(int i=0;i<seconds;i++)Session.CareSeconds(1,IsCareEating); dirty=true;
                if(previous!=Session.State.notice) { Message=Session.State.notice; Changed?.Invoke(); }
            }
            if(saveClock>=Mathf.Clamp(Preferences.autoSaveSeconds,30,300))
            {
                saveClock=0;
                if(dirty)BeginCommit(new CampaignSession(CloneState(Session.State),Session.Catalog),null,null,false);
            }
        }
        public static string SceneName(CampaignPage page) => "Campaign"+page;
        public void Go(CampaignPage page)
        {
            if(Busy)return;
            if(!Application.CanStreamedLevelBeLoaded(SceneName(page))) { Message="无法载入场景："+SceneName(page); return; }
            if(Session!=null && dirty)
            {
                CaptureThumbnail();
                BeginCommit(new CampaignSession(CloneState(Session.State),Session.Catalog),Message,()=>Navigate(page),false);
            }
            else Navigate(page);
        }
        void Navigate(CampaignPage page)
        {
            Modal=false;
            string source=SceneManager.GetActiveScene().name;
            CampaignTransitionMode? mode=source==SceneName(CampaignPage.MainMenu)||source==SceneName(CampaignPage.Saves)?CampaignTransitionMode.BlackFade:(CampaignTransitionMode?)null;
            transition.Go(page,error=>Message=error,mode);
        }
        public void CaptureThumbnail()
        {
            var view=FindFirstObjectByType<CampaignBackdrop>(); if(!view || !view.Frame || SceneManager.GetActiveScene().name!=SceneName(CampaignPage.Greenhouse))return;
            var rt=RenderTexture.GetTemporary(320,180,0); var old=RenderTexture.active; Texture2D tex=null;
            try
            {
                // The observation viewport is narrower than the save thumbnail. Fit it
                // with side bars instead of stretching the beetle back to 16:9.
                RenderTexture.active=rt;GL.Clear(false,true,Color.black);
                float fit=Mathf.Min(320f/view.Frame.width,180f/view.Frame.height);
                float w=view.Frame.width*fit,h=view.Frame.height*fit;
                GL.PushMatrix();
                try { GL.LoadPixelMatrix(0,320,180,0);Graphics.DrawTexture(new Rect((320-w)*.5f,(180-h)*.5f,w,h),view.Frame); }
                finally { GL.PopMatrix(); }
                tex=new Texture2D(320,180,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,320,180),0,0);tex.Apply();latestThumbnail=Convert.ToBase64String(tex.EncodeToJPG(65));
            }
            finally { RenderTexture.active=old; RenderTexture.ReleaseTemporary(rt); if(tex)Destroy(tex); }
        }
        public void ApplyPreferences()
        {
            if(transition)transition.Mode=Preferences.transitionMode;
            AudioListener.volume=Mathf.Clamp01(Preferences.master); QualitySettings.vSyncCount=Preferences.vsync?1:0; Application.targetFrameRate=Preferences.fps;
            if(!Application.isEditor)
            {
                int[,] sizes={{1280,720},{1600,900},{1920,1080},{2560,1440}}; int r=Mathf.Clamp(Preferences.resolution,0,3);
                Screen.SetResolution(sizes[r,0],sizes[r,1],Preferences.windowMode==0?FullScreenMode.Windowed:Preferences.windowMode==1?FullScreenMode.FullScreenWindow:FullScreenMode.ExclusiveFullScreen);
            }
            var audio=FindFirstObjectByType<ArenaAudio>(); if(audio)audio.volume=Preferences.carapace;
        }
        public void SavePreferences()
        {
            try { Preferences.inputOverrides=Controls.SaveBindingOverridesAsJson(); AtomicWrite(Path.Combine(SaveDirectory,"settings.json"),JsonUtility.ToJson(Preferences,true)); ApplyPreferences(); Message="设置已保存"; }
            catch(Exception e) { Message="设置保存失败："+e.Message; }
        }
        void Flush()
        {
            if(pendingWrite!=null)FinishCommit(false);
            if(Session==null || !dirty)return;
            try { repository.Write(0,Snapshot(Session)); dirty=false; }
            catch(Exception e) { Message="保存失败："+e.Message; Debug.LogWarning(Message); }
        }
        void OnApplicationPause(bool paused) { if(paused)Flush(); }
        void OnApplicationQuit() { Flush(); }
        void OnDestroy() { if(Instance==this) { Flush(); Controls?.Dispose(); Instance=null; } }
    }
}
