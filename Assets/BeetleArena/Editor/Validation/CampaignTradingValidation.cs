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

// Domain regressions can also run in a plain .NET harness without opening Unity scenes.
public static class CampaignTradingValidation
{
    static readonly List<string> checks=new List<string>();
    static void Check(bool ok,string message) { if(!ok)throw new InvalidOperationException(message);checks.Add("PASS "+message); }
    static void Rejected(Action action,string message) { bool failed=false;try{action();}catch(CampaignRuleException){failed=true;}Check(failed,message); }
    static CampaignSession Session(uint seed=2187) {var s=CampaignSession.New(CampaignCatalog.Defaults(),seed);s.State.coins=100000;return s;}
    static string Preserve(CampaignSession s)
    {
        var b=s.Create("red",47);s.Add(b);b.dead=true;b.hearts=0;return s.PreserveDead(b.id,"2026-10-22");
    }
    static void Finish(CampaignSession s,MarketAuctionLot a) {while(!a.closed)s.AdvanceMarketAuction(a.id);s.Validate();}

    public static IReadOnlyList<string> RunDomain()
    {
        checks.Clear();var s=Session();s.Validate();
        Check(s.State.marketAuctions.Count==3&&s.State.specimenStock.Count==3,"new and weekly markets contain live lots and framed specimens");
        var living=s.State.beetles[0];long coins=s.State.coins;
        Rejected(()=>s.PreserveDead(living.id,"2026-10-22"),"living insects cannot be preserved");
        Check(s.State.coins==coins&&s.State.beetles.Contains(living),"refused preservation changes no ownership or money");
        living.dead=true;living.hearts=0;string originalId=living.id;var p=s.Specimen(s.PreserveDead(originalId,"2026-10-22"));
        Check(s.State.coins==coins-CampaignSession.SpecimenPreparationCost&&!s.State.beetles.Contains(living)&&p.beetle.id==originalId&&p.beetle.dead,"preservation charges once and transfers the original individual out of greenhouse");
        Rejected(()=>s.PreserveDead(originalId,"2026-10-22"),"a corpse cannot produce duplicate specimens");
        long value=p.value;s.EditSpecimen(p.id,"林间纪念","自定义种类铭牌","2026-10-21",2,90);
        Check(p.labelName=="林间纪念"&&p.speciesLabel=="自定义种类铭牌"&&p.dateLabel=="2026-10-21"&&p.frameStyle==2&&p.rotation==90&&p.value==value,"editable labels and frame rotation preserve underlying species and valuation");
        var wall=new List<string>{p.id};for(int i=1;i<12;i++)wall.Add(Preserve(s));
        for(int i=0;i<wall.Count;i++)s.PlaceSpecimen(wall[i],i);
        s.PlaceSpecimen(wall[0],11);
        Check(s.Specimen(wall[0]).wallSlot==11&&s.Specimen(wall[11]).wallSlot==0&&s.State.specimens.Count(x=>x.wallSlot>=0)==12,"twelve wall positions allow occupied-slot swaps without losing specimens");
        Rejected(()=>s.PlaceSpecimen(wall[0],12),"a thirteenth wall position is rejected");
        string extra=Preserve(s);s.PlaceSpecimen(extra,0);
        Check(s.Specimen(wall[11]).wallSlot==-1&&s.Specimen(extra).wallSlot==0&&s.State.specimens.Count==13,"placing from storage onto a full wall returns its previous occupant to storage");
        s.UnmountSpecimen(extra);Check(s.Specimen(extra).wallSlot==-1,"unmount returns a frame to collection storage");
        var clone=CampaignSnapshots.Copy(s.State);clone.specimens[0].labelName="changed";clone.specimens[0].beetle.genes[0].stars=3;
        Check(s.State.specimens[0].labelName!="changed"&&s.State.specimens[0].beetle.genes[0].stars==1,"specimen snapshots deeply isolate preserved genes and metadata");
        for(int i=0;i<3;i++)s.ListSpecimen(wall[i]);
        Rejected(()=>s.ListSpecimen(extra),"live and specimen listings share finite market cabinet slots");
        Rejected(()=>s.EditSpecimen(wall[0],"n","s","d",0,0),"listed frames cannot change their advertised metadata");
        var offer=s.State.specimenOffers.First();long quote=offer.price;s.UnlistSpecimen(offer.specimenId);s.ListSpecimen(offer.specimenId);
        Check(s.State.specimenOffers.First(x=>x.specimenId==offer.specimenId).price==quote,"withdrawing and relisting cannot reroll a specimen quote");
        offer=s.State.specimenOffers.First();coins=s.State.coins;string sold=offer.specimenId;s.AcceptSpecimenOffer(offer.id);
        Check(s.State.coins==coins+s.SaleProceeds(offer.price)&&!s.State.specimens.Any(x=>x.id==sold)&&!s.State.specimenOffers.Any(x=>x.specimenId==sold),"specimen listing settlement charges commission and removes all stale offers");
        Rejected(()=>s.AcceptSpecimenOffer(offer.id),"a specimen offer cannot pay twice");s.Validate();

        s=Session();var stock=s.State.specimenStock[0];coins=s.State.coins;long retail=s.SpecimenPurchasePrice(stock);s.BuySpecimen(stock.id);var mail=s.State.mail.Last();
        Check(s.State.coins==coins-retail&&s.State.specimens.Count==0&&mail.attachments[0].specimens.Single().id==stock.id,"specimen retail transfers ownership to a mailbox attachment before claiming");
        Rejected(()=>s.BuySpecimen(stock.id),"a sold retail specimen cannot be purchased twice");
        clone=CampaignSnapshots.Copy(s.State);clone.mail.Last().attachments[0].specimens[0].beetle.name="mutated";
        Check(mail.attachments[0].specimens[0].beetle.name!="mutated","mail attachment specimen graphs are deeply copied");
        s.ClaimMail(mail.id);Rejected(()=>s.ClaimMail(mail.id),"specimen attachment cannot be claimed twice");
        Check(s.State.specimens.Single().id==stock.id&&mail.attachments[0].specimens.Count==0,"claim moves sole specimen ownership into collection");
        coins=s.State.coins;s.SellSpecimen(stock.id);Check(s.State.coins-coins<retail&&s.State.specimens.Count==0,"buying and immediately selling a specimen cannot create coins");
        foreach(string item in new[]{CampaignItems.Food(0),CampaignItems.Food(1),CampaignItems.Food(2),CampaignItems.Sap,CampaignItems.Lamp})
        {
            coins=s.State.coins;s.BuyItem(item,3);s.ClaimMail(s.State.mail.Last().id);s.SellItem(item,3);
            Check(s.InventoryCount(item)==0&&s.State.coins<coins,"quantity purchase and resale conserve inventory with a spread: "+item);
            Rejected(()=>s.SellItem(item),"cannot sell absent inventory: "+item);
        }
        s.BuyItem(CampaignItems.Cabinet);Rejected(()=>s.BuyItem(CampaignItems.Cabinet),"pending cabinet upgrades cannot be ordered twice");
        s.ClaimMail(s.State.mail.Last().id);s.SellItem(CampaignItems.Cabinet);Check(!s.CabinetOrdered,"selling an uninstalled kit releases the single-order restriction");
        var b=s.State.stock[0];coins=s.State.coins;retail=s.PurchasePrice(b);s.Buy(b.id);s.ClaimMail(s.State.mail.Last().id);s.SellBeetle(b.id);
        Check(!s.State.storedBeetles.Any(x=>x.id==b.id)&&s.State.coins<coins,"warehouse live beetles can be sold without a greenhouse slot and never flip for profit");
        Rejected(()=>s.SellBeetle(b.id),"a sold live individual cannot pay twice");
        var rare=s.State.beetles[0];rare.genes[0].stars=3;rare.mutation="传奇亚种";rare.value=CampaignEconomy.Initial(rare,s.Catalog.Species(rare.speciesId));s.List(rare.id);
        Check(s.State.offers.All(o=>s.SaleProceeds(o.price)<s.PurchasePrice(rare)),"specialist buyer premiums remain below retail after commission");s.Unlist(rare.id);s.Validate();

        s=Session();var lot=s.State.marketAuctions[0];coins=s.State.coins;long first=s.AuctionMinimumBid(lot);s.BidMarketAuction(lot.id,first);
        Check(s.State.coins==coins-first&&lot.playerBid==first&&lot.round==0,"bidding escrows money without advancing NPC rounds");
        long second=s.AuctionMinimumBid(lot);s.BidMarketAuction(lot.id,second);
        Check(s.State.coins==coins-second&&lot.playerBid==second,"raising your own bid charges only the difference");
        Rejected(()=>s.BidMarketAuction(lot.id,second),"a bid must exceed the current minimum increment");
        clone=CampaignSnapshots.Copy(s.State);clone.marketAuctions[0].beetles[0].genes[0].stars=3;
        Check(lot.Beetle.genes[0].stars!=3&&clone.marketAuctions[0].playerBid==second,"pending auction snapshots retain escrow and deeply isolate the lot");
        lot.rivalLimit=(long)(lot.expectedPrice*.95);s.State.random=1;s.AdvanceMarketAuction(lot.id);
        Check(lot.playerBid==0&&lot.highBidder!="你"&&s.State.coins==coins,"NPC outbid returns the entire player escrow immediately");
        s.BidMarketAuction(lot.id,lot.expectedPrice);Finish(s,lot);
        Check(lot.won&&lot.playerBid==0&&s.State.coins==coins-lot.expectedPrice&&s.State.mail.Last().senderId=="auction"&&lot.beetles.Count==0,"an expected-price highest bid wins with exactly one payment and mail delivery");
        Rejected(()=>s.AdvanceMarketAuction(lot.id),"closed auctions cannot settle again");Rejected(()=>s.BidMarketAuction(lot.id,lot.expectedPrice*2),"closed auctions reject new bids");
        var specimenLot=s.State.marketAuctions.Single(a=>a.kind=="specimen");s.BidMarketAuction(specimenLot.id,specimenLot.expectedPrice);Finish(s,specimenLot);s.ClaimMail(s.State.mail.Last().id);
        Check(s.State.specimens.Count==1&&s.State.specimens[0].wallSlot==-1,"auction specimen claims are ready for room placement");
        var pending=s.State.marketAuctions.First(a=>!a.closed);s.BidMarketAuction(pending.id,pending.expectedPrice);coins=s.State.coins;s.NextWeek();
        Check(s.State.coins==coins&&s.State.marketAuctions.Count==3&&s.State.marketAuctions.All(a=>!a.closed)&&s.State.mail.Last().senderId=="auction","advancing the week settles pending escrow before generating new auction lots");s.Validate();

        var capped=Session();capped.State.coins=CampaignEconomy.Maximum;
        var rivalLot=capped.State.marketAuctions[0];rivalLot.expectedPrice=1000;rivalLot.rivalLimit=950;
        var winningLot=capped.State.marketAuctions[1];winningLot.expectedPrice=1000;winningLot.rivalLimit=900;
        capped.BidMarketAuction(rivalLot.id,250);capped.BidMarketAuction(winningLot.id,1000);capped.Earn(5000);
        Check(capped.State.coins==CampaignEconomy.Maximum-1250,"earnings reserve wallet capacity for every outstanding auction escrow");
        capped.State.random=1;capped.AdvanceMarketAuction(rivalLot.id);
        Check(rivalLot.playerBid==0&&capped.State.coins==CampaignEconomy.Maximum-1000,"an outbid at the wallet cap refunds exactly while preserving another lot's escrow");
        var failedLot=capped.State.marketAuctions[2];failedLot.expectedPrice=1000;failedLot.rivalLimit=100;
        capped.BidMarketAuction(failedLot.id,250);capped.Earn(5000);capped.State.random=0x12345678;Finish(capped,failedLot);
        Check(!failedLot.won&&capped.State.coins==CampaignEconomy.Maximum-1000,"seller rejection at the wallet cap returns the full bid");
        Finish(capped,winningLot);Check(winningLot.won&&capped.State.coins==CampaignEconomy.Maximum-1000,"winning clears escrow without refunding the purchase price");

        int lowWins=0,nearWins=0;
        for(int n=1;n<=512;n++)foreach(bool near in new[]{false,true})
        {
            var trial=Session((uint)n);var a=trial.State.marketAuctions[0];a.expectedPrice=1000;a.rivalLimit=100;
            long bid=near?750:250;coins=trial.State.coins;trial.BidMarketAuction(a.id,bid);trial.State.random=unchecked((uint)n*2654435761u);Finish(trial,a);
            if(a.won){if(near)nearWins++;else lowWins++;}
            if(!a.won&&(trial.State.coins!=coins||trial.State.mail.Last().senderId!="auction"||trial.State.mail.Last().attachments.Count!=0))throw new InvalidOperationException("failed seller acceptance must refund the full bid and mail a receipt");
        }
        Check(lowWins>0&&lowWins<70&&nearWins>230&&nearWins<340&&nearWins>lowWins*4,"below-expectation offers have measured partial acceptance, increasing near expected price (25%: "+lowWins+"/512; 75%: "+nearWins+"/512)");
        var legacy=CampaignSnapshots.Copy(s.State);legacy.tradingVersion=0;legacy.specimens=null;legacy.specimenStock=null;legacy.specimenOffers=null;legacy.marketAuctions=null;
        int count=legacy.beetles.Count;var migrated=new CampaignSession(legacy,s.Catalog);migrated.Validate();
        Check(migrated.State.tradingVersion==1&&migrated.State.beetles.Count==count&&migrated.State.marketAuctions.Count==3,"legacy saves gain specimen and auction storage without losing live ownership");
        return checks;
    }

#if UNITY_EDITOR
    [MenuItem("Tools/Beetle Arena/Validate Campaign Trading")]
    public static void Run()
    {
        RunDomain();var s=Session();string p=Preserve(s);s.EditSpecimen(p,"归档甲虫","锹甲纪念","2026-10-22",1,-45);s.PlaceSpecimen(p,11);
        var lot=s.State.marketAuctions.First(a=>a.kind=="specimen");s.BidMarketAuction(lot.id,lot.expectedPrice);Finish(s,lot);
        var pending=s.State.marketAuctions.First(a=>!a.closed);s.BidMarketAuction(pending.id,pending.expectedPrice);
        string folder=Path.GetFullPath("Artifacts/Trading/TestSave");var repository=new CampaignSaveRepository(folder);
        s.RenamePlayer("验收玩家");repository.Write(0,new CampaignSave {state=CampaignSnapshots.Copy(s.State),catalog=s.Catalog});var save=repository.Read(0);var loaded=new CampaignSession(save.state,save.catalog);loaded.Validate();
        Check(loaded.Specimen(p).wallSlot==11&&loaded.Specimen(p).rotation==-45&&loaded.Specimen(p).dateLabel=="2026-10-22"&&loaded.Specimen(p).beetle.meal==null,"disk round trip preserves wall composition and removes inline phantom meals");
        Check(loaded.GetMarketAuction(pending.id).playerBid==pending.expectedPrice&&loaded.State.coins==s.State.coins,"disk round trip preserves pending bid escrow without another deduction");
        var mail=loaded.State.mail.Last();loaded.ClaimMail(mail.id);loaded.Validate();Rejected(()=>loaded.ClaimMail(mail.id),"a reloaded specimen mail cannot be claimed twice");
        Directory.CreateDirectory("Artifacts/Trading");File.WriteAllLines("Artifacts/Trading/Data.txt",checks);Debug.Log("Campaign trading PASS: "+checks.Count+" contracts");
    }
#endif
}
