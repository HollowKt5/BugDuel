using System;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>
    /// 导入网格的骨架适配层。腿长、关节初始姿态及弯曲平面由建模配置生成并序列化。
    /// 运行时只旋转骨骼；不移动关节、不缩放腿段、不修改原 FBX。
    /// </summary>
    public sealed class BeetleModelRig : MonoBehaviour
    {
        [Serializable]
        public sealed class LegBinding
        {
            public Transform hip, knee, ankle, tip;
            public Quaternion hipRest = Quaternion.identity, kneeRest = Quaternion.identity;
            public Vector3 bendInBody, footOffsetInModel;
            public float upperLength, lowerLength;
            [NonSerialized] public Vector3 lastBend;
            [NonSerialized] public float targetError;
        }

        public Transform model, body;
        public Vector3 bodyRest = new Vector3(0, .95f, 0);
        public LegBinding[] bindings;
        [Serializable]
        public sealed class FeelerBinding
        {
            public Transform pivot;
            public Vector3[] surfacePoints;
            public Vector3 bendAxis;
            [NonSerialized] public float avoidanceAngle;
        }
        public FeelerBinding[] feelers;
        float postureLift;
        [Min(0)] public float soleClearance = .025f;
        [Range(.1f, 1)] public float headMotionScale = .48f;
        [Range(.01f, .2f)] public float reachMargin = .015f;

        public void ResetRig()
        {
            foreach (var b in bindings)
            {
                b.hip.localRotation = b.hipRest;
                b.knee.localRotation = b.kneeRest;
                b.ankle.localRotation = Quaternion.identity;
                b.lastBend = Vector3.zero;
                b.targetError = 0;
            }
            if(feelers != null) foreach(var f in feelers) f.avoidanceAngle=0;
            postureLift=0;
        }

        /// <summary>触角靠近坡面时主动收起，避免用细触角的碰撞反过来顶起整只甲虫。</summary>
        public void AvoidFeelerGround(IBeetleGround ground, float dt)
        {
            if(ground==null || feelers==null)return;
            float neededLift=0;
            foreach(var f in feelers) {
                Quaternion rest=f.pivot.rotation;
                float wanted=0, scale=Mathf.Abs(model.lossyScale.x), bestDepth=float.MaxValue;
                for(int iteration=0;iteration<=12;iteration++) {
                    float angle=iteration*5;
                    Quaternion candidate=rest*Quaternion.AngleAxis(angle,f.bendAxis);
                    float depth=0;
                    foreach(var point in f.surfacePoints) {
                        Vector3 p=f.pivot.position+candidate*(point*scale);
                        if(ground.Sample(new Vector2(p.x,p.z),out var hit)) depth=Mathf.Max(depth,hit.height+.025f*scale-p.y);
                    }
                    if(depth<bestDepth) {bestDepth=depth;wanted=angle;}
                    if(depth<=0)break;
                }
                // 接近时快速避让、离开时缓慢放松，避免台面法线变化造成触角来回弹动。
                float blend=1-Mathf.Exp(-dt*(wanted>f.avoidanceAngle?35:7));
                f.avoidanceAngle=Mathf.Lerp(f.avoidanceAngle,wanted,blend);
                f.pivot.rotation=rest*Quaternion.AngleAxis(f.avoidanceAngle,f.bendAxis);
                foreach(var point in f.surfacePoints) {
                    Vector3 p=f.pivot.TransformPoint(point);
                    if(ground.Sample(new Vector2(p.x,p.z),out var hit)) neededLift=Mathf.Max(neededLift,(hit.height+.025f*scale-p.y)/scale);
                }
            }
            // 根节点仍由物理模拟拥有。小幅抬高胸部姿态，再由六足 IK 保持原支撑点，
            // 避免触角基部在陡坡上无论怎样旋转都低于地表；不把这一修正当成攻击位移。
            float response=neededLift>postureLift?35:7;
            postureLift=Mathf.Lerp(postureLift,Mathf.Clamp(neededLift,0,.25f),1-Mathf.Exp(-dt*response));
            body.position+=Vector3.up*(postureLift*Mathf.Abs(model.lossyScale.x));
        }

        public void Solve(BeetleLegRig leg, Vector3 target, BeetleState state, IBeetleGround ground)
        {
            int index = (leg.side < 0 ? 0 : 3) + leg.pair;
            var b = bindings[index];
            float scale = Mathf.Abs(model.lossyScale.x);
            // 支撑点限制在本腿的活动扇区内。限制用模型实测脚位，避免继承几何体腿宽。
            Vector3 local = model.InverseTransformPoint(target);
            local.x = leg.side * Mathf.Max(Mathf.Abs(leg.restAnkle.x) * .55f, local.x * leg.side);
            local.z = Mathf.Clamp(local.z, leg.restAnkle.z - .55f, leg.restAnkle.z + .55f);
            target = model.TransformPoint(local);
            Vector3 normal = state.SurfaceNormal;
            if (!state.Airborne && !state.HasFallen && ground != null && ground.Sample(new Vector2(target.x, target.z), out var hit))
            {
                target.y = Mathf.Max(target.y, hit.height + (soleClearance + leg.soleOffset) * scale);
                normal = hit.normal;
            }
            // 足节的原始弯曲形状保留，只让整个足节顺着坡面转动。
            Quaternion footFrame = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0, state.Heading, 0);
            if (state.Airborne || state.HasFallen) footFrame = model.rotation;
            Vector3 ankleTarget = target - footFrame * (b.footOffsetInModel * scale);
            b.hip.localRotation = b.hipRest;
            b.knee.localRotation = b.kneeRest;
            Vector3 hip = b.hip.position;
            float a = b.upperLength * scale, c = b.lowerLength * scale;
            Vector3 offset = ankleTarget - hip;
            float distance = Mathf.Clamp(offset.magnitude, Mathf.Abs(a - c) + .001f * scale, (a + c) * (1 - reachMargin));
            Vector3 direction = offset.sqrMagnitude > 1e-10f ? offset.normalized : Vector3.down;
            ankleTarget = hip + direction * distance;
            Vector3 pole = Vector3.ProjectOnPlane(body.TransformDirection(b.bendInBody), direction);
            // 提示向量接近共线时沿用上一有效弯曲方向，最后用身体外侧轴作为备用。
            if (pole.sqrMagnitude < 1e-6f) pole = Vector3.ProjectOnPlane(b.lastBend, direction);
            if (pole.sqrMagnitude < 1e-6f) pole = Vector3.ProjectOnPlane(model.right * leg.side, direction);
            if (pole.sqrMagnitude < 1e-6f) pole = Vector3.ProjectOnPlane(model.forward, direction);
            pole.Normalize(); b.lastBend = pole;
            float along = (a * a - c * c + distance * distance) / (2 * distance);
            Vector3 kneeTarget = hip + direction * along + pole * Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            b.hip.rotation = Quaternion.FromToRotation(b.knee.position - hip, kneeTarget - hip) * b.hip.rotation;
            b.knee.rotation = Quaternion.FromToRotation(b.ankle.position - b.knee.position, ankleTarget - b.knee.position) * b.knee.rotation;
            b.ankle.rotation = footFrame;
            b.targetError = Vector3.Distance(b.tip.position, target);
        }
    }
}
