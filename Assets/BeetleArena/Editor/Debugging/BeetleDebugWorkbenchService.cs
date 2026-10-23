using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace BeetleArena.EditorTools
{
    [Serializable] public struct DebugBonePose
    {
        public Vector3 position,scale;public Quaternion rotation;
        public DebugBonePose(Transform t){position=t.localPosition;rotation=t.localRotation;scale=t.localScale;}
        public void Apply(Transform t){t.localPosition=position;t.localRotation=rotation;t.localScale=scale;}
    }
    [Serializable] public sealed class DebugPoseFrame
    {
        public float time;public string[] actions;public DebugBonePose[] bones;
        public float[] stamina,footError,headPitch;public float acceptedFraction;
    }
    [Serializable] public sealed class DebugPoseClip
    {
        public int version=1;public string fingerprint;public string[] paths;public DebugPoseFrame[] frames;
    }
    [Serializable] public sealed class DebugCommand
    {
        public string id,action,path,module;public int seed=71,arena,actor,steps=1,frame;public float speed=.25f,seconds;public bool enabled=true;
    }

    /// <summary>
    /// 人与AI共用的调试用例层。UI不直接更改状态，命令白名单不接受任意C#。
    /// 仅编辑器存在；临时场景由本服务独占，退出/编译/切Play时释放。
    /// </summary>
    [InitializeOnLoad]
    public static class BeetleDebugWorkbenchService
    {
        public static BeetleMatchController Match { get; private set; }
        public static bool OwnsSandbox=>sandbox!=null;
        public static bool Recording { get; private set; }
        public static bool Playing { get; private set; }
        public static float Speed { get; set; }=.25f;
        public static bool IsViewingClip=>livePose!=null;
        public static int SelectedActor,SelectedLeg=-1;
        public static bool DrawIK=true,DrawContacts=true,MoveFoot;
        public static Vector3? FootOverride;
        public static string LastMessage { get; private set; }="创建预览场景，或连接已运行的对局。";
        public static IReadOnlyList<DebugPoseFrame> Frames=>frames;
        public static IReadOnlyList<JObject> Trace=>trace;
        const int FrameCapacity=300,TraceCapacity=1200,PairsPerStep=96;
        static readonly List<DebugPoseFrame> frames=new List<DebugPoseFrame>(FrameCapacity);
        static readonly List<JObject> trace=new List<JObject>(TraceCapacity);
        static readonly List<JObject> pairs=new List<JObject>(PairsPerStep);
        static readonly Dictionary<GameObject,bool> visibility=new Dictionary<GameObject,bool>();
        static BeetleValidationScene sandbox;static Transform[] bones;static DebugBonePose[] livePose;
        static JObject previewBaseline,footBaseline;static double clock;static float accumulated;static int droppedPairs;
        static string lastCase;static BeetleSimulation observedSimulation;
        public static string ArtifactRoot=>Path.GetFullPath("Artifacts/DebugWorkbench");
        static BeetleDebugWorkbenchService()
        {
            EditorApplication.update+=Update;
            AssemblyReloadEvents.beforeAssemblyReload+=Close;
            EditorApplication.quitting+=Close;
            EditorApplication.playModeStateChanged+=OnPlayMode;
            BeetleDebugHooks.Stepped+=OnStep;BeetleDebugHooks.Transition+=OnTransition;
            BeetleDebugHooks.Decision+=OnDecision;BeetleDebugHooks.ContactAttempt+=OnContact;BeetleDebugHooks.NarrowContact+=OnPair;
        }
        static void OnPlayMode(PlayModeStateChange state){if(state==PlayModeStateChange.ExitingEditMode || state==PlayModeStateChange.ExitingPlayMode)Close();}
        public static void Close()
        {
            Playing=false;ReturnToLive();footBaseline=null;Recording=false;Match=null;observedSimulation=null;bones=null;previewBaseline=null;
            try{sandbox?.Dispose();}finally{sandbox=null;foreach(var item in visibility)if(item.Key){if(item.Value)SceneVisibilityManager.instance.Hide(item.Key,false);else SceneVisibilityManager.instance.Show(item.Key,false);}visibility.Clear();}
            frames.Clear();trace.Clear();pairs.Clear();FootOverride=null;LastMessage="调试连接已关闭，预览资源已释放。";
        }
        public static void CreateSandbox(int seed=71,int arena=0)
        {
            Close();
            try
            {
                foreach(var scene in Enumerable.Range(0,UnityEngine.SceneManagement.SceneManager.sceneCount).Select(UnityEngine.SceneManagement.SceneManager.GetSceneAt))
                    foreach(var root in scene.GetRootGameObjects())
                    {
                        foreach(var t in root.GetComponentsInChildren<Transform>(true))visibility[t.gameObject]=SceneVisibilityManager.instance.IsHidden(t.gameObject);
                        if(!visibility[root])SceneVisibilityManager.instance.Hide(root,true);
                    }
                sandbox=new BeetleValidationScene("DebugWorkbenchPreview");Match=sandbox.Match;
                Match.SelectArena(arena);Match.StartMatch(seed);Match.EditorSetPaused(true);BindBones();
                Recording=true;OnStep(Match);LastMessage="隔离预览已创建；正式场景和配置不保存修改。";
                if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.Frame(new Bounds(new Vector3(0,1,0),new Vector3(12,7,12)),false);
            }
            catch{Close();throw;}
        }
        public static void Attach(BeetleMatchController match)
        {
            if(!match || match.Simulation==null)throw new InvalidOperationException("请选择已进入Play并启动对局的控制器。");
            if(sandbox!=null && match==Match)return;
            Close();Match=match;BindBones();Recording=true;OnStep(Match);LastMessage="已连接当前对局；开始记录，未自动暂停。";
        }
        static void BindBones(){bones=Match.actors.SelectMany(a=>a.GetComponentsInChildren<Transform>(true)).ToArray();livePose=null;FootOverride=null;}
        static void RequireMatch(){if(!Match || Match.Simulation==null)throw new InvalidOperationException("请先创建预览场景或连接运行中的对局。");}
        public static void Pause(){RequireMatch();Playing=false;Match.EditorSetPaused(true);LastMessage="已暂停。";}
        public static void PlaySlow(float speed)
        {
            Pause();ReturnToLive();RestoreFootPreview();Speed=Mathf.Clamp(speed,.05f,1);clock=EditorApplication.timeSinceStartup;accumulated=0;Playing=true;
            LastMessage="按正式固定步慢放 ×"+Speed.ToString("F2");
        }
        public static void Step(int count=1)
        {
            RequireMatch();if(count<1 || count>500)throw new ArgumentOutOfRangeException(nameof(count),"每次推进1..500步。");
            Pause();ReturnToLive();RestoreFootPreview();FootOverride=null;
            for(int i=0;i<count;i++)Match.EditorStep();
            SceneView.RepaintAll();EditorApplication.QueuePlayerLoopUpdate();
        }
        static void Update()
        {
            if(!Playing || !Match)return;
            double now=EditorApplication.timeSinceStartup;accumulated+=Mathf.Min((float)(now-clock),.1f)*Speed;clock=now;
            try
            {
                int work=0;while(accumulated>=BeetleSimulation.FixedStep && work++<5){Match.EditorStep();accumulated-=BeetleSimulation.FixedStep;}
                SceneView.RepaintAll();EditorApplication.QueuePlayerLoopUpdate();
            }
            catch(Exception e){Playing=false;LastMessage=e.Message;Debug.LogException(e);}
        }
        public static void SetRecording(bool enabled){Recording=enabled;pairs.Clear();droppedPairs=0;}
        static void CheckSession()
        {
            if(!Match || ReferenceEquals(observedSimulation,Match.Simulation))return;
            observedSimulation=Match.Simulation;frames.Clear();trace.Clear();pairs.Clear();droppedPairs=0;BindBones();
        }
        static void Add(JObject row){if(!Recording)return;if(trace.Count==TraceCapacity)trace.RemoveAt(0);trace.Add(row);}
        static JObject Row(string kind,float time)=>new JObject{{"kind",kind},{"time",time}};
        static JArray V(Vector3 v)=>new JArray(v.x,v.y,v.z);
        static void OnPair(BeetleContactSolver solver,Collider a,Collider b,Vector3 normal,float depth,string kind)
        {
            if(!Recording || !Match || solver!=Match.Contacts)return;
            CheckSession();
            if(pairs.Count>=PairsPerStep){droppedPairs++;return;}
            pairs.Add(new JObject{{"a",a.name},{"b",b.name},{"normal",V(normal)},{"depth",depth},{"kind",kind}});
        }
        static void OnContact(BeetleSimulation sim,BeetleContactSolver solver,float fraction,float residual,Vector3 pa,Vector3 pb,Vector3 aa,Vector3 ab)
        {
            if(!Recording || !Match || sim!=Match.Simulation)return;
            CheckSession();
            float ma=Vector3.Distance(pa,aa),mb=Vector3.Distance(pb,ab);
            if(ma<.00001f && mb<.00001f && residual<.00001f && pairs.Count==0)return;
            var row=Row("contact-attempt",sim.MotionTime);row["fraction"]=fraction;row["residual"]=residual;
            row["proposal"]=new JArray(V(pa),V(pb));row["corrected"]=new JArray(V(aa),V(ab));
            row["budget"]=new JArray(solver.MotionBudget(0),solver.MotionBudget(1));row["pairs"]=new JArray(pairs.Select(p=>p.DeepClone()));row["omittedPairs"]=droppedPairs;
            row["note"]="候选尝试；最终是否采用请看同一时刻的 step.acceptedFraction";Add(row);pairs.Clear();droppedPairs=0;
        }
        static void OnTransition(BeetleSimulation sim,int actor,BeetleAction from,BeetleAction to,string caller,int line)
        {
            if(!Recording || !Match || sim!=Match.Simulation)return;
            CheckSession();
            var row=Row("transition",sim.MotionTime);row["actor"]=actor;row["from"]=from.ToString();row["to"]=to.ToString();row["sourceMethod"]=caller;row["sourceLine"]=line;Add(row);
        }
        static void OnDecision(BeetleSimulation sim,int actor,bool permit,float choice,string reason)
        {
            if(!Recording || !Match || sim!=Match.Simulation)return;
            CheckSession();
            var row=Row("decision",sim.MotionTime);row["actor"]=actor;row["permitModule"]=permit;row["choice"]=choice;row["gate"]=reason;
            row["intent"]=sim.Fighters[actor].Intent.ToString();Add(row);
        }
        static void OnStep(BeetleMatchController match)
        {
            if(!Recording || match!=Match)return;
            CheckSession();
            if(bones==null)BindBones();
            var frame=new DebugPoseFrame{time=match.Simulation.MotionTime,bones=bones.Select(t=>new DebugBonePose(t)).ToArray(),
                actions=match.Simulation.Fighters.Select(b=>b.Action.ToString()).ToArray(),stamina=match.Simulation.Fighters.Select(b=>b.Stamina).ToArray(),
                footError=match.actors.Select(a=>a.animator.importedRig.bindings.Max(b=>b.targetError)).ToArray(),
                headPitch=match.actors.Select(a=>Mathf.DeltaAngle(0,a.animator.headPivot.localEulerAngles.x)).ToArray(),acceptedFraction=match.Contacts.AcceptedMotionFraction};
            if(frames.Count==FrameCapacity)frames.RemoveAt(0);frames.Add(frame);
            var row=Row("step",frame.time);row["acceptedFraction"]=frame.acceptedFraction;row["actions"]=new JArray(frame.actions);row["footError"]=new JArray(frame.footError);Add(row);
            pairs.Clear();droppedPairs=0;
        }
        public static string SaveCase(string path=null)
        {
            RequireMatch();Pause();ReturnToLive();RestoreFootPreview();
            path=path??Path.Combine(ArtifactRoot,"case-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".json");
            lastCase=BeetleDebugSnapshot.Save(Match,path);File.WriteAllText(Path.ChangeExtension(lastCase,"trace.json"),new JArray(trace.Select(t=>t.DeepClone())).ToString(Formatting.Indented));
            LastMessage="现场已保存："+lastCase;return lastCase;
        }
        public static void LoadCase(string path)
        {
            var data=BeetleDebugSnapshot.Read(path);
            if(!Match)CreateSandbox((int)data["seed"],(int)data["arena"]);
            Pause();ReturnToLive();RestoreFootPreview();BeetleDebugSnapshot.Restore(Match,data);BindBones();frames.Clear();trace.Clear();OnStep(Match);lastCase=Path.GetFullPath(path);
            LastMessage="现场已恢复并暂停；可继续单步。";
        }
        public static void ShowFrame(int index)
        {
            RequireMatch();RestoreFootPreview();if(index<0 || index>=frames.Count)throw new ArgumentOutOfRangeException(nameof(index));Pause();
            if(livePose==null)livePose=bones.Select(t=>new DebugBonePose(t)).ToArray();
            var frame=frames[index];for(int i=0;i<bones.Length;i++)frame.bones[i].Apply(bones[i]);SceneView.RepaintAll();
            LastMessage="姿态录像 t="+frame.time.ToString("F2")+"；不是可继续模拟的中途快照。";
        }
        public static void ReturnToLive()
        {
            if(livePose==null)return;
            if(bones!=null && bones.Length==livePose.Length)for(int i=0;i<bones.Length;i++)if(bones[i])livePose[i].Apply(bones[i]);
            livePose=null;Physics.SyncTransforms();SceneView.RepaintAll();
        }
        public static string CaptureImage(string path=null)
        {
            RequireMatch();return BeetleDebugCapture.Save(Match,path??Path.Combine(ArtifactRoot,"view-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".png"));
        }
        public static string SaveClip(string path=null)
        {
            RequireMatch();path=path??Path.Combine(ArtifactRoot,"poses-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".json");
            var clip=new DebugPoseClip{fingerprint=BeetleDebugSnapshot.Fingerprint(Match),paths=bones.Select(BeetleDebugGraph.TransformKey).ToArray(),frames=frames.ToArray()};
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));File.WriteAllText(path,JsonUtility.ToJson(clip));return Path.GetFullPath(path);
        }
        public static void LoadClip(string path)
        {
            RequireMatch();if(new FileInfo(path).Length>64*1024*1024)throw new InvalidOperationException("录像超过64MB限制。");
            var clip=JsonUtility.FromJson<DebugPoseClip>(File.ReadAllText(path));
            if(clip.version!=1 || clip.fingerprint!=BeetleDebugSnapshot.Fingerprint(Match) || !clip.paths.SequenceEqual(bones.Select(BeetleDebugGraph.TransformKey)))throw new InvalidOperationException("录像版本/骨架不匹配。");
            if(clip.frames==null || clip.frames.Length>FrameCapacity || clip.frames.Any(f=>f.bones==null || f.bones.Length!=bones.Length))throw new InvalidOperationException("录像帧结构无效。");
            Pause();ReturnToLive();frames.Clear();frames.AddRange(clip.frames);if(frames.Count>0)ShowFrame(0);
        }
        public static void PreviewMotion(int actor,string moduleName)
        {
            RequireMatch();if(!OwnsSandbox)throw new InvalidOperationException("动作试演只允许在隔离预览场景中进行。");
            if(actor<0 || actor>1)throw new ArgumentOutOfRangeException(nameof(actor));
            Pause();ReturnToLive();RestoreFootPreview();Match.StartMatch(Match.Simulation.Seed);Match.EditorSetPaused(true);
            typeof(BeetleSimulation).GetProperty("Phase").SetValue(Match.Simulation,MatchPhase.Fighting);
            foreach(var b in Match.Simulation.Fighters){b.Action=BeetleAction.Brace;b.ActionTime=0;b.ActionDuration=6;b.DecisionTime=6;}
            var self=Match.Simulation.Fighters[actor];self.Motion.Refresh(Match.Simulation,self);
            var module=Match.settings.motionModules.FirstOrDefault(m=>m && m.name==moduleName);
            if(module){self.Motion.EditorPreviewStart(module);self.Action=BeetleAction.Module;self.ActionDuration=module.duration;}
            else
            {
                if(!Enum.TryParse(moduleName,out BeetleAction action) || !new[]{BeetleAction.Probe,BeetleAction.ShoveWindup,BeetleAction.ChargeWindup,BeetleAction.PryWindup,BeetleAction.HornLock}.Contains(action))throw new ArgumentException("未知试演动作："+moduleName);
                self.Action=action;self.ActionDuration=action==BeetleAction.PryWindup?.62f:action==BeetleAction.HornLock?Match.settings.hornLockSeconds:.35f;
            }
            // 试演从同一个完整现场起算；后续仍走真实状态转换与对手受击/接触，不强造命中。
            previewBaseline=BeetleDebugSnapshot.Capture(Match);frames.Clear();trace.Clear();OnStep(Match);
            LastMessage="已安排动作试演："+moduleName+"。对手保持防守，命中仍需真实接触。";
        }
        public static void SeekPreview(float seconds)
        {
            RequireMatch();if(!OwnsSandbox || previewBaseline==null)throw new InvalidOperationException("先安排一个动作试演。");
            if(float.IsNaN(seconds) || seconds<0 || seconds>5)throw new ArgumentOutOfRangeException(nameof(seconds));
            Pause();ReturnToLive();RestoreFootPreview();BeetleDebugSnapshot.Restore(Match,previewBaseline);frames.Clear();trace.Clear();
            int ticks=Mathf.RoundToInt(seconds/BeetleSimulation.FixedStep);if(ticks>0)Step(ticks);else OnStep(Match);LastMessage="试演从初始现场向前重算到 "+seconds.ToString("F2")+" 秒。";
        }
        public static void SetFootTarget(int actor,int leg,Vector3 point)
        {
            RequireMatch();if(!OwnsSandbox || Playing || !Match.IsPaused || IsViewingClip)throw new InvalidOperationException("拖脚测试需要暂停的隔离预览场景，并退出录像查看。");
            if(actor<0 || actor>1 || leg<0 || leg>5)throw new ArgumentOutOfRangeException();
            if(footBaseline==null)footBaseline=BeetleDebugSnapshot.Capture(Match);
            var a=Match.actors[actor];a.animator.importedRig.Solve(a.animator.legs[leg],point,a.State,Match.ActiveSurface);
            SelectedActor=actor;SelectedLeg=leg;FootOverride=point;SceneView.RepaintAll();
        }
        public static void RestoreFootPreview()
        {
            if(footBaseline==null)return;
            var snapshot=footBaseline;footBaseline=null;FootOverride=null;
            if(Match)BeetleDebugSnapshot.Restore(Match,snapshot);
        }
        public static void Resume()
        {
            RequireMatch();ReturnToLive();RestoreFootPreview();
            if(OwnsSandbox){PlaySlow(1);return;}
            Playing=false;Match.EditorSetPaused(false);LastMessage="已恢复实时对局。";
        }
        public static JObject Inspect()
        {
            RequireMatch();var result=new JObject{{"time",Match.Simulation.MotionTime},{"arena",Match.ArenaName},{"seed",Match.Simulation.Seed},{"paused",Match.IsPaused},{"sandbox",OwnsSandbox},{"viewingClip",IsViewingClip},{"recordedFrames",frames.Count}};
            var fighters=new JArray();foreach(var actor in Match.actors)
            {
                var b=actor.State;var modules=new JArray();foreach(var m in Match.settings.motionModules??Array.Empty<BeetleMotionModule>())if(m)modules.Add(new JObject{{"name",m.name},{"eligibility",b.Motion.EditorEligibility(m)}});
                fighters.Add(new JObject{{"index",b.Index},{"action",b.Action.ToString()},{"intent",b.Intent.ToString()},{"actionTime",b.ActionTime},{"duration",b.ActionDuration},{"decisionWait",b.DecisionTime},{"stamina",b.Stamina},{"burst",b.BurstReserve},{"blockedSeconds",b.ContactBlockedSeconds},{"modules",modules},
                    {"legs",new JArray(actor.animator.legs.Select((leg,i)=>new JObject{{"slot",i},{"stepping",leg.stepping},{"stepPhase",leg.stepTime},{"target",V(leg.planted)},{"actual",V(actor.animator.importedRig.bindings[i].tip.position)},{"error",actor.animator.importedRig.bindings[i].targetError}}))}});
            }
            result["fighters"]=fighters;result["contact"]=new JObject{{"acceptedFraction",Match.Contacts.AcceptedMotionFraction},{"limitedSteps",Match.Contacts.LimitedMotionSteps},{"liftAnchorError",Match.Contacts.LiftAnchorError},{"liftPair",Match.Contacts.LiftContact}};return result;
        }
        /// <summary>供AI/批处理调用的唯一命令分发，与窗口按钮调用同一方法。</summary>
        public static string ExecuteJson(string json)
        {
            string id=null;
            try
            {
                var c=JsonUtility.FromJson<DebugCommand>(json);id=c.id;JToken result;
                switch(c.action)
                {
                    case "sandbox":CreateSandbox(c.seed,c.arena);result=Inspect();break;
                    case "attach":var matches=UnityEngine.Object.FindObjectsByType<BeetleMatchController>(FindObjectsSortMode.None).Where(m=>m.Simulation!=null).ToArray();if(matches.Length!=1)throw new InvalidOperationException("运行对局不唯一，请通过窗口指定。");Attach(matches[0]);result=Inspect();break;
                    case "inspect":result=Inspect();break;
                    case "pause":Pause();result=Inspect();break;
                    case "resume":Resume();result=Inspect();break;
                    case "restore-foot":RestoreFootPreview();result=Inspect();break;
                    case "step":Step(c.steps);result=Inspect();break;
                    case "slow":PlaySlow(c.speed);result=new JValue("slow playback started");break;
                    case "record":SetRecording(c.enabled);result=new JValue(Recording);break;
                    case "capture":result=new JValue(SaveCase(c.path));break;
                    case "screenshot":result=new JValue(CaptureImage(c.path));break;
                    case "restore":LoadCase(c.path);result=Inspect();break;
                    case "preview":PreviewMotion(c.actor,c.module);result=Inspect();break;
                    case "seek-preview":SeekPreview(c.seconds);result=Inspect();break;
                    case "show-frame":ShowFrame(c.frame);result=new JValue(LastMessage);break;
                    case "live":ReturnToLive();result=Inspect();break;
                    case "export-clip":result=new JValue(SaveClip(c.path));break;
                    case "load-clip":LoadClip(c.path);result=new JValue(LastMessage);break;
                    case "trace":result=new JArray(trace.Select(t=>t.DeepClone()));break;
                    case "close":Close();result=new JValue("closed");break;
                    default:throw new ArgumentException("未知调试命令："+c.action);
                }
                return new JObject{{"id",id},{"ok",true},{"result",result}}.ToString(Formatting.None);
            }
            catch(Exception e){LastMessage=e.Message;return new JObject{{"id",id},{"ok",false},{"error",e.Message}}.ToString(Formatting.None);}
        }
    }
}
