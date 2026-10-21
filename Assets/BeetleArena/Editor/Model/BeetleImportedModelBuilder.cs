using System;
using System.Collections.Generic;
using System.Linq;
using BeetleArena;
using UnityEditor;
using UnityEngine;

/// <summary>
/// bug.fbx 的显式解剖绑定。该模型是 22 个静态网格，没有原生骨架。
/// 保留所有源顶点、三角形、UV 和贴图；生成独立网格副本及六条三段关节链。
/// 本表是对当前模型的配置，不是任意 FBX 的自动识别器。
/// </summary>
public static class BeetleImportedModelBuilder
{
    const string Folder = "Assets/BeetleArena/Generated/ImportedModel";
    static readonly Dictionary<int, MeshFilter> parts = new Dictionary<int, MeshFilter>();
    // 源模型：Y 朝头部、-X 朝背部、Z 为左右。统一为 Unity Y 向上、Z 朝前。
    // 5 倍归一化后仍由 MatchSettings.visualScale 与个体 size 统一缩放。
    public static Vector3 Point(Vector3 p) => new Vector3(-p.z * 5, (.17f - p.x) * 5, (p.y - .43f) * 5);
    static Vector3 Direction(Vector3 p) => new Vector3(-p.z, -p.x, p.y);

    public static BeetleActor BuildActor(GameObject source, int index)
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/BeetleArena/Generated", "ImportedModel");
        parts.Clear();
        foreach (var f in source.GetComponentsInChildren<MeshFilter>())
            parts.Add(int.Parse(f.name.Substring(f.name.LastIndexOf('_') + 1)), f);
        if (parts.Count != 22) throw new InvalidOperationException("bug.fbx topology changed; review the explicit part bindings.");
        var root = new GameObject(index == 0 ? "01 Amber - Real Beetle" : "02 Ink - Real Beetle");
        var actor = root.AddComponent<BeetleActor>();
        var anim = root.AddComponent<BeetleProceduralAnimator>(); actor.animator = anim;
        var rig = root.AddComponent<BeetleModelRig>(); anim.importedRig = rig;
        anim.model = Node("FBX Model", root.transform, Vector3.zero);
        anim.bodyPivot = Node("Body", anim.model, Point(new Vector3(-.02f, .43f, 0)));
        anim.headPivot = Node("Head horn lever", anim.bodyPivot, Point(new Vector3(.025f, .68f, 0)));
        rig.model = anim.model; rig.body = anim.bodyPivot; rig.bodyRest = anim.bodyPivot.localPosition;
        rig.bindings = new BeetleModelRig.LegBinding[6]; anim.legs = new BeetleLegRig[6];
        var renderers = new List<Renderer>(); var collisions = new List<Collider>();
        // 鞘翅/腹部/前胸硬壳保持刚性；头部与真正的头角共同抬举。
        foreach (int id in new[] { 0, 12, 3, 22 }) renderers.Add(RigidPart(id, anim.model, anim.bodyPivot));
        foreach (int id in new[] { 5, 13, 15, 20, 21 }) renderers.Add(RigidPart(id, anim.model, anim.headPivot));
        anim.antennae = new Transform[2];
        rig.feelers = new BeetleModelRig.FeelerBinding[2];
        for (int i = 0; i < 2; i++)
        {
            int id = i == 0 ? 2 : 14;
            int side = i == 0 ? -1 : 1;
            var pivot = Node(i == 0 ? "Left antenna" : "Right antenna", anim.headPivot,
                BeetleAntennaBindingUpgrade.PivotInModel(side));
            anim.antennae[i] = pivot;
            // 从源三角面裁切出独立触角和口器资源；切口同位、触角根部固定，摆动不裂开。
            var mesh = MeshFor(id);
            var go = new GameObject("tripo_part_" + id);
            var renderer = BeetleAntennaBindingUpgrade.BindPart(go, anim.model, anim.headPivot, pivot, mesh, side);
            UnityEngine.Object.DestroyImmediate(mesh);
            renderer.sharedMaterial = MaterialFor(id);
            renderers.Add(renderer);
            var mouth=anim.headPivot.Find(BeetleAntennaBindingUpgrade.MouthName(side)).GetComponent<MeshRenderer>();
            mouth.sharedMaterial=renderer.sharedMaterial;renderers.Add(mouth);
            foreach(var jaw in anim.headPivot.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.name.EndsWith("jaw from part "+id)))
            {jaw.sharedMaterial=renderer.sharedMaterial;renderers.Add(jaw);}
            rig.feelers[i] = BeetleAntennaBindingUpgrade.CreateFeeler(anim.model, pivot, renderer.sharedMesh.vertices, side);
        }
        for (int sideIndex = 0; sideIndex < 2; sideIndex++)
        for (int pair = 0; pair < 3; pair++)
        {
            int side = sideIndex == 0 ? -1 : 1;
            float sourceSide = -side;
            // 在源模型中测得的股节根、膝、胫跗关节及爪尖。两侧分别使用源网格。
            Vector3 h, k, a, t;
            if (pair == 0) {
                h = new Vector3(.025f, .625f, .090f * sourceSide);
                k = new Vector3(.070f, .625f, .180f * sourceSide);
                a = new Vector3(.115f, .756f, .220f * sourceSide);
                t = new Vector3(.148f, .895f, .229f * sourceSide);
            } else if (pair == 1) {
                h = new Vector3(.012f, .510f, .098f * sourceSide);
                k = new Vector3(.045f, .477f, .179f * sourceSide);
                a = new Vector3(.106f, .405f, .222f * sourceSide);
                t = new Vector3(.155f, .248f, .282f * sourceSide);
            } else {
                h = new Vector3(.004f, .355f, .105f * sourceSide);
                k = new Vector3(.020f, .285f, .179f * sourceSide);
                a = new Vector3(.062f, .180f, .180f * sourceSide);
                t = new Vector3(.062f, .019f, .182f * sourceSide);
            }
            h = Point(h); k = Point(k); a = Point(a); t = Point(t);
            string name = (side < 0 ? "Left " : "Right ") + new[] { "front", "middle", "hind" }[pair];
            var hip = Node(name + " hip", anim.bodyPivot, h);
            var knee = Node(name + " knee", hip, k);
            var ankle = Node(name + " ankle", knee, a);
            var tip = Node(name + " contact", ankle, t);
            Vector3 direction = (a - h).normalized;
            Vector3 bend = Vector3.ProjectOnPlane(k - h, direction).normalized;
            var binding = new BeetleModelRig.LegBinding {
                hip = hip, knee = knee, ankle = ankle, tip = tip,
                bendInBody = anim.bodyPivot.InverseTransformDirection(bend), footOffsetInModel = t - a,
                upperLength = Vector3.Distance(h, k), lowerLength = Vector3.Distance(k, a)
            };
            int n = sideIndex * 3 + pair; rig.bindings[n] = binding;
            anim.legs[n] = new BeetleLegRig {
                hip = hip, knee = knee, ankle = ankle, upper = hip, lower = knee, foot = ankle,
                claws = Array.Empty<Transform>(), teeth = Array.Empty<Transform>(),
                // 中足源站位在落地后已接近完全伸直，向前收 .18 留出支撑与补步余量。
                hipInBody = anim.bodyPivot.InverseTransformPoint(h), restAnkle = t + Vector3.forward * (pair == 2 ? .32f : pair == 1 ? .18f : 0),
                upperLength = binding.upperLength, lowerLength = binding.lowerLength, side = side, pair = pair
            };
            int[] ids = side < 0 ? (pair == 0 ? new[] { 1 } : pair == 1 ? new[] { 16, 10 } : new[] { 6, 18 })
                                : (pair == 0 ? new[] { 7, 9 } : pair == 1 ? new[] { 4, 19 } : new[] { 17, 8 });
            // 由足节原网格计算接触厚度，不能把所有脚的骨骼原点直接当成脚底。
            Vector3 footAxis = (t-a).normalized;
            float footBottom = ids.SelectMany(id => parts[id].sharedMesh.vertices).Select(Point)
                .Where(v => Vector3.Dot(v-a,footAxis) > .06f).Min(v => v.y);
            anim.legs[n].soleOffset = Mathf.Max(0,t.y-footBottom);
            foreach (int id in ids) renderers.Add(SkinnedPart(id, anim.model, new[] { hip, knee, ankle }, new[] { h, k, a, t }));
            collisions.Add(Capsule(name + " femur contact", hip, h, k, .10f));
            collisions.Add(Capsule(name + " tibia contact", knee, k, a, .15f));
            // 爪尖通过 IK 的接地点约束。足节代理细一些，避免台面把支撑腿连同整虫反复顶起。
            collisions.Add(Capsule(name + " tarsus contact", ankle, a, t, .028f));
        }
        // 腹部、前胸使用低面数凸椭球；长角用分段胶囊，保留分叉间的空隙。
        collisions.Add(Ellipsoid("Abdomen contact", anim.bodyPivot, new Vector3(0, 1.02f, -.48f), new Vector3(.82f, .49f, 1.03f)));
        collisions.Add(Ellipsoid("Thorax contact", anim.bodyPivot, new Vector3(0, 1.06f, 1.12f), new Vector3(.74f, .47f, .50f)));
        collisions.Add(Ellipsoid("Head contact", anim.headPivot, Point(new Vector3(.065f, .70f, 0)), new Vector3(.30f, .24f, .28f)));
        Vector3[] horn = { new Vector3(.025f,.710f,0), new Vector3(.023f,.80f,0), new Vector3(-.022f,.885f,0), new Vector3(-.075f,.94f,0) };
        for (int i = 0; i < horn.Length - 1; i++) collisions.Add(Capsule("Cephalic horn " + i, anim.headPivot, Point(horn[i]), Point(horn[i+1]), i == 0 ? .16f : .10f));
        for (int side = -1; side <= 1; side += 2) {
            collisions.Add(Capsule("Horn fork " + side, anim.headPivot, Point(horn[2]), Point(new Vector3(-.071f,.936f,.066f*side)), .078f));
            collisions.Add(Capsule("Horn tip " + side, anim.headPivot, Point(new Vector3(-.071f,.936f,.066f*side)), Point(new Vector3(-.087f,.984f,.053f*side)), .045f));
        }
        collisions.Add(Capsule("Thoracic horn", anim.bodyPivot, Point(new Vector3(-.10f,.695f,0)), Point(new Vector3(-.145f,.766f,0)), .105f));
        anim.shellRenderers = renderers.Where(r => r.name == "tripo_part_0" || r.name == "tripo_part_12" || r.name == "tripo_part_3").ToArray();
        var contacts = root.AddComponent<BeetleContactRig>(); contacts.shapes = collisions.ToArray(); contacts.surfaces = renderers.ToArray();
        BeetleHornBindingUpgrade.Configure(anim);
        var rb = root.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        return actor;
    }

    static Transform Node(string name, Transform parent, Vector3 position)
    {
        var t = new GameObject(name).transform; t.SetParent(parent, false); t.position = position; return t;
    }

    static Mesh MeshFor(int id)
        => ConvertSourceMesh(parts[id].sharedMesh, id);

    // 场景完整构建与增量触角升级共用同一坐标转换，避免旧场景与新建场景方向不一致。
    // 返回独立副本，调用者负责保存或销毁；绝不修改源 FBX Mesh。
    public static Mesh ConvertSourceMesh(Mesh source, int id)
    {
        var mesh = UnityEngine.Object.Instantiate(source); mesh.name = "FBX part " + id;
        mesh.vertices = mesh.vertices.Select(Point).ToArray();
        mesh.normals = mesh.normals.Select(Direction).ToArray();
        var tangents = mesh.tangents;
        for (int i = 0; i < tangents.Length; i++) { var v = Direction(tangents[i]); tangents[i] = new Vector4(v.x, v.y, v.z, tangents[i].w); }
        mesh.tangents = tangents; mesh.RecalculateBounds(); return mesh;
    }

    static Material MaterialFor(int id)
    {
        string path = Folder + "/Part" + id + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool created = !mat;
        if (!mat) mat = new Material(Shader.Find("BeetleArena/Toon")); mat.name = "Imported beetle " + id;
        var original = parts[id].GetComponent<Renderer>().sharedMaterial;
        mat.SetTexture("_BaseMap", original.mainTexture); mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_OutlineWidth", id == 0 || id == 12 || id == 3 ? 1.15f : .55f);
        mat.SetFloat("_SpecStrength", .23f); mat.SetFloat("_SpecPower", 64); mat.SetFloat("_SpecThreshold", .55f);
        mat.SetFloat("_SpecSoftness", .08f); mat.SetFloat("_BandSoftness", .09f); mat.SetFloat("_AmbientStrength", .45f);
        if(created) AssetDatabase.CreateAsset(mat, path); else EditorUtility.SetDirty(mat);
        return mat;
    }

    static Mesh SaveMesh(Mesh mesh, string name)
    {
        string path = Folder + "/" + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing) { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); EditorUtility.SetDirty(existing); return existing; }
        AssetDatabase.CreateAsset(mesh, path); return mesh;
    }

    static Renderer RigidPart(int id, Transform model, Transform bone)
    {
        var go = new GameObject("tripo_part_" + id); go.transform.SetParent(model, false);
        go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(MeshFor(id), "Part" + id);
        var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = MaterialFor(id);
        go.transform.SetParent(bone, true); return renderer;
    }

    static Renderer SkinnedPart(int id, Transform model, Transform[] bones, Vector3[] joints)
    {
        var mesh = MeshFor(id); var vertices = mesh.vertices; var weights = new BoneWeight[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            int nearest = 0; float best = float.MaxValue;
            for (int j = 0; j < 3; j++) {
                Vector3 line = joints[j+1] - joints[j];
                float t = Mathf.Clamp01(Vector3.Dot(vertices[i] - joints[j], line) / line.sqrMagnitude);
                float d = (vertices[i] - (joints[j] + line * t)).sqrMagnitude;
                if (d < best) { best = d; nearest = j; }
            }
            // 大部分硬质腿段为刚性权重，仅在关节附近的窄区间做相邻骨骼混合。
            var w = new BoneWeight { boneIndex0 = nearest, weight0 = 1 };
            for (int joint = 1; joint <= 2; joint++) {
                float r = Vector3.Distance(vertices[i], joints[joint]);
                if (r > .16f || (nearest != joint-1 && nearest != joint)) continue;
                Vector3 bisector = ((joints[joint]-joints[joint-1]).normalized + (joints[joint+1]-joints[joint]).normalized).normalized;
                float blend = Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.075f,.075f,Vector3.Dot(vertices[i]-joints[joint],bisector)));
                w.boneIndex0 = joint-1; w.boneIndex1 = joint; w.weight0 = 1-blend; w.weight1 = blend;
            }
            weights[i] = w;
        }
        mesh.boneWeights = weights;
        mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * model.localToWorldMatrix).ToArray();
        var go = new GameObject("tripo_part_" + id); go.transform.SetParent(model, false);
        var renderer = go.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = SaveMesh(mesh,"Part"+id);
        renderer.bones = bones; renderer.rootBone = bones[0]; renderer.sharedMaterial = MaterialFor(id);
        renderer.updateWhenOffscreen = true; renderer.quality = SkinQuality.Bone2;
        renderer.localBounds = new Bounds(Vector3.up, new Vector3(5,5,7)); return renderer;
    }

    static Collider Capsule(string name, Transform bone, Vector3 a, Vector3 b, float radius)
    {
        var t = Node(name, bone, (a+b)*.5f); t.rotation = Quaternion.FromToRotation(Vector3.up, b-a);
        var c = t.gameObject.AddComponent<CapsuleCollider>(); c.radius = radius; c.height = Vector3.Distance(a,b)+radius*2; c.direction = 1; c.isTrigger = true;
        return c;
    }

    static Collider Ellipsoid(string name, Transform bone, Vector3 center, Vector3 radii)
    {
        var verts = new List<Vector3>(); var tris = new List<int>();
        const int rows=8, cols=12;
        for(int y=0;y<=rows;y++) for(int x=0;x<cols;x++) {
            float phi=Mathf.PI*y/rows, theta=Mathf.PI*2*x/cols;
            verts.Add(Vector3.Scale(new Vector3(Mathf.Sin(phi)*Mathf.Cos(theta),Mathf.Cos(phi),Mathf.Sin(phi)*Mathf.Sin(theta)),radii));
        }
        for(int y=0;y<rows;y++) for(int x=0;x<cols;x++) {int a=y*cols+x,b=y*cols+(x+1)%cols,c=a+cols,d=b+cols; tris.AddRange(new[]{a,c,b,b,c,d});}
        var mesh = new Mesh {name=name}; mesh.SetVertices(verts);mesh.SetTriangles(tris,0);mesh.RecalculateBounds();
        var t=Node(name,bone,center);var collider=t.gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh=SaveMesh(mesh,name);collider.convex=true;collider.isTrigger=true;return collider;
    }
}

