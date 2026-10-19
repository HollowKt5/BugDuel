using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UI=UnityEngine.UI;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        void DrawMenu()
        {
            Label(root,"BUG",85,65,650,150,90,accent);
            Label(root,"DUEL",85,190,650,150,90,accent);
            Label(root,L("甲虫决斗 · 小小虫舍，大大梦想","Tiny beetles. Grand ambitions."),90,350,640,50,25);
            Btn(root,"Start  /  开始",90,435,380,64,()=>app.Go(CampaignPage.Saves));
            Btn(root,"Setting  /  设置",90,515,380,64,OpenSettings);
            Btn(root,"Introduction  /  游玩指南",90,595,380,64,OpenIntro);
            Btn(root,"Exit  /  退出",90,675,380,64,()=>Confirm(L("保存当前进度并退出游戏？","Save progress and exit?"),()=>{if(Session==null||app.SaveSlot(0))Application.Quit();}));
            Label(root,"OFFLINE  •  COLLECT  •  CARE  •  COMPETE",92,775,850,30,18,muted);
        }
        void DrawSaves()
        {
            Label(root,L("虫舍日记 / 选择存档","Your greenhouse / Save slots"),45,45,1200,60,38);
            Label(root,L("1个自动槽 + 3个手动槽。点击空槽新建存档，或读取已有旅程。","One auto slot and three manual slots. Create a save in an empty slot or load a journey."),48,120,1480,45,21,muted);
            for(int slot=0;slot<4;slot++)
            {
                int index=slot;var p=Panel(root,40+slot*390,205,365,540);
                Label(p,slot==0?"AUTO SAVE":$"JOURNAL 0{slot}",22,20,320,32,23,accent);
                CampaignSave save=null;string error=null;try{save=app.ReadSlot(slot);}catch(Exception e){error=e.Message;}
                if(save==null && error==null)
                {
                    var create=Btn(p,L("新建存档","Create save"),62.5f,242,240,56,()=>
                    {
                        if(Session==null)app.NewGame(index);
                        else Confirm("在此空槽开始新的旅程？已有手动存档保留，后续自动保存会记录新旅程。",()=>app.NewGame(index));
                    });
                    var center=(RectTransform)create.transform;center.anchorMin=center.anchorMax=center.pivot=new Vector2(.5f,.5f);center.anchoredPosition=Vector2.zero;
                    continue;
                }
                var thumbnail=Rect("Thumbnail",p,20,70,325,180);var image=Component<UI.RawImage>(thumbnail);image.texture=null;image.color=new Color(.24f,.31f,.23f);image.raycastTarget=false;
                if(!string.IsNullOrEmpty(save?.thumbnail))try{var t=new Texture2D(2,2);if(t.LoadImage(Convert.FromBase64String(save.thumbnail))){image.texture=t;image.color=Color.white;thumbnails.Add(t);}else Destroy(t);}catch{ }
                if(save!=null)
                    Label(p,$"第 {save.state.week} 周  ·  {save.state.coins:N0} G\n栏位 {save.state.beetles.Count}/{save.state.capacity}\n最高联赛 Lv.{save.state.highestLeague}\n体长纪录 {save.state.recordLength:0.0} mm\n{save.savedAt}",22,270,320,155,22);
                else Label(p,"存档不可读\n"+error,22,280,320,150,22,muted);
                Btn(p,L("读取","Load"),20,445,155,48,()=>app.LoadSlot(index),save!=null);
                Btn(p,L("覆盖","Overwrite"),185,445,155,48,()=>Confirm("覆盖此槽位的存档？",()=>app.SaveSlot(index)),Session!=null);
                Btn(p,L("删除","Delete"),20,500,320,32,()=>Confirm("永久删除此槽位及备份？",()=>app.DeleteSlot(index)),save!=null||error!=null);
            }
            Btn(root,Session!=null?L("返回温室","Greenhouse"):L("返回主菜单","Main menu"),45,770,310,50,()=>app.Go(Session!=null?CampaignPage.Greenhouse:CampaignPage.MainMenu));
        }
        void OpenIntro()
        {
            var p=OpenModal(1450,535);Label(p,L("从小虫舍到世界虫王","From a tiny greenhouse to the throne"),30,28,1360,50,34);
            string[] titles={"01  捕虫 / 买虫","02  喂养 / 冲体长","03  联赛 / 拍卖"};
            string[] bodies={"派遣队伍去四大栖息地。\n在市场买虫、竞拍神秘蛹。\n别忘了为回程预留栏位。","红薯和水果补充饱食与健康。\n幼虫选饲料瓶和温控方案。\n甲虫翻不过身时，要及时帮忙！","体长决定联赛级别。\n三连胜夺冠，身价大涨！\n卖冠军或首配权，收血统分红。"};
            string[] en={"Send squads to four habitats.\nBuy beetles or bid on mystery pupae.\nReserve room for the returning squad.","Feed adults and help overturned beetles.\nChoose feed and temperature for larvae.\nBreed stronger genes and rare colors.","Length decides the league tier.\nThree wins in a row earn a championship.\nAuction champions or breeding rights."};
            for(int i=0;i<3;i++){var c=Panel(p,30+i*470,110,450,310,true);Label(c,app.Preferences.language==0?titles[i]:new[]{"01  Collect","02  Care","03  Compete"}[i],24,25,400,50,29,accent);Label(c,L(bodies[i],en[i]),24,100,400,190,24);}
            Btn(p,L("出发吧！","Let's go!"),520,450,410,55,CloseModal);SelectFirst(p);
        }
        void OpenSettings()
        {
            var p=OpenModal(1050,700);settingsOpen=true;Label(p,L("设置","Settings"),30,25,850,50,36);
            string[] tabs={"Game","Audio","Video","Controls"};for(int i=0;i<4;i++){int tab=i;Btn(p,tabs[i],30+i*250,95,235,48,()=>{settingsTab=tab;OpenSettings();},settingsTab!=i);}
            if(settingsTab==0)
            {
                SettingRow(p,"界面语言 / Language",190,app.Preferences.language==0?"简体中文":"English (navigation)",()=>{app.Preferences.language=1-app.Preferences.language;OpenSettings();});
                SettingRow(p,"定时自动保存（交易即时保存）",265,app.Preferences.autoSaveSeconds+" s",()=>{int[] values={30,60,180,300};app.Preferences.autoSaveSeconds=values[(Array.IndexOf(values,app.Preferences.autoSaveSeconds)+1)%4];OpenSettings();});
                SettingRow(p,"默认战斗速度",340,app.Preferences.battleSpeed+"×",()=>{app.Preferences.battleSpeed=app.Preferences.battleSpeed==1?2:app.Preferences.battleSpeed==2?4:1;OpenSettings();});
                Label(p,"经营数据随交易、报名、结算即时存档。\n定时保存负责翻身状态等持续变化；离线不会继续扣健康。",40,445,960,110,22,muted);
            }
            else if(settingsTab==1)
            {
                Slider(p,"主音量",190,app.Preferences.master,v=>{app.Preferences.master=v;app.ApplyPreferences();});
                Slider(p,"音乐",275,app.Preferences.music,v=>app.Preferences.music=v);
                Slider(p,"自然环境",360,app.Preferences.nature,v=>app.Preferences.nature=v);
                Slider(p,"甲壳碰撞 ASMR",445,app.Preferences.carapace,v=>{app.Preferences.carapace=v;app.ApplyPreferences();});
            }
            else if(settingsTab==2)
            {
                string[] resolutions={"1280 × 720","1600 × 900","1920 × 1080","2560 × 1440"};
                SettingRow(p,"分辨率",180,resolutions[Mathf.Clamp(app.Preferences.resolution,0,3)],()=>{app.Preferences.resolution=(app.Preferences.resolution+1)%4;OpenSettings();});
                SettingRow(p,"窗口模式",255,new[]{"窗口","无边框全屏","独占全屏"}[Mathf.Clamp(app.Preferences.windowMode,0,2)],()=>{app.Preferences.windowMode=(app.Preferences.windowMode+1)%3;OpenSettings();});
                SettingRow(p,"帧率 / VSync",330,app.Preferences.fps+" FPS / "+(app.Preferences.vsync?"ON":"OFF"),()=>{if(app.Preferences.fps==60)app.Preferences.fps=120;else if(app.Preferences.fps==120)app.Preferences.fps=30;else{app.Preferences.fps=60;app.Preferences.vsync=!app.Preferences.vsync;}OpenSettings();});
                SettingRow(p,"微距景深",405,app.Preferences.depthOfField?"开启":"关闭",()=>{app.Preferences.depthOfField=!app.Preferences.depthOfField;OpenSettings();});
                SettingRow(p,L("经营页转场","Management transitions"),480,app.Preferences.transitionMode==CampaignTransitionMode.BlackFade?L("黑屏淡入淡出","Fade through black"):L("电视开幕","TV opening"),()=>
                {
                    app.Preferences.transitionMode=app.Preferences.transitionMode==CampaignTransitionMode.BlackFade?CampaignTransitionMode.Television:CampaignTransitionMode.BlackFade;
                    app.ApplyPreferences();OpenSettings();
                });
                Label(p,"主菜单、存档页切出时使用黑屏淡入淡出。",40,552,960,42,21,muted);
            }
            else
            {
                Label(p,"键鼠 / Xbox / PlayStation / Switch 控制器\n界面导航使用方向键、摇杆、确认与取消；下方可重映射经营快捷键。",35,165,970,75,21,muted);
                string[] actions={"Back","NextWeek","Help"};string[] names={"返回 / 关闭","下一周","帮助翻身"};
                for(int i=0;i<3;i++)
                {
                    var action=app.Controls[actions[i]];Label(p,names[i],40,270+i*85,280,55,24);
                    for(int j=0;j<2;j++){int binding=j;Btn(p,action.GetBindingDisplayString(j),330+j*335,260+i*85,320,55,()=>Rebind(action,binding));}
                }
                Btn(p,"恢复默认按键",40,525,280,50,()=>{app.Controls.RemoveAllBindingOverrides();OpenSettings();});
            }
            Btn(p,L("保存并关闭","Save & close"),40,610,460,54,()=>{app.SavePreferences();CloseModal();QueueRefresh();});
            Btn(p,L("返回","Back"),530,610,480,54,()=>{app.SavePreferences();CloseModal();});SelectFirst(p);
        }
        void SettingRow(RectTransform p,string title,float y,string value,Action click){Label(p,title,40,y+10,500,55,24);Btn(p,value,560,y,450,58,click);}
        void Slider(RectTransform p,string title,float y,float value,Action<float> changed)
        {
            Label(p,title,40,y,330,55,24);var rt=Panel(p,380,y+10,625,30,true);var slider=rt.gameObject.AddComponent<UI.Slider>();slider.minValue=0;slider.maxValue=1;
            var fill=Panel(rt,0,0,625,30,true);fill.GetComponent<UI.Image>().color=accent;slider.fillRect=fill;slider.value=value;slider.onValueChanged.AddListener(v=>changed(v));
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
