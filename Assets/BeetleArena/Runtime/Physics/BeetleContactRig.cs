using System;
using UnityEngine;

namespace BeetleArena
{
    public enum BeetleContactRole { Leg, Body, HeadHorn }
    // 真实模型的不可见碰撞代理。shapes 用于物理，surfaces 仅保存可见网格用于诊断。
    // 蒙皮 Renderer.bounds 不能作为独立腿段代理的包围盒，两组数组无需一一对应。
    public sealed class BeetleContactRig : MonoBehaviour
    {
        /// <summary>
        /// 模型接触语义，与对象名称和数组顺序无关。新物种应显式绑定；未绑定的旧模型保留名称回退。
        /// legIndex 按左前/中/后、右前/中/后排列，非腿部忽略此字段。
        /// weapon 标识角部反馈；它不会凭空授予大颚等尚未实现的攻击机制。
        /// </summary>
        [Serializable]
        public sealed class ShapeBinding
        {
            public Collider shape;
            public BeetleContactRole role = BeetleContactRole.Body;
            [Range(-1, 5)] public int legIndex = -1;
            public bool weapon;
        }

        // 由模型生成器绑定的胶囊及凸网格代理，随各自骨骼运动。
        public Collider[] shapes;
        [Tooltip("Optional explicit collision semantics. Unbound shapes retain legacy name mapping; unknown names are body contacts.")]
        public ShapeBinding[] shapeBindings = Array.Empty<ShapeBinding>();
        // 保留源模型的可见 Renderer，便于验证完整模型是否正确迁移。
        public Renderer[] surfaces;
        // 运行时部件 AABB 缓存，worldBounds 是它们合并后的整只甲虫 AABB。
        [NonSerialized] public Bounds[] bounds;
        [NonSerialized] public Bounds worldBounds;
        // Cache references only. Read exact world poses for narrow-phase tests so repeated
        // root translations cannot accumulate rounding error in an independent pose cache.
        [NonSerialized] public Transform[] shapeTransforms;
        BeetleContactRole[] roles;
        int[] legIndices;
        bool[] weapons;
        Collider[] classifiedShapes;
        BindingSnapshot[] classifiedBindings;
        struct BindingSnapshot
        {
            public bool present;
            public Collider shape;
            public BeetleContactRole role;
            public int legIndex;
            public bool weapon;
            public BindingSnapshot(ShapeBinding binding)
            {
                present = binding != null;
                shape = binding?.shape;
                role = binding != null ? binding.role : BeetleContactRole.Body;
                legIndex = binding != null ? binding.legIndex : -1;
                weapon = binding != null && binding.weapon;
            }
            public bool Matches(ShapeBinding binding) => present == (binding != null) &&
                (!present || ReferenceEquals(shape, binding.shape) && role == binding.role &&
                    legIndex == binding.legIndex && weapon == binding.weapon);
        }
        // 模型构建器已有明确命名。每局首次缓存分类一次，避免每个接触对做字符串判断。
        public BeetleContactRole Role(int index) => roles[index];
        public int LegIndex(int index) => legIndices[index];
        // 胸角在物理解算中仍属 Body，但表现采样可把它识别为角；不修改原枚举或序列化值。
        public bool IsHorn(int index) => weapons[index];

        /// <summary>重命名旧模型部件后显式失效；代理和显式绑定更新会自动识别，不在热路径读 name。</summary>
        public void InvalidateShapeMetadata() => classifiedShapes = null;
        void OnValidate() => InvalidateShapeMetadata();
        bool ShapeMetadataCurrent()
        {
            if (classifiedShapes == null || classifiedShapes.Length != shapes.Length || roles == null ||
                classifiedBindings == null || classifiedBindings.Length != (shapeBindings?.Length ?? 0)) return false;
            for (int i = 0; i < shapes.Length; i++)
                if (!shapes[i] || !ReferenceEquals(classifiedShapes[i], shapes[i])) return false;
            for (int i = 0; i < classifiedBindings.Length; i++)
                if (!classifiedBindings[i].Matches(shapeBindings[i])) return false;
            return true;
        }
        void EnsureShapeMetadata()
        {
            if (ShapeMetadataCurrent()) return;
            // 校验失败后保持缓存无效，修复配置后下一次调用可安全重建。
            classifiedShapes = null;
            roles = new BeetleContactRole[shapes.Length];
            legIndices = new int[shapes.Length]; weapons = new bool[shapes.Length];
            int count = shapeBindings?.Length ?? 0;
            for (int i = 0; i < count; i++)
            {
                var binding = shapeBindings[i];
                // Inspector 新添加的空行可暂存；有 collider 的绑定必须明确且有效。
                if (binding == null || !binding.shape) continue;
                if (binding.role != BeetleContactRole.Leg && binding.role != BeetleContactRole.Body &&
                    binding.role != BeetleContactRole.HeadHorn)
                    throw new InvalidOperationException("Invalid contact role at binding " + i);
                if (binding.role == BeetleContactRole.Leg && (binding.legIndex < 0 || binding.legIndex > 5))
                    throw new InvalidOperationException("Leg contact binding requires a slot from 0 to 5 at binding " + i);
                for (int j = 0; j < i; j++)
                    if (shapeBindings[j] != null && shapeBindings[j].shape == binding.shape)
                        throw new InvalidOperationException("Duplicate collision shape binding at index " + i);
            }
            for (int i = 0; i < shapes.Length; i++)
            {
                if (!shapes[i]) throw new InvalidOperationException("Missing beetle collision shape at index " + i);
                ShapeBinding binding = null;
                for (int j = 0; j < count; j++)
                    if (shapeBindings[j] != null && shapeBindings[j].shape == shapes[i])
                    { binding = shapeBindings[j]; break; }
                if (binding != null)
                {
                    roles[i] = binding.role;
                    legIndices[i] = binding.role == BeetleContactRole.Leg ? binding.legIndex : -1;
                    weapons[i] = binding.weapon;
                    continue;
                }
                string n = shapes[i].name;
                roles[i] = n.StartsWith("Cephalic horn", StringComparison.Ordinal) || n.StartsWith("Horn fork", StringComparison.Ordinal) || n.StartsWith("Horn tip", StringComparison.Ordinal)
                    ? BeetleContactRole.HeadHorn : BeetleContactRole.Body;
                legIndices[i] = LegacyLegIndex(n);
                if (roles[i] != BeetleContactRole.HeadHorn && legIndices[i] >= 0) roles[i] = BeetleContactRole.Leg;
                weapons[i] = roles[i] == BeetleContactRole.HeadHorn || n == "Thoracic horn";
            }
            classifiedShapes = (Collider[])shapes.Clone();
            classifiedBindings = new BindingSnapshot[count];
            for (int i = 0; i < count; i++) classifiedBindings[i] = new BindingSnapshot(shapeBindings[i]);
        }
        static int LegacyLegIndex(string name)
        {
            int offset = name.StartsWith("Left ", StringComparison.Ordinal) ? 0 :
                name.StartsWith("Right ", StringComparison.Ordinal) ? 3 : -1;
            if (offset < 0) return -1;
            // 只接受模型构建器的足节名称；例如 Left front mandible 仍是未知器官。
            string part = name.Substring(offset == 0 ? 5 : 6);
            int pair, prefixLength;
            if (part.StartsWith("front ", StringComparison.Ordinal)) { pair = 0; prefixLength = 6; }
            else if (part.StartsWith("middle ", StringComparison.Ordinal)) { pair = 1; prefixLength = 7; }
            else if (part.StartsWith("hind ", StringComparison.Ordinal)) { pair = 2; prefixLength = 5; }
            else return -1;
            string segment = part.Substring(prefixLength);
            return segment == "femur contact" || segment == "tibia contact" || segment == "tarsus contact" ||
                segment == "contact" ? offset + pair : -1;
        }
        // 调用方先 Physics.SyncTransforms，再读取代理 Collider.bounds；不逐帧分配数组。
        public void CacheBounds()
        {
            if (shapes == null || shapes.Length == 0) throw new InvalidOperationException("No collision shapes bound.");
            if (bounds == null || bounds.Length != shapes.Length) bounds = new Bounds[shapes.Length];
            if (shapeTransforms == null || shapeTransforms.Length != shapes.Length)
                shapeTransforms = new Transform[shapes.Length];
            EnsureShapeMetadata();
            for (int i = 0; i < shapes.Length; i++)
            {
                bounds[i] = shapes[i].bounds;
                shapeTransforms[i] = shapes[i].transform;
            }
            worldBounds = bounds[0];
            for (int i = 1; i < bounds.Length; i++) worldBounds.Encapsulate(bounds[i]);
        }
        // 仅整体平移时，根节点与缓存 AABB 同量平移即可，不必重读每个 Renderer。
        // 若改变局部关节/旋转/缩放，必须重新 CacheBounds，不能只调用本方法更新缓存。
        public void Translate(Vector3 movement)
        {
            transform.position += movement;
            for (int i = 0; i < bounds.Length; i++)
                bounds[i].center += movement;
            worldBounds.center += movement;
        }
    }
}
