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

public static class CampaignPlayerIdentityValidation
{
    static readonly List<string> checks=new List<string>();
    static void Check(bool value,string message)
    {
        if(!value)throw new InvalidOperationException("Player identity contract failed: "+message);
        checks.Add("PASS "+message);
    }
    static string Plain(string value)=>CampaignDisplayText.Localize(value,text=>text);
    static CampaignSession Session()
    {var session=CampaignSession.New(CampaignCatalog.Defaults(),51428);session.State.coins=100000;return session;}
    static void Reject(CampaignSession session,string value,string message)
    {
        string name=session.State.playerName,notice=session.State.notice;long coins=session.State.coins;uint random=session.State.random;
        bool refused=false;try{session.RenamePlayer(value);}catch(CampaignRuleException){refused=true;}
        Check(refused&&session.State.playerName==name&&session.State.notice==notice&&session.State.coins==coins&&session.State.random==random,message);
    }

    public static IReadOnlyList<string> RunDomain()
    {
        checks.Clear();var session=Session();
        Check(session.NeedsPlayerName&&string.IsNullOrEmpty(session.State.playerName),"unnamed sessions require a player-supplied identity instead of receiving an invented default");
        session.RenamePlayer("  林间饲养员  ");
        Check(!session.NeedsPlayerName&&session.State.playerName=="林间饲养员"&&Plain(session.PlayerSignature)=="林间饲养员","Chinese names are trimmed and explicitly protected for display");
        session.RenamePlayer("  Oak Keeper  ");Check(session.State.playerName=="Oak Keeper","Latin names retain their internal spaces");
        session.RenamePlayer(new string('甲',24));Check(session.State.playerName.Length==24,"the 24-character boundary is accepted");
        Reject(session,null,"null names are rejected without mutation");Reject(session,"   ","blank names are rejected without mutation");
        Reject(session,new string('甲',25),"overlong names are rejected without mutation");
        Reject(session,"林\n间","embedded newlines are rejected without mutation");Reject(session,"林\t间","embedded control characters are rejected without mutation");
        Reject(session,"<b>林间</b>","rich-text delimiters are rejected without mutation");

        session.RenamePlayer("死亡");
        var mail=new CampaignMail {id="identity-mail",senderId="keeper",subject="验收邮件",body="死亡",week=session.State.week};
        string localized=CampaignDisplayText.Localize(session.MailBody(mail),text=>text.Replace("死亡","Dead").Replace("你好","Hello"));
        Check(localized.StartsWith("死亡，Hello：",StringComparison.Ordinal)&&localized.EndsWith("Dead",StringComparison.Ordinal)&&mail.body=="死亡",
            "a name matching a system word remains literal while surrounding mail prose translates");
        session.State.mail.Add(mail);
        session.RenamePlayer("琥珀收藏家");
        var playerLot=session.State.marketAuctions[0];var npcLot=session.State.marketAuctions[1];
        session.BidMarketAuction(playerLot.id,playerLot.expectedPrice);
        npcLot.highBidder="琥珀收藏家";npcLot.currentBid=npcLot.expectedPrice;npcLot.highBidderIsPlayer=false;
        Check(playerLot.highBidderIsPlayer&&Plain(session.AuctionBidderName(playerLot))=="琥珀收藏家"&&!npcLot.highBidderIsPlayer,
            "player and NPC ownership remain distinct even when their visible names match");
        long funds=session.State.coins,escrow=playerLot.playerBid;uint seed=session.State.random;
        string selected=session.State.selectedId;var beetles=session.State.beetles.Select(b=>b.id).ToArray();
        var stock=session.State.stock.Select(b=>b.id).ToArray();var inventory=session.State.inventory.Select(item=>(item.itemId,item.quantity)).ToArray();
        var lotIds=session.State.marketAuctions.Select(lot=>lot.id).ToArray();
        session.RenamePlayer("New Keeper");
        Check(session.State.coins==funds&&session.State.random==seed&&session.State.selectedId==selected&&playerLot.playerBid==escrow&&
            session.State.beetles.Select(b=>b.id).SequenceEqual(beetles)&&session.State.stock.Select(b=>b.id).SequenceEqual(stock)&&
            session.State.inventory.Select(item=>(item.itemId,item.quantity)).SequenceEqual(inventory)&&session.State.marketAuctions.Select(lot=>lot.id).SequenceEqual(lotIds),
            "renaming preserves funds, escrow, selected ownership, inventory, stock, lots and gameplay random state");
        Check(playerLot.highBidder=="New Keeper"&&Plain(session.AuctionBidderName(playerLot))=="New Keeper"&&npcLot.highBidder=="琥珀收藏家"&&session.AuctionBidderName(npcLot)=="琥珀收藏家",
            "only explicitly player-owned bids receive the new signature");
        Check(Plain(session.MailBody(mail)).StartsWith("New Keeper，你好：",StringComparison.Ordinal)&&mail.body=="死亡","existing mail greetings use the current player name without rewriting original content");
        var snapshot=CampaignSnapshots.Copy(session.State);var detached=new CampaignSession(snapshot,session.Catalog);detached.Validate();
        Check(detached.State.playerName=="New Keeper"&&detached.State.playerIdentityVersion==1&&detached.State.marketAuctions[0].highBidderIsPlayer&&!detached.State.marketAuctions[1].highBidderIsPlayer,
            "transaction snapshots preserve player identity and explicit auction ownership");
        detached.RenamePlayer("Snapshot Only");Check(session.State.playerName=="New Keeper"&&playerLot.highBidder=="New Keeper","detached renames cannot alter the active identity or bid signature");
        while(!playerLot.closed)session.AdvanceMarketAuction(playerLot.id);
        Check(playerLot.won&&playerLot.highBidderIsPlayer&&playerLot.playerBid==0&&session.State.mail.Last().attachments.Count>0,
            "renaming an escrowed winning bid still delivers its original lot exactly once");session.Validate();

        var legacy=CampaignSnapshots.Copy(session.State);legacy.playerIdentityVersion=0;legacy.playerName=null;
        legacy.marketAuctions[0].highBidderIsPlayer=false;
        legacy.marketAuctions[1].highBidder="你";legacy.marketAuctions[1].highBidderIsPlayer=false;
        legacy.marketAuctions[2].currentBid=legacy.marketAuctions[2].playerBid=legacy.marketAuctions[2].expectedPrice;legacy.marketAuctions[2].highBidder="Old signature";legacy.marketAuctions[2].highBidderIsPlayer=false;
        var upgraded=new CampaignSession(legacy,session.Catalog);
        Check(upgraded.NeedsPlayerName&&legacy.playerIdentityVersion==1&&legacy.marketAuctions.All(lot=>lot.highBidderIsPlayer),
            "legacy won, explicit-you and active-escrow bids migrate without assigning a player name");
        upgraded.RenamePlayer("补名玩家");upgraded.Validate();
        Check(legacy.marketAuctions.All(lot=>lot.highBidder=="补名玩家")&&!upgraded.NeedsPlayerName,"supplying a legacy name updates migrated player signatures and validates the save");
        var modern=CampaignSnapshots.Copy(session.State);modern.playerName="你";modern.marketAuctions[1].highBidder="你";modern.marketAuctions[1].highBidderIsPlayer=false;
        var reopened=new CampaignSession(modern,session.Catalog);reopened.RenamePlayer("再更名");
        Check(!modern.marketAuctions[1].highBidderIsPlayer&&modern.marketAuctions[1].highBidder=="你","current-version saves never infer NPC ownership from a coincidentally matching name");
        return checks;
    }

#if UNITY_EDITOR
    [MenuItem("Tools/甲壳虫斗/验证/战役流程/玩家身份数据", false, 570)]
    public static void Run()
    {
        RunDomain();var session=Session();session.RenamePlayer("归档饲养员");var lot=session.State.marketAuctions[0];
        session.BidMarketAuction(lot.id,lot.expectedPrice);
        string folder=Path.GetFullPath("Artifacts/PlayerIdentity/Data/Save-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        var repository=new CampaignSaveRepository(folder);repository.Write(0,new CampaignSave {state=CampaignSnapshots.Copy(session.State),catalog=session.Catalog});
        var save=repository.Read(0);var loaded=new CampaignSession(save.state,save.catalog);loaded.Validate();
        Check(loaded.State.playerName=="归档饲养员"&&!loaded.NeedsPlayerName&&loaded.State.marketAuctions[0].highBidderIsPlayer&&loaded.State.marketAuctions[0].playerBid==lot.playerBid,
            "real JSON save reload preserves Chinese identity, player ownership and escrow");
        loaded.RenamePlayer("Saved Keeper");repository.Write(1,new CampaignSave {state=loaded.State,catalog=loaded.Catalog});
        var second=repository.Read(1);
        Check(second.state.playerName=="Saved Keeper"&&second.state.marketAuctions[0].highBidder=="Saved Keeper"&&repository.Read(0).state.playerName=="归档饲养员",
            "renaming and saving another slot preserves independent stored identities");
        Directory.CreateDirectory("Artifacts/PlayerIdentity/Data");File.WriteAllLines("Artifacts/PlayerIdentity/Data/Result.txt",checks);
        Debug.Log("Player identity data PASS: "+checks.Count+" contracts");
    }
#endif
}
