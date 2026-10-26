using System;

namespace BugDuel.Campaign
{
    public sealed class CampaignBreedingOffer
    {
        public int Index { get; internal set; }
        public int Week { get; internal set; }
        public long Fee { get; internal set; }
        public int BaseFailurePercent { get; internal set; }
        public int LuckAdjustment { get; internal set; }
        public int FailurePercent => Math.Max(2,Math.Min(30,BaseFailurePercent+LuckAdjustment));
        public CampaignBeetle Mother { get; internal set; }
    }

    public sealed partial class CampaignSession
    {
        public const int BreedingOfferCount=6;

        // A quote is stable for this father and week. Opening the page cannot reroll luck
        // or consume the simulation RNG, and center mothers never enter player ownership.
        public CampaignBreedingOffer BreedingOffer(string fatherId,int index)
        {
            Require(index>=0&&index<BreedingOfferCount,"母虫服务不存在");
            var father=Beetle(fatherId);var species=Catalog.Species(father.speciesId);
            uint seed=2166136261;
            foreach(char c in father.id+":"+State.week+":"+index)seed=unchecked((seed^c)*16777619);
            uint motherSeed=2166136261;
            foreach(char c in species.id+":"+State.week+":"+index)motherSeed=unchecked((motherSeed^c)*16777619);
            int tier=index/2;
            var mother=new CampaignBeetle {
                id="center:"+species.id+":"+State.week+":"+index,speciesId=species.id,
                name="中心母虫 "+(index+1),female=true,habitat=Catalog.housing[0].id,
                stage=CampaignLifeStage.ActiveAdult,lifeVersion=1,
                length=Math.Round(species.length*(.95+tier*.06+(motherSeed%11)*.005),1)
            };
            mother.genes.Add(new Gene(CampaignGenetics.GeneIds[(int)(motherSeed%5)],tier+1));
            mother.initialValue=mother.value=CampaignEconomy.Initial(mother,species);
            return new CampaignBreedingOffer {Index=index,Week=State.week,Mother=mother,
                Fee=Math.Max(100,species.price/4)*(tier+1),BaseFailurePercent=26-tier*8,
                LuckAdjustment=(int)((seed>>8)%17)-8};
        }

        public bool HireBreeding(string fatherId,int index,int quotedWeek)
        {
            var father=Beetle(fatherId);HealthyAdult(father);
            Require(!father.female,"请选择雄虫");
            Require(father.cooldownWeek<=State.week,"亲本需要休息到下一周");
            Require(State.week==quotedWeek,"母虫服务已刷新，请重新选择");
            Space(1);var offer=BreedingOffer(fatherId,index);Spend(offer.Fee);
            father.cooldownWeek=State.week+1;father.nutrition-=15;
            if(Roll()*100<offer.FailurePercent)
            {
                Tell("本次配种失败，费用不退还。雄虫下周可再次配种。");return false;
            }
            Add(CreateOffspring(father,offer.Mother,false,0));
            Tell("配种成功，获得 1 枚卵。");return true;
        }
    }
}
