using System;
using System.Collections.Generic;
using System.Linq;

namespace BugDuel.Campaign
{
    public enum CampaignLifeStage { Egg, Larva1, Larva2, Larva3, Prepupa, Pupa, DormantAdult, ActiveAdult }

    [Serializable] public sealed class CampaignClock
    {
        public double elapsedDays, realSecondsPerDay=600, speed=1;
        public bool paused;
        public int Year => (int)Math.Floor(elapsedDays/360)+1;
        public int Month => (int)Math.Floor(elapsedDays/30)%12+1;
        public int Day => (int)Math.Floor(elapsedDays)%30+1;
        public int Hour => (int)Math.Floor((elapsedDays-Math.Floor(elapsedDays))*24+1e-8)%24;
        public int Minute => (int)Math.Floor((elapsedDays-Math.Floor(elapsedDays))*1440+1e-7)%60;
    }

    [Serializable] public sealed class CampaignLifeRules
    {
        public double eggDays=15, larva1Days=10, larva2Days=18, larva3Days=210, prepupaDays=8, pupaDays=18, dormantDays=21, adultDays=180;
        public CampaignLifeRules Copy() => (CampaignLifeRules)MemberwiseClone();
        public double Duration(CampaignLifeStage stage)
        {
            switch(stage)
            {
                case CampaignLifeStage.Egg:return eggDays;
                case CampaignLifeStage.Larva1:return larva1Days;
                case CampaignLifeStage.Larva2:return larva2Days;
                case CampaignLifeStage.Larva3:return larva3Days;
                case CampaignLifeStage.Prepupa:return prepupaDays;
                case CampaignLifeStage.Pupa:return pupaDays;
                case CampaignLifeStage.DormantAdult:return dormantDays;
                default:return adultDays;
            }
        }
    }

    public static class CampaignRearingItems
    {
        public const string EggMedium="rearing-egg-medium", Humus="rearing-humus", FermentedWood="rearing-wood",
            FungusBottle="rearing-fungus", PupaCell="rearing-pupa-cell", AdultJelly="rearing-adult-jelly";
        public static readonly IReadOnlyList<string> All=Array.AsReadOnly(new[]{EggMedium,Humus,FermentedWood,FungusBottle,PupaCell,AdultJelly});
        public static string Name(string id)
        {
            switch(id)
            {
                case EggMedium:return "卵期孵化基质";case Humus:return "L1 细筛腐殖土";case FermentedWood:return "发酵木屑";
                case FungusBottle:return "幼虫营养菌瓶";case PupaCell:return "人工蛹室";case AdultJelly:return "成虫甲虫果冻";
                default:throw new CampaignRuleException("养殖用品不存在");
            }
        }
        public static long Price(string id)
        {
            switch(id) {case EggMedium:return 40;case Humus:return 50;case FermentedWood:return 90;case FungusBottle:return 180;case PupaCell:return 120;case AdultJelly:return 30;default:throw new CampaignRuleException("养殖用品不存在");}
        }
        public static bool Applicable(string id,CampaignLifeStage stage)
        {
            switch(id)
            {
                case EggMedium:return stage==CampaignLifeStage.Egg;
                case Humus:return stage==CampaignLifeStage.Larva1;
                case FermentedWood:case FungusBottle:return stage==CampaignLifeStage.Larva2||stage==CampaignLifeStage.Larva3;
                case PupaCell:return stage==CampaignLifeStage.Prepupa||stage==CampaignLifeStage.Pupa||stage==CampaignLifeStage.DormantAdult;
                case AdultJelly:return stage==CampaignLifeStage.ActiveAdult;
                default:return false;
            }
        }
        public static bool Applicable(string id,CampaignBeetle beetle) => !beetle.dead&&Applicable(id,beetle.stage);
        public static string Description(string id)
        {
            switch(id)
            {
                case EggMedium:return "卵期使用：基质营养 +35，洁净度恢复至100。";
                case Humus:return "L1 使用：基质营养 +45，洁净度恢复至100。";
                case FermentedWood:return "L2 / L3 使用：基质营养 +60，洁净度恢复至100。";
                case FungusBottle:return "L2 / L3 使用：营养与洁净度恢复至100，羽化体长额外 +5%（不叠加）。";
                case PupaCell:return "预蛹、蛹、蛰伏期使用：保护30天，洁净度恢复至100，健康 +0.25。";
                case AdultJelly:return "活动成虫使用：饱食 +40，健康 +0.5。蛰伏成虫尚不进食。";
                default:throw new CampaignRuleException("养殖用品不存在");
            }
        }
    }

    public sealed partial class CampaignSession
    {
        public static string StageName(CampaignLifeStage stage)
        {
            switch(stage)
            {
                case CampaignLifeStage.Egg:return "卵";case CampaignLifeStage.Larva1:return "L1 一龄幼虫";case CampaignLifeStage.Larva2:return "L2 二龄幼虫";
                case CampaignLifeStage.Larva3:return "L3 三龄幼虫";case CampaignLifeStage.Prepupa:return "预蛹";case CampaignLifeStage.Pupa:return "蛹";
                case CampaignLifeStage.DormantAdult:return "成虫蛰伏期";default:return "成虫活动期";
            }
        }
        public static string StageDescription(CampaignLifeStage stage)
        {
            switch(stage)
            {
                case CampaignLifeStage.Egg:return "保持孵化基质洁净，等待自然孵化。";
                case CampaignLifeStage.Larva1:return "细筛腐殖土适合刚孵化的幼虫，避免频繁翻动。";
                case CampaignLifeStage.Larva2:return "逐渐增加食量，可更换发酵木屑或营养菌瓶。";
                case CampaignLifeStage.Larva3:return "主要生长期，基质营养影响羽化体长。";
                case CampaignLifeStage.Prepupa:return "停止进食、准备化蛹；保持安静，可配置人工蛹室。";
                case CampaignLifeStage.Pupa:return "蛹室内完成变态，不需要投喂成虫食物。";
                case CampaignLifeStage.DormantAdult:return "已羽化但尚未进食，等待体表硬化与自然苏醒。";
                default:return "活动进食期，可参赛与繁殖；默认寿命180天，不含此前蛰伏期。";
            }
        }
        static bool Larval(CampaignLifeStage stage) => stage>=CampaignLifeStage.Larva1&&stage<=CampaignLifeStage.Larva3;
        double Duration(CampaignBeetle b,CampaignLifeStage stage)
        {
            double scale=stage==CampaignLifeStage.ActiveAdult?1:b.developmentScale;
            if(stage==CampaignLifeStage.Egg||Larval(stage))scale*=b.climate==0?.85:b.climate==2?1.15:1;
            return State.lifeRules.Duration(stage)*scale;
        }
        public double StageDuration(CampaignBeetle b) => Duration(b,b.stage);
        public double StageProgress(CampaignBeetle b) => Math.Max(0,Math.Min(1,b.stageAgeDays/StageDuration(b)));
        public double RemainingLifeDays(CampaignBeetle b)
        {
            if(b.dead)return 0;
            double days=Math.Max(0,StageDuration(b)-b.stageAgeDays);
            for(int stage=(int)b.stage+1;stage<=(int)CampaignLifeStage.ActiveAdult;stage++)days+=Duration(b,(CampaignLifeStage)stage);
            return days;
        }
        public double RemainingDevelopmentDays(CampaignBeetle b) => b.stage==CampaignLifeStage.ActiveAdult?0:Math.Max(0,RemainingLifeDays(b)-State.lifeRules.adultDays);

        IEnumerable<CampaignBeetle> LifecycleBeetles() => State.beetles.Concat(State.storedBeetles).Concat(State.stock).Concat(MailBeetles()).Concat(TradingBeetles())
            .Concat(State.battle==null?Array.Empty<CampaignBeetle>():new[]{State.battle.opponent})
            .Concat(State.pupaAuction==null?Array.Empty<CampaignBeetle>():new[]{State.pupaAuction.beetle});
        void UpgradeCalendar()
        {
            if(State.calendarVersion!=0)return;
            State.clock=new CampaignClock {elapsedDays=(State.week-1)*7d};State.lifeRules=new CampaignLifeRules();State.calendarVersion=1;
        }
        void UpgradeLifeHistory()
        {
            foreach(var b in LifecycleBeetles())
            {
                if(b.lifeVersion!=0)continue;
                b.lifeVersion=1;b.stageAgeDays=b.totalAgeDays=b.adultAgeDays=0;b.developmentScale=1;
                b.substrateNutrition=85;b.substrateCleanliness=100;b.pupalProtectionDays=0;
                if(b.dead||b.readyWeek<=State.week)b.stage=CampaignLifeStage.ActiveAdult;
                else
                {
                    b.stage=CampaignLifeStage.Larva3;
                    if(b.readyWeek<int.MaxValue)
                    {
                        double remaining=(b.readyWeek-State.week)*7d;
                        double normal=Duration(b,CampaignLifeStage.Larva3)+Duration(b,CampaignLifeStage.Prepupa)+Duration(b,CampaignLifeStage.Pupa)+Duration(b,CampaignLifeStage.DormantAdult);
                        b.developmentScale=remaining/normal;
                    }
                }
            }
        }
        public void ConfigureClock(double secondsPerDay,double speed,bool paused)
        {
            Require(Finite(secondsPerDay,10,86400)&&Finite(speed,.1,1000),"每天真实时长需在10至86400秒之间，速度需在0.1至1000倍之间");
            State.clock.realSecondsPerDay=secondsPerDay;State.clock.speed=speed;State.clock.paused=paused;Tell(paused?"自然时钟已暂停。":"自然时钟设置已保存。");
        }
        static bool ValidLifeRules(CampaignLifeRules rules) => rules!=null&&Enum.GetValues(typeof(CampaignLifeStage)).Cast<CampaignLifeStage>().All(stage=>Finite(rules.Duration(stage),.25,3650));
        public void ConfigureLifeRules(CampaignLifeRules rules)
        {
            Require(ValidLifeRules(rules),"各阶段时长需在0.25至3650天之间");
            var progress=LifecycleBeetles().Where(b=>!b.dead).Select(b=>(beetle:b,progress:StageProgress(b))).ToArray();State.lifeRules=rules.Copy();
            foreach(var item in progress)
            {
                item.beetle.stageAgeDays=item.progress*StageDuration(item.beetle);
                if(item.beetle.stage==CampaignLifeStage.ActiveAdult)item.beetle.adultAgeDays=item.beetle.stageAgeDays;
                UpdateReadyWeek(item.beetle);
            }
            Tell("生命周期已调整，保留当前阶段完成比例；活动成虫寿命与蛰伏期分别计时。");
        }
        public void AdvanceRealSeconds(double seconds)
        {
            Require(Finite(seconds,0,86400),"推进时间无效");
            if(State.clock.paused||State.battle!=null||State.auction!=null||State.pupaAuction!=null||seconds==0)return;
            AdvanceDays(seconds*State.clock.speed/State.clock.realSecondsPerDay);
        }
        public void AdvanceDays(double days)
        {
            Require(Finite(days,0,36500),"每次推进天数需在0至36500之间");
            Require(State.battle==null&&State.auction==null&&State.pupaAuction==null,"请先完成当前赛事或拍卖");
            double target=State.clock.elapsedDays+days;Require(target<6999993,"日历已达到存档支持的年份上限");
            while(State.clock.elapsedDays<target-1e-10)
            {
                double nextDay=Math.Floor(State.clock.elapsedDays+1e-10)+1;
                double nextWeek=State.week*7d;
                double end=Math.Min(target,Math.Min(nextDay,nextWeek)),step=end-State.clock.elapsedDays;
                foreach(var b in State.beetles.Where(x=>!x.dead).ToArray())AdvanceIndividual(b,step,b.listed);
                foreach(var b in State.storedBeetles.Where(x=>!x.dead).ToArray())AdvanceIndividual(b,step,true);
                State.clock.elapsedDays=end;
                if(end>=nextDay-1e-10)
                    foreach(var b in State.beetles.Where(x=>x.Adult(State.week)&&!x.listed&&!x.overturned&&x.meal!=null).ToArray())CompleteMeal(b.id,b.meal.id);
                if(end>=nextWeek-1e-10)AdvanceEconomicWeek();
            }
        }
        void AdvanceIndividual(CampaignBeetle b,double days,bool boarded)
        {
            while(days>1e-10&&!b.dead)
            {
                double remaining=Math.Max(0,StageDuration(b)-b.stageAgeDays),step=Math.Min(days,remaining);
                if(step>0)
                {
                    ApplyDailyCare(b,step,boarded);b.stageAgeDays+=step;b.totalAgeDays+=step;
                    if(b.stage==CampaignLifeStage.ActiveAdult)b.adultAgeDays+=step;
                    b.pupalProtectionDays=Math.Max(0,b.pupalProtectionDays-step);days-=step;
                }
                if(b.dead)break;
                if(b.stageAgeDays>=StageDuration(b)-1e-9)TransitionLifeStage(b);
            }
        }
        void ApplyDailyCare(CampaignBeetle b,double days,bool boarded)
        {
            if(b.stage==CampaignLifeStage.ActiveAdult)
            {
                double before=b.nutrition,loss=boarded?0:20d/7;
                b.nutrition=Math.Max(0,b.nutrition-loss*days);
                if(boarded)b.nutrition=Math.Max(55,b.nutrition);
                double healthyDays=before>=40?(loss==0?days:Math.Min(days,(before-40)/loss)):0;
                if(boarded)healthyDays=days;
                b.hearts=Math.Max(0,Math.Min(b.maxHearts,b.hearts+healthyDays*Catalog.Housing(b.habitat).healthRecovery/7-(days-healthyDays)*.75/7));
            }
            else
            {
                double nutritionLoss=b.stage==CampaignLifeStage.Egg?.05:Larval(b.stage)?.3:0;
                double dirt=b.stage==CampaignLifeStage.Egg?.03:Larval(b.stage)?.18:.04;
                b.substrateNutrition=Math.Max(0,b.substrateNutrition-nutritionLoss*days);b.substrateCleanliness=Math.Max(0,b.substrateCleanliness-dirt*days);
                if(boarded){b.substrateNutrition=Math.Max(40,b.substrateNutrition);b.substrateCleanliness=Math.Max(50,b.substrateCleanliness);}
                if(Larval(b.stage))b.nutrition=b.substrateNutrition;
                bool fragile=b.stage==CampaignLifeStage.Prepupa||b.stage==CampaignLifeStage.Pupa||b.stage==CampaignLifeStage.DormantAdult;
                double unprotected=fragile?Math.Max(0,days-b.pupalProtectionDays):days;
                double loss=(b.substrateCleanliness<20?unprotected*.025:0)+(Larval(b.stage)&&b.substrateNutrition<10?days*.015:0);
                b.hearts=Math.Max(0,Math.Min(b.maxHearts,b.hearts-loss+(boarded?.01*days:0)));
            }
            if(b.hearts<=0)LifeDeath(b,"因长期缺乏照料而死亡");
        }
        void TransitionLifeStage(CampaignBeetle b)
        {
            if(b.stage==CampaignLifeStage.ActiveAdult){LifeDeath(b,"走完了活动成虫的一生");return;}
            b.stage=(CampaignLifeStage)((int)b.stage+1);b.stageAgeDays=0;
            if(b.stage==CampaignLifeStage.DormantAdult){FinalizeGrowth(b);b.nutrition=85;}
            if(b.stage==CampaignLifeStage.ActiveAdult){b.adultAgeDays=0;b.developmentScale=1;b.pupalProtectionDays=0;b.nutrition=Math.Max(85,b.nutrition);}
            SetAutomaticStageName(b);UpdateReadyWeek(b);
            Tell(CampaignDisplayText.Name(b)+"进入"+StageName(b.stage)+"。"+(b.stage==CampaignLifeStage.ActiveAdult?"开始进食，活动寿命从今天计起。":StageDescription(b.stage)));
        }
        void FinalizeGrowth(CampaignBeetle b)
        {
            var feed=Catalog.feeds[Math.Max(0,b.feed)];double quality=.9+.1*b.substrateNutrition/100;
            b.length=Catalog.Species(b.speciesId).length*b.inheritance*feed.multiplier*(b.climate==0?.88:b.climate==2?1.22:1)*CampaignGenetics.LengthBonus(b.genes)*quality*(1+b.growthQualityBonus);
            if(b.feed==0&&State.feedRecipe)b.length*=1.05;
            if(State.mutationRecipe&&string.IsNullOrEmpty(b.mutation))b.mutation=CampaignGenetics.Combo(b.genes);
            if(string.IsNullOrEmpty(b.mutation)&&Roll()<feed.mutationChance)b.mutation=Roll()<.5?"白眼":"三叉角";
            b.initialValue=b.value=CampaignEconomy.Money(CampaignEconomy.Initial(b,Catalog.Species(b.speciesId))*Math.Pow(2,b.broodValueAwards));State.recordLength=Math.Max(State.recordLength,b.length);
        }
        void LifeDeath(CampaignBeetle b,string reason)
        {
            b.dead=true;b.hearts=0;b.listed=false;b.overturned=false;b.meal=null;b.hasMeal=false;
            State.offers.RemoveAll(o=>o.beetleId==b.id);Tell(CampaignDisplayText.Name(b)+reason+"。可以制作标本或遗弃遗体。");
        }
        void SetAutomaticStageName(CampaignBeetle b)
        {
            if(b.customName)return;
            b.name=Catalog.Species(b.speciesId).name+(b.stage==CampaignLifeStage.ActiveAdult||b.stage==CampaignLifeStage.DormantAdult?"":StageName(b.stage));
        }
        void UpdateReadyWeek(CampaignBeetle b) => b.readyWeek=b.stage==CampaignLifeStage.ActiveAdult?State.week:State.week+(int)Math.Ceiling(RemainingDevelopmentDays(b)/7);
        void SetInitialStage(CampaignBeetle b,CampaignLifeStage stage)
        {
            b.lifeVersion=1;b.stage=stage;b.stageAgeDays=b.adultAgeDays=b.totalAgeDays=0;b.developmentScale=1;b.feed=stage<=CampaignLifeStage.Larva3?-1:0;
            SetAutomaticStageName(b);UpdateReadyWeek(b);
        }
        public void UseRearingItem(string id,string itemId)
        {
            var b=State.beetles.Concat(State.storedBeetles).FirstOrDefault(x=>x.id==id);Require(b!=null,"没有这只自有甲虫");Available(b);
            Require(CampaignRearingItems.Applicable(itemId,b.stage),"这件用品不适合当前阶段");ConsumeItem(itemId);
            switch(itemId)
            {
                case CampaignRearingItems.EggMedium:b.substrateNutrition=Math.Min(100,b.substrateNutrition+35);b.substrateCleanliness=100;break;
                case CampaignRearingItems.Humus:b.substrateNutrition=Math.Min(100,b.substrateNutrition+45);b.substrateCleanliness=100;break;
                case CampaignRearingItems.FermentedWood:b.substrateNutrition=Math.Min(100,b.substrateNutrition+60);b.substrateCleanliness=100;break;
                case CampaignRearingItems.FungusBottle:b.substrateNutrition=b.substrateCleanliness=100;b.growthQualityBonus=.05;break;
                case CampaignRearingItems.PupaCell:b.pupalProtectionDays=30;b.substrateCleanliness=100;b.hearts=Math.Min(b.maxHearts,b.hearts+.25);break;
                case CampaignRearingItems.AdultJelly:b.nutrition=Math.Min(100,b.nutrition+40);b.hearts=Math.Min(b.maxHearts,b.hearts+.5);break;
            }
            if(Larval(b.stage))b.nutrition=b.substrateNutrition;
            Tell("已为"+CampaignDisplayText.Name(b)+"使用"+CampaignRearingItems.Name(itemId)+"。");
        }
        double StageMarketFactor(CampaignBeetle b)
        {
            switch(b.stage)
            {
                case CampaignLifeStage.Egg:return .15;case CampaignLifeStage.Larva1:return .22;case CampaignLifeStage.Larva2:return .32;
                case CampaignLifeStage.Larva3:return .5;case CampaignLifeStage.Prepupa:return .65;case CampaignLifeStage.Pupa:return .75;
                case CampaignLifeStage.DormantAdult:return .9;default:return .35+.65*Math.Max(0,1-b.adultAgeDays/State.lifeRules.adultDays);
            }
        }
        void ValidateLifecycle()
        {
            RequireData(State.calendarVersion==1&&State.clock!=null&&ValidLifeRules(State.lifeRules),"日历或生命周期配置无效");
            RequireData(Finite(State.clock.elapsedDays,0,6999992.999999)&&Finite(State.clock.realSecondsPerDay,10,86400)&&Finite(State.clock.speed,.1,1000)&&State.week==(int)Math.Floor((State.clock.elapsedDays+1e-9)/7)+1,"自然时钟与经济周不一致");
            foreach(var b in LifecycleBeetles())
            {
                RequireData(b.broodValueAwards>=0,"同腹估值奖励次数无效");
                RequireData(b.lifeVersion==1&&Enum.IsDefined(typeof(CampaignLifeStage),b.stage)&&Finite(b.developmentScale,.00001,100000)&&Finite(b.stageAgeDays,0,b.dead?10000000:StageDuration(b)+1e-7)&&Finite(b.totalAgeDays,0,10000000)&&Finite(b.adultAgeDays,0,b.dead?10000000:State.lifeRules.adultDays+1e-7),"甲虫阶段或年龄无效");
                RequireData(Finite(b.substrateNutrition,0,100)&&Finite(b.substrateCleanliness,0,100)&&Finite(b.pupalProtectionDays,0,30)&&Finite(b.growthQualityBonus,0,.05),"基质或蛹室数据无效");
                RequireData(b.stage==CampaignLifeStage.ActiveAdult?Math.Abs(b.adultAgeDays-b.stageAgeDays)<1e-7:b.adultAgeDays==0,"活动成虫日龄无效");
            }
        }
    }
}
