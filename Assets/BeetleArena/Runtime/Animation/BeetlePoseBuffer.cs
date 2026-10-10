using UnityEngine;

namespace BeetleArena
{
    /// <summary>两份固定步姿态快照。显示插值只改 Transform，下一模拟步先恢复权威姿态。</summary>
    public sealed class BeetlePoseBuffer
    {
        struct Pose
        {
            public Vector3 position, scale;
            public Quaternion rotation;
            public Pose(Transform t) { position = t.localPosition; rotation = t.localRotation; scale = t.localScale; }
        }
        readonly Transform[] bones;
        readonly Pose[] previous, current;

        public BeetlePoseBuffer(Transform model)
        {
            bones = model.GetComponentsInChildren<Transform>(true);
            previous = new Pose[bones.Length]; current = new Pose[bones.Length];
            for (int i = 0; i < bones.Length; i++) previous[i] = current[i] = new Pose(bones[i]);
        }

        // 在 AI/IK 固定步之前调用，不能让上一可视帧的中间姿态进入下一步碰撞计算。
        public void BeginStep()
        {
            for (int i = 0; i < bones.Length; i++)
            {
                previous[i] = current[i];
                Apply(bones[i], current[i]);
            }
        }

        public void Capture()
        {
            for (int i = 0; i < bones.Length; i++) current[i] = new Pose(bones[i]);
        }

        // 不推进 walkCycle/脚步计时，只混合两份既定结果；数组换局时分配，每帧零托管分配。
        public void Render(float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].localPosition = Vector3.Lerp(previous[i].position, current[i].position, alpha);
                bones[i].localRotation = Quaternion.Slerp(previous[i].rotation, current[i].rotation, alpha);
                bones[i].localScale = Vector3.Lerp(previous[i].scale, current[i].scale, alpha);
            }
        }

        static void Apply(Transform t, Pose pose)
        { t.localPosition = pose.position; t.localRotation = pose.rotation; t.localScale = pose.scale; }
    }
}
