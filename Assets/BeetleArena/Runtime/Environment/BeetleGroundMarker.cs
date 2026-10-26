using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeetleArena
{
    /// <summary>Runtime ground-conforming faction ring. Vertices query ground independently to prevent depth fighting between flat disc and curved terrain.</summary>
    public sealed class BeetleGroundMarker : IDisposable
    {
        const int Segments = 48;
        readonly Transform target;
        readonly MeshFilter filter;
        readonly Renderer renderer;
        readonly Mesh original, mesh;
        readonly Vector3 originalScale;
        readonly Vector3[] vertices = new Vector3[Segments * 2];
        readonly Vector3[] normals = new Vector3[Segments * 2];
        readonly Vector2[] offsets = new Vector2[Segments * 2];
        readonly IBeetleGround ground;
        readonly int[] triangles = new int[Segments * 6];
        Vector2 lastCenter;
        bool hasSample, topologyUploaded, disposed;
        readonly bool originalEnabled, originalReceiveShadows;
        readonly ShadowCastingMode originalShadowCasting;

        public BeetleGroundMarker(Transform target, IBeetleGround ground, float size)
        {
            this.target = target; this.ground = ground;
            filter = target.GetComponent<MeshFilter>(); renderer = target.GetComponent<Renderer>();
            original = filter.sharedMesh; originalScale = target.localScale;
            originalEnabled = renderer.enabled; originalReceiveShadows = renderer.receiveShadows;
            originalShadowCasting = renderer.shadowCastingMode;
            mesh = new Mesh { name = "Runtime conforming team ring", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic(); filter.sharedMesh = mesh;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * 2 * Mathf.PI / Segments;
                Vector2 direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                offsets[i * 2] = direction * (.68f * size); offsets[i * 2 + 1] = direction * (.82f * size);
                int a = i * 2, b = ((i + 1) % Segments) * 2, t = i * 6;
                triangles[t] = a; triangles[t + 1] = a + 1; triangles[t + 2] = b + 1;
                triangles[t + 3] = a; triangles[t + 4] = b + 1; triangles[t + 5] = b;
            }
        }

        public void Update(Vector2 center, bool visible)
        {
            renderer.enabled = visible;
            if (!visible) return;
            target.SetPositionAndRotation(new Vector3(center.x, 0, center.y), Quaternion.identity);
            target.localScale = Vector3.one;
            // Marker does not track tiny sub-frame displacements; caches center to minimize terrain queries at rest while tracking current visual pose.
            if (hasSample && (center - lastCenter).sqrMagnitude < .000001f) return;
            bool supported = true;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector2 p = center + offsets[i];
                float height = 0;
                if (ground != null)
                {
                    if (!ground.Sample(p, out var hit)) { supported = false; break; }
                    height = hit.height;
                }
                vertices[i] = new Vector3(offsets[i].x, height + .045f, offsets[i].y);
                normals[i] = Vector3.up;
            }
            // Conceals marker along cliff edges rather than dangling ungrounded rings or clipping into steep drops.
            renderer.enabled = supported;
            if (!supported) { hasSample = false; return; }
            mesh.vertices = vertices;
            // Topology and normals remain constant across center movements; uploaded once per runtime mesh.
            if (!topologyUploaded) { mesh.normals = normals; mesh.triangles = triangles; topologyUploaded = true; }
            mesh.RecalculateBounds();
            lastCenter = center; hasSample = true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (filter) filter.sharedMesh = original;
            if (target) target.localScale = originalScale;
            if (renderer) { renderer.enabled = originalEnabled; renderer.receiveShadows = originalReceiveShadows;
                renderer.shadowCastingMode = originalShadowCasting; }
            if (mesh) { if (Application.isPlaying) UnityEngine.Object.Destroy(mesh); else UnityEngine.Object.DestroyImmediate(mesh); }
        }
    }
}
