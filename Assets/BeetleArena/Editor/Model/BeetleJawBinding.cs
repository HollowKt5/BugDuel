using System.Linq;
using BeetleArena;
using UnityEditor;
using UnityEngine;

/// <summary>Slices anterior mouthparts into left and right mandibles based on geometry; source Part index does not dictate anatomical side.</summary>
public static class BeetleJawBinding
{
    public const float RootZ = 1.50f, OuterX = .20f;
    public static string PieceName(int id, int side) => (side < 0 ? "Left" : "Right") + " jaw from part " + id;
    public static float Weight(Vector3 p) => Mathf.SmoothStep(0,1,Mathf.InverseLerp(RootZ,1.58f,p.z)) *
        Mathf.SmoothStep(0,1,Mathf.InverseLerp(OuterX,.13f,Mathf.Abs(p.x)));

    static Mesh Save(Mesh mesh, string name)
    {
        string path="Assets/BeetleArena/Generated/ImportedModel/"+name+".asset";
        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing) {EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(existing);return existing;}
        AssetDatabase.CreateAsset(mesh,path);return mesh;
    }

    /// <summary>Takes complete mouthpart copy, returns fixed base retained on head; caller owns saving returned mesh.</summary>
    public static Mesh Bind(Transform model, Transform head, Mesh mouth, int id, Renderer template, BeetleContactRig contacts)
    {
        var modelRig=model.GetComponentInParent<BeetleModelRig>();
        if(!modelRig) modelRig=model.root.GetComponentInChildren<BeetleModelRig>();
        if(!modelRig)throw new System.InvalidOperationException("Jaw binding needs an imported model rig.");
        var rig=modelRig.GetComponent<BeetleJawRig>();if(!rig)rig=modelRig.gameObject.AddComponent<BeetleJawRig>();
        modelRig.jaws=rig;
        BeetleHeadMeshSplit.SplitJaw(mouth,out var fixedBase,out var left,out var right);
        Object.DestroyImmediate(mouth);
        for(int side=-1;side<=1;side+=2)
        {
            string name=side<0?"Left jaw pivot":"Right jaw pivot";
            var pivot=head.Find(name);if(!pivot){pivot=new GameObject(name).transform;pivot.SetParent(head,false);}
            pivot.SetPositionAndRotation(model.TransformPoint(new Vector3(side*.16f,.43f,RootZ)),model.rotation);
            if(side<0){rig.left=pivot;rig.leftRest=pivot.localRotation;}else{rig.right=pivot;rig.rightRest=pivot.localRotation;}
            var mesh=side<0?left:right;
            if(mesh.vertexCount==0){Object.DestroyImmediate(mesh);continue;}
            var piece=pivot.Find(PieceName(id,side));if(!piece){piece=new GameObject(PieceName(id,side)).transform;piece.SetParent(pivot,false);}
            var rest=pivot.worldToLocalMatrix*model.localToWorldMatrix;
            piece.localPosition=rest.GetColumn(3);piece.localRotation=rest.rotation;piece.localScale=rest.lossyScale;
            var skin=piece.GetComponent<SkinnedMeshRenderer>();if(!skin)skin=piece.gameObject.AddComponent<SkinnedMeshRenderer>();
            var vertices=mesh.vertices;
            mesh.boneWeights=vertices.Select(p=>new BoneWeight{boneIndex0=0,weight0=1-Weight(p),boneIndex1=1,weight1=Weight(p)}).ToArray();
            mesh.bindposes=new[]{head.worldToLocalMatrix*piece.localToWorldMatrix,pivot.worldToLocalMatrix*piece.localToWorldMatrix};
            skin.sharedMesh=Save(mesh,"Part"+id+(side<0?"_LeftJaw":"_RightJaw"));
            skin.bones=new[]{head,pivot};skin.rootBone=head;skin.quality=SkinQuality.Bone2;
            skin.localBounds=new Bounds(Vector3.up,new Vector3(5,5,7));skin.updateWhenOffscreen=true;
            skin.sharedMaterials=template.sharedMaterials;skin.enabled=template.enabled;
            skin.shadowCastingMode=template.shadowCastingMode;skin.receiveShadows=template.receiveShadows;
            if(contacts && !contacts.surfaces.Contains(skin))contacts.surfaces=contacts.surfaces.Concat(new Renderer[]{skin}).ToArray();
            EditorUtility.SetDirty(skin);EditorUtility.SetDirty(pivot);
        }
        EditorUtility.SetDirty(rig);EditorUtility.SetDirty(modelRig);
        return fixedBase;
    }
}


