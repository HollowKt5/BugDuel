using UnityEngine;

namespace BeetleArena
{
    /// <summary>
    /// 当前导入模型的独立颚片控制接口。只拥有两个颚枢轴，不写头、触角或物理根节点。
    /// 没有自主 Update：未来进食模块在固定步姿态捕获前调用 SetPose，避免与显示插值争写。
    /// 切线、转轴和角度为当前美术模型的适配值，不是昆虫解剖测量数据。
    /// </summary>
    public sealed class BeetleJawRig : MonoBehaviour
    {
        public Transform left, right;
        public Quaternion leftRest = Quaternion.identity, rightRest = Quaternion.identity;
        [Range(0, 25)] public float openingDegrees = 12;
        [Range(0, 15)] public float sweepDegrees = 6;

        /// <param name="opening">0=原始闭合形态，1=最大左右展开；不允许过闭合。</param>
        /// <param name="sweep">-1..1，共同的小幅前后扫动，未来由进食节律驱动。</param>
        public void SetPose(float opening, float sweep = 0)
        {
            // 外部动画输入异常时回到安全静止值，不能把 NaN 写进 Transform。
            if (float.IsNaN(opening) || float.IsInfinity(opening)) opening = 0;
            if (float.IsNaN(sweep) || float.IsInfinity(sweep)) sweep = 0;
            float yaw = Mathf.Clamp01(opening) * Mathf.Clamp(openingDegrees, 0, 25);
            float pitch = Mathf.Clamp(sweep, -1, 1) * Mathf.Clamp(sweepDegrees, 0, 15);
            if (left) left.localRotation = leftRest * Quaternion.Euler(pitch, -yaw, 0);
            if (right) right.localRotation = rightRest * Quaternion.Euler(pitch, yaw, 0);
        }
        public void ResetPose() => SetPose(0);
    }
}
