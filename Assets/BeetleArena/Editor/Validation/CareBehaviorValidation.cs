using System;
using System.Collections.Generic;
using System.IO;
using BugDuel.Domain;
using BugDuel.Campaign;
using BeetleArena;
using BeetleArena.Campaign;
using UnityEditor;
using UnityEngine;

public static class CareBehaviorValidation
{
    static readonly List<string> checks=new List<string>();
    [MenuItem("Tools/甲壳虫斗/验证/养殖/自然行为", false, 542)]
    public static void Run()
    {
        checks.Clear();
        var p=new CareBehaviorProfile();var brain=new CareStateMachine(p,123);
        var o=new CareObservation { Nutrition=80,HealthFraction=1,Hour=21,HabitatActivity=1,RestPreference=.5 };
        var seen=new HashSet<CareState>{brain.State};
        for(int i=0;i<18000;i++) { o.AtGoal=i%40==0;brain.Step(.05,o);seen.Add(brain.State); }
        foreach(var state in new[]{CareState.Idle,CareState.Wandering,CareState.Inspecting,CareState.Grooming,CareState.SeekingShelter,CareState.Resting})
            Check(seen.Contains(state),"autonomous cycle reaches "+state);
        brain=new CareStateMachine(p,123);o.FoodAvailable=true;o.AtGoal=true;brain.Step(.05,o);
        Check(brain.State==CareState.SeekingFood,"new food cannot reuse a previous goal's arrival");
        o.AtGoal=false;for(int i=0;i<200;i++)brain.Step(.05,o);
        Check(brain.State==CareState.SeekingFood&&!brain.MealReady,"no eating before actual arrival");
        o.AtGoal=true;brain.Step(.05,o);Check(brain.State==CareState.Eating,"arrival starts eating");
        brain.Step(.5,o);double elapsed=brain.Elapsed;o.Paused=true;
        for(int i=0;i<200;i++)brain.Step(.05,o);
        Check(brain.Elapsed==elapsed&&!brain.MealReady,"pause freezes feeding progress");o.Paused=false;
        o.Overturned=true;brain.Step(.05,o);Check(brain.State==CareState.Struggling&&!brain.MealReady,"overturn interrupts eating");
        o.Overturned=false;brain.Step(.05,o);Check(brain.State==CareState.Recovering,"rescue enters recovery before food");
        for(int i=0;i<160;i++)brain.Step(.05,o);
        Check(brain.MealReady,"meal finishes after recovery and a new uninterrupted eating period");
        o.FoodAvailable=false;brain.Step(.05,o);Check(brain.State==CareState.Grooming,"completed meal leads to grooming");
        for(int i=0;i<100;i++)brain.Step(.05,o);brain.Disturb();brain.Step(.05,o);
        Check(brain.State==CareState.Startled,"nearby interaction causes startle");
        for(int i=0;i<45;i++){brain.Disturb();brain.Step(.05,o);}
        Check(brain.State!=CareState.Startled,"repeated clicks cannot indefinitely restart startle");
        o.Unavailable=true;brain.Step(.05,o);Check(brain.State==CareState.Unavailable,"reserved beetle leaves active care");
        o.Unavailable=false;o.Growing=true;brain.Step(.05,o);Check(brain.State==CareState.Growing,"immature beetles do not roam or eat adult meals");
        o.Dead=true;o.Overturned=true;brain.Step(.05,o);Check(brain.State==CareState.Dead&&!brain.MealReady,"death takes priority over struggle");
        o=new CareObservation{Nutrition=80,HealthFraction=.2,Hour=12,HabitatActivity=1};
        brain=new CareStateMachine(p,42);brain.Step(.05,o);Check(brain.State==CareState.SeekingShelter,"injured beetle seeks a resting spot");
        var motor=new CareLocomotion(42,new CareWalkArea(new Rect(-2,-2,4,4),.8f));int steps=0;
        while(brain.State!=CareState.Resting&&steps++<1000){o.AtGoal=motor.AtGoal;brain.Step(.05,o);motor.Tick(brain,.05f);}
        Check(brain.State==CareState.Resting,"bounded motor reaches shelter");
        o.HealthFraction=1;o.FoodAvailable=true;
        for(int i=0;i<500;i++){o.AtGoal=motor.AtGoal;brain.Step(.05,o);motor.Tick(brain,.05f);}
        Check(brain.MealReady&&Vector2.Distance(motor.Position,motor.FeedingPoint)<.03f,"motor reaches and faces food, then stops");
        Check(motor.Speed==0,"eating cannot slide");
        bool rejected=false;try{new CareBehaviorProfile{eatSeconds=double.NaN}.Validate();}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"nonfinite hot-update parameters rejected");
        var changed=new CareBehaviorProfile{eatSeconds=12};brain.Configure(changed);Check(!double.IsNaN(brain.MealProgress)&&brain.Profile==changed,"profile replacement keeps the same brain");
        Check(typeof(CareStateMachine).Assembly.GetReferencedAssemblies().Length>0&&
            Array.TrueForAll(typeof(CareStateMachine).Assembly.GetReferencedAssemblies(),a=>!a.Name.StartsWith("Unity")&&!a.Name.StartsWith("BeetleArena")),"care domain has no engine or battle assembly dependency");
        Meals();
        EatingProtection();
        Directory.CreateDirectory("Artifacts/Campaign/Care");
        File.WriteAllLines("Artifacts/Campaign/Care/Contracts.txt",checks);
        Debug.Log("Husbandry behavior PASS: "+checks.Count+" contracts.");
    }
    static void Meals()
    {
        var catalog=JsonUtility.FromJson<CampaignCatalog>(File.ReadAllText("Assets/StreamingAssets/campaign-content.json"));catalog.Validate();
        Check(true,"shipped/legacy catalog without behavior fields loads");
        var s=CampaignSession.New(catalog,31);var b=s.Beetle(s.State.selectedId);b.nutrition=40;b.hearts=3;long coins=s.State.coins;uint random=s.State.random;
        CampaignMailboxValidation.SupplyFood(s,0);s.Feed(b.id,0);string meal=b.meal.id;
        Check(s.State.coins==coins-15&&b.nutrition==40&&b.hearts==3,"placing food pays once and does not grant instant nutrition");
        bool rejected=false;try{s.Feed(b.id,1);}catch(InvalidOperationException){rejected=true;}
        Check(rejected&&s.State.coins==coins-15&&b.meal.id==meal,"duplicate food neither overwrites nor charges");
        var copy=CampaignSnapshots.Copy(s.State);copy.beetles[0].meal.food=2;
        Check(b.meal.food==0,"pending meals are deeply copied in transactions");
        var restored=JsonUtility.FromJson<CampaignState>(JsonUtility.ToJson(s.State));
        Check(restored.beetles[0].meal.id==meal,"pending food survives save/load");
        b.overturned=true;rejected=false;try{s.CompleteMeal(b.id,meal);}catch(InvalidOperationException){rejected=true;}
        Check(rejected&&b.meal!=null&&b.nutrition==40,"overturned beetle cannot settle food");
        b.overturned=false;s.CompleteMeal(b.id,meal);
        Check(b.meal==null&&b.nutrition==75&&b.hearts==3.5&&s.State.coins==coins-15,"one meal grants its exact benefit once");
        rejected=false;try{s.CompleteMeal(b.id,meal);}catch(InvalidOperationException){rejected=true;}
        Check(rejected&&b.nutrition==75,"stale completion cannot grant food twice");
        Check(s.State.random==random,"food and behavior do not perturb economy/combat RNG");
        CampaignMailboxValidation.SupplyFood(s,1);s.Feed(b.id,1);s.NextWeek();Check(b.meal==null,"week advance resolves offered food once");
        s.Validate();
        Directory.CreateDirectory("Artifacts/Campaign/Care/Serialization");
        var repository=new CampaignSaveRepository(Path.GetFullPath("Artifacts/Campaign/Care/Serialization"));
        s.RenamePlayer("验收玩家");repository.Write(0,new CampaignSave { state=s.State,catalog=catalog });
        var disk=repository.Read(0);
        Check(disk.state.beetles[0].meal==null&&!disk.state.beetles[0].hasMeal,"save/load preserves an empty dish instead of a phantom meal");
        var legacy=JsonUtility.FromJson<CampaignCatalog>(File.ReadAllText("Assets/StreamingAssets/campaign-content.json"));
        foreach(var rule in legacy.species)rule.behavior=null;
        foreach(var rule in legacy.housing)rule.behavior=null;
        var old=JsonUtility.FromJson<CampaignCatalog>(JsonUtility.ToJson(legacy));old.Validate();
        Check(CareBehaviorProfile.Resolve(old.species[0].behavior).eatSeconds>0,"legacy nullable profiles survive another serialization round trip");
    }
    static void EatingProtection()
    {
        var s = CampaignSession.New(CampaignCatalog.Defaults(), 31);
        var eating = s.State.beetles[0];
        var walking = s.State.beetles[1];
        s.Catalog.Housing(eating.habitat).accidentPerMinute = .25;
        CampaignMailboxValidation.SupplyFood(s,0);s.Feed(eating.id, 0);
        // Seed 1 makes the first one-second accident draw succeed deterministically.
        s.State.random = 1;
        s.CareSeconds(1, id => id == eating.id);
        Check(!eating.overturned && walking.overturned, "eating immunity is per individual and leaves other accidents active");
        s.State.random = 1;
        s.CareSeconds(1, id => false);
        Check(eating.overturned, "offered food without active eating does not prevent overturning");
        eating.overturnSeconds = 60;
        double hearts = eating.hearts;
        s.CareSeconds(24, id => true);
        Check(eating.hearts == hearts - 1, "eating protection does not stop damage to an already overturned beetle");
        s.Rescue(eating.id);
        s.CompleteMeal(eating.id, eating.meal.id);
        s.State.random = 1;
        s.CareSeconds(1, id => true);
        Check(eating.overturned, "meal completion immediately ends immunity even before the host refreshes its state");
    }
    static void Check(bool ok,string name)
    { if(!ok)throw new InvalidOperationException("Care contract failed: "+name);checks.Add("PASS "+name); }
}
