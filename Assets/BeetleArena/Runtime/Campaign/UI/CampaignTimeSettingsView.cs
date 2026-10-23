using System;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        void OpenTimeSettings() => OpenTimeSettings(null);

        void OpenTimeSettings(Action returnTo)
        {
            if(Session==null)return;
            var dialog=OpenModal(760,470);var openedModal=modal;
            MText(dialog,"时间速度",24,26,712,40,30,accent,true);
            MText(dialog,"仅影响当前存档 · 保存后应用 · 取消会放弃本次修改",24,76,712,28,17,muted);
            var clock=State.clock;double speed=clock.speed;
            MText(dialog,$"自然一天基准：现实 {clock.realSecondsPerDay/60:0.###} 分钟（1×）",24,120,712,30,18);
            double[] speeds={.5,1,2,4,8,16,32,64};
            var speedButtons=new UnityEngine.UI.Button[speeds.Length];
            TMP_Text pace=null;
            for(int i=0;i<speeds.Length;i++)
            {
                int index=i;
                speedButtons[i]=MButton(dialog,speeds[i].ToString("0.#",CultureInfo.InvariantCulture)+"×",24+i%4*182,176+i/4*46,166,36,()=>{speed=speeds[index];RefreshClock();},true,18);
            }
            pace=MText(dialog,"",24,276,712,30,18,accent);
            MText(dialog,"每月 30 天 · 每年 12 个月\n建造、操作面板与格斗时自动暂停\n关闭游戏后不增长离线时间",24,320,712,66,17,muted);
            void RefreshClock()
            {
                for(int i=0;i<speeds.Length;i++)
                {
                    bool selected=Math.Abs(speeds[i]-speed)<.0001;
                    speedButtons[i].GetComponent<UnityEngine.UI.Image>().color=selected?CampaignRetroTheme.Frame:CampaignRetroTheme.Button;
                    speedButtons[i].GetComponentInChildren<TMP_Text>().color=selected?Color.white:ink;
                }
                CampaignLocalization.Bind(pace,$"当前速度：现实 {clock.realSecondsPerDay/60/speed:0.###} 分钟 / 游戏日");
            }
            RefreshClock();
            dialogNotice=MText(dialog,"",24,402,438,44,16,CampaignRetroTheme.Price);
            void Done(){if(modal!=openedModal)return;CloseModal();returnTo?.Invoke();}
            modalBack=Done;
            MButton(dialog,"保存并返回",484,402,160,43,DoneSave,true,18);
            MButton(dialog,"取消",656,402,80,43,Done,true,18);
            void DoneSave()
            {
                app.Execute(session=>session.ConfigureClock(session.State.clock.realSecondsPerDay,speed,false),Done);
            }
            SelectFirst(dialog);
        }
    }
}
