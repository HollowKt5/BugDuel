using TMPro;
using UnityEngine;

namespace BeetleArena.Campaign
{
    public sealed class CampaignCalendarReadout : MonoBehaviour
    {
        TMP_Text label;
        bool compact;
        bool hasShown;
        (int year,int month,int day,int hour,int minute,double speed,int status) shown;
        public void Bind(TMP_Text target,bool singleLine) { label=target;compact=singleLine;hasShown=false;Update(); }
        void Update()
        {
            var app=CampaignApp.Instance;if(!label||app?.Session==null)return;
            var clock=app.Session.State.clock;
            int status=app.CalendarRunning?0:app.Constructing?1:2;
            var current=(clock.Year,clock.Month,clock.Day,clock.Hour,clock.Minute,clock.speed,status);
            // Compare values before formatting; localized labels already listen for locale changes.
            if(hasShown&&current==shown)return;shown=current;hasShown=true;
            string statusText=status==0?"流逝中":status==1?"建造暂停":"操作暂停";
            string text=$"第 {shown.year} 年 · {shown.month:00} 月 · {shown.day:00} 日"+(compact?"  |  ":"\n")+$"{shown.hour:00}:{shown.minute:00} · {shown.speed:0.#}×"+(compact?"":" · "+statusText);
            CampaignLocalization.Bind(label,text);
        }
    }
}
