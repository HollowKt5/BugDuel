using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UI=UnityEngine.UI;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        bool settingsOpen;
        int settingsTab;
        InputActionRebindingExtensions.RebindingOperation rebind;

        void OpenSettings()
        {
            BeetleDisplayPreferences.Save();
            float extraHeight=settingsTab==3?80:0;
            var p=OpenModal(1050,760+extraHeight);settingsOpen=true;Label(p,"设置",30,25,850,50,36);
            string[] tabs={"游戏","音频","视频","画面","控制"};for(int i=0;i<tabs.Length;i++){int tab=i;Btn(p,tabs[i],30+i*200,95,185,48,()=>{settingsTab=tab;OpenSettings();},settingsTab!=i);}
            if(settingsTab==0)
            {
                SettingRow(p,"玩家名字",170,Session==null?"开始游戏后可设置":Session.PlayerSignature,()=>OpenPlayerNaming(null,OpenSettings),Session!=null);
                SettingRow(p,"界面语言",230,CampaignLocalization.English?"English":"简体中文",()=>
                {
                    app.Preferences.language=CampaignLocalization.English?0:1;
                    CampaignLocalization.Select(app.Preferences.language);app.SavePreferences();
                    QueueRefresh();OpenSettings();
                });
                SettingRow(p,"定时自动保存（交易即时保存）",290,app.Preferences.autoSaveSeconds+" 秒",()=>{int[] values={30,60,180,300};app.Preferences.autoSaveSeconds=values[(Array.IndexOf(values,app.Preferences.autoSaveSeconds)+1)%4];OpenSettings();});
                SettingRow(p,"默认战斗速度",350,app.Preferences.battleSpeed+"×",()=>{app.Preferences.battleSpeed=app.Preferences.battleSpeed==1?2:app.Preferences.battleSpeed==2?4:1;OpenSettings();});
                Label(p,"当前季节（随游戏日历）",40,420,500,55,24);
                Label(p,CampaignRetroTheme.SeasonName,560,420,450,55,24);
                SettingRow(p,"明暗模式",470,CampaignRetroTheme.Dark?"经典暗色":"明亮浅色",()=>{CampaignRetroTheme.SetMode(!CampaignRetroTheme.Dark);QueueRefresh();OpenSettings();});
                SettingRow(p,"时间速度",530,Session==null?"开始游戏后可设置":"调整自然时间倍率",()=>OpenTimeSettings(OpenSettings),Session!=null);
            }
            else if(settingsTab==1)
            {
                var arenaAudio=FindFirstObjectByType<ArenaAudio>();
                Slider(p,"主音量",190,app.Preferences.master,v=>{app.Preferences.master=v;AudioListener.volume=v;});
                Slider(p,"音乐",275,app.Preferences.music,v=>app.Preferences.music=v);
                Slider(p,"自然环境",360,app.Preferences.nature,v=>app.Preferences.nature=v);
                Slider(p,"甲壳碰撞音效",445,app.Preferences.carapace,v=>{app.Preferences.carapace=v;if(arenaAudio)arenaAudio.volume=v;});
                Label(p,"拖动滑块或点击音轨调整；选中后可用 ← / → 或手柄微调。",40,550,970,60,20,muted);
            }
            else if(settingsTab==2)
            {
                string[] resolutions={"1280 × 720","1600 × 900","1920 × 1080","2560 × 1440"};
                SettingRow(p,"分辨率",170,resolutions[Mathf.Clamp(app.Preferences.resolution,0,3)],()=>{app.Preferences.resolution=(app.Preferences.resolution+1)%4;OpenSettings();});
                SettingRow(p,"窗口模式",240,new[]{"窗口","无边框全屏","独占全屏"}[Mathf.Clamp(app.Preferences.windowMode,0,2)],()=>{app.Preferences.windowMode=(app.Preferences.windowMode+1)%3;OpenSettings();});
                SettingRow(p,"帧率上限",310,app.Preferences.vsync?"跟随显示器刷新率":app.Preferences.fps+" 帧/秒",()=>{int[] values={30,60,120,144};app.Preferences.fps=values[(Array.IndexOf(values,app.Preferences.fps)+1)%values.Length];OpenSettings();},!app.Preferences.vsync);
                SettingRow(p,"垂直同步",380,app.Preferences.vsync?"开启":"关闭",()=>{app.Preferences.vsync=!app.Preferences.vsync;OpenSettings();});
                SettingRow(p,"微距景深",450,app.Preferences.depthOfField?"开启":"关闭",()=>{app.Preferences.depthOfField=!app.Preferences.depthOfField;OpenSettings();});
                SettingRow(p,"经营页转场",520,app.Preferences.transitionMode==CampaignTransitionMode.BlackFade?"黑屏淡入淡出":"电视开幕",()=>
                {
                    app.Preferences.transitionMode=app.Preferences.transitionMode==CampaignTransitionMode.BlackFade?CampaignTransitionMode.Television:CampaignTransitionMode.BlackFade;
                    app.ApplyPreferences();OpenSettings();
                });
                Label(p,"主菜单、存档页切出时使用黑屏淡入淡出。",40,593,960,42,20,muted);
            }
            else if(settingsTab==3) DrawDisplaySettings(p);
            else
            {
                Label(p,"键盘、鼠标与手柄\n界面导航使用方向键、摇杆、确认与取消；下方可重映射经营快捷键。",35,165,970,75,21,muted);
                string[] actions={"Back","Help"};string[] names={"返回 / 关闭","帮助翻身"};
                for(int i=0;i<actions.Length;i++)
                {
                    var action=app.Controls[actions[i]];Label(p,names[i],40,270+i*85,280,55,24);
                    for(int j=0;j<2;j++){int binding=j;Btn(p,FormatBinding(action,j),330+j*335,260+i*85,320,55,()=>Rebind(action,binding));}
                }
                Btn(p,"恢复默认按键",40,525,280,50,()=>{app.Controls.RemoveAllBindingOverrides();OpenSettings();});
            }
            Label(p,"设置即时生效，关闭时自动保存。",40,632+extraHeight,960,34,18,muted);
            bool canReturn=page!=CampaignPage.MainMenu&&page!=CampaignPage.Saves;
            Btn(p,"完成",40,680+extraHeight,canReturn?630:970,54,()=>{BeetleDisplayPreferences.Save();CloseModal();QueueRefresh();});
            if(canReturn)Btn(p,"返回主菜单",690,680+extraHeight,320,54,()=>{CloseModal();app.Go(CampaignPage.MainMenu);});SelectFirst(p);
        }
        void SettingRow(RectTransform p,string title,float y,string value,Action click,bool enabled=true){Label(p,title,40,y+10,500,55,24);Btn(p,value,560,y,450,58,click,enabled);}
        void Slider(RectTransform p,string title,float y,float value,Action<float> changed)
        {
            Label(p,title,40,y+10,290,55,24);
            var percentage=Label(p,Mathf.RoundToInt(value*100)+"%",920,y+8,90,50,23,accent);percentage.alignment=TMPro.TextAlignmentOptions.Right;
            var rt=Rect("Slider "+title,p,340,y,560,56);
            RetroSettingsSlider.Create(rt,value,CampaignRetroTheme.DarkEdge,accent,CampaignRetroTheme.LightEdge,v=>
            { percentage.text=Mathf.RoundToInt(v*100)+"%";changed(v); });
        }

        void DrawDisplaySettings(RectTransform p)
        {
            Label(p,"游戏滤镜",40,184,280,48,24);
            var filterButton=Btn(p,BeetleDisplayPreferences.StyleName(BeetleDisplayPreferences.Style),350,174,660,60,()=>{});
            filterButton.name="GameFilter";
            Label(p,"仅当前场景 · 滤镜互斥 · 界面保持清晰",40,249,960,42,20,muted);
            var preview=Panel(p,40,306,470,270,true);
            if(backdrop && backdrop.Frame)
            {
                var display=Rect("Style Preview",preview,4,4,462,262).gameObject.AddComponent<UnityEngine.UI.RawImage>();
                display.texture=backdrop.Frame;display.raycastTarget=false;BeetleCombatCrt.Attach(display);
            }
            Label(p,"当前场景的实时画面预览",40,279,470,25,17,muted);
            Label(p,"像素档位",550,301,460,34,22);
            UI.Button pixelButton=null,spotlightButton=null;TMPro.TMP_Text description=null,glassDescription=null;RetroFilterDropdown dropdown=null,glassDropdown=null;
            pixelButton=Btn(p,BeetleDisplayPreferences.PixelSizeLabel,550,341,460,52,()=>
            { BeetleDisplayPreferences.NextPixelSize();RefreshControls(); },BeetleDisplayPreferences.IsDither(BeetleDisplayPreferences.Style));
            pixelButton.name="FilterPixelSize";
            description=Label(p,RetroFilterDropdown.Description(BeetleDisplayPreferences.Style),550,409,460,66,20,muted);
            dropdown=RetroFilterDropdown.Attach(filterButton,_=>RefreshControls(),(RectTransform)p.parent);
            Label(p,"玻璃效果",550,485,460,30,22);
            var glassButton=Btn(p,BeetleGlassPreferences.QualityName(BeetleGlassPreferences.Quality),550,525,460,48,()=>{});
            glassButton.name="GlassQuality";
            glassDescription=Label(p,BeetleGlassPreferences.Description(BeetleGlassPreferences.Quality),550,579,460,42,17,muted);
            glassDescription.name="GlassQualityDescription";
            glassDropdown=RetroFilterDropdown.AttachGlass(glassButton,_=>RefreshControls(),(RectTransform)p.parent);
            Btn(p,"使用编辑器配置",40,579,470,40,()=>
            { BeetleDisplayPreferences.UseSceneDefault(gameObject.scene.name);RefreshControls(); });
            Label(p,"养殖聚光灯",40,652,500,55,24);
            spotlightButton=Btn(p,BeetleHusbandryLightingPreferences.Enabled?"开启":"关闭",560,642,450,58,()=>
            { BeetleHusbandryLightingPreferences.SetEnabled(!BeetleHusbandryLightingPreferences.Enabled);RefreshControls(); });
            spotlightButton.name="HusbandrySpotlight";
            void RefreshControls()
            {
                dropdown.RefreshLabel();glassDropdown.RefreshLabel();
                CampaignLocalization.Bind(spotlightButton.GetComponentInChildren<TMPro.TMP_Text>(),BeetleHusbandryLightingPreferences.Enabled?"开启":"关闭");
                CampaignLocalization.Bind(glassDescription,BeetleGlassPreferences.Description(BeetleGlassPreferences.Quality));
                pixelButton.interactable=BeetleDisplayPreferences.IsDither(BeetleDisplayPreferences.Style);
                var pixelLabel=pixelButton.GetComponentInChildren<TMPro.TMP_Text>();
                pixelLabel.color=pixelButton.interactable?ink:muted;
                CampaignLocalization.Bind(pixelLabel,BeetleDisplayPreferences.PixelSizeLabel);
                CampaignLocalization.Bind(description,RetroFilterDropdown.Description(BeetleDisplayPreferences.Style));
            }
        }
        static string FormatBinding(InputAction action,int binding)
        {
            var display=action.GetBindingDisplayString(binding,out var layout,out var controlPath);
            if(string.IsNullOrEmpty(display))return "未绑定";
            // Resolve the effective binding, including overrides and device-specific layouts.
            // Letter keys and unknown device legends stay literal: G must never become currency.
            if(string.IsNullOrEmpty(layout)||string.IsNullOrEmpty(controlPath))return CampaignLocalization.Literal(display);
            string device=InputSystem.IsFirstLayoutBasedOnSecond(layout,"Keyboard")?"keyboard":
                InputSystem.IsFirstLayoutBasedOnSecond(layout,"Mouse")?"mouse":
                InputSystem.IsFirstLayoutBasedOnSecond(layout,"Gamepad")?"gamepad":"";
            string control=controlPath.ToLowerInvariant();
            if(device=="keyboard"&&control.Length==7&&control.StartsWith("numpad",StringComparison.Ordinal)&&char.IsDigit(control[6]))
                return "小键盘 "+control[6];
            if(device=="gamepad"&&(control=="buttonsouth"||control=="buttonnorth"||control=="buttonwest"||control=="buttoneast"))
                return CampaignLocalization.Literal(display switch {"Cross"=>"×","Circle"=>"○","Triangle"=>"△","Square"=>"□",_=>display});
            string source=(device+"/"+control) switch
            {
                "keyboard/escape"=>"Esc",
                "keyboard/space"=>"空格键",
                "keyboard/enter"=>"回车键",
                "keyboard/tab"=>"制表键",
                "keyboard/backspace"=>"退格键",
                "keyboard/delete"=>"删除键",
                "keyboard/insert"=>"插入键",
                "keyboard/home"=>"行首键",
                "keyboard/end"=>"行尾键",
                "keyboard/pageup"=>"上一页键",
                "keyboard/pagedown"=>"下一页键",
                "keyboard/uparrow"=>"↑",
                "keyboard/downarrow"=>"↓",
                "keyboard/leftarrow"=>"←",
                "keyboard/rightarrow"=>"→",
                "keyboard/leftshift"=>"左上档键",
                "keyboard/rightshift"=>"右上档键",
                "keyboard/leftctrl"=>"左控制键",
                "keyboard/rightctrl"=>"右控制键",
                "keyboard/leftalt"=>"左换档键",
                "keyboard/rightalt"=>"右换档键",
                "keyboard/leftmeta"=>"左系统键",
                "keyboard/rightmeta"=>"右系统键",
                "keyboard/contextmenu"=>"菜单键",
                "keyboard/capslock"=>"大写锁定键",
                "keyboard/numlock"=>"数字锁定键",
                "keyboard/printscreen"=>"截屏键",
                "keyboard/scrolllock"=>"滚动锁定键",
                "keyboard/pause"=>"暂停键",
                "keyboard/numpadenter"=>"小键盘回车键",
                "keyboard/numpaddivide"=>"小键盘 /",
                "keyboard/numpadmultiply"=>"小键盘 ×",
                "keyboard/numpadplus"=>"小键盘 +",
                "keyboard/numpadminus"=>"小键盘 −",
                "keyboard/numpadperiod"=>"小键盘 .",
                "keyboard/numpadequals"=>"小键盘 =",
                "mouse/leftbutton"=>"鼠标左键",
                "mouse/rightbutton"=>"鼠标右键",
                "mouse/middlebutton"=>"鼠标中键",
                "mouse/forwardbutton"=>"鼠标前进键",
                "mouse/backbutton"=>"鼠标后退键",
                "gamepad/dpad"=>"十字方向键",
                "gamepad/dpad/up"=>"十字键上",
                "gamepad/dpad/down"=>"十字键下",
                "gamepad/dpad/left"=>"十字键左",
                "gamepad/dpad/right"=>"十字键右",
                "gamepad/leftstickpress"=>"按下左摇杆",
                "gamepad/rightstickpress"=>"按下右摇杆",
                "gamepad/leftshoulder"=>"左肩键",
                "gamepad/rightshoulder"=>"右肩键",
                "gamepad/lefttrigger"=>"左扳机",
                "gamepad/righttrigger"=>"右扳机",
                "gamepad/start"=>"手柄菜单键",
                "gamepad/select"=>"手柄选择键",
                _=>null
            };
            return source??CampaignLocalization.Literal(display);
        }

        void Rebind(InputAction action,int binding)
        {
            var p=OpenModal(850,300);Label(p,"请按下新的按键或手柄按钮\nEsc 取消",40,40,770,120,29);action.Disable();
            rebind=action.PerformInteractiveRebinding(binding).WithControlsExcluding("<Mouse>/position").WithControlsExcluding("<Mouse>/delta").WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(op=>{op.Dispose();rebind=null;action.Enable();app.SavePreferences();OpenSettings();})
                .OnCancel(op=>{op.Dispose();rebind=null;action.Enable();if(!closingModal)OpenSettings();});
            rebind.Start();
        }
    }
}
