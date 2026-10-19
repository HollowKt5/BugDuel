using System;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace BeetleArena.Campaign
{
    // A display clock only; local wall time never advances husbandry or offline damage.
    public sealed class CampaignLocalClock : MonoBehaviour
    {
        TMP_Text label;
        long minute=-1;
        public static string Format(DateTime time) => time.ToString("MM月dd日\nHH:mm",CultureInfo.InvariantCulture);
        public void Bind(TMP_Text target) { label=target;minute=-1;Update(); }
        void Update()
        {
            if(!label)return;var now=DateTime.Now;long current=now.Ticks/TimeSpan.TicksPerMinute;
            if(current==minute)return;minute=current;label.text=Format(now);
        }
    }
}
