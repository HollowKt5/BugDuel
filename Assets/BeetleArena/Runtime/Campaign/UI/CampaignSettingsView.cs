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
            var layout=Layout.settings;
            float extraHeight=settingsTab==3?80:0;
            var p=OpenModal(layout.size.x,layout.Height+extraHeight);settingsOpen=true;
            float padding=layout.padding,inside=p.rect.width-padding*2,tabStart=padding-10,tabWidth=(p.rect.width-tabStart*2-65)/5;
            Label(p,"设置",tabStart,25,p.rect.width-tabStart*2,50,36);
            string[] tabs={"游戏","音频","视频","画面","控制"};for(int i=0;i<tabs.Length;i++){int tab=i;Btn(p,tabs[i],tabStart+i*(tabWidth+15),95,tabWidth,48,()=>{settingsTab=tab;OpenSettings();},settingsTab!=i);}
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
                float labelWidth=inside*layout.labelFraction,buttonX=padding+labelWidth+layout.columnGap;
                Label(p,"当前季节（随游戏日历）",padding,layout.RowY(410)+10,labelWidth,55,24);
                Label(p,CampaignRetroTheme.SeasonName,buttonX,layout.RowY(410)+10,p.rect.width-padding-buttonX,55,24);
                SettingRow(p,"明暗模式",470,CampaignRetroTheme.Dark?"经典暗色":"明亮浅色",()=>{CampaignRetroTheme.SetMode(!CampaignRetroTheme.Dark);QueueRefresh();OpenSettings();});
                SettingRow(p,"时间速度",530,Session==null?"开始游戏后可设置":"调整自然时间倍率",()=>OpenTimeSettings(OpenSettings),Session!=null);
                SettingRow(p,"界面风格",590,CampaignRetroTheme.StyleName,()=>
                {
                    CampaignRetroTheme.SetStyle(CampaignRetroTheme.Paper?CampaignUiStyle.Classic:CampaignUiStyle.ComicPaper);
                    QueueRefresh();OpenSettings();
                });
            }
            else if(settingsTab==1)
            {
                var arenaAudio=FindFirstObjectByType<ArenaAudio>();
                Slider(p,"主音量",190,app.Preferences.master,v=>{app.Preferences.master=v;AudioListener.volume=v;});
                Slider(p,"音乐",275,app.Preferences.music,v=>app.Preferences.music=v);
                Slider(p,"自然环境",360,app.Preferences.nature,v=>app.Preferences.nature=v);
                Slider(p,"甲壳碰撞音效",445,app.Preferences.carapace,v=>{app.Preferences.carapace=v;if(arenaAudio)arenaAudio.volume=v;});
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
            }
            else if(settingsTab==3) DrawDisplaySettings(p);
            else
            {
                Label(p,"点击按键重新绑定",padding-5,185,inside,45,21,muted);
                float bindingStart=padding+inside*290/970,bindWidth=(p.rect.width-padding-bindingStart-15)/2;
                string[] actions={"Back","Help"};string[] names={"返回 / 关闭","帮助翻身"};
                for(int i=0;i<actions.Length;i++)
                {
                    var action=app.Controls[actions[i]];Label(p,names[i],padding,270+i*85,bindingStart-padding-10,55,24);
                    for(int j=0;j<2;j++){int binding=j;Btn(p,FormatBinding(action,j),bindingStart+j*(bindWidth+15),260+i*85,bindWidth,55,()=>Rebind(action,binding));}
                }
                Btn(p,"恢复默认按键",padding,525,280,50,()=>{app.Controls.RemoveAllBindingOverrides();OpenSettings();});
            }
            bool canReturn=page!=CampaignPage.MainMenu&&page!=CampaignPage.Saves;
            Btn(p,"完成",padding,p.rect.height-80,canReturn?inside-340:inside,54,()=>{BeetleDisplayPreferences.Save();CloseModal();QueueRefresh();});
            if(canReturn)Btn(p,"返回主菜单",p.rect.width-padding-320,p.rect.height-80,320,54,()=>{CloseModal();app.Go(CampaignPage.MainMenu);});SelectFirst(p);
        }
        void SettingRow(RectTransform p,string title,float y,string value,Action click,bool enabled=true)
        {
            var layout=Layout.settings;
            float labelWidth=(p.rect.width-layout.padding*2)*layout.labelFraction,buttonX=layout.padding+labelWidth+layout.columnGap;
            y=layout.RowY(y);
            Label(p,title,layout.padding,y+10,labelWidth,55,24);
            Btn(p,value,buttonX,y,p.rect.width-layout.padding-buttonX,58,click,enabled);
        }
        void Slider(RectTransform p,string title,float y,float value,Action<float> changed)
        {
            float padding=Layout.settings.padding,inside=p.rect.width-padding*2,labelWidth=inside*290/970,sliderX=padding+labelWidth+10;
            Label(p,title,padding,y+10,labelWidth,55,24);
            var percentage=Label(p,Mathf.RoundToInt(value*100)+"%",p.rect.width-padding-90,y+8,90,50,23,accent);percentage.alignment=TMPro.TextAlignmentOptions.Right;
            var rt=Rect("Slider "+title,p,sliderX,y,p.rect.width-padding-110-sliderX,56);
            RetroSettingsSlider.Create(rt,value,CampaignRetroTheme.DarkEdge,accent,CampaignRetroTheme.LightEdge,v=>
            { percentage.text=Mathf.RoundToInt(v*100)+"%";changed(v); });
        }

        void DrawDisplaySettings(RectTransform p)
        {
            float padding=Layout.settings.padding,inside=p.rect.width-padding*2;
            float leftWidth=(inside-40)*470/930,rightX=padding+leftWidth+40,rightWidth=p.rect.width-padding-rightX;
            float filterX=padding+inside*310/970;
            Label(p,"游戏滤镜",padding,184,filterX-padding-30,48,24);
            var filterButton=Btn(p,BeetleDisplayPreferences.StyleName(BeetleDisplayPreferences.Style),filterX,174,p.rect.width-padding-filterX,60,()=>{});
            filterButton.name="GameFilter";
            Label(p,"仅当前场景",padding,249,inside-10,42,20,muted);
            var preview=Panel(p,padding,306,leftWidth,270,true);
            if(backdrop && backdrop.Frame)
            {
                var display=Rect("Style Preview",preview,4,4,leftWidth-8,262).gameObject.AddComponent<UnityEngine.UI.RawImage>();
                backdrop.previewOutput=display;
                display.texture=backdrop.WorldFrame;display.raycastTarget=false;BeetleCombatCrt.Attach(display);
            }
            Label(p,"像素档位",rightX,301,rightWidth,34,22);
            UI.Button pixelButton=null,spotlightButton=null;TMPro.TMP_Text description=null,glassDescription=null;RetroFilterDropdown dropdown=null,glassDropdown=null;
            pixelButton=Btn(p,BeetleDisplayPreferences.PixelSizeLabel,rightX,341,rightWidth,52,()=>
            { BeetleDisplayPreferences.NextPixelSize();RefreshControls(); },BeetleDisplayPreferences.IsDither(BeetleDisplayPreferences.Style));
            pixelButton.name="FilterPixelSize";
            description=Label(p,RetroFilterDropdown.Description(BeetleDisplayPreferences.Style),rightX,409,rightWidth,66,20,muted);
            dropdown=RetroFilterDropdown.Attach(filterButton,_=>RefreshControls(),(RectTransform)p.parent);
            Label(p,"玻璃效果",rightX,485,rightWidth,30,22);
            var glassButton=Btn(p,BeetleGlassPreferences.QualityName(BeetleGlassPreferences.Quality),rightX,525,rightWidth,48,()=>{});
            glassButton.name="GlassQuality";
            glassDescription=Label(p,BeetleGlassPreferences.Description(BeetleGlassPreferences.Quality),rightX,579,rightWidth,42,17,muted);
            glassDescription.name="GlassQualityDescription";
            glassDropdown=RetroFilterDropdown.AttachGlass(glassButton,_=>RefreshControls(),(RectTransform)p.parent);
            Btn(p,"使用编辑器配置",padding,579,leftWidth,40,()=>
            { BeetleDisplayPreferences.UseSceneDefault(gameObject.scene.name);RefreshControls(); });
            float labelWidth=inside*Layout.settings.labelFraction,buttonX=padding+labelWidth+Layout.settings.columnGap;
            Label(p,"养殖聚光灯",padding,652,labelWidth,55,24);
            spotlightButton=Btn(p,BeetleHusbandryLightingPreferences.Enabled?"开启":"关闭",buttonX,642,p.rect.width-padding-buttonX,58,()=>
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
