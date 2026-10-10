using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 保留真实攻击者的扬角动作，连续测量受击者响应；不能把攻击者改为 Brace 来验证一个空中跳跃。
public static class BeetleLiftValidation
{
    [MenuItem("Tools/Beetle Arena/Validate Horn Supported Lift")]
    public static void Run() => RunCases(true);
    public static void RunCases(bool assertResults)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before validation.");
        const string temporary = "Assets/BeetleArena/Generated/LiftReworkValidation.unity";
        var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!AssetDatabase.CopyAsset(BeetleArenaSceneBuilder.ScenePath, temporary)) throw new Exception("Temporary scene exists");
        var scene = EditorSceneManager.OpenScene(temporary, OpenSceneMode.Additive);
        BeetleMatchSettings settings = null;
        var report = new StringBuilder("HORN-SUPPORTED LIFT VALIDATION\n");
        Directory.CreateDirectory("Artifacts/LiftRework");
        try
        {
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            var match = scene.GetRootGameObjects().Select(go => go.GetComponent<BeetleMatchController>()).First(m => m);
            settings = UnityEngine.Object.Instantiate(match.settings); settings.tossChance = 0;
            match.settings = settings; match.hud.Bind(match);
            for(int arena=0;arena<2;arena++)
            {
                match.SelectArena(arena); match.StartMatch(71);
                var sim=match.Simulation; while(sim.Phase==MatchPhase.Countdown) sim.Step(.02f);
                var a=sim.Fighters[0]; var b=sim.Fighters[1];
                a.Position=new Vector2(-2.05f,0); b.Position=new Vector2(2.05f,0); a.Heading=90; b.Heading=-90;
                a.Action=BeetleAction.PryWindup; a.ActionTime=0; a.ActionDuration=.55f;
                b.Action=BeetleAction.Brace; b.ActionDuration=10; b.Balance=.3f; b.Stamina=35;
                sim.SetGround(match.ActiveSurface);
                for(int i=0;i<2;i++)match.actors[i].Bind(sim.Fighters[i],settings,match.ActiveSurface);
                match.Contacts.Solve(sim,.02f);
                Vector2 start=b.Position; Vector3 prev=match.actors[1].transform.position;
                float peak=0,travel=0,step=0,depth=0,unsupported=0,struggle=0,anchorError=0;
                int supportedTicks=0, firstSupport=-1;
                float responseStep=0; float[] legMotion=new float[6];
                var bones=match.actors[1].animator.importedRig.bindings;
                var lastKnees=bones.Select(leg=>leg.knee.localRotation).ToArray();
                var csv=new StringBuilder("time,action,supported,rootY,groundY,pitch,travel,unsupported,struggle,anchorError,overlap\n");
                for(int tick=0;tick<270;tick++)
                {
                    if(firstSupport>=0 && !b.Lift.Supported)
                    {
                        if(a.Action!=BeetleAction.ReleaseLift && a.Action!=BeetleAction.Toss)
                        {a.Action=BeetleAction.Brace; a.ActionTime=0; a.ActionDuration=10;}
                        if(!b.Airborne){b.Action=BeetleAction.Brace;b.ActionTime=0;b.ActionDuration=10;}
                    }
                    foreach(var actor in match.actors)actor.BeforeStep(); sim.Step(.02f);
                    if(b.Lift.Supported){supportedTicks++;if(firstSupport<0)firstSupport=tick;}
                    peak=Mathf.Max(peak,b.Height-b.SurfaceHeight);
                    travel=Mathf.Max(travel,Vector2.Distance(start,b.Position));
                    unsupported=Mathf.Max(unsupported,b.Lift.UnsupportedTime);
                    struggle=Mathf.Max(struggle,match.actors[1].animator.StruggleWeight);
                    float delta=Vector3.Distance(prev,match.actors[1].transform.position);
                    step=Mathf.Max(step,delta); if(firstSupport>=0)responseStep=Mathf.Max(responseStep,delta);
                    prev=match.actors[1].transform.position;
                    if(b.Lift.UnsupportedTime<=.70001f && match.actors[1].animator.StruggleWeight>.00001f)
                        throw new Exception("Struggle started before 0.7s of lost support.");
                    for(int k=0;k<6;k++)
                    {
                        if(match.actors[1].animator.StruggleWeight>.1f)legMotion[k]+=Quaternion.Angle(lastKnees[k],bones[k].knee.localRotation);
                        lastKnees[k]=bones[k].knee.localRotation;
                    }
                    if(b.Lift.Supported)anchorError=Mathf.Max(anchorError,match.Contacts.LiftAnchorError);
                    foreach(var actor in match.actors)actor.Render(.5f,sim.Elapsed,.02f);
                    match.Contacts.ProjectView(sim); float d=match.Contacts.MeasurePenetration(); depth=Mathf.Max(depth,d);
                    csv.AppendLine($"{tick*.02f:F3},{b.Action},{b.Lift.Supported},{b.Height:F4},{b.SurfaceHeight:F4},{b.Lift.Pitch:F2},{Vector2.Distance(start,b.Position):F4},{b.Lift.UnsupportedTime:F3},{match.actors[1].animator.StruggleWeight:F3},{match.Contacts.LiftAnchorError:F4},{d:F6},{a.Action},{Vector2.Distance(a.Position,b.Position):F3},{match.actors[0].animator.headPivot.localEulerAngles.x:F1},{match.Contacts.LastSeparationPair},{match.Contacts.LastSeparationNormal}");
                    if(new[]{0,10,25,40,60,75,95}.Contains(tick)) Capture(match,$"Supported-{arena}-{tick}.png");
                    if(firstSupport>=0 && new[]{0,10,25,40,60,75,95}.Contains(tick-firstSupport))
                        Capture(match,$"Phases-{arena}-{tick-firstSupport}.png");
                }
                report.AppendLine($"arena={arena}, contact={match.Contacts.LiftContact}, supported={supportedTicks*.02f:F2}s, rise={peak:F3}, travel={travel:F3}, step={step:F3}, anchorError={anchorError:F3}, unsupported={unsupported:F2}s, struggle={struggle:F3}, overlap={depth:F6}, end={b.Action}, fallen={b.HasFallen}");
                report.AppendLine($"  max step after lift began={responseStep:F3}; knee motion during struggle=[{string.Join(",",legMotion.Select(v=>v.ToString("F1")))}]");
                File.WriteAllText($"Artifacts/LiftRework/Trajectory-{arena}.csv",csv.ToString());
                // 岩台这个初始站位可能被高低差/对方头角挡住；不能为了测试成功而隔空托举。
                // 岩台自然对局另行要求发生实际托举。平地受控场景必须完整展示托举与落地。
                bool badLift = supportedTicks>0 && (supportedTicks<30 || peak<.3f || unsupported<.7f || struggle<.1f || responseStep>.12f || legMotion.Any(v=>v<2));
                if(assertResults && (badLift || arena==0 && supportedTicks==0 || depth>.003f || b.HasFallen))
                    throw new Exception("Supported lift trajectory failed: "+report);
            }
            match.SelectArena(0);
            settings.tossChance = .08f;
            typeof(BeetleArenaCollisionValidation).GetMethod("ValidateToss",BindingFlags.Static|BindingFlags.NonPublic)
                .Invoke(null,new object[]{match,report});
            report.AppendLine("ALL TARGETED RESPONSE CHECKS PASSED");
        }
        finally
        {
            File.WriteAllText("Artifacts/LiftRework/Validation.txt",report.ToString());
            EditorSceneManager.CloseScene(scene,true);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous); AssetDatabase.DeleteAsset(temporary);
            if(settings)UnityEngine.Object.DestroyImmediate(settings);
        }
    }
    static void Capture(BeetleMatchController match,string filename)
    {
        typeof(BeetleArenaCollisionValidation).GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic)
            .Invoke(null,new object[]{match,filename});
        File.Copy("Artifacts/BeetleArena/"+filename,"Artifacts/LiftRework/"+filename,true);
    }
}
