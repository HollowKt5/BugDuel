using UnityEngine;

namespace BeetleArena
{
    // 一次托举的接触几何：局部锚点随真实骨骼转动。没有把受击者设为攻击者的子对象。
    // 约束只在有效托举状态中保持，脱角后立即释放；所有碰撞体仍参与防穿透。
    public sealed class BeetleHornSupport
    {
        public int Attacker { get; private set; } = -1;
        public int Defender => 1 - Attacker;
        Transform horn, receiver;
        Vector3 hornPoint, receiverPoint;
        float previousPitch; bool measurementReady;
        /// <summary>只采样最终接受的模拟姿态；不得在显示帧或被拒绝的试算中调用。</summary>
        public void Measure(BeetleSimulation sim, BeetleActor attacker, float dt)
        {
            if (!Active(sim) || dt <= 0) return;
            Transform pivot = attacker.animator.headPivot;
            float pitch = Mathf.DeltaAngle(0, pivot.localEulerAngles.x);
            float raised = measurementReady ? Mathf.Max(0, -Mathf.DeltaAngle(previousPitch, pitch)) * Mathf.Deg2Rad : 0;
            previousPitch = pitch; measurementReady = true;
            Vector3 radial = horn.TransformPoint(hornPoint) - pivot.position;
            float arm = Vector3.ProjectOnPlane(radial, pivot.right).magnitude;
            Vector3 tangent = Vector3.Cross(-pivot.right, radial);
            float quality = 1 - Mathf.InverseLerp(.10f, .45f, Error);
            BeetleThrowMechanics.Accumulate(sim.Settings, sim.Fighters[Attacker], sim.Fighters[Defender],
                dt, raised, arm, tangent, quality);
        }
        public string ContactDescription { get; private set; }
        public float Error => horn && receiver ? Vector3.Distance(horn.TransformPoint(hornPoint), receiver.TransformPoint(receiverPoint)) : 0;

        public bool TryCapture(int index, BeetleContactRig source, BeetleContactRig target)
        {
            float best = float.PositiveInfinity;
            Collider chosenHorn = null, chosenTarget = null;
            Vector3 contact = Vector3.zero;
            for (int i = 0; i < source.shapes.Length; i++)
            {
                if (source.Role(i) != BeetleContactRole.HeadHorn) continue;
                var a = source.shapes[i];
                for (int j = 0; j < target.shapes.Length; j++)
                {
                    var role = target.Role(j);
                    // 角尖互顶不是腹下承重；必须先低头入角，直到接触前胸/头部/腹部实体。
                    if (role != BeetleContactRole.Body || target.IsHorn(j)) continue;
                    var b = target.shapes[j];
                    // 交替最近点覆盖胶囊与凸壳；贴合容差只接纳上一固定步刚分离的真实角接触。
                    Vector3 q = b.ClosestPoint(a.bounds.center);
                    Vector3 p = a.ClosestPoint(q);
                    q = b.ClosestPoint(p);
                    float gap = Vector3.Distance(p, q);
                    if (gap > .085f || p.y > q.y + .055f || (p.y + q.y) * .5f > b.bounds.center.y + .03f) continue;
                    // 优先腹部/前胸承重，次选头部/角的下缘卡入；脚相碰永远不能启动托举。
                    float score = gap + (role == BeetleContactRole.Body ? 0 : .1f);
                    if (score >= best) continue;
                    best = score; chosenHorn = a; chosenTarget = b; contact = (p + q) * .5f;
                }
            }
            if (!chosenHorn) return false;
            Attacker = index; horn = chosenHorn.transform; receiver = chosenTarget.transform;
            measurementReady = false;
            hornPoint = horn.InverseTransformPoint(contact); receiverPoint = receiver.InverseTransformPoint(contact);
            ContactDescription = chosenHorn.name + " -> " + chosenTarget.name;
            return true;
        }

        public bool Active(BeetleSimulation sim) => Attacker >= 0 && horn && receiver &&
            sim.Fighters[Defender].Lift.Supporter == Attacker;

        public void Follow(BeetleSimulation sim, BeetleContactRig target, float dt)
        {
            if (!Active(sim) || dt <= 0) return;
            Vector3 correction = horn.TransformPoint(hornPoint) - receiver.TransformPoint(receiverPoint);
            // 限速防止接触建立/骨骼姿态变化造成瞬移；碰撞约束随后仍可将物体推出实体表面。
            target.Translate(Vector3.ClampMagnitude(correction, sim.Settings.liftFollowSpeed * dt));
        }
    }
}
