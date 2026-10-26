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
            var label=button.GetComponentInChildren<TMP_Text>();CampaignLocalization.SetFontSize(label,compact?14:19,compact?12:15);
            Component<CampaignCalendarReadout>(button.transform).Bind(label,compact);
            return button;
        }

        void DrawCalendarHud()
        {
            var panel=Panel(canvas,0,0,332,71);
            panel.name="Game calendar";panel.anchorMin=panel.anchorMax=panel.pivot=Vector2.one;
            panel.anchoredPosition=new Vector2(-14,-14);panel.SetAsLastSibling();
            DrawCalendarClock(panel,5,5,322,61);
        }

        void DrawStageCare(Flow flow,CampaignBeetle beetle)
        {
            float w=flow.CardWidth,pad=12,inside=w-pad*2;
            foreach(string item in CampaignRearingItems.All)
            {
                if(!CampaignRearingItems.Applicable(item,beetle.stage))continue;
                string id=item;int count=Session.InventoryCount(id);if(beetle.Adult(State.week)&&count==0)continue;var row=Card(flow,133);
                Label(row,CampaignRearingItems.Name(id)+" ×"+count,pad,9,inside,29,19,accent);
                Label(row,CampaignRearingItems.Description(id),pad,40,inside,46,15,muted);
                GreenhouseButton(row,"使用养殖用品",pad,93,inside,30,()=>app.Execute(s=>s.UseRearingItem(beetle.id,id)),count>0&&!beetle.listed);
            }
            if(!beetle.Adult(State.week)){var shop=Card(flow,55);GreenhouseButton(shop,"前往市场购买阶段用品",pad,10,inside,34,()=>app.Go(CampaignPage.Market));}
        }
    }
}
