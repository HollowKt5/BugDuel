using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BeetleArena.Campaign;
using BugDuel.Campaign;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UI=UnityEngine.UI;

[InitializeOnLoad]
public static class CampaignPlayerIdentityPlayValidation
{
    const string Key="BugDuel.PlayerIdentityPlayValidation";
    public const string Output="Artifacts/PlayerIdentity/Play";
    static CampaignPlayerIdentityPlayValidation()
    {
        string path=SessionState.GetString(Key,"");if(path.Length>0)CampaignApp.StorageOverride=path;
        EditorApplication.playModeStateChanged+=OnPlay;
        EditorApplication.update+=()=>{if(SessionState.GetString(Key,"").Length>0&&EditorApplication.isPlaying)EditorApplication.QueuePlayerLoopUpdate();};
    }
    [MenuItem("Tools/甲壳虫斗/验证/战役流程/玩家身份与对话交互", false, 571)]
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before isolated identity validation.");
        Directory.CreateDirectory(Output);string path=Path.GetFullPath(Output+"/Save-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        var legacy=CampaignSession.New(CampaignCatalog.Defaults(),51429);legacy.ConfigureClock(600,1,false);
        var repository=new CampaignSaveRepository(path);repository.Write(2,new CampaignSave {state=legacy.State,catalog=legacy.Catalog});
        // Deliberately retain one genuinely old, unnamed JSON fixture; all other acceptance saves are explicitly named.
        string json=File.ReadAllText(repository.SlotPath(2));
        json=Regex.Replace(json,"\"playerName\":(?:null|\"\"),?","");json=Regex.Replace(json,"\"playerIdentityVersion\":1,?","");
        File.WriteAllText(repository.SlotPath(2),json);
        File.WriteAllText(Output+"/Result.txt","RUNNING\nIsolated saves: "+path+"\n");
        SessionState.SetString(Key+".storage",CampaignApp.StorageOverride??"");SessionState.SetString(Key,path);CampaignApp.StorageOverride=path;
        SessionState.SetString(Key+".start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Campaign/CampaignMainMenu.unity");
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Show();EditorApplication.isPlaying=true;
    }
    static void OnPlay(PlayModeStateChange change)
    {
        if(SessionState.GetString(Key,"").Length==0)return;
        if(change==PlayModeStateChange.EnteredPlayMode)
        {var go=new GameObject("Player identity acceptance");UnityEngine.Object.DontDestroyOnLoad(go);go.AddComponent<CampaignPlayerIdentityPlayDriver>();}
        if(change!=PlayModeStateChange.EnteredEditMode)return;
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key+".start",""));
        string storage=SessionState.GetString(Key+".storage","");CampaignApp.StorageOverride=storage.Length==0?null:storage;
        SessionState.EraseString(Key);SessionState.EraseString(Key+".start");SessionState.EraseString(Key+".storage");
    }
}

public sealed class CampaignPlayerIdentityPlayDriver:MonoBehaviour
{
    const string Output=CampaignPlayerIdentityPlayValidation.Output;
    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    CampaignApp app;Mouse mouse;InputSettings originalInput,testInput;
    bool originalBackground,cleaned;int originalLanguage;float originalListenerVolume;
    Texture2D baseline;
    readonly List<string> errors=new List<string>();
    CampaignView View=>FindFirstObjectByType<CampaignView>();
    RectTransform Modal=>View.GetComponentsInChildren<RectTransform>().FirstOrDefault(rect=>rect.name=="Modal");
    RectTransform Page=>View.GetComponentsInChildren<RectTransform>().First(rect=>rect.name=="Page");
    TMP_InputField Input=>Modal.GetComponentInChildren<TMP_InputField>();
    IEnumerable<UI.Button> Buttons=>FindObjectsByType<UI.Button>(FindObjectsSortMode.None).Where(button=>button.gameObject.activeInHierarchy);
    void Start()
    {
        originalBackground=Application.runInBackground;Application.runInBackground=true;originalLanguage=CampaignLocalization.English?1:0;originalListenerVolume=AudioListener.volume;
        Application.logMessageReceived+=Log;originalInput=InputSystem.settings;testInput=Instantiate(originalInput);InputSystem.settings=testInput;
        testInput.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        testInput.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        mouse=InputSystem.AddDevice<Mouse>("Identity validation mouse");StartCoroutine(Guard(Run()));
    }
    void Log(string message,string trace,LogType type)
    {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+trace);}
    IEnumerator Guard(IEnumerator routine)
    {
        var stack=new Stack<IEnumerator>();stack.Push(routine);
        while(stack.Count>0)
        {
            bool moved=false;object current=null;Exception failure=null;
            try{moved=stack.Peek().MoveNext();if(moved)current=stack.Peek().Current;}catch(Exception error){failure=error;}
            if(failure!=null){while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();Finish(failure);yield break;}
            if(!moved){(stack.Pop() as IDisposable)?.Dispose();continue;}
            if(current is IEnumerator nested)stack.Push(nested);else yield return current;
        }
        Finish(null);
    }
    void Check(bool value,string message)
    {if(!value)throw new InvalidOperationException(message+" | "+app?.Message);File.AppendAllText(Output+"/Result.txt","PASS "+message+"\n");}
    IEnumerator Wait(Func<bool> condition,string label)
    {
        float until=Time.realtimeSinceStartup+40;
        while(!condition()){if(Time.realtimeSinceStartup>until)throw new TimeoutException(label+" | "+app?.Message);yield return null;}
        yield return null;yield return null;
    }
    IEnumerator Idle()=>Wait(()=>app&&!app.Busy,"durable commit");
    IEnumerator OnPage(CampaignPage page)=>Wait(()=>app&&!app.Busy&&View&&View.page==page&&SceneManager.GetActiveScene().name==CampaignApp.SceneName(page),page.ToString());
    UI.Button Button(string text)=>Buttons.First(button=>button.IsInteractable()&&(button.name==text||button.GetComponentsInChildren<TMP_Text>().Any(label=>label.text==text)));
    Vector2 ScreenPoint(RectTransform rect)
    {
        var canvas=rect.GetComponentInParent<Canvas>();var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
        return RectTransformUtility.WorldToScreenPoint(camera,rect.TransformPoint(rect.rect.center));
    }
    IEnumerator ClickAt(Vector2 point)
    {
        InputSystem.QueueStateEvent(mouse,new MouseState {position=point});yield return null;yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState {position=point}.WithButton(MouseButton.Left));yield return null;yield return null;
        InputSystem.QueueStateEvent(mouse,new MouseState {position=point});yield return null;
    }
    IEnumerator Click(UI.Button button)
    {
        // A preceding click can create this dialog in EventSystem.Update during the same frame.
        // Wait for the first canvas build before checking the new graphic's raycast depth.
        yield return new WaitForEndOfFrame();
        if(!button||!button.IsInteractable())throw new InvalidOperationException("Button is unavailable");
        Vector2 point=ScreenPoint(button.GetComponent<RectTransform>());var hits=new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
        if(hits.Count==0||hits[0].gameObject.GetComponentInParent<UI.Button>()!=button)
        {
            var frame=GameFrame();SaveFrame(frame,"Failure");Destroy(frame);
            throw new InvalidOperationException("Button is occluded: "+button.name+" at "+point+"; hits="+string.Join(", ",hits.Select(hit=>hit.gameObject.name)));
        }
        yield return ClickAt(point);
    }
    IEnumerator Click(string text)=>Click(Button(text));
    void InvokeView(string method,params object[] args)=>typeof(CampaignView).GetMethods(Private).Single(info=>info.Name==method&&info.GetParameters().Length==args.Length).Invoke(View,args);
    bool TransparentDialog()
    {
        var image=Modal.GetComponent<UI.Image>();var canvas=Modal.parent.GetComponent<RectTransform>();var interaction=Page.GetComponent<CanvasGroup>();
        return image&&image.color.a==0&&image.raycastTarget&&Modal.anchorMin==Vector2.zero&&Modal.anchorMax==Vector2.one&&
            Vector2.Distance(Modal.rect.size,canvas.rect.size)<1&&Page.gameObject.activeInHierarchy&&!interaction.interactable&&!interaction.blocksRaycasts&&
            Modal.childCount==1&&Modal.GetChild(0).GetComponent<UI.Image>().color.a==1;
    }
    bool BlackNaming()
    {
        return Modal&&Modal.GetComponent<UI.Image>().color==Color.black&&
            Modal.GetChild(0).name=="PlayerNaming"&&!Modal.GetComponentsInChildren<RectTransform>().Any(rect=>rect.name=="DialogTitleBar");
    }
    Texture2D GameFrame()
    {
        var game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));RenderTexture target=null;
        for(Type type=game.GetType();type!=null&&!target;type=type.BaseType)
        {
            var field=type.GetField("m_TargetTexture",Private|BindingFlags.DeclaredOnly)??type.GetField("m_RenderTexture",Private|BindingFlags.DeclaredOnly);
            if(field!=null)target=field.GetValue(game) as RenderTexture;
        }
        if(!target)throw new InvalidOperationException("Game View has no composed render texture");
        var previous=RenderTexture.active;var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try{RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();return texture;}
        finally{RenderTexture.active=previous;}
    }
    void SaveFrame(Texture2D frame,string name)
    {
        var pixels=frame.GetPixels32();
        if(SystemInfo.graphicsUVStartsAtTop)
        {var upright=new Color32[pixels.Length];for(int y=0;y<frame.height;y++)Array.Copy(pixels,y*frame.width,upright,(frame.height-1-y)*frame.width,frame.width);frame.SetPixels32(upright);frame.Apply();}
        File.WriteAllBytes(Output+"/"+name+".png",frame.EncodeToPNG());
    }
    IEnumerator Capture(string name,bool remember=false,bool compare=false)
    {
        var game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));game.Focus();game.Repaint();
        yield return new WaitForSecondsRealtime(.15f);yield return new WaitForEndOfFrame();var frame=GameFrame();
        try
        {
            if(compare)
            {
                Check(baseline&&baseline.width==frame.width&&baseline.height==frame.height,"dialog comparison retains the same composed Game View dimensions");
                var before=baseline.GetPixels32();var after=frame.GetPixels32();double sharpEdges=0,softEdges=0,colorDifference=0;int total=0;
                // The same page remains behind the dialog, with softened text/edges and no tint.
                for(int y=frame.height/12;y<frame.height*11/12;y++)for(int x=frame.width/30;x<frame.width/7;x++)
                {
                    int i=y*frame.width+x;total++;
                    sharpEdges+=Math.Abs(before[i].r-before[i+1].r)+Math.Abs(before[i].g-before[i+1].g)+Math.Abs(before[i].b-before[i+1].b);
                    softEdges+=Math.Abs(after[i].r-after[i+1].r)+Math.Abs(after[i].g-after[i+1].g)+Math.Abs(after[i].b-after[i+1].b);
                    colorDifference+=Math.Abs(before[i].r-after[i].r)+Math.Abs(before[i].g-after[i].g)+Math.Abs(before[i].b-after[i].b);
                }
                Check(sharpEdges>100,"background comparison includes actual page text and edges");
                File.AppendAllText(Output+"/Result.txt",$"Blur edge ratio {softEdges/sharpEdges:0.000}; mean color difference {colorDifference/(total*3):0.000}\n");
                Check(softEdges<sharpEdges*.85&&colorDifference/(total*3)<35,"dialog softly blurs original page detail without replacing or tinting its background");
            }
            if(remember){if(baseline)Destroy(baseline);baseline=Instantiate(frame);}
            SaveFrame(frame,name);
        }
        finally{Destroy(frame);}
    }
    IEnumerator NewNamePrompt()
    {
        var create=Buttons.Where(button=>button.name=="新建存档").OrderBy(button=>ScreenPoint(button.GetComponent<RectTransform>()).x).First();
        yield return Click(create);yield return Wait(()=>app.Modal&&Modal&&Modal.GetComponentInChildren<TMP_InputField>(),"new journey naming dialog");
    }
    IEnumerator DialogAnimationChecks()
    {
        float previousTimeScale=Time.timeScale,scaledStart=Time.time;Time.timeScale=0;
        try
        {
            CampaignDialogMotion motion=null;var opening=new List<Vector2>();var openButton=Button("设置");
            UnityEngine.Events.UnityAction opened=()=>{motion=Modal.GetComponent<CampaignDialogMotion>();opening.Add(new Vector2(motion.Panel.localScale.x,motion.Alpha));};
            openButton.onClick.AddListener(opened);
            yield return Click(openButton);if(openButton)openButton.onClick.RemoveListener(opened);
            float deadline=Time.realtimeSinceStartup+5;
            while(motion.IsAnimating)
            {
                opening.Add(new Vector2(motion.Panel.localScale.x,motion.Alpha));
                if(Time.realtimeSinceStartup>deadline)throw new TimeoutException("dialog unscaled opening");yield return null;
            }
            Check(opening[0].x<.99f&&opening[0].y<.01f&&opening.Any(sample=>sample.x>opening[0].x+.005f&&sample.x<.999f)&&opening.Any(sample=>sample.y>.05f&&sample.y<.95f),
                "first settings window visibly scales and fades across presented frames");
            Check(Vector3.Distance(motion.Panel.localScale,Vector3.one)<.001f&&Mathf.Abs(motion.Alpha-1)<.001f&&Mathf.Abs(Time.time-scaledStart)<.001f,
                "opening reaches full scale and alpha while scaled game time is stopped");
            foreach(string tab in new[]{"音频","游戏"})
            {
                var button=Button(tab);Vector2 replaced=Vector2.zero;
                UnityEngine.Events.UnityAction switched=()=>{motion=Modal.GetComponent<CampaignDialogMotion>();replaced=new Vector2(motion.Panel.localScale.x,motion.Alpha);};
                button.onClick.AddListener(switched);yield return Click(button);if(button)button.onClick.RemoveListener(switched);
                bool settled=replaced==Vector2.one;
                for(int frame=0;frame<4;frame++){settled&=!motion.IsAnimating&&Mathf.Abs(motion.Alpha-1)<.001f&&Vector3.Distance(motion.Panel.localScale,Vector3.one)<.001f;yield return null;}
                Check(settled,"settings tab replacement stays fully visible without replaying window motion: "+tab);
            }
            var closing=motion;var owner=closing.gameObject;var closeButton=Button("完成");bool released=false;var closingSamples=new List<Vector2>();
            UnityEngine.Events.UnityAction closed=()=>
            {
                var group=owner.GetComponent<CanvasGroup>();released=closing.Closing&&!group.interactable&&!group.blocksRaycasts&&!app.Modal;
                var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=ScreenPoint(closing.Panel)},hits);
                released&=hits.All(hit=>!hit.gameObject.transform.IsChildOf(owner.transform));
                closingSamples.Add(new Vector2(closing.Panel.localScale.x,closing.Alpha));
            };
            closeButton.onClick.AddListener(closed);yield return Click(closeButton);if(closeButton)closeButton.onClick.RemoveListener(closed);
            deadline=Time.realtimeSinceStartup+5;
            while(owner)
            {
                closingSamples.Add(new Vector2(closing.Panel.localScale.x,closing.Alpha));
                if(Time.realtimeSinceStartup>deadline)throw new TimeoutException("dialog unscaled closing");yield return null;
            }
            Check(released&&closingSamples.Any(sample=>sample.x<.999f&&sample.x>.94f)&&closingSamples.Any(sample=>sample.y>.01f&&sample.y<.95f),
                "closing window shrinks and fades after immediately releasing its input raycast");
            Check(!owner&&!app.Modal&&Mathf.Abs(Time.time-scaledStart)<.001f,"closing completes and destroys its window while scaled game time is stopped");
        }
        finally{Time.timeScale=previousTimeScale;}
    }
    IEnumerator MailAudioChecks(string purchaseMailId)
    {
        var sound=app.GetComponent<CampaignMailAudio>();int arrived=sound.NotificationCount,notified=sound.NotificationPlayedCount;
        int deleted=sound.DeletionCount,disposed=sound.DeletionPlayedCount;
        Check(sound.NotificationClip&&sound.DeletionClip&&sound.NotificationClip!=sound.DeletionClip,"mail delivery and disposal have distinct generated audio clips");
        InvokeView("OpenMailbox");yield return null;yield return null;
        yield return Click(Buttons.First(button=>button.name=="查看 · 有附件"));yield return Idle();
        Check(app.Session.State.mail.Single(mail=>mail.id==purchaseMailId).read&&sound.NotificationCount==arrived&&sound.NotificationPlayedCount==notified,
            "viewing an unread delivery does not repeat its notification sound");
        yield return Click("领取全部附件");yield return Idle();
        string temporary=app.SlotPath(0)+".tmp",beforeFile=File.ReadAllText(app.SlotPath(0));
        using(var locked=new FileStream(temporary,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
        {
            yield return Click("删除邮件");yield return Idle();
            Check(!app.Session.State.mail.Single(mail=>mail.id==purchaseMailId).deleted&&File.ReadAllText(app.SlotPath(0))==beforeFile&&sound.DeletionCount==deleted&&sound.DeletionPlayedCount==disposed,
                "failed durable mail deletion keeps the message and emits no disposal sound");
        }
        if(File.Exists(temporary))File.Delete(temporary);
        yield return Click("删除邮件");yield return Idle();
        Check(app.Session.State.mail.Single(mail=>mail.id==purchaseMailId).deleted&&sound.DeletionCount==deleted+1&&sound.DeletionPlayedCount==disposed+1,
            "a committed delete-to-trash plays exactly one disposal sound");
        yield return Click("已删除");yield return Click("查看邮件");yield return Idle();yield return Click("恢复到收件箱");yield return Idle();
        Check(!app.Session.State.mail.Single(mail=>mail.id==purchaseMailId).deleted&&sound.DeletionCount==deleted+1&&sound.DeletionPlayedCount==disposed+1&&sound.NotificationCount==arrived&&sound.NotificationPlayedCount==notified,
            "restoring a deleted message is silent and does not masquerade as new mail");
        yield return Click("收件箱");
        yield return Click(Buttons.Where(button=>button.name=="查看邮件").OrderByDescending(button=>ScreenPoint(button.GetComponent<RectTransform>()).y).First());yield return Idle();
        yield return Click("删除邮件");yield return Idle();yield return Click("已删除");yield return Click("查看邮件");yield return Idle();
        deleted=sound.DeletionCount;disposed=sound.DeletionPlayedCount;
        yield return Click("永久删除");yield return Idle();
        Check(app.Session.State.mail.All(mail=>mail.id!=purchaseMailId)&&sound.DeletionCount==deleted+1&&sound.DeletionPlayedCount==disposed+1,
            "purging a claimed message plays one disposal sound only after durable removal");
        InvokeView("CloseModal");yield return null;
        app.Execute(session=>session.BuyItem(CampaignItems.Food(1)));yield return Idle();string unread=app.Session.State.mail.Last().id;
        Check(app.SaveSlot(3)&&!app.ReadSlot(3).state.mail.Single(mail=>mail.id==unread).read,"audio reload fixture contains a durable unread delivery");
        arrived=sound.NotificationCount;notified=sound.NotificationPlayedCount;deleted=sound.DeletionCount;disposed=sound.DeletionPlayedCount;
        app.LoadSlot(3);yield return OnPage(CampaignPage.Greenhouse);
        Check(!app.Session.State.mail.Single(mail=>mail.id==unread).read&&sound.NotificationCount==arrived&&sound.NotificationPlayedCount==notified&&sound.DeletionCount==deleted&&sound.DeletionPlayedCount==disposed,
            "loading a save with unread mail is silent and does not replay delivery or disposal events");
        float previousMaster=app.Preferences.master;
        try
        {
            app.Preferences.master=0;app.ApplyPreferences();app.Execute(session=>session.BuyItem(CampaignItems.Food(2)));yield return Idle();
            string mutedMail=app.Session.State.mail.Last().id;app.Execute(session=>session.DeleteMail(mutedMail));yield return Idle();
            Check(sound.NotificationCount==arrived+1&&sound.DeletionCount==deleted+1&&sound.NotificationPlayedCount==notified&&sound.DeletionPlayedCount==disposed,
                "master mute still records committed mail events while suppressing both audio plays");
        }
        finally{app.Preferences.master=previousMaster;app.ApplyPreferences();}
    }
    IEnumerator Run()
    {
        yield return Wait(()=>CampaignApp.Instance&&!CampaignApp.Instance.Busy,"startup");app=CampaignApp.Instance;
        CampaignLocalization.Select(0);EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();yield return Wait(()=>Application.isFocused,"Game View focus");
        yield return Click("开始");yield return OnPage(CampaignPage.Saves);yield return NewNamePrompt();
        Check(BlackNaming()&&app.Session==null&&!File.Exists(app.SlotPath(0)),"new-game naming has its own black screen without allocating a session or save");
        yield return Capture("00-Player-Naming");
        Input.text="   ";yield return Click("开始旅程");yield return Idle();
        Check(app.Modal&&app.Session==null&&!File.Exists(app.SlotPath(0)),"blank new-game names keep the dialog open and create no save");
        Input.text="<玩家>";yield return Click("开始旅程");yield return Idle();
        Check(app.Modal&&app.Session==null&&!File.Exists(app.SlotPath(0)),"invalid rich-text names create no save");
        yield return Click("取消");Check(!app.Modal&&app.Session==null&&!File.Exists(app.SlotPath(0)),"canceling new-game naming returns to the unchanged save page");
        yield return NewNamePrompt();Input.text="  Ada 林  ";yield return Click("开始旅程");
        yield return Wait(()=>app.Session!=null&&app.GetComponent<CampaignSceneTransition>().Busy,"naming fade started after save");
        Check(BlackNaming()&&app.GetComponent<CampaignSceneTransition>().ActiveMode==CampaignTransitionMode.BlackFade,"committed new name fades directly from its black screen without flashing the save page");
        yield return OnPage(CampaignPage.Greenhouse);
        Check(app.Session.State.playerName=="Ada 林"&&app.ReadSlot(0).state.playerName=="Ada 林"&&!app.Modal,"new-game name is trimmed and durable before entering the greenhouse");
        app.Execute(session=>{session.ConfigureClock(600,1,true);session.State.coins=100000;var lot=session.State.marketAuctions[0];session.BidMarketAuction(lot.id,lot.expectedPrice);});yield return Idle();
        yield return DialogAnimationChecks();
        var owned=app.Session.State.beetles.Select(beetle=>beetle.id).ToArray();string selected=app.Session.State.selectedId,lotId=app.Session.State.marketAuctions[0].id;
        long funds=app.Session.State.coins,escrow=app.Session.GetMarketAuction(lotId).playerBid;
        yield return Click("设置");yield return Click("Ada 林");yield return Wait(()=>Modal&&Modal.GetComponentInChildren<TMP_InputField>(),"settings player name editor");
        Input.text="死亡";string originalFile=File.ReadAllText(app.SlotPath(0)),temporary=app.SlotPath(0)+".tmp";var original=app.Session;
        using(var locked=new FileStream(temporary,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
        {
            yield return Click("保存名字");yield return Idle();
            Check(app.Modal&&ReferenceEquals(original,app.Session)&&app.Session.State.playerName=="Ada 林"&&File.ReadAllText(app.SlotPath(0))==originalFile,
                "failed durable rename retains the original name, live session and save while leaving the editor open");
        }
        if(File.Exists(temporary))File.Delete(temporary);
        yield return Click("保存名字");yield return Wait(()=>!app.Busy&&app.Session.State.playerName=="死亡","renamed player committed");
        Check(app.Modal&&app.ReadSlot(0).state.playerName=="死亡"&&app.Session.State.beetles.Select(beetle=>beetle.id).SequenceEqual(owned)&&
            app.Session.State.selectedId==selected&&app.Session.State.coins==funds&&app.Session.GetMarketAuction(lotId).playerBid==escrow&&app.Session.GetMarketAuction(lotId).highBidder=="死亡",
            "settings rename returns to settings and preserves ownership, selection, coins and auction escrow");
        yield return Click("完成");InvokeView("OpenMailbox");yield return null;yield return null;
        yield return Click(Buttons.First(button=>button.name=="查看邮件"||button.name=="查看 · 有附件"));yield return Idle();
        CampaignLocalization.Select(1);yield return null;yield return null;
        Check(Modal.GetComponentsInChildren<TMP_Text>().Any(label=>label.text.Contains("死亡"))&&Modal.GetComponentsInChildren<TMP_Text>().All(label=>!label.text.Any(c=>c>='\uE000'&&c<='\uE003')),
            "English mailbox keeps the player-entered system-word name literal with no visible display markers");
        Check(View.GetComponentInChildren<CampaignModalBackdrop>().Ready,"mailbox uses the rendered original page as a soft background");
        yield return Capture("01-Mailbox-Soft-Background");
        InvokeView("CloseModal");CampaignLocalization.Select(0);yield return null;yield return null;
        Check(app.SaveSlot(1),"named identity saved to an independent reload slot");

        app.Go(CampaignPage.Market);yield return OnPage(CampaignPage.Market);
        yield return Click(Buttons.Single(button=>button.name.EndsWith("食物与养殖补给",StringComparison.Ordinal)));yield return null;yield return null;
        yield return Capture("01-Market-Original",remember:true);
        var card=View.GetComponentsInChildren<RectTransform>().Single(rect=>rect.name=="GoodsCard"&&rect.GetComponentsInChildren<TMP_Text>().Any(label=>label.text==CampaignItems.Name(CampaignItems.Food(0))));
        yield return Click(card.GetComponentInChildren<UI.Button>());yield return null;yield return null;
        Check(TransparentDialog()&&Modal.GetChild(0).name=="ClassicDialog"&&Modal.GetComponentsInChildren<RectTransform>().Count(rect=>rect.name=="WindowTitle")==1&&
            !Modal.GetComponentsInChildren<RectTransform>().Any(rect=>rect.name=="DialogTitleBar"),"market purchase reuses one readable floating dialog without duplicate panels or title bars");
        var underlying=Buttons.Single(button=>button.name=="返回温室");Vector2 behind=ScreenPoint(underlying.GetComponent<RectTransform>());
        var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=behind},hits);
        Check(hits.Count>0&&hits[0].gameObject==Modal.gameObject,"transparent backdrop still intercepts background button clicks");
        yield return ClickAt(behind);Check(app.Modal&&View.page==CampaignPage.Market,"clicking outside the dialog cannot navigate the original page");
        yield return Capture("02-Purchase-Overlay",compare:true);funds=app.Session.State.coins;
        var mailSound=app.GetComponent<CampaignMailAudio>();int notifications=mailSound.NotificationCount,notificationPlays=mailSound.NotificationPlayedCount;
        app.Preferences.master=.7f;app.ApplyPreferences();
        yield return Click("确认购买");yield return Idle();
        Check(!app.Modal&&app.Session.State.coins==funds-app.Session.SupplyBuyPrice(CampaignItems.Food(0))&&View.GetComponentsInChildren<TMP_Text>().Any(label=>label.name=="StatusMessage"&&label.text==CampaignLocalization.Text(app.Message)&&!label.GetComponent<UI.Image>()),
            "purchase returns to the same page and displays its operation message without a separate background block");
        string purchaseMailId=app.Session.State.mail.Last().id;
        Check(mailSound.NotificationCount==notifications+1&&mailSound.NotificationPlayedCount==notificationPlays+1,"actual market food purchase produces exactly one new-mail notification and sound");
        yield return Click(Buttons.Single(button=>button.name=="售出收藏与库存"));yield return null;yield return null;
        yield return Click(Buttons.First(button=>button.name=="出售给商店"));yield return null;yield return null;
        Check(TransparentDialog(),"ordinary sale confirmation also preserves the original page behind its dialog");
        yield return Capture("03-Sale-Confirmation");yield return Click("取消");
        foreach(string tab in new[]{"甲虫商店 (自营与用品)","甲虫市场 (交易 BBS)","公开拍卖 (自由竞价)","售出收藏与库存"})
        {yield return Click(Buttons.Single(button=>button.name==tab));yield return null;yield return null;Check(Page.GetComponentsInChildren<TMP_Text>().Any(label=>label.name=="StatusMessage"&&!label.GetComponent<UI.Image>()),"inline operation status: "+tab);}
        InvokeView("OpenWarehouse");yield return null;yield return null;
        Check(TransparentDialog()&&Modal.GetComponentsInChildren<TMP_Text>().Any(label=>label.name=="StatusMessage"&&!label.GetComponent<UI.Image>()),"warehouse keeps the page backdrop and draws feedback directly in its dialog");InvokeView("CloseModal");

        yield return MailAudioChecks(purchaseMailId);

        app.LoadSlot(2);yield return OnPage(CampaignPage.Greenhouse);yield return Wait(()=>app.NeedsPlayerName&&app.Modal&&Modal.GetComponentInChildren<TMP_InputField>(),"legacy name request");
        double days=app.Session.State.clock.elapsedDays;funds=app.Session.State.coins;owned=app.Session.State.beetles.Select(beetle=>beetle.id).ToArray();
        bool rejected=!app.Execute(session=>session.Earn(1))&&!app.Manage(CampaignPage.Greenhouse,"NextWeek");yield return new WaitForSecondsRealtime(.6f);
        Check(rejected&&!app.CalendarRunning&&app.Session.State.clock.elapsedDays==days&&app.Session.State.coins==funds&&BlackNaming(),"unnamed legacy saves use the separate black name screen and freeze gameplay until named");
        Input.text="Legacy Keeper";yield return Capture("04-Legacy-Player-Name");yield return Click("保存并继续");yield return Wait(()=>!app.Busy&&!app.NeedsPlayerName&&!app.Modal,"legacy identity commit");
        Check(app.ReadSlot(0).state.playerName=="Legacy Keeper"&&app.Session.State.beetles.Select(beetle=>beetle.id).SequenceEqual(owned)&&app.Session.State.coins==funds,"legacy naming is durably saved without replacing its owned collection or money");
        app.LoadSlot(1);yield return OnPage(CampaignPage.Greenhouse);yield return new WaitForSecondsRealtime(.2f);
        Check(app.Session.State.playerName=="死亡"&&!app.NeedsPlayerName&&!app.Modal,"reloading an already named slot does not ask for another name");
        Check(errors.Count==0,"no runtime errors or exceptions");
    }
    void Cleanup()
    {
        if(cleaned)return;cleaned=true;Application.logMessageReceived-=Log;
        if(mouse!=null)InputSystem.RemoveDevice(mouse);if(originalInput)InputSystem.settings=originalInput;if(testInput)Destroy(testInput);
        if(baseline)Destroy(baseline);Application.runInBackground=originalBackground;CampaignLocalization.Select(originalLanguage);AudioListener.volume=originalListenerVolume;
    }
    void OnDestroy()=>Cleanup();
    void Finish(Exception error)
    {Cleanup();File.AppendAllText(Output+"/Result.txt",(error==null?"COMPLETE PASS":"FAILED "+error)+"\n"+string.Join("\n",errors));EditorApplication.isPlaying=false;}
}
