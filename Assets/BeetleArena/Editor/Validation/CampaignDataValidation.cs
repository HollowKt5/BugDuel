using System;
using System.Linq;
using BugDuel.Campaign;
using BeetleArena.Campaign;
using UnityEngine;

// Isolated data contracts only. Never opens Play mode, touches player saves, or decides real match results.
public static class CampaignDataValidation
{
    public static void Run()
    {
        var c=CampaignCatalog.Defaults();c.Validate();
        var s=CampaignSession.New(c,1987);s.Validate();
        var copy=new CampaignSession(CampaignApp.CloneState(s.State),c);copy.Validate();
        Check(copy.State.battle==null&&copy.State.auction==null&&copy.State.pupaAuction==null,"optional snapshot records remain absent");
        copy.State.beetles[0].genes[0].stars=3;copy.State.beetles[0].ancestors.Add("isolated");copy.State.stock.Clear();
        Check(s.State.beetles[0].genes[0].stars==1&&s.State.beetles[0].ancestors.Count==0&&s.State.stock.Count>0,"transaction snapshot owns nested collections");
        Check(new[]{54.999,55,79.999,80,109.999,110,144.999,145}.Select(c.Tier).Select(x=>x.tier).SequenceEqual(new[]{1,2,2,3,3,4,4,5}),"league boundaries");
        string id=s.State.beetles[0].id,mother=s.State.beetles[1].id;
        s.State.coins=100000;s.Breed(id,mother);var child=s.State.beetles.Last();Check(child.maxHearts==5&&child.inheritance>=.945,"outcrossing");s.Grow(child.id,1,0);s.NextWeek();s.NextWeek();Check(child.Adult(s.State.week),"growth completion");s.Validate();
        s.Dispatch("oak",false,false);s.NextWeek();Check(s.State.expeditions.Count==0&&s.State.beetles.Count==5,"reserved expedition arrival");s.Validate();
        bool rejected=false;try{s.Dispatch("oak",false,false);}catch(InvalidOperationException){rejected=true;}Check(rejected,"capacity includes reservations");
        var adult=s.Beetle(id);adult.nutrition=100;adult.hearts=5;
        s.List(id);long quote=s.State.offers.First().price;s.Unlist(id);s.List(id);Check(s.State.offers.First().price==quote,"stable weekly offers");s.Unlist(id);
        for(int i=0;i<3;i++){adult.nutrition=100;adult.hearts=5;s.EnterLeague(id);string match=s.State.battle.id;var saved=CampaignApp.CloneState(s.State);Check(saved.battle.id==match,"pending match JSON");s.SettleBattle(match,true);}
        Check(adult.championships>0&&s.State.highestLeague==1,"champion unlock");
        long before=s.State.coins;s.BeginAuction(id,true);while(s.State.auction.round<s.State.auction.rounds)s.AdvanceAuction();s.CloseAuction();Check(s.State.coins>before&&s.State.beetles.Contains(adult)&&adult.rightsSold&&s.State.hall.Count==1,"breeding rights settlement");s.Validate();
        adult.overturned=true;adult.overturnSeconds=0;adult.hearts=5;s.CareSeconds(60);Check(adult.hearts==5,"rescue grace");s.CareSeconds(120);Check(adult.dead&&adult.hearts==0,"unassisted overturn fatality");s.Validate();
        var father=new CampaignBeetle{genes=new System.Collections.Generic.List<Gene>{new Gene("力量"),new Gene("力量",2)}};
        var mom=new CampaignBeetle{genes=new System.Collections.Generic.List<Gene>{new Gene("力量",2)}};
        var genes=CampaignGenetics.Inherit(father,mom);Check(genes.Last().stars==3&&genes.Count==2,"adjacent gene merge");
        CheckPendingActivities();
        var sale=CampaignSession.New(c,77);string sold=sale.State.selectedId;sale.List(sold);sale.AcceptOffer(sale.State.offers.First().id);
        Check(sale.State.selectedId!=sold&&sale.State.beetles.Any(b=>b.id==sale.State.selectedId),"sale keeps selection valid without UI mutating state");
        foreach(CampaignPage page in Enum.GetValues(typeof(CampaignPage)))
        {
            Check(!CampaignInputPolicy.CanManage(page,true,true,false)&&!CampaignInputPolicy.CanManage(page,true,false,true),"busy/modal input policy: "+page);
        }
        var invalid=CampaignCatalog.Defaults();invalid.species=invalid.species.Where(x=>x.id!="red").ToArray();
        bool blocked=false;try{invalid.Validate();}catch(InvalidOperationException){blocked=true;}Check(blocked,"content preserves starter species");
        Debug.Log("Campaign data contracts PASS: snapshot isolation, league boundaries, outcrossing, growth, expeditions, capacity, weekly offers, championship, auction, rescue, genetics, input policy.");
    }
    public static void CheckPendingActivities()
    {
        var s=CampaignSession.New(CampaignCatalog.Defaults(),4511);s.State.coins=100000;s.NextWeek();s.StartPupaAuction();
        Check(s.Reserved==0,"blind auction delivers through mail without reserving greenhouse space");s.Validate();
        var copy=new CampaignSession(CampaignApp.CloneState(s.State),s.Catalog);copy.Validate();Check(copy.State.pupaAuction!=null,"blind auction persistence");
        copy.BidPupa();copy.BidPupa();copy.BidPupa();copy.Validate();Check(copy.Reserved==0&&copy.State.beetles.Count==2&&copy.State.mail.Last().senderId=="auction","blind auction mailbox settlement");
        var b=copy.State.beetles[0];b.nutrition=100;long value=b.value;int streak=b.streak;copy.EnterLeague(b.id);copy.DrawBattle(copy.State.battle.id);
        Check(b.value==value&&b.streak==streak&&copy.State.battle==null,"draw does not become a defeat");copy.Validate();
        Debug.Log("Campaign pending-activity contracts PASS: blind auction reservation, JSON, settlement, combat draw.");
    }
    static void Check(bool passed,string name){if(!passed)throw new InvalidOperationException("Campaign contract failed: "+name);}
}
