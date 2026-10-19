using System.Collections.Generic;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>Actual shell mesh envelope for overturn support; independent from combat contacts.</summary>
    public sealed class CareShellContact
    {
        sealed class Surface { public Transform transform;public Vector3[] points; }
        readonly List<Surface> surfaces=new List<Surface>();
        public CareShellContact(BeetleProceduralAnimator rig)
        {
            foreach(var renderer in rig.shellRenderers)
            {
                if(renderer is SkinnedMeshRenderer skinned)
                {
                    var baked=new Mesh();skinned.BakeMesh(baked);
                    surfaces.Add(new Surface { transform=renderer.transform,points=baked.vertices });
                    if(Application.isPlaying)Object.Destroy(baked);else Object.DestroyImmediate(baked);
                    continue;
                }
                var filter=renderer.GetComponent<MeshFilter>();
                if(filter&&filter.sharedMesh&&filter.sharedMesh.isReadable)
                    surfaces.Add(new Surface { transform=filter.transform,points=filter.sharedMesh.vertices });
            }
        }
        public float LowestPoint()
        {
            float lowest=float.PositiveInfinity;
            foreach(var surface in surfaces)
            {
                var matrix=surface.transform.localToWorldMatrix;
                foreach(var point in surface.points)lowest=Mathf.Min(lowest,matrix.MultiplyPoint3x4(point).y);
            }
            return lowest;
        }
        public void Settle(Transform body,float height,bool restingOnShell)
        {
            float min=LowestPoint();if(!float.IsFinite(min))return;
            float delta=height+.003f-min;
            if(restingOnShell||delta>0)body.position+=Vector3.up*delta;
        }
    }
}
