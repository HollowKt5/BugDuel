using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BeetleArena;
using BeetleArena.Campaign;
using UnityEditor;
using UnityEngine;

/// <summary>Headless checks for the tactical card set: catalogue, hand composition, play gates and localisation.</summary>
public static class CardSystemValidation
{
    static readonly ArenaCard[] Specials = { ArenaCard.Rosin, ArenaCard.Honeydew, ArenaCard.Breathe };

    [MenuItem("Tools/甲壳虫斗/验证/战斗与动作/战术卡组", false, 320)]
    public static void Run()
    {
        bool previousEnglish = CampaignLocalization.English;
        int checks = 0;
        var report = new StringBuilder("TACTICAL CARD SYSTEM\n");
        var assets = new List<ScriptableObject>();
        void Check(bool valid, string detail)
        {
            if (!valid) throw new InvalidOperationException("Tactical cards: " + detail);
            report.AppendLine("PASS " + (++checks) + " " + detail);
        }
        try
        {
            Catalogue(Check);
            LoadoutRules(Check);
            HandAndCooldown(Check, assets);
            PlayGates(Check, assets);
            BasicCardIdentity(Check, assets);
            WithoutCards(Check, assets);
            Localisation(Check);
            report.AppendLine("PASS " + checks + " checks: catalogue, loadout rules, hand order, shared cooldown, play gates, basic card identity and localisation.");
        }
        catch (Exception exception)
        {
            report.AppendLine("FAIL " + exception.Message);
            report.AppendLine(exception.StackTrace);
            throw;
        }
        finally
        {
            CampaignLocalization.Select(previousEnglish ? 1 : 0);
            foreach (var asset in assets)
                UnityEngine.Object.DestroyImmediate(asset);
            Directory.CreateDirectory("Artifacts/Cards");
            File.WriteAllText("Artifacts/Cards/CardSystem.txt", report.ToString());
        }
        Debug.Log("Tactical card system PASS: " + checks + " checks. See Artifacts/Cards/CardSystem.txt");
    }

    static void Catalogue(Action<bool, string> check)
    {
        var names = new HashSet<string>();
        var english = new HashSet<string>();
        int basics = 0, specials = 0, events = 0;
        for (var card = ArenaCard.Shove; card <= ArenaCard.Jelly; card++)
        {
            var info = ArenaCardCatalog.Get(card);
            check(!string.IsNullOrWhiteSpace(info.Name) && !string.IsNullOrWhiteSpace(info.EnglishName) &&
                !string.IsNullOrWhiteSpace(info.Description) && !string.IsNullOrWhiteSpace(info.EnglishDescription),
                card + " has Chinese and English card text");
            check(info.Cooldown > 0, card + " cooldown is positive");
            check(names.Add(info.Name) && english.Add(info.EnglishName), card + " names are unique");
            if (ArenaCardCatalog.IsBasic(card)) basics++;
            if (ArenaCardCatalog.IsSpecial(card)) specials++;
            if (card >= ArenaCard.LastStand) events++;
        }
        check(ArenaCardCatalog.SlotCount == 8 && ArenaCardCatalog.SpecialSlots == 3 && ArenaCardCatalog.FirstSpecialSlot == 4 &&
            ArenaCardCatalog.EventSlot == 7, "hand layout constants stay at 4 basics, 3 specials and 1 event slot");
        check(basics == 4 && specials == 12 && events == 5, "card ranges resolve to 4 basic, 12 special and 5 event cards");
        check(ArenaCardCatalog.IsSpecial(ArenaCard.Rosin) && ArenaCardCatalog.IsSpecial(ArenaCard.Stridulate) &&
            !ArenaCardCatalog.IsSpecial(ArenaCard.Sidestep) && !ArenaCardCatalog.IsSpecial(ArenaCard.LastStand),
            "special range excludes basic and event cards");
    }

    static void LoadoutRules(Action<bool, string> check)
    {
        check(ArenaCardCatalog.ValidLoadout(Specials), "three distinct specials form a valid loadout");
        check(!ArenaCardCatalog.ValidLoadout(new[] { ArenaCard.Rosin, ArenaCard.Rosin, ArenaCard.Breathe }), "duplicate specials rejected");
        check(!ArenaCardCatalog.ValidLoadout(new[] { ArenaCard.Shove, ArenaCard.Rosin, ArenaCard.Breathe }), "basic cards cannot enter the draft");
        check(!ArenaCardCatalog.ValidLoadout(new[] { ArenaCard.Rosin, ArenaCard.Breathe }), "loadout requires exactly three cards");
        check(!ArenaCardCatalog.ValidLoadout(new[] { ArenaCard.Rosin, ArenaCard.Breathe, ArenaCard.LastStand }), "event cards cannot enter the draft");
    }

    static void HandAndCooldown(Action<bool, string> check, List<ScriptableObject> assets)
    {
        var sim = Exhibition(assets);
        check(sim.Phase == MatchPhase.Countdown, "new match waits in countdown");
        check(sim.EquipCards(Specials), "loadout is accepted during countdown");
        check(!sim.EquipCards(Specials), "loadout cannot be replaced mid-match");
        check(sim.CardAt(0) == ArenaCard.Shove && sim.CardAt(1) == ArenaCard.Probe &&
            sim.CardAt(2) == ArenaCard.Brace && sim.CardAt(3) == ArenaCard.Sidestep,
            "basic slots are Shove, Probe, Brace and Sidestep in that order");
        check(sim.CardAt(4) == ArenaCard.Rosin && sim.CardAt(5) == ArenaCard.Honeydew && sim.CardAt(6) == ArenaCard.Breathe,
            "selected specials occupy slots five to seven");
        check(sim.CardAt(7) == ArenaCard.None, "event slot starts empty");
        check(sim.CardAt(8) == ArenaCard.None && sim.CardAt(-1) == ArenaCard.None, "out-of-range slots resolve to none");
        check(!sim.CanPlayCard(0), "cards cannot be played during countdown");

        ToFighting(sim);
        check(sim.CanPlayCard(1), "probe is playable once the bout starts");
        check(sim.TryPlayCard(1), "probe plays from the hand");
        check(Mathf.Approximately(sim.CardCooldownDuration, ArenaCardCatalog.Get(ArenaCard.Probe).Cooldown) &&
            sim.CardCooldownRemaining > 2.9f, "playing probe starts its own cooldown");
        check(!sim.CanPlayCard(0) && !sim.CanPlayCard(2), "cooldown locks the whole hand");
        // Clock-only advance: the cooldown must follow fight time without depending on a volatile bout.
        AdvanceElapsed(sim, 1.5f);
        check(Mathf.Abs(sim.CardCooldownRemaining - 1.5f) < .05f && !sim.CanPlayCard(0),
            "shared cooldown counts down with fight time (remaining " + sim.CardCooldownRemaining.ToString("0.00") + ")");
        AdvanceElapsed(sim, 1.6f);
        check(sim.CardCooldownRemaining <= 0 && sim.CanPlayCard(0), "hand unlocks when the shared cooldown expires");
        check(!sim.TryPlayCard(7), "empty event slot cannot be played");
    }

    static void PlayGates(Action<bool, string> check, List<ScriptableObject> assets)
    {
        var sim = Exhibition(assets);
        Require(sim.EquipCards(Specials), "loadout");
        ToFighting(sim);
        check(sim.CanPlayCard(4) && sim.CanPlayCard(5) && sim.CanPlayCard(6),
            "instant utility cards are playable while standing");
        sim.Fighters[0].Airborne = true;
        for (int slot = 0; slot < 7; slot++)
            check(!sim.CanPlayCard(slot), "slot " + (slot + 1) + " is unavailable while airborne");
        sim.Fighters[0].Airborne = false;
        check(sim.CanPlayCard(5), "honeydew becomes available again after landing");
        sim.Fighters[0].Action = BeetleAction.Lifting;
        for (int slot = 0; slot < 7; slot++)
            check(!sim.CanPlayCard(slot), "slot " + (slot + 1) + " is unavailable while being lifted");
        sim.Fighters[0].Action = BeetleAction.Assess;
        sim.Fighters[0].Stamina = 0;
        check(!sim.CanPlayCard(0) && !sim.CanPlayCard(1), "attacks require stamina");
        check(sim.CanPlayCard(6), "deep breath does not require stamina");
    }

    static void BasicCardIdentity(Action<bool, string> check, List<ScriptableObject> assets)
    {
        check(BeetleSimulation.ShoveCost > BeetleSimulation.ProbeCost, "probe is the cheaper basic attack");
        check(BeetleSimulation.ProbePower > 0 && BeetleSimulation.ProbePower < 1, "probe hits softer than the full shove");
        check(ArenaCardCatalog.Get(ArenaCard.Probe).Cooldown < ArenaCardCatalog.Get(ArenaCard.Shove).Cooldown,
            "probe recharges faster than the full shove");
        check(ArenaCardCatalog.Get(ArenaCard.Sidestep).Cooldown < ArenaCardCatalog.Get(ArenaCard.Brace).Cooldown,
            "sidestep and brace are separate defensive tempos");

        var sim = Exhibition(assets);
        Require(sim.EquipCards(Specials), "loadout");
        ToFighting(sim);
        var player = sim.Fighters[0];
        player.Position = new Vector2(1.1f, .2f);
        player.Velocity = Vector2.zero;
        player.HasGround = true;
        player.Airborne = false;
        player.HasFallen = false;
        check(sim.TryPlayCard(3), "sidestep plays from the defensive slot");
        check(player.Action == BeetleAction.Circle && player.Velocity.magnitude > 1 &&
            Vector2.Dot(player.Velocity, player.Position) < 0,
            "sidestep is a sideways step that does not carry the player further out");
    }

    // Card code must stay dormant in the standalone duel: a bout without a loadout keeps its normal
    // hand-free behaviour and reaches a result.
    static void WithoutCards(Action<bool, string> check, List<ScriptableObject> assets)
    {
        var sim = Exhibition(assets);
        check(!sim.HasCardLoadout, "a match without a drafted loadout has no hand");
        check(!sim.CanPlayCard(0) && !sim.CanPlayCard(7), "card slots stay inert without a loadout");
        for (int i = 0; i < 12000 && sim.Phase != MatchPhase.Finished; i++)
            sim.Step(BeetleSimulation.FixedStep);
        check(sim.Phase == MatchPhase.Finished, "uncarded bout still reaches a result");
        check(Mathf.Approximately(sim.CardCooldownRemaining, 0), "uncarded bout never starts a card cooldown");
    }

    static void Localisation(Action<bool, string> check)
    {
        CampaignLocalization.Initialize(0);
        check(CampaignLocalization.Text("普通进攻") == "普通进攻", "Chinese locale keeps the Chinese card name");
        CampaignLocalization.Select(1);
        // Sentinel fallback: only a real table value can replace it, so these checks prove the
        // entries exist rather than echoing the authored English back.
        const string missing = "\u0000missing";
        int translated = 0;
        for (var card = ArenaCard.Shove; card <= ArenaCard.Jelly; card++)
        {
            var info = ArenaCardCatalog.Get(card);
            string name = CampaignLocalization.Text(info.Name, missing);
            string description = CampaignLocalization.Text(info.Description, missing);
            check(name == info.EnglishName, card + " name has an English table entry");
            check(description == info.EnglishDescription, card + " description has an English table entry");
            translated++;
        }
        check(translated == 21, "all 21 cards were checked against the table");
        check(CampaignLocalization.Text("战术卡组", missing) == "TACTICAL LOADOUT", "draft title uses the table");
        check(CampaignLocalization.Text("MissingCardTextEntry", "Fallback") == "Fallback",
            "missing entries fall back to authored English");
        check(CampaignLocalization.Text("战术卡 · 母虫信息素", missing) == "Tactic · Pheromone",
            "card play message translates through its pattern and the card name");
        check(CampaignLocalization.Text("全栏冷却   12.0s", missing) == "HAND LOCKED   12.0s",
            "shared cooldown status translates its number");
        check(CampaignLocalization.Text("准备  3", missing) == "Ready 3", "countdown status reuses the existing template");
        check(CampaignLocalization.Text("卡片模式 · 加油停用", missing) == "Cards · cheering disabled",
            "cheer-disabled hint has an English entry");
        check(CampaignLocalization.Text("王者昂首 · 破绽撬飞", missing) == "King's Lift · Opening launch",
            "King's Lift launch message translates");
        check(CampaignLocalization.Text("观众果冻", missing) == "CROWD JELLY", "crowd jelly marker translates");
        check(CampaignLocalization.Text("等待局势触发临场机变卡。", missing) == "Waiting for a combat event.",
            "event slot hint translates");
        check(CampaignLocalization.Text("选择 3 张特殊卡。进攻、防守各 2 张基础牌自动携带，临场机变由战况触发。", missing) ==
            "Choose 3 specials. Two attack and two defense cards are carried automatically. Events unlock from combat.",
            "draft prompt translates");
    }

    static BeetleSimulation Exhibition(List<ScriptableObject> assets)
    {
        var settings = ScriptableObject.CreateInstance<BeetleMatchSettings>();
        assets.Add(settings);
        settings.countdownSeconds = 0;
        settings.Validate();
        return new BeetleSimulation(settings, 4242);
    }

    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    static void ToFighting(BeetleSimulation sim)
    {
        for (int i = 0; i < 500 && sim.Phase != MatchPhase.Fighting; i++)
            sim.Step(BeetleSimulation.FixedStep);
        Require(sim.Phase == MatchPhase.Fighting, "bout did not start");
    }

    // Cooldown checks advance the authoritative fight clock only, so no contact, ring-out or AI
    // decision can shorten the measured window.
    static void AdvanceElapsed(BeetleSimulation sim, float seconds)
    {
        var elapsed = typeof(BeetleSimulation).GetProperty("Elapsed");
        Require(elapsed != null, "simulation clock property missing");
        elapsed.SetValue(sim, (float)elapsed.GetValue(sim) + seconds);
    }
}
