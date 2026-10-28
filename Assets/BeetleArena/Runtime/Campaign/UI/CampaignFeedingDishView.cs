using System.Linq;
using BugDuel.Campaign;
using UnityEngine;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        bool feedingDishOpen;
        string feedingDishBeetle;
        RectTransform feedingDishWindow;
        public bool FeedingDishOpen=>feedingDishOpen;

        void OpenFeedingDish()
        {
            var beetle=State.beetles.FirstOrDefault(b=>b.id==State.selectedId);
            if(beetle==null||!beetle.Adult(State.week))return;
            CloseGreenhouseInfo();feedingDishBeetle=beetle.id;feedingDishOpen=true;
            RememberControlHint(ControlHint.FeedingDish);QueueRefresh();
        }
        void CloseFeedingDish()
        {
            if(!feedingDishOpen)return;
            feedingDishOpen=false;feedingDishBeetle=null;
            if(feedingDishWindow)feedingDishWindow.gameObject.SetActive(false);
            if(UnityEngine.EventSystems.EventSystem.current)UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            QueueRefresh();
        }
        void DrawFeedingDish()
        {
            var b=State.beetles.FirstOrDefault(value=>value.id==feedingDishBeetle);
            if(b==null||b.id!=State.selectedId||!b.Adult(State.week)){CloseFeedingDish();return;}
            const float width=440,height=325;
            var panel=feedingDishWindow=Panel(canvas,0,0,width,height,name:"FeedingDishWindow");
            panel.SetAsLastSibling();panel.anchorMin=panel.anchorMax=panel.pivot=Vector2.one;panel.anchoredPosition=new Vector2(-24,-48);
            Component<RetroBevel>(panel).Configure(3,CampaignRetroTheme.LightEdge,CampaignRetroTheme.DarkEdge);
            var shadow=Component<UnityEngine.UI.Shadow>(panel);shadow.enabled=!CampaignRetroTheme.Paper;shadow.effectColor=new Color(0,0,0,.3f);shadow.effectDistance=new Vector2(4,-5);
            MTitle(panel,"喂食盆",width-6);
            MButton(panel,"×",width-35,5,28,26,CloseFeedingDish,true,18,false,CampaignRetroTheme.Danger,CampaignRetroTheme.TitleInk);
            Label(panel,CampaignLocalization.Name(b),16,44,width-32,29,22,accent);
            string status=b.meal!=null?"盆中食物："+CampaignItems.Name(CampaignItems.Food(b.meal.food)):b.foodResidue?"残渣待清理":"食盆为空";
            Label(panel,status,16,79,width-32,35,19);
            bool available=!b.listed&&State.battle?.beetleId!=b.id&&State.auction?.beetleId!=b.id;
            for(int i=0;i<3;i++)
            {
                int index=i,count=Session.InventoryCount(CampaignItems.Food(i));
                var row=Panel(panel,14,125+i*49,width-28,43,name:"DishFood "+i);
                Label(row,CampaignItems.Name(CampaignItems.Food(i))+" ×"+count,10,3,225,35,20);
                GreenhouseButton(row,"放入食物",width-145,4,108,35,()=>app.Execute(s=>s.Feed(b.id,index)),available&&!b.overturned&&b.meal==null&&count>0);
            }
            GreenhouseButton(panel,b.meal!=null?"清空食盆":"清理残渣",14,280,199,32,()=>
            {
                if(b.meal!=null)Confirm("清空会丢弃尚未吃完的食物，不会退回仓库。确定清理？",()=>app.Execute(s=>s.ClearFoodDish(b.id)));
                else app.Execute(s=>s.ClearFoodDish(b.id));
            },available&&(b.meal!=null||b.foodResidue));
            GreenhouseButton(panel,"去市场",227,280,199,32,()=>app.Go(CampaignPage.Market));
        }
    }
}
