using System;
using UnityEngine;

namespace BeetleArena
{
    public enum BeetleContactRole { Leg, Body, HeadHorn }
    // 真实模型的不可见碰撞代理。shapes 用于物理，surfaces 仅保存可见网格用于诊断。
    // 蒙皮 Renderer.bounds 不能作为独立腿段代理的包围盒，两组数组无需一一对应。
    public sealed class BeetleContactRig : MonoBehaviour
    {
        // 由模型生成器绑定的胶囊及凸网格代理，随各自骨骼运动。
        public Collider[] shapes;
        // 保留源模型的可见 Renderer，便于验证完整模型是否正确迁移。
        public Renderer[] surfaces;
        // 运行时部件 AABB 缓存，worldBounds 是它们合并后的整只甲虫 AABB。
        [NonSerialized] public Bounds[] bounds;
        [NonSerialized] public Bounds worldBounds;
        BeetleContactRole[] roles;
        int[] legIndices;
        bool[] weapons;
        Collider[] classifiedShapes;
        // 模型构建器已有明确命名。每局首次缓存分类一次，避免每个接触对做字符串判断。
        public BeetleContactRole Role(int index) => roles[index];
        public int LegIndex(int index) => legIndices[index];
        // 胸角在物理解算中仍属 Body，但表现采样可把它识别为角；不修改原枚举或序列化值。
        public bool IsHorn(int index) => weapons[index];

        /// <summary>改变代理数组/部件命名后显式失效；不会在热路径重新读 Unity 的 name 属性。</summary>
        public void InvalidateShapeMetadata() => classifiedShapes = null;
        void OnValidate() => InvalidateShapeMetadata();
        void EnsureShapeMetadata()
        {
            if (ReferenceEquals(classifiedShapes, shapes) && roles != null && roles.Length == shapes.Length) return;
            roles = new BeetleContactRole[shapes.Length];
            legIndices = new int[shapes.Length]; weapons = new bool[shapes.Length];
            for (int i = 0; i < shapes.Length; i++)
            {
                if (!shapes[i]) throw new InvalidOperationException("Missing beetle collision shape at index " + i);
                string n = shapes[i].name;
                roles[i] = n.StartsWith("Cephalic horn", StringComparison.Ordinal) || n.StartsWith("Horn fork", StringComparison.Ordinal) || n.StartsWith("Horn tip", StringComparison.Ordinal)
                    ? BeetleContactRole.HeadHorn : n == "Abdomen contact" || n == "Thorax contact" || n == "Head contact" || n == "Thoracic horn"
                    ? BeetleContactRole.Body : BeetleContactRole.Leg;
                legIndices[i] = roles[i] != BeetleContactRole.Leg ? -1 :
                    (n.StartsWith("Left ", StringComparison.Ordinal) ? 0 : 3) + (n.Contains("front") ? 0 : n.Contains("middle") ? 1 : 2);
                weapons[i] = roles[i] == BeetleContactRole.HeadHorn || n == "Thoracic horn";
            }
            classifiedShapes = shapes;
        }
        // 调用方先 Physics.SyncTransforms，再读取代理 Collider.bounds；不逐帧分配数组。
        public void CacheBounds()
        {
            if (shapes == null || shapes.Length == 0) throw new InvalidOperationException("No collision shapes bound.");
            if (bounds == null || bounds.Length != shapes.Length) bounds = new Bounds[shapes.Length];
            EnsureShapeMetadata();
            for (int i = 0; i < shapes.Length; i++) bounds[i] = shapes[i].bounds;
            worldBounds = bounds[0];
            for (int i = 1; i < bounds.Length; i++) worldBounds.Encapsulate(bounds[i]);
        }
        // 仅整体平移时，根节点与缓存 AABB 同量平移即可，不必重读每个 Renderer。
        // 若改变局部关节/旋转/缩放，必须重新 CacheBounds，不能只调用本方法更新缓存。
        public void Translate(Vector3 movement)
        {
            transform.position += movement;
            for (int i = 0; i < bounds.Length; i++) bounds[i].center += movement;
            worldBounds.center += movement;
        }
    }
}
