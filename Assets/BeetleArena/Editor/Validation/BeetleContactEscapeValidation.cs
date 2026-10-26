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
/// Replays authoritative geometry and gait cache saved from user deadlock scenarios. Not a full recording: RNG rebuilt from seed.
/// Verifies whether known interlocked poses escape via standard fixed steps while preserving collision, motion, and presentation isolation.
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
            // Fixture locks collision/gait state; calibrated antenna attachment points remain owned by assets.
            // Antenna has rotational animation only; rebinding mouthparts must not overwrite new skinned bindings with legacy anchor positions.
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
            // Heading represents unaccepted steering intent; must restore physical root rotation. Fixture reconstructs planar yaw from
            // six tarsal points and bone chains with error < 3e-7; provenance logged in rootRotationSource.
            actor.transform.rotation = Q(snapshot["rootRotations"][i]);
            actor.CaptureSimulationPose(); actor.BeforeStep();
        }
        var contacts = new BeetleContactSolver(match.actors, match.arenaContacts, .006f);
        sim.ContactSolver = contacts; SetProperty(match, "Contacts", contacts);
        // Initializes display cache from current safe scene without advancing IK or replacing fault pose with new stance.
        var view = (BeetleContactMotion[])typeof(BeetleContactSolver).GetField("viewMotion", Fields).GetValue(contacts);
        foreach (var motion in view) motion.Begin();
        typeof(BeetleContactSolver).GetField("viewInitialized", Fields).SetValue(contacts, true);
        Physics.SyncTransforms();
    }

    [MenuItem("Tools/甲壳虫斗/验证/战斗/接触脱困", false, 505)]
    public static void Run()
    {
        var report = new StringBuilder("CAPTURED CONTACT DEADLOCK\n" + DateTimeOffset.Now.ToString("o") + "\n");
        var reference = new Vector3[600, 2];
        bool success = true;
        try
        {
            ValidateHeadRetries(report);
            ValidateRetreatBudget();
            foreach (int renders in new[] { 0, 1, 4 })
            {
                using var fixture = new BeetleValidationScene("ContactEscapeValidation");
                var match = fixture.Match; Restore(match); var sim = match.Simulation;
                var origins = match.actors.Select(a => a.transform.position).ToArray();
                float longestBlocked = 0, blocked = 0, overlap = 0, excess = 0, firstProgress = -1, renderDifference = 0;
                int maxRetreatTrials = 0;
                for (int tick = 0; tick < 600; tick++)
                {
                    var before = sim.Fighters.Select(b => new Vector3(b.Position.x, b.Height, b.Position.y)).ToArray();
                    match.AdvanceFixedStep();
                    maxRetreatTrials = Mathf.Max(maxRetreatTrials, match.Contacts.LastRetreatTrials);
                    if (maxRetreatTrials > BeetleContactSolver.MaxRetreatTrialsPerStep)
                        throw new Exception("Retreat exceeded the fixed-step candidate budget.");
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
                report.AppendLine($"renders={renders}: progress={firstProgress:F2}s, longestBlocked={longestBlocked:F2}s, overlap={overlap:F6}, budgetExcess={excess:F6}, simulationDifference={renderDifference:F7}, maxRetreatTrials={maxRetreatTrials}");
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

    // Deliberately inadmissible proposals with a tiny positive step exercise the
    // worst-case search. This checks its bound/rollback, not natural escape speed.
    public static void ValidateRetreatBudget()
    {
        using var fixture = new BeetleValidationScene("RetreatBudgetValidation");
        var match = fixture.Match; Restore(match); var sim = match.Simulation;
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var cursor = typeof(BeetleContactSolver).GetField("rootRetreatCursor", fields);
        int previousCursor = (int)cursor.GetValue(match.Contacts);
        for (int step = 0; step < 16; step++)
        {
            var beforeA = match.actors[0].transform.position;
            var beforeB = match.actors[1].transform.position;
            sim.Fighters[0].Position = sim.Fighters[1].Position;
            for (int i = 0; i < 2; i++)
            {
                sim.Fighters[i].ContactBlockedSeconds = 1;
                sim.Fighters[i].Velocity = Vector2.zero;
            }
            match.Contacts.Solve(sim, .0001f);
            int nextCursor = (int)cursor.GetValue(match.Contacts);
            if (match.Contacts.LastRetreatTrials != BeetleContactSolver.MaxRetreatTrialsPerStep || nextCursor == previousCursor)
                throw new Exception("Worst-case retreat did not consume/resume its bounded candidate search at step " + step);
            if (match.Contacts.MeasurePenetration() > .003f ||
                Vector3.Distance(beforeA, match.actors[0].transform.position) > match.Contacts.MotionBudget(0) + .0003f ||
                Vector3.Distance(beforeB, match.actors[1].transform.position) > match.Contacts.MotionBudget(1) + .0003f)
                throw new Exception("Exhausted retreat did not restore a safe bounded pose at step " + step);
            previousCursor = nextCursor;
        }
        Directory.CreateDirectory("Artifacts/Performance");
        File.WriteAllText("Artifacts/Performance/RetreatBudget.txt", "PASS: 16 rejected steps, exactly 6 retreat trials per step, candidate cursor advances and wraps, safe pose restored within motion/penetration limits.\n");
    }

    // Simulates contact retries that restore Transforms only: cache must not cause identical proposals to pitch lower/higher across retries.
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
                // Quaternion.Angle exhibits quantization error near zero; compares poses using absolute dot product.
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

        float[] yaws = { -30, -24, 0, 24, 30 };
        float[] turns = { -4, 4 };
        // Outside the usual range, an outward request stays put and an inward
        // request takes only its requested step; neither snaps back to the limit.
        float[,] expectedYaws = { { -30, -26 }, { -24, -20 }, { -4, 4 }, { 20, 24 }, { 26, 30 } };
        for (int i = 0; i < 2; i++)
        for (int angle = 0; angle < yaws.Length; angle++)
        for (int direction = 0; direction < turns.Length; direction++)
        {
            var animator = fixture.Match.actors[i].animator;
            float yaw = yaws[angle], turn = turns[direction], expected = expectedYaws[angle, direction];
            float pitch = i == 0 ? 11 : -17;
            const float cachedPitch = -43, cachedYaw = 67;
            var origin = Quaternion.Euler(pitch, yaw, 0);
            pitchField.SetValue(animator, cachedPitch); yawField.SetValue(animator, cachedYaw);
            animator.headPivot.localRotation = origin;
            bool changed = animator.TryTurnHeadForRetreat(turn);
            var first = animator.headPivot.localRotation;
            bool firstCachesUnchanged = (float)pitchField.GetValue(animator) == cachedPitch &&
                (float)yawField.GetValue(animator) == cachedYaw;
            animator.headPivot.localRotation = origin;
            bool repeated = animator.TryTurnHeadForRetreat(turn);
            float actual = Mathf.DeltaAngle(0, animator.headPivot.localEulerAngles.y);
            float step = Mathf.DeltaAngle(yaw, actual);
            if (changed != (expected != yaw) || repeated != changed || !firstCachesUnchanged ||
                (float)pitchField.GetValue(animator) != cachedPitch || (float)yawField.GetValue(animator) != cachedYaw ||
                Mathf.Abs(Mathf.DeltaAngle(expected, actual)) > .001f || Mathf.Abs(step) > 4.001f || step * turn < -.001f ||
                (Mathf.Abs(yaw) <= 24 && Mathf.Abs(actual) > 24.001f) ||
                Quaternion.Angle(origin, animator.headPivot.localRotation) > 4.01f ||
                1 - Mathf.Abs(Quaternion.Dot(first, animator.headPivot.localRotation)) > .000001f ||
                1 - Mathf.Abs(Quaternion.Dot(first, Quaternion.Euler(pitch, expected, 0))) > .000001f)
                throw new Exception($"Unsafe sideways neck retreat trial: actor={i}, yaw={yaw}, turn={turn}, actual={actual}");
        }
        report.AppendLine("PASS: 20 sideways neck trials preserve pitch/caches, repeat identically, stay within the normal yaw range and never reverse or snap an out-of-range pose.");
    }
}
