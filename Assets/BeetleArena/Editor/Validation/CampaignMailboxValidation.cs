using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BeetleArena.Campaign;
using BugDuel.Campaign;
using UnityEditor;
using UnityEngine;

public static class CampaignMailboxValidation
{
    static readonly List<string> checks=new List<string>();
    static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);checks.Add("PASS "+message);}
    static void Rejected(CampaignSession s,Action command,string message)
    {
        string before=JsonUtility.ToJson(s.State);bool rejected=false;try{command();}catch(InvalidOperationException){rejected=true;}
        Check(rejected&&before==JsonUtility.ToJson(s.State),message);
    }
    public static void SupplyFood(CampaignSession s,int food)
    {
        s.BuyFood(food);s.ClaimMail(s.State.mail.Last().id);
    }
    [MenuItem("Tools/Beetle Arena/Validate Mailbox Data")]
    public static void Run()
    {
        checks.Clear();var s=CampaignSession.New(CampaignCatalog.Defaults(),3184);s.State.coins=100000;
        Check(s.State.mail.Count==1&&s.UnreadMail==1,"new game has one welcome message");
        var stock=s.State.stock[0];long coins=s.State.coins,cost=CampaignEconomy.Money(s.Asking(stock)*1.1);s.Buy(stock.id);var mail=s.State.mail.Last();
        Check(s.State.coins==coins-cost&&s.State.beetles.Count==2&&s.State.storedBeetles.Count==0&&mail.attachments[0].beetles[0].id==stock.id,"purchase deducts once and delivers only to mail");
        Rejected(s,()=>s.Buy(stock.id),"sold stock cannot be bought twice");
        var copy=CampaignSnapshots.Copy(s.State);copy.mail.Last().attachments[0].beetles[0].genes[0].stars=3;
        Check(mail.attachments[0].beetles[0].genes[0].stars==1,"nested mail attachments are deeply copied");
        while(s.State.beetles.Count<s.State.capacity)s.Add(s.Create("red",40));
        s.ClaimMail(mail.id);Check(s.State.storedBeetles.Single().id==stock.id&&mail.attachments[0].beetles.Count==0,"claim moves live ownership into warehouse even when greenhouse is full");
        Rejected(s,()=>s.ClaimMail(mail.id),"claimed mail cannot duplicate a live beetle");
        Rejected(s,()=>s.HouseStoredBeetle(stock.id),"full greenhouse leaves stored beetle intact");
        s.Upgrade();s.HouseStoredBeetle(stock.id);Check(s.State.storedBeetles.Count==0&&s.State.beetles.Count==7,"storage placement uses greenhouse capacity");
        coins=s.State.coins;s.BuyFood(0);mail=s.State.mail.Last();Check(s.InventoryCount(CampaignItems.Food(0))==0&&s.State.coins==coins-15,"food is unavailable before attachment claim");
        s.DeleteMail(mail.id);Rejected(s,()=>s.PurgeMail(mail.id),"unclaimed attachments cannot be permanently deleted");Rejected(s,()=>s.ClaimMail(mail.id),"deleted mail must be restored before claiming");
        s.RestoreMail(mail.id);s.ClaimMail(mail.id);coins=s.State.coins;
        s.Feed(stock.id,0);Check(s.State.coins==coins&&s.InventoryCount(CampaignItems.Food(0))==0&&s.Beetle(stock.id).meal!=null,"feeding consumes claimed stock without another charge");
        Rejected(s,()=>s.ClaimMail(mail.id),"claimed consumable cannot be duplicated");s.DeleteMail(mail.id);s.PurgeMail(mail.id);Check(!s.State.mail.Contains(mail),"claimed mail can be permanently removed");
        foreach(bool lamp in new[]{false,true})
        {
            s.BuySupply(lamp);Check((lamp?s.State.lamp:s.State.sap)==0,"supplies wait in mailbox");s.ClaimMail(s.State.mail.Last().id);
            Check((lamp?s.State.lamp:s.State.sap)==1,"supplies enter existing expedition inventory on claim");
        }
        s.ExpandCabinet();Check(s.State.cabinet==3&&s.CabinetOrdered,"display upgrade waits for delivery");Rejected(s,()=>s.ExpandCabinet(),"pending display kit cannot be purchased twice");
        s.ClaimMail(s.State.mail.Last().id);s.InstallCabinetUpgrade();Check(s.State.cabinet==6&&s.InventoryCount(CampaignItems.Cabinet)==0,"display kit installs from inventory");
        s.NextWeek();s.StartPupaAuction();Check(s.Reserved==0,"mail-delivered pupa does not reserve greenhouse space");
        s.BidPupa();s.BidPupa();s.BidPupa();mail=s.State.mail.Last();Check(mail.senderId=="auction"&&mail.Pending&&s.State.pupaAuction==null,"auction prize arrives by mail");
        s.ClaimMail(mail.id);s.Validate();
        string folder=Path.GetFullPath("Artifacts/Mailbox/TestSave");var repository=new CampaignSaveRepository(folder);
        s.RenamePlayer("验收玩家");repository.Write(0,new CampaignSave {state=CampaignSnapshots.Copy(s.State),catalog=s.Catalog});var read=repository.Read(0);var loaded=new CampaignSession(read.state,read.catalog);loaded.Validate();
        Check(loaded.State.mail.Last().claimed&&loaded.State.storedBeetles.Single().meal==null,"disk round trip preserves claims and restores absent optional meals");
        Rejected(loaded,()=>loaded.ClaimMail(mail.id),"reload cannot reclaim an attachment");
        var legacy=CampaignSnapshots.Copy(s.State);legacy.mailboxVersion=0;legacy.mail=null;legacy.inventory=null;legacy.storedBeetles=null;
        repository.Write(1,new CampaignSave {state=legacy,catalog=s.Catalog});read=repository.Read(1);
        Check(read.state.mailboxVersion==1&&read.state.mail.Count==1&&read.state.sap==s.State.sap&&read.state.lamp==s.State.lamp&&read.state.beetles.Count==s.State.beetles.Count,"legacy save gains mailbox without losing owned beetles or supplies");
        Directory.CreateDirectory("Artifacts/Mailbox");File.WriteAllLines("Artifacts/Mailbox/Data.txt",checks);Debug.Log("Mailbox data PASS: "+checks.Count);
    }
    public static void RunPlay()
    {
        SessionState.SetBool("BugDuel.MailboxValidation",true);
        try{RetroPresentationValidation.Run();}catch{SessionState.EraseBool("BugDuel.MailboxValidation");throw;}
    }
}
