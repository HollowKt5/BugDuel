using System;
using System.IO;
using System.Linq;
using System.Text;
using BeetleArena;
using UnityEngine;
public static class BeetleLiftCatchValidation
{
    public static void CatchSteps()
    {
        Directory.CreateDirectory("Artifacts/LiftCatch");
        using var fixture = new BeetleValidationScene("CatchStepsValidation");
        var m=fixture.Match; m.SelectArena(0);m.StartMatch(71);
        while(m.Simulation.Phase==MatchPhase.Countdown)m.AdvanceFixedStep();
        var sim=m.Simulation;var a=sim.Fighters[0];var b=sim.Fighters[1];
        Directory.CreateDirectory("Artifacts/LiftCatch/Frames"); m.hud.gameObject.SetActive(false);
        a.Position=new Vector2(-3.5f,0);b.Position=new Vector2(1.5f,0);b.Heading=-90;
        foreach(var f in sim.Fighters){f.Motion.Cancel();f.Action=BeetleAction.Brace;f.ActionTime=0;f.ActionDuration=10;f.DecisionTime=10;}
        sim.SetGround(m.ActiveSurface);for(int i=0;i<2;i++)m.actors[i].Bind(sim.Fighters[i],m.settings,m.ActiveSurface);
        m.Contacts.Solve(sim,0);b.Velocity=new Vector2(5,0);b.ImpactVisual=1;b.Balance=.25f;
        var anim=m.actors[1].animator; var last=new Quaternion[6]; for(int i=0;i<6;i++)last[i]=anim.importedRig.bindings[i].knee.localRotation;
        int steps=0,hindConflict=0;float motion=0,travel=0,overlap=0;var start=b.Position;
        for(int tick=0;tick<100;tick++)
        {
            m.AdvanceFixedStep();m.Present(1,sim.MotionTime,.02f);
            if(!b.Airborne&&!b.HasFallen)
            {
                for(int i=0;i<6;i++)
                {if(anim.legs[i].stepping)steps++;var now=anim.importedRig.bindings[i].knee.localRotation;motion+=Quaternion.Angle(last[i],now);last[i]=now;}
                if(anim.legs[2].stepping&&anim.legs[5].stepping)hindConflict++;
            }
            travel=Mathf.Max(travel,Vector2.Distance(start,b.Position));overlap=Mathf.Max(overlap,m.Contacts.MeasurePenetration());
            if(tick%3==0)
            {
                var camera=m.spectatorCamera.GetComponent<Camera>();
                Vector3 focus=m.actors[1].transform.position+Vector3.up*.5f;
                camera.orthographicSize=3.2f;camera.transform.position=focus+new Vector3(0,5,-8);camera.transform.LookAt(focus);
                Shot(m,$"Artifacts/LiftCatch/Frames/Catch-{tick/3:D3}.png");
            }
        }
        string report=$"CATCH STEPS actual rig: step samples={steps}, knee travel={motion:F2} degrees, root travel={travel:F3}, hind conflicts={hindConflict}, overlap={overlap:F6}\n";
        File.WriteAllText("Artifacts/LiftCatch/Recovery.txt",report);
        if(steps==0||motion<20||travel<.2f||hindConflict!=0||overlap>.006f)throw new Exception("Sliding recovery failed: "+report);
    }
    public static void Shot(BeetleMatchController m,string path)
    {
        var camera=m.spectatorCamera.GetComponent<Camera>();var old=camera.targetTexture;var active=RenderTexture.active;
        var objects=m.gameObject.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Select(t=>t.gameObject).ToArray();
        var layers=objects.Select(o=>o.layer).ToArray();int mask=camera.cullingMask;
        var rt=RenderTexture.GetTemporary(800,450,24);var pixels=new Texture2D(800,450,TextureFormat.RGB24,false);
        try{foreach(var obj in objects)obj.layer=31;camera.cullingMask=1<<31;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,800,450),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());}
        finally{camera.cullingMask=mask;for(int i=0;i<objects.Length;i++)objects[i].layer=layers[i];camera.targetTexture=old;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(pixels);}
    }
    public static void CompareReplay()
    {
        using var fixture=new BeetleValidationScene("ReplayComparison");var m=fixture.Match;m.SelectArena(0);
        var report=new StringBuilder($"inspection={m.settings.throwInspectionMode}, gain={m.settings.throwHeavySpeedMultiplier}, pry={m.settings.pryImpulse}\n");
        Vector3[] baseline=new Vector3[3000]; int baselineTicks=0;
        for(int run=0;run<3;run++)
        {
            m.StartMatch(71);int first=-1,steps=0;
            for(int tick=0;tick<1500 && m.Simulation.Phase!=MatchPhase.Finished;tick++)
            {
                m.AdvanceFixedStep();var sim=m.Simulation;steps++;
                for(int i=0;i<2;i++)
                {
                    var b=sim.Fighters[i];var p=new Vector3(b.Position.x,b.Height,b.Position.y);
                    if(run==0)baseline[tick*2+i]=p;
                    else if(first<0&&tick<baselineTicks&&Vector3.Distance(p,baseline[tick*2+i])>.0001f)first=tick;
                }
                if(run==1)m.Present(1,sim.MotionTime,.02f);
            }
            if(run==0)baselineTicks=steps;
            report.AppendLine($"run={run} renders={(run==1?1:0)} ticks={steps} firstDifference={first} elapsed={m.Simulation.Elapsed:F2} throws={m.Simulation.Fighters.Sum(b=>b.Throws)} pries={m.Simulation.Fighters.Sum(b=>b.Pries)}");
            File.WriteAllText("Artifacts/LiftCatch/Comparison.txt",report.ToString());
        }
    }
    public static void Diagnose()
    {
        Directory.CreateDirectory("Artifacts/LiftCatch");
        using var fixture = new BeetleValidationScene("LiftCatchValidation");
        var m=fixture.Match; m.SelectArena(0); m.StartMatch(71);
        var sim=m.Simulation; while(sim.Phase==MatchPhase.Countdown)m.AdvanceFixedStep();
        var a=sim.Fighters[0];var b=sim.Fighters[1];
        a.Position=new Vector2(-2.05f,0);b.Position=new Vector2(2.05f,0);a.Heading=90;b.Heading=-90;
        a.Action=BeetleAction.PryWindup;a.ActionTime=0;a.ActionDuration=.55f;
        b.Action=BeetleAction.Brace;b.ActionTime=0;b.ActionDuration=10;b.Balance=.3f;b.Stamina=35;
        sim.SetGround(m.ActiveSurface);for(int i=0;i<2;i++)m.actors[i].Bind(sim.Fighters[i],m.settings,m.ActiveSurface);
        m.Contacts.Solve(sim,0);
        var output=new StringBuilder("time,a,b,dist,head,pries,throws,hornContact,bestGap,pY,qY,centerY,pair,blocked,separation\n");
        var ra=m.actors[0].GetComponent<BeetleContactRig>();var rb=m.actors[1].GetComponent<BeetleContactRig>();
        for(int tick=0;tick<180;tick++)
        {
            m.AdvanceFixedStep();if(tick%5!=0)continue;
            ra.CacheBounds();rb.CacheBounds();float best=100,py=0,qy=0,cy=0;string pair="";
            for(int i=0;i<ra.shapes.Length;i++)if(ra.Role(i)==BeetleContactRole.HeadHorn)
            for(int j=0;j<rb.shapes.Length;j++)if(rb.Role(j)==BeetleContactRole.Body && rb.shapes[j].name!="Thoracic horn")
            {var x=ra.shapes[i];var y=rb.shapes[j];var q=y.ClosestPoint(x.bounds.center);var p=x.ClosestPoint(q);q=y.ClosestPoint(p);float gap=Vector3.Distance(p,q);
             if(gap<best){best=gap;py=p.y;qy=q.y;cy=y.bounds.center.y;pair=x.name+"/"+y.name;}}
            output.AppendLine($"{tick*.02f:F2},{a.Action},{b.Action},{Vector2.Distance(a.Position,b.Position):F3},{Mathf.DeltaAngle(0,m.actors[0].animator.headPivot.localEulerAngles.x):F1},{a.Pries},{a.Throws},{m.Contacts.HasMotionContact(0,BeetleMotionContact.Horn,0)},{best:F3},{py:F3},{qy:F3},{cy:F3},{pair},{a.ContactBlockedSeconds:F2},{m.Contacts.LastSeparationPair}");
        }
        File.WriteAllText("Artifacts/LiftCatch/Diagnosis.csv",output.ToString());
    }
}
