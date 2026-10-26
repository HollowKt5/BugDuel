using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BeetleArena;
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
public static class GameFilterValidation
{
    const string Key="BugDuel.GameFilterValidation";
    public const string Output="Artifacts/GameFilter";
    static GameFilterValidation()
    {
        string path=SessionState.GetString(Key,"");if(path.Length>0)CampaignApp.StorageOverride=path;
        EditorApplication.playModeStateChanged+=OnPlay;
        EditorApplication.update+=()=>{if(SessionState.GetString(Key,"").Length>0&&EditorApplication.isPlaying)EditorApplication.QueuePlayerLoopUpdate();};
    }
    [MenuItem("Tools/甲壳虫斗/验证/界面与画面/游戏滤镜交互", false, 605)]
    public static void Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before isolated filter validation.");
        Directory.CreateDirectory(Output);
        string path=Path.GetFullPath(Output+"/Save-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        var session=CampaignSession.New(CampaignCatalog.Defaults(),31568);session.RenamePlayer("滤镜验收");
        foreach(var beetle in session.State.beetles){beetle.overturned=false;beetle.nutrition=100;beetle.hearts=beetle.maxHearts;}
        new CampaignSaveRepository(path).Write(0,new CampaignSave {state=session.State,catalog=session.Catalog});
        File.WriteAllText(Output+"/Result.txt","GAME FILTER PLAY VALIDATION\n"+DateTimeOffset.Now.ToString("o")+"\nIsolated saves: "+path+"\n");
        File.WriteAllText(Output+"/InputTrace.txt",DateTimeOffset.Now.ToString("o")+"\n");
        SessionState.SetString(Key+".storage",CampaignApp.StorageOverride??"");SessionState.SetString(Key,path);CampaignApp.StorageOverride=path;
        SessionState.SetString(Key+".start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Campaign/CampaignMainMenu.unity");
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Show();EditorApplication.isPlaying=true;
    }
    static void OnPlay(PlayModeStateChange state)
    {
        if(SessionState.GetString(Key,"").Length==0)return;
        if(state==PlayModeStateChange.EnteredPlayMode)
        {var host=new GameObject("Game filter acceptance");UnityEngine.Object.DontDestroyOnLoad(host);host.AddComponent<GameFilterPlayDriver>();}
        if(state!=PlayModeStateChange.EnteredEditMode)return;
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key+".start",""));
        string storage=SessionState.GetString(Key+".storage","");CampaignApp.StorageOverride=storage.Length==0?null:storage;
        SessionState.EraseString(Key);SessionState.EraseString(Key+".start");SessionState.EraseString(Key+".storage");
    }
}

public sealed class GameFilterPlayDriver:MonoBehaviour
{
    const string Output=GameFilterValidation.Output;
    static readonly BeetleDisplayStyle[] Styles={BeetleDisplayStyle.Clean,BeetleDisplayStyle.Crt,BeetleDisplayStyle.OrderedDither,BeetleDisplayStyle.DenseDither};
    CampaignApp app;DisplayPreferencesSnapshot preferences;WorldDisplayProfile profile;string profileJson;
    readonly Dictionary<Material,string> materials=new Dictionary<Material,string>();
    readonly Dictionary<InputActionAsset,InputDevice[]> uiDeviceFilters=new Dictionary<InputActionAsset,InputDevice[]>();
    readonly List<string> errors=new List<string>();
    InputSettings originalInput,testInput;Mouse mouse;Keyboard keyboard;
    bool background,cleaned;float volume,timeScale;int language,checks;
    CampaignView View=>FindFirstObjectByType<CampaignView>();
    RetroFilterDropdown Dropdown=>FindObjectsByType<RetroFilterDropdown>(FindObjectsSortMode.None).Single(d=>d.isActiveAndEnabled&&!d.IsGlassQualityDropdown);
    void Start()
    {
        background=Application.runInBackground;Application.runInBackground=true;volume=AudioListener.volume;timeScale=Time.timeScale;language=CampaignLocalization.English?1:0;
        preferences=new DisplayPreferencesSnapshot();profile=BeetleDisplayPreferences.Profile;
        if(profile)
        {
            profileJson=EditorJsonUtility.ToJson(profile);
            foreach(var setting in profile.scenes)
                foreach(var material in new[]{setting.ditherMaterial,setting.denseDitherMaterial,setting.customMaterial}.Where(m=>m))
                    if(!materials.ContainsKey(material))materials.Add(material,EditorJsonUtility.ToJson(material));
        }
        originalInput=InputSystem.settings;testInput=Instantiate(originalInput);InputSystem.settings=testInput;
        testInput.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        testInput.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        mouse=InputSystem.AddDevice<Mouse>("Filter validation mouse");keyboard=InputSystem.AddDevice<Keyboard>("Filter validation keyboard");
        Application.logMessageReceived+=Log;StartCoroutine(Guard(Run()));
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
    void Check(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message+" | "+app?.Message);checks++;File.AppendAllText(Output+"/Result.txt","PASS "+message+"\n");}
    IEnumerator Wait(Func<bool> condition,string message)
    {float until=Time.realtimeSinceStartup+45;while(!condition()){if(Time.realtimeSinceStartup>until)throw new TimeoutException(message);yield return null;}yield return null;yield return null;}
    IEnumerator Rendered(){yield return null;yield return null;yield return new WaitForEndOfFrame();}
    IEnumerator Idle()=>Wait(()=>app&&!app.Busy,"durable save");
    IEnumerator Page(CampaignPage page)=>Wait(()=>app&&!app.Busy&&View&&View.page==page&&View.backdrop.Ready,page.ToString());
    UI.Button Button(string caption)=>FindObjectsByType<UI.Button>(FindObjectsSortMode.None).First(b=>b.IsInteractable()&&
        (b.name==caption||b.GetComponentsInChildren<TMP_Text>().Any(t=>t.text==caption)));
    void Submit(UI.Button button)
    {EventSystem.current.SetSelectedGameObject(button.gameObject);ExecuteEvents.Execute(button.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);}
    Vector2 ScreenPoint(RectTransform rect)
    {
        var canvas=rect.GetComponentInParent<Canvas>();var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
        return RectTransformUtility.WorldToScreenPoint(camera,rect.TransformPoint(rect.rect.center));
    }
    IEnumerator Pointer(Vector2 point)
    {
        IsolateUiDevices();TraceInput("pointer move to "+point);
        InputSystem.QueueStateEvent(mouse,new MouseState {position=point});yield return null;yield return null;
        TraceInput("pointer before down");
        InputSystem.QueueStateEvent(mouse,new MouseState {position=point}.WithButton(MouseButton.Left));yield return null;yield return null;
        TraceInput("pointer pressed");
        InputSystem.QueueStateEvent(mouse,new MouseState {position=point});yield return null;yield return null;
        TraceInput("pointer released");
    }
    IEnumerator Click(UI.Button button)
    {
        Vector2 point=ScreenPoint((RectTransform)button.transform);var hits=new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
        Check(hits.Count>0&&ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject)==button.gameObject,"pointer reaches "+button.name);
        yield return Pointer(point);
    }
    IEnumerator Press(Key key)
    {
        IsolateUiDevices();
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return null;yield return null;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;yield return null;
    }
    void IsolateUiDevices()
    {
        var module=EventSystem.current.currentInputModule as UnityEngine.InputSystem.UI.InputSystemUIInputModule;
        if(!module||!module.actionsAsset||uiDeviceFilters.ContainsKey(module.actionsAsset))return;
        var asset=module.actionsAsset;var devices=asset.devices;
        uiDeviceFilters.Add(asset,devices.HasValue?devices.Value.ToArray():null);
        TraceInput("before UI device isolation");
        // SingleMouseOrPen merges physical and virtual pointers. Keep the real action/raycast
        // event chain, but prevent unrelated desktop mouse movement from replacing test input.
        asset.devices=new InputDevice[]{mouse,keyboard};TraceInput("after UI device isolation");
    }
    static string Mask(InputBinding? mask)=>mask.HasValue?"path="+mask.Value.path+", groups="+mask.Value.groups+", action="+mask.Value.action:"all";
    static string DeviceName(InputDevice device)=>device==null?"none":device.name+"#"+device.deviceId;
    static void DescribeInput(List<string> lines,UnityEngine.InputSystem.UI.InputSystemUIInputModule module)
    {
        var asset=module.actionsAsset;
        lines.Add("UI asset="+(asset?asset.name:"none")+" pointerBehavior="+module.pointerBehavior+" mask="+(asset?Mask(asset.bindingMask):"none")+
            " devices="+(asset&&asset.devices.HasValue?string.Join(",",asset.devices.Value.Select(DeviceName)):"all"));
        foreach(var reference in new[]{module.point,module.leftClick})
        {
            var action=reference?reference.action:null;
            if(action==null){lines.Add("UI action missing");continue;}
            var map=action.actionMap;
            lines.Add("Action="+action.name+" enabled="+action.enabled+" phase="+action.phase+" value="+action.ReadValueAsObject()+
                " activeControl="+(action.activeControl==null?"none":action.activeControl.path+"@"+DeviceName(action.activeControl.device))+
                " controls="+string.Join(",",action.controls.Select(control=>control.path+"@"+DeviceName(control.device)))+
                " bindingMask="+Mask(action.bindingMask)+" mapMask="+(map==null?"none":Mask(map.bindingMask))+
                " mapDevices="+(map!=null&&map.devices.HasValue?string.Join(",",map.devices.Value.Select(DeviceName)):"all"));
        }
    }
    void TraceInput(string step)
    {
        var lines=new List<string> {"frame="+Time.frameCount+" scene="+SceneManager.GetActiveScene().name+" "+step};
        var module=EventSystem.current?EventSystem.current.currentInputModule as UnityEngine.InputSystem.UI.InputSystemUIInputModule:null;
        if(module)DescribeInput(lines,module);
        File.AppendAllLines(Output+"/InputTrace.txt",lines);
    }
    void CheckProfiles()
    {
        string[] scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>Path.GetFileNameWithoutExtension(s.path)).Distinct().ToArray();
        Check(profile&&scenes.Length==12&&scenes.All(name=>profile.scenes.Count(s=>s.sceneName==name)==1),"all 12 build scenes have exactly one authored display setting");
        var owned=new HashSet<Material>();
        foreach(string scene in scenes)
        {
            var setting=profile.Find(scene);
            foreach(var pair in new[]{(setting.ditherMaterial,"BugDuel/Combat Ordered Dither"),(setting.denseDitherMaterial,"BugDuel/Combat Dense Dither")})
                Check(pair.Item1&&AssetDatabase.Contains(pair.Item1)&&owned.Add(pair.Item1)&&pair.Item1.shader.name==pair.Item2&&pair.Item1.shader.isSupported&&!ShaderUtil.ShaderHasError(pair.Item1.shader)&&
                    pair.Item1.HasProperty("_PixelSize")&&pair.Item1.HasProperty("_DitherStrength")&&pair.Item1.HasProperty("_ColorLevels"),scene+" has an independent valid "+pair.Item2+" material");
            BeetleDisplayPreferences.UseSceneDefault(scene);
        }
        Check((int)BeetleDisplayStyle.Custom==3&&(int)BeetleDisplayStyle.DenseDither==4,"new dense preset preserves the saved custom-material enum value");
        BeetleDisplayPreferences.SetStyle("SampleScene",BeetleDisplayStyle.OrderedDither);BeetleDisplayPreferences.SetPixelSize("SampleScene",4);
        BeetleDisplayPreferences.SetStyle("SampleScene",BeetleDisplayStyle.DenseDither);
        Check(BeetleDisplayPreferences.GetPixelSize("SampleScene")==2,"dense preset starts at level two independently of the saved soft size");
        BeetleDisplayPreferences.SetPixelSize("SampleScene",2);BeetleDisplayPreferences.SetStyle("SampleScene",BeetleDisplayStyle.OrderedDither);
        Check(BeetleDisplayPreferences.GetPixelSize("SampleScene")==3,"switching back restores the separate soft pixel preference, clamped to level three");
        BeetleDisplayPreferences.SetStyle("SampleScene",BeetleDisplayStyle.DenseDither);
        Check(BeetleDisplayPreferences.GetPixelSize("SampleScene")==2,"dense pixel preference survives the round trip");
        BeetleDisplayPreferences.UseSceneDefault("SampleScene");
    }
    IEnumerator DropdownInputs()
    {
        var dropdown=Dropdown;float scaled=Time.time;Time.timeScale=0;
        try
        {
            Submit(dropdown.Trigger);var popup=dropdown.PopupRect;var group=popup.GetComponent<CanvasGroup>();
            float initialAlpha=group.alpha,initialScale=popup.localScale.x;bool intermediate=false;
            Check(dropdown.IsOpen&&dropdown.IsAnimating&&initialAlpha<.99f&&initialScale<.99f,"filter popup begins with reduced scale and alpha");
            while(dropdown.IsAnimating)
            {yield return null;intermediate|=group.alpha>initialAlpha+.01f&&group.alpha<.99f&&Mathf.Abs(popup.localScale.x-1)>.001f;}
            Check(intermediate&&Mathf.Abs(group.alpha-1)<.001f&&Vector3.Distance(popup.localScale,Vector3.one)<.001f&&Time.time==scaled,"filter popup animates through intermediate values with unscaled time and ends at full size");
            Check(dropdown.OptionButtons.Select(b=>b.name).SequenceEqual(Styles.Select(style=>"FilterOption-"+style)),"real dropdown contains exactly original, CRT, soft dither and dense dither");
            yield return Rendered();Destroy(Frame("01-Filter-Dropdown"));
            yield return Press(Key.Escape);
            Check(!dropdown.IsOpen&&app.Modal,"Escape dismisses the dropdown before its parent settings dialog");
            yield return Wait(()=>!dropdown.IsAnimating,"dropdown dismissal");
            yield return Click(dropdown.Trigger);yield return Wait(()=>dropdown.IsOpen&&!dropdown.IsAnimating,"reopen dropdown");
            // A point on the modal's title is outside the popup while still inside settings.
            var title=View.GetComponentsInChildren<TMP_Text>().First(label=>label.text=="设置");
            yield return Pointer(ScreenPoint(title.rectTransform));yield return Wait(()=>!dropdown.IsAnimating,"outside dismissal");
            Check(!dropdown.IsOpen&&app.Modal,"outside pointer closes only the dropdown");
            EventSystem.current.SetSelectedGameObject(dropdown.Trigger.gameObject);yield return Press(Key.Enter);
            yield return Wait(()=>dropdown.IsOpen&&!dropdown.IsAnimating,"keyboard opens choices");
            yield return Press(Key.DownArrow);yield return Press(Key.Enter);yield return Wait(()=>!dropdown.IsOpen&&!dropdown.IsAnimating,"keyboard selection");
            Check(BeetleDisplayPreferences.Style==BeetleDisplayStyle.Crt&&app.Modal,"keyboard opens, navigates and selects a filter without closing settings");
        }
        finally{Time.timeScale=timeScale;}
    }
    IEnumerator Select(BeetleDisplayStyle style)
    {
        var dropdown=Dropdown;yield return Click(dropdown.Trigger);yield return Wait(()=>dropdown.IsOpen&&!dropdown.IsAnimating,"filter choices");
        yield return Click(dropdown.OptionButtons.Single(b=>b.name=="FilterOption-"+style));yield return Wait(()=>!dropdown.IsOpen&&!dropdown.IsAnimating,"filter selection");yield return Rendered();
        var label=dropdown.Trigger.GetComponentInChildren<TMP_Text>();label.ForceMeshUpdate();
        Check(BeetleDisplayPreferences.Style==style&&label.text.Contains(BeetleDisplayPreferences.StyleName(style)),"selection persists and displays "+style+" in "+SceneManager.GetActiveScene().name);
        Check(!label.isTextTruncated,"selected filter label is fully visible: "+style+" in "+SceneManager.GetActiveScene().name);
    }
    void CheckSurface(UI.RawImage surface,BeetleDisplayStyle style,string label)
    {
        var effect=surface.GetComponent<BeetleCombatCrt>();
        bool clean=style==BeetleDisplayStyle.Clean,crt=style==BeetleDisplayStyle.Crt,dither=BeetleDisplayPreferences.IsDither(style);
        string shader=style==BeetleDisplayStyle.DenseDither?"BugDuel/Combat Dense Dither":dither?"BugDuel/Combat Ordered Dither":"BugDuel/Combat CRT";
        Check(effect&&effect.CrtActive==crt&&effect.DitherActive==dither&&!effect.CustomActive&&effect.EffectActive==!clean&&
            (clean||surface.material.shader.name==shader),label+" binds only "+style);
        Check(surface.texture&&surface.texture.width>1&&surface.texture.height>1,label+" retains its world render texture");
    }
    void CheckCampaignPreview(BeetleDisplayStyle style)
    {
        var surface=View.backdrop.output;var preview=View.GetComponentsInChildren<UI.RawImage>().Single(image=>image.name=="Style Preview");
        CheckSurface(surface,style,"greenhouse world");CheckSurface(preview,style,"live settings preview");
        bool same=preview.texture==surface.texture&&preview.material.shader==surface.material.shader;
        if(BeetleDisplayPreferences.IsDither(style))same&=new[]{"_PixelSize","_ColorLevels","_DitherStrength"}.All(p=>Mathf.Approximately(surface.material.GetFloat(p),preview.material.GetFloat(p)));
        Check(same,"preview uses the same scene frame and filter parameters as the world");
        Check(FindObjectsByType<UI.Graphic>(FindObjectsSortMode.None).Where(g=>g!=surface&&g!=preview).All(g=>!IsFilter(g.material)),"ordinary HUD and dialog graphics have no world-filter material");
    }
    static bool IsFilter(Material material)=>material&&new[]{"BugDuel/Combat CRT","BugDuel/Combat Ordered Dither","BugDuel/Combat Dense Dither"}.Contains(material.shader.name);
    IEnumerator CompareHudPixels()
    {
        BeetleDisplayPreferences.Style=BeetleDisplayStyle.Clean;yield return new WaitForSecondsRealtime(.4f);yield return Rendered();var clean=Frame("03-Greenhouse-Original");
        BeetleDisplayPreferences.Style=BeetleDisplayStyle.DenseDither;yield return Rendered();var dense=Frame("04-Greenhouse-Dense");
        try
        {
            var a=clean.GetPixels32();var b=dense.GetPixels32();int ui=0,world=0;
            for(int y=40;y<clean.height-130;y++)for(int x=20;x<clean.width-20;x++)
            {int index=y*clean.width+x;if(a[index].Equals(b[index]))continue;if(x<clean.width/3-20)ui++;else if(x>clean.width/2)world++;}
            Check(ui==0&&world>1000,"composed pixels change in the world while the opaque husbandry sidebar remains exact: UI="+ui+", world="+world);
        }
        finally{Destroy(clean);Destroy(dense);}
    }
    IEnumerator CheckShaderFallback()
    {
        // Exercise the renderer's built-in shaders without creating or saving any assets.
        var setting=profile.Find("CampaignMainMenu");var soft=setting.ditherMaterial;var dense=setting.denseDitherMaterial;
        try
        {
            setting.ditherMaterial=null;setting.denseDitherMaterial=null;
            BeetleDisplayPreferences.Style=BeetleDisplayStyle.OrderedDither;yield return Rendered();
            CheckSurface(View.backdrop.output,BeetleDisplayStyle.OrderedDither,"soft shader fallback");
            BeetleDisplayPreferences.Style=BeetleDisplayStyle.DenseDither;yield return Rendered();
            CheckSurface(View.backdrop.output,BeetleDisplayStyle.DenseDither,"dense shader fallback after a null-source switch");
        }
        finally{setting.ditherMaterial=soft;setting.denseDitherMaterial=dense;BeetleDisplayPreferences.UseSceneDefault("CampaignMainMenu");}
    }
    IEnumerator Run()
    {
        CheckProfiles();CalibrateDither();CampaignLocalization.Select(0);
        BeetleDisplayPreferences.SetStyle("CampaignGreenhouse",BeetleDisplayStyle.Clean);BeetleDisplayPreferences.SetStyle("BeetleArena",BeetleDisplayStyle.Crt);
        app=CampaignApp.Ensure();yield return Page(CampaignPage.MainMenu);yield return CheckShaderFallback();app.LoadSlot(0);yield return Page(CampaignPage.Greenhouse);
        Check(app.Execute(s=>s.ConfigureClock(86400,1,true)),"isolated husbandry is paused for deterministic visual comparison");yield return Idle();yield return new WaitForSecondsRealtime(.5f);
        Submit(Button("设置"));yield return Rendered();Submit(Button("画面"));yield return new WaitForSecondsRealtime(.35f);yield return Rendered();
        Check(View.GetComponentsInChildren<TMP_Text>().Any(text=>text.text=="游戏滤镜"),"campaign settings label is Game Filter");
        yield return DropdownInputs();
        foreach(var style in Styles)
        {yield return Select(style);CheckCampaignPreview(style);}
        Check(BeetleDisplayPreferences.GetStyle("BeetleArena")==BeetleDisplayStyle.Crt,"campaign filter selection does not modify standalone combat");
        Destroy(Frame("02-Settings-Dense-Preview"));Submit(Button("完成"));yield return new WaitForSecondsRealtime(.35f);yield return Rendered();
        yield return CompareHudPixels();Check(app.SaveSlot(1),"manual filter fixture save succeeds");yield return Idle();
        var oldView=View;app.LoadSlot(1);yield return Page(CampaignPage.Greenhouse);
        Check(!oldView&&View,"loading the saved slot destroys the old view and reloads the greenhouse scene");
        BeetleDisplayPreferences.ReloadProfile();
        Check(BeetleDisplayPreferences.Style==BeetleDisplayStyle.DenseDither&&PlayerPrefs.GetInt(BeetleDisplayPreferences.SceneStyleKey("CampaignGreenhouse"))==(int)BeetleDisplayStyle.DenseDither,"save/load and preference-cache reload retain the greenhouse filter");
        Submit(Button("设置"));yield return new WaitForSecondsRealtime(.35f);yield return Rendered();
        if(!FindObjectsByType<RetroFilterDropdown>(FindObjectsSortMode.None).Any(d=>!d.IsGlassQualityDropdown)) {Submit(Button("画面"));yield return Rendered();}
        Check(Dropdown.Trigger.GetComponentInChildren<TMP_Text>().text.Contains(BeetleDisplayPreferences.StyleName(BeetleDisplayStyle.DenseDither)),"reopened campaign settings reflect the saved dense choice");
        Submit(Button("完成"));yield return new WaitForSecondsRealtime(.3f);
        // Standalone combat is a separate entry point, not a Campaign navigation destination.
        // Shut down the campaign owner before the harness replaces its scene externally.
        var previousApp=app;Destroy(previousApp.gameObject);
        yield return Wait(()=>!previousApp&&!CampaignApp.Instance,"campaign application shutdown");app=null;
        Check(!FindFirstObjectByType<CampaignSceneTransition>(),"standalone entry starts after the previous campaign transition owner is disposed");
        yield return SceneManager.LoadSceneAsync("BeetleArena");yield return Rendered();yield return Rendered();
        var match=FindFirstObjectByType<BeetleMatchController>();match.EditorSetPaused(true);
        var settings=FindFirstObjectByType<BeetleFeedbackSettings>();yield return Click(settings.openButton);yield return new WaitForSecondsRealtime(.35f);
        Check(settings.IsOpen,"real pointer click opens standalone combat settings");
        Check(settings.panel.GetComponentsInChildren<TMP_Text>().Any(text=>text.text=="游戏滤镜"),"standalone combat exposes the same Game Filter dropdown");
        var defaultsLabel=settings.panel.GetComponentsInChildren<UI.Button>().Single(button=>button.name=="Scene Defaults").GetComponentInChildren<TMP_Text>();
        defaultsLabel.ForceMeshUpdate();Check(!defaultsLabel.isTextTruncated,"standalone scene-defaults button label is fully visible");
        foreach(var style in new[]{BeetleDisplayStyle.Clean,BeetleDisplayStyle.OrderedDither,BeetleDisplayStyle.DenseDither,BeetleDisplayStyle.Crt})
        {
            yield return Select(style);var display=FindFirstObjectByType<BeetleCombatDisplay>();var camera=display.GetComponent<Camera>();var surface=display.GetComponentInChildren<UI.RawImage>();
            if(style==BeetleDisplayStyle.Clean)Check(!surface&&!camera.targetTexture,"standalone original mode releases the filter render target");
            else
            {
                CheckSurface(surface,style,"standalone combat");
                Check(FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c=>c.isRootCanvas&&c.worldCamera==camera).All(c=>c.renderMode!=RenderMode.ScreenSpaceCamera),"standalone HUD stays outside the filtered world camera");
            }
            if(style==BeetleDisplayStyle.DenseDither)Destroy(Frame("05-Combat-Dense"));
        }
        var combatDropdown=Dropdown;yield return Click(combatDropdown.Trigger);yield return Wait(()=>combatDropdown.IsOpen&&!combatDropdown.IsAnimating,"combat dropdown");
        bool paused=match.IsPaused;
        var inputModule=EventSystem.current.currentInputModule as UnityEngine.InputSystem.UI.InputSystemUIInputModule;
        bool spaceSubmits=inputModule&&inputModule.submit!=null&&inputModule.submit.action.controls.Any(control=>control==keyboard.spaceKey);
        yield return Press(Key.Space);
        Check(match.IsPaused==paused,"Space in the combat settings dropdown does not toggle match pause");
        yield return Press(Key.B);
        Check(SceneManager.GetActiveScene().name=="BeetleArena"&&settings.IsOpen,"B in combat settings does not navigate to standalone husbandry");
        if(spaceSubmits)
        {
            yield return Wait(()=>!combatDropdown.IsAnimating,"Space submission");
            Check(!combatDropdown.IsOpen&&BeetleDisplayPreferences.Style==BeetleDisplayStyle.Crt,"Space submits the focused filter when bound as UI Submit");
            yield return Click(combatDropdown.Trigger);yield return Wait(()=>combatDropdown.IsOpen&&!combatDropdown.IsAnimating,"combat dropdown after submission");
        }
        else Check(combatDropdown.IsOpen,"unbound Space leaves filter choices open without leaking to combat");
        yield return Press(Key.Escape);yield return Wait(()=>!combatDropdown.IsAnimating,"combat escape");
        Check(!combatDropdown.IsOpen&&settings.panel.activeInHierarchy,"combat Escape also closes only its dropdown");
        yield return Click(settings.closeButton);yield return new WaitForSecondsRealtime(.25f);yield return Click(settings.openButton);yield return new WaitForSecondsRealtime(.3f);
        Check(settings.IsOpen,"real pointer click reopens standalone combat settings");
        Check(Dropdown.Trigger.GetComponentInChildren<TMP_Text>().text.Contains(BeetleDisplayPreferences.StyleName(BeetleDisplayStyle.Crt)),"reopened combat settings reflect CRT");
        yield return SceneManager.LoadSceneAsync("BeetleArena");yield return Rendered();yield return Rendered();FindFirstObjectByType<BeetleMatchController>().EditorSetPaused(true);
        CheckSurface(FindFirstObjectByType<BeetleCombatDisplay>().GetComponentInChildren<UI.RawImage>(),BeetleDisplayStyle.Crt,"reloaded standalone combat");
        BeetleDisplayPreferences.Save();BeetleDisplayPreferences.ReloadProfile();app=CampaignApp.Ensure();
        Check(app.Session==null&&app!=previousApp,"campaign reopens as a fresh application before loading the isolated save");
        app.LoadSlot(1);yield return Page(CampaignPage.Greenhouse);yield return Rendered();
        CheckSurface(View.backdrop.output,BeetleDisplayStyle.DenseDither,"campaign loaded after combat");
        Check(BeetleDisplayPreferences.GetStyle("BeetleArena")==BeetleDisplayStyle.Crt,"campaign slot load preserves the separate combat filter preference");
        Check(WriteAssetDiffs(),"player selections and GPU calibration leave authored profile and materials unchanged");
        Check(errors.Count==0,"no runtime errors or exceptions");
    }
    bool WriteAssetDiffs()
    {
        string currentProfile=EditorJsonUtility.ToJson(profile);bool assetsUnchanged=currentProfile==profileJson;
        if(!assetsUnchanged)
        {File.WriteAllText(Output+"/Diff-Profile-Before.json",profileJson);File.WriteAllText(Output+"/Diff-Profile-After.json",currentProfile);}
        int materialIndex=0;
        foreach(var pair in materials)
        {
            string currentMaterial=EditorJsonUtility.ToJson(pair.Key);
            if(currentMaterial!=pair.Value)
            {
                assetsUnchanged=false;string prefix=Output+"/Diff-Material-"+materialIndex;
                File.WriteAllText(prefix+"-Before.json",pair.Value);File.WriteAllText(prefix+"-After.json",currentMaterial);
                File.WriteAllText(prefix+"-Asset.txt",AssetDatabase.GetAssetPath(pair.Key)+"\n"+pair.Key.name);
            }
            materialIndex++;
        }
        return assetsUnchanged;
    }
    void CalibrateDither()
    {
        const int width=384,height=192;
        var source=new Texture2D(width,height,TextureFormat.RGB24,false);var pixels=new Color[width*height];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)pixels[y*width+x]=y<height/2?Color.Lerp(Color.black,Color.white,x/(width-1f)):Color.HSVToRGB(x/(width-1f),.8f,.75f);
        source.SetPixels(pixels);source.Apply();var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32);var capture=new Texture2D(width,height,TextureFormat.RGB24,false);var previous=RenderTexture.active;
        Color32[] soft=null;
        Color32[] Read(){RenderTexture.active=target;capture.ReadPixels(new Rect(0,0,width,height),0,0);capture.Apply();return capture.GetPixels32();}
        try
        {
            Graphics.Blit(source,target);var baseline=Read();
            foreach(bool dense in new[]{false,true})
            {
                var shader=Resources.Load<Shader>(dense?"BeetleDenseDither":"BeetleCombatDither");var material=new Material(shader);
                try
                {
                    Check(shader&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader)&&Mathf.Approximately(material.GetFloat("_PixelSize"),2)&&Mathf.Approximately(material.GetFloat("_ColorLevels"),dense?6:16)&&Mathf.Approximately(material.GetFloat("_DitherStrength"),dense?1:.5f),"GPU preset defaults: "+(dense?"dense 2px / 6 levels / full":"soft 2px / 16 levels / half"));
                    Graphics.Blit(source,target,material);var output=Read();File.WriteAllBytes(Output+"/Calibration-"+(dense?"Dense":"Soft")+".png",capture.EncodeToPNG());
                    Graphics.Blit(source,target,material);var repeated=Read();
                    Check(output.SequenceEqual(repeated),"identical input gives stable ordered pixels: "+(dense?"dense":"soft"));
                    int colorful=output.Count(c=>Math.Max(c.r,Math.Max(c.g,c.b))-Math.Min(c.r,Math.Min(c.g,c.b))>50);
                    float maxError=0;
                    for(int y=24;y<height-24;y+=24)for(int x=24;x<width-24;x+=24)
                    {
                        if(y<height/2&&y+24>height/2)continue;Vector3 a=Vector3.zero,b=Vector3.zero;
                        for(int dy=0;dy<24;dy++)for(int dx=0;dx<24;dx++)
                        {int i=(y+dy)*width+x+dx;var s=baseline[i];var t=output[i];a+=new Vector3(s.r,s.g,s.b);b+=new Vector3(t.r,t.g,t.b);}
                        var error=(b-a)/(24*24*255f);maxError=Mathf.Max(maxError,Mathf.Abs(error.x),Mathf.Abs(error.y),Mathf.Abs(error.z));
                    }
                    Check(colorful>width*height/3&&maxError<.065f,"original RGB survives "+(dense?"dense":"soft")+" dithering; maximum local channel error="+maxError.ToString("F4"));
                    if(!dense)
                    {
                        soft=output;bool uniform=true;
                        for(int y=2;y<height-2;y+=2)for(int x=2;x<width-2;x+=2)
                            for(int dy=0;dy<2;dy++)for(int dx=0;dx<2;dx++)uniform&=output[(y+dy)*width+x+dx].Equals(output[y*width+x]);
                        Check(uniform,"soft default holds one color across each 2 by 2 pixel cell");
                    }
                    else
                    {
                        int levels=output.Take(width*height/2).Select(color=>color.r).Distinct().Count();
                        Check(levels>=5&&levels<=7,"dense GPU output retains the configured six-level quantization: "+levels+" byte levels");
                        int changed=0;for(int i=0;i<output.Length;i++)if(!output[i].Equals(soft[i]))changed++;
                        Check(changed>output.Length/3,"soft and dense presets produce visibly distinct GPU output: "+changed+" pixels");
                    }
                }
                finally{Destroy(material);}
            }
        }
        finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Destroy(source);Destroy(capture);}
    }
    Texture2D Frame(string name)
    {
        var game=EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor"));RenderTexture target=null;
        for(Type type=game.GetType();type!=null&&!target;type=type.BaseType)
        {var field=type.GetField("m_TargetTexture",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)??type.GetField("m_RenderTexture",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);if(field!=null)target=field.GetValue(game) as RenderTexture;}
        if(!target)throw new InvalidOperationException("Game View has no composed frame");
        var previous=RenderTexture.active;var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try{RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();}finally{RenderTexture.active=previous;}
        if(SystemInfo.graphicsUVStartsAtTop)
        {var source=texture.GetPixels32();var upright=new Color32[source.Length];for(int y=0;y<texture.height;y++)Array.Copy(source,y*texture.width,upright,(texture.height-1-y)*texture.width,texture.width);texture.SetPixels32(upright);texture.Apply();}
        File.WriteAllBytes(Output+"/"+name+".png",texture.EncodeToPNG());return texture;
    }
    void Cleanup()
    {
        if(cleaned)return;cleaned=true;Application.logMessageReceived-=Log;
        foreach(var pair in uiDeviceFilters)if(pair.Key)pair.Key.devices=pair.Value==null?(UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputDevice>?)null:new UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputDevice>(pair.Value);
        if(mouse!=null)InputSystem.RemoveDevice(mouse);if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(originalInput)InputSystem.settings=originalInput;if(testInput)Destroy(testInput);
        preferences?.Dispose();if(profile&&profileJson!=null)EditorJsonUtility.FromJsonOverwrite(profileJson,profile);
        foreach(var pair in materials)if(pair.Key)EditorJsonUtility.FromJsonOverwrite(pair.Value,pair.Key);
        BeetleDisplayPreferences.ReloadProfile();Application.runInBackground=background;AudioListener.volume=volume;Time.timeScale=timeScale;CampaignLocalization.Select(language);
    }
    void OnDestroy()=>Cleanup();
    void WriteFailureState(Exception failure)
    {
        var lines=new List<string> {DateTimeOffset.Now.ToString("o"),failure.ToString(),"Scene="+SceneManager.GetActiveScene().name+" frame="+Time.frameCount};
        foreach(var dropdown in FindObjectsByType<RetroFilterDropdown>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            lines.Add("Dropdown "+dropdown.name+" scene="+dropdown.gameObject.scene.name+" activeSelf="+dropdown.gameObject.activeSelf+" activeInHierarchy="+dropdown.gameObject.activeInHierarchy+
                " isActiveAndEnabled="+dropdown.isActiveAndEnabled+" IsOpen="+dropdown.IsOpen+" IsAnimating="+dropdown.IsAnimating);
        foreach(var settings in FindObjectsByType<BeetleFeedbackSettings>(FindObjectsInactive.Include,FindObjectsSortMode.None))
        {
            var motion=settings.panel.GetComponent<BeetlePanelMotion>();
            lines.Add("Settings "+settings.name+" IsOpen="+settings.IsOpen+" panel.activeSelf="+settings.panel.activeSelf+" panel.activeInHierarchy="+settings.panel.activeInHierarchy+
                " panelMotion.IsAnimating="+(motion&&motion.IsAnimating)+" panelMotion.Visible="+(motion&&motion.Visible));
        }
        var events=EventSystem.current;
        lines.Add("EventSystem.current="+(events?events.name:"none")+" selected="+(events&&events.currentSelectedGameObject?events.currentSelectedGameObject.name:"none"));
        foreach(var eventSystem in FindObjectsByType<EventSystem>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            foreach(var module in eventSystem.GetComponents<BaseInputModule>())
            {
                lines.Add("InputModule "+module.GetType().Name+" owner="+eventSystem.name+" enabled="+module.enabled+" isActiveAndEnabled="+module.isActiveAndEnabled+" current="+(events&&events.currentInputModule==module));
                if(module is UnityEngine.InputSystem.UI.InputSystemUIInputModule ui)DescribeInput(lines,ui);
            }
        if(mouse!=null)lines.Add("TestMouse position="+mouse.position.ReadValue()+" leftPressed="+mouse.leftButton.isPressed);
        File.WriteAllLines(Output+"/FailureState.txt",lines);
        try{Destroy(Frame("Failure"));}
        catch(Exception captureError){File.AppendAllText(Output+"/FailureState.txt","\nScreenshot failed: "+captureError);}
    }
    void Finish(Exception failure)
    {
        try{WriteAssetDiffs();if(failure!=null)WriteFailureState(failure);}
        catch(Exception diagnosticError){File.AppendAllText(Output+"/Result.txt","Diagnostic failure: "+diagnosticError+"\n");if(failure==null)failure=diagnosticError;}
        Cleanup();File.AppendAllText(Output+"/Result.txt",(failure==null?"COMPLETE PASS":"FAILED "+failure)+" | "+checks+" checks\n"+string.Join("\n",errors));EditorApplication.isPlaying=false;
    }
}
