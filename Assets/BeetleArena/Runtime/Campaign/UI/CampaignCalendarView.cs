using System;
using BugDuel.Campaign;
using TMPro;
using UnityEngine;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        UnityEngine.UI.Button DrawCalendarClock(Transform parent,float x,float y,float width,float height,bool compact=false)
        {
            var button=Btn(parent,"日历与时间",x,y,width,height,OpenTimeSettings);
            var label=button.GetComponentInChildren<TMP_Text>();label.fontSize=compact?14:19;
            label.fontSizeMin=compact?12:15;label.fontSizeMax=label.fontSize;
            Component<CampaignCalendarReadout>(button.transform).Bind(label,compact);
            return button;
        }

        void DrawCalendarHud()
        {
            var panel=Panel(canvas,0,0,332,108);
            panel.name="Game calendar";panel.anchorMin=panel.anchorMax=panel.pivot=Vector2.one;
            panel.anchoredPosition=new Vector2(-14,-14);panel.SetAsLastSibling();
            DrawCalendarClock(panel,5,5,322,61);
            var speedButton=Btn(panel,$"速度 {State.clock.speed:0.#}×",6,73,153,28,()=>
            {
                double[] speeds={.5,1,2,4,8,16,32,64};
                double speed=speeds[(Array.IndexOf(speeds,State.clock.speed)+1)%speeds.Length];
                app.Execute(s=>s.ConfigureClock(s.State.clock.realSecondsPerDay,speed,false));
            });
            var speedLabel=speedButton.GetComponentInChildren<TMP_Text>();speedLabel.enableAutoSizing=true;speedLabel.fontSizeMin=13;speedLabel.fontSizeMax=17;
            Btn(panel,"时间设置",166,73,160,28,OpenTimeSettings);
        }

        void DrawStageCare(Flow flow,CampaignBeetle beetle)
        {
            float w=flow.CardWidth,pad=12,inside=w-pad*2;
            var info=Card(flow,110);
            Label(info,CampaignSession.StageName(beetle.stage)+" · 养殖要点",pad,10,inside,30,21,accent);
            Label(info,CampaignSession.StageDescription(beetle.stage),pad,49,inside,51,17,muted);
            foreach(string item in CampaignRearingItems.All)
            {
                if(!CampaignRearingItems.Applicable(item,beetle.stage))continue;
                string id=item;int count=Session.InventoryCount(id);var row=Card(flow,133);
                Label(row,CampaignRearingItems.Name(id)+" ×"+count,pad,9,inside,29,19,accent);
                Label(row,CampaignRearingItems.Description(id),pad,40,inside,46,15,muted);
                GreenhouseButton(row,"使用养殖用品",pad,93,inside,30,()=>app.Execute(s=>s.UseRearingItem(beetle.id,id)),count>0&&!beetle.listed);
            }
            var shop=Card(flow,55);GreenhouseButton(shop,"前往市场购买阶段用品",pad,10,inside,34,()=>app.Go(CampaignPage.Market));
        }
    }
}
