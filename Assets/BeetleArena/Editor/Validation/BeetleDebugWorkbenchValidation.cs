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

/// <summary>现场续算、观察隔离、跨场景对象重绑定与工具生命期回归；不以种子重开冒充快照恢复。</summary>
public static class BeetleDebugWorkbenchValidation
{
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static JObject SimulationGraph(BeetleMatchController match)
    {
        var graph=new BeetleDebugGraph(match);var root=graph.Write(match.Simulation);
        return new JObject{{"root",root},{"nodes",graph.Nodes}};
    }
    [MenuItem("Tools/Beetle Arena/Validate Debug Workbench")]
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
                // 在非整数插值姿态保存，确认不会把显示帧的姿态误当下一固定步权威起点。
                match.EditorSetPaused(false);match.Present(.37f,match.Simulation.MotionTime,.007f);match.EditorSetPaused(true);
                var snapshot=BeetleDebugSnapshot.Capture(match);
                string path=Path.Combine(artifact,"validation-arena-"+arena+".json");BeetleDebugSnapshot.Save(match,path);
                var expected=new JObject[60];
                BeetleDebugWorkbenchService.SetRecording(false);
                for(int step=0;step<60;step++){match.EditorStep();expected[step]=SimulationGraph(match);}
                BeetleDebugSnapshot.Restore(match,snapshot);BeetleDebugWorkbenchService.SetRecording(true);
                for(int step=0;step<60;step++)
                {
                    match.EditorStep();
                    Require(JToken.DeepEquals(expected[step],SimulationGraph(match)),"Snapshot continuation differs at arena="+arena+", tick="+step);
                    // 检视与模块资格检查必须只读，不能改变下一步。
                    _=BeetleDebugWorkbenchService.Inspect();
                }
                report.AppendLine("PASS arena="+arena+": 60 fixed steps exact simulation/RNG/contact graph equality after restore; diagnostics/Inspect do not alter result.");
                // 文件恢复到新建的另一套实例，验证场景对象路径映射与共享引用。
                BeetleDebugWorkbenchService.CreateSandbox(71,arena);match=BeetleDebugWorkbenchService.Match;
                BeetleDebugWorkbenchService.LoadCase(path);
                for(int step=0;step<10;step++){match.EditorStep();Require(JToken.DeepEquals(expected[step],SimulationGraph(match)),"Cross-scene restore differs at tick="+step);}
                report.AppendLine("PASS arena="+arena+": file restored into fresh scene/actors, continued 10 steps identically.");
                // 失败前置检查不得改写当前模拟。
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

