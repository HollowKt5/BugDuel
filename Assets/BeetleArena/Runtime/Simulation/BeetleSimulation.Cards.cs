using System.Collections.Generic;
using UnityEngine;

namespace BeetleArena
{
    public sealed partial class BeetleSimulation
    {
        // Two distinct basic attacks: the full shove and the cheap probe. Both use the shared
        // CardShove window; ShovePower scales the impulse and the wind-up lunge.
        public const float ShoveCost = 10, ProbeCost = 5;
        public const float ProbePower = .65f;

        ArenaCard[] cardSlots;
        ArenaCard eventCard;
        float cardUnlockTime, frontalContact, lastFrontalContact;
        int offeredEvents;
        bool coolRain, jellyOnField, kingReady;
        readonly CardEffects[] cardEffects = { new CardEffects(), new CardEffects() };

        sealed class CardEffects
        {
            public float Rosin, Emery, Pheromone, Camphor, Dull, Breath, Limp, Startled, Brace, Anchor, JellySpeed, Groom;
            public bool CardShove;
            public float ShovePower = 1;
        }

        public bool HasCardLoadout => cardSlots != null;
        public float CardCooldownRemaining => Mathf.Max(0, cardUnlockTime - Elapsed);
        public float CardCooldownDuration { get; private set; }
        public bool JellyOnField => jellyOnField && Phase == MatchPhase.Fighting;

        public bool EquipCards(IReadOnlyList<ArenaCard> specials)
        {
            if (Phase != MatchPhase.Countdown || HasCardLoadout || !ArenaCardCatalog.ValidLoadout(specials))
                return false;
            cardSlots = new[] { ArenaCard.Shove, ArenaCard.Probe, ArenaCard.Brace, ArenaCard.Sidestep,
                specials[0], specials[1], specials[2] };
            return true;
        }

        public ArenaCard CardAt(int slot)
        {
            if (!HasCardLoadout || slot < 0 || slot >= ArenaCardCatalog.SlotCount)
                return ArenaCard.None;
            return slot == ArenaCardCatalog.EventSlot ? eventCard : cardSlots[slot];
        }

        static bool CanAct(BeetleState b) => b.HasGround && !b.HasFallen && !b.Airborne && !b.IsOverturned;

        public bool CanPlayCard(int slot)
        {
            ArenaCard card = CardAt(slot);
            var player = Fighters[0];
            var rival = Fighters[1];
            if (Phase != MatchPhase.Fighting || card == ArenaCard.None || CardCooldownRemaining > 0 || player.HasFallen)
                return false;
            // Every card needs the player standing and not being lifted; tool cards additionally need a
            // standing rival. Instant utility cards used to ignore this and could be played while airborne.
            if (!CanAct(player) || player.Action == BeetleAction.Lifting)
                return false;
            switch (card)
            {
                case ArenaCard.Shove:
                    return player.Stamina >= ShoveCost * ActionCostMultiplier(player);
                case ArenaCard.Probe:
                    return player.Stamina >= ProbeCost * ActionCostMultiplier(player);
                case ArenaCard.LastStand:
                    return CanAct(rival);
                case ArenaCard.Camphor:
                case ArenaCard.Stridulate:
                    return CanAct(rival) && rival.Action != BeetleAction.Lifting;
                default:
                    return true;
            }
        }

        public bool TryPlayCard(int slot)
        {
            if (!CanPlayCard(slot))
                return false;
            var card = CardAt(slot);
            var info = ArenaCardCatalog.Get(card);
            CardCooldownDuration = info.Cooldown;
            cardUnlockTime = Elapsed + info.Cooldown;
            if (slot == ArenaCardCatalog.EventSlot)
            {
                offeredEvents |= EventBit(card);
                eventCard = ArenaCard.None;
            }
            ApplyCard(card);
            Raise("战术卡 · " + info.Name, 0, .25f);
            return true;
        }

        void ApplyCard(ArenaCard card)
        {
            var player = Fighters[0];
            var rival = Fighters[1];
            var own = cardEffects[0];
            var enemy = cardEffects[1];
            Vector2 axis = CardAxis();
            switch (card)
            {
                case ArenaCard.Shove:
                    own.CardShove = true;
                    own.ShovePower = 1;
                    player.ContactReleaseRemaining = 0;
                    player.Stamina -= ShoveCost * ActionCostMultiplier(player);
                    player.ExertionCooldown = Settings.exertionRecoveryDelay;
                    SetAction(player, BeetleAction.ShoveWindup, .18f);
                    break;
                case ArenaCard.Probe:
                    own.CardShove = true;
                    own.ShovePower = ProbePower;
                    player.ContactReleaseRemaining = 0;
                    player.Stamina -= ProbeCost * ActionCostMultiplier(player);
                    player.ExertionCooldown = Settings.exertionRecoveryDelay;
                    SetAction(player, BeetleAction.ShoveWindup, .12f);
                    break;
                case ArenaCard.Brace:
                    own.Brace = Elapsed + 2;
                    player.Balance = .95f;
                    player.Velocity *= .25f;
                    SetAction(player, BeetleAction.Brace, 2);
                    break;
                case ArenaCard.Sidestep:
                {
                    // Steps toward the centre so the basic card cannot by itself walk the player out.
                    Vector2 side = new Vector2(-axis.y, axis.x);
                    if (Vector2.Dot(side, player.Position) > 0)
                        side = -side;
                    if (player.Position.sqrMagnitude < .0004f && Vector2.Dot(side, player.Forward) < 0)
                        side = -side;
                    player.Velocity += side * 1.15f;
                    player.Balance = Mathf.Max(player.Balance, .8f);
                    SetAction(player, BeetleAction.Circle, .45f);
                    break;
                }
                case ArenaCard.Rosin:
                    own.Rosin = Elapsed + 8;
                    break;
                case ArenaCard.Knock:
                    BreakCardContact(BeetleAction.Assess, .45f);
                    if (CanAct(player)) player.Velocity = -axis * .85f;
                    if (CanAct(rival)) rival.Velocity = axis * .85f;
                    break;
                case ArenaCard.Unhook:
                    BreakCardContact(BeetleAction.Disengage, .55f);
                    frontalContact = 0;
                    lastFrontalContact = 0;
                    break;
                case ArenaCard.Emery:
                    own.Emery = Elapsed + 6;
                    break;
                case ArenaCard.Pheromone:
                    own.Pheromone = Elapsed + 6;
                    break;
                case ArenaCard.Camphor:
                    enemy.Camphor = Elapsed + 3;
                    rival.Intent = BeetleIntent.Reposition;
                    rival.IntentTime = 0;
                    SetAction(rival, BeetleAction.Retreat, 3);
                    break;
                case ArenaCard.DullAntennae:
                    enemy.Dull = Elapsed + 5;
                    break;
                case ArenaCard.Honeydew:
                    player.Fatigue = Mathf.Max(0, player.Fatigue - 20);
                    RestoreCardStamina(player, 35);
                    break;
                case ArenaCard.Groom:
                    player.LandingSliding = false;
                    player.LandingImpactSpeed = 0;
                    player.ContactBlockedSeconds = 0;
                    player.ContactReleaseRemaining = 0;
                    player.ContactReleaseCooldown = 0;
                    if (CanAct(player))
                    {
                        player.Footing = 1;
                        own.Groom = Elapsed + .6f;
                        if (player.Action == BeetleAction.Disengage)
                            SetAction(player, BeetleAction.Assess, .25f);
                    }
                    break;
                case ArenaCard.Breathe:
                    own.Breath = Elapsed + 12;
                    break;
                case ArenaCard.PlayDead:
                    own.Limp = Elapsed + .8f;
                    SetAction(player, BeetleAction.Stagger, .8f);
                    player.Velocity *= .35f;
                    break;
                case ArenaCard.Stridulate:
                    enemy.Startled = Elapsed + .9f;
                    rival.Intent = BeetleIntent.Assess;
                    rival.IntentTime = 0;
                    SetAction(rival, BeetleAction.Assess, .9f);
                    rival.Velocity *= .25f;
                    break;
                case ArenaCard.LastStand:
                    rival.Velocity += axis * 2.25f;
                    rival.ImpactVisual = 1;
                    player.Velocity = player.Position.magnitude > Settings.arenaRadius * .75f
                        ? -player.Position.normalized * 1.5f : axis * 1.5f;
                    SetAction(player, BeetleAction.Shove, .34f);
                    break;
                case ArenaCard.Groove:
                    own.Anchor = Elapsed + 3;
                    player.Velocity = Vector2.zero;
                    SetAction(player, BeetleAction.Brace, 3);
                    break;
                case ArenaCard.King:
                    kingReady = true;
                    break;
                case ArenaCard.Rain:
                    coolRain = true;
                    break;
                case ArenaCard.Jelly:
                    jellyOnField = true;
                    break;
            }
        }

        Vector2 CardAxis()
        {
            Vector2 delta = Fighters[1].Position - Fighters[0].Position;
            return delta.sqrMagnitude > .0001f ? delta.normalized : Fighters[0].Forward;
        }

        void BreakCardContact(BeetleAction action, float duration)
        {
            foreach (var b in Fighters)
            {
                if (b.Lift.Supported)
                {
                    b.Lift.Supporter = -1;
                    if (b.Height - b.SurfaceHeight < .12f)
                    {
                        b.Airborne = false;
                        b.VerticalVelocity = 0;
                    }
                    else
                        SetAction(b, BeetleAction.Falling, 1);
                }
                b.BoutTime = 0;
                b.CombatPhase = ExchangePhase.Probing;
                b.ContactBlockedSeconds = 0;
                b.TouchingOpponent = false;
                if (!CanAct(b))
                    continue;
                b.Intent = BeetleIntent.Assess;
                b.IntentTime = 0;
                b.ContactReleaseRemaining = action == BeetleAction.Disengage ? duration : 0;
                b.ContactReleaseCooldown = duration;
                SetAction(b, action, duration);
            }
        }

        void UpdateCardEvents(float dt)
        {
            if (!HasCardLoadout || Phase != MatchPhase.Fighting)
                return;
            var player = Fighters[0];
            var rival = Fighters[1];
            var axis = CardAxis();
            bool facingOff = CanAct(player) && CanAct(rival) &&
                Vector2.Dot(player.Forward, axis) > .7f && Vector2.Dot(rival.Forward, -axis) > .7f &&
                (player.Velocity - rival.Velocity).sqrMagnitude < .36f;
            if (facingOff && player.TouchingOpponent && rival.TouchingOpponent)
                lastFrontalContact = Elapsed;
            // Accepted collision poses can leave a skin gap for a few frames during the same clinch.
            frontalContact = facingOff && lastFrontalContact > 0 && Elapsed - lastFrontalContact <= .2f
                ? frontalContact + dt : 0;
            var view = new BeetleTacticalSnapshot(player, rival, Settings.arenaRadius, EngagementDistance);
            // Trigger priority is the enum order and therefore explicit here:
            // Last Stand > Wood Groove > King's Lift > Cool Rain > Crowd Jelly. Each event can be
            // offered once per bout; OfferEvent clears the offer as soon as its condition lapses.
            OfferEvent(ArenaCard.LastStand, view.Edge > .85f && player.Footing < .99f && CanAct(player));
            OfferEvent(ArenaCard.Groove, frontalContact >= 3);
            OfferEvent(ArenaCard.King, view.Opening > .4f && !kingReady);
            OfferEvent(ArenaCard.Rain, Elapsed > 60 && player.Stamina01 < .3f && rival.Stamina01 < .3f && !coolRain);
            if (!jellyOnField)
                OfferEvent(ArenaCard.Jelly, view.Distance > EngagementDistance);

            if (!jellyOnField)
                return;
            BeetleState nearest = player.Position.sqrMagnitude <= rival.Position.sqrMagnitude ? player : rival;
            BeetleState other = nearest == player ? rival : player;
            if (!CanAct(nearest))
                nearest = other;
            if (!CanAct(nearest) || nearest.Position.sqrMagnitude > .85f * .85f)
                return;
            jellyOnField = false;
            RestoreCardStamina(nearest, 20);
            cardEffects[nearest.Index].JellySpeed = Elapsed + 4;
            Raise(FighterName(nearest.Index) + "吃到了观众投下的果冻", nearest.Index, .25f);
        }

        static int EventBit(ArenaCard card) => 1 << ((int)card - (int)ArenaCard.LastStand);

        void OfferEvent(ArenaCard card, bool condition)
        {
            int bit = EventBit(card);
            if (!condition)
                offeredEvents &= ~bit;
            else if ((eventCard == ArenaCard.None || card < eventCard) && (offeredEvents & bit) == 0)
                eventCard = card;
        }

        static void RestoreCardStamina(BeetleState b, float amount) =>
            b.Stamina = Mathf.Min(b.StaminaLimit, b.Stamina + amount);

        bool CardActive(float until) => until > Elapsed && Phase == MatchPhase.Fighting;

        bool CardShove(BeetleState b) => cardEffects[b.Index].CardShove;
        bool CardAnchored(BeetleState b) => CardActive(cardEffects[b.Index].Anchor);
        bool CardBracing(BeetleState b) => CardActive(cardEffects[b.Index].Brace);
        bool CardLimp(BeetleState b) => CardActive(cardEffects[b.Index].Limp);
        bool CardDulled(BeetleState b) => CardActive(cardEffects[b.Index].Dull);
        bool CardEmery(BeetleState b) => CardActive(cardEffects[b.Index].Emery);
        bool CardDistracted(BeetleState b) => CardActive(cardEffects[b.Index].Camphor) || CardActive(cardEffects[b.Index].Startled);

        float CardPush(BeetleState b)
        {
            var effect = cardEffects[b.Index];
            float push = CardActive(effect.Pheromone) ? 1.4f : 1;
            if (effect.Limp > 0 && !CardActive(effect.Limp) && CardActive(effect.Limp + 3))
                push *= 1.35f;
            return push;
        }

        float ActionCostMultiplier(BeetleState b)
        {
            var effect = cardEffects[b.Index];
            return (CardActive(effect.Pheromone) ? 1.5f : 1) * (CardActive(effect.Breath) ? .6f : 1) * (coolRain ? .4f : 1);
        }

        void ApplyActionEnergy(BeetleState b, float cost, bool threatened, float dt) =>
            BeetleEnergyPolicy.Apply(b, Settings, cost, threatened, dt, ActionCostMultiplier(b),
                CardActive(cardEffects[b.Index].Pheromone) ? 2 : 1);

        void ApplyCardFooting(BeetleState b, float priorFooting)
        {
            if (CardActive(cardEffects[b.Index].Groom))
                b.Footing = Mathf.Max(b.Footing, .96f);
            if (!CardActive(cardEffects[b.Index].Rosin))
                return;
            b.SurfaceGrip = Mathf.Max(b.SurfaceGrip, 1.4f);
            if (b.Footing < priorFooting)
                b.Footing = (b.Footing + priorFooting) * .5f;
        }

        float CardDisplacement(BeetleState b) => CardAnchored(b) ? 0 : CardBracing(b) ? .25f : 1;

        internal float ApplyAnchoringWeight(float weight) => CardAnchored(Fighters[0]) ? 0 : weight;

        bool TryKingLaunch(BeetleState attacker, BeetleState defender, Vector2 direction)
        {
            if (!kingReady || attacker.Index != 0 || !CanAct(attacker) || !CanAct(defender))
                return false;
            kingReady = false;
            defender.Airborne = true;
            defender.Velocity = direction * 2.6f;
            defender.VerticalVelocity = 4.8f;
            defender.Balance = .15f;
            defender.ImpactVisual = defender.LiftVisual = 1;
            defender.RollVisual = attacker.CircleDirection * 24;
            attacker.Contacts++;
            defender.Contacts++;
            ScoreContact(attacker, 1.5f);
            SetAction(defender, BeetleAction.Launched, Mathf.Max(.25f, 9.6f / Settings.gravity));
            Raise("王者昂首 · 破绽撬飞", attacker.Index, 1);
            return true;
        }
    }
}
