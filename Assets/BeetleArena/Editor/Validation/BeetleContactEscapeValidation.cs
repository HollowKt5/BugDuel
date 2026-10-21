using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BeetleArena;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 从用户卡死现场保存的权威几何、步态缓存重放。不是完整录像：随机源从种子重建，
/// 检验的是已知互锁姿态能否通过正常固定步退出，并保持碰撞、位移和显示隔离。
/// </summary>
public static class BeetleContactEscapeValidation
{
    const string FixturePath = "Assets/BeetleArena/Editor/Validation/Fixtures/FlatHornDeadlock.json";
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static Vector3 V3(JToken t) => new Vector3((float)t[0], (float)t[1], (float)t[2]);
    static Quaternion Q(JToken t) => new Quaternion((float)t[0], (float)t[1], (float)t[2], (float)t[3]);
    static void RestoreFields(object target, JObject values)
    {
        foreach (var property in values.Properties())
        {
            var field = target.GetType().GetField(property.Name, Fields);
            if (field == null || field.IsInitOnly) throw new Exception("Stale deadlock fixture field: " + property.Name);
            Type type = field.FieldType; JToken v = property.Value;
            object value = type == typeof(Vector3) ? V3(v) : type == typeof(Vector2) ? new Vector2((float)v[0], (float)v[1]) :
                type == typeof(Quaternion) ? Q(v) : type.IsEnum ? Enum.ToObject(type, (int)v) : v.ToObject(type);
            field.SetValue(target, value);
        }
    }
    static void SetProperty(object target, string name, object value) =>
        target.GetType().GetProperty(name, Fields).SetValue(target, value);

    public static void Restore(BeetleMatchController match)
    {
        var snapshot = JObject.Parse(File.ReadAllText(FixturePath));
        match.SelectArena(0); match.StartMatch((int)snapshot["seed"]);
        var sim = match.Simulation;
        SetProperty(sim, "Phase", MatchPhase.Fighting);
        SetProperty(sim, "Elapsed", (float)snapshot["time"]);
        SetProperty(sim, "MotionTime", (float)snapshot["motionTime"]);
        for (int i = 0; i < 2; i++)
        {
            var actor = match.actors[i]; var state = sim.Fighters[i];
            RestoreFields(state.Traits, (JObject)snapshot["traits"][i]);
            RestoreFields(state, (JObject)snapshot["states"][i]);
            actor.Bind(state, match.settings, match.ActiveSurface);
            // 夹具锁定碰撞/步态现场，模型标定后的触角安装点仍由资产拥有。
            // 触角只有旋转动画；重绑口器后不能把旧安装点位置覆盖回新蒙皮绑定。
            var feelerPositions = actor.animator.antennae.SelectMany(p => p.GetComponentsInChildren<Transform>())
                .ToDictionary(t => t, t => t.localPosition);
            RestoreFields(actor.animator, (JObject)snapshot["animators"][i]);
            for (int j = 0; j < actor.animator.legs.Length; j++)
                RestoreFields(actor.animator.legs[j], (JObject)snapshot["legs"][i][j]);
            foreach (var bone in snapshot["bones"][i])
            {
                string path = (string)bone["path"]; var transform = actor.transform.Find(path);
                if (!transform) throw new Exception("Stale deadlock fixture bone: " + path);
                transform.localPosition = feelerPositions.TryGetValue(transform, out var mount) ? mount : V3(bone["p"]);
                transform.localRotation = Q(bone["r"]); transform.localScale = V3(bone["s"]);
            }
            // Heading 是尚未完全接受的朝向指令，必须恢复实际根旋转。夹具从六足世界点
            // 与骨链反求平地 yaw，误差 < 3e-7；来源记录保存在 rootRotationSource。
            actor.transform.rotation = Q(snapshot["rootRotations"][i]);
            actor.CaptureSimulationPose(); actor.BeforeStep();
        }
        var contacts = new BeetleContactSolver(match.actors, match.arenaContacts, .006f);
        sim.ContactSolver = contacts; SetProperty(match, "Contacts", contacts);
        // 以当前安全现场初始化显示缓存；不推进一次 IK，也不把故障姿态替换成新的站姿。
        var view = (BeetleContactMotion[])typeof(BeetleContactSolver).GetField("viewMotion", Fields).GetValue(contacts);
        foreach (var motion in view) motion.Begin();
        typeof(BeetleContactSolver).GetField("viewInitialized", Fields).SetValue(contacts, true);
        Physics.SyncTransforms();
    }

    [MenuItem("Tools/Beetle Arena/Validate Contact Escape")]
    public static void Run()
    {
        var report = new StringBuilder("CAPTURED CONTACT DEADLOCK\n" + DateTimeOffset.Now.ToString("o") + "\n");
        var reference = new Vector3[600, 2];
        bool success = true;
        try
        {
            ValidateHeadRetries(report);
            foreach (int renders in new[] { 0, 1, 4 })
            {
                using var fixture = new BeetleValidationScene("ContactEscapeValidation");
                var match = fixture.Match; Restore(match); var sim = match.Simulation;
                var origins = match.actors.Select(a => a.transform.position).ToArray();
                float longestBlocked = 0, blocked = 0, overlap = 0, excess = 0, firstProgress = -1, renderDifference = 0;
                for (int tick = 0; tick < 600; tick++)
                {
                    var before = sim.Fighters.Select(b => new Vector3(b.Position.x, b.Height, b.Position.y)).ToArray();
                    match.AdvanceFixedStep();
                    blocked = match.Contacts.AcceptedMotionFraction == 0 ? blocked + .02f : 0;
                    longestBlocked = Mathf.Max(longestBlocked, blocked);
                    bool moved = true;
                    for (int i = 0; i < 2; i++)
                    {
                        var b = sim.Fighters[i]; var p = new Vector3(b.Position.x, b.Height, b.Position.y);
                        excess = Mathf.Max(excess, Vector3.Distance(before[i], p) - match.Contacts.MotionBudget(i));
                        moved &= Vector3.Distance(origins[i], p) > .15f;
                        if (renders == 0) reference[tick, i] = p;
                        else renderDifference = Mathf.Max(renderDifference, Vector3.Distance(reference[tick, i], p));
                    }
                    if (moved && firstProgress < 0) firstProgress = (tick + 1) * .02f;
                    overlap = Mathf.Max(overlap, match.Contacts.MeasurePenetration());
                    for (int frame = 1; frame <= renders; frame++)
                    {
                        match.Present(frame / (float)renders, sim.MotionTime, .02f / renders);
                        overlap = Mathf.Max(overlap, match.Contacts.MeasurePenetration());
                    }
                }
                report.AppendLine($"renders={renders}: progress={firstProgress:F2}s, longestBlocked={longestBlocked:F2}s, overlap={overlap:F6}, budgetExcess={excess:F6}, simulationDifference={renderDifference:F7}");
                success &= firstProgress > 0 && firstProgress < 2 && longestBlocked < 1 && overlap < .003f && excess < .0003f && renderDifference < .00001f;
            }
            if (!success) throw new Exception("Captured contact lock persists, exceeds motion/penetration limits, or render feeds back.");
            report.AppendLine("ALL CONTACT ESCAPE CHECKS PASSED");
        }
        finally
        {
            Directory.CreateDirectory("Artifacts/DeadlockFix");
            File.WriteAllText("Artifacts/DeadlockFix/CapturedReplay.txt", report.ToString());
        }
    }

    // 模拟接触重试只还原 Transform 的场景：缓存不能让同一提案越试越低头/抬头。
    static void ValidateHeadRetries(StringBuilder report)
    {
        using var fixture = new BeetleValidationScene("HeadRetryValidation");
        fixture.Match.SelectArena(0); fixture.Match.StartMatch(71);
        var pitchField = typeof(BeetleProceduralAnimator).GetField("headPitch", Fields);
        var yawField = typeof(BeetleProceduralAnimator).GetField("headYaw", Fields);
        for (int i = 0; i < 2; i++)
        {
            var animator = fixture.Match.actors[i].animator;
            foreach (float pitch in new[] { 12f, -30f, -53f, -62f })
            {
                float yaw = i == 0 ? 17 : -13;
                var candidate = Quaternion.Euler(pitch, yaw, 0);
                pitchField.SetValue(animator, pitch); yawField.SetValue(animator, yaw);
                animator.headPivot.localRotation = candidate; animator.YieldHead(4);
                var first = animator.headPivot.localRotation;
                float firstPitch = (float)pitchField.GetValue(animator);
                animator.headPivot.localRotation = candidate; animator.YieldHead(4);
                float expectedPitch = Mathf.Min(pitch, Mathf.Max(-55, pitch - 4));
                float actualPitch = (float)pitchField.GetValue(animator);
                var expected = Quaternion.Euler(expectedPitch, yaw, 0);
                // Quaternion.Angle 在接近零时有量化误差，姿态使用绝对点积比较。
                if (Mathf.Abs(Mathf.DeltaAngle(firstPitch, actualPitch)) >= .0001f ||
                    Mathf.Abs(Mathf.DeltaAngle(expectedPitch, actualPitch)) >= .0001f ||
                    1 - Mathf.Abs(Quaternion.Dot(first, animator.headPivot.localRotation)) >= .000001f ||
                    1 - Mathf.Abs(Quaternion.Dot(expected, animator.headPivot.localRotation)) >= .000001f)
                    throw new Exception($"Head retry changed the same candidate: actor={i}, pitch={pitch}");
            }
        }
        report.AppendLine("PASS: 8 head retry cases are identical, preserve yaw and do not pull back a head raised beyond the yield limit.");
        for (int i = 0; i < 2; i++)
        foreach (float pitch in new[] { -60f, 16f, 34f, 40f })
        {
            var animator = fixture.Match.actors[i].animator;
            var origin = Quaternion.Euler(pitch, 12, 0);
            pitchField.SetValue(animator, pitch);
            animator.headPivot.localRotation = origin;
            bool changed = animator.TryLowerHeadForRetreat(4);
            var first = animator.headPivot.localRotation;
            animator.headPivot.localRotation = origin;
            animator.TryLowerHeadForRetreat(4);
            float expected = pitch >= 35 ? pitch : Mathf.Min(35, pitch + 4);
            if (changed != (pitch < 35) ||
                Mathf.Abs((float)pitchField.GetValue(animator) - pitch) > .0001f ||
                1 - Mathf.Abs(Quaternion.Dot(first, animator.headPivot.localRotation)) > .000001f ||
                1 - Mathf.Abs(Quaternion.Dot(first, Quaternion.Euler(expected, 12, 0))) > .000001f)
                throw new Exception($"Unsafe neck retreat trial: actor={i}, pitch={pitch}");
        }
        report.AppendLine("PASS: 8 neck retreat trials preserve yaw/caches, repeat identically and respect the downward angle limit.");
    }
}
