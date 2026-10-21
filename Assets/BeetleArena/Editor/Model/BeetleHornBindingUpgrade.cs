using System;
using System.IO;
using System.Linq;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Measured anatomy for this bug.fbx only; split fused skull/horn and thorax/horn surfaces.</summary>
public static class BeetleHornBindingUpgrade
{
    public const float CephalicRootZ = 1.70f, ThoracicRootZ = 1.40f, ThoracicRootY = 1.30f;
    const string Folder = "Assets/BeetleArena/Generated/ImportedModel";
    const string PrefabPath = "Assets/BeetleArena/Generated/ArticulatedBeetle.prefab";
    public const string CephalicName = "Cephalic horn surface", ThoracicName = "Thoracic horn surface";

    public static void Configure(BeetleProceduralAnimator anim)
    {
        if (!anim.importedRig) return;
        var binding = anim.GetComponent<BeetleHornRig>();
        if (binding && binding.cephalicSurface && binding.thoracicSurface) return;
        if (!binding) binding = anim.gameObject.AddComponent<BeetleHornRig>();
        binding.head = anim.headPivot;
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/bug.fbx");
        var contactRig = anim.GetComponent<BeetleContactRig>();
        Vector3 headScale = anim.headPivot.localScale;
        Quaternion headRotation = anim.headPivot.localRotation, bodyRotation = anim.bodyPivot.localRotation;
        anim.headPivot.localScale = Vector3.one;
        anim.headPivot.localRotation = anim.bodyPivot.localRotation = Quaternion.identity;
        try
        {
            var pivot = new GameObject("Cephalic horn length").transform;
            pivot.SetParent(anim.headPivot, false);
            pivot.position = anim.model.TransformPoint(new Vector3(0, .725f, CephalicRootZ));
            binding.lengthPivot = pivot;
            foreach (int id in new[] { 5, 3 })
            {
                var original = source.GetComponentsInChildren<MeshFilter>().Single(x => x.name == "tripo_part_" + id).sharedMesh;
                var converted = BeetleImportedModelBuilder.ConvertSourceMesh(original, id);
                BeetleHeadMeshSplit.SplitHorn(converted, id == 5 ? CephalicRootZ : ThoracicRootZ,
                    id == 5 ? -100 : ThoracicRootY, out var shell, out var horn);
                Object.DestroyImmediate(converted);
                var part = anim.model.GetComponentsInChildren<MeshFilter>().Single(x => x.name == "tripo_part_" + id);
                part.sharedMesh = Save(shell, "Part" + id + "_Shell");
                EditorUtility.SetDirty(part);
                if (PrefabUtility.IsPartOfPrefabInstance(part)) PrefabUtility.RecordPrefabInstancePropertyModifications(part);
                var go = new GameObject(id == 5 ? CephalicName : ThoracicName);
                go.transform.SetParent(id == 5 ? pivot : anim.bodyPivot, false);
                Matrix4x4 rest = go.transform.parent.worldToLocalMatrix * anim.model.localToWorldMatrix;
                go.transform.localPosition = rest.GetColumn(3); go.transform.localRotation = rest.rotation;
                go.transform.localScale = rest.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = Save(horn, "Part" + id + "_Horn");
                var surface = go.AddComponent<MeshRenderer>();
                surface.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Part" + id + ".mat");
                if (id == 5) binding.cephalicSurface = surface; else binding.thoracicSurface = surface;
                contactRig.surfaces = contactRig.surfaces.Concat(new Renderer[] { surface }).ToArray();
            }
            // These existing collider transforms stay on the head: no path or bone reparenting.
            binding.contacts = contactRig.shapes.OfType<CapsuleCollider>().Where(c => c.transform.parent == anim.headPivot &&
                (c.name.StartsWith("Cephalic horn ") || c.name.StartsWith("Horn fork ") || c.name.StartsWith("Horn tip ")))
                .Select(c =>
                {
                    if (c.direction != 1 || c.center != Vector3.zero || c.transform.localScale != Vector3.one)
                        throw new InvalidOperationException("Unexpected horn capsule binding: " + c.name);
                    Vector3 half = Vector3.up * Mathf.Max(0, c.height * .5f - c.radius);
                    return new BeetleHornRig.ContactBinding { shape = c,
                        a = anim.headPivot.InverseTransformPoint(c.transform.TransformPoint(-half)),
                        b = anim.headPivot.InverseTransformPoint(c.transform.TransformPoint(half)),
                        restPosition = c.transform.localPosition, restRotation = c.transform.localRotation,
                        restScale = c.transform.localScale, radius = c.radius, height = c.height };
                }).ToArray();
            if (binding.contacts.Length != 7) throw new InvalidOperationException("Expected seven cephalic horn contact segments.");
            EditorUtility.SetDirty(binding); EditorUtility.SetDirty(contactRig);
            if (PrefabUtility.IsPartOfPrefabInstance(contactRig)) PrefabUtility.RecordPrefabInstancePropertyModifications(contactRig);
        }
        finally
        {
            anim.headPivot.localScale = headScale;
            anim.headPivot.localRotation = headRotation; anim.bodyPivot.localRotation = bodyRotation;
        }
    }

    static Mesh Save(Mesh mesh, string name)
    {
        string path = Folder + "/" + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(existing); return existing; }
        AssetDatabase.CreateAsset(mesh, path); return mesh;
    }

    [MenuItem("Tools/Beetle Arena/Correct Horn Anatomy Bindings")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before upgrading bindings.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the open scene first.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
            try { Configure(prefab.GetComponent<BeetleProceduralAnimator>()); PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            // Active game scenes only; retain BeforeMigration as the original reference.
            foreach (string path in new[] { BeetleArenaSceneBuilder.ScenePath, "Assets/Scenes/BeetleNursery.unity" }
                         .Concat(Directory.GetFiles("Assets/Scenes/Campaign", "*.unity").Select(p => p.Replace('\\', '/'))))
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                foreach (var anim in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<BeetleProceduralAnimator>(true)))
                    Configure(anim);
                if (path == CampaignIridescenceSetup.ScenePath) CampaignIridescenceSetup.ApplyToScene(scene);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
    }
}
