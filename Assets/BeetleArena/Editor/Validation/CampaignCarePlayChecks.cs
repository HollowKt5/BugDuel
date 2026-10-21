using System;
using System.Collections;
using System.IO;
using BugDuel.Campaign;
using BugDuel.Domain;
using BeetleArena;
using BeetleArena.Campaign;
using UnityEngine;
using Object=UnityEngine.Object;

public static class CampaignCarePlayChecks
{
    static CampaignCareController Controller => Object.FindFirstObjectByType<CampaignCareController>();
    public static IEnumerator Run(CampaignApp app,Action<bool,string> check,Func<string,IEnumerator> capture)
    {
        string id=app.Session.State.selectedId,other=app.Session.State.beetles.Find(b=>b.id!=id).id;
        app.Execute(s=>{s.House(id,"recovery");s.House(other,"recovery");s.Beetle(id).nutrition=40;s.Beetle(id).hearts=4;});yield return Idle(app);
        var resident=Controller.Find(id);
        Vector2 before=resident.Motor.Position;
        yield return Until(()=>resident.Brain.State==CareState.Wandering,"autonomous walking",35);
        // A new random destination may require turning before translation begins.
        yield return Until(()=>Vector2.Distance(before,resident.Motor.Position)>.005f,"walking translation after turn",35);
        check(Vector2.Distance(before,resident.Motor.Position)>.005f,"autonomous walking moves the actual specimen");
        yield return capture("02a-CareWalking");
        long coins=app.Session.State.coins;
        app.Execute(s=>{CampaignMailboxValidation.SupplyFood(s,1);s.Feed(id,1);});yield return Idle(app);
        yield return Until(()=>resident.Brain.State==CareState.Eating,"feeding arrival");
        check(GameObject.Find("CareFood")&&resident.Motor.AtGoal,"food is visible and eating starts only at the food spot");
        yield return Seconds(.35f);yield return capture("02b-CareEating");
        app.Modal=true;yield return null;yield return null;
        double elapsed=resident.Brain.Elapsed;Vector2 position=resident.Motor.Position;
        var rig=Object.FindFirstObjectByType<CampaignSpecimen>().rig;
        Quaternion head=rig.headPivot.localRotation;
        yield return Seconds(.5f);
        check(resident.Brain.Elapsed==elapsed&&resident.Motor.Position==position&&Quaternion.Angle(head,rig.headPivot.localRotation)<.001f,"modal freezes care timers, movement and pose");
        app.Modal=false;app.Select(other);yield return null;
        check(ReferenceEquals(Controller.Find(id),resident),"switching observation preserves the individual brain");
        var housing = app.Session.Catalog.Housing("recovery");
        double previousRisk = housing.accidentPerMinute;
        uint previousRandom = app.Session.State.random;
        housing.accidentPerMinute = .25;
        app.Session.State.random = 1;
        yield return Seconds(1.15f);
        check(!app.Session.Beetle(id).overturned && app.Session.Beetle(other).overturned,
            "live accident tick protects an unselected eating beetle while another beetle can overturn");
        housing.accidentPerMinute = previousRisk;
        app.Execute(s => { s.Rescue(other); s.State.random = previousRandom; });
        yield return Idle(app);
        yield return Until(()=>!app.Busy&&app.Session.Beetle(id).meal==null,"unselected beetle finishes meal");
        check(app.Session.Beetle(id).nutrition==65&&app.Session.State.coins==coins-20,"unselected meal settles exactly once");
        app.Select(id);yield return null;
        check(resident.Brain.State==CareState.Grooming,"actual meal completion enters grooming");
        yield return Seconds(.35f);yield return capture("02c-CareGrooming");
        app.Execute(s=>{CampaignMailboxValidation.SupplyFood(s,2);s.Feed(id,2);});yield return Idle(app);
        yield return Until(()=>resident.Brain.State==CareState.Eating,"second meal starts");
        string mealId=app.Session.Beetle(id).meal.id;
        app.Execute(s=>{s.Beetle(id).overturned=true;s.Beetle(id).overturnSeconds=0;});yield return Idle(app);
        yield return Until(()=>resident.Brain.State==CareState.Struggling,"overturn interruption");
        check(app.Session.Beetle(id).meal.id==mealId&&!resident.Brain.MealReady,"overturn retains uneaten food without nutrition credit");
        yield return Seconds(1);yield return capture("02d-CareStruggling");
        check(app.SaveSlot(3),"save interrupted meal");app.LoadSlot(3);
        yield return Until(()=>!app.Busy&&Controller&&Controller.Find(id)?.Brain.State==CareState.Struggling,"reload interrupted meal");
        resident=Controller.Find(id);
        check(app.Session.Beetle(id).meal.id==mealId&&app.Session.Beetle(id).nutrition==65,"reload preserves food and does not pay or eat twice");
        app.Execute(s=>s.Rescue(id));yield return Idle(app);
        yield return Until(()=>resident.Brain.State==CareState.Recovering,"rescue recovery");
        yield return Seconds(.35f);yield return capture("02e-CareRecovery");
        yield return Until(()=>!app.Busy&&app.Session.Beetle(id).meal==null,"meal resumes after rescue");
        check(app.Session.Beetle(id).nutrition==90,"rescued beetle resumes its paid meal");
        yield return Seconds(4);
        Controller.Disturb(id);yield return null;yield return null;
        check(resident.Brain.State==CareState.Startled,"observation interaction produces a brief startle");
        yield return Seconds(.35f);yield return capture("02f-CareStartled");
        var catalog=JsonUtility.FromJson<CampaignCatalog>(JsonUtility.ToJson(app.Session.Catalog));catalog.revision++;
        catalog.Species(app.Session.Beetle(id).speciesId).behavior=new CareBehaviorProfile { eatSeconds=-1 };
        File.WriteAllText(app.ContentPath,JsonUtility.ToJson(catalog));var previous=app.Session;
        app.ReloadContent();yield return null;
        check(app.Session==previous,"invalid behavior hot update leaves live data unchanged");
        catalog.Species(app.Session.Beetle(id).speciesId).behavior=new CareBehaviorProfile { eatSeconds=2.5,walkSpeed=.31 };
        File.WriteAllText(app.ContentPath,JsonUtility.ToJson(catalog));app.ReloadContent();yield return Idle(app);yield return null;
        check(ReferenceEquals(Controller.Find(id),resident)&&resident.Brain.Profile.eatSeconds==2.5&&resident.Brain.Profile.walkSpeed==.31,"valid hot update changes behavior in place");
        app.Execute(s=>s.Beetle(id).hearts=1);yield return Idle(app);
        yield return Until(()=>resident.Brain.State==CareState.Resting,"injured beetle rests",40);
        yield return capture("02g-CareResting");
        check(resident.Motor.Speed==0,"resting beetle does not slide");
        app.Execute(s=>{s.Beetle(id).hearts=5;var b=s.Create("red",48);b.name="验收甲虫";s.Add(b);});yield return Idle(app);
        string testId=app.Session.State.selectedId;
        app.Execute(s=>{var b=s.Beetle(testId);b.dead=true;b.hearts=0;});yield return Idle(app);yield return null;
        var dead=Controller.Find(testId);rig=Object.FindFirstObjectByType<CampaignSpecimen>().rig;
        var body=rig.bodyPivot.localRotation;var leg=rig.legs[0].hip.localRotation;
        yield return Seconds(.5f);
        check(dead.Brain.State==CareState.Dead&&Quaternion.Angle(body,rig.bodyPivot.localRotation)<.001f&&Quaternion.Angle(leg,rig.legs[0].hip.localRotation)<.001f,"dead beetle freezes without walking, feeding or struggling");
        app.Execute(s=>s.ClearDead(testId));yield return Idle(app);app.Select(id);yield return null;
        check(Controller.Find(testId)==null,"removing an individual also removes its brain");
        check(!rig.enabled,"greenhouse never enables the combat animator");
        check(Object.FindObjectsByType<BeetleMatchController>(FindObjectsSortMode.None).Length==0,"greenhouse behavior requires no battle controller");
        // A fresh food offer remains possible after every interruption and content replacement.
        check(app.Session.Beetle(id).meal==null&&!app.Session.Beetle(id).dead,"care acceptance leaves a usable adult");
        app.DeleteSlot(3); // This temporary slot belongs to the isolated acceptance run.
    }
    static IEnumerator Idle(CampaignApp app) => Until(()=>!app.Busy,"care transaction");
    static IEnumerator Seconds(float seconds)
    { float until=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<until)yield return null; }
    static IEnumerator Until(Func<bool> condition,string name,float timeout=25)
    {
        float until=Time.realtimeSinceStartup+timeout;
        while(!condition()){if(Time.realtimeSinceStartup>until)throw new TimeoutException(name);yield return null;}
        yield return null;yield return null;
    }
}
