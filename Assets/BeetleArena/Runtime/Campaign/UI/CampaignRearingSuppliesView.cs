using System.Linq;
using BugDuel.Campaign;
using UnityEngine;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        void OpenRearingSupplyTargets(string itemId)
        {
            var dialog=MarketDialog("养殖用品 · 选择使用对象",940,690);var openedModal=modal;
            MText(dialog,$"{CampaignRearingItems.Name(itemId)} · 库存 {Session.InventoryCount(itemId)} 件",20,48,900,36,24,accent,true);
            MText(dialog,CampaignRearingItems.Description(itemId),20,97,900,59,19,muted);
            var list=Column(20,171,900,398,dialog);int targets=0;
            foreach(var beetle in State.beetles.Concat(State.storedBeetles))
            {
                var b=beetle;bool stored=State.storedBeetles.Contains(b);
                if(b.dead||!CampaignRearingItems.Applicable(itemId,b.stage))continue;
                targets++;var row=Card(list,100);
                MText(row,CampaignLocalization.Name(b),14,12,614,32,22,accent,true);
                string care=b.stage==CampaignLifeStage.ActiveAdult?$"饱食 {b.nutrition:0}% / 健康 {b.hearts:0.#} 心":$"基质营养 {b.substrateNutrition:0}% / 洁净 {b.substrateCleanliness:0}%";
                MText(row,(stored?"仓库暂存":"温室入住")+" · "+CampaignSession.StageName(b.stage)+" · "+care,14,55,614,31,17,muted);
                bool available=!b.listed&&State.battle?.beetleId!=b.id&&State.auction?.beetleId!=b.id;
                MButton(row,available?"使用一件":"甲虫忙碌中",642,31,212,38,()=>app.Execute(s=>s.UseRearingItem(b.id,itemId),()=>
                {
                    if(!this||modal!=openedModal)return;
                    OpenWarehouse();
                }),available&&Session.InventoryCount(itemId)>0,18);
            }
            if(targets==0)MText(dialog,"暂无适用甲虫",30,286,880,80,24,muted);
            dialogNotice=MText(dialog,"",20,585,900,39,17,CampaignRetroTheme.Price);
            MButton(dialog,"返回仓库",20,636,900,34,OpenWarehouse,true,18);modalBack=OpenWarehouse;SelectFirst(dialog);
        }
    }
}
