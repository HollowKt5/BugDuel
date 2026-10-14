using System;
using UnityEngine;

namespace BeetleArena
{
    [Serializable]
    // 单条腿的绑定信息与步态缓存。真实模型的额外骨骼数据保存在 BeetleModelRig。
    // 它描述的是两段式 IK 加足节、爪、刺的结构；不是 Unity Animator 的人形骨骼配置。
    public sealed class BeetleLegRig
    {
        // hip/knee/ankle 是关节点；upper/lower/foot 是连接关节的几何体腿段。
        public Transform hip, upper, knee, lower, ankle, foot;
        // 脚端的爪和胫节上的刺作为附属部件，求完主腿关节后再放置。
        public Transform[] claws, teeth;
        // hipInBody 使用 bodyPivot 局部坐标；restAnkle 使用 model 局部坐标，不能混用。
        public Vector3 hipInBody, restAnkle;
        // 建模阶段单位缩放下记录的两段长度；求解时乘 visualScale×size 换成世界长度。
        public float upperLength, lowerLength;
        // 正式模型的爪尖参考点到足节最低顶点的距离，避免骨骼落地时爪网格埋入台面。
        public float soleOffset;
        // side：左 -1、右 +1；pair：前 0、中 1、后 2。用于弯曲方向、活动区域和步态相位。
        public int side, pair;
        // 以下是运行时缓存，不写入预制体。planted 为世界坐标；迈步期间它也记录抬起的脚目标。
        // stepFrom/stepTo 是本次迈步的世界起终点，身体移动时不会自动跟着改变。
        [NonSerialized] public Vector3 planted, stepFrom, stepTo;
        // 归一化迈步进度，通常从 0 累加到 1；不是以秒为单位的累计时间。
        [NonSerialized] public float stepTime;
        // 单步启动时固定时长；速度在迈步中改变也不会突然改变脚端运动速度。
        [NonSerialized] public float stepDuration;
        [NonSerialized] public bool stepping;
        // 上一步真实脚端以及落地接续轨迹，均为世界坐标。
        [NonSerialized] public Vector3 lastFoot, landingFrom;
        [NonSerialized] public float landingBlend;
        // 可插拔动作退出后统一把脚端平滑交还步态；模块删除也不会留下悬空脚点。
        [NonSerialized] public bool moduleOwned;
        [NonSerialized] public float moduleReturn = 1;
        [NonSerialized] public Vector3 moduleReturnFrom;
        [NonSerialized] public bool supportAnchored;
        [NonSerialized] public Vector3 supportAnchor;
        [NonSerialized] public float tumbleFrameWeight;
    }

    // 程序动画总入口：战斗状态→身体/头角姿态→脚部目标轨迹→真实骨骼 IK。
    // 它消费战斗状态，不负责攻击结算、重力或胜负；动画产生的重叠交给接触求解器修正。
    public sealed class BeetleProceduralAnimator : MonoBehaviour
    {
        [Header("Model articulation")]
        // model 管整体视觉缩放；bodyPivot 控制身体俯仰/侧倾；headPivot 控制头角抬举。
        public Transform model, bodyPivot, headPivot;
        public BeetleModelRig importedRig;
        public Transform[] antennae;
        public BeetleLegRig[] legs;
        public Renderer[] shellRenderers;
        // 身体静止局部位置和触角初始局部旋转；叠加动画时从基准姿态出发，避免累计漂移。
        Vector3 bodyRest;
        // 仅缓存动作产生的胸部高度；坡面避障随后叠加，不能从 Transform 读回并累积。
        float bodyHeightOffset;
        Quaternion[] antennaRest;
        Vector3[] antennaAngles;
        // size 是个体大小，scale 是最终视觉缩放；三个 Pitch/Roll 为平滑后的角度（度）。
        // walkCycle 是连续步态相位。ground 与模拟共用，只采样当前有效擂台。
        float size = 1, scale = .7f, arenaRadius = 5.2f, bodyPitch, bodyRoll, headPitch, walkCycle;
        IBeetleGround ground;
        Vector3 previousPosition;
        bool initialized;
        BeetleMatchSettings settings;
        bool previousAirborne;
        BeetleAction previousAction;
        float liftingHeadStart, headYaw, liftingYaw, releaseHeadStart, releaseBodyStart;
        public float StruggleWeight { get; private set; }
        public float StruggleFrequency { get; private set; }
        float strugglePhase;
        bool fallingPose, floorPose;
        bool flightSpinning, flightRecovering;
        bool edgeTumbling;
        public float EdgeReach { get; private set; }
        public bool EdgeTumbling => edgeTumbling && flightSpinning;
        bool backRighting;
        Vector3 rightingAngularVelocity;
        float stableRightingSeconds;
        public float LandingTuck { get; private set; }
        public bool BackLandingDetected { get; private set; }
        public float BackStruggleSeconds { get; private set; }
        public bool IsRighting => flightRecovering;
        float flightPitchAngle, flightRollAngle, flightPitchRate, flightRollRate, flightSeconds, flightRecovery;
        Quaternion flightOrigin;
        float fallPitchStart, fallRollStart, floorPitchStart, floorRollStart;
        float FallRecovery(BeetleState b) => Ease(Mathf.Clamp01((b.Fall.GroundSeconds - .12f) / settings.fallRecoverySeconds));

        // 对 SmoothStep 频率曲线解析积分：相位连续，速度也连续。
        // 不能写成 time * Lerp(slow, fast, time)，那会额外加速并在切换处跳拍。
        void UpdateStruggle(BeetleState state)
        {
            // 已落台继续寻触；地面恢复也保留连续摆腿相位，不能判负时突然归零。
            float t = state.Fall.Active
                ? Mathf.Max(0, Mathf.Max(state.Lift.UnsupportedTime, state.Fall.UnsupportedSeconds) - settings.liftStruggleDelay)
                    + state.Fall.GroundSeconds
                : state.Airborne ? Mathf.Max(0, state.Lift.UnsupportedTime - settings.liftStruggleDelay) : 0;
            float ramp = Mathf.Max(.1f, settings.struggleRampSeconds);
            float u = Mathf.Clamp01(t / ramp);
            float f0 = settings.struggleStartHz, df = settings.struggleMaxHz - f0;
            StruggleWeight = Ease(Mathf.Clamp01(t / .35f));
            StruggleFrequency = t > 0 ? f0 + df * Ease(u) : 0;
            float cycles = f0 * t + df * (ramp * (u * u * u - .5f * u * u * u * u) + Mathf.Max(0, t - ramp));
            strugglePhase = cycles * (2 * Mathf.PI);
        }

        float SoleHeight(BeetleLegRig leg) => importedRig
            ? (leg.soleOffset + importedRig.soleClearance) * Mathf.Abs(model.lossyScale.x) : .05f * size;

        // 每局重新绑定时重置缩放、姿态、步态和落脚点。颜色通过 MaterialPropertyBlock 设置，
        // 避免为两只甲虫复制材质。调用前 Actor 已把根节点放到本局初始位置。
        public void Initialize(BeetleTraits traits, BeetleMatchSettings settings, Color shellColor, IBeetleGround surface = null)
        {
            this.settings = settings; previousAirborne = false; StruggleWeight = StruggleFrequency = strugglePhase = 0;
            previousAction = BeetleAction.Assess; liftingHeadStart = headYaw = liftingYaw = 0;
            fallingPose = floorPose = flightSpinning = flightRecovering = false;
            edgeTumbling = false; EdgeReach = 0;
            flightPitchAngle = flightRollAngle = flightSeconds = flightRecovery = 0;
            LandingTuck = BackStruggleSeconds = 0; BackLandingDetected = backRighting = false;
            rightingAngularVelocity = Vector3.zero; stableRightingSeconds = 0;
            ground = surface;
            size = traits.size; scale = settings.visualScale * size; arenaRadius = settings.arenaRadius;
            model.localScale = Vector3.one * scale;
            model.localPosition = importedRig ? Vector3.zero : new Vector3(0, -.077f * size, 0);
            bodyRest = importedRig ? importedRig.bodyRest : new Vector3(0, 1.10f, 0);
            if (importedRig) importedRig.ResetRig();
            bodyPivot.localPosition = bodyRest;
            bodyPivot.localRotation = Quaternion.identity;
            headPivot.localRotation = Quaternion.identity;
            headPivot.localScale = new Vector3(1, 1, traits.hornLengthFactor);
            bodyPitch = bodyRoll = headPitch = walkCycle = bodyHeightOffset = 0;
            antennaAngles = new Vector3[antennae.Length];
            if (antennaRest == null || antennaRest.Length != antennae.Length)
            {
                antennaRest = new Quaternion[antennae.Length];
                for (int i = 0; i < antennae.Length; i++) antennaRest[i] = antennae[i].localRotation;
            }
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", importedRig ? Color.Lerp(Color.white, shellColor, .20f) : shellColor);
            foreach (var renderer in shellRenderers) if (renderer) renderer.SetPropertyBlock(block);
            foreach (var leg in legs)
            {
                leg.tumbleFrameWeight = 0;
                leg.planted = NominalFoot(leg);
                leg.lastFoot = leg.planted; leg.landingBlend = 1;
                leg.moduleOwned = false; leg.moduleReturn = 1;
                leg.supportAnchored = false;
                leg.stepping = false;
            }
            previousPosition = transform.position;
            initialized = true;
        }

        // Fixed-step body rotation after an actual release. Long throws tumble; a short pry
        // retains its small recoil. The same articulated colliders follow this rotation.
        void UpdateFlightSpin(BeetleState state, float dt)
        {
            if (!state.Fall.EdgeDrop) edgeTumbling = false;
            if (!edgeTumbling && state.Fall.EdgeDrop && !state.Fall.OnFloor && !state.Lift.Supported)
            {
                // Pivot departure approximation: forward/backward/side slips inherit different
                // rotation axes. No artificial spin is added to an already airborne high throw.
                edgeTumbling = flightSpinning = true; flightRecovering = false;
                flightSeconds = flightPitchAngle = flightRollAngle = 0;
                flightOrigin = bodyPivot.localRotation;
                Vector2 outward = state.Fall.EdgeDirection;
                Vector3 local = model.InverseTransformDirection(new Vector3(outward.x, 0, outward.y));
                float angularSpeed = Mathf.Clamp(state.Fall.EdgeSpeed / Mathf.Max(.4f, size * .7f) * Mathf.Rad2Deg, 75, 200);
                flightPitchRate = local.z * angularSpeed;
                flightRollRate = -local.x * angularSpeed;
                LandingTuck = 0;
            }
            if (state.IsOverturned && !flightSpinning && !flightRecovering)
            {
                flightRecovering = backRighting = BackLandingDetected = true;
                flightRecovery = stableRightingSeconds = 0; rightingAngularVelocity = Vector3.zero;
            }
            if (!flightSpinning && !flightRecovering && state.Action == BeetleAction.Launched && state.Airborne && !state.Lift.Supported)
            {
                Vector3 launch = state.Lift.LaunchVelocity;
                float horizontal = new Vector2(launch.x, launch.z).magnitude;
                float range = horizontal * 2 * Mathf.Max(0, launch.y) / Mathf.Max(1, settings.gravity);
                if (range >= 4 && launch.magnitude >= settings.throwHighArcSpeed)
                {
                    flightSpinning = true; flightSeconds = flightPitchAngle = flightRollAngle = 0;
                    edgeTumbling = false;
                    LandingTuck = BackStruggleSeconds = 0; BackLandingDetected = backRighting = false;
                    flightOrigin = bodyPivot.localRotation;
                    float strength = Mathf.InverseLerp(6, 17.6f, launch.magnitude);
                    float along = Vector2.Dot(new Vector2(launch.x, launch.z), state.Forward);
                    flightPitchRate = (along >= 0 ? 1 : -1) * Mathf.Lerp(180, 340, strength);
                    float side = state.Lift.TwistSign == 0 ? state.CircleDirection : state.Lift.TwistSign;
                    flightRollRate = side * Mathf.Lerp(35, 90, strength);
                }
            }
            if (flightSpinning)
            {
                if (!state.Airborne || state.Fall.OnFloor || state.Lift.Supported)
                {
                    flightSpinning = false; flightRecovering = true; flightRecovery = 0;
                    backRighting = Vector3.Dot(bodyPivot.up, model.up) < -.25f;
                    BackLandingDetected = backRighting;
                    rightingAngularVelocity = Vector3.zero; stableRightingSeconds = 0;
                }
                else
                {
                    flightSeconds += dt;
                    // Predict the nearby surface along the descending trajectory. Tuck only
                    // near contact, rather than spending the whole flight rigidly folded.
                    Vector2 ahead = state.Position + state.Velocity * .2f;
                    float surface = ground != null && ground.Sample(ahead, out var hit) ? hit.height : settings.groundHeight;
                    float clearance = Mathf.Max(0, bodyPivot.position.y - .38f * size - surface);
                    float down = Mathf.Max(0, -state.VerticalVelocity);
                    float gravity = Mathf.Max(1, settings.gravity);
                    float untilContact = (Mathf.Sqrt(down * down + 2 * gravity * clearance) - down) / gravity;
                    float tuck = state.VerticalVelocity < 0 ? 1 - Ease(Mathf.InverseLerp(.10f, .45f, untilContact)) : 0;
                    LandingTuck = Mathf.Lerp(LandingTuck, tuck, 1 - Mathf.Exp(-dt * 18));
                    // Clear the supporting horn before developing the full angular speed.
                    float release = Ease(Mathf.InverseLerp(.06f, .22f, flightSeconds));
                    float damping = Mathf.Exp(-.12f * flightSeconds);
                    flightPitchAngle += flightPitchRate * release * damping * dt;
                    flightRollAngle += flightRollRate * release * damping * dt;
                }
            }
            if (flightRecovering)
            {
                flightRecovery += dt;
                if (backRighting) BackStruggleSeconds += dt;
                backRighting = Vector3.Dot(bodyPivot.up, model.up) < .35f;
                stableRightingSeconds = state.RightingStable ? stableRightingSeconds + dt : 0;
                // No timed success: actual upright support is required to hand back to gait.
                if (stableRightingSeconds >= .2f)
                {
                    flightRecovering = false; rightingAngularVelocity = Vector3.zero;
                    bodyPitch = Mathf.DeltaAngle(0, bodyPivot.localEulerAngles.x);
                    bodyRoll = Mathf.DeltaAngle(0, bodyPivot.localEulerAngles.z);
                    floorPitchStart = bodyPitch; floorRollStart = bodyRoll;
                }
            }
        }

        // time 用于连续摆动，dt 用于平滑和迈步进度；state 提供速度、动作阶段与受击反馈。
        // 当前 ContactDriven 模式由 PoseSimulation 推进一次姿态，Render 不再重复推进脚步。
        public void Pose(BeetleState state, float time, float dt)
        {
            if (!initialized || legs == null) return;
            float speed = state.Velocity.magnitude;
            UpdateFlightSpin(state, dt);
            bool approachingEdge = false;
            if (!state.Airborne && !state.HasFallen && !state.Lift.Supported && speed > .25f && ground != null)
                approachingEdge = !ground.Sample(state.Position + state.Velocity.normalized * (.75f * size), out _);
            EdgeReach = Mathf.Lerp(EdgeReach, approachingEdge ? 1 : 0, 1 - Mathf.Exp(-dt * 14));
            if (state.Action == BeetleAction.Lifting && previousAction != BeetleAction.Lifting)
            { liftingHeadStart = headPitch; liftingYaw = headYaw; }
            if ((state.Action == BeetleAction.ReleaseLift || state.Action == BeetleAction.Toss) && previousAction != state.Action)
            { releaseHeadStart = headPitch; releaseBodyStart = bodyPitch; }
            float phase = state.ActionProgress;
            float targetHead = 12f, pitch = 0, roll = state.RollVisual;
            // 发力的视觉权重：影响身体下压和细微扭动；不是实际攻击力或足部摩擦力。
            float strain = 0;
            // 按动作设目标姿态：试探小幅点头，蓄力低头，抬举/挑飞快速扬角，腾空加入侧倾。
            // 角度正负依赖当前模型坐标轴；换 FBX 必须重新校准枢轴和骨骼初始方向。
            switch (state.Action)
            {
                case BeetleAction.Assess: targetHead = 15 + InsectMotionTiming.Bout(time + state.Index * .83f, 1.43f, 203) * 9; break;
                case BeetleAction.Probe: targetHead = 18 + InsectMuscleAnimation.Recruit(phase) * 14; pitch = 2.5f * InsectMuscleAnimation.Recruit(phase); break;
                case BeetleAction.Disengage: targetHead = 17; pitch = -2; break;
                case BeetleAction.ChargeWindup: targetHead = Mathf.Lerp(18, 56, Ease(phase)); pitch = 9 * Ease(phase); strain = .8f; break;
                // A charge drives the whole body forward with a low horn; reserve the
                // large upward head sweep for Surge / a physically established lift.
                case BeetleAction.Charge: targetHead = Mathf.Lerp(56, 24, InsectMuscleAnimation.Strike(phase)); pitch = -8 * Mathf.Sin(phase * Mathf.PI); strain = 1.65f; break;
                case BeetleAction.HornLock:
                    // 交角时短促尝试发力，中间保持支撑；不再持续高频机械颤动。
                    float effort = InsectMotionTiming.Bout(time + state.Index * .83f, .93f, 227);
                    targetHead = 24 + effort * 26;
                    pitch = 3 + effort * 9; roll += state.CircleDirection * effort * 7;
                    strain = 1.25f; break;
                case BeetleAction.ShoveWindup: targetHead = Mathf.Lerp(14, 30, Ease(phase)); pitch = 4; break;
                case BeetleAction.Shove: targetHead = Mathf.Lerp(34, 18, InsectMuscleAnimation.Strike(phase)); pitch = -5 * Mathf.Sin(phase * Mathf.PI); strain = 1.25f; break;
                case BeetleAction.PryWindup: targetHead = Mathf.Lerp(20, 82, Ease(phase)); strain = .5f; break;
                // 入角阶段保持头角低位，只有真正触及对方身体下缘才切换到 Lifting 扬角。
                case BeetleAction.Pry: targetHead = 82; strain = .7f; break;
                case BeetleAction.Lifting:
                    // 明确扬起真实头角，接触锚点随骨骼运动，再由物理约束带起对手。
                    float raise = Ease(Mathf.Clamp01(state.ActionTime / settings.liftRaiseSeconds));
                    targetHead = Mathf.Lerp(64, -85, raise); pitch = -6 * raise; strain = 1;
                    break;
                case BeetleAction.Lifted:
                    targetHead = 12; pitch = state.Lift.Pitch; roll = state.Lift.Roll;
                    break;
                case BeetleAction.ReleaseLift:
                    // 收角与后撤同步，让对方沿角面滑离，避免一帧恢复低头姿态把受击者横向顶走。
                    targetHead = 30; pitch = Mathf.Lerp(releaseBodyStart, 0, Ease(phase));
                    break;
                case BeetleAction.Toss:
                    targetHead = Mathf.Lerp(18, -24, Ease(Mathf.Min(1, phase * 3)));
                    pitch = -12 * Mathf.Sin(phase * Mathf.PI); strain = 1.4f; break;
                case BeetleAction.Launched:
                    targetHead = 5;
                    pitch = Mathf.Lerp(-32, 12, phase);
                    roll += state.CircleDirection * Mathf.Sin(phase * Mathf.PI) * 30;
                    break;
                case BeetleAction.Brace: targetHead = 30; pitch = 5; strain = .65f; break;
                case BeetleAction.Recover: targetHead = 18; break;
                case BeetleAction.Subdued: targetHead = 23; pitch = 3; break;
                case BeetleAction.Stagger: pitch = -state.LiftVisual * 17; break;
                // 失足/判负的身体运动在下方从实际进入姿态接续，不设置固定“死亡”侧躺角。
                case BeetleAction.Falling:
                case BeetleAction.Defeated: targetHead = 16; break;
                case BeetleAction.Winner: targetHead = 6; break;
            }
            pitch -= state.LiftVisual * 8;
            pitch -= state.ImpactVisual * 5;
            if (state.Action != BeetleAction.Lifted) { pitch += state.Lift.Pitch; roll += state.Lift.Roll; }
            UpdateStruggle(state);
            var modulePose = state.Motion.PoseSample();
            if (state.Fall.Active)
            {
                // 使用独立固定步时钟：比赛 Finished 后 Elapsed 停止，跌落和翻正仍须继续。
                if (!fallingPose) { fallPitchStart = bodyPitch; fallRollStart = bodyRoll; fallingPose = true; }
                float ft = state.Fall.Time;
                float turn = 1 - Mathf.Exp(-ft * 2.2f);
                float side = Mathf.Abs(state.Fall.SideSpeed) > .1f ? Mathf.Sign(state.Fall.SideSpeed) : state.CircleDirection;
                pitch = fallPitchStart + Mathf.Clamp(state.Fall.ForwardSpeed * 5, -28, 28) * turn;
                roll = fallRollStart + side * (14 + Mathf.Min(20, Mathf.Abs(state.Fall.SideSpeed) * 6)) * turn;
                roll += Mathf.Sin(ft * 6.5f) * 4 * turn;
                if (state.Fall.OnFloor)
                {
                    if (!floorPose) { floorPitchStart = bodyPitch; floorRollStart = bodyRoll; floorPose = true; }
                    float recover = FallRecovery(state), gt = state.Fall.GroundSeconds;
                    float push = Mathf.Sin(gt * 9) * Mathf.Sin(Mathf.PI * recover) * 7;
                    pitch = Mathf.Lerp(floorPitchStart, 0, recover);
                    roll = Mathf.Lerp(floorRollStart, 0, recover) + side * push;
                    targetHead = 16 + Mathf.Sin(gt * 3) * 3;
                }
                else floorPose = false;
                // Finished 阶段不再衰减旧的 LiftVisual/ImpactVisual，已在上方覆盖这些旧目标。
            }
            else { fallingPose = floorPose = false; }
            pitch += modulePose.BodyPitch; roll += modulePose.BodyRoll;
            targetHead += modulePose.HeadPitch; strain += modulePose.Strain;
            // 指数平滑让姿态逐渐靠近目标，减少状态切换时跳变；dt=0 时保留原姿态。
            // 模块可选择较快关节响应，但抬举/抛出/跌落保留原参数和接触接续。
            float response = state.Action == BeetleAction.Module && modulePose.PoseResponse > 0
                ? Mathf.Clamp(modulePose.PoseResponse, 13, 29) : InsectMuscleAnimation.Response(state, 13);
            float blend = 1 - Mathf.Exp(-dt * response);
            if (importedRig) targetHead = (targetHead - 12) * importedRig.headMotionScale *
                (state.Committed && state.Action != BeetleAction.Module ? Mathf.Lerp(1.18f, 1.48f, state.Traits.drivePreference) : 1);
            // Modules already specify their complete pose; do not amplify it a second time.
            if (state.Committed && state.Action != BeetleAction.Module) pitch *= Mathf.Lerp(.85f, 1.25f, state.Traits.drivePreference);
            if (state.Action == BeetleAction.Lifting)
                targetHead = liftingHeadStart - state.LiftSweepTarget * state.LiftStrokeProgress;
            // 出手保留已接受的抬头姿态，再短促跟随；不能切到固定低头角度反向撞回受击者。
            if (state.Action == BeetleAction.Toss)
            {
                targetHead = releaseHeadStart - 8 * Mathf.Sin(phase * Mathf.PI);
                pitch = Mathf.Lerp(releaseBodyStart, -8, Ease(phase));
            }
            // 提前松脱/未完全抬起时也从实际头角开始收角，不能假定已经扬到 -85 度。
            if (state.Action == BeetleAction.ReleaseLift)
                targetHead = Mathf.Lerp(releaseHeadStart, importedRig ? (30 - 12) * importedRig.headMotionScale : 30, Ease(phase));
            headPitch = Mathf.Lerp(headPitch, targetHead, blend);
            bodyPitch = Mathf.Lerp(bodyPitch, pitch, blend);
            bodyRoll = Mathf.Lerp(bodyRoll, roll, blend);
            // 步态频率随平面速度提升；相位仍需配合落脚误差，静止时不会单凭相位不断迈步。
            walkCycle += dt * Mathf.Lerp(.8f, 2.4f, Mathf.Clamp01(speed));
            // 外骨骼不做持续整胸呼吸起伏；静置重心变化由支撑模块负责。
            float bob = Mathf.Sin(walkCycle * Mathf.PI * 4) * Mathf.Min(speed, 1) * .018f;
            float landingCompression = state.Fall.OnFloor
                ? Mathf.Sin(Mathf.PI * Mathf.Clamp01(state.Fall.GroundSeconds / .24f)) * Mathf.Min(.1f, state.Fall.ImpactSpeed * .012f) : 0;
            // 蓄力开始/退出时胸部高度也渐变，避免快速招募表现变成整虫一帧上下跳。
            bodyHeightOffset = Mathf.Lerp(bodyHeightOffset,
                flightRecovering ? 0 : bob - strain * .055f - landingCompression + modulePose.BodyHeight, 1 - Mathf.Exp(-dt * 22));
            bodyPivot.localPosition = bodyRest + Vector3.up * bodyHeightOffset;
            Quaternion uprightPose = Quaternion.Euler(bodyPitch, 0, bodyRoll);
            if (flightSpinning)
            {
                Quaternion spun = flightOrigin * Quaternion.AngleAxis(flightPitchAngle, Vector3.right) * Quaternion.AngleAxis(flightRollAngle, Vector3.forward);
                bodyPivot.localRotation = Quaternion.Slerp(bodyPivot.localRotation, spun, 1-Mathf.Exp(-dt*25));
            }
            else if (flightRecovering)
            {
                float inertia = Mathf.Max(.08f, state.Traits.mass * size * size * .36f);
                Vector3 torque = state.RightingGrounded ? state.RightingTorque : Vector3.zero;
                rightingAngularVelocity += torque / inertia * (Mathf.Rad2Deg * dt);
                rightingAngularVelocity *= Mathf.Exp(-dt * 8);
                rightingAngularVelocity = Vector3.ClampMagnitude(rightingAngularVelocity, 65);
                float turn = rightingAngularVelocity.magnitude * dt;
                if (turn > .00001f) bodyPivot.rotation = Quaternion.AngleAxis(turn, rightingAngularVelocity.normalized) * bodyPivot.rotation;
            }
            else bodyPivot.localRotation = uprightPose;
            // Quiet assessment uses small irregular sampling; insertion stays sagittal.
            float yawTarget = state.Action == BeetleAction.Assess ? (InsectMotionTiming.Bout(time + state.Index * .83f, 1.61f, 239) - .5f * InsectMotionTiming.Bout(time + .4f, 2.17f, 241)) * 3 :
                state.Action == BeetleAction.PryWindup || state.Action == BeetleAction.Pry ? 0 :
                state.Action == BeetleAction.Lifting || state.Action == BeetleAction.ReleaseLift || state.Action == BeetleAction.Toss ? liftingYaw : 0;
            yawTarget += modulePose.HeadYaw;
            headYaw = Mathf.LerpAngle(headYaw, yawTarget, blend);
            headPivot.localRotation = Quaternion.Euler(headPitch, headYaw, 0);
            if (importedRig && !state.Airborne && !state.HasFallen)
                headPitch = importedRig.AvoidHeadGround(headPivot, ground, headPitch, headYaw);
            for (int i = 0; i < antennae.Length; i++)
            {
                state.Motion.SampleAntenna(i, out var targetAngles);
                antennaAngles[i] = Vector3.Lerp(antennaAngles[i], targetAngles, 1 - Mathf.Exp(-dt * 18));
                antennae[i].localRotation = antennaRest[i] * Quaternion.Euler(antennaAngles[i]);
            }
            if(importedRig && !state.Airborne && !state.HasFallen) importedRig.AvoidFeelerGround(ground, dt);

            // 统计已经在摆动的腿，供后面的抬腿预算使用。预算限制新增迈步，不会强行中断旧迈步。
            int steppingCount = 0;
            foreach (var leg in legs) if (leg.stepping) steppingCount++;
            // 只有速度足够快且未进入承诺动作时使用三足交替节奏；强行发力时采用谨慎步态。
            bool catching = !state.Airborne && !state.HasFallen && speed > .12f &&
                (state.ImpactVisual > .08f || state.Balance < .45f);
            bool fast = speed > .55f && !state.Committed && !catching;
            // 根节点单次跳动超过 2 世界单位时重置落脚缓存，避免从遥远旧位置拉出长腿。
            bool teleported = Vector3.Distance(previousPosition, transform.position) > 2;
            foreach (var leg in legs)
            {
                // 名义落脚点随身体平移/转向；已落脚的 planted 尽量停留在世界原位，形成支撑期。
                leg.tumbleFrameWeight = 0;
                Vector3 nominal = NominalFoot(leg);
                if (importedRig && !state.Airborne && !state.HasFallen)
                    nominal = importedRig.PlanSupport(leg, nominal, state, ground);
                Vector3 ankle;
                int slot = (leg.side < 0 ? 0 : 3) + leg.pair;
                bool ownsLeg = state.Motion.SampleLeg(slot, out var moduleOffset);
                bool anchor = state.Motion.PlantSupportLeg(slot) && !leg.stepping && leg.landingBlend >= 1 && !teleported &&
                    (leg.supportAnchored || leg.lastFoot.y <= FootHeight(leg.lastFoot, leg) + .06f * size);
                if (anchor && !leg.supportAnchored) leg.supportAnchor = leg.lastFoot;
                leg.supportAnchored = anchor;
                if (leg.pair == 2 && legs[leg.side < 0 ? 5 : 2].stepping) ownsLeg = false;
                if (leg.moduleOwned && !ownsLeg)
                { leg.moduleReturnFrom = leg.lastFoot; leg.moduleReturn = 0; }
                leg.moduleOwned = ownsLeg;
                // 腾空时取消地面迈步，改为局部空间中的不同相位摆动，表现收腿和挣扎。
                // 被挑飞时振幅较大，已落台时较小；该分支不产生推进身体的真实力。
                if (flightSpinning || flightRecovering)
                {
                    // Far throws use a compact body-relative pose. Do not mix world-space
                    // foot chasing, ground gait or combat modules into a rotating torso.
                    float upright = Vector3.Angle(bodyPivot.up, model.up);
                    float extend = flightRecovering && !backRighting ? Ease(Mathf.InverseLerp(65, 12, upright)) : 0;
                    float order = (leg.pair == 1 ? 0 : leg.pair == 2 ? .12f : .24f) + (leg.side > 0 ? .06f : 0);
                    extend = Ease(Mathf.InverseLerp(order, 1, extend));
                    leg.tumbleFrameWeight = 1;
                    Vector3 tucked = leg.restAnkle - bodyRest;
                    float curl = flightSpinning ? LandingTuck : 1;
                    tucked.x *= Mathf.Lerp(.96f, .82f, curl);
                    tucked.y += Mathf.Lerp(.06f, .20f, curl);
                    tucked.z += (1 - leg.pair) * -.045f;
                    // Gentle airborne seeking fades out before impact. Back-down landing
                    // recruits alternating sides; all targets stay in their anatomical sector.
                    float limbPhase = flightSpinning ? flightSeconds * 7 : flightRecovery * 10;
                    limbPhase += leg.pair * 1.1f + (leg.side > 0 ? Mathf.PI : 0);
                    float struggle = flightSpinning ? .14f * (1 - LandingTuck) : backRighting ? .21f : .02f;
                    tucked.y += struggle * Mathf.Sin(limbPhase) * (1 - extend);
                    tucked.z += struggle * .45f * Mathf.Cos(limbPhase) * (1 - extend);
                    if (edgeTumbling && flightSpinning)
                    {
                        // Reach forward and rake back with alternating forelegs while the lip
                        // is still nearby. Rear legs keep their own phase; tuck before impact.
                        float reach = Mathf.Exp(-flightSeconds * 1.3f) * (1 - LandingTuck);
                        float rake = .5f + .5f * Mathf.Sin(flightSeconds * 12 + (leg.side > 0 ? Mathf.PI : 0));
                        float front = leg.pair == 0 ? 1 : leg.pair == 1 ? .35f : 0;
                        tucked.z += front * reach * (.25f + .40f * rake);
                        tucked.y += front * reach * (.16f * (1 - rake) - .12f);
                    }
                    if (backRighting)
                    {
                        float push = Mathf.Max(0, Mathf.Sin(limbPhase));
                        tucked.x += leg.side * .16f * push;
                        tucked.y -= .16f * push;
                    }
                    ankle = bodyPivot.TransformPoint(tucked);
                    Vector3 facing = Vector3.ProjectOnPlane(bodyPivot.forward, model.up);
                    if (facing.sqrMagnitude < .001f) facing = model.forward;
                    Quaternion supportFrame = Quaternion.LookRotation(facing, model.up);
                    Vector3 support = bodyPivot.position + supportFrame * ((leg.restAnkle - bodyRest) * scale);
                    support.y = state.Fall.OnFloor ? settings.groundHeight + SoleHeight(leg) : FootHeight(support, leg);
                    ankle = Vector3.Lerp(ankle, support, extend);
                    leg.stepping = leg.supportAnchored = leg.moduleOwned = false;
                    ownsLeg = false;
                    leg.moduleReturn = leg.landingBlend = 1;
                    leg.planted = ankle;
                }
                else if (state.Airborne || state.HasFallen)
                {
                    leg.stepping = false;
                    // 脚端目标跟随被撬起的胸腹姿态。先抬前足、中足，后足尽量留在原支点抓地。
                    // 支点失去之后才收腿/挣扎；不会因设了 Airborne 就让六条腿同时离地。
                    float cycle = strugglePhase + leg.pair * 2.2f + leg.side * 1.1f;
                    float amplitude = .36f * StruggleWeight;
                    Vector3 curl = new Vector3(-leg.side * .15f, .26f, -.08f * (1 - leg.pair));
                    Vector3 kick = new Vector3(leg.side * Mathf.Sin(cycle + .7f) * amplitude * .55f,
                        Mathf.Sin(cycle) * amplitude, Mathf.Cos(cycle * .86f + leg.pair) * amplitude);
                    // Immediately seek a foothold during free flight; vigorous struggling
                    // still waits for the existing lost-support delay.
                    if (!state.Lift.Supported)
                        kick += new Vector3(leg.side * .05f * Mathf.Sin(cycle), .09f * Mathf.Sin(cycle + 1), .12f * Mathf.Cos(cycle));
                    ankle = bodyPivot.TransformPoint(leg.restAnkle - bodyRest + curl + kick);
                    if (state.Fall.Active)
                    {
                        // 初始伸展寻触，继而左右错相的伸-屈动作。只有触及台外地面后才混向支撑足位。
                        float ft = state.Fall.Time;
                        float reach = 1 - Ease(Mathf.Clamp01(ft / settings.fallReachSeconds));
                        float search = (1 - reach) * .16f;
                        float legPhase = ft * 7 + leg.pair * 1.8f + leg.side * 1.2f;
                        Vector3 seek = new Vector3(leg.side * (.16f * reach + search * Mathf.Sin(legPhase)),
                            -.16f * reach + search * Mathf.Cos(legPhase), (1 - leg.pair) * .09f);
                        ankle = bodyPivot.TransformPoint(leg.restAnkle - bodyRest + curl * (1 - reach) + kick + seek);
                        if (state.Fall.OnFloor)
                        {
                            float recovery = FallRecovery(state);
                            Vector3 support = model.TransformPoint(leg.restAnkle);
                            support.y = settings.groundHeight + SoleHeight(leg);
                            // 分侧/前中后错开寻触；恢复完成后稳定撑地，避免六足同步划水或永久蹬腿。
                            float probe = Mathf.Max(0, Mathf.Sin(legPhase)) * .10f * (1 - recovery);
                            support.y += probe;
                            float settle = Ease(Mathf.Clamp01(state.Fall.GroundSeconds / .24f));
                            ankle = Vector3.Lerp(ankle, support, settle);
                            ankle.y = Mathf.Max(ankle.y, settings.groundHeight + SoleHeight(leg));
                        }
                        // 空中接续旧脚端，且限制 IK 目标速度；接触求解仍会检查所有腿和角。
                        ankle = Vector3.MoveTowards(leg.lastFoot, ankle,
                            (4 * size + speed + Mathf.Abs(state.VerticalVelocity)) * dt);
                    }
                    if (state.Lift.Supported)
                    {
                        float start = leg.pair == 0 ? .05f : leg.pair == 1 ? .25f : .5f;
                        float letGo = Ease(Mathf.InverseLerp(start, start + .4f, state.Lift.RaiseProgress));
                        ankle = Vector3.Lerp(leg.planted, ankle, letGo);
                    }
                    // 台面上方的爪不得穿入表面；台外保持自然悬空，不把脚吸回台边。
                    if (!state.HasFallen && ground != null && ground.Sample(new Vector2(ankle.x, ankle.z), out _))
                        ankle.y = Mathf.Max(ankle.y, FootHeight(ankle, leg));
                }
                else
                {
                    if (previousAirborne)
                    { leg.landingFrom = leg.lastFoot; leg.landingBlend = 0; leg.stepping = false; }
                    if (leg.landingBlend < 1)
                    {
                        leg.landingBlend = Mathf.Min(1, leg.landingBlend + dt / .18f);
                        leg.planted = Vector3.Lerp(leg.landingFrom, nominal, Ease(leg.landingBlend));
                        leg.planted.y = Mathf.Max(leg.planted.y, FootHeight(leg.planted, leg));
                        SolveLeg(leg, leg.planted, state); leg.lastFoot = ActualFoot(leg, leg.planted);
                        continue;
                    }
                    if (teleported)
                    { if (leg.stepping) steppingCount--; leg.planted = nominal; leg.stepping = false; }
                    // Contact correction and fast turns can leave a planted foot on
                    // the wrong side of its own abdomen. Replant before solving IK.
                    // 碰撞修正或急转可能让旧脚点跨入身体内侧，或离名义位置过远。
                    // 一般偏离通过较快的补步追回；只有真实传送/严重越过身体才直接重置。
                    Vector3 localPlant = model.InverseTransformPoint(leg.planted);
                    float plantError = Vector3.Distance(leg.planted, nominal);
                    bool urgent = localPlant.x * leg.side < (importedRig ? Mathf.Abs(leg.restAnkle.x) * .55f : 1.10f) || plantError > .40f * size;
                    // 长足节中足的剩余可达空间比统一位置阈值小得多，提前补步恢复弯曲余量。
                    urgent |= importedRig && plantError > .04f * size &&
                        importedRig.SupportReach(leg, leg.planted, state, ground) > .96f;
                    if (localPlant.x * leg.side < 0 || plantError > 1.5f * size)
                    {
                        if (leg.stepping) steppingCount--;
                        leg.planted = nominal; leg.stepping = false;
                    }
                    // 快走分两组：左前+左后+右中，另一组相差半周期；慢走按腿序错开相位。
                    float tripodOffset = ((leg.side < 0 && leg.pair != 1) || (leg.side > 0 && leg.pair == 1)) ? 0 : .5f;
                    float waveOffset = leg.side < 0 ? leg.pair / 3f : Mathf.Repeat(leg.pair / 3f + 1f / 3f, 1);
                    // Repeat 把持续增长的相位折回 0～1；只有进入该腿的迈步窗口才允许启动一步。
                    float cycle = Mathf.Repeat(walkCycle + (fast ? tripodOffset : waveOffset), 1);
                    float error = Vector3.Distance(leg.planted, nominal);
                    // 发力/撑地最多新增到 1 条摆动腿；普通快走上限 3，慢走上限 2。
                    int maxLifted = catching || state.Committed || state.Action == BeetleAction.Brace ? 1 : fast ? 3 : 2;
                    urgent |= catching && error > .05f * size;
                    // 同时满足：未迈步、偏离名义位置超过 0.16×体型、轮到该腿、仍有抬腿名额。
                    bool oppositeHindBusy = leg.pair == 2 &&
                        (legs[leg.side < 0 ? 5 : 2].stepping || legs[leg.side < 0 ? 5 : 2].moduleOwned ||
                         state.Motion.SampleLeg(leg.side < 0 ? 5 : 2, out _) ||
                         legs[leg.side < 0 ? 5 : 2].moduleReturn < 1);
                    if (!leg.stepping && !state.Motion.PlantSupportLeg(slot) && !oppositeHindBusy && (urgent || error > .16f * size && cycle < (fast ? .42f : .24f)) && steppingCount < (catching ? 1 : urgent ? 3 : maxLifted))
                    {
                        leg.stepFrom = leg.planted;
                        // 按当前速度预判 0.17 秒后的前方位置，让脚落在行进方向前方；开始一步后终点固定。
                        leg.stepTo = nominal + new Vector3(state.Velocity.x, 0, state.Velocity.y) * .17f;
                        leg.stepTo.y = FootHeight(leg.stepTo, leg);
                        if (importedRig) leg.stepTo = importedRig.PlanSupport(leg, leg.stepTo, state, ground);
                        // 快慢招募只影响本次摆腿；支撑脚继续世界锁定，非六腿一起加速。
                        float vigor = Mathf.Lerp(.82f, 1, state.BurstReserve);
                        leg.stepDuration = (catching ? .14f : urgent ? .12f : fast ? .16f : .22f) / vigor;
                        leg.stepDuration *= Mathf.Lerp(.93f, 1.07f, InsectMotionTiming.Value(Mathf.FloorToInt(walkCycle), slot + 331));
                        leg.stepTime = 0; leg.stepping = true; steppingCount++;
                    }
                    if (leg.stepping)
                    {
                        // 每一步使用启动时确定的时长；除以该时长得到归一化进度。
                        leg.stepTime += dt / Mathf.Max(.08f, leg.stepDuration);
                        float t = Mathf.Clamp01(leg.stepTime);
                        // 水平路径用分段 Swing 曲线；竖直额外偏移 sin(πt)×0.13×体型。
                        // t=0/1 时额外高度为零，t=0.5 最高；这条弧线生成 IK 的输入目标。
                        leg.planted = Vector3.Lerp(leg.stepFrom, leg.stepTo, InsectMuscleAnimation.Swing(t)) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * (catching ? .22f : .13f) * size);
                        // 跨越凸起时沿整条摆腿路径查地表，足尖留出抬脚余量，不仅检查起终点。
                        leg.planted.y = Mathf.Max(leg.planted.y, FootHeight(leg.planted, leg) + Mathf.Sin(t * Mathf.PI) * .08f * size);
                        if (t >= 1) { leg.stepping = false; steppingCount--; }
                    }
                    ankle = leg.planted;
                }
                // 模块只交脚端意图，宿主统一做地面投影、速度约束和原有 IK/碰撞。
                if (leg.supportAnchored)
                {
                    // Keep the accepted ground point in world space as the torso rises/drives.
                    // IK may lose reach; the resulting real foot gap reduces simulation drive.
                    ankle = leg.supportAnchor; leg.planted = ankle;
                    leg.moduleReturn = 1; leg.stepping = false;
                }
                else if (ownsLeg)
                {
                    Vector3 target = state.Fall.OnFloor ? model.TransformPoint(leg.restAnkle) : nominal;
                    if (state.Fall.OnFloor) target.y = settings.groundHeight + SoleHeight(leg);
                    target += model.TransformVector(moduleOffset);
                    target.y = Mathf.Max(target.y, state.Fall.OnFloor ? settings.groundHeight + SoleHeight(leg) : FootHeight(target, leg));
                    ankle = Vector3.MoveTowards(leg.lastFoot, target, (state.Motion.LegSpeed(slot) * size + speed) * dt);
                    leg.moduleReturn = 1;
                    leg.planted = ankle; leg.stepping = false;
                }
                else if (leg.moduleReturn < 1)
                {
                    // 从退出瞬间的实际脚位接续，避免插件取消时瞬间回到名义点。
                    leg.moduleReturn = Mathf.Min(1, leg.moduleReturn + dt / .18f);
                    Vector3 target = state.Fall.OnFloor ? model.TransformPoint(leg.restAnkle) : state.Airborne ? ankle : nominal;
                    if (state.Fall.OnFloor) target.y = settings.groundHeight + SoleHeight(leg);
                    ankle = Vector3.Lerp(leg.moduleReturnFrom, target, Ease(leg.moduleReturn));
                    leg.planted = ankle; leg.stepping = false;
                }
                // 无论脚正在支撑、迈步还是腾空，都用同一个求解器连接腿根与脚踝目标。
                if (EdgeReach > .01f && !state.Airborne && !state.HasFallen &&
                    !flightSpinning && !flightRecovering && leg.pair == 0 && !ownsLeg)
                {
                    float rake = Mathf.Max(0, Mathf.Sin(time * 12 + (leg.side > 0 ? Mathf.PI : 0)));
                    Vector3 reaching = nominal + model.TransformVector(new Vector3(0, .18f * rake, .45f * rake));
                    if (ground != null && ground.Sample(new Vector2(reaching.x, reaching.z), out _))
                        reaching.y = Mathf.Max(reaching.y, FootHeight(reaching, leg));
                    ankle = Vector3.Lerp(ankle, reaching, EdgeReach);
                    leg.planted = ankle;
                }
                SolveLeg(leg, ankle, state);
                leg.lastFoot = ActualFoot(leg, ankle);
            }
            previousAirborne = state.Airborne || state.HasFallen;
            previousAction = state.Action;
            previousPosition = transform.position;
        }

        // 接触限制可能只接受部分姿态。下一步从实际接受的关节角继续，落地也使用实际脚尖。
        // 地面 planted 仍是世界空间抓地点，不能随整虫修正一起平移，否则会滑步。
        public void AcceptSolvedPose(bool limited)
        {
            if (limited)
            {
                bodyPitch = Mathf.DeltaAngle(0, bodyPivot.localEulerAngles.x);
                bodyRoll = Mathf.DeltaAngle(0, bodyPivot.localEulerAngles.z);
                headPitch = Mathf.DeltaAngle(0, headPivot.localEulerAngles.x);
                headYaw = Mathf.DeltaAngle(0, headPivot.localEulerAngles.y);
            }
            foreach (var leg in legs) leg.lastFoot = ActualFoot(leg, leg.lastFoot);
            previousPosition = transform.position;
        }

        Vector3 ActualFoot(BeetleLegRig leg, Vector3 fallback) => importedRig
            ? importedRig.bindings[(leg.side < 0 ? 0 : 3) + leg.pair].tip.position : fallback;

        // 接触约束的局部反馈：角下受力先让头关节退让，不能把一根角的接触全换算为根部后退。
        public void YieldHead(float degrees)
        {
            // 接触重试先恢复候选 Transform，却不会回滚动画平滑缓存。
            // 从本次实际试算姿态取角，避免失败的重试累计退让或覆盖插值后的偏航。
            headPitch = Mathf.DeltaAngle(0, headPivot.localEulerAngles.x);
            headYaw = Mathf.DeltaAngle(0, headPivot.localEulerAngles.y);
            // 已经高于退让范围时保持原角度。直接 Max(-32, ...) 会把已扬到 -46 度
            // 的头强行拉回 -32 度，瞬间重新顶入对手并引发整虫位置修正。
            headPitch = Mathf.Min(headPitch, Mathf.Max(-55, headPitch - degrees));
            headPivot.localRotation = Quaternion.Euler(headPitch, headYaw, 0);
        }
        // 可动腿被角/对方脚挡住时调整其 IK 目标，骨长不变；坚硬胸腹才承担整虫分离。
        public void YieldLeg(int index, Vector3 direction, BeetleState state)
        {
            var leg = legs[index];
            Vector3 outward = model.right * leg.side;
            Vector3 move = Vector3.ProjectOnPlane(direction, Vector3.up) + outward * .5f;
            if (move.sqrMagnitude < .001f) move = outward;
            Vector3 target = ActualFoot(leg, leg.planted) + move.normalized * .035f + Vector3.up * .025f;
            target.y = Mathf.Max(target.y, FootHeight(target, leg));
            leg.planted = target; leg.stepping = false;
            SolveLeg(leg, target, state); leg.lastFoot = ActualFoot(leg, target);
        }

        // 把初始脚踝位置从模型局部空间变为世界坐标，再设定台面上的脚踝高度。
        Vector3 NominalFoot(BeetleLegRig leg)
        {
            var foot = model.TransformPoint(leg.restAnkle);
            foot.y = FootHeight(foot, leg);
            return foot;
        }
        public Vector3 MotionFootBase(int slot, BeetleState state)
        {
            var leg = legs[slot]; var target = NominalFoot(leg);
            return importedRig && !state.Airborne && !state.HasFallen
                ? importedRig.PlanSupport(leg, target, state, ground) : target;
        }
        // While the body is supported, a foot beyond the rim is held above the
        // edge. Lowering its target through the stump wall made the whole compound
        // body get pushed sideways by its own misplaced foot.
        // 查询足踝下方的真实台面；台外无命中时收在根部高度附近，不向森林地板伸长腿。
        float FootHeight(Vector3 p, BeetleLegRig leg = null)
        {
            float clearance = importedRig ? (importedRig.soleClearance + (leg == null ? 0 : leg.soleOffset)) * scale : .13f * size;
            if (ground == null) return clearance;
            return (ground.Sample(new Vector2(p.x, p.z), out var hit) ? hit.height : transform.position.y) + clearance;
        }

        // 两段式解析 IK：已知腿根 A、目标 C、两段长度 a/b，反算中间关节 B。
        // 所有三角形计算在世界空间完成；state 参数目前未参与这个方法的计算。
        void SolveLeg(BeetleLegRig leg, Vector3 target, BeetleState state)
        {
            if (importedRig) { importedRig.Solve(leg, target, state, ground); return; }
            // 先回到模型局部空间限制脚的活动区域：左右不越过身体内侧，前后不跨入相邻腿区域。
            Quaternion legFrame = Quaternion.Slerp(model.rotation, bodyPivot.rotation, leg.tumbleFrameWeight);
            Vector3 legOrigin = Vector3.Lerp(model.TransformPoint(bodyRest), bodyPivot.position, leg.tumbleFrameWeight);
            Vector3 localTarget = Quaternion.Inverse(legFrame) * (target - legOrigin) / scale + bodyRest;
            localTarget.x = leg.side * Mathf.Max(1.02f, localTarget.x * leg.side);
            // Keep each leg in its anatomical sector instead of crossing neighbours.
            localTarget.z = Mathf.Clamp(localTarget.z, leg.restAnkle.z - .32f, leg.restAnkle.z + .32f);
            target = legOrigin + legFrame * ((localTarget - bodyRest) * scale);
            // 解剖活动区限制改变了 XZ，必须重新校正支撑目标的 Y，避免限位后落入坡面。
            if (!state.Airborne && !state.HasFallen) target.y = Mathf.Max(target.y, FootHeight(target));
            Vector3 hip = bodyPivot.TransformPoint(leg.hipInBody);
            float upper = leg.upperLength * scale, lower = leg.lowerLength * scale;
            Vector3 offset = target - hip;
            // 三角形可达距离为 |a-b|～a+b；留 0.001 余量避免完全折叠或拉直。
            // 超出范围时夹紧脚踝目标，因此最终脚点可能与输入目标存在偏差。
            float distance = Mathf.Clamp(offset.magnitude, Mathf.Abs(upper - lower) + .001f, upper + lower - .001f);
            Vector3 direction = offset.sqrMagnitude > .00001f ? offset.normalized : Vector3.down;
            target = hip + direction * distance;
            // 弯曲提示朝身体外侧、略向上，前后腿再带一点前后偏向。
            // 投影到垂直于腿根→脚端的平面，确保后面的 along 与 bend 两个方向正交。
            // 当前模型依赖固定姿态范围；若提示与目标方向平行，多段/FBX 版本需增加备用方向。
            Vector3 pole = legFrame * new Vector3(leg.side, .4f, (1 - leg.pair) * .18f);
            pole = Vector3.ProjectOnPlane(pole, direction).normalized;
            // 余弦定理：中间关节在 A→C 方向的投影长度 x=(a²-b²+d²)/(2d)。
            float along = (upper * upper - lower * lower + distance * distance) / (2 * distance);
            // 勾股定理：离开 A→C 直线的弯曲高度 h=√(a²-x²)；Max(0,...) 防浮点误差开负平方根。
            float bend = Mathf.Sqrt(Mathf.Max(0, upper * upper - along * along));
            // 最终中间关节 B=A+direction×x+pole×h；给定 pole 后选择了一个确定的弯曲平面。
            Vector3 knee = hip + direction * along + pole * bend;
            // 写入关节点，再把两段胶囊体接在关节点之间。此处直接改几何体，未驱动蒙皮骨骼。
            leg.hip.position = hip;
            leg.knee.position = knee;
            leg.ankle.position = target;
            Segment(leg.upper, hip, knee);
            Segment(leg.lower, knee, target);
            // 主腿求解完成后，由脚踝构造脚趾、爪和胫刺位置；这些附属段不单独执行 IK。
            Vector3 footNormal = state.SurfaceNormal;
            if (!state.Airborne && ground != null && ground.Sample(new Vector2(target.x, target.z), out var footHit)) footNormal = footHit.normal;
            Quaternion footFrame = Quaternion.FromToRotation(Vector3.up, footNormal) * Quaternion.Euler(0, state.Heading, 0);
            footFrame = Quaternion.Slerp(footFrame, bodyPivot.rotation, leg.tumbleFrameWeight);
            Vector3 toe = target + footFrame * new Vector3(leg.side * .15f, -.11f, .16f) * scale;
            Segment(leg.foot, target, toe);
            for (int i = 0; i < leg.claws.Length; i++)
                Segment(leg.claws[i], toe, toe + footFrame * new Vector3(leg.side * .075f, -.02f, i == 0 ? -.07f : .07f) * scale);
            for (int i = 0; i < leg.teeth.Length; i++)
            {
                Vector3 p = Vector3.Lerp(knee, target, .39f + i * .24f);
                Segment(leg.teeth[i], p, p + transform.TransformDirection(new Vector3(leg.side * .1f, .015f, .065f)) * scale);
            }
        }

        // 把沿本地 Y 轴的胶囊体摆在两个世界端点之间：中点定位、朝向连线、调整长度。
        // 这是当前基本几何体的驱动方式；FBX 骨骼通常应保留骨长并旋转骨骼。
        static void Segment(Transform segment, Vector3 a, Vector3 b)
        {
            segment.position = (a + b) * .5f;
            segment.rotation = Quaternion.FromToRotation(Vector3.up, b - a);
            Vector3 s = segment.localScale;
            // Unity 默认胶囊 Y 向总长为 2；再除父级世界 Y 缩放，避免模型整体缩放后长度被重复放大。
            // 该换算依赖当前层级的缩放约定，不是任意非均匀缩放/剪切层级的通用解。
            s.y = Vector3.Distance(a, b) / (2 * Mathf.Max(.001f, segment.parent.lossyScale.y));
            segment.localScale = s;
        }
        // 三次平滑函数 3t²-2t³：端点值为 0/1，端点一阶导数为 0，使插值起止速度平缓。
        static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }
    }
}
