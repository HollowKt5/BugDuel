using System.Linq;
using UnityEngine;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        enum ControlHint { None=0,Beetle=1,FeedingDish=2,Camera=4,Warehouse=8,Construction=16,Pickup=32 }
        ControlHint shownControlHint;
        RectTransform controlHintPanel;
        bool Learned(ControlHint hint)=>(app.Preferences.learnedControls&(int)hint)!=0;
        void RememberControlHint(ControlHint hint)
        {
            if(hint==ControlHint.None||Learned(hint))return;
            app.Preferences.learnedControls|=(int)hint;app.SavePreferences(false);QueueRefresh();
        }
        ControlHint NeededControlHint()
        {
            if(page!=CampaignPage.Greenhouse||Session==null)return ControlHint.None;
            if(BeetleGrab&&BeetleGrab.IsManipulating)return ControlHint.None;
            if(construction&&construction.Editing)return construction.HasPreview&&!Learned(ControlHint.Construction)?ControlHint.Construction:ControlHint.None;
            if(warehouseOpen)return warehouseCategory<0&&!Learned(ControlHint.Warehouse)?ControlHint.Warehouse:ControlHint.None;
            if(greenhouseInfoOpen||feedingDishOpen)return ControlHint.None;
            var beetle=State.beetles.FirstOrDefault(b=>b.id==State.selectedId);
            if(beetle==null||beetle.dead||beetle.overturned)return ControlHint.None;
            if(!Learned(ControlHint.Beetle))return ControlHint.Beetle;
            if(beetle.Adult(State.week)&&!Learned(ControlHint.FeedingDish))return ControlHint.FeedingDish;
            if(beetle.Adult(State.week)&&!Learned(ControlHint.Pickup))return ControlHint.Pickup;
            return !Learned(ControlHint.Camera)?ControlHint.Camera:ControlHint.None;
        }
        void UpdateControlHints()
        {
            if(page!=CampaignPage.Greenhouse||Session==null)return;
            if(warehouseOpen&&warehouseCategory>=0)RememberControlHint(ControlHint.Warehouse);
            if(observation&&observation.UserInputRevision>0)RememberControlHint(ControlHint.Camera);
            if(construction&&construction.PlacementRevision>0)RememberControlHint(ControlHint.Construction);
            if(BeetleGrab&&BeetleGrab.PickupCount>0)RememberControlHint(ControlHint.Pickup);
            if(controlHintPanel)controlHintPanel.gameObject.SetActive(!modal&&!app.Busy&&shownControlHint!=ControlHint.None);
            if(shownControlHint!=NeededControlHint())QueueRefresh();
        }
        void DrawControlHint()
        {
            shownControlHint=NeededControlHint();if(shownControlHint==ControlHint.None)return;
            string text=shownControlHint switch
            {
                ControlHint.Beetle=>"点击甲虫查看信息，并进行管理或布置环境。",
                ControlHint.FeedingDish=>"点击缸内的喂食盆，可以放入食物或清理残渣。",
                ControlHint.Camera=>"按住鼠标右键拖动可旋转视角，滚轮缩放。按 Esc 打开设置。",
                ControlHint.Pickup=>"长按甲虫背部0.7秒，白圈转满后提起；松开左键放下。",
                ControlHint.Warehouse=>"先选择左侧分类，再点击物品查看和使用。",
                _=>"移动鼠标选择位置，左键放置；Q / E 旋转，Esc 取消。"
            };
            var layout=Layout.hint;
            float width=layout.size.x,padding=layout.padding;
            const float bodyTop=48,buttonHeight=32,bottomPadding=14;
            var panel=controlHintPanel=Panel(canvas,0,0,width,layout.size.y,name:"ControlHint");
            panel.anchorMin=panel.anchorMax=panel.pivot=new Vector2(1,0);panel.anchoredPosition=new Vector2(-layout.inset.x,layout.inset.y);panel.SetAsLastSibling();
            var title=Label(panel,"操作提示",padding,12,width-padding*2,28,18,accent);
            title.alignment=TMPro.TextAlignmentOptions.TopLeft;
            var body=Label(panel,text,padding,bodyTop,width-padding*2,48,19);
            body.alignment=TMPro.TextAlignmentOptions.TopLeft;body.enableAutoSizing=false;
            float bodyHeight=Mathf.Max(48,Mathf.Ceil(body.GetPreferredValues(body.text,width-padding*2,float.PositiveInfinity).y)+4);
            body.rectTransform.sizeDelta=new Vector2(width-padding*2,bodyHeight);
            panel.sizeDelta=new Vector2(width,Mathf.Max(layout.size.y,bodyTop+bodyHeight+14+buttonHeight+bottomPadding));
            var hint=shownControlHint;
            var button=GreenhouseButton(panel,"知道了",0,0,100,buttonHeight,()=>RememberControlHint(hint));
            var buttonRect=(RectTransform)button.transform;
            buttonRect.anchorMin=buttonRect.anchorMax=buttonRect.pivot=new Vector2(1,0);
            buttonRect.anchoredPosition=new Vector2(-padding,bottomPadding);
        }
    }
}
