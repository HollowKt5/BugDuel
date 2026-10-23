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

public static class CampaignNamingValidation
{
    static readonly List<string> checks=new List<string>();
    static void Check(bool passed,string message)
    {
        if(!passed)throw new InvalidOperationException("Campaign naming contract failed: "+message);
        checks.Add("PASS "+message);
    }
    static CampaignSession Session()
    {
        var s=CampaignSession.New(CampaignCatalog.Defaults(),8241);s.State.coins=100000;return s;
    }
    static void RejectRename(CampaignSession s,CampaignBeetle b,string name,string message)
    {
        string previous=b.name;bool custom=b.customName;long coins=s.State.coins;uint random=s.State.random;
        bool rejected=false;try{s.RenameBeetle(b.id,name);}catch(CampaignRuleException){rejected=true;}
        Check(rejected&&b.name==previous&&b.customName==custom&&s.State.coins==coins&&s.State.random==random,message);
    }
    static CampaignBeetle BreedChild(CampaignSession s)
    {
        // Naming regression uses an explicit short incubation cycle; default biology is covered by lifecycle validation.
        s.ConfigureLifeRules(new CampaignLifeRules {eggDays=1,larva1Days=1,larva2Days=1,larva3Days=1,prepupaDays=1,pupaDays=1,dormantDays=1,adultDays=180});
        var father=s.State.beetles.First(b=>!b.female);var mother=s.State.beetles.First(b=>b.female);
        s.Breed(father.id,mother.id);return s.State.beetles.Last();
    }
    static void SellChampion(CampaignSession s,CampaignBeetle b,bool rights)
    {
        s.BeginAuction(b.id,rights);while(s.State.auction.round<s.State.auction.rounds)s.AdvanceAuction();s.CloseAuction();
    }

    public static IReadOnlyList<string> RunDomain()
    {
        checks.Clear();var s=Session();var b=s.State.beetles[0];var before=CampaignSnapshots.Copy(b);
        long coins=s.State.coins;uint random=s.State.random;string selection=s.State.selectedId;
        s.RenameBeetle(b.id,"  栗子将军  ");
        Check(b.name=="栗子将军"&&b.customName,"Chinese custom names are trimmed and marked as player-authored");
        Check(s.State.coins==coins&&s.State.random==random&&s.State.selectedId==selection&&b.id==before.id&&b.speciesId==before.speciesId&&b.length==before.length&&b.hearts==before.hearts&&b.nutrition==before.nutrition&&b.value==before.value&&b.initialValue==before.initialValue&&b.readyWeek==before.readyWeek&&b.genes.Select(g=>(g.id,g.stars)).SequenceEqual(before.genes.Select(g=>(g.id,g.stars))),"renaming preserves ownership, stats, genes, money and gameplay random state");
        s.RenameBeetle(b.id,"  Oak Knight  ");Check(b.name=="Oak Knight"&&b.customName,"English custom names preserve internal spaces");
        string boundary=new string('甲',CampaignSession.BeetleNameLimit);s.RenameBeetle(b.id,boundary);
        Check(b.name.Length==24,"the advertised 24-character name limit is accepted exactly");
        RejectRename(s,b,null,"null input is rejected without modifying state");
        RejectRename(s,b,"","empty input is rejected without modifying state");
        RejectRename(s,b,"   ","whitespace-only input is rejected without modifying state");
        RejectRename(s,b,new string('甲',CampaignSession.BeetleNameLimit+1),"overlong input is rejected without modifying state");
        RejectRename(s,b,"甲\n虫","embedded newline is rejected without modifying state");
        RejectRename(s,b,"甲\r虫","embedded carriage return is rejected without modifying state");
        RejectRename(s,b,"甲\t虫","embedded tab is rejected without modifying state");
        RejectRename(s,b,"甲\0虫","embedded control code is rejected without modifying state");
        RejectRename(s,b,"<b>甲虫</b>","rich text tags are rejected without modifying state");
        RejectRename(s,b,"甲>虫","a lone rich text delimiter is rejected without modifying state");

        var stock=s.State.stock[0];RejectRename(s,stock,"商店甲虫","unowned shop stock cannot be renamed");
        var lot=s.State.marketAuctions.First(a=>a.Beetle!=null);RejectRename(s,lot.Beetle,"拍品甲虫","unowned auction stock cannot be renamed");
        s.Buy(stock.id);var mail=s.State.mail.Last();string subject=mail.subject,attachmentName=mail.attachments[0].name;
        RejectRename(s,mail.attachments[0].beetles.Single(),"邮寄甲虫","a purchase must be claimed before it can be renamed");
        s.ClaimMail(mail.id);s.RenameBeetle(stock.id,"Warehouse Knight");
        Check(s.State.storedBeetles.Single().name=="Warehouse Knight"&&s.State.storedBeetles.Single().customName,"claimed warehouse beetles support custom names");
        Check(mail.subject==subject&&mail.attachments[0].name==attachmentName&&mail.attachments[0].beetles.Count==0,"renaming claimed ownership does not rewrite the delivery receipt");
        s.HouseStoredBeetle(stock.id);
        Check(s.Beetle(stock.id).name=="Warehouse Knight"&&s.Beetle(stock.id).customName,"warehouse placement preserves the custom name and flag");
        var copy=CampaignSnapshots.Copy(s.State);var copied=copy.beetles.First(x=>x.id==stock.id);
        Check(copied.name=="Warehouse Knight"&&copied.customName,"transaction snapshots preserve names and player-authored flags");
        copied.name="Snapshot only";copied.customName=false;
        Check(s.Beetle(stock.id).name=="Warehouse Knight"&&s.Beetle(stock.id).customName,"editing a detached name snapshot cannot modify live ownership");s.Validate();

        var named=Session();var namedChild=BreedChild(named);named.RenameBeetle(namedChild.id,"小松露");named.Grow(namedChild.id,0,0);
        named.NextWeek();named.NextWeek();
        Check(namedChild.Adult(named.State.week)&&namedChild.name=="小松露"&&namedChild.customName,"a named larva keeps its name after maturation");named.Validate();
        var defaults=Session();var defaultChild=BreedChild(defaults);string larvaName=defaultChild.name;defaults.Grow(defaultChild.id,0,0);
        defaults.NextWeek();defaults.NextWeek();
        Check(defaultChild.Adult(defaults.State.week)&&!defaultChild.customName&&defaultChild.name==defaults.Catalog.Species(defaultChild.speciesId).name&&defaultChild.name!=larvaName,"an unnamed larva still receives its normal adult species name");defaults.Validate();

        var memorial=Session();var dead=memorial.State.beetles[0];dead.dead=true;dead.hearts=0;memorial.RenameBeetle(dead.id,"最后的琥珀");
        var specimen=memorial.Specimen(memorial.PreserveDead(dead.id,"2026-10-22"));
        Check(specimen.labelName=="最后的琥珀"&&specimen.beetle.name=="最后的琥珀"&&specimen.beetle.customName,"dead owned beetles can be named and their framed specimen inherits the name");
        RejectRename(memorial,specimen.beetle,"改动归档","preserved beetles leave the live naming scope; plaques use specimen editing");memorial.Validate();

        var market=Session();var listed=market.State.beetles[0];market.List(listed.id);
        var offers=market.State.offers.Select(o=>(o.id,o.beetleId,o.price,o.week,o.buyer)).ToArray();coins=market.State.coins;random=market.State.random;
        market.RenameBeetle(listed.id,"挂牌新名字");
        Check(listed.listed&&listed.name=="挂牌新名字"&&market.State.offers.Select(o=>(o.id,o.beetleId,o.price,o.week,o.buyer)).SequenceEqual(offers)&&market.State.coins==coins&&market.State.random==random,"renaming a listing preserves its ownership key, locked quotes and random state");
        var accepted=market.State.offers.First();market.AcceptOffer(accepted.id);
        Check(market.State.coins==coins+market.SaleProceeds(accepted.price)&&!market.State.beetles.Contains(listed),"the pre-rename offer still settles once for the same beetle and price");
        RejectRename(market,listed,"售出后的名字","sold beetles cannot be renamed");market.Validate();

        var history=Session();var champion=history.State.beetles[0];champion.championships=1;history.RenameBeetle(champion.id,"首配权历史名");SellChampion(history,champion,true);
        history.RenameBeetle(champion.id,"冠军新名字");
        Check(history.State.hall.Single().name=="首配权历史名"&&champion.name=="冠军新名字","later renames do not rewrite an existing champion transaction history");
        SellChampion(history,champion,false);
        Check(history.State.hall.Last().name=="冠军新名字"&&!history.State.beetles.Contains(champion),"a new champion sale records the name used at settlement");history.Validate();
        return checks;
    }

#if UNITY_EDITOR
    [MenuItem("Tools/Beetle Arena/Validate Campaign Naming")]
    public static void Run()
    {
        RunDomain();var s=Session();var adult=s.State.beetles[0];s.RenameBeetle(adult.id,"归档甲虫");
        var larva=BreedChild(s);s.RenameBeetle(larva.id,"Archive Larva");s.Grow(larva.id,0,0);
        var stored=s.State.stock[0];s.Buy(stored.id);s.ClaimMail(s.State.mail.Last().id);s.RenameBeetle(stored.id,"仓库的琥珀");
        var dead=s.Create("red",47);s.Add(dead);dead.dead=true;dead.hearts=0;s.RenameBeetle(dead.id,"纪念标本");string specimenId=s.PreserveDead(dead.id,"2026-10-22");
        string folder=Path.GetFullPath("Artifacts/Naming/TestSave");var repository=new CampaignSaveRepository(folder);
        s.RenamePlayer("验收玩家");repository.Write(0,new CampaignSave {state=CampaignSnapshots.Copy(s.State),catalog=s.Catalog});
        var saved=repository.Read(0);var loaded=new CampaignSession(saved.state,saved.catalog);loaded.Validate();
        Check(loaded.Beetle(adult.id).name=="归档甲虫"&&loaded.Beetle(adult.id).customName&&loaded.State.storedBeetles.Single().name=="仓库的琥珀"&&loaded.State.storedBeetles.Single().customName,"real JSON save preserves Chinese greenhouse and warehouse names with their flags");
        Check(loaded.Beetle(larva.id).name=="Archive Larva"&&loaded.Beetle(larva.id).customName&&loaded.Specimen(specimenId).labelName=="纪念标本"&&loaded.Specimen(specimenId).beetle.customName,"real JSON save preserves named larvae and inherited specimen identity");
        loaded.NextWeek();loaded.NextWeek();
        Check(loaded.Beetle(larva.id).Adult(loaded.State.week)&&loaded.Beetle(larva.id).name=="Archive Larva","a named larva also retains its name when maturing after reload");
        loaded.HouseStoredBeetle(stored.id);loaded.Validate();
        Check(loaded.Beetle(stored.id).name=="仓库的琥珀"&&loaded.Beetle(stored.id).customName,"a reloaded warehouse name survives subsequent greenhouse placement");
        Directory.CreateDirectory("Artifacts/Naming");File.WriteAllLines("Artifacts/Naming/Data.txt",checks);Debug.Log("Campaign naming PASS: "+checks.Count+" contracts");
    }
#endif
}
