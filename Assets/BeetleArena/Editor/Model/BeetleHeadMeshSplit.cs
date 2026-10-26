using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Geometric mesh slicing for current model: slices triangles along two calibration planes to generate decoupled antenna/mouthpart meshes.
/// Boundary-straddling triangles are interpolated, preserving original surfaces and UVs without dropping triangles.
/// Both halves duplicate cut seam edges so assembled seams align without overlapping caps.
/// </summary>
public static class BeetleHeadMeshSplit
{
    public const float LateralCut = .235f, ForwardCut = 1.60f;
    struct Vertex
    {
        public Vector3 p, n;
        public Vector2 uv, uv2;
        public Vector4 tangent;
        public Color color;
        public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex {
            p = Vector3.LerpUnclamped(a.p,b.p,t), n = Vector3.LerpUnclamped(a.n,b.n,t).normalized,
            uv = Vector2.LerpUnclamped(a.uv,b.uv,t), uv2 = Vector2.LerpUnclamped(a.uv2,b.uv2,t),
            tangent = Vector4.LerpUnclamped(a.tangent,b.tangent,t), color = Color.LerpUnclamped(a.color,b.color,t)
        };
    }
    sealed class Output
    {
        readonly List<Vertex> vertices = new List<Vertex>();
        readonly Dictionary<Vertex,int> indices = new Dictionary<Vertex,int>();
        readonly List<int> triangles = new List<int>();
        int Add(Vertex vertex)
        {
            if (indices.TryGetValue(vertex,out int index)) return index;
            index=vertices.Count; vertices.Add(vertex); indices.Add(vertex,index); return index;
        }
        public void Polygon(List<Vertex> polygon)
        {
            for(int i=1;i+1<polygon.Count;i++)
            {
                if(Vector3.Cross(polygon[i].p-polygon[0].p,polygon[i+1].p-polygon[0].p).sqrMagnitude<1e-18f) continue;
                triangles.Add(Add(polygon[0])); triangles.Add(Add(polygon[i])); triangles.Add(Add(polygon[i+1]));
            }
        }
        public Mesh Mesh(string name, bool uv2Present, bool colorsPresent, bool tangentsPresent)
        {
            var mesh=new Mesh { name=name, indexFormat=vertices.Count>65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            var positions=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var uv2=new List<Vector2>();var colors=new List<Color>();var tangents=new List<Vector4>();
            foreach(var v in vertices) {positions.Add(v.p);normals.Add(v.n);uv.Add(v.uv);uv2.Add(v.uv2);colors.Add(v.color);tangents.Add(v.tangent);}
            mesh.SetVertices(positions);mesh.SetNormals(normals);mesh.SetUVs(0,uv);
            if(uv2Present)mesh.SetUVs(1,uv2);if(colorsPresent)mesh.SetColors(colors);if(tangentsPresent)mesh.SetTangents(tangents);
            mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();return mesh;
        }
    }
    // Positive and negative half-spaces form a complete partition; intersection vertices snap onto plane, avoiding seam floating-point drift.
    static List<Vertex> Clip(List<Vertex> polygon, int axis, float sign, float cut, bool inside)
    {
        var result=new List<Vertex>();
        if(polygon.Count==0)return result;
        Vertex a=polygon[polygon.Count-1];float da=sign*a.p[axis]-cut;
        foreach(var b in polygon)
        {
            float db=sign*b.p[axis]-cut;
            bool keepA=inside?da>=0:da<=0, keepB=inside?db>=0:db<=0;
            if(keepA!=keepB)
            {
                var cross=Vertex.Lerp(a,b,da/(da-db));cross.p[axis]=cut/sign;result.Add(cross);
            }
            if(keepB)result.Add(b);a=b;da=db;
        }
        return result;
    }
    /// <summary>Partition a rigid part by measured forward/height planes, retaining source surface and UVs.</summary>
    public static void SplitHorn(Mesh source, float forwardCut, float heightCut, out Mesh shell, out Mesh horn)
    {
        if (!source || source.subMeshCount != 1) throw new ArgumentException("Expected one source surface.");
        var p = source.vertices; var n = source.normals; var uv = source.uv;
        var uv2 = source.uv2; var t = source.tangents; var c = source.colors;
        var data = new Vertex[p.Length];
        for (int i = 0; i < p.Length; i++) data[i] = new Vertex { p = p[i], n = n[i], uv = uv[i],
            uv2 = uv2.Length == p.Length ? uv2[i] : Vector2.zero,
            tangent = t.Length == p.Length ? t[i] : Vector4.zero, color = c.Length == p.Length ? c[i] : Color.white };
        var shellOut = new Output(); var hornOut = new Output(); var triangles = source.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            var face = new List<Vertex> { data[triangles[i]], data[triangles[i + 1]], data[triangles[i + 2]] };
            shellOut.Polygon(Clip(face, 2, 1, forwardCut, false));
            var front = Clip(face, 2, 1, forwardCut, true);
            shellOut.Polygon(Clip(front, 1, 1, heightCut, false));
            hornOut.Polygon(Clip(front, 1, 1, heightCut, true));
        }
        shell = shellOut.Mesh(source.name + " shell", uv2.Length == p.Length, c.Length == p.Length, t.Length == p.Length);
        horn = hornOut.Mesh(source.name + " horn", uv2.Length == p.Length, c.Length == p.Length, t.Length == p.Length);
    }
    public static void Split(Mesh source, int side, out Mesh antenna, out Mesh mouth)
    {
        if(!source || (side!=-1 && side!=1) || source.subMeshCount!=1)throw new ArgumentException("Review this model's head split inputs/submeshes.");
        var p=source.vertices;var n=source.normals;var uv=source.uv;var uv2=source.uv2;var tangent=source.tangents;var colors=source.colors;
        if(n.Length!=p.Length || uv.Length!=p.Length)throw new ArgumentException("Head source must include normals and UV0.");
        var data=new Vertex[p.Length];
        for(int i=0;i<p.Length;i++)data[i]=new Vertex {p=p[i],n=n[i],uv=uv[i],uv2=uv2.Length==p.Length?uv2[i]:Vector2.zero,
            tangent=tangent.Length==p.Length?tangent[i]:Vector4.zero,color=colors.Length==p.Length?colors[i]:Color.white};
        var distal=new Output();var central=new Output();var triangles=source.triangles;
        for(int i=0;i<triangles.Length;i+=3)
        {
            var face=new List<Vertex>{data[triangles[i]],data[triangles[i+1]],data[triangles[i+2]]};
            var outside=Clip(face,0,side,LateralCut,true);
            central.Polygon(Clip(face,0,side,LateralCut,false));
            central.Polygon(Clip(outside,2,1,ForwardCut,false));
            distal.Polygon(Clip(outside,2,1,ForwardCut,true));
        }
        antenna=distal.Mesh(source.name+" antenna",uv2.Length==p.Length,colors.Length==p.Length,tangent.Length==p.Length);
        mouth=central.Mesh(source.name+" mouth",uv2.Length==p.Length,colors.Length==p.Length,tangent.Length==p.Length);
    }
    /// <summary>Sub-partitions anterior central mouthparts; remaining surfaces stay as fixed base, with interpolated vertices along boundaries.</summary>
    public static void SplitJaw(Mesh source, out Mesh fixedBase, out Mesh left, out Mesh right)
    {
        var p=source.vertices;var n=source.normals;var uv=source.uv;var uv2=source.uv2;var t=source.tangents;var c=source.colors;
        var data=new Vertex[p.Length];
        for(int i=0;i<p.Length;i++)data[i]=new Vertex{p=p[i],n=n[i],uv=uv[i],uv2=uv2.Length==p.Length?uv2[i]:Vector2.zero,
            tangent=t.Length==p.Length?t[i]:Vector4.zero,color=c.Length==p.Length?c[i]:Color.white};
        var baseOut=new Output();var leftOut=new Output();var rightOut=new Output();var triangles=source.triangles;
        for(int i=0;i<triangles.Length;i+=3)
        {
            var face=new List<Vertex>{data[triangles[i]],data[triangles[i+1]],data[triangles[i+2]]};
            baseOut.Polygon(Clip(face,2,1,BeetleJawBinding.RootZ,false));
            var front=Clip(face,2,1,BeetleJawBinding.RootZ,true);
            baseOut.Polygon(Clip(front,0,1,BeetleJawBinding.OuterX,true));
            var withinRight=Clip(front,0,1,BeetleJawBinding.OuterX,false);
            baseOut.Polygon(Clip(withinRight,0,-1,BeetleJawBinding.OuterX,true));
            var center=Clip(withinRight,0,-1,BeetleJawBinding.OuterX,false);
            leftOut.Polygon(Clip(center,0,1,0,false));rightOut.Polygon(Clip(center,0,1,0,true));
        }
        fixedBase=baseOut.Mesh(source.name+" fixed base",uv2.Length==p.Length,c.Length==p.Length,t.Length==p.Length);
        left=leftOut.Mesh(source.name+" left jaw",uv2.Length==p.Length,c.Length==p.Length,t.Length==p.Length);
        right=rightOut.Mesh(source.name+" right jaw",uv2.Length==p.Length,c.Length==p.Length,t.Length==p.Length);
    }
}
