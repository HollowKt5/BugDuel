using System;
using System.Collections.Generic;
using System.IO;
using BeetleArena.Campaign;
using BugDuel.Campaign;
using UnityEditor;
using UnityEngine;

public static class CampaignFoodDishValidation
{
    static readonly List<string> checks=new List<string>();

    [MenuItem("Tools/甲壳虫斗/验证/养殖/食盆操作", false, 543)]
    public static void Run()
    {
        checks.Clear();
        string root=Path.GetFullPath("Artifacts/Campaign/Care");
        string folder=Path.Combine(root,"FoodDishSave-"+Guid.NewGuid().ToString("N"));
        var repository=new CampaignSaveRepository(folder);
        try
        {
            DiscardFood();
            CompletionAndSnapshots();
            ResidueHasNoPenalty();
            OwnershipAndAvailability();
            SaveCompatibility(repository);
            LifeCleanup();
            File.WriteAllLines(Path.Combine(root,"FoodDish.txt"),checks);
            Debug.Log("Food dish PASS: "+checks.Count+" contracts.");
        }
        finally
        {
            for(int slot=0;slot<4;slot++)repository.Delete(slot);
            if(Directory.GetFileSystemEntries(folder).Length==0)Directory.Delete(folder);
        }
    }

    static CampaignSession New()
    {
        var s=CampaignSession.New(CampaignCatalog.Defaults(),8713);
        s.RenamePlayer("食盆验收玩家");
        return s;
    }

    static void Supply(CampaignSession s,int food)
    {
        s.BuyFood(food);
        s.ClaimMail(s.State.mail[s.State.mail.Count-1].id);
    }

    static void DiscardFood()
    {
        var s=New();var b=s.Beetle(s.State.selectedId);
        Check(!b.foodResidue,"new beetles start with no food residue");
        b.nutrition=40;b.hearts=3;
        Supply(s,0);long coins=s.State.coins;uint random=s.State.random;
        s.Feed(b.id,0);string mealId=b.meal.id;
        Check(b.hasMeal&&!b.foodResidue&&b.nutrition==40&&b.hearts==3,"placing food does not grant nutrition or create residue");
        s.ClearFoodDish(b.id);
        Check(b.meal==null&&!b.hasMeal&&!b.foodResidue,"clearing discards unfinished food and empties the dish");
        Check(s.State.coins==coins&&s.InventoryCount(CampaignItems.Food(0))==0,"discarded food never refunds coins or returns to inventory");
        Check(b.nutrition==40&&b.hearts==3&&s.State.random==random,"discarding food changes neither nutrition, health nor RNG");
        Check(s.State.notice=="食盆已清理","cleaning reports a successful dish operation");
        Rejected(s,()=>s.CompleteMeal(b.id,mealId),"discarded meal cannot later grant its reward");
        Rejected(s,()=>s.ClearFoodDish(b.id),"clearing an empty dish is rejected without changing state");
        s.Validate();
    }

    static void CompletionAndSnapshots()
    {
        var s=New();var b=s.Beetle(s.State.selectedId);b.nutrition=40;b.hearts=3;
        Supply(s,1);s.Feed(b.id,1);
        var detached=new CampaignSession(CampaignSnapshots.Copy(s.State),s.Catalog);
        var copy=detached.Beetle(b.id);
        Check(!ReferenceEquals(copy,b)&&!ReferenceEquals(copy.meal,b.meal),"pending dish and meal belong to detached snapshot records");
        detached.ClearFoodDish(b.id);
        Check(b.meal!=null&&b.hasMeal&&!b.foodResidue,"clearing a transaction snapshot leaves the original pending dish intact");
        string mealId=b.meal.id;uint random=s.State.random;long coins=s.State.coins;
        s.CompleteMeal(b.id,mealId);
        Check(b.foodResidue&&b.meal==null&&!b.hasMeal,"completed meal leaves residue instead of a pending meal");
        Check(b.nutrition==65&&b.hearts==3.5&&s.State.coins==coins&&s.State.random==random,"meal completion grants only the original food benefit without cost or RNG");
        Rejected(s,()=>s.CompleteMeal(b.id,mealId),"residue cannot be completed as another meal");
        detached=new CampaignSession(CampaignSnapshots.Copy(s.State),s.Catalog);
        Check(detached.Beetle(b.id).foodResidue,"transaction snapshot preserves the residue flag");
        detached.ClearFoodDish(b.id);
        Check(b.foodResidue&&!detached.Beetle(b.id).foodResidue,"cleaning residue in a snapshot leaves the committed beetle unchanged");
        s.ClearFoodDish(b.id);
        Check(!b.foodResidue&&b.nutrition==65&&b.hearts==3.5&&s.State.coins==coins&&s.State.random==random,"cleaning residue grants no rewards and consumes no RNG");
        b.foodResidue=true;Supply(s,2);random=s.State.random;coins=s.State.coins;
        s.Feed(b.id,2);
        Check(b.meal!=null&&b.hasMeal&&b.foodResidue,"uncleared residue does not prevent the next feeding");
        Check(b.nutrition==65&&b.hearts==3.5&&s.State.coins==coins&&s.State.random==random,"feeding over residue has no extra reward, charge or RNG effect");
        b.overturned=true;s.ClearFoodDish(b.id);
        Check(b.overturned&&b.meal==null&&!b.hasMeal&&!b.foodResidue,"cleaning an overturned adult's dish does not rescue or feed it");
        s.Validate();
    }

    static void ResidueHasNoPenalty()
    {
        var clean=New();var dirty=new CampaignSession(CampaignSnapshots.Copy(clean.State),clean.Catalog);
        dirty.Beetle(dirty.State.selectedId).foodResidue=true;
        clean.AdvanceDays(1);dirty.AdvanceDays(1);
        clean.CareSeconds(1);dirty.CareSeconds(1);
        dirty.Beetle(dirty.State.selectedId).foodResidue=false;
        Check(JsonUtility.ToJson(clean.State)==JsonUtility.ToJson(dirty.State),"residue alone changes no aging, nutrition, health, accident or RNG outcomes");
    }

    static void OwnershipAndAvailability()
    {
        var s=New();
        Rejected(s,()=>s.ClearFoodDish("missing"),"unknown beetle cannot have its dish cleaned");
        var stock=s.State.stock[0];stock.foodResidue=true;
        Rejected(s,()=>s.ClearFoodDish(stock.id),"market beetle is not an owned dish target");
        var stored=s.Create("red",45);stored.foodResidue=true;s.State.storedBeetles.Add(stored);
        Rejected(s,()=>s.ClearFoodDish(stored.id),"stored beetle has no active terrarium dish");
        RejectUnavailable(b=>b.listed=true,"listed beetle's dish cannot be changed");
        RejectUnavailable(b=>b.dead=true,"dead beetle's dish cannot be changed");
        RejectUnavailable(b=>b.stage=CampaignLifeStage.Larva3,"immature beetles cannot use adult food dishes");
        s=New();var b=s.Beetle(s.State.selectedId);b.foodResidue=true;
        s.State.battle=new PendingBattle {beetleId=b.id};
        Rejected(s,()=>s.ClearFoodDish(b.id),"battle-reserved beetle's dish cannot be changed");
        s=New();b=s.Beetle(s.State.selectedId);b.foodResidue=true;
        s.State.auction=new AuctionState {beetleId=b.id};
        Rejected(s,()=>s.ClearFoodDish(b.id),"auction-reserved beetle's dish cannot be changed");
    }

    static void RejectUnavailable(Action<CampaignBeetle> prepare,string message)
    {
        var s=New();var b=s.Beetle(s.State.selectedId);b.foodResidue=true;prepare(b);
        Rejected(s,()=>s.ClearFoodDish(b.id),message);
    }

    static void SaveCompatibility(CampaignSaveRepository repository)
    {
        var s=New();var b=s.Beetle(s.State.selectedId);
        Supply(s,0);s.Feed(b.id,0);s.CompleteMeal(b.id,b.meal.id);
        repository.Write(0,new CampaignSave {state=CampaignSnapshots.Copy(s.State),catalog=s.Catalog});
        var saved=repository.Read(0);var loaded=new CampaignSession(saved.state,saved.catalog);var restored=loaded.Beetle(b.id);
        Check(restored.foodResidue&&restored.meal==null&&!restored.hasMeal,"completed meal residue survives the real save repository round trip");
        loaded.ClearFoodDish(b.id);
        repository.Write(0,new CampaignSave {state=CampaignSnapshots.Copy(loaded.State),catalog=loaded.Catalog});
        saved=repository.Read(0);restored=saved.state.beetles[0];
        Check(!restored.foodResidue&&restored.meal==null&&!restored.hasMeal,"cleaned dish stays empty after save and reload");
        Supply(s,1);s.Feed(b.id,1);string mealId=b.meal.id;
        repository.Write(1,new CampaignSave {state=CampaignSnapshots.Copy(s.State),catalog=s.Catalog});
        saved=repository.Read(1);restored=saved.state.beetles[0];
        Check(restored.foodResidue&&restored.hasMeal&&restored.meal.id==mealId,"pending food and prior residue both survive save and reload");
        string legacy=JsonUtility.ToJson(new CampaignSave {state=CampaignSnapshots.Copy(s.State),catalog=s.Catalog})
            .Replace("\"foodResidue\":false,","").Replace("\"foodResidue\":true,","");
        Check(!legacy.Contains("foodResidue"),"legacy fixture contains no residue field");
        CampaignSaveRepository.AtomicWrite(repository.SlotPath(2),legacy);
        saved=repository.Read(2);restored=saved.state.beetles[0];
        Check(!restored.foodResidue&&restored.hasMeal&&restored.meal.id==mealId,"old save defaults residue to false while preserving offered food");
        restored=saved.state.beetles[1];
        Check(!restored.foodResidue&&restored.meal==null&&!restored.hasMeal,"old save with empty dish stays clean and has no phantom food");
    }

    static void LifeCleanup()
    {
        var s=New();var b=s.Beetle(s.State.selectedId);
        Supply(s,0);s.Feed(b.id,0);b.foodResidue=true;b.overturned=true;b.overturnSeconds=60;b.hearts=.01;
        s.CareSeconds(1);
        Check(b.dead&&b.meal==null&&!b.hasMeal&&!b.foodResidue,"death clears food and residue along with the existing meal state");
        s=New();b=s.Beetle(s.State.selectedId);b.dead=true;b.hearts=0;b.foodResidue=true;
        b.meal=new CampaignMeal {id="discard-on-preserve",food=0};b.hasMeal=true;
        string specimenId=s.PreserveDead(b.id,"第 1 周");
        var specimen=s.State.specimens.Find(p=>p.id==specimenId);
        Check(specimen!=null&&specimen.beetle.meal==null&&!specimen.beetle.hasMeal&&!specimen.beetle.foodResidue,"preserved specimen does not retain live feeding state");
        s.Validate();
    }

    static void Rejected(CampaignSession s,Action command,string message)
    {
        string before=JsonUtility.ToJson(s.State);bool rejected=false;
        try { command(); } catch(CampaignRuleException) { rejected=true; }
        Check(rejected&&before==JsonUtility.ToJson(s.State),message);
    }

    static void Check(bool ok,string message)
    {
        if(!ok)throw new InvalidOperationException("Food dish contract failed: "+message);
        checks.Add("PASS "+message);
    }
}
