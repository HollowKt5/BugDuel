using System.Collections.Generic;
using System.Linq;

namespace BugDuel.Campaign
{
    // Copy only the mutable object graph. Transactions never serialize JSON to clone state.
    public static class CampaignSnapshots
    {
        public static CampaignState Copy(CampaignState s)
        {
            var copy = s.ShallowCopy();
            copy.beetles = s.beetles.Select(Copy).ToList();
            copy.stock = s.stock.Select(Copy).ToList();
            copy.expeditions = s.expeditions.Select(x => new Expedition { id=x.id, habitat=x.habitat, dueWeek=x.dueWeek, count=x.count, large=x.large }).ToList();
            copy.offers = s.offers.Select(x => new MarketOffer { id=x.id, beetleId=x.beetleId, buyer=x.buyer, price=x.price, week=x.week, femaleBonus=x.femaleBonus, recipe=x.recipe, permit=x.permit }).ToList();
            copy.hall = s.hall.Select(x => new HallRecord { name=x.name, speciesId=x.speciesId, mutation=x.mutation, length=x.length, price=x.price, week=x.week, rights=x.rights, royalties=x.royalties }).ToList();
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
            copy.genes = b.genes.Select(g => new Gene(g.id, g.stars)).ToList();
            copy.meal = b.meal == null ? null : new CampaignMeal { id=b.meal.id, food=b.meal.food };
            return copy;
        }
    }
}
