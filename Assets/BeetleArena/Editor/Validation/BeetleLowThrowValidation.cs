using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using BeetleArena;
using UnityEngine;

public static class BeetleLowThrowValidation
{
    public static void Run(bool verify=false)
    {
        using var fixture=new BeetleValidationScene("LowThrowValidation");
        var match=fixture.Match;var settings=fixture.Settings;float originalForce=settings.throwMuscleForce;
        string folder="Artifacts/LowThrow/"+(verify?"After":"Before");Directory.CreateDirectory(folder);
        var report=new StringBuilder("LOW THROW CONTACT/FLIGHT DIAGNOSTIC\n");
        bool passed=true;
        foreach(int seed in new[]{71,2731,7319,9633})for(int arena=0;arena<2;arena++)for(int variant=0;variant<2;variant++)
        {
            settings.throwInspectionMode=false;settings.tossChance=0;settings.throwMuscleForce=originalForce*(variant==0?.3f:.6f);
            match.SelectArena(arena);match.StartMatch(seed);var sim=match.Simulation;
            while(sim.Phase==MatchPhase.Countdown)match.AdvanceFixedStep();
            var a=sim.Fighters[0];var b=sim.Fighters[1];
            a.Position=new Vector2(-2.05f,0);b.Position=new Vector2(2.05f,0);a.Heading=90;b.Heading=-90;
            a.Action=BeetleAction.PryWindup;a.ActionTime=0;a.ActionDuration=.55f;
            b.Action=BeetleAction.Brace;b.ActionDuration=10;b.Balance=.3f;b.Stamina=35;
            sim.SetGround(match.ActiveSurface);for(int i=0;i<2;i++)match.actors[i].Bind(sim.Fighters[i],settings,match.ActiveSurface);
            match.Contacts.Solve(sim,0);
            int frozen=0,maxFrozen=0,launch=-1,rejected=0,viewFrozen=0,maxViewFrozen=0;float overlap=0,maxAngle=0,maxBudgetExcess=0,maxViewLag=0;
            double slowest=0,total=0;var watch=new Stopwatch();
            var csv=new StringBuilder("tick,action,airborne,supported,x,y,vx,vy,step,accepted,pitch,bodyDelta,solveMs,viewStep,viewLag,pair\n");
            var lastRotation=match.actors[1].animator.bodyPivot.rotation;
            for(int tick=0;tick<240;tick++)
            {
                if(launch>=0)
                {
                    if(a.Action!=BeetleAction.Toss&&a.Action!=BeetleAction.ReleaseLift){a.Action=BeetleAction.Brace;a.ActionTime=0;a.ActionDuration=10;}
                    if(!b.Airborne&&!b.HasFallen){b.Action=BeetleAction.Brace;b.ActionTime=0;b.ActionDuration=10;}
                }
                Vector3 viewBefore=match.actors[1].transform.position;
                Vector3 before=new Vector3(b.Position.x,b.Height,b.Position.y);watch.Restart();match.AdvanceFixedStep();watch.Stop();
                total+=watch.Elapsed.TotalMilliseconds;slowest=Math.Max(slowest,watch.Elapsed.TotalMilliseconds);
                if(a.Throws>0&&launch<0)launch=tick;
                float step=Vector3.Distance(before,new Vector3(b.Position.x,b.Height,b.Position.y));
                bool flight=launch>=0&&b.Airborne&&!b.Lift.Supported&&!b.HasFallen;
                frozen=flight&&step<.00005f?frozen+1:0;maxFrozen=Math.Max(maxFrozen,frozen);
                if(flight&&match.Contacts.AcceptedMotionFraction<.05f)rejected++;
                if(!b.HasFallen)maxBudgetExcess=Mathf.Max(maxBudgetExcess,step-match.Contacts.MotionBudget(1));
                var rotation=match.actors[1].animator.bodyPivot.rotation;float angle=Quaternion.Angle(lastRotation,rotation);lastRotation=rotation;
                if(launch>=0)maxAngle=Mathf.Max(maxAngle,angle);
                match.Present(.5f,sim.MotionTime,.02f);overlap=Mathf.Max(overlap,match.Contacts.MeasurePenetration());
                float viewStep=Vector3.Distance(viewBefore,match.actors[1].transform.position);
                float viewLag=Vector3.Distance(match.actors[1].transform.position,new Vector3(b.Position.x,b.Height,b.Position.y));
                if(flight)maxViewLag=Mathf.Max(maxViewLag,viewLag);
                viewFrozen=flight&&step>.005f&&viewStep<.00005f?viewFrozen+1:0;maxViewFrozen=Math.Max(maxViewFrozen,viewFrozen);
                csv.AppendLine($"{tick},{b.Action},{b.Airborne},{b.Lift.Supported},{b.Position.x:F4},{b.Height:F4},{b.Velocity.x:F3},{b.VerticalVelocity:F3},{step:F5},{match.Contacts.AcceptedMotionFraction:F4},{b.Lift.Pitch:F2},{angle:F2},{watch.Elapsed.TotalMilliseconds:F3},{viewStep:F5},{viewLag:F5},{match.Contacts.LastSeparationPair}");
            }
            bool ok=launch>=0&&maxFrozen<=3&&maxViewFrozen<=3&&maxViewLag<.2f&&maxAngle<35&&overlap<.003f&&maxBudgetExcess<.0002f;passed&=ok;
            report.AppendLine($"seed={seed} arena={arena} variant={variant} kind={b.Lift.ThrowKind} launch={launch} maxFrozen={maxFrozen} viewFrozen={maxViewFrozen} viewLag={maxViewLag:F4} rejectedAirborne={rejected} overlap={overlap:F6} budgetExcess={maxBudgetExcess:F6} bodyDelta={maxAngle:F2} avgStepMs={total/240:F2} maxStepMs={slowest:F2} pass={ok}");
            File.WriteAllText(folder+"/Trajectory-"+seed+"-"+arena+"-"+variant+".csv",csv.ToString());File.WriteAllText(folder+"/Report.txt",report.ToString());
        }
        if(verify&&!passed)throw new InvalidOperationException("Low throw regression failed; see "+folder);
    }
}
