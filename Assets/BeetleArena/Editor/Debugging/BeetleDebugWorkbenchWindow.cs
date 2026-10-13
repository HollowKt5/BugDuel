using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BeetleArena;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BeetleArena.EditorTools
{
    public sealed class BeetleDebugWorkbenchWindow : EditorWindow
    {
        const string Folder="Assets/BeetleArena/Editor/Debugging/";
        [MenuItem("Tools/Beetle Arena/Debug Workbench")]
        public static void Open()=>GetWindow<BeetleDebugWorkbenchWindow>("BugDuel 调试").Show();
        T Q<T>(string name) where T:VisualElement=>rootVisualElement.Q<T>(name);
        void Run(Action action){try{action();}catch(Exception e){Q<Label>("status").text=e.Message;Debug.LogWarning(e.Message);error=e.Message;}Refresh();}
        string error;double nextRefresh;
        public void CreateGUI()
        {
            rootVisualElement.Clear();AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Folder+"BeetleDebugWorkbench.uxml").CloneTree(rootVisualElement);
            minSize=new Vector2(440,580);
            Q<ObjectField>("target").objectType=typeof(BeetleMatchController);
            Choices("arena",new[]{"平地木桩","起伏岩台"});Choices("actor",new[]{"琥珀 / 0","墨甲 / 1"});
            Choices("leg",new[]{"全部","左前足","左中足","左后足","右前足","右中足","右后足"});
            Choices("metric",new[]{"脚端最大误差","头角俯仰","体力","接受姿态比例"});
            Choices("motion",new[]{"Probe","ShoveWindup","ChargeWindup","PryWindup","HornLock"});
            Q<CurveField>("curve").SetEnabled(false);
            Button("sandbox",()=>{BeetleDebugWorkbenchService.CreateSandbox(Q<IntegerField>("seed").value,Q<DropdownField>("arena").index);UpdateMotions();});
            Button("attach",()=>{var match=Q<ObjectField>("target").value as BeetleMatchController;
                if(!match && Selection.activeGameObject)match=Selection.activeGameObject.GetComponentInParent<BeetleMatchController>();
                if(!match){var all=UnityEngine.Object.FindObjectsByType<BeetleMatchController>(FindObjectsSortMode.None).Where(m=>m.Simulation!=null).ToArray();if(all.Length==1)match=all[0];}
                BeetleDebugWorkbenchService.Attach(match);UpdateMotions();});
            Button("resume",BeetleDebugWorkbenchService.Resume);Button("restore-foot",BeetleDebugWorkbenchService.RestoreFootPreview);
            Button("close",BeetleDebugWorkbenchService.Close);Button("pause",BeetleDebugWorkbenchService.Pause);
            Button("step",()=>BeetleDebugWorkbenchService.Step());Button("steps",()=>BeetleDebugWorkbenchService.Step(10));
            Button("slow",()=>BeetleDebugWorkbenchService.PlaySlow(Q<Slider>("speed").value));
            Button("capture",()=>BeetleDebugWorkbenchService.SaveCase());Button("screenshot",()=>BeetleDebugWorkbenchService.CaptureImage());
            Button("restore",()=>{var p=EditorUtility.OpenFilePanel("载入问题现场",BeetleDebugWorkbenchService.ArtifactRoot,"json");if(p.Length>0){BeetleDebugWorkbenchService.LoadCase(p);UpdateMotions();}});
            Button("folder",()=>{Directory.CreateDirectory(BeetleDebugWorkbenchService.ArtifactRoot);EditorUtility.RevealInFinder(BeetleDebugWorkbenchService.ArtifactRoot);});
            Button("live",BeetleDebugWorkbenchService.ReturnToLive);Button("export-clip",()=>BeetleDebugWorkbenchService.SaveClip());
            Button("load-clip",()=>{var p=EditorUtility.OpenFilePanel("载入姿态录像",BeetleDebugWorkbenchService.ArtifactRoot,"json");if(p.Length>0)BeetleDebugWorkbenchService.LoadClip(p);});
            Button("preview",()=>BeetleDebugWorkbenchService.PreviewMotion(Q<DropdownField>("actor").index,Q<DropdownField>("motion").value));
            Button("seek-preview",()=>BeetleDebugWorkbenchService.SeekPreview(Q<Slider>("preview-time").value));
            Button("copy-inspect",()=>EditorGUIUtility.systemCopyBuffer=BeetleDebugWorkbenchService.Inspect().ToString(Formatting.Indented));
            Button("export-trace",()=>{Directory.CreateDirectory(BeetleDebugWorkbenchService.ArtifactRoot);File.WriteAllText(Path.Combine(BeetleDebugWorkbenchService.ArtifactRoot,"trace-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".json"),new JArray(BeetleDebugWorkbenchService.Trace.Select(t=>t.DeepClone())).ToString(Formatting.Indented));});
            Q<SliderInt>("frame").RegisterValueChangedCallback(e=>Run(()=>BeetleDebugWorkbenchService.ShowFrame(e.newValue)));
            Q<Toggle>("record").RegisterValueChangedCallback(e=>BeetleDebugWorkbenchService.SetRecording(e.newValue));
            Q<Toggle>("draw-ik").RegisterValueChangedCallback(e=>{BeetleDebugWorkbenchService.DrawIK=e.newValue;SceneView.RepaintAll();});
            Q<Toggle>("draw-contact").RegisterValueChangedCallback(e=>{BeetleDebugWorkbenchService.DrawContacts=e.newValue;SceneView.RepaintAll();});
            Q<Toggle>("move-foot").RegisterValueChangedCallback(e=>BeetleDebugWorkbenchService.MoveFoot=e.newValue);
            Q<DropdownField>("actor").RegisterValueChangedCallback(e=>{BeetleDebugWorkbenchService.SelectedActor=Q<DropdownField>("actor").index;SceneView.RepaintAll();});
            Q<DropdownField>("leg").RegisterValueChangedCallback(e=>{BeetleDebugWorkbenchService.SelectedLeg=Q<DropdownField>("leg").index-1;SceneView.RepaintAll();});
            UpdateMotions();Refresh();
        }
        void Button(string name,Action action)=>Q<Button>(name).clicked+=()=>{error=null;Run(action);};
        void Choices(string name,IEnumerable<string> values){var f=Q<DropdownField>(name);f.choices=values.ToList();f.index=0;}
        void UpdateMotions()
        {
            var list=new List<string>{"Probe","ShoveWindup","ChargeWindup","PryWindup","HornLock"};var match=BeetleDebugWorkbenchService.Match;
            if(match)list.AddRange((match.settings.motionModules??Array.Empty<BeetleMotionModule>()).Where(m=>m && m.Channel==BeetleMotionChannel.Combat).Select(m=>m.name));Choices("motion",list);
        }
        void OnInspectorUpdate(){if(EditorApplication.timeSinceStartup<nextRefresh)return;nextRefresh=EditorApplication.timeSinceStartup+.2;Refresh();}
        void Refresh()
        {
            if(Q<Label>("status")==null)return;
            Q<Label>("status").text=error??BeetleDebugWorkbenchService.LastMessage;
            Q<Toggle>("record").SetValueWithoutNotify(BeetleDebugWorkbenchService.Recording);
            var match=BeetleDebugWorkbenchService.Match;if(!match){Q<TextField>("inspect").SetValueWithoutNotify("尚未连接对局。");return;}
            var frames=BeetleDebugWorkbenchService.Frames;Q<SliderInt>("frame").highValue=Mathf.Max(0,frames.Count-1);
            if(!BeetleDebugWorkbenchService.IsViewingClip)Q<SliderInt>("frame").SetValueWithoutNotify(Mathf.Max(0,frames.Count-1));
            var state=BeetleDebugWorkbenchService.Inspect();
            Q<TextField>("inspect").SetValueWithoutNotify($"t={match.Simulation.MotionTime:F2}s  {match.ArenaName}  接受比例={match.Contacts.AcceptedMotionFraction:F3}\n"+
                string.Join("\n",match.Simulation.Fighters.Select(b=>$"[{b.Index}] {b.Intent} / {b.Action} {b.ActionTime:F2}/{b.ActionDuration:F2}s  体力={b.Stamina:F1} 爆发={b.BurstReserve:F2} 受阻={b.ContactBlockedSeconds:F2}s"))+"\n"+
                string.Join("\n",state["fighters"][Q<DropdownField>("actor").index]["modules"].Select(m=>$"{m["name"]}: {m["eligibility"]}")));
            Q<TextField>("trace").SetValueWithoutNotify(string.Join("\n",BeetleDebugWorkbenchService.Trace.Where(t=>(string)t["kind"]!="step").Reverse().Take(6).Reverse().Select(t=>t.ToString(Formatting.None))));
            int index=Q<DropdownField>("actor").index,metric=Q<DropdownField>("metric").index;
            var curve=new AnimationCurve(frames.Select(f=>new Keyframe(f.time,metric==0?f.footError[index]:metric==1?f.headPitch[index]:metric==2?f.stamina[index]:f.acceptedFraction)).ToArray());Q<CurveField>("curve").SetValueWithoutNotify(curve);
        }
    }
}
