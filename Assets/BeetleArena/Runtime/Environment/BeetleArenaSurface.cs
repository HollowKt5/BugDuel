using UnityEngine;

namespace BeetleArena
{
    /// <summary>Surface query result: World Y height and outward normal; represents supportable arena surface only.</summary>
    public struct BeetleGroundSample
    {
        public float height, grip;
        public Vector3 normal;
    }

    // Simulation depends on query interface only; headless tests can use default planar disc or custom surface implementations.
    public interface IBeetleGround
    {
        bool Sample(Vector2 position, out BeetleGroundSample sample);
    }

    /// <summary>Surface provider for designated arena. Queries specified Collider directly without hitting beetles or outer floor.</summary>
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

        // Collider.Raycast is independent of global layer masks and trigger settings, avoiding RaycastHit array allocations.
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
