using UnityEngine;
namespace BeetleArena
{
    /// <summary>共用局部 TRS 值类型。只保存数值，不拥有时钟；两个姿态缓冲仍独立拥有自己的数组。</summary>
    internal readonly struct BeetleLocalPose
    {
        readonly Vector3 position, scale;
        readonly Quaternion rotation;
        public BeetleLocalPose(Transform t)
        { position = t.localPosition; rotation = t.localRotation; scale = t.localScale; }
        public void Apply(Transform t)
        { t.localPosition = position; t.localRotation = rotation; t.localScale = scale; }
        public static void Blend(Transform t, in BeetleLocalPose from, in BeetleLocalPose to, float alpha)
        {
            t.localPosition = Vector3.Lerp(from.position, to.position, alpha);
            t.localRotation = Quaternion.Slerp(from.rotation, to.rotation, alpha);
            t.localScale = Vector3.Lerp(from.scale, to.scale, alpha);
        }
    }
}
