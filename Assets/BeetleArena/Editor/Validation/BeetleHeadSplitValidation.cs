using System;
using System.IO;
using System.Linq;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEngine;

/// <summary>验证实际分开的网格、无表面遗漏、纹理接缝以及蒙皮后的切口连续性。</summary>
public static class BeetleHeadSplitValidation
{
    static void Require(bool value,string message) {if(!value)throw new Exception(message);}
    static double Area(Vector3[] vertices,int[] triangles)
    {
        double area=0;for(int i=0;i<triangles.Length;i+=3)
            area+=Vector3.Cross(vertices[triangles[i+1]]-vertices[triangles[i]],vertices[triangles[i+2]]-vertices[triangles[i]]).magnitude*.5;
        return area;
    }
    static Vector3[] Bake(SkinnedMeshRenderer renderer)
    {
        var mesh=new Mesh();try {renderer.BakeMesh(mesh,false);return mesh.vertices.Select(v=>renderer.transform.position+renderer.transform.rotation*v).ToArray();}
        finally {UnityEngine.Object.DestroyImmediate(mesh);}
    }
    [MenuItem("Tools/Beetle Arena/Validate Split Head Meshes")]
    public static void Run()
    {
        var report=new StringBuilder("SPLIT HEAD MESH VALIDATION\n"+DateTimeOffset.Now.ToString("o")+"\n");
        try
        {
            using var fixture=new BeetleValidationScene("SplitHeadValidation");
            var match=fixture.Match;match.SelectArena(0);match.StartMatch(71);
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/bug.fbx");
            foreach(var actor in match.actors)
            {
                var anim=actor.animator;
                for(int index=0;index<2;index++)
                {
                    int id=index==0?2:14,side=index==0?-1:1;
                    var antenna=anim.antennae[index].Find("tripo_part_"+id).GetComponent<SkinnedMeshRenderer>();
                    var mouth=anim.headPivot.Find(BeetleAntennaBindingUpgrade.MouthName(side)).GetComponent<MeshRenderer>();
                    Require(antenna && mouth && antenna.gameObject!=mouth.gameObject,"Independent render objects missing");
                    var a=antenna.sharedMesh;var m=mouth.GetComponent<MeshFilter>().sharedMesh;
                    Require(a!=m && AssetDatabase.GetAssetPath(a).EndsWith("_Antenna.asset") && AssetDatabase.GetAssetPath(m).EndsWith("_Mouth.asset"),"Independent mesh assets missing");
                    Require(actor.GetComponent<BeetleContactRig>().surfaces.Contains(antenna) && actor.GetComponent<BeetleContactRig>().surfaces.Contains(mouth),"Missing renderer registration");
                    var original=source.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="tripo_part_"+id).sharedMesh;
                    double originalArea=Area(original.vertices.Select(BeetleImportedModelBuilder.Point).ToArray(),original.triangles);
                    var jaws=anim.headPivot.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.name.EndsWith("jaw from part "+id)).ToArray();
                    Require(jaws.Length==(id==2?2:1),"Missing jaw pieces");
                    double areaError=Math.Abs(Area(a.vertices,a.triangles)+Area(m.vertices,m.triangles)+jaws.Sum(j=>Area(j.sharedMesh.vertices,j.sharedMesh.triangles))-originalArea);
                    Require(areaError<originalArea*1e-5,"Split omitted or duplicated source surface");
                    Require(a.vertices.All(v=>v.x*side>=.235f-1e-6f && v.z>=1.60f-1e-6f),"Mouth geometry remains in antenna mesh");
                    var av=a.vertices;var mv=m.vertices;var auv=a.uv;var muv=m.uv;
                    var seams=Enumerable.Range(0,av.Length).Where(i=>Mathf.Abs(av[i].x*side-.235f)<1e-6f || Mathf.Abs(av[i].z-1.60f)<1e-6f).ToArray();
                    Require(seams.Length>0,"No cut boundary found");
                    var partners=seams.Select(i=>Enumerable.Range(0,mv.Length).OrderBy(j=>(mv[j]-av[i]).sqrMagnitude+(muv[j]-auv[i]).sqrMagnitude).First()).ToArray();
                    float uvError=0;for(int s=0;s<seams.Length;s++)
                    {
                        Require(Vector3.Distance(av[seams[s]],mv[partners[s]])<1e-5f,"Cut boundaries do not meet");
                        uvError=Mathf.Max(uvError,Vector2.Distance(auv[seams[s]],muv[partners[s]]));
                    }
                    Require(uvError<1e-5f,"UV seam mismatch");
                    var pivot=anim.antennae[index];var rotation=pivot.localRotation;
                    var other=anim.antennae[1-index].GetComponentInChildren<SkinnedMeshRenderer>();
                    var before=Bake(antenna);var otherBefore=Bake(other);var mouthBefore=mv.Select(mouth.transform.TransformPoint).ToArray();
                    float seamError=0,mouthMotion=0,tipMotion=0,otherMotion=0;
                    try
                    {
                        foreach(var test in new[]{Quaternion.Euler(28,side*32,18),Quaternion.Euler(-45,side*55,-25),Quaternion.Euler(65,0,0)})
                        {
                            pivot.localRotation=rotation*test;var after=Bake(antenna);var otherAfter=Bake(other);
                            for(int s=0;s<seams.Length;s++)seamError=Mathf.Max(seamError,Vector3.Distance(after[seams[s]],mouth.transform.TransformPoint(mv[partners[s]])));
                            for(int v=0;v<mv.Length;v++)mouthMotion=Mathf.Max(mouthMotion,Vector3.Distance(mouthBefore[v],mouth.transform.TransformPoint(mv[v])));
                            for(int v=0;v<av.Length;v++)if(av[v].x*side>.4f)tipMotion=Mathf.Max(tipMotion,Vector3.Distance(before[v],after[v]));
                            for(int v=0;v<otherBefore.Length;v++)otherMotion=Mathf.Max(otherMotion,Vector3.Distance(otherBefore[v],otherAfter[v]));
                        }
                        Require(seamError<1e-5f && mouthMotion<1e-6f && otherMotion<1e-6f && tipMotion>.03f,"Motion separation or animated seam failed");
                    }
                    finally {pivot.localRotation=rotation;}
                    report.AppendLine($"actor={actor.State.Index}, part={id}: triangles={a.triangles.Length/3}+{m.triangles.Length/3}, areaError={areaError:E3}, seamVertices={seams.Length}, uvError={uvError:E3}, animatedSeamError={seamError:E3}, mouthMotion={mouthMotion:E3}, otherMotion={otherMotion:E3}, tipMotion={tipMotion:F5}");
                }
                Require(anim.model.GetComponentsInChildren<Renderer>().Count(r=>r.name.StartsWith("Mouth from part"))==2,"Duplicate mouth objects");
            }
            report.AppendLine("PASS: independent mesh assets/objects, complete surface, matching UVs, fixed mouth and continuous animated seams.");
        }
        finally {Directory.CreateDirectory("Artifacts/HeadSplit");File.WriteAllText("Artifacts/HeadSplit/Validation.txt",report.ToString());}
    }
}

