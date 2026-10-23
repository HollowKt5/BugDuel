using System;
using System.IO;
using System.Threading.Tasks;
using BugDuel.Campaign;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

namespace BeetleArena.Campaign
{
    public enum CampaignPage { MainMenu, Saves, Greenhouse, Catching, Market, League, Arena, Auction, SpecimenRoom }
    public readonly struct CampaignCurrencyChange
    {
        public readonly long Before,After,Delta,Sequence;
        public CampaignCurrencyChange(long before,long after,long sequence)
        { Before=before;After=after;Delta=after-before;Sequence=sequence; }
    }
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
        public bool Constructing { get; set; }
        public bool NeedsPlayerName => Session!=null && Session.NeedsPlayerName;
        public int? PlayerNameRequestSlot { get; private set; }
        public bool CalendarRunning => Session!=null && !NeedsPlayerName && !Busy && !Modal && !Constructing && !Session.State.clock.paused &&
            Session.State.battle==null && Session.State.auction==null && Session.State.pupaAuction==null &&
            Enum.TryParse(SceneManager.GetActiveScene().name.Replace("Campaign",""),out CampaignPage currentPage) &&
            CampaignInputPolicy.CanManage(currentPage,true,false,false);
        public InputActionMap Controls { get; private set; }
        public event Action Changed;
        // Published once after a durable write. Loading another journey establishes a new baseline.
        public event Action<CampaignState,CampaignState,bool> SessionCommitted;
        public event Action<CampaignCurrencyChange> CurrencyChanged;
        public event Action<long> CurrencyReset;
        public long CurrencyBalance { get; private set; }
        public long CurrencySequence { get; private set; }
        public long CurrencyNetChange { get; private set; }
        public CampaignCurrencyChange LastCurrencyChange { get; private set; }
        internal Func<string, bool> IsCareEating { get; set; }
        public string SaveDirectory => repository.DirectoryPath;
        public string ContentPath => Path.Combine(SaveDirectory,"campaign-content.json");
        public string SlotPath(int slot) => repository.SlotPath(slot);
        CampaignSaveRepository repository;
        CampaignSceneTransition transition;
        Task pendingWrite;
        CampaignSession pendingSession;
        Action afterCommit;
        bool notifyCommit,pendingCurrencyReset,currencyReady;
        string commitMessage,pendingThumbnail;
        float careClock, saveClock, calendarClock;
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
            string builtIn=Path.Combine(Application.streamingAssetsPath,"campaign-content.json");
            if(File.Exists(builtIn) && TryLoadCatalog(builtIn,"内置内容读取失败，使用默认内容：",out var loadedCatalog))catalog=loadedCatalog;
            try
            {
                if(!File.Exists(ContentPath))AtomicWrite(ContentPath,JsonUtility.ToJson(catalog,true));
            }
            catch(Exception error) when(IsStorageFailure(error)) { ReportExternalFailure("内容文件创建失败：",error); }
            catch(Exception error) { ReportUnexpectedFailure("内容文件创建失败：",error); }
            try
            {
                string path=Path.Combine(SaveDirectory,"settings.json");
                if(File.Exists(path))Preferences=CampaignJson.Read<CampaignPreferences>(path);
            }
            catch(Exception error) when(IsExternalDataFailure(error)) { ReportExternalFailure("设置读取失败，使用默认设置：",error); }
            catch(Exception error) { ReportUnexpectedFailure("设置读取失败，使用默认设置：",error); }
            CampaignLocalization.Initialize(Preferences.language);
            Controls=new InputActionMap("Campaign");
            Controls.AddAction("Back",InputActionType.Button,"<Keyboard>/escape").AddBinding("<Gamepad>/buttonEast");
            Controls.AddAction("Help",InputActionType.Button,"<Keyboard>/h").AddBinding("<Gamepad>/buttonWest");
            if(!string.IsNullOrEmpty(Preferences.inputOverrides))
            {
                try { Controls.LoadBindingOverridesFromJson(Preferences.inputOverrides); }
                catch(ArgumentException error)
                {
                    Controls.RemoveAllBindingOverrides();
                    ReportExternalFailure("按键配置无效，已使用默认按键：",new CampaignDataException("按键 JSON 格式无效",error));
                }
                catch(Exception error)
                {
                    Controls.RemoveAllBindingOverrides();
                    ReportUnexpectedFailure("按键配置读取失败，已使用默认按键：",error);
                }
            }
            transition=gameObject.AddComponent<CampaignSceneTransition>();
            Controls.Enable(); ApplyPreferences(); gameObject.AddComponent<CampaignAudio>(); RetroUiAudio.Ensure();CampaignCurrencyAudio.Ensure(this);
            gameObject.AddComponent<CampaignMailAudio>();
        }
        // True means accepted. Dependent actions run only after the durable commit callback.
        public bool Execute(Action<CampaignSession> command,Action committed=null)
        {
            if(Session==null || Busy)return false;
            CampaignSession candidate;
            try
            {
                candidate=new CampaignSession(CloneState(Session.State),Session.Catalog);
                command(candidate);
                if(candidate.NeedsPlayerName) { Message="请先设置玩家名字，再继续旅程。";return false; }
            }
            catch(CampaignRuleException refusal) { Message=refusal.Message; return false; }
            catch(Exception error) { return ReportUnexpectedFailure("操作未提交：",error); }
            try
            {
                // A command must produce valid state. Failure here is a code defect,
                // even when the same validator also handles imported save data.
                candidate.Validate();
                BeginCommit(candidate,candidate.State.notice,committed); return true;
            }
            catch(Exception error) { return ReportUnexpectedFailure("操作未提交：",error); }
        }
        public bool Manage(CampaignPage page,string action)
        {
            if(NeedsPlayerName||Constructing||!CampaignInputPolicy.CanManage(page,Session!=null,Busy,Modal))return false;
            if(action=="Help")return Execute(s=>s.Rescue(s.State.selectedId));
            return false;
        }
        void BeginCommit(CampaignSession candidate,string message,Action completed=null,bool notify=true,int slot=0,bool newJourney=false,bool resetCurrency=false,string journeyThumbnail=null)
        {
            if(Busy)throw new InvalidOperationException("请等待当前保存或转场完成");
            pendingSession=candidate; commitMessage=message; afterCommit=completed; notifyCommit=notify;
            pendingCurrencyReset=resetCurrency||newJourney||Session==null;
            var snapshot=Snapshot(candidate);
            // A different journey supplies its own image, including an absent image in an old save.
            if(newJourney||resetCurrency)snapshot.thumbnail=journeyThumbnail;
            pendingThumbnail=snapshot.thumbnail;
            pendingWrite=repository.WriteAsync(slot,snapshot);
        }
        void FinishCommit(bool invokeCallback)
        {
            if(pendingWrite==null)return;
            var write=pendingWrite; var candidate=pendingSession; var callback=afterCommit; var thumbnail=pendingThumbnail; bool notify=notifyCommit,resetCurrency=pendingCurrencyReset;
            pendingWrite=null; pendingSession=null; afterCommit=null; pendingThumbnail=null;
            try { write.GetAwaiter().GetResult(); }
            catch(Exception error) when(IsStorageFailure(error)) { ReportExternalFailure("保存失败，操作未提交：",error); return; }
            catch(Exception error) { ReportUnexpectedFailure("保存失败，操作未提交：",error); return; }
            var previousState=Session?.State;
            Session=candidate; latestThumbnail=thumbnail; dirty=false; saveClock=0;
            CommitCurrencyBalance(candidate.State.coins,resetCurrency);
            SessionCommitted?.Invoke(previousState,candidate.State,resetCurrency);
            if(notify) { Message=commitMessage; Changed?.Invoke(); }
            if(invokeCallback)callback?.Invoke();
        }
        void CommitCurrencyBalance(long balance,bool reset)
        {
            if(reset||!currencyReady)
            {
                currencyReady=true;CurrencyBalance=balance;CurrencyNetChange=0;LastCurrencyChange=default;
                CurrencyReset?.Invoke(balance);return;
            }
            long before=CurrencyBalance;CurrencyBalance=balance;
            if(before==balance)return;
            var change=new CampaignCurrencyChange(before,balance,++CurrencySequence);
            CurrencyNetChange+=change.Delta;LastCurrencyChange=change;CurrencyChanged?.Invoke(change);
        }
        CampaignSave Snapshot(CampaignSession session) => new CampaignSave
        {
            savedAt=DateTime.Now.ToString("yyyy-MM-dd HH:mm"), state=CloneState(session.State), catalog=session.Catalog, thumbnail=latestThumbnail,
            hasBattle=session.State.battle!=null, hasAuction=session.State.auction!=null, hasPupaAuction=session.State.pupaAuction!=null
        };
        public void NewGame(int slot=0)
        {
            if(!Busy)PlayerNameRequestSlot=slot;
        }
        public void CancelNewGameNaming() => PlayerNameRequestSlot=null;
        public bool NewGame(int slot,string playerName,Action committed=null)
        {
            if(Busy)return false;
            string problem=CampaignSession.NameProblem(playerName);
            if(problem!=null){Message=problem;return false;}
            try
            {
                if(File.Exists(SlotPath(slot))) { Message="此槽位已有存档，请选择空槽或先删除旧存档"; return false; }
                if(!TryLoadCatalog(ContentPath,"无法新建游戏：",out var c))return false;
                var candidate=CampaignSession.New(c,unchecked((uint)DateTime.UtcNow.Ticks)); candidate.RenamePlayer(playerName);
                candidate.Tell("欢迎，"+candidate.PlayerSignature+"。先照顾甲虫，再去试试新秀联赛。");candidate.Validate();
                BeginCommit(candidate,candidate.State.notice,()=>
                {
                    careClock=calendarClock=saveClock=0;
                    // The chosen slot is durable before entering play. Subsequent saves use the auto slot.
                    dirty=slot!=0;PlayerNameRequestSlot=null;committed?.Invoke();Navigate(CampaignPage.Greenhouse);
                },slot:slot,newJourney:true);
                return true;
            }
            catch(Exception error) { return ReportUnexpectedFailure("无法新建游戏：",error); }
        }
        public CampaignSave ReadSlot(int slot) => repository.Read(slot);
        public void LoadSlot(int slot)
        {
            if(Busy)return;
            try
            {
                var save=ReadSlot(slot); if(save==null) { Message="这是一个空槽位"; return; }
                var candidate=new CampaignSession(save.state,save.catalog);
                // Old player-controlled pauses must not leave a loaded journey frozen.
                // Keep its date, pace and lifecycle rules; normal UI/build pauses are transient.
                candidate.State.clock.paused=false;
                BeginCommit(candidate,"已读取第 "+candidate.State.week+" 周存档。",()=>{careClock=calendarClock=saveClock=0;Navigate(CampaignPage.Greenhouse);},resetCurrency:true,journeyThumbnail:save.thumbnail);
            }
            catch(Exception error) when(IsExternalDataFailure(error)) { ReportExternalFailure("读取失败，原文件保留：",error); }
            catch(Exception error) { ReportUnexpectedFailure("读取失败，原文件保留：",error); }
        }
        // Explicit manual saves and shutdown flush synchronously; frequent transactions and autosaves do not.
        public bool SaveSlot(int slot)
        {
            if(Busy)return false;
            try
            {
                if(Session==null) { Message="请先开始游戏"; return false; }
                CaptureThumbnail(); repository.Write(slot,Snapshot(Session)); dirty=false;CommitCurrencyBalance(Session.State.coins,false);
                Message="已保存到 "+(slot==0?"自动槽":"手动槽 "+slot);
                if(SceneManager.GetActiveScene().name==SceneName(CampaignPage.Saves))Changed?.Invoke();
                return true;
            }
            catch(Exception error) when(IsStorageFailure(error)) { ReportExternalFailure("保存失败：",error); return false; }
            catch(Exception error) { return ReportUnexpectedFailure("保存失败：",error); }
        }
        public void DeleteSlot(int slot)
        {
            if(Busy)return;
            try { repository.Delete(slot); Message="槽位已删除"; Changed?.Invoke(); }
            catch(Exception error) when(IsStorageFailure(error)) { ReportExternalFailure("删除失败：",error); }
            catch(Exception error) { ReportUnexpectedFailure("删除失败：",error); }
        }
        public static void AtomicWrite(string path,string text) => CampaignSaveRepository.AtomicWrite(path,text);
        public void ReloadContent()
        {
            if(Busy)return;
            try
            {
                if(Session==null) { Message="先进入温室"; return; }
                if(Session.State.battle!=null || Session.State.auction!=null || Session.State.pupaAuction!=null) { Message="请先完成赛事与拍卖再热更"; return; }
                if(new FileInfo(ContentPath).Length>1000000)throw new CampaignDataException("内容包超过1MB");
                if(!TryLoadCatalog(ContentPath,"热更未应用：",out var c))return;
                if(c.revision<=Session.Catalog.revision) { Message="请提高内容 revision 后再重载"; return; }
                var candidate=new CampaignSession(CloneState(Session.State),c); candidate.Validate();
                BeginCommit(candidate,"内容已原子更新至 r"+c.revision+"；占用中的物种与环境均已校验。");
            }
            catch(Exception error) when(IsExternalDataFailure(error)) { ReportExternalFailure("热更未应用：",error); }
            catch(Exception error) { ReportUnexpectedFailure("热更未应用：",error); }
        }
        public void Select(string id)
        {
            if(Session==null || Busy)return; Session.Beetle(id); Session.State.selectedId=id; dirty=true; Changed?.Invoke();
        }
        public static CampaignState CloneState(CampaignState source) => CampaignSnapshots.Copy(source);
        void Update()
        {
            if(pendingWrite!=null && pendingWrite.IsCompleted)FinishCommit(true);
            if(!CalendarRunning)return;
            calendarClock+=Time.unscaledDeltaTime;
            careClock+=Time.unscaledDeltaTime; saveClock+=Time.unscaledDeltaTime;
            if(calendarClock>=.25f)
            {
                double seconds=calendarClock;calendarClock=0;
                // Weekly royalties and escrow refunds become visible only after a durable commit.
                if(Session.State.clock.elapsedDays+seconds*Session.State.clock.speed/Session.State.clock.realSecondsPerDay>=Session.State.week*7d-1e-9)
                { Execute(s=>s.AdvanceRealSeconds(seconds));return; }
                int previousDay=(int)Session.State.clock.elapsedDays;
                string previousNotice=Session.State.notice;
                Session.AdvanceRealSeconds(seconds);dirty=true;
                if(previousNotice!=Session.State.notice || previousDay!=(int)Session.State.clock.elapsedDays)
                { Message=Session.State.notice;Changed?.Invoke(); }
            }
            if(careClock>=1)
            {
                int seconds=Mathf.FloorToInt(careClock); careClock-=seconds;
                string previous=Session.State.notice; for(int i=0;i<seconds;i++)Session.CareSeconds(1,IsCareEating); dirty=true;
                if(previous!=Session.State.notice) { Message=Session.State.notice; Changed?.Invoke(); }
            }
            if(saveClock>=Mathf.Clamp(Preferences.autoSaveSeconds,30,300))
            {
                saveClock=0;
                if(dirty)
                {
                    CaptureThumbnail();
                    BeginCommit(new CampaignSession(CloneState(Session.State),Session.Catalog),null,null,false);
                }
            }
        }
        public static string SceneName(CampaignPage page) => "Campaign"+page;
        public void RevealAfterPlayerNaming(Action atBlack) => transition.Play(atBlack,error=>Message=error,CampaignTransitionMode.BlackFade);
        public void Go(CampaignPage page)
        {
            if(Busy)return;
            if(!Application.CanStreamedLevelBeLoaded(SceneName(page))) { Message="无法载入场景："+SceneName(page); return; }
            // Camera orbit and sidebar changes do not dirty domain state, but still change the picture.
            bool captured=Session!=null && CaptureThumbnail();
            if(Session!=null && (dirty||captured))
            {
                BeginCommit(new CampaignSession(CloneState(Session.State),Session.Catalog),Message,()=>Navigate(page),false);
            }
            else Navigate(page);
        }
        void Navigate(CampaignPage page)
        {
            Modal=false;Constructing=false;
            string source=SceneManager.GetActiveScene().name;
            CampaignTransitionMode? mode=source==SceneName(CampaignPage.MainMenu)||source==SceneName(CampaignPage.Saves)?CampaignTransitionMode.BlackFade:(CampaignTransitionMode?)null;
            transition.Go(page,error=>Message=error,mode);
        }
        public bool CaptureThumbnail()
        {
            if(Session==null || Busy || Constructing || SceneManager.GetActiveScene().name!=SceneName(CampaignPage.Greenhouse))return false;
            var view=FindFirstObjectByType<CampaignBackdrop>(); if(!view || !view.Ready || !view.Frame)return false;
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
            return true;
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
            catch(Exception error) when(IsStorageFailure(error)) { ReportExternalFailure("设置保存失败：",error); }
            catch(Exception error) { ReportUnexpectedFailure("设置保存失败：",error); }
        }
        void Flush(bool invokeCallback=false)
        {
            // Capture the live journey before resolving a pending load, which may replace it.
            bool captured=CaptureThumbnail();
            if(pendingWrite!=null)FinishCommit(invokeCallback);
            if(Session==null || (!dirty&&!captured))return;
            try { repository.Write(0,Snapshot(Session)); dirty=false;CommitCurrencyBalance(Session.State.coins,false); }
            catch(Exception error) when(IsStorageFailure(error)) { ReportExternalFailure("保存失败：",error); }
            catch(Exception error) { ReportUnexpectedFailure("保存失败：",error); }
        }
        bool TryLoadCatalog(string path,string failureMessage,out CampaignCatalog catalog)
        {
            catalog=null;
            try
            {
                var loaded=CampaignJson.Read<CampaignCatalog>(path); loaded.Validate(); catalog=loaded; return true;
            }
            catch(Exception error) when(IsExternalDataFailure(error)) { ReportExternalFailure(failureMessage,error); return false; }
            catch(Exception error) { return ReportUnexpectedFailure(failureMessage,error); }
        }
        static bool IsStorageFailure(Exception error) => error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException;
        internal static bool IsExternalDataFailure(Exception error) => error is CampaignDataException || IsStorageFailure(error);
        internal static string ExternalFailureMessage(Exception error) => error is CampaignDataException?error.Message:"无法访问文件，请检查磁盘空间和读写权限。";
        void ReportExternalFailure(string operation,Exception error)
        {
            // Include type, stack and any inner parser exception in the diagnostic,
            // while keeping filesystem paths and implementation details off the UI.
            Debug.LogWarning(operation+"\n"+error,this);
            Message=operation+ExternalFailureMessage(error);
        }
        bool ReportUnexpectedFailure(string operation,Exception error)
        {
            Debug.LogException(error,this);
            Message=operation+"发生内部错误，详情已记录到控制台。";
            return false;
        }
        // Pausing retains the live scene: committed operations still need to finish their UI flow.
        // Quitting/destroying skips those callbacks because their scene may already be unloading.
        void OnApplicationPause(bool paused) { if(paused)Flush(true); }
        void OnApplicationQuit() { Flush(); }
        void OnDestroy() { if(Instance==this) { Flush(); Controls?.Dispose(); Instance=null; } }
    }
}
