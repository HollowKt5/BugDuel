using System;
using System.Collections.Generic;
using System.Linq;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Mouthparts/antennae kinematic segmentation for current bug.fbx, and incremental upgrade entry for existing scenes.
/// Part2 and Part14 are continuous submeshes; now sliced into distinct antenna/mouthpart meshes and Renderers.
/// Fixed mouthparts follow head; movable mandibles split via BeetleJawBinding; antennae articulate independently with seamless seams.
/// Boundary parameters are calibrated against current geometry, not a generalized insect segmentation algorithm.
/// </summary>
public static class BeetleAntennaBindingUpgrade
{
    const string SourcePath = "Assets/bug.fbx";
    const string MeshFolder = "Assets/BeetleArena/Generated/ImportedModel";
    const string PrefabPath = "Assets/BeetleArena/Generated/ArticulatedBeetle.prefab";

    // All values represent model-space coordinates after BeetleImportedModelBuilder.Point transform, prior to visualScale.
    // Legacy pivot Z=1.45 resided behind the true slender neck; new pivot aligns with neck cross-section centroid.
    public static Vector3 PivotInModel(int side) => new Vector3(side * .235f, .477832f, 1.64343f);

    public static float AntennaWeight(Vector3 point, int side)
    {
        // Part14 also includes lateral posterior mouthparts; filtering strictly on X would inadvertently move these regions.
        // Antenna weights require passing both forward and lateral thresholds; remaining vertices are strictly zero.
        // Both cut seams have exactly zero antenna weight, ensuring duplicated seam edges remain coincident with rigid mouthparts.
        return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(BeetleHeadMeshSplit.LateralCut, .285f, side * point.x)) *
            Mathf.SmoothStep(0, 1, Mathf.InverseLerp(BeetleHeadMeshSplit.ForwardCut, 1.62f, point.z));
    }
    public static string MouthName(int side) => side < 0 ? "Mouth from part 2" : "Mouth from part 14";
    static Mesh SavePart(Mesh mesh, string name)
    {
        string path=MeshFolder+"/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing) {EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(existing);return existing;}
        AssetDatabase.CreateAsset(mesh,path);return mesh;
    }

    /// <summary>
    /// Installs 2-bone skinning onto an existing or newly spawned Part GameObject without altering its parent-child path.
    /// body, head, and pivot must be in bind pose upon invocation; mesh must be a mutable cloned copy.
    /// renderer bindposes derive from its rest transform; must not substitute rotated antenna matrices.
    /// </summary>
    public static SkinnedMeshRenderer BindPart(GameObject part, Transform model, Transform head,
        Transform pivot, Mesh mesh, int side, BeetleContactRig contacts = null)
    {
        RequireEditMode();
        if (!part || !model || !head || !pivot || !mesh || (side != -1 && side != 1))
            throw new ArgumentException("Incomplete antenna binding input.");
        var oldRigid = part.GetComponent<MeshRenderer>();
        var renderer = part.GetComponent<SkinnedMeshRenderer>();
        if (oldRigid && renderer) throw new InvalidOperationException("Part has both rigid and skinned renderers: " + part.name);

        // Avoids moving vertices to accommodate new pivot. Original mesh vertices reside in model space, reconstructing the same rest matrix.
        // Existing GameObjects avoid temporary reparenting to preserve prefab instance hierarchy; new GameObjects establish original paths.
        if (part.transform.parent != pivot) part.transform.SetParent(pivot, false);
        Matrix4x4 restInPivot = pivot.worldToLocalMatrix * model.localToWorldMatrix;
        part.transform.localPosition = restInPivot.GetColumn(3);
        part.transform.localRotation = restInPivot.rotation;
        part.transform.localScale = restInPivot.lossyScale;

        BeetleHeadMeshSplit.Split(mesh,side,out var antennaMesh,out var mouthMesh);
        Vector3[] vertices = antennaMesh.vertices;
        var weights = new BoneWeight[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            float w = AntennaWeight(vertices[i], side);
            weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1 - w, boneIndex1 = 1, weight1 = w };
        }
        antennaMesh.boneWeights = weights;
        Matrix4x4 rendererRest = part.transform.localToWorldMatrix;
        antennaMesh.bindposes = new[] { head.worldToLocalMatrix * rendererRest, pivot.worldToLocalMatrix * rendererRest };

        if (oldRigid)
        {
            // Migration reuses existing materials and render toggles. Avoids re-running MaterialFor to preserve user material tweaks.
            var materials = oldRigid.sharedMaterials; bool visible = oldRigid.enabled, receive = oldRigid.receiveShadows;
            var shadows = oldRigid.shadowCastingMode; var lightProbes = oldRigid.lightProbeUsage;
            var reflectionProbes = oldRigid.reflectionProbeUsage;
            int sortingLayer = oldRigid.sortingLayerID, sortingOrder = oldRigid.sortingOrder;
            var properties = new MaterialPropertyBlock(); oldRigid.GetPropertyBlock(properties);
            var referenceSlots = new List<int>();
            if (contacts && contacts.surfaces != null)
                for (int i = 0; i < contacts.surfaces.Length; i++)
                    if (contacts.surfaces[i] == oldRigid) referenceSlots.Add(i);
            // Preserves reference transform and appearance before removing rigid Renderer, preventing holding two Renderers on same GameObject.
            UnityEngine.Object.DestroyImmediate(oldRigid);
            renderer = part.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMaterials = materials; renderer.enabled = visible;
            renderer.shadowCastingMode = shadows; renderer.receiveShadows = receive;
            renderer.lightProbeUsage = lightProbes; renderer.reflectionProbeUsage = reflectionProbes;
            renderer.sortingLayerID = sortingLayer; renderer.sortingOrder = sortingOrder;
            renderer.SetPropertyBlock(properties);
            foreach (int i in referenceSlots) contacts.surfaces[i] = renderer;
        }
        else if (!renderer) renderer = part.AddComponent<SkinnedMeshRenderer>();
        var filter = part.GetComponent<MeshFilter>();
        if (filter) UnityEngine.Object.DestroyImmediate(filter);
        int id=side<0?2:14;
        renderer.sharedMesh = SavePart(antennaMesh,"Part"+id+"_Antenna");
        renderer.bones = new[] { head, pivot };
        renderer.rootBone = head;
        renderer.quality = SkinQuality.Bone2;
        renderer.updateWhenOffscreen = true;
        // Rotated antennae can exceed static source mesh bounding box; uses conservative bounding volume consistent with legs.
        renderer.localBounds = new Bounds(Vector3.up, new Vector3(5, 5, 7));
        // Discrete mouthpart GameObject is parented to head, not antenna node. Re-running upgrades reuses existing instances/assets without duplicates.
        var mouthTransform=head.Find(MouthName(side));
        if(!mouthTransform) {mouthTransform=new GameObject(MouthName(side)).transform;mouthTransform.SetParent(head,false);}
        var mouthRest=head.worldToLocalMatrix*model.localToWorldMatrix;
        mouthTransform.localPosition=mouthRest.GetColumn(3);mouthTransform.localRotation=mouthRest.rotation;mouthTransform.localScale=mouthRest.lossyScale;
        var mouthFilter=mouthTransform.GetComponent<MeshFilter>();if(!mouthFilter)mouthFilter=mouthTransform.gameObject.AddComponent<MeshFilter>();
        var mouthRenderer=mouthTransform.GetComponent<MeshRenderer>();if(!mouthRenderer)mouthRenderer=mouthTransform.gameObject.AddComponent<MeshRenderer>();
        mouthMesh=BeetleJawBinding.Bind(model,head,mouthMesh,id,renderer,contacts);
        mouthFilter.sharedMesh=SavePart(mouthMesh,"Part"+id+"_Mouth");
        mouthRenderer.sharedMaterials=renderer.sharedMaterials;
        mouthRenderer.shadowCastingMode=renderer.shadowCastingMode;mouthRenderer.receiveShadows=renderer.receiveShadows;
        mouthRenderer.enabled=renderer.enabled;
        if(contacts && !contacts.surfaces.Contains(mouthRenderer)) contacts.surfaces=contacts.surfaces.Concat(new Renderer[]{mouthRenderer}).ToArray();
        EditorUtility.SetDirty(mouthRenderer);EditorUtility.SetDirty(mouthFilter);
        EditorUtility.SetDirty(renderer);
        if (contacts) EditorUtility.SetDirty(contacts);
        return renderer;
    }

    /// <summary>
    /// Ground avoidance sampling queries distal tips that follow antennae completely. Transition zones and mouthparts are driven by head/two-bone blend,
    /// and cannot be treated as rigid antenna surfaces predictable by pivot.TransformPoint.
    /// </summary>
    public static BeetleModelRig.FeelerBinding CreateFeeler(Transform model, Transform pivot, Vector3[] vertices, int side)
    {
        var distal = vertices.Where(p => AntennaWeight(p, side) >= .999999f).ToArray();
        if (distal.Length < 3) throw new InvalidOperationException("No fully weighted antenna vertices; review the source model boundary.");
        var extremities = new List<Vector3>();
        for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
        {
            var axis = new Vector3(x, y, z);
            if (axis.sqrMagnitude > 0) extremities.Add(distal.OrderByDescending(p => Vector3.Dot(p, axis)).First());
        }
        Vector3 lowestWorld = model.TransformPoint(distal.OrderBy(p => p.y).First());
        Vector3 bendWorld = Vector3.Cross(lowestWorld - pivot.position, model.up).normalized;
        return new BeetleModelRig.FeelerBinding
        {
            pivot = pivot,
            surfacePoints = extremities.Distinct().Select(p => pivot.InverseTransformPoint(model.TransformPoint(p))).ToArray(),
            bendAxis = pivot.InverseTransformDirection(bendWorld)
        };
    }

    [MenuItem("Tools/甲壳虫斗/模型与绑定/拆分触角与口器网格", false, 400)]
    public static void Run() => Debug.Log(UpgradeExistingAssets());

    /// <summary>
    /// Incrementally upgrades both scene Actors and existing prefabs without rebuilding scene, UI, arena, or other bones.
    /// Reuses active scene objects if open; otherwise opens additively, saves, and closes without closing other user scenes.
    /// Mutates source prefab using LoadPrefabContents / SaveAsPrefabAsset / UnloadPrefabContents.
    /// </summary>
    public static string UpgradeExistingAssets()
    {
        RequireEditMode();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        if (!source) throw new InvalidOperationException("Missing source FBX: " + SourcePath);
        var sourceParts = SourceParts(source);
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(BeetleArenaSceneBuilder.ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        GameObject prefab = null;
        try
        {
            if (opened) scene = EditorSceneManager.OpenScene(BeetleArenaSceneBuilder.ScenePath, OpenSceneMode.Additive);
            var actors = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<BeetleActor>(true)).ToArray();
            if (actors.Length != 2) throw new InvalidOperationException("Expected exactly two existing scene beetles.");
            prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
            var prefabActor = prefab.GetComponent<BeetleActor>();
            // Validates all inputs upfront; prevents discovering halfway through that the second Actor is incompatible.
            foreach (var actor in actors) ValidateActor(actor);
            ValidateActor(prefabActor);
            UpgradeActor(prefabActor, sourceParts);
            PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath, out bool prefabSaved);
            if (!prefabSaved) throw new InvalidOperationException("Could not save upgraded beetle prefab.");
            // Upgrades prefab first; active scene instances inherit new Renderer, then record necessary scene overrides.
            foreach (var actor in actors) UpgradeActor(actor, sourceParts);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save upgraded beetle scene.");
            AssetDatabase.SaveAssets();
            return "Split antenna/mouth meshes: 2 scene beetles + prefab; 7 independent mesh assets (antennae, fixed bases, jaw pieces); source FBX preserved.";
        }
        finally
        {
            if (prefab) PrefabUtility.UnloadPrefabContents(prefab);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    static Dictionary<int, Mesh> SourceParts(GameObject source)
    {
        var result = new Dictionary<int, Mesh>();
        foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
            if (filter.name == "tripo_part_2" || filter.name == "tripo_part_14")
                result.Add(filter.name == "tripo_part_2" ? 2 : 14, filter.sharedMesh);
        if (result.Count != 2 || !result[2] || !result[14]) throw new InvalidOperationException("Source FBX antenna part names changed.");
        return result;
    }

    static void ValidateActor(BeetleActor actor)
    {
        if (!actor || !actor.animator || !actor.animator.importedRig || !actor.GetComponent<BeetleContactRig>())
            throw new InvalidOperationException("Expected an imported beetle with contact rig.");
        var anim = actor.animator;
        if (!anim.model || !anim.bodyPivot || !anim.headPivot || anim.antennae == null || anim.antennae.Length != 2)
            throw new InvalidOperationException("Incomplete imported beetle hierarchy: " + actor.name);
        for (int i = 0; i < 2; i++)
        {
            int id = i == 0 ? 2 : 14;
            if (!anim.antennae[i] || !anim.antennae[i].Find("tripo_part_" + id))
                throw new InvalidOperationException("Missing original antenna part path on " + actor.name);
        }
    }

    static void UpgradeActor(BeetleActor actor, Dictionary<int, Mesh> sourceParts)
    {
        var anim = actor.animator; var rig = anim.importedRig;
        var contacts = actor.GetComponent<BeetleContactRig>();
        Vector3 bodyPosition = anim.bodyPivot.localPosition;
        Quaternion bodyRotation = anim.bodyPivot.localRotation, headRotation = anim.headPivot.localRotation;
        // Shared mesh bindposes must derive from consistent bind pose; must not bake arbitrary editor preview head angles.
        // Actor/model root transform and scale need not be zeroed: worldToLocal * rendererRest cancels them out.
        anim.bodyPivot.localPosition = rig.bodyRest;
        anim.bodyPivot.localRotation = Quaternion.identity;
        anim.headPivot.localRotation = Quaternion.identity;
        try
        {
            var feelers = new BeetleModelRig.FeelerBinding[2];
            for (int i = 0; i < 2; i++)
            {
                int id = i == 0 ? 2 : 14, side = i == 0 ? -1 : 1;
                var pivot = anim.antennae[i];
                var part = pivot.Find("tripo_part_" + id).gameObject;
                pivot.SetPositionAndRotation(anim.model.TransformPoint(PivotInModel(side)), anim.model.rotation);
                var copy = BeetleImportedModelBuilder.ConvertSourceMesh(sourceParts[id], id);
                try
                {
                    var renderer = BindPart(part, anim.model, anim.headPivot, pivot, copy, side, contacts);
                    feelers[i] = CreateFeeler(anim.model, pivot, renderer.sharedMesh.vertices, side);
                }
                finally { UnityEngine.Object.DestroyImmediate(copy); }
                EditorUtility.SetDirty(pivot);
                if (PrefabUtility.IsPartOfPrefabInstance(part))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(part.GetComponent<SkinnedMeshRenderer>());
                    PrefabUtility.RecordPrefabInstancePropertyModifications(part.transform);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(pivot);
                }
            }
            rig.feelers = feelers;
            EditorUtility.SetDirty(rig); EditorUtility.SetDirty(anim); EditorUtility.SetDirty(contacts);
            if (PrefabUtility.IsPartOfPrefabInstance(actor))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(rig);
                PrefabUtility.RecordPrefabInstancePropertyModifications(contacts);
            }
        }
        finally
        {
            anim.bodyPivot.localPosition = bodyPosition;
            anim.bodyPivot.localRotation = bodyRotation;
            anim.headPivot.localRotation = headRotation;
        }
    }

    static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before changing antenna mesh bindings.");
    }
}

