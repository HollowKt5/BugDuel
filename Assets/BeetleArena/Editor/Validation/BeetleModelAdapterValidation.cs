using System;
using System.IO;
using System.Linq;
using System.Text;
using BeetleArena;
using BugDuel.Domain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BeetleModelAdapterValidation
{
    public const string Report = "Artifacts/ModelAdapter/Validation.txt";

    [MenuItem("Tools/甲壳虫斗/验证/模型与动作/可变模型与翅膀绑定", false, 707)]
    public static void Run()
    {
        if (EditorApplication.isPlaying || AnimationMode.InAnimationMode()) throw new InvalidOperationException("Stop Play and animation previews first.");
        var scene = EditorSceneManager.NewPreviewScene();
        var log = new StringBuilder(); int checks = 0;
        Directory.CreateDirectory("Artifacts/ModelAdapter");
        void Check(bool ok, string message)
        {
            log.AppendLine((ok ? "PASS " : "FAIL ") + (++checks) + " " + message);
            if (!ok) throw new InvalidOperationException(message);
        }
        try
        {
            foreach (int count in new[] { 0, 1, 3, 8, 13 })
            {
                var adapter = Synthetic(scene, count);
                Check(adapter.Validate(out var error), $"{count} accessory joints: model rest binding valid ({error})");
                var animator = adapter.GetComponent<BeetleProceduralAnimator>(); var rig = animator.importedRig;
                var appendages = adapter.GetComponent<BeetleAppendageRig>();
                Check(appendages.Validate(out error), $"{count} accessory joints: appendage binding valid ({error})");
                Array.Reverse(rig.bindings);
                Check(Enumerable.Range(0, 6).All(slot => rig.BindingFor(animator.LegForSlot(slot)).hip ==
                    adapter.legs.First(l => (int)l.slot == slot).hip), "Leg lookup ignores both binding and visual array order");
                var traits = new BeetleArena.BeetleTraits { size = 1, mass = 1, endurance = 100, hornLengthFactor = 1.6f };
                var state = new BeetleState(0, traits, Vector2.zero, 0);
                float maxError = 0;
                foreach (var leg in animator.legs)
                {
                    Vector3 target = rig.BindingFor(leg).tip.position;
                    rig.Solve(leg, target, state, null);
                    maxError = Mathf.Max(maxError, Vector3.Distance(target, rig.BindingFor(leg).tip.position));
                }
                Check(maxError < .001f, "Nonidentity bone axes and extra tarsal bones preserve foot contact, error=" + maxError);
                rig.ResetRig();
                Check(adapter.legs.All(l => Quaternion.Angle(l.ankle.localRotation, l.captured.ankleRest) < .01f), "Reset restores source ankle bind rotations");
                var care = new CarePosePresenter(animator); care.Reset(false, false);
                for (int frame = 0; frame < 180; frame++) care.Present(CareState.Idle, 0, 1f / 60);
                Check(care.Feet.SupportCount >= 3 && care.Feet.MaximumError < .005f,
                    "New model works through care initialization/foot IK, error=" + care.Feet.MaximumError);
                if (count > 0) CheckAppendages(appendages, animator, care, Check);
                var clone = UnityEngine.Object.Instantiate(adapter.gameObject);
                SceneManager.MoveGameObjectToScene(clone, scene);
                var clonedAdapter = clone.GetComponent<BeetleModelAdapter>();
                var clonedAnimator = clone.GetComponent<BeetleProceduralAnimator>();
                var settings = ScriptableObject.CreateInstance<BeetleMatchSettings>();
                try
                {
                    clonedAnimator.Initialize(traits, settings, Color.white);
                    var clonedAppendages = clonedAnimator.appendages;
                    clonedAppendages.SetFlight(1, 1, 2);
                    for (int frame = 0; frame < 120; frame++) clonedAnimator.Pose(state, frame / 60f, 1f / 60);
                    Check(clonedAppendages.Pose.extension == 1 && float.IsFinite(clonedAnimator.bodyPivot.position.y),
                        "New model animates optional wings through the combat presentation owner");
                    clonedAnimator.Initialize(traits, settings, Color.white);
                    Check(clonedAnimator.headPivot.localScale == Vector3.one && clonedAppendages.Pose.extension == 0,
                        "Reinitialize closes wings and never stretches an imported head to fake a horn");
                    Check(clonedAdapter.legs.All(l => l.captured.hip.IsChildOf(clone.transform)) &&
                        clonedAppendages.joints.All(j => j.capturedPivot.IsChildOf(clone.transform)),
                        "Cloned model keeps its own serialized rest references and binds independently");
                }
                finally { UnityEngine.Object.DestroyImmediate(settings); UnityEngine.Object.DestroyImmediate(clone); }
                UnityEngine.Object.DestroyImmediate(adapter.gameObject);
            }

            var invalid = Synthetic(scene, 3); var original = invalid.GetComponent<BeetleProceduralAnimator>().legs;
            invalid.legs[1].slot = invalid.legs[0].slot;
            Check(!invalid.Validate(out _), "Duplicate logical leg slot rejected before animation");
            bool threw = false;
            try { invalid.ApplyBindings(); } catch (InvalidOperationException) { threw = true; }
            Check(threw && ReferenceEquals(original, invalid.GetComponent<BeetleProceduralAnimator>().legs), "Invalid model does not partially replace live bindings");
            var invalidAppendages = invalid.GetComponent<BeetleAppendageRig>();
            invalidAppendages.joints[1].pivot = invalidAppendages.joints[0].pivot;
            Check(!invalidAppendages.Validate(out _), "Duplicate joint ownership rejected");
            invalidAppendages.joints = new[] { new BeetleAppendageRig.Joint { pivot = invalid.legs[0].knee } };
            Check(!invalidAppendages.Validate(out _, false), "Appendage/IK ownership conflict rejected");
            UnityEngine.Object.DestroyImmediate(invalid.gameObject);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BeetleArena/Generated/ArticulatedBeetle.prefab");
            var existing = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); existing.SetActive(false);
            var legacy = existing.GetComponent<BeetleProceduralAnimator>();
            Array.Reverse(legacy.importedRig.bindings); Array.Reverse(legacy.legs);
            var legacyCare = new CarePosePresenter(legacy); legacyCare.Reset(false, false);
            for (int frame = 0; frame < 240; frame++)
            {
                existing.transform.Rotate(0, .6f, 0); legacyCare.Present(CareState.Wandering, .16f, 1f / 60);
            }
            Check(legacyCare.Feet.SupportCount >= 3 && legacyCare.Feet.MaximumError < .02f, "Existing beetle supports turning with reordered leg/binding arrays");
            log.AppendLine(CareMotionValidation.CheckLogic());
            Check(true, "Existing care cadence, support, grooming and pause regressions");
            CheckCombat(Check);
            log.AppendLine("COMPLETE PASS");
        }
        catch (Exception ex) { log.AppendLine(ex.ToString()); throw; }
        finally { EditorSceneManager.ClosePreviewScene(scene); File.WriteAllText(Report, log.ToString()); }
    }

    static void CheckAppendages(BeetleAppendageRig appendages, BeetleProceduralAnimator animator,
        CarePosePresenter care, Action<bool, string> check)
    {
        var joint = appendages.joints[0]; var rest = joint.rest;
        var pose = new BeetleAppendagePose { extension = .3f, flapStrength = 1, flapPhase = .25f };
        check(pose.Sample(BeetlePoseChannel.ShellOpen) > 0 && pose.Sample(BeetlePoseChannel.WingDeploy) == 0 &&
            pose.Sample(BeetlePoseChannel.WingFlap) == 0, "Elytra open before hindwings deploy or flap");
        pose.extension = .7f;
        check(pose.Sample(BeetlePoseChannel.ShellOpen) == 1 && pose.Sample(BeetlePoseChannel.WingDeploy) > 0 &&
            pose.Sample(BeetlePoseChannel.WingFlap) == 0, "Hindwings fold before elytra close; partial deploy cannot flap");
        pose.extension = 1;
        appendages.Apply(pose); var opened = joint.pivot.localRotation;
        for (int i = 0; i < 100; i++) appendages.Apply(pose);
        check(Quaternion.Angle(opened, joint.pivot.localRotation) < .01f && Quaternion.Angle(opened, rest) > 1,
            "Arbitrary local axes animate from captured rest without cumulative drift");
        var buffer = new BeetlePoseBuffer(animator.model);
        buffer.BeginStep(); appendages.ResetPose(); buffer.Capture(); buffer.Render(.5f);
        var halfway = joint.pivot.localRotation; buffer.Render(.5f);
        check(Quaternion.Angle(halfway, joint.pivot.localRotation) < .01f && Quaternion.Angle(halfway, rest) > .1f,
            "All added wing transforms participate in idempotent pose interpolation");
        buffer.BeginStep();
        check(Quaternion.Angle(joint.pivot.localRotation, rest) < .01f, "Authoritative rest restored before next simulation step");
        appendages.SetFlight(1, 1, 2);
        for (int i = 0; i < 60; i++) appendages.Advance(1f / 60);
        var phase = appendages.Pose.flapPhase; var held = joint.pivot.localRotation;
        appendages.Advance(0);
        check(appendages.Pose.flapPhase == phase && Quaternion.Angle(held, joint.pivot.localRotation) < .01f, "Zero dt freezes wing phase");
        care.PresentAmbient(CareState.Idle, .1f);
        check(appendages.Pose.flapPhase == phase, "Paused care sensing cannot advance wings or feeding");
        appendages.ResetPose();
        check(Quaternion.Angle(rest, joint.pivot.localRotation) < .01f, "Reset returns wings to their original folded bind pose");
        appendages.Apply(new BeetleAppendagePose { extension = float.NaN, flapStrength = float.PositiveInfinity, flapPhase = float.NaN });
        check(float.IsFinite(joint.pivot.localRotation.x), "Invalid channel inputs cannot poison transforms");
        bool dirty = appendages.gameObject.scene.isDirty;
        using (var preview = new BeetleAppendagePreview(appendages))
        {
            preview.Sample(pose);
            check(Quaternion.Angle(rest, joint.pivot.localRotation) > 1, "Editor preview evaluates configured joints");
        }
        check(!AnimationMode.InAnimationMode() && Quaternion.Angle(rest, joint.pivot.localRotation) < .01f &&
            dirty == appendages.gameObject.scene.isDirty, "Editor preview restores transforms without dirtying the scene");
    }

    static BeetleModelAdapter Synthetic(Scene scene, int count)
    {
        var root = new GameObject("Adapter fixture " + count); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, scene);
        var adapter = root.AddComponent<BeetleModelAdapter>();
        adapter.model = Node("Model", root.transform, Vector3.zero, Quaternion.identity);
        adapter.body = Node("Body", adapter.model, Vector3.up * .8f, Quaternion.identity);
        adapter.head = Node("Head", adapter.body, new Vector3(0, .8f, 1), Quaternion.identity);
        adapter.legs = new BeetleModelAdapter.Leg[6];
        int[] order = { 5, 0, 4, 1, 3, 2 };
        for (int i = 0; i < order.Length; i++)
        {
            int slot = order[i], side = slot < 3 ? -1 : 1; float z = .65f - (slot % 3) * .65f;
            var hip = Node("Hip " + slot, adapter.body, new Vector3(side * .3f, .8f, z), Quaternion.Euler(12, 19, side * 8));
            var knee = Node("Knee " + slot, hip, new Vector3(side * .75f, .5f, z - .12f), Quaternion.Euler(24, -13, side * 30));
            var ankle = Node("Ankle " + slot, knee, new Vector3(side * .9f, .13f, z + .12f), Quaternion.Euler(18, 27, side * 16));
            var tarsus = Node("Extra tarsus " + slot, ankle, new Vector3(side * .93f, .08f, z + .18f), Quaternion.Euler(5, 31, 9));
            var tip = Node("Tip " + slot, tarsus, new Vector3(side * .95f, .03f, z + .24f), Quaternion.identity);
            adapter.legs[i] = new BeetleModelAdapter.Leg { slot = (BeetleLegSlot)slot, hip = hip, knee = knee, ankle = ankle, tip = tip };
        }
        adapter.CaptureRestPose(); adapter.ApplyBindings();
        var appendages = root.AddComponent<BeetleAppendageRig>(); appendages.joints = new BeetleAppendageRig.Joint[count];
        for (int i = 0; i < count; i++)
        {
            var parent = i > 1 && i % 2 == 0 ? appendages.joints[i - 1].pivot : adapter.body;
            var pivot = Node("Accessory " + i, parent, new Vector3(i * .03f, 1, -.2f), Quaternion.Euler(9, i * 17, 23));
            appendages.joints[i] = new BeetleAppendageRig.Joint
            {
                label = "Joint " + i, pivot = pivot,
                rotations = new[] {
                    new BeetleAppendageRig.Rotation { channel = i == 0 ? BeetlePoseChannel.ShellOpen : BeetlePoseChannel.WingDeploy,
                        localAxis = new Vector3(1, .2f, .5f), degrees = i % 2 == 0 ? 55 : -55 },
                    new BeetleAppendageRig.Rotation { channel = BeetlePoseChannel.WingFlap, localAxis = Vector3.forward, degrees = 25, phaseOffset = i * .05f }
                }
            };
        }
        appendages.CaptureRestPose(); return adapter;
    }

    static Transform Node(string name, Transform parent, Vector3 position, Quaternion rotation)
    {
        var node = new GameObject(name).transform; node.SetParent(parent, false); node.SetPositionAndRotation(position, rotation); return node;
    }

    static void CheckCombat(Action<bool, string> check)
    {
        using var fixture = new BeetleValidationScene("AdapterCombatValidation"); var match = fixture.Match;
        foreach (var actor in match.actors) { Array.Reverse(actor.animator.legs); Array.Reverse(actor.animator.importedRig.bindings); }
        match.StartMatch(7319);
        float repeatError = 0;
        for (int tick = 0; tick < 300; tick++)
        {
            foreach (var actor in match.actors) actor.BeforeStep();
            match.Simulation.Step(.02f);
            foreach (var actor in match.actors)
            {
                actor.Render(.5f, match.Simulation.Elapsed, .02f);
                Vector3 before = actor.animator.LegForSlot(0).ankle.position;
                actor.Render(.5f, match.Simulation.Elapsed, .02f);
                repeatError = Mathf.Max(repeatError, Vector3.Distance(before, actor.animator.LegForSlot(0).ankle.position));
                checkFinite(actor.animator.bodyPivot.position);
            }
        }
        check(repeatError < .0001f, "Existing combat + contact solver runs 300 steps with reordered model bindings; repeat error=" + repeatError);
        void checkFinite(Vector3 value) { if (!float.IsFinite(value.x + value.y + value.z)) throw new InvalidOperationException("Nonfinite combat pose"); }
    }
}
