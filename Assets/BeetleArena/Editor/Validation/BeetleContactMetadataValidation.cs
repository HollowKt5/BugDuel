using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

    /// <summary>Contact semantics, legacy model compatibility, and metadata caching boundaries; decoupled from production scene or model assets.</summary>
public static class BeetleContactMetadataValidation
{
    static void Require(bool condition, string why) { if (!condition) throw new Exception(why); }
    static void Check(BeetleContactRig rig, int index, BeetleContactRole role, int slot, bool weapon, string why)
    {
        Require(rig.Role(index) == role && rig.LegIndex(index) == slot && rig.IsHorn(index) == weapon, why);
    }
    static Collider Shape(Transform root, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        return go.AddComponent<BoxCollider>();
    }
    static void ExpectInvalid(Action action, string why)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception(why);
    }

    [MenuItem("Tools/甲壳虫斗/验证/战斗/接触元数据", false, 504)]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run contact metadata validation outside Play Mode.");
        var previous = SceneManager.GetActiveScene();
        bool dirty = previous.isDirty;
        int sceneCount = SceneManager.sceneCount;
        var report = new StringBuilder("CONTACT METADATA CONTRACTS\n" + DateTimeOffset.Now.ToString("o") + "\n");
        var temporary = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("Contact metadata fixture");
            SceneManager.MoveGameObjectToScene(root, temporary);
            var rig = root.AddComponent<BeetleContactRig>();
            var horn = Shape(root.transform, "Cephalic horn 0");
            var thoracicHorn = Shape(root.transform, "Thoracic horn");
            var unknown = Shape(root.transform, "Right mandible contact");
            var misleading = Shape(root.transform, "Left front mandible contact");
            rig.shapes = new[] { horn, thoracicHorn, unknown, misleading };
            rig.CacheBounds();
            Check(rig, 0, BeetleContactRole.HeadHorn, -1, true, "Legacy head horn changed");
            Check(rig, 1, BeetleContactRole.Body, -1, true, "Legacy thoracic horn changed");
            Check(rig, 2, BeetleContactRole.Body, -1, false, "Unknown shape became a hind leg");
            Check(rig, 3, BeetleContactRole.Body, -1, false, "Unknown shape with directional words became a leg");

            var legs = new List<Collider>();
            for (int slot = 0; slot < 6; slot++)
                foreach (string segment in new[] { "femur", "tibia", "tarsus" })
                    legs.Add(Shape(root.transform, (slot < 3 ? "Left " : "Right ") +
                        new[] { "front", "middle", "hind" }[slot % 3] + " " + segment + " contact"));
            rig.shapes = legs.ToArray(); rig.CacheBounds();
            for (int index = 0; index < 18; index++)
                Check(rig, index, BeetleContactRole.Leg, index / 3, false, "Legacy six-leg segment mapping changed");
            report.AppendLine("PASS: old head/chest horns and all six leg slots; unknown organs remain body contacts.");

            rig.shapes = new[] { horn, unknown }; rig.CacheBounds();
            rig.shapes = new[] { unknown, horn }; rig.CacheBounds();
            Check(rig, 0, BeetleContactRole.Body, -1, false, "Same-length array replacement reused old metadata");
            Check(rig, 1, BeetleContactRole.HeadHorn, -1, true, "Same-length array replacement lost horn metadata");
            rig.shapes[0] = thoracicHorn; rig.CacheBounds();
            Check(rig, 0, BeetleContactRole.Body, -1, true, "In-place collider replacement reused old metadata");
            horn.name = "Unclassified head"; rig.InvalidateShapeMetadata(); rig.CacheBounds();
            Check(rig, 1, BeetleContactRole.Body, -1, false, "Explicit rename invalidation failed");
            horn.name = "Horn tip 1"; rig.InvalidateShapeMetadata(); rig.CacheBounds();
            Check(rig, 1, BeetleContactRole.HeadHorn, -1, true, "Legacy horn-tip fallback failed");
            report.AppendLine("PASS: equal-length and in-place collider replacement; explicit legacy rename invalidation.");

            rig.shapes = new[] { unknown, horn };
            var binding = new BeetleContactRig.ShapeBinding
                { shape = unknown, role = BeetleContactRole.Leg, legIndex = 2, weapon = true };
            rig.shapeBindings = new[] { binding }; rig.CacheBounds();
            Check(rig, 0, BeetleContactRole.Leg, 2, true, "Explicit custom-organ binding failed");
            binding.role = BeetleContactRole.Body; binding.weapon = false; rig.CacheBounds();
            Check(rig, 0, BeetleContactRole.Body, -1, false, "In-place binding edit reused old metadata");
            binding.shape = horn; binding.role = BeetleContactRole.Leg; binding.legIndex = 4; rig.CacheBounds();
            Check(rig, 0, BeetleContactRole.Body, -1, false, "Retargeted binding remained on old collider");
            Check(rig, 1, BeetleContactRole.Leg, 4, false, "Explicit role did not override legacy name");
            rig.shapeBindings = new[] { new BeetleContactRig.ShapeBinding
                { shape = horn, role = BeetleContactRole.Body, weapon = true } };
            rig.CacheBounds();
            Check(rig, 1, BeetleContactRole.Body, -1, true, "Same-length binding array replacement reused old values");
            rig.shapeBindings = null; rig.CacheBounds();
            Check(rig, 1, BeetleContactRole.HeadHorn, -1, true, "Removing explicit bindings did not restore legacy mapping");
            rig.shapeBindings = new BeetleContactRig.ShapeBinding[] { null, new BeetleContactRig.ShapeBinding() };
            rig.CacheBounds();
            Check(rig, 1, BeetleContactRole.HeadHorn, -1, true, "Empty Inspector entries broke legacy mapping");
            report.AppendLine("PASS: explicit role/slot/weapon, edits/retargets/replacement/removal, empty Inspector entries.");

            binding.shape = unknown; binding.role = BeetleContactRole.Leg; binding.legIndex = 6;
            rig.shapeBindings = new[] { binding };
            ExpectInvalid(rig.CacheBounds, "Out-of-range leg slot was accepted");
            binding.legIndex = 0;
            binding.role = (BeetleContactRole)99;
            ExpectInvalid(rig.CacheBounds, "Unknown contact role was accepted");
            binding.role = BeetleContactRole.Leg;
            rig.shapeBindings = new[] { binding, new BeetleContactRig.ShapeBinding { shape = unknown } };
            ExpectInvalid(rig.CacheBounds, "Ambiguous duplicate binding was accepted");
            rig.shapeBindings = new[] { binding }; rig.CacheBounds();
            Check(rig, 0, BeetleContactRole.Leg, 0, false, "Valid repair after rejected configuration failed");
            rig.shapes[1] = null;
            ExpectInvalid(rig.CacheBounds, "Missing collider in cached array was accepted");
            rig.shapes[1] = horn; rig.CacheBounds();
            Check(rig, 1, BeetleContactRole.HeadHorn, -1, true, "Valid repair after missing collider failed");
            report.AppendLine("PASS: invalid slot/role, duplicate binding and missing collider rejected; repaired configuration rebuilds.");
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            EditorSceneManager.ClosePreviewScene(temporary);
            Directory.CreateDirectory("Artifacts/ArchitectureReview");
            File.WriteAllText("Artifacts/ArchitectureReview/ContactMetadata.txt", report.ToString());
        }
        Require(SceneManager.sceneCount == sceneCount && SceneManager.GetActiveScene() == previous &&
            previous.isDirty == dirty, "Validation changed original scene state");
        report.AppendLine("ALL CONTACT METADATA CHECKS PASSED");
        File.WriteAllText("Artifacts/ArchitectureReview/ContactMetadata.txt", report.ToString());
        Debug.Log("All contact metadata checks passed.");
    }
}
