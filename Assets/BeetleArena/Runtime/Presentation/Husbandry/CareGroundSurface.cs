using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>Foot contacts on low decorative meshes. High obstacles stay in the enclosure route planner.</summary>
    public sealed class CareGroundSurface : IDisposable
    {
        readonly float floorHeight;
        readonly List<MeshCollider> surfaces = new List<MeshCollider>();
        readonly List<MeshCollider> owned = new List<MeshCollider>();
        public CareGroundSurface(CareEnclosureLayout layout)
        {
            floorHeight = layout.GroundHeight;
            foreach (var renderer in layout.GroundDetails)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh) continue;
                var collider = renderer.GetComponent<MeshCollider>();
                if (!collider || !collider.enabled)
                {
                    collider = renderer.gameObject.AddComponent<MeshCollider>();
                    collider.hideFlags = HideFlags.DontSave;
                    collider.sharedMesh = filter.sharedMesh; owned.Add(collider);
                }
                surfaces.Add(collider);
            }
            Physics.SyncTransforms();
        }
        public float HeightAt(Vector3 point)
        {
            float height = floorHeight;
            var ray = new Ray(new Vector3(point.x, floorHeight + .3f, point.z), Vector3.down);
            foreach (var surface in surfaces)
            {
                if (!surface || !surface.enabled || !surface.gameObject.activeInHierarchy) continue;
                var b = surface.bounds;
                if (point.x < b.min.x || point.x > b.max.x || point.z < b.min.z || point.z > b.max.z) continue;
                if (surface.Raycast(ray, out var hit, .35f)) height = Mathf.Max(height, hit.point.y);
            }
            return height;
        }
        public void Dispose()
        {
            foreach (var collider in owned) if (collider)
            {
                collider.enabled = false;
                if (Application.isPlaying) UnityEngine.Object.Destroy(collider); else UnityEngine.Object.DestroyImmediate(collider);
            }
            owned.Clear(); surfaces.Clear();
        }
    }
}
