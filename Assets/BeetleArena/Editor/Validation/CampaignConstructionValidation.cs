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

public static class CampaignConstructionValidation
{
    static readonly List<string> checks=new List<string>();
    static void Check(bool value,string message) {if(!value)throw new InvalidOperationException("Construction contract failed: "+message);checks.Add("PASS "+message);}
    static void Rejected(Action action,string message) {bool rejected=false;try{action();}catch(CampaignRuleException){rejected=true;}Check(rejected,message);}
    static void Invalid(Action action,string message) {bool rejected=false;try{action();}catch(CampaignDataException){rejected=true;}Check(rejected,message);}
    static CampaignSession Session() {var s=CampaignSession.New(CampaignCatalog.Defaults(),29173);s.State.coins=100000;return s;}
    static void Supply(CampaignSession s,string item,int count=1) {s.BuyItem(item,count);s.ClaimMail(s.State.mail.Last().id);}
    static int Owned(CampaignSession s,string item) => s.InventoryCount(item)+s.State.decorations.Count(p=>p.itemId==item);
    static void Board(CampaignSession s,CampaignBeetle beetle) {s.State.beetles.Remove(beetle);s.State.storedBeetles.Add(beetle);s.State.selectedId=s.State.beetles.FirstOrDefault()?.id;}

    public static IReadOnlyList<string> RunDomain()
    {
        checks.Clear();var s=Session();s.Validate();string first=s.State.beetles[0].id,second=s.State.beetles[1].id;
        Check(s.State.constructionVersion==1&&CampaignDecorations.All.Count==8&&s.State.inventory.Sum(x=>x.quantity)==4&&s.State.decorations.Count==0,"new games receive exactly four starter landscape items and eight catalogue choices");
        var reloaded=new CampaignSession(CampaignSnapshots.Copy(s.State),s.Catalog);reloaded.Validate();
        Check(reloaded.State.inventory.Sum(x=>x.quantity)==4,"loading a current save never repeats the starter grant");
        var legacy=Session();legacy.State.constructionVersion=0;legacy.State.decorations=null;legacy.State.inventory.RemoveAll(x=>CampaignDecorations.Known(x.itemId));
        var migrated=new CampaignSession(legacy.State,legacy.Catalog);migrated.Validate();new CampaignSession(migrated.State,migrated.Catalog).Validate();
        Check(migrated.State.decorations.Count==0&&migrated.State.inventory.Sum(x=>x.quantity)==4,"legacy saves receive an empty layout and a one-time starter grant");

        Check(CampaignDecorations.Columns==12&&CampaignDecorations.Rows==10&&CampaignDecorations.IsReservedCell(0,3)&&!CampaignDecorations.IsReservedCell(3,2)&&!CampaignDecorations.IsReservedCell(8,7)&&CampaignDecorations.IsReservedCell(1,1)&&!CampaignDecorations.IsReservedCell(1,3),"shared grid protects the box edge and circular corners while releasing the former central reservation");
        Check(s.CanPlaceDecoration(first,CampaignDecorations.Driftwood,1,3,0)&&s.CanPlaceDecoration(first,CampaignDecorations.Driftwood,1,3,90)&&CampaignDecorations.Width(CampaignDecorations.Driftwood,90)==1&&CampaignDecorations.Depth(CampaignDecorations.Driftwood,90)==3,"quarter-turn rotation swaps rectangular footprints before geometry checks");
        Check(!s.CanPlaceDecoration(first,CampaignDecorations.Stones,12,4,0)&&!s.CanPlaceDecoration(first,CampaignDecorations.Stones,-1,4,0)&&!s.CanPlaceDecoration(first,CampaignDecorations.Stones,1,3,45),"out-of-bounds cells and unsupported angles are rejected by preview");
        long coins=s.State.coins;string stone=s.PlaceDecoration(first,CampaignDecorations.Stones,1,3,0);
        Check(s.InventoryCount(CampaignDecorations.Stones)==0&&s.State.coins==coins&&s.Decoration(first,stone).beetleId==first,"placing consumes one owned item without charging again");
        int fernCount=s.InventoryCount(CampaignDecorations.Fern);uint random=s.State.random;
        Rejected(()=>s.PlaceDecoration(first,CampaignDecorations.Fern,1,3,0),"overlapping scenery cannot be placed");
        Rejected(()=>s.PlaceDecoration(first,CampaignDecorations.Fern,1,1,0),"circular vessel corners cannot be occupied");
        Rejected(()=>s.PlaceDecoration(s.State.stock[0].id,CampaignDecorations.Fern,9,3,0),"merchant stock cannot receive a player-owned layout");
        Rejected(()=>s.PlaceDecoration(first,CampaignDecorations.Water,3,1,0),"unowned scenery cannot be placed despite valid geometry");
        Check(s.InventoryCount(CampaignDecorations.Fern)==fernCount&&s.State.decorations.Count==1&&s.State.coins==coins&&s.State.random==random,"failed placement leaves inventory, layout, money and random state unchanged");
        var free=Session();string freeOwner=free.State.beetles[0].id;
        free.PlaceDecoration(freeOwner,CampaignDecorations.Fern,4,4,0);free.Validate();
        Check(free.DecorationSpace(freeOwner).HasLivingSpace,"formerly reserved central scenery is legal when a continuous living area remains");
        var divided=new CampaignConstructionSpace(Enumerable.Range(1,8).SelectMany(z=>new[]{5,6}.Select(x=>new CampaignDecorationPlacement {itemId=CampaignDecorations.Stones,x=x,z=z})));
        Check(divided.LivingCells<CampaignConstructionSpace.MinimumLivingCells&&!divided.HasLivingSpace,"disconnected pockets cannot be added together to meet living-space capacity");
        var crowded=Session();string crowdedOwner=crowded.State.beetles[0].id;Supply(crowded,CampaignDecorations.Stones,70);
        int rejectedX=-1,rejectedZ=-1;
        for(int z=1;z<9;z++)for(int x=1;x<11;x++)
        {
            if(CampaignDecorations.IsReservedCell(x,z))continue;
            if(crowded.CanPlaceDecoration(crowdedOwner,CampaignDecorations.Stones,x,z,0))crowded.PlaceDecoration(crowdedOwner,CampaignDecorations.Stones,x,z,0);
            else if(crowded.DecorationPlacementProblem(crowdedOwner,CampaignDecorations.Stones,x,z,0).StartsWith("生活空间不足")){rejectedX=x;rejectedZ=z;}
        }
        Check(rejectedX>=0&&crowded.DecorationSpace(crowdedOwner).HasLivingSpace,"dense construction stops before the continuous living area or safe return patch disappears");
        int stock=crowded.InventoryCount(CampaignDecorations.Stones),count=crowded.State.decorations.Count;uint crowdedRandom=crowded.State.random;
        Rejected(()=>crowded.PlaceDecoration(crowdedOwner,CampaignDecorations.Stones,rejectedX,rejectedZ,0),"insufficient living space rejects a placement transaction");
        Check(crowded.InventoryCount(CampaignDecorations.Stones)==stock&&crowded.State.decorations.Count==count&&crowded.State.random==crowdedRandom,"space rejection consumes no item, id or random state");
        var badSpace=new CampaignSession(CampaignSnapshots.Copy(crowded.State),crowded.Catalog);
        badSpace.State.decorations.Add(new CampaignDecorationPlacement {id="blocked-living-space",beetleId=crowdedOwner,itemId=CampaignDecorations.Stones,x=rejectedX,z=rejectedZ});
        Invalid(badSpace.Validate,"save validation rejects a layout that exhausts connected living space");
        bool moveRejected=false;
        foreach(var placement in crowded.State.decorations.ToArray())
        {
            string problem=crowded.DecorationPlacementProblem(crowdedOwner,placement.itemId,rejectedX,rejectedZ,0,placement.id);
            if(problem==null||!problem.StartsWith("生活空间不足"))continue;
            int oldX=placement.x,oldZ=placement.z;
            Rejected(()=>crowded.MoveDecoration(crowdedOwner,placement.id,rejectedX,rejectedZ,0),"moving scenery also protects continuous living space");
            Check(placement.x==oldX&&placement.z==oldZ&&crowded.InventoryCount(CampaignDecorations.Stones)==stock,"rejected move retains its original occupied cells and inventory");moveRejected=true;break;
        }
        Check(moveRejected,"dense layout contains a meaningful movement rejection case");crowded.Validate();
        string wood=s.PlaceDecoration(first,CampaignDecorations.Driftwood,2,3,90);
        Check(s.CanPlaceDecoration(first,CampaignDecorations.Driftwood,2,3,90,wood)&&!s.CanPlaceDecoration(first,CampaignDecorations.Driftwood,2,3,90),"move preview ignores only its own footprint");
        s.MoveDecoration(first,wood,2,3,270);s.MoveDecoration(first,stone,1,4,90);
        Rejected(()=>s.MoveDecoration(first,wood,1,3,90),"moving onto another footprint fails");
        Check(s.Decoration(first,wood).x==2&&s.Decoration(first,wood).rotation==270&&s.InventoryCount(CampaignDecorations.Driftwood)==0,"failed movement retains position and consumes no extra inventory");
        Check(!s.CanPlaceDecoration(second,CampaignDecorations.Driftwood,2,3,90,wood),"preview cannot ignore scenery owned by another beetle");
        Supply(s,CampaignDecorations.Stones);string otherStone=s.PlaceDecoration(second,CampaignDecorations.Stones,1,4,180);s.Validate();
        Check(s.State.decorations.Count(p=>p.x==1&&p.z==4)==2,"different beetles can independently use identical cells");
        Rejected(()=>s.ReturnDecoration(second,stone),"another beetle's scenery cannot be returned through a mismatched owner");
        var copy=CampaignSnapshots.Copy(s.State);copy.decorations[0].x=9;copy.decorations.RemoveAt(1);copy.inventory[0].quantity++;
        Check(s.Decoration(first,stone).x==1&&s.State.decorations.Count==3&&s.InventoryCount(CampaignDecorations.Fern)==1,"snapshot copies isolate placement records, their list and inventory");
        s.ReturnDecoration(first,stone);int returned=s.InventoryCount(CampaignDecorations.Stones);
        Rejected(()=>s.ReturnDecoration(first,stone),"returned scenery cannot be returned twice");
        Check(returned==1&&s.InventoryCount(CampaignDecorations.Stones)==1,"return restores exactly one inventory item");
        coins=s.State.coins;s.SellDecoration(first,wood);
        Rejected(()=>s.SellDecoration(first,wood),"a sold placement cannot pay twice");
        Check(s.State.coins==coins+56&&s.InventoryCount(CampaignDecorations.Driftwood)==0&&!s.State.decorations.Any(p=>p.id==wood),"direct placement sale pays forty percent once without also returning stock");

        foreach(var item in CampaignDecorations.All)
        {
            var trade=Session();int before=trade.InventoryCount(item.id);long funds=trade.State.coins;trade.BuyItem(item.id,2);var mail=trade.State.mail.Last();
            Check(trade.InventoryCount(item.id)==before&&mail.Pending&&mail.attachments[0].quantity==2,"landscape purchase stays in mail until claimed: "+item.id);
            trade.ClaimMail(mail.id);trade.SellItem(item.id,2);trade.Validate();
            Check(trade.InventoryCount(item.id)==before&&trade.State.coins==funds-item.price*2+(long)Math.Floor(item.price*.4)*2,"landscape buy/claim/resale conserves items with a fixed spread: "+item.id);
        }
        var cap=Session();string capBeetle=cap.State.beetles[0].id;string capStone=cap.PlaceDecoration(capBeetle,CampaignDecorations.Stones,1,3,0);
        cap.State.inventory.Add(new CampaignInventoryStack {itemId=CampaignDecorations.Stones,quantity=999999});
        Rejected(()=>cap.BuyItem(CampaignDecorations.Stones),"deployed scenery reserves its warehouse capacity for eventual return");cap.ReturnDecoration(capBeetle,capStone);cap.Validate();
        Check(cap.InventoryCount(CampaignDecorations.Stones)==1000000,"a full-capacity scenery collection can still be reclaimed without overflow or loss");

        var stored=s.State.beetles.First(b=>b.id==second);Board(s,stored);s.Validate();
        Check(s.Decoration(second,otherStone).beetleId==second,"warehouse storage retains the beetle's own layout");
        s.HouseStoredBeetle(second);s.Validate();Check(s.Decoration(second,otherStone).rotation==180,"moving back into the greenhouse restores the same layout");
        var growing=Session();growing.ConfigureLifeRules(new CampaignLifeRules {eggDays=1,larva1Days=1,larva2Days=1,larva3Days=1,prepupaDays=1,pupaDays=1,dormantDays=1});
        growing.Breed(growing.State.beetles[0].id,growing.State.beetles[1].id);var egg=growing.State.beetles.Last();string eggFern=growing.PlaceDecoration(egg.id,CampaignDecorations.Fern,9,3,0);
        growing.AdvanceDays(7);growing.Validate();Check(egg.stage==CampaignLifeStage.ActiveAdult&&growing.Decoration(egg.id,eggFern).x==9,"stage transitions retain the layout under one shared safe grid mask");

        foreach(string route in new[]{"shop","warehouse-shop","bbs","champion","preserve","discard"})
        {
            var exit=Session();var beetle=exit.State.beetles[0];exit.PlaceDecoration(beetle.id,CampaignDecorations.Stones,1,3,0);
            if(route=="warehouse-shop")Board(exit,beetle);
            if(route=="shop"||route=="warehouse-shop")exit.SellBeetle(beetle.id);
            else if(route=="bbs"){exit.List(beetle.id);exit.AcceptOffer(exit.State.offers.First(o=>o.beetleId==beetle.id).id);}
            else if(route=="champion"){beetle.championships=1;exit.BeginAuction(beetle.id,false);while(exit.State.auction.round<exit.State.auction.rounds)exit.AdvanceAuction();exit.CloseAuction();}
            else {beetle.dead=true;beetle.hearts=0;if(route=="preserve")exit.PreserveDead(beetle.id,"纪念日");else {Board(exit,beetle);exit.ClearDead(beetle.id);}}
            exit.Validate();Check(exit.InventoryCount(CampaignDecorations.Stones)==1&&!exit.State.decorations.Any(p=>p.beetleId==beetle.id),"losing beetle ownership reclaims scenery exactly once: "+route);
        }
        var rights=Session();var champion=rights.State.beetles[0];champion.championships=1;string kept=rights.PlaceDecoration(champion.id,CampaignDecorations.Stones,1,3,0);
        rights.BeginAuction(champion.id,true);while(rights.State.auction.round<rights.State.auction.rounds)rights.AdvanceAuction();rights.CloseAuction();rights.Validate();
        Check(rights.Decoration(champion.id,kept)!=null&&rights.InventoryCount(CampaignDecorations.Stones)==0,"selling breeding rights retains both beetle ownership and its scenery");

        var valid=Session();valid.PlaceDecoration(valid.State.beetles[0].id,CampaignDecorations.Stones,1,3,0);
        foreach(string corruption in new[]{"owner","overlap","rotation","reserved","item"})
        {
            var broken=new CampaignSession(CampaignSnapshots.Copy(valid.State),valid.Catalog);var p=broken.State.decorations[0];
            if(corruption=="owner")p.beetleId=broken.State.stock[0].id;
            else if(corruption=="overlap")broken.State.decorations.Add(new CampaignDecorationPlacement {id="duplicate-footprint",beetleId=p.beetleId,itemId=p.itemId,x=p.x,z=p.z,rotation=p.rotation});
            else if(corruption=="rotation")p.rotation=45;
            else if(corruption=="reserved"){p.x=0;p.z=4;}
            else p.itemId="missing-landscape";
            Invalid(broken.Validate,"save validation rejects corrupt construction "+corruption);
        }
        return checks;
    }

#if UNITY_EDITOR
    [MenuItem("Tools/Beetle Arena/Validate Campaign Construction")]
    public static void Run()
    {
        RunDomain();var s=Session();string beetle=s.State.beetles[0].id;string placed=s.PlaceDecoration(beetle,CampaignDecorations.Driftwood,1,3,90);Supply(s,CampaignDecorations.Arch,2);
        string folder=Path.GetFullPath("Artifacts/Construction/TestSave");var repository=new CampaignSaveRepository(folder);
        s.RenamePlayer("验收玩家");repository.Write(0,new CampaignSave {state=CampaignSnapshots.Copy(s.State),catalog=s.Catalog});var saved=repository.Read(0);var restored=new CampaignSession(saved.state,saved.catalog);restored.Validate();
        Check(restored.State.constructionVersion==1&&restored.Decoration(beetle,placed).rotation==90&&restored.Decoration(beetle,placed).x==1,"real JSON save restores beetle ownership, cell and rotation");
        Check(restored.InventoryCount(CampaignDecorations.Arch)==2&&restored.InventoryCount(CampaignDecorations.Driftwood)==0,"real JSON save retains inventory without repeating starter gifts");
        restored.ReturnDecoration(beetle,placed);restored.SellItem(CampaignDecorations.Driftwood);restored.Validate();
        Check(restored.InventoryCount(CampaignDecorations.Driftwood)==0&&restored.State.decorations.Count==0,"reloaded placement remains usable through return and resale");
        Directory.CreateDirectory("Artifacts/Construction");File.WriteAllLines("Artifacts/Construction/Data.txt",checks);Debug.Log("Campaign construction PASS: "+checks.Count+" contracts");
    }
#endif
}
