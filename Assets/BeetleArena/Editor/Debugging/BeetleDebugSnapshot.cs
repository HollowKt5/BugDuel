using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace BeetleArena.EditorTools
{
    /// <summary>同版本现场恢复：模拟/随机源/模块/IK/接触/插值缓存完整捕获，音频粒子和相机内部时钟不在范围内。</summary>
    public static class BeetleDebugSnapshot
    {
        const int Version=1;
        static bool Included(FieldInfo field)=>!(field.DeclaringType==typeof(BeetleActor) && field.Name=="marker");
        static IEnumerable<Component> Roots(BeetleMatchController match)
        {
            yield return match;
            foreach(var actor in match.actors)
            {
                yield return actor;yield return actor.animator;
                yield return actor.animator.importedRig;yield return actor.GetComponent<BeetleContactRig>();
                if(actor.animator.importedRig.jaws)yield return actor.animator.importedRig.jaws;
            }
        }
        static IEnumerable<Transform> Bones(BeetleMatchController match)=>match.actors.SelectMany(a=>a.GetComponentsInChildren<Transform>(true));
        public static string Fingerprint(BeetleMatchController match)
        {
            var text=new StringBuilder(Application.unityVersion+"|"+Environment.Version+"|"+Version);
            // 配置内容也参与比较；瞬态 Settings 副本映射到同一个固定键，跨编辑器会话不依赖 instanceID。
            var graph=new BeetleDebugGraph(match);text.Append(graph.WriteFields(match.settings).ToString(Formatting.None));text.Append(graph.Nodes.ToString(Formatting.None));
            foreach(var module in match.settings.motionModules??Array.Empty<BeetleMotionModule>())if(module)text.Append(EditorJsonUtility.ToJson(module));
            text.Append(AssetDatabase.GetAssetDependencyHash(BeetleArenaSceneBuilder.ScenePath));
            foreach(string file in Directory.GetFiles("Assets/BeetleArena/Runtime","*.cs",SearchOption.AllDirectories).OrderBy(p=>p,StringComparer.Ordinal))text.Append(File.ReadAllText(file));
            using var hash=SHA256.Create();return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-","");
        }
        public static JObject Capture(BeetleMatchController match)
        {
            if(!match || match.Simulation==null)throw new InvalidOperationException("没有已启动的对局。");
            var graph=new BeetleDebugGraph(match);var components=new JArray();
            foreach(var component in Roots(match))components.Add(new JObject{{"key",graph.Key(component)},{"fields",graph.WriteFields(component,Included)}});
            var transforms=new JArray();
            foreach(var t in Bones(match))transforms.Add(new JObject{{"key",graph.Key(t)},{"p",graph.Write(t.localPosition)},{"r",graph.Write(t.localRotation)},{"s",graph.Write(t.localScale)}});
            return new JObject{{"version",Version},{"created",DateTimeOffset.Now.ToString("o")},{"unity",Application.unityVersion},
                {"fingerprint",Fingerprint(match)},{"seed",match.Simulation.Seed},{"arena",match.ArenaIndex},{"time",match.Simulation.MotionTime},
                {"scope","simulation + RNG + motion + IK + contacts + pose buffers; no audio/particles/camera-clock"},
                {"components",components},{"transforms",transforms},{"nodes",graph.Nodes}};
        }
        public static void Restore(BeetleMatchController match,JObject data)
        {
            if((int?)data["version"]!=Version || (string)data["fingerprint"]!=Fingerprint(match))
                throw new InvalidOperationException("现场版本/代码/模型/配置不匹配；拒绝覆盖当前对局。请使用保存现场时的版本。");
            var graph=new BeetleDebugGraph(match);
            // 先完全解析、校验所有引用及字段，最后才修改现有组件和 Transform。
            graph.Load((JArray)data["nodes"]);
            var components=((JArray)data["components"]).Select(n=>(node:n,target:graph.Resolve((string)n["key"]))).ToArray();
            var expected=Roots(match).Select(graph.Key).OrderBy(k=>k).ToArray();
            if(!expected.SequenceEqual(components.Select(c=>graph.Key(c.target)).OrderBy(k=>k)))throw new InvalidOperationException("现场组件拓扑不匹配。");
            var changes=new List<(object target,FieldInfo field,object value)>();
            foreach(var c in components)
            {
                var fields=BeetleDebugGraph.Fields(c.target.GetType()).Where(Included).ToArray();var values=(JObject)c.node["fields"];
                if(fields.Length!=values.Count)throw new InvalidOperationException("现场字段数变化。");
                foreach(var field in fields)
                {
                    string key=field.DeclaringType.FullName+"/"+field.Name;
                    if(!values.TryGetValue(key,out var token))throw new InvalidOperationException("现场缺少字段："+key);
                    object value=graph.Read(token);
                    if(value!=null && !field.FieldType.IsInstanceOfType(value))throw new InvalidOperationException("现场字段类型变化："+key);
                    changes.Add((c.target,field,value));
                }
            }
            var poses=((JArray)data["transforms"]).Select(n=>(target:(Transform)graph.Resolve((string)n["key"]),
                p:(Vector3)graph.Read(n["p"]),r:(Quaternion)graph.Read(n["r"]),s:(Vector3)graph.Read(n["s"]))).ToArray();
            if(poses.Length!=Bones(match).Count() || poses.Select(p=>p.target).Distinct().Count()!=poses.Length)throw new InvalidOperationException("现场骨架拓扑不匹配。");
            match.SelectArena((int)data["arena"]);
            foreach(var c in changes)c.field.SetValue(c.target,c.value);
            // Horn length changes native CapsuleCollider dimensions at Bind time. Restoring
            // Transform poses alone leaves the destination match's horn lengths in PhysX.
            // Rebuild those dimensions from the restored entrant before publishing saved poses.
            foreach(var actor in match.actors)
            {
                var horns=actor.animator.importedRig?actor.animator.importedRig.GetComponent<BeetleHornRig>():null;
                if(horns)horns.SetLength(actor.State.Traits.hornLengthFactor);
            }
            foreach(var pose in poses){pose.target.localPosition=pose.p;pose.target.localRotation=pose.r;pose.target.localScale=pose.s;}
            match.EditorReconnectEvents();match.EditorSetPaused(true);
            // 仅重建表现侧反馈，不回写模拟；每次恢复清除旧声音/火星节奏，不声称音视频逐帧复现。
            if(match.contactFeedback)match.contactFeedback.Bind(match.actors);
            foreach(var actor in match.actors)actor.EditorRefreshMarker();
            Physics.SyncTransforms();if(match.hud)match.hud.Refresh();SceneView.RepaintAll();
        }
        public static string Save(BeetleMatchController match,string path)
        {
            path=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary=path+".tmp";File.WriteAllText(temporary,Capture(match).ToString(Formatting.None),Encoding.UTF8);
            if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
            return path;
        }
        public static JObject Read(string path)
        {
            var file=new FileInfo(path);if(file.Length>64*1024*1024)throw new InvalidOperationException("现场文件超过64MB限制。");
            using var reader=new JsonTextReader(File.OpenText(path)){MaxDepth=96,DateParseHandling=DateParseHandling.None};return JObject.Load(reader);
        }
    }
}
