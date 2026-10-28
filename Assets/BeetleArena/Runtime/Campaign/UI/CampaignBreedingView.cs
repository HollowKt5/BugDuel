using System.Linq;
using BugDuel.Campaign;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        string breedingFather;
        void DrawBreedingCenter()
        {
            Label(root,"育种中心",52,120,620,50,34,accent);
            Label(root,"母虫由中心提供，不转让。",52,177,1470,36,22,muted);
            var males=State.beetles.Where(b=>!b.female&&b.Adult(State.week)).ToArray();
            if(!males.Any(b=>b.id==breedingFather))breedingFather=males.FirstOrDefault()?.id;
            var father=males.FirstOrDefault(b=>b.id==breedingFather);
            var list=Column(52,232,350,577,name:"BreedingFathers");
            foreach(var male in males)
            {
                var b=male;var row=Card(list,144);
                Label(row,CampaignLocalization.Name(b),14,12,308,34,24,accent);
                Label(row,$"{Session.Catalog.Species(b.speciesId).name} · {b.length:0.0} mm",14,52,308,30,19);
                Btn(row,b.id==breedingFather?"已选父本":"选择父本",14,94,308,36,()=>{breedingFather=b.id;QueueRefresh();},b.id!=breedingFather);
            }
            if(father==null){Label(root,"没有可用的雄性成虫",440,240,1040,60,28,muted);return;}
            bool ready=!father.listed&&!father.overturned&&father.hearts>=System.Math.Min(3,father.maxHearts)&&father.nutrition>=40&&
                father.cooldownWeek<=State.week&&State.battle?.beetleId!=father.id&&State.auction?.beetleId!=father.id&&State.beetles.Count+Session.Reserved<State.capacity;
            var offers=Column(426,232,1120,577,name:"BreedingMothers");
            for(int i=0;i<CampaignSession.BreedingOfferCount;i++)
            {
                var offer=Session.BreedingOffer(father.id,i);var m=offer.Mother;var row=Card(offers,208);
                Label(row,m.name+"  ·  "+Session.Catalog.Species(m.speciesId).name,20,12,720,34,25,accent);
                Label(row,$"{m.length:0.0} mm · {string.Join(" / ",m.genes.Select(g=>g.id+new string('★',g.stars)))}",20,52,720,33,21);
                Label(row,$"价格基础失败率 {offer.BaseFailurePercent}% · 随机运气 {offer.LuckAdjustment:+0;-0;0}%",20,94,740,32,21,muted);
                Label(row,$"最终失败率 {offer.FailurePercent}%（最高 30%）",20,134,740,38,26,accent);
                Label(row,$"{offer.Fee:N0} G",800,28,280,42,30,accent);
                string fatherId=father.id;
                Btn(row,"选择配种",800,96,280,48,()=>Confirm(
                    $"支付 {offer.Fee:N0} G，使用「{m.name}」配种？\n随机失败率 {offer.FailurePercent}%；失败不退费。\n成功获得 1 枚卵；雄虫下周可再次配种。",
                    ()=>app.Execute(s=>s.HireBreeding(fatherId,offer.Index,offer.Week))),ready&&State.coins>=offer.Fee);
            }
            if(!ready)Label(root,"需要健康、饱食且未占用的雄虫，并预留 1 个温室栏位。",440,813,1100,30,19,muted);
        }
    }
}
