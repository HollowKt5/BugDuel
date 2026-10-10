using UnityEngine;

namespace BeetleArena
{
    /// <summary>地表查询结果：高度为世界 Y，法线朝外；只代表可支撑的擂台表面。</summary>
    public struct BeetleGroundSample
    {
        public float height, grip;
        public Vector3 normal;
    }

    // 数据模拟只依赖查询接口；无场景测试仍可使用默认水平圆台或自定义地表实现。
    public interface IBeetleGround
    {
        bool Sample(Vector2 position, out BeetleGroundSample sample);
    }

    /// <summary>指定擂台的地表提供者。直接查询指定 Collider，不会射中甲虫或台外地板。</summary>
    public sealed class BeetleArenaSurface : MonoBehaviour, IBeetleGround
    {
        public string displayName = "平地木桩";
        [Tooltip("只指定能站立的台面；台外森林地面不能放在这里。")]
        public Collider[] supportColliders;
        [Tooltip("提供给穿透求解的完整环境，可包含台面侧壁和台外地板。")]
        public Collider[] contactColliders;
        [Range(.2f, 1.5f)] public float grip = 1;
        [Range(5, 60)] public float maximumSlope = 38;
        [Tooltip("射线从此世界高度向下查询，须高于擂台最高点。")]
        public float rayCeiling = 15;
        [Min(1)] public float rayDistance = 30;

        // Collider.Raycast 不依赖全局层遮罩/触发器设置，也不需要分配 RaycastHit 数组。
        public bool Sample(Vector2 position, out BeetleGroundSample sample)
        {
            sample = default;
            if (!isActiveAndEnabled || supportColliders == null) return false;
            float nearest = float.PositiveInfinity;
            Ray ray = new Ray(new Vector3(position.x, rayCeiling, position.y), Vector3.down);
            foreach (var collider in supportColliders)
            {
                if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                if (!collider.Raycast(ray, out RaycastHit hit, rayDistance) || hit.distance >= nearest) continue;
                if (hit.normal.y < Mathf.Cos(maximumSlope * Mathf.Deg2Rad)) continue;
                nearest = hit.distance;
                sample = new BeetleGroundSample { height = hit.point.y, normal = hit.normal.normalized, grip = grip };
            }
            return nearest < float.PositiveInfinity;
        }
    }
}
