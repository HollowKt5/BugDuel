using TMPro;
using UnityEngine;

namespace BeetleArena.Campaign
{
    public sealed class CampaignCalendarReadout : MonoBehaviour
    {
        TMP_Text label;
        bool compact;
        string shown;
        public void Bind(TMP_Text target,bool singleLine) { label=target;compact=singleLine;shown=null;Update(); }
        void Update()
        {
            var app=CampaignApp.Instance;if(!label||app?.Session==null)return;
            var clock=app.Session.State.clock;
            string status=app.CalendarRunning?"流逝中":app.Constructing?"建造暂停":"操作暂停";
            string text=$"第 {clock.Year} 年 · {clock.Month:00} 月 · {clock.Day:00} 日"+(compact?"  |  ":"\n")+$"{clock.Hour:00}:{clock.Minute:00} · {clock.speed:0.#}× · {status}";
            if(text==shown)return;shown=text;CampaignLocalization.Bind(label,text);
        }
    }
}
