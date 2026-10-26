using System;
using System.IO;
using System.Linq;
using System.Text;
using BeetleArena;
using BeetleArena.EditorTools;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Scene resumption, telemetry isolation, cross-scene re-binding, and tool lifecycle regressions; does not substitute snapshot restore with seed re-runs.</summary>
public static class BeetleDebugWorkbenchValidation
{
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static JObject SimulationGraph(BeetleMatchController match)
    {
        var graph=new BeetleDebugGraph(match);var root=graph.Write(match.Simulation);
        return new JObject{{"root",root},{"nodes",graph.Nodes}};
    }
    static bool SameCrossSceneState(JObject expected,JObject actual,ref double queryError)
    {
        // A fresh PhysX scene can differ by float roundoff in ClosestPoint. Bound only
        // the four derived foreleg-query values; RNG, decisions and all physics stay exact.
        var expectedNodes=(JArray)expected["nodes"];var actualNodes=(JArray)actual["nodes"];
        if(expectedNodes.Count!=actualNodes.Count)return false;
        for(int i=0;i<expectedNodes.Count;i++)
        {
            if(expectedNodes[i]["type"].Type!=JTokenType.String ||
                (string)expectedNodes[i]["type"]!=typeof(BeetleMotionRuntime).FullName)continue;
            const string context="BeetleArena.BeetleMotionRuntime/<Context>k__BackingField";
            var e=expectedNodes[i]["fields"][context]["fields"];var a=actualNodes[i]["fields"][context]["fields"];
            double queryErrorLocal=0;
            bool Near(JToken left,JToken right)
            {
                if(JToken.DeepEquals(left,right))return true;
                double error=Math.Abs((double)left["value"]-(double)right["value"]);
                queryErrorLocal=Math.Max(queryErrorLocal,error);return error<=1e-6;
            }
            const string distance="BeetleArena.BeetleMotionContext/ForelegTargetDistance";
            const string aim="BeetleArena.BeetleMotionContext/ForelegAimCorrection";
            if(!Near(e[distance],a[distance]))return false;
            foreach(string axis in new[]{"x","y","z"})
                if(!Near(e[aim]["fields"]["UnityEngine.Vector3/"+axis],a[aim]["fields"]["UnityEngine.Vector3/"+axis]))return false;
            queryError=Math.Max(queryError,queryErrorLocal);
            a[distance]=e[distance].DeepClone();a[aim]=e[aim].DeepClone();
        }
        return JToken.DeepEquals(expected,actual);
    }
    [MenuItem("Tools/甲壳虫斗/验证/架构与性能/调试工作台", false, 744)]
    public static void Run()
    {
        var report=new StringBuilder("DEBUG WORKBENCH VALIDATION\n"+DateTimeOffset.Now.ToString("o")+"\n");
        string artifact=BeetleDebugWorkbenchService.ArtifactRoot;Directory.CreateDirectory(artifact);
        BeetleDebugWorkbenchService.Close();
        int scenes=UnityEngine.SceneManagement.SceneManager.sceneCount;
        try
        {
            for(int arena=0;arena<2;arena++)
            {
                BeetleDebugWorkbenchService.CreateSandbox(1132085498,arena);var match=BeetleDebugWorkbenchService.Match;
                BeetleDebugWorkbenchService.Step(200);
                // Saves at non-integer interpolation poses, confirming display poses are never mistaken for authoritative next-step origins.
                match.EditorSetPaused(false);match.Present(.37f,match.Simulation.MotionTime,.007f);match.EditorSetPaused(true);
                var snapshot=BeetleDebugSnapshot.Capture(match);
                var capsuleSizes=match.actors.SelectMany(a=>a.GetComponentsInChildren<CapsuleCollider>(true))
                    .Select(c=>new Vector2(c.radius,c.height)).ToArray();
                string path=Path.Combine(artifact,"validation-arena-"+arena+".json");BeetleDebugSnapshot.Save(match,path);
                var expected=new JObject[60];
                BeetleDebugWorkbenchService.SetRecording(false);
                for(int step=0;step<60;step++){match.EditorStep();expected[step]=SimulationGraph(match);}
                BeetleDebugSnapshot.Restore(match,snapshot);BeetleDebugWorkbenchService.SetRecording(true);
                for(int step=0;step<60;step++)
                {
                    match.EditorStep();
                    Require(JToken.DeepEquals(expected[step],SimulationGraph(match)),"Snapshot continuation differs at arena="+arena+", tick="+step);
                    // Inspection and module eligibility queries must be read-only without mutating subsequent steps.
                    _=BeetleDebugWorkbenchService.Inspect();
                }
                report.AppendLine("PASS arena="+arena+": 60 fixed steps exact simulation/RNG/contact graph equality after restore; diagnostics/Inspect do not alter result.");
                // Restores file into newly spawned secondary instance, verifying scene object path mappings and shared references.
                BeetleDebugWorkbenchService.CreateSandbox(71,arena);match=BeetleDebugWorkbenchService.Match;
                BeetleDebugWorkbenchService.LoadCase(path);
                var restoredCapsules=match.actors.SelectMany(a=>a.GetComponentsInChildren<CapsuleCollider>(true)).ToArray();
                Require(restoredCapsules.Length==capsuleSizes.Length,"Snapshot collider topology changed");
                for(int i=0;i<restoredCapsules.Length;i++)
                    Require(restoredCapsules[i].radius==capsuleSizes[i].x && restoredCapsules[i].height==capsuleSizes[i].y,
                        "Cross-scene restore retained destination capsule dimensions: "+restoredCapsules[i].name);
                double maxQueryError=0;
                for(int step=0;step<10;step++){match.EditorStep();Require(SameCrossSceneState(expected[step],SimulationGraph(match),ref maxQueryError),"Cross-scene restore differs at tick="+step);}
                report.AppendLine("PASS arena="+arena+": native capsule dimensions match; 10 steps exact RNG/decisions/physics, foreleg query error="+maxQueryError.ToString("E3")+" (limit 1e-6).");
                // Prerequisite check failures must not mutate active simulation.
                var before=SimulationGraph(match);var broken=(JObject)snapshot.DeepClone();broken["fingerprint"]="invalid";
                bool rejected=false;try{BeetleDebugSnapshot.Restore(match,broken);}catch(InvalidOperationException){rejected=true;}
                Require(rejected && JToken.DeepEquals(before,SimulationGraph(match)),"Stale snapshot mutated match");
                Require(BeetleDebugWorkbenchService.Frames.Count>0,"No recorded frames");
                var transforms=match.actors.SelectMany(a=>a.GetComponentsInChildren<Transform>(true)).ToArray();var poses=transforms.Select(t=>new DebugBonePose(t)).ToArray();
                BeetleDebugWorkbenchService.ShowFrame(0);BeetleDebugWorkbenchService.ReturnToLive();
                for(int i=0;i<transforms.Length;i++)Require(Vector3.Distance(poses[i].position,transforms[i].localPosition)<1e-7f && Quaternion.Angle(poses[i].rotation,transforms[i].localRotation)<.001f,"Pose playback leaked into live transforms");
                string clip=BeetleDebugWorkbenchService.SaveClip(Path.Combine(artifact,"validation-poses-"+arena+".json"));
                BeetleDebugWorkbenchService.LoadClip(clip);BeetleDebugWorkbenchService.ReturnToLive();
                Require(JToken.DeepEquals(before,SimulationGraph(match)),"Pose clip mutated simulation");
                report.AppendLine("PASS stale fingerprint rejected; pose clip export/import/review preserves simulation.");
            }
            BeetleDebugWorkbenchService.PreviewMotion(0,BeetleDebugWorkbenchService.Match.settings.motionModules.OfType<HornFeintMotion>().First().name);
            BeetleDebugWorkbenchService.SeekPreview(.4f);var first=SimulationGraph(BeetleDebugWorkbenchService.Match);
            BeetleDebugWorkbenchService.SeekPreview(.4f);Require(JToken.DeepEquals(first,SimulationGraph(BeetleDebugWorkbenchService.Match)),"Preview seek accumulates state");
            var response=JObject.Parse(BeetleDebugWorkbenchService.ExecuteJson("{\"id\":\"test\",\"action\":\"inspect\"}"));
            Require((bool)response["ok"] && (string)response["id"]=="test","AI command result invalid");
            response=JObject.Parse(BeetleDebugWorkbenchService.ExecuteJson("{\"id\":\"bad\",\"action\":\"not-a-command\"}"));
            Require(!(bool)response["ok"],"Unknown command accepted");
            BeetleDebugWorkbenchWindow.Open();
            var window=Resources.FindObjectsOfTypeAll<BeetleDebugWorkbenchWindow>().Single();
            Require(window.rootVisualElement.Q<UnityEngine.UIElements.Button>("capture")!=null,"UXML window did not load");window.Close();
            report.AppendLine("PASS repeatable action preview seek; structured AI command success/errors; actual UI Toolkit tree imported.");
        }
        finally
        {
            BeetleDebugWorkbenchService.Close();
            report.AppendLine("sceneCountBefore="+scenes+", after="+UnityEngine.SceneManagement.SceneManager.sceneCount);
            File.WriteAllText(Path.Combine(artifact,"Validation.txt"),report.ToString());
        }
        Require(scenes==UnityEngine.SceneManagement.SceneManager.sceneCount,"Preview scene leaked");
        Debug.Log(report.ToString());
    }
}

