using System;
using System.Collections.Generic;
using System.Linq;
using BeetleArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 当前 bug.fbx 的口器/触角运动分区，以及现有场景的增量升级入口。
/// Part2/14 各是一块连续网格；现在裁切为独立触角/口器 Mesh 和 Renderer。
/// 固定口器跟随头部，可动颚片由 BeetleJawBinding 拆分；触角独立摆动，切口保留连接。
/// 该边界来自当前模型测量，不是任意昆虫模型的自动分割算法。
/// </summary>
public static class BeetleAntennaBindingUpgrade
{
    const string SourcePath = "Assets/bug.fbx";
    const string MeshFolder = "Assets/BeetleArena/Generated/ImportedModel";
    const string PrefabPath = "Assets/BeetleArena/Generated/ArticulatedBeetle.prefab";

    // 所有数值均为 BeetleImportedModelBuilder.Point 转换后的、尚未乘 visualScale 的模型坐标。
    // 原枢轴 Z=1.45 位于真正细颈后方；新的枢轴取颈部截面中心。
    public static Vector3 PivotInModel(int side) => new Vector3(side * .235f, .477832f, 1.64343f);

    public static float AntennaWeight(Vector3 point, int side)
    {
        // Part14 还包含外侧但靠后的口器；只检查左右位置会错误带动这部分。
        // 前向门槛与外侧门槛同时成立才允许触角权重，其他顶点精确为零。
        // 两条真实切口上的权重都严格为0，复制的边缘才能与刚性口器保持重合。
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
    /// 为一个已有或新建的同名 Part GO 安装两骨蒙皮，不改变其最终父子路径。
    /// 调用时 body/head/pivot 必须处于绑定姿势，mesh 必须是可修改的生成副本。
    /// renderer 的绑定矩阵来自它自己的静止变换，不能以已旋转的触角矩阵代替。
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

        // 不移动顶点来迁就新枢轴。原网格顶点本来处于 model 坐标，故重建同一静止矩阵。
        // 现有 GO 不临时换父节点，避免破坏预制体实例结构；新建 GO 才建立原来的父子路径。
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
            // 迁移使用现有材质/渲染开关。不要重新跑 MaterialFor 重置用户已经调整的材质。
            var materials = oldRigid.sharedMaterials; bool visible = oldRigid.enabled, receive = oldRigid.receiveShadows;
            var shadows = oldRigid.shadowCastingMode; var lightProbes = oldRigid.lightProbeUsage;
            var reflectionProbes = oldRigid.reflectionProbeUsage;
            int sortingLayer = oldRigid.sortingLayerID, sortingOrder = oldRigid.sortingOrder;
            var properties = new MaterialPropertyBlock(); oldRigid.GetPropertyBlock(properties);
            var referenceSlots = new List<int>();
            if (contacts && contacts.surfaces != null)
                for (int i = 0; i < contacts.surfaces.Length; i++)
                    if (contacts.surfaces[i] == oldRigid) referenceSlots.Add(i);
            // 先保存引用位置与外观，再移除刚性 Renderer，避免同一 GO 短暂拥有两种 Renderer。
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
        // 触角旋转后可能超出源网格静止包围盒；使用与现有六足一致的保守模型范围。
        renderer.localBounds = new Bounds(Vector3.up, new Vector3(5, 5, 7));
        // 独立口器对象挂在头部，而不是触角节点。重复升级复用同一对象/资产，不生成副本。
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
    /// 避地采样只取完全跟随触角的远端。过渡带/口器受两骨或头部控制，
    /// 不能继续把它们当作 pivot.TransformPoint 可准确预测的刚性触角表面。
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

    [MenuItem("Tools/Beetle Arena/Split Antenna And Mouth Meshes")]
    public static void Run() => Debug.Log(UpgradeExistingAssets());

    /// <summary>
    /// 增量升级正式场景两只 Actor 和已有预制体；不重建场景、UI、擂台或其他骨骼。
    /// 场景若已打开就沿用现有对象，否则临时 Additive 打开，保存后关闭；不关闭用户其他场景。
    /// 使用 LoadPrefabContents/SaveAsPrefabAsset/UnloadPrefabContents 修改原预制体。
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
            // 先核对所有输入，缺失绑定时不要做到一半才发现另一个 Actor 不适用。
            foreach (var actor in actors) ValidateActor(actor);
            ValidateActor(prefabActor);
            UpgradeActor(prefabActor, sourceParts);
            PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath, out bool prefabSaved);
            if (!prefabSaved) throw new InvalidOperationException("Could not save upgraded beetle prefab.");
            // 先升级 Prefab，场景中若有它的实例便继承新 Renderer，再记录自己的必要覆盖。
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
        // 共享 Mesh 的 bindposes 必须来自一致的绑定姿势，不能把某次编辑器预览的头部角度烘进去。
        // Actor/model 整体位置与缩放不用清零：worldToLocal * rendererRest 会抵消它们。
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

