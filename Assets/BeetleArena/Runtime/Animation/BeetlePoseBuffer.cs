using UnityEngine;

namespace BeetleArena
{
    /// <summary>两份固定步姿态快照。显示插值只改 Transform，下一模拟步先恢复权威姿态。</summary>
    public sealed class BeetlePoseBuffer
    {
        readonly Transform[] bones;
        readonly BeetleLocalPose[] previous, current;

        public BeetlePoseBuffer(Transform model)
        {
            bones = model.GetComponentsInChildren<Transform>(true);
            previous = new BeetleLocalPose[bones.Length]; current = new BeetleLocalPose[bones.Length];
            for (int i = 0; i < bones.Length; i++) previous[i] = current[i] = new BeetleLocalPose(bones[i]);
        }

        // 在 AI/IK 固定步之前调用，不能让上一可视帧的中间姿态进入下一步碰撞计算。
        public void BeginStep()
        {
            for (int i = 0; i < bones.Length; i++)
            {
                previous[i] = current[i];
                current[i].Apply(bones[i]);
            }
        }

        public void Capture()
        {
            for (int i = 0; i < bones.Length; i++) current[i] = new BeetleLocalPose(bones[i]);
        }

        // 不推进 walkCycle/脚步计时，只混合两份既定结果；数组换局时分配，每帧零托管分配。
        public void Render(float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            for (int i = 0; i < bones.Length; i++)
            {
                BeetleLocalPose.Blend(bones[i], previous[i], current[i], alpha);
            }
        }

        // 只读引用供本 Actor 的接触缓冲复用层级枚举；消费者不能修改这个数组。
        internal Transform[] Bones => bones;
    }
}
