using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeetleArena
{
    /// <summary>运行时贴地阵营环。每个顶点单独查询地面，避免平圆盘与曲面交叉产生深度闪烁。</summary>
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
        bool hasSample;

        public BeetleGroundMarker(Transform target, IBeetleGround ground, float size)
        {
            this.target = target; this.ground = ground;
            filter = target.GetComponent<MeshFilter>(); renderer = target.GetComponent<Renderer>();
            original = filter.sharedMesh; originalScale = target.localScale;
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
            // 标记无需每帧追踪微小位移；缓存中心，减少静止时地形查询。仍使用当前画面中的位置。
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
            // 边缘处宁可隐藏标记，也不让缺支撑的环悬在台外或刺入悬崖。
            renderer.enabled = supported;
            if (!supported) { hasSample = false; return; }
            mesh.vertices = vertices; mesh.normals = normals; mesh.triangles = triangles; mesh.RecalculateBounds();
            lastCenter = center; hasSample = true;
        }

        public void Dispose()
        {
            if (filter) filter.sharedMesh = original;
            if (target) target.localScale = originalScale;
            if (renderer) renderer.enabled = true;
            if (mesh) { if (Application.isPlaying) UnityEngine.Object.Destroy(mesh); else UnityEngine.Object.DestroyImmediate(mesh); }
        }
    }
}
