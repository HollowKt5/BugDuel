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
            var dialog=OpenModal(760,398);var openedModal=modal;
            MText(dialog,"时间速度",24,26,712,40,30,accent,true);
            var clock=State.clock;double speed=clock.speed;
            MText(dialog,$"自然一天基准：现实 {clock.realSecondsPerDay/60:0.###} 分钟（1×）",24,84,712,30,18);
            double[] speeds={.5,1,2,4,8,16,32,64};
            var speedButtons=new UnityEngine.UI.Button[speeds.Length];
            TMP_Text pace=null;
            for(int i=0;i<speeds.Length;i++)
            {
                int index=i;
                speedButtons[i]=MButton(dialog,speeds[i].ToString("0.#",CultureInfo.InvariantCulture)+"×",24+i%4*182,140+i/4*46,166,36,()=>{speed=speeds[index];RefreshClock();},true,18);
            }
            pace=MText(dialog,"",24,242,712,30,18,accent);
            MText(dialog,"建造、操作面板与格斗时暂停 · 离线不计时",24,282,712,26,17,muted);
            void RefreshClock()
            {
                for(int i=0;i<speeds.Length;i++)
                {
                    bool selected=Math.Abs(speeds[i]-speed)<.0001;
                    speedButtons[i].GetComponent<UnityEngine.UI.Image>().color=selected?CampaignRetroTheme.Selection:CampaignRetroTheme.Button;
                    speedButtons[i].GetComponentInChildren<TMP_Text>().color=selected?CampaignRetroTheme.SelectionInk:ink;
                }
                CampaignLocalization.Bind(pace,$"当前速度：现实 {clock.realSecondsPerDay/60/speed:0.###} 分钟 / 游戏日");
            }
            RefreshClock();
            dialogNotice=MText(dialog,"",24,330,438,44,16,CampaignRetroTheme.Price);
            void Done(){if(modal!=openedModal)return;CloseModal();returnTo?.Invoke();}
            modalBack=Done;
            MButton(dialog,"保存并返回",484,330,160,43,DoneSave,true,18);
            MButton(dialog,"取消",656,330,80,43,Done,true,18);
            void DoneSave()
            {
                app.Execute(session=>session.ConfigureClock(session.State.clock.realSecondsPerDay,speed,false),Done);
            }
            SelectFirst(dialog);
        }
    }
}
