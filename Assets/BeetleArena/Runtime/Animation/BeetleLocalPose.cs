using UnityEngine;
namespace BeetleArena
{
    /// <summary>Shared local TRS value type. Stores raw transforms without clock ownership; pose buffers maintain independent backing arrays.</summary>
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
