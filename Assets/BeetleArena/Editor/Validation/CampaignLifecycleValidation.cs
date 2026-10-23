using System;
using System.Collections.Generic;
using System.Linq;
using BugDuel.Campaign;
#if UNITY_EDITOR
using System.IO;
using BeetleArena.Campaign;
using UnityEditor;
using UnityEngine;
#endif

public static class CampaignLifecycleValidation
{
    static readonly List<string> checks=new List<string>();
    static void Check(bool valid,string message) {if(!valid)throw new InvalidOperationException("Lifecycle contract failed: "+message);checks.Add("PASS "+message);}
    static bool Near(double a,double b) => Math.Abs(a-b)<1e-6;
    static void Rejected(Action action,string message) {bool refused=false;try{action();}catch(CampaignRuleException){refused=true;}Check(refused,message);}
    static CampaignSession Session() {var s=CampaignSession.New(CampaignCatalog.Defaults(),58217);s.State.coins=100000;return s;}
    static CampaignLifeRules FastRules(double adult=180) => new CampaignLifeRules {eggDays=1,larva1Days=1,larva2Days=1,larva3Days=1,prepupaDays=1,pupaDays=1,dormantDays=1,adultDays=adult};
    static CampaignBeetle Egg(CampaignSession s) {s.Breed(s.State.beetles[0].id,s.State.beetles[1].id);return s.State.beetles.Last();}
    static void Board(CampaignSession s,CampaignBeetle b) {s.State.beetles.Remove(b);s.State.storedBeetles.Add(b);if(s.State.selectedId==b.id)s.State.selectedId=s.State.beetles.FirstOrDefault()?.id;}
    static void Supply(CampaignSession s,string id,int count=1) {s.BuyItem(id,count);s.ClaimMail(s.State.mail.Last().id);}
    static void WinSeason(CampaignSession s,CampaignBeetle b)
    {
        for(int i=0;i<s.Catalog.seasonWins;i++)
        {b.nutrition=100;b.hearts=b.maxHearts;s.EnterLeague(b.id);s.SettleBattle(s.State.battle.id,true);}
    }
    static CampaignSession ChampionFamily(out string youngerId,bool stored=false)
    {
        var s=Session();var rules=FastRules();rules.pupaDays=2;s.ConfigureLifeRules(rules);
        var older=Egg(s);s.NextWeek();var younger=Egg(s);youngerId=younger.id;s.AdvanceDays(1);
        if(stored)Board(s,younger);
        long initial=younger.initialValue;WinSeason(s,older);
        Check(younger.stage==CampaignLifeStage.Larva1&&younger.broodValueAwards==1&&younger.initialValue==initial*2,stored?"a warehouse juvenile sibling receives the same first-championship award as a greenhouse sibling":"a juvenile sibling records the championship award and immediately receives its existing double valuation");
        s.Validate();return s;
    }
    static void CheckGrownSibling(CampaignSession s,string id)
    {
        s.AdvanceDays(7);var b=s.State.beetles.Concat(s.State.storedBeetles).First(x=>x.id==id);s.Validate();
        Check(b.Adult(s.State.week)&&b.broodValueAwards==1&&b.initialValue==CampaignEconomy.Initial(b,s.Catalog.Species(b.speciesId))*2,"eclosion retains the earned sibling multiplier on the final adult valuation");
    }

    public static IReadOnlyList<string> RunDomain()
    {
        checks.Clear();var s=Session();s.Validate();var adult=s.State.beetles[0];
        Check(s.State.calendarVersion==1&&s.State.clock.Year==1&&s.State.clock.Month==1&&s.State.clock.Day==1&&adult.Adult(s.State.week)&&adult.adultAgeDays==0,"new campaigns start a natural calendar and newly acquired adults have their full active lifespan");
        var sample=new CampaignClock {elapsedDays=393.5};
        Check(sample.Year==2&&sample.Month==2&&sample.Day==4&&sample.Hour==12&&sample.Minute==0,"calendar has 30-day months, 12-month years and fractional-day time");
        s.ConfigureClock(600,2,false);string notice=s.State.notice;s.AdvanceRealSeconds(30);
        Check(Near(s.State.clock.elapsedDays,.1)&&Near(adult.adultAgeDays,.1)&&s.State.notice==notice,"real seconds advance calendar and age at configured speed without fractional-step notices");
        s.ConfigureClock(600,2,true);double frozen=s.State.clock.elapsedDays;s.AdvanceRealSeconds(300);
        Check(s.State.clock.elapsedDays==frozen&&Near(adult.adultAgeDays,frozen),"pausing the clock freezes real-time growth and age");
        Rejected(()=>s.ConfigureClock(0,1,false),"invalid clock settings are rejected");
        Rejected(()=>s.ConfigureLifeRules(new CampaignLifeRules {adultDays=0}),"zero-length lifecycle stages are rejected");

        s=Session();var child=Egg(s);s.RenameBeetle(child.id,"漫长的一生");var parents=s.State.beetles.Where(b=>b.id!=child.id).ToArray();foreach(var parent in parents)Board(s,parent);
        Check(child.stage==CampaignLifeStage.Egg&&!child.Adult(s.State.week)&&Near(s.RemainingDevelopmentDays(child),300),"breeding creates an egg with the full 300-day default path to active adulthood");
        var stages=new[]{CampaignLifeStage.Egg,CampaignLifeStage.Larva1,CampaignLifeStage.Larva2,CampaignLifeStage.Larva3,CampaignLifeStage.Prepupa,CampaignLifeStage.Pupa,CampaignLifeStage.DormantAdult};
        foreach(var stage in stages)
        {
            Check(child.stage==stage&&!child.Adult(s.State.week),"phase is explicit and cannot fight or breed early: "+stage);
            double duration=s.StageDuration(child);s.AdvanceDays(duration);s.Validate();
        }
        Check(child.stage==CampaignLifeStage.ActiveAdult&&child.Adult(s.State.week)&&child.adultAgeDays==0&&Near(child.totalAgeDays,300)&&child.name=="漫长的一生","eclosion and dormancy finish before the active age starts, preserving custom names");
        Board(s,child);s.AdvanceDays(179.999);
        Check(!child.dead&&Near(child.adultAgeDays,179.999),"boarded adults remain alive just before six full active months");
        s.AdvanceDays(.001);s.Validate();
        Check(child.dead&&Near(child.adultAgeDays,180)&&Near(child.totalAgeDays,480),"active adulthood ends at 180 days, independently of the prior dormant stage");
        string memorial=s.PreserveDead(child.id,"第2年4月");double recorded=s.Specimen(memorial).beetle.adultAgeDays;
        s.ConfigureLifeRules(FastRules(1));s.Validate();
        Check(s.Specimen(memorial).beetle.adultAgeDays==recorded&&Near(recorded,180),"changing future lifecycle rules never rewrites a dead specimen's historical age");

        s=Session();s.ConfigureLifeRules(FastRules(3.5));var listed=s.State.beetles[0];s.List(listed.id);var stored=s.State.beetles[1];Board(s,stored);
        s.AdvanceDays(3.49);Check(!listed.dead&&!stored.dead&&listed.nutrition>=55&&stored.nutrition>=55,"market and warehouse boarding provide basic care while age keeps advancing");
        s.AdvanceDays(.01);s.Validate();
        Check(listed.dead&&!listed.listed&&!s.State.offers.Any(o=>o.beetleId==listed.id)&&stored.dead,"listing and storage cannot freeze lifespan; death removes obsolete offers");
        s.ClearDead(stored.id);Check(!s.State.storedBeetles.Contains(stored),"dead warehouse insects can be discarded");

        s=Session();child=Egg(s);s.AdvanceDays(5);double progress=s.StageProgress(child),age=child.totalAgeDays;var modified=s.State.lifeRules.Copy();modified.eggDays=30;s.ConfigureLifeRules(modified);
        Check(Near(s.StageProgress(child),progress)&&Near(child.stageAgeDays,10)&&Near(child.totalAgeDays,age),"life rule edits preserve stage completion ratio without rewriting recorded elapsed age");
        modified.eggDays=300;Check(s.State.lifeRules.eggDays==30,"life rules are copied instead of retaining the caller's mutable object");
        var snapshot=CampaignSnapshots.Copy(s.State);snapshot.clock.elapsedDays+=1;snapshot.lifeRules.eggDays=100;snapshot.beetles.Last().stageAgeDays=0;
        Check(Near(s.State.clock.elapsedDays,5)&&s.State.lifeRules.eggDays==30&&Near(child.stageAgeDays,10),"transaction snapshots isolate clock, lifecycle configuration and individual phase progress");
        s=Session();child=Egg(s);s.Grow(child.id,0,0);Check(Near(s.StageDuration(child),12.75),"warm incubation shortens the egg stage as well as larval development");

        s=Session();var split=new CampaignSession(CampaignSnapshots.Copy(s.State),s.Catalog);s.AdvanceDays(7);for(int i=0;i<28;i++)split.AdvanceDays(.25);
        Check(Near(s.State.clock.elapsedDays,7)&&s.State.week==2&&Near(s.State.beetles[0].adultAgeDays,7)&&Near(s.State.beetles[0].nutrition,65)&&Near(split.State.beetles[0].nutrition,s.State.beetles[0].nutrition)&&Near(split.State.beetles[0].hearts,s.State.beetles[0].hearts),"fractional and whole-day advancement agree without duplicating weekly nutrition or age");
        var skipped=Session();skipped.NextWeek();Check(Near(skipped.State.clock.elapsedDays,7)&&skipped.State.week==2&&Near(skipped.State.beetles[0].adultAgeDays,7),"legacy NextWeek delegates to one seven-day advancement");
        skipped.State.beetles[0].overturned=true;Rejected(()=>skipped.NextWeek(),"manual weekly skipping retains its rescue-first safety gate");
        s=Session();s.Dispatch("oak",false,false);s.AdvanceDays(6.99);Check(s.State.expeditions.Count==1&&s.Reserved==2,"expeditions remain pending before the economic-week boundary");
        s.AdvanceDays(.01);s.Validate();Check(s.State.expeditions.Count==0&&s.State.beetles.Count==4&&s.State.marketWeek==2,"natural time resolves expeditions and refreshes market exactly at seven days");
        s=Session();var auction=s.State.marketAuctions[0];s.BidMarketAuction(auction.id,auction.expectedPrice);long coins=s.State.coins;
        s.State.hall.Add(new HallRecord {name="祖辈",speciesId="red",length=48,price=200,week=1});s.AdvanceDays(21);s.Validate();
        Check(s.State.week==4&&s.State.hall[0].royalties==60&&s.State.coins==coins+60&&s.State.mail.Any(m=>m.senderId=="auction"&&m.attachments.Count>0)&&s.State.marketAuctions.Count==3,"multi-week natural advancement settles escrow, royalties and new auctions once per boundary");
        s=Session();var frozenStock=s.State.stock.First(b=>b.stage!=CampaignLifeStage.ActiveAdult);s.Buy(frozenStock.id);var pending=s.State.mail.Last();s.AdvanceDays(20);
        Check(pending.attachments[0].beetles[0].stageAgeDays==0,"unclaimed shop deliveries use explicit supplier boarding rather than secretly aging in mail");
        s.ClaimMail(pending.id);s.AdvanceDays(1);Check(frozenStock.stageAgeDays>0||frozenStock.stage!=CampaignLifeStage.Egg,"claimed warehouse ownership immediately begins natural progression");

        foreach(string item in CampaignRearingItems.All)
        {
            s=Session();s.ConfigureLifeRules(FastRules());child=Egg(s);
            CampaignLifeStage stage=item==CampaignRearingItems.EggMedium?CampaignLifeStage.Egg:item==CampaignRearingItems.Humus?CampaignLifeStage.Larva1:item==CampaignRearingItems.PupaCell?CampaignLifeStage.Pupa:item==CampaignRearingItems.AdultJelly?CampaignLifeStage.ActiveAdult:CampaignLifeStage.Larva2;
            s.AdvanceDays((int)stage);child.substrateNutrition=20;child.substrateCleanliness=10;child.nutrition=20;child.hearts=2;
            Supply(s,item);coins=s.State.coins;s.UseRearingItem(child.id,item);s.Validate();
            bool effect=item==CampaignRearingItems.AdultJelly?child.nutrition==60&&Near(child.hearts,2.5):item==CampaignRearingItems.PupaCell?child.pupalProtectionDays==30&&Near(child.hearts,2.25):child.substrateNutrition>20&&child.substrateCleanliness==100;
            Check(s.InventoryCount(item)==0&&s.State.coins==coins&&effect,"stage-specific supply consumes real inventory and applies its documented effect: "+item);
        }
        s=Session();s.ConfigureLifeRules(FastRules());child=Egg(s);Supply(s,CampaignRearingItems.AdultJelly);
        Rejected(()=>s.UseRearingItem(child.id,CampaignRearingItems.AdultJelly),"adult jelly cannot be fed to an egg");Check(s.InventoryCount(CampaignRearingItems.AdultJelly)==1,"wrong-stage use leaves inventory intact");
        s.AdvanceDays(2);Supply(s,CampaignRearingItems.FungusBottle,2);s.UseRearingItem(child.id,CampaignRearingItems.FungusBottle);s.UseRearingItem(child.id,CampaignRearingItems.FungusBottle);
        Check(Near(child.growthQualityBonus,.05)&&s.InventoryCount(CampaignRearingItems.FungusBottle)==0,"fungus nutrition bonus is capped instead of stacking repeated size multipliers");
        s.AdvanceDays(3);Supply(s,CampaignRearingItems.PupaCell);s.UseRearingItem(child.id,CampaignRearingItems.PupaCell);s.AdvanceDays(1);
        Check(child.stage==CampaignLifeStage.DormantAdult&&Near(child.pupalProtectionDays,29)&&child.adultAgeDays==0,"pupal protection decays in real calendar days through adult dormancy");

        var legacy=Session();var legacyAdult=legacy.State.beetles[0];var legacyChild=legacy.Create("red",45);legacy.Add(legacyChild);
        legacy.State.week=40;legacy.State.calendarVersion=0;legacy.State.clock=null;legacy.State.lifeRules=null;
        legacyAdult.lifeVersion=0;legacyAdult.readyWeek=1;legacyChild.lifeVersion=0;legacyChild.readyWeek=42;legacyChild.feed=1;legacyChild.climate=2;
        var migrated=new CampaignSession(legacy.State,legacy.Catalog);migrated.Validate();
        Check(Near(migrated.State.clock.elapsedDays,273)&&legacyAdult.stage==CampaignLifeStage.ActiveAdult&&legacyAdult.adultAgeDays==0&&!legacyAdult.dead,"legacy week counts map to calendar dates without retroactively aging existing adults");
        Check(legacyChild.stage==CampaignLifeStage.Larva3&&legacyChild.feed==1&&legacyChild.climate==2&&Near(migrated.RemainingDevelopmentDays(legacyChild),14),"legacy growing larvae retain their remaining deadline and chosen nutrition/climate");
        migrated.AdvanceDays(13.999);Check(!legacyChild.Adult(migrated.State.week),"legacy remaining growth is not prematurely completed during migration");
        migrated.AdvanceDays(.001);migrated.Validate();Check(legacyChild.Adult(migrated.State.week)&&legacyChild.adultAgeDays<1e-6,"legacy scheduled growth still reaches active adulthood at its original remaining deadline");
        s=Session();s.NextWeek();s.StartPupaAuction();Check(s.State.pupaAuction.beetle.stage==CampaignLifeStage.Pupa,"blind auction purchases are actual pupae rather than stalled generic larvae");
        double elapsed=s.State.clock.elapsedDays;s.AdvanceRealSeconds(600);Check(s.State.clock.elapsedDays==elapsed,"real-time calendar pauses while an incompatible legacy activity is pending");
        s.BidPupa();s.BidPupa();s.BidPupa();s.ClaimMail(s.State.mail.Last().id);var pupa=s.State.storedBeetles.Last();s.AdvanceDays(s.StageDuration(pupa));
        Check(pupa.stage==CampaignLifeStage.DormantAdult&&pupa.adultAgeDays==0,"a bought pupa automatically ecloses into a separate dormant stage without a growth purchase");s.Validate();
        s=ChampionFamily(out string youngerId);snapshot=CampaignSnapshots.Copy(s.State);
        Check(snapshot.beetles.Last().broodValueAwards==1,"transaction snapshots preserve the earned juvenile valuation award");
        var family=new CampaignSession(snapshot,s.Catalog);CheckGrownSibling(family,youngerId);
        Check(s.Beetle(youngerId).stage==CampaignLifeStage.Larva1,"growing the snapshot does not alter the original juvenile");
        var olderSibling=family.State.beetles.First(b=>b.brood==family.Beetle(youngerId).brood&&b.id!=youngerId);
        long adultInitial=olderSibling.initialValue,adultValue=olderSibling.value;
        WinSeason(family,family.Beetle(youngerId));family.Validate();
        Check(olderSibling.broodValueAwards==1&&olderSibling.initialValue==adultInitial*2&&olderSibling.value==adultValue*2,"an already adult sibling retains the original immediate initial and current value doubling");
        WinSeason(family,olderSibling);
        Check(family.Beetle(youngerId).broodValueAwards==1,"defending an existing championship cannot award the same sibling bonus again");
        family=ChampionFamily(out youngerId,true);var storedSibling=family.State.storedBeetles.Single();
        olderSibling=family.State.beetles.First(b=>b.brood==storedSibling.brood);
        long storedInitial=storedSibling.initialValue,storedValue=storedSibling.value;
        WinSeason(family,olderSibling);family.Validate();
        Check(storedSibling.broodValueAwards==1&&storedSibling.initialValue==storedInitial&&storedSibling.value==storedValue,"defending a championship does not repeat a warehouse sibling's award or valuation");
        CheckGrownSibling(family,youngerId);
        return checks;
    }

#if UNITY_EDITOR
    [MenuItem("Tools/Beetle Arena/Validate Campaign Lifecycle")]
    public static void Run()
    {
        RunDomain();var s=Session();s.ConfigureLifeRules(FastRules());var child=Egg(s);s.RenameBeetle(child.id,"自然时钟宝宝");Board(s,s.State.beetles[0]);
        s.ConfigureClock(1200,5,false);s.AdvanceRealSeconds(120);s.ConfigureClock(1200,5,true);
        string folder=Path.GetFullPath("Artifacts/Lifecycle/TestSave");var repository=new CampaignSaveRepository(folder);
        s.RenamePlayer("验收玩家");repository.Write(0,new CampaignSave {state=CampaignSnapshots.Copy(s.State),catalog=s.Catalog});var saved=repository.Read(0);var restored=new CampaignSession(saved.state,saved.catalog);restored.Validate();
        Check(Near(restored.State.clock.elapsedDays,.5)&&restored.State.clock.paused&&restored.State.clock.realSecondsPerDay==1200&&restored.State.clock.speed==5,"JSON round trip preserves clock progress, speed, day length and pause state");
        Check(restored.Beetle(child.id).stage==CampaignLifeStage.Egg&&Near(restored.Beetle(child.id).stageAgeDays,.5)&&restored.State.lifeRules.eggDays==1,"JSON round trip preserves fractional lifecycle progress and per-save stage rules");
        restored.ConfigureClock(1200,5,false);restored.AdvanceRealSeconds(120);restored.Validate();
        Check(restored.Beetle(child.id).stage==CampaignLifeStage.Larva1&&restored.Beetle(child.id).name=="自然时钟宝宝"&&Near(restored.State.storedBeetles[0].adultAgeDays,1),"reloaded natural time resumes once, retaining names and warehouse age");
        var family=ChampionFamily(out string youngerId);family.RenamePlayer("验收玩家");
        repository.Write(1,new CampaignSave {state=CampaignSnapshots.Copy(family.State),catalog=family.Catalog});var familySave=repository.Read(1);
        var restoredFamily=new CampaignSession(familySave.state,familySave.catalog);
        Check(restoredFamily.Beetle(youngerId).broodValueAwards==1&&restoredFamily.Beetle(youngerId).stage==CampaignLifeStage.Larva1,"JSON round trip preserves a juvenile sibling's earned championship award");
        CheckGrownSibling(restoredFamily,youngerId);
        family=ChampionFamily(out youngerId,true);family.RenamePlayer("验收玩家");
        repository.Write(2,new CampaignSave {state=CampaignSnapshots.Copy(family.State),catalog=family.Catalog});familySave=repository.Read(2);
        restoredFamily=new CampaignSession(familySave.state,familySave.catalog);var storedSibling=restoredFamily.State.storedBeetles.Single(b=>b.id==youngerId);
        Check(storedSibling.broodValueAwards==1&&storedSibling.stage==CampaignLifeStage.Larva1,"JSON round trip preserves the warehouse juvenile and its earned championship award");
        CheckGrownSibling(restoredFamily,youngerId);
        Directory.CreateDirectory("Artifacts/Lifecycle");File.WriteAllLines("Artifacts/Lifecycle/Data.txt",checks);Debug.Log("Campaign lifecycle PASS: "+checks.Count+" contracts");
    }
#endif
}
