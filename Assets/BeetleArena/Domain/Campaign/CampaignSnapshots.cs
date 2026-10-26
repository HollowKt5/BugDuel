using System;
using System.Collections.Generic;

namespace BugDuel.Campaign
{
    // Copy only the mutable object graph. Transactions never serialize JSON to clone state.
    public static class CampaignSnapshots
    {
        // The detached lists and mutable records still allocate. Cache non-capturing
        // converters and allocate each backing array once, without LINQ iterators.
        static List<T> CopyList<T>(List<T> source, Func<T, T> copy)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var result = new List<T>(source.Count);
            for (int i = 0; i < source.Count; i++) result.Add(copy(source[i]));
            return result;
        }

        public static CampaignState Copy(CampaignState s)
        {
            var copy = s.ShallowCopy();
            copy.clock=new CampaignClock {elapsedDays=s.clock.elapsedDays,realSecondsPerDay=s.clock.realSecondsPerDay,speed=s.clock.speed,paused=s.clock.paused};
            copy.lifeRules=s.lifeRules.Copy();
            copy.beetles = CopyList(s.beetles, b => Copy(b));
            copy.stock = CopyList(s.stock, b => Copy(b));
            copy.storedBeetles=CopyList(s.storedBeetles, b => Copy(b));
            copy.inventory=CopyList(s.inventory,x=>new CampaignInventoryStack {itemId=x.itemId,quantity=x.quantity});
            copy.decorations=CopyList(s.decorations,x=>new CampaignDecorationPlacement {id=x.id,beetleId=x.beetleId,itemId=x.itemId,x=x.x,z=x.z,rotation=x.rotation});
            copy.mail=CopyList(s.mail,m=>new CampaignMail {id=m.id,senderId=m.senderId,subject=m.subject,body=m.body,week=m.week,read=m.read,deleted=m.deleted,claimed=m.claimed,
                attachments=CopyList(m.attachments,a=>new CampaignAttachment {itemId=a.itemId,name=a.name,literalName=a.literalName,quantity=a.quantity,beetles=CopyList(a.beetles,b=>Copy(b)),specimens=CopyList(a.specimens,p=>Copy(p))})});
            copy.specimens=CopyList(s.specimens,p=>Copy(p));
            copy.specimenStock=CopyList(s.specimenStock,p=>Copy(p));
            copy.specimenOffers=CopyList(s.specimenOffers,x=>new SpecimenOffer {id=x.id,specimenId=x.specimenId,buyer=x.buyer,price=x.price,week=x.week});
            copy.marketAuctions=CopyList(s.marketAuctions,a=>new MarketAuctionLot {id=a.id,kind=a.kind,name=a.name,highBidder=a.highBidder,result=a.result,
                expectedPrice=a.expectedPrice,currentBid=a.currentBid,playerBid=a.playerBid,rivalLimit=a.rivalLimit,round=a.round,rounds=a.rounds,closed=a.closed,won=a.won,playerParticipated=a.playerParticipated,highBidderIsPlayer=a.highBidderIsPlayer,
                beetles=CopyList(a.beetles,b=>Copy(b)),specimens=CopyList(a.specimens,p=>Copy(p))});
            copy.expeditions = CopyList(s.expeditions,x => new Expedition { id=x.id, habitat=x.habitat, dueWeek=x.dueWeek, count=x.count, large=x.large });
            copy.offers = CopyList(s.offers,x => new MarketOffer { id=x.id, beetleId=x.beetleId, buyer=x.buyer, price=x.price, week=x.week, femaleBonus=x.femaleBonus, recipe=x.recipe, permit=x.permit });
            copy.hall = CopyList(s.hall,x => new HallRecord { name=x.name, customName=x.customName, speciesId=x.speciesId, mutation=x.mutation, length=x.length, price=x.price, week=x.week, rights=x.rights, royalties=x.royalties });
            copy.battle = s.battle == null ? null : new PendingBattle { id=s.battle.id, beetleId=s.battle.beetleId, opponent=Copy(s.battle.opponent), seed=s.battle.seed, tier=s.battle.tier, fee=s.battle.fee };
            copy.auction = s.auction == null ? null : new AuctionState { beetleId=s.auction.beetleId, rights=s.auction.rights, round=s.auction.round, rounds=s.auction.rounds, bid=s.auction.bid, finalBid=s.auction.finalBid, bidder=s.auction.bidder };
            copy.pupaAuction = s.pupaAuction == null ? null : new PupaAuction { beetle=Copy(s.pupaAuction.beetle), round=s.pupaAuction.round, bid=s.pupaAuction.bid };
            return copy;
        }

        public static CampaignBeetle Copy(CampaignBeetle b)
        {
            if (b == null) return null;
            var copy = b.ShallowCopy();
            copy.ancestors = new List<string>(b.ancestors);
            copy.genes = CopyList(b.genes,g => new Gene(g.id, g.stars));
            copy.meal = b.meal == null ? null : new CampaignMeal { id=b.meal.id, food=b.meal.food };
            return copy;
        }
        public static CampaignSpecimen Copy(CampaignSpecimen p) => new CampaignSpecimen {id=p.id,beetle=Copy(p.beetle),labelName=p.labelName,speciesLabel=p.speciesLabel,dateLabel=p.dateLabel,
            preparedWeek=p.preparedWeek,wallSlot=p.wallSlot,frameStyle=p.frameStyle,rotation=p.rotation,listed=p.listed,value=p.value,
            plaqueVersion=p.plaqueVersion,customLabelName=p.customLabelName,customSpeciesLabel=p.customSpeciesLabel,customDateLabel=p.customDateLabel};
    }
}
