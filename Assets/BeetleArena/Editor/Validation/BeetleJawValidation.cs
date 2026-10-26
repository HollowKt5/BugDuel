using System;
using System.IO;
using System.Linq;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEngine;

/// <summary>Bakes mandible skinning in editor, validating fixed seams, independent motion, reset, and numerical safety.</summary>
public static class BeetleJawValidation
{
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static Vector3[] Bake(SkinnedMeshRenderer r)
    {
        var mesh=new Mesh();try{r.BakeMesh(mesh,false);return mesh.vertices.Select(v=>r.transform.position+r.transform.rotation*v).ToArray();}
        finally{UnityEngine.Object.DestroyImmediate(mesh);}
    }
    [MenuItem("Tools/甲壳虫斗/验证/模型与动作/颚部拆分", false, 703)]
    public static void Run()
    {
        var report=new StringBuilder("JAW VALIDATION\n"+DateTimeOffset.Now.ToString("o")+"\n");
        try
        {
            using var fixture=new BeetleValidationScene("JawValidation");
            fixture.Match.StartMatch(71);
            foreach(var actor in fixture.Match.actors)
            {
                var anim=actor.animator;var rig=anim.importedRig.jaws;
                Require(rig && rig.left && rig.right,"Missing jaw rig");
                var pieces=anim.headPivot.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.name.Contains("jaw from part")).ToArray();
                var antenna=anim.antennae.Select(a=>a.GetComponentInChildren<SkinnedMeshRenderer>()).ToArray();
                var antennaBefore=antenna.Select(Bake).ToArray();
                var before=pieces.Select(Bake).ToArray();var headRest=anim.headPivot.localRotation;
                Require(pieces.Length==3,"Duplicate/missing jaw pieces");
                float maxGap=0,maxMotion=0,maxAntennaMotion=0,maxReset=0;
                foreach(float sweep in new[]{-1f,0f,1f})
                {
                    rig.SetPose(1,sweep);
                    for(int j=0;j<pieces.Length;j++)
                    {
                        var piece=pieces[j];var mesh=piece.sharedMesh;var p=mesh.vertices;var uv=mesh.uv;var after=Bake(piece);
                        int id=piece.name.EndsWith(" 2")?2:14;
                        var baseTransform=anim.headPivot.Find(BeetleAntennaBindingUpgrade.MouthName(id==2?-1:1));
                        var baseMesh=baseTransform.GetComponent<MeshFilter>().sharedMesh;var bp=baseMesh.vertices;var buv=baseMesh.uv;
                        Require(p.Length>0,"Empty jaw piece: "+piece.name);
                        Require(actor.GetComponent<BeetleContactRig>().surfaces.Contains(piece),"Unregistered jaw renderer");
                        for(int i=0;i<p.Length;i++)
                        {
                            maxMotion=Mathf.Max(maxMotion,Vector3.Distance(before[j][i],after[i]));
                            if(Mathf.Abs(p[i].z-BeetleJawBinding.RootZ)>1e-6f && Mathf.Abs(Mathf.Abs(p[i].x)-BeetleJawBinding.OuterX)>1e-6f)continue;
                            int k=Enumerable.Range(0,bp.Length).OrderBy(v=>(bp[v]-p[i]).sqrMagnitude+(buv[v]-uv[i]).sqrMagnitude).First();
                            Require(Vector3.Distance(bp[k],p[i])<1e-5f && Vector2.Distance(buv[k],uv[i])<1e-5f,"Jaw root seam/UV missing");
                            maxGap=Mathf.Max(maxGap,Vector3.Distance(after[i],baseTransform.TransformPoint(bp[k])));
                        }
                    }
                    for(int a=0;a<antenna.Length;a++)
                    {var after=Bake(antenna[a]);for(int i=0;i<after.Length;i++)maxAntennaMotion=Mathf.Max(maxAntennaMotion,Vector3.Distance(after[i],antennaBefore[a][i]));}
                }
                rig.ResetPose();
                for(int j=0;j<pieces.Length;j++){var after=Bake(pieces[j]);for(int i=0;i<after.Length;i++)maxReset=Mathf.Max(maxReset,Vector3.Distance(before[j][i],after[i]));}
                Require(maxGap<1e-5f && maxMotion>.01f && maxAntennaMotion<1e-6f && maxReset<1e-5f,"Jaw motion isolation/seam/reset failed");
                Require(Quaternion.Angle(headRest,anim.headPivot.localRotation)<1e-4f,"Jaw changed head pose");
                rig.SetPose(float.NaN,float.PositiveInfinity);Require(Quaternion.Angle(rig.left.localRotation,rig.leftRest)<1e-4f,"Invalid input leaked");
                report.AppendLine($"actor={actor.State.Index}, pieces={pieces.Length}, triangles={string.Join(",",pieces.Select(p=>p.sharedMesh.triangles.Length/3))}, seamGap={maxGap:E3}, jawMotion={maxMotion:F5}, antennaMotion={maxAntennaMotion:E3}, resetError={maxReset:E3}");
            }
            report.AppendLine("PASS: independent jaw pieces, stable root seams, UVs, head/antenna isolation and reset.");
        }
        finally{Directory.CreateDirectory("Artifacts/JawSplit");File.WriteAllText("Artifacts/JawSplit/Validation.txt",report.ToString());}
    }
}

