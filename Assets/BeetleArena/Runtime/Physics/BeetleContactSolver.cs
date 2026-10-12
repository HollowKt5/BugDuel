using UnityEngine;

namespace BeetleArena
{
    // 模拟依赖的接触接口：实现可以读取并修正双方状态。缺省为空时走纯数据备用分离算法。
    public interface IBeetleContactSolver { void Solve(BeetleSimulation simulation, float dt); }

    // Kinematic articulated contact constraints. PhysX supplies narrow-phase convex
    // mesh contacts; this solver owns movement so animation cannot override a rigidbody.
    // 细分凸网格的运动学接触求解器：PhysX 负责检测，代码负责位置和速度修正。
    // 不是让动态 Rigidbody 自由碰撞；也没有逐脚求解摩擦、肌肉或真实角尖杠杆力。
    public sealed class BeetleContactSolver : IBeetleContactSolver, IBeetleLiftContacts, IBeetleMotionContacts
    {
        // Actor 提供姿态，ContactRig 保存部件碰撞体，arena 保存树桩和地面的接触体。
        readonly BeetleActor[] actors;
        readonly BeetleContactRig[] rigs;
        readonly Collider[] arena;
        // 分离时保留的小间隙，降低刚好贴边时下一步反复穿入造成的抖动。
        readonly float skin;
        // 复用候选索引数组，减少每次求解中因临时集合带来的托管分配。
        readonly int[] candidatesA, candidatesB;
        // 上一轮互碰分离后的余量；斜坡会把双方重新挤近，不能仅凭环境本轮未移动就提前结束。
        float separationRemaining;
        // 诊断值：记录 Separate 阶段测得的历史最大残余重叠，不代表所有时刻/所有物体的绝对上限。
        public float MaximumResidual { get; private set; }
        // 记录 Solve 调用中出现甲虫相互接触的次数；不包含仅接触台面的情况。
        public int ContactFrames { get; private set; }
        public string LastWorstContact { get; private set; }
        public string LastSeparationPair { get; private set; }
        public Vector3 LastSeparationNormal { get; private set; }
        readonly BeetleHornSupport hornSupport = new BeetleHornSupport();
        readonly BeetleContactMotion[] motion, viewMotion;
        bool viewInitialized;
        readonly Vector2[] proposedVelocity = new Vector2[2];
        readonly float[] motionBudget = new float[2];
        public int LimitedMotionSteps { get; private set; }
        public float AcceptedMotionFraction { get; private set; } = 1;
        // 本次 Solve 真正使用的预算；落地后速度可能清零，诊断不能由事后速度重新猜测。
        public float MotionBudget(int index) => motionBudget[index];
        public string LiftContact => hornSupport.ContactDescription;
        public float LiftAnchorError => hornSupport.Error;
        public bool TryBeginLift(BeetleSimulation sim, BeetleState attacker, BeetleState defender)
        {
            if (hornSupport.Active(sim) || attacker.Lift.Supported || defender.Lift.Supported) return false;
            Physics.SyncTransforms(); Cache();
            return hornSupport.TryCapture(attacker.Index, rigs[attacker.Index], rigs[defender.Index]);
        }
        // 每局创建求解器，收集两套碰撞体并启用 ContactDriven，使姿态随固定模拟推进。
        public BeetleContactSolver(BeetleActor[] actors, Collider[] arena, float skin)
        {
            this.actors = actors; this.arena = arena; this.skin = skin;
            motion = new[] { new BeetleContactMotion(actors[0]), new BeetleContactMotion(actors[1]) };
            viewMotion = new[] { new BeetleContactMotion(actors[0]), new BeetleContactMotion(actors[1]) };
            rigs = new[] { actors[0].GetComponent<BeetleContactRig>(), actors[1].GetComponent<BeetleContactRig>() };
            candidatesA = new int[rigs[0].shapes.Length]; candidatesB = new int[rigs[1].shapes.Length];
            foreach (var actor in actors) actor.ContactDriven = true;
        }
        // 先将 Actor 放到模拟位置并更新关节，再同步 PhysX 变换和包围盒，最后执行分离。
        // 修正后的根节点位置写回模拟状态，成为下一步积分和落台判断的起点。
        public void Solve(BeetleSimulation sim, float dt)
        {
            for (int i = 0; i < 2; i++)
            {
                motion[i].Begin();
                var b = sim.Fighters[i]; proposedVelocity[i] = b.Velocity;
                // 保留真实抛物运动的速度；普通关节碰撞修正只有统一的整步预算。
                float physicalSpeed = new Vector3(b.Velocity.x, b.Airborne ? b.VerticalVelocity : 0, b.Velocity.y).magnitude;
                motionBudget[i] = Mathf.Max(sim.Settings.contactMotionSpeed, physicalSpeed + .5f) * dt;
                actors[i].PoseSimulation(sim.MotionTime, dt);
            }
            Physics.SyncTransforms();
            Cache();
            if (dt > 0 && (ArticulatedContact(sim.Fighters[0]) || ArticulatedContact(sim.Fighters[1]))) YieldArticulatedContacts(sim);
            if (hornSupport.Active(sim)) hornSupport.Follow(sim, rigs[hornSupport.Defender], dt);
            foreach (var m in motion) m.Propose();
            bool contact = Constrain(sim, true);
            AcceptedMotionFraction = 1;
            if (dt > 0 && !MotionAccepted())
            {
                LimitedMotionSteps++;
                // 动画先试完整步；若多次分离累计成瞬移，则逐次缩小姿态推进量。
                // 所有重试共用原始速度，拒绝的试算不能消耗或产生动量。
                bool accepted = false;
                for (int attempt = 1; attempt <= 7; attempt++)
                {
                    float fraction = Mathf.Pow(.5f, attempt);
                    for (int i = 0; i < 2; i++)
                    { motion[i].Apply(fraction); sim.Fighters[i].Velocity = proposedVelocity[i]; }
                    Physics.SyncTransforms(); Cache();
                    // 缩小整体姿态后重新求局部避让。不能把必要的头角退让也缩到几乎为零，
                    // 否则互锁的叉角永远无法解开；每次重试都从同一姿态提案重建，不累计转角。
                    if (ArticulatedContact(sim.Fighters[0]) || ArticulatedContact(sim.Fighters[1])) YieldArticulatedContacts(sim);
                    contact = Constrain(sim, true);
                    AcceptedMotionFraction = fraction;
                    if (MotionAccepted()) { accepted = true; break; }
                }
                if (!accepted)
                {
                    // 上一步是已验证的安全姿态。暂缓受阻动作，不能强行平移整虫或关掉碰撞体。
                    for (int i = 0; i < 2; i++)
                    { motion[i].Apply(0); sim.Fighters[i].Velocity = proposedVelocity[i]; }
                    Physics.SyncTransforms(); Cache(); AcceptedMotionFraction = 0; contact = true;
                }
            }
            for (int i = 0; i < 2; i++)
            {
                var state = sim.Fighters[i];
                Vector3 p = actors[i].transform.position;
                state.Position = new Vector2(p.x, p.z); state.Height = p.y;
                if (state.HasFallen)
                {
                    bool floor = HasForestFloorContact(rigs[i], sim.Settings.groundHeight);
                    state.Fall.SetFloorContact(floor, -state.VerticalVelocity);
                    if (floor && state.VerticalVelocity < 0) state.VerticalVelocity = 0;
                }
                // 双方共用本次是否相互接触的结果。当前不记录具体接触部位、法线或接触点。
                state.TouchingOpponent = contact;
                if (state.Lift.Supported)
                    state.Lift.BlockedTime = AcceptedMotionFraction < .05f ? state.Lift.BlockedTime + dt : Mathf.Max(0, state.Lift.BlockedTime - 2 * dt);
                UpdateFootSupport(actors[i], sim, dt);
                actors[i].animator.AcceptSolvedPose(AcceptedMotionFraction < 1);
                actors[i].CaptureSimulationPose();
            }
            // 所有回退/环境修正均已结束，才将这一步实际抬头做功计入撬投。
            if (hornSupport.Active(sim)) hornSupport.Measure(sim, actors[hornSupport.Attacker], dt);
            if (!viewInitialized || dt <= 0)
            { foreach (var m in viewMotion) m.Begin(); viewInitialized = true; }
            if (contact) ContactFrames++;
        }
        // 将当前接受姿态向下探 3.5cm，确认确实接触配置高度处的台外地面。
        // 用接触法线而非“判负/根节点高度”猜落地；不把擂台侧壁当作地面。
        bool HasForestFloorContact(BeetleContactRig rig, float floorHeight)
        {
            if (arena == null) return false;
            foreach (var surface in arena)
            {
                if (!surface || !surface.enabled || !surface.gameObject.activeInHierarchy ||
                    Mathf.Abs(surface.bounds.max.y - floorHeight) > .05f) continue;
                foreach (var shape in rig.shapes)
                {
                    if (shape.bounds.min.y > floorHeight + .05f) continue;
                    if (Physics.ComputePenetration(shape, shape.transform.position - Vector3.up * .035f,
                        shape.transform.rotation, surface, surface.transform.position, surface.transform.rotation,
                        out var normal, out _) && normal.y > .5f) return true;
                }
            }
            return false;
        }
        public bool HasMotionContact(int attacker, BeetleMotionContact kind, int forelegSlot)
        {
            if (attacker < 0 || attacker > 1 || kind == BeetleMotionContact.None) return false;
            Physics.SyncTransforms(); Cache();
            var a = rigs[attacker]; var b = rigs[1 - attacker];
            for (int i = 0; i < a.shapes.Length; i++)
            {
                if (kind == BeetleMotionContact.Horn && a.Role(i) != BeetleContactRole.HeadHorn) continue;
                if (kind == BeetleMotionContact.Foreleg && a.LegIndex(i) != forelegSlot) continue;
                for (int j = 0; j < b.shapes.Length; j++)
                {
                    // 前足必须触到对方足/身体，不能把两个角碰到当作前足打中。
                    if (kind == BeetleMotionContact.Foreleg && b.Role(j) == BeetleContactRole.HeadHorn) continue;
                    var bounds = a.bounds[i]; bounds.Expand(.05f);
                    if (!bounds.Intersects(b.bounds[j])) continue;
                    var ca = a.shapes[i]; var cb = b.shapes[j];
                    Vector3 toward = (cb.ClosestPoint(a.bounds[i].center) - a.bounds[i].center).normalized;
                    // 接触求解保留 skin 间隙，向对方探测 2cm；无碰撞的动作仍可完成但无命中。
                    if (Physics.ComputePenetration(ca, ca.transform.position + toward * .02f, ca.transform.rotation,
                        cb, cb.transform.position, cb.transform.rotation, out _, out _)) return true;
                }
            }
            return false;
        }
        bool MotionAccepted()
        {
            if (separationRemaining > .001f) return false;
            for (int i = 0; i < 2; i++)
                if (Vector3.Distance(motion[i].Start, actors[i].transform.position) > motionBudget[i] + .0001f) return false;
            return true;
        }
        static void UpdateFootSupport(BeetleActor actor, BeetleSimulation sim, float dt)
        {
            var state = actor.State; var anim = actor.animator;
            if (!state.Airborne || state.HasFallen)
            { state.Lift.UnsupportedTime = 0; state.Lift.GripReleased = false; return; }
            bool free = true;
            if (anim.importedRig)
                for (int i = 0; i < anim.legs.Length; i++)
                {
                    Vector3 foot = anim.importedRig.bindings[i].tip.position;
                    float sole = (anim.legs[i].soleOffset + anim.importedRig.soleClearance) * Mathf.Abs(anim.model.lossyScale.x);
                    if (sim.SampleGround(new Vector2(foot.x, foot.z), out var hit) && foot.y - hit.height - sole < .045f)
                    { free = false; break; }
                }
            else free = state.Height - state.SurfaceHeight > .14f;
            if (free) state.Lift.GripReleased = true;
            state.Lift.UnsupportedTime = state.Lift.GripReleased ? state.Lift.UnsupportedTime + dt : 0;
        }
        // 渲染插值后再约束一次模型位置，覆盖两个安全端点之间仍可能重叠的情况。
        // 此路径不修改模拟速度，也不把显示位置回写状态；下一步会重新按模拟数据摆放 Actor。
        public void ProjectView(BeetleSimulation sim, float dt = BeetleSimulation.FixedStep)
        {
            // 上一显示帧的安全姿态作为起点。回退固定步 alpha 会让画面倒跳；这里沿上一
            // 实际显示姿态连续前进，时间步决定可移动距离。显示缓存不参与权威模拟。
            foreach (var m in viewMotion) m.Propose();
            bool accepted = false;
            for (int attempt = 0; attempt <= 7; attempt++)
            {
                if (attempt > 0)
                    foreach (var m in viewMotion) m.Apply(Mathf.Pow(.5f, attempt));
                Physics.SyncTransforms(); Cache(); Constrain(sim, false);
                bool safe = separationRemaining <= .001f;
                for (int i = 0; i < 2; i++)
                {
                    var state = sim.Fighters[i];
                    float speed = new Vector3(state.Velocity.x, state.Airborne ? state.VerticalVelocity : 0, state.Velocity.y).magnitude;
                    float budget = Mathf.Max(sim.Settings.contactMotionSpeed, speed + .5f) * Mathf.Max(0, dt);
                    safe &= Vector3.Distance(viewMotion[i].Start, actors[i].transform.position) <= budget + .0001f;
                }
                if (safe) { accepted = true; break; }
            }
            if (!accepted) foreach (var m in viewMotion) m.Apply(0);
            Physics.SyncTransforms(); Cache();
            foreach (var m in viewMotion) m.Begin();
            foreach (var actor in actors) actor.UpdateMarker();
        }
        // 最多八轮交替处理甲虫互碰和环境碰撞；环境修正可能把甲虫重新挤向对方。
        // 当环境不再移动模型时提前停止，避免每帧总是跑满迭代预算。
        bool Constrain(BeetleSimulation sim, bool changeVelocity)
        {
            bool contact = false;
            for (int pass = 0; pass < 8; pass++)
            {
                contact |= Separate(sim, changeVelocity);
                bool arenaMoved = false;
                foreach (var rig in rigs) arenaMoved |= ResolveArena(rig);
                if (!arenaMoved && separationRemaining <= .0005f) break;
            }
            // 最后一轮环境修正也可能重新挤入对手，不能沿用 Separate 之前的残余值。
            // 将台面与双方全部代理的最终检测作为接受条件，防止岩台把不安全姿态存成回退点。
            separationRemaining = MeasurePenetration(false);
            return contact;
        }
        // 按当前姿态更新部件与整只甲虫的世界 AABB，供后面的宽阶段快速筛选。
        void Cache() { foreach (var rig in rigs) rig.CacheBounds(); }
        // 双方沿中心连线的水平轴分离，最多 24 次。限制为水平修正，避免角的碰撞法线
        // 把双方都无意推到空中；真正抬举由战斗规则显式给竖直速度。
        bool Separate(BeetleSimulation sim, bool changeVelocity)
        {
            // 托举时沿真实三维接触法线解开重叠。旧算法强制水平分离，会把扬角的竖向接触
            // 放大成后退，甚至在有向上速度时仍先把双方横向弹开。
            if (hornSupport.Active(sim)) return SeparateSupported(sim);
            // 脱角后直到身体姿态恢复，继续保留三维接触响应；不能在释放这一帧突然切回横推。
            if (hornSupport.Attacker >= 0 && Mathf.Abs(sim.Fighters[hornSupport.Defender].Lift.Pitch) > .01f)
            {
                if (sim.Fighters[hornSupport.Defender].Airborne) return SeparateSupported(sim);
                bool recoveryContact = SeparateInsertion(sim, changeVelocity, hornSupport.Attacker);
                if (separationRemaining > .001f) recoveryContact |= SeparateCentered(sim, changeVelocity);
                return recoveryContact;
            }
            bool insertingA = Inserting(sim.Fighters[0]);
            bool insertingB = Inserting(sim.Fighters[1]);
            if (insertingA || insertingB)
            {
                bool insertionContact = SeparateInsertion(sim, changeVelocity, insertingA ? 0 : 1);
                // 交错的多条腿可能让逐接触投影互相抵消。只有剩余穿透超出容差才退回保守分离。
                if (separationRemaining > .001f) insertionContact |= SeparateCentered(sim, changeVelocity);
                return insertionContact;
            }
            return SeparateCentered(sim, changeVelocity);
        }
        bool SeparateCentered(BeetleSimulation sim, bool changeVelocity)
        {
            bool contact = false;
            Vector3 axis = rigs[0].transform.position - rigs[1].transform.position;
            axis.y = 0; axis = axis.sqrMagnitude > .000001f ? axis.normalized : Vector3.right;
            // A 的位移权重使用 B.mass/(A.mass+B.mass)：A 越重，自身被移开的比例越小。
            float aWeight = sim.Fighters[1].Traits.mass / (sim.Fighters[0].Traits.mass + sim.Fighters[1].Traits.mass);
            for (int iteration = 0; iteration < 24; iteration++)
            {
                float push = RequiredSeparation(axis, out float depth);
                // 小于容差则停止；单次修正量也限幅，减轻复杂接触突然大幅弹开。
                if (depth <= .0005f) break;
                contact = true;
                // Along the centre line keeps horns from lifting both beetles into
                // the air. Actual lifting remains the explicitly simulated action.
                push = Mathf.Clamp(push + skin, .005f, .3f);
                rigs[0].Translate(axis * push * aWeight);
                rigs[1].Translate(-axis * push * (1 - aWeight));
            }
            RequiredSeparation(axis, out float residual);
            separationRemaining = residual;
            MaximumResidual = Mathf.Max(MaximumResidual, residual);
            // 模拟阶段移除两者沿分离轴继续靠近的相对速度；切向速度保留。
            // 这里只消除相互挤入，不负责产生攻击推力，后者由 Simulation.ApplyHit 计算。
            if (contact && changeVelocity)
            {
                Vector2 n = new Vector2(axis.x, axis.z);
                float closing = Vector2.Dot(sim.Fighters[0].Velocity - sim.Fighters[1].Velocity, n);
                if (closing < 0)
                {
                    sim.Fighters[0].Velocity -= n * closing * aWeight;
                    sim.Fighters[1].Velocity += n * closing * (1 - aWeight);
                }
            }
            return contact;
        }
        static bool Inserting(BeetleState state) => state.Action == BeetleAction.PryWindup || state.Action == BeetleAction.Pry;

        // 托举与脱角仍允许头/腿局部退让。仅在入角时启用会使岩台高低差下的叉角互锁，
        // 受击者无法沿任何小步安全抬起，最终整只虫子僵住到托举结束。
        static bool ArticulatedContact(BeetleState state) => Inserting(state) ||
            state.Action == BeetleAction.Lifting || state.Action == BeetleAction.ReleaseLift;

        void YieldArticulatedContacts(BeetleSimulation sim)
        {
            // 每步只处理一轮局部退让，之后重新同步实际胶囊体；绝不关闭或忽略它们的碰撞。
            var a=rigs[0];var b=rigs[1];
            int changedLegsA=0,changedLegsB=0;bool headA=false,headB=false,changed=false;
            for(int i=0;i<a.shapes.Length;i++)for(int j=0;j<b.shapes.Length;j++)
            {
                if(!a.bounds[i].Intersects(b.bounds[j]))continue;
                var ca=a.shapes[i];var cb=b.shapes[j];
                if(!Physics.ComputePenetration(ca,ca.transform.position,ca.transform.rotation,cb,cb.transform.position,cb.transform.rotation,out var n,out float d)||d<.0005f)continue;
                var ra=a.Role(i);var rb=b.Role(j);
                if(ra==BeetleContactRole.HeadHorn && rb==BeetleContactRole.HeadHorn)
                {
                    if(n.y<-.25f && !headB)
                    {actors[1].animator.YieldHead(4);headB=true;changed=true;}
                    else if(n.y>.25f && !headA)
                    {actors[0].animator.YieldHead(4);headA=true;changed=true;}
                }
                int la=a.LegIndex(i),lb=b.LegIndex(j);
                if(la>=0 && (changedLegsA&(1<<la))==0)
                {actors[0].animator.YieldLeg(la,n,sim.Fighters[0]);changedLegsA|=1<<la;changed=true;}
                if(lb>=0 && (changedLegsB&(1<<lb))==0)
                {actors[1].animator.YieldLeg(lb,-n,sim.Fighters[1]);changedLegsB|=1<<lb;changed=true;}
            }
            if(changed){Physics.SyncTransforms();Cache();}
        }

        // 入角阶段还在地面上：沿真实法线的水平投影侧向滑开，让叉角可以从旁边通过。
        // 旧的中心轴投影丢失了侧向滑动，近乎竖直的接触反而被放大成整虫倒退。
        bool SeparateInsertion(BeetleSimulation sim, bool changeVelocity, int attacker)
        {
            bool contact = false; var a = rigs[0]; var b = rigs[1];
            float weight = attacker == 0 ? .75f : .25f;
            for (int pass = 0; pass < 32; pass++)
            {
                float deepest = 0; Vector3 n = Vector3.zero;
                for(int i=0;i<a.shapes.Length;i++)for(int j=0;j<b.shapes.Length;j++)
                {
                    if(!a.bounds[i].Intersects(b.bounds[j]))continue;
                    var ca=a.shapes[i];var cb=b.shapes[j];
                    if(Physics.ComputePenetration(ca,ca.transform.position,ca.transform.rotation,cb,cb.transform.position,cb.transform.rotation,out var normal,out float depth) && depth>deepest)
                    { deepest=depth;n=normal;LastSeparationPair=ca.name+" / "+cb.name;LastSeparationNormal=normal; }
                }
                if(deepest<.0005f)break;
                contact=true;
                Vector3 horizontal=new Vector3(n.x,0,n.z);
                float projection=horizontal.magnitude;
                if(projection<.001f)horizontal=(a.transform.position-b.transform.position).normalized;
                else horizontal/=projection;
                Vector3 shift=horizontal*Mathf.Min(.08f,(deepest+skin)/Mathf.Max(.2f,projection));
                a.Translate(shift*weight);b.Translate(-shift*(1-weight));
                if(changeVelocity)
                {
                    Vector2 axis=new Vector2(horizontal.x,horizontal.z);
                    float closing=Vector2.Dot(sim.Fighters[0].Velocity-sim.Fighters[1].Velocity,axis);
                    if(closing<0){sim.Fighters[0].Velocity-=axis*closing*weight;sim.Fighters[1].Velocity+=axis*closing*(1-weight);}
                }
            }
            RequiredSeparation(Vector3.right,out float residual);
            separationRemaining=residual;MaximumResidual=Mathf.Max(MaximumResidual,residual);
            return contact;
        }
        bool SeparateSupported(BeetleSimulation sim)
        {
            var a = rigs[hornSupport.Attacker]; var b = rigs[hornSupport.Defender];
            bool contact = false;
            for (int pass = 0; pass < 32; pass++)
            {
                float deepest = 0; Vector3 correction = Vector3.zero;
                for (int i = 0; i < a.shapes.Length; i++)
                for (int j = 0; j < b.shapes.Length; j++)
                {
                    if (!a.bounds[i].Intersects(b.bounds[j])) continue;
                    var ca = a.shapes[i]; var cb = b.shapes[j];
                    if (Physics.ComputePenetration(cb, cb.transform.position, cb.transform.rotation,
                        ca, ca.transform.position, ca.transform.rotation, out var normal, out float depth) && depth > deepest)
                    { deepest = depth; correction = normal * (depth + skin); }
                }
                if (deepest < .0005f) break;
                contact = true;
                // 攻击者六足提供地面反力，受击者沿分离法线响应；不把攻击者一同抬到空中。
                if (correction.y < 0 && b.transform.position.y < sim.Fighters[hornSupport.Defender].SurfaceHeight + .12f)
                {
                    // 受击者仍贴台面时，不能沿斜向下的角碰撞法线把它压进地里，
                    // 再让地面把它弹回原处。攻击者在水平面退让，解除这组互相抵消的约束。
                    Vector3 horizontal = new Vector3(correction.x, 0, correction.z);
                    float fraction = horizontal.magnitude / Mathf.Max(.00001f, correction.magnitude);
                    if (fraction > .05f)
                        a.Translate(-Vector3.ClampMagnitude(horizontal / Mathf.Max(.1f, fraction * fraction), .04f));
                    else
                    {
                        Vector3 away = Vector3.ProjectOnPlane(a.transform.position - b.transform.position, Vector3.up).normalized;
                        a.Translate(away * Mathf.Min(.04f, correction.magnitude));
                    }
                }
                else b.Translate(Vector3.ClampMagnitude(correction, .08f));
            }
            RequiredSeparation(Vector3.up, out float residual);
            separationRemaining = residual; MaximumResidual = Mathf.Max(MaximumResidual, residual);
            return contact;
        }
        // 先检查整只甲虫包围盒，再挑出可能相交的部件，最后对部件对进行精确凸体检测。
        // 先用 AABB 排除远离部件，仅精确检测可能接触的代理对。
        float RequiredSeparation(Vector3 axis, out float maximumDepth, bool describe = false)
        {
            float result = 0; maximumDepth = 0;
            var a = rigs[0]; var b = rigs[1];
            if (!a.worldBounds.Intersects(b.worldBounds)) return 0;
            int countA = 0, countB = 0;
            for (int i = 0; i < a.shapes.Length; i++) if (a.bounds[i].Intersects(b.worldBounds)) candidatesA[countA++] = i;
            for (int i = 0; i < b.shapes.Length; i++) if (b.bounds[i].Intersects(a.worldBounds)) candidatesB[countB++] = i;
            for (int ai = 0; ai < countA; ai++)
            for (int bi = 0; bi < countB; bi++)
            {
                int i = candidatesA[ai], j = candidatesB[bi];
                if (!a.bounds[i].Intersects(b.bounds[j])) continue;
                var ca = a.shapes[i]; var cb = b.shapes[j];
                // ComputePenetration 只返回分离法线与深度，不会自动移动碰撞体。
                if (!Physics.ComputePenetration(ca, ca.transform.position, ca.transform.rotation,
                    cb, cb.transform.position, cb.transform.rotation, out Vector3 normal, out float depth)) continue;
                if(describe && depth > maximumDepth) { LastSeparationPair = ca.name + " / " + cb.name; LastSeparationNormal = normal; }
                maximumDepth = Mathf.Max(maximumDepth, depth);
                // 检测深度沿法线给出，而本求解器沿水平中心轴移动，因此除以法线与轴的夹角余弦。
                // 分母下限 0.15 避免接近垂直时数值爆炸；后续迭代逐步消除余下重叠。
                result = Mathf.Max(result, depth / Mathf.Max(.15f, Mathf.Abs(Vector3.Dot(normal, axis))));
            }
            return result;
        }
        // 每轮找部件与树桩/地面最深的重叠，沿其法线移动整只甲虫，最多 16 次。
        // 用于防止关节动作插入台面；平移后由 ContactRig 同步平移缓存包围盒。
        bool ResolveArena(BeetleContactRig rig)
        {
            if (arena == null) return false;
            bool moved = false;
            for (int iteration = 0; iteration < 16; iteration++)
            {
                Vector3 correction = Vector3.zero; float deepest = 0;
                foreach (var surface in arena)
                for (int i = 0; i < rig.shapes.Length; i++)
                {
                    if (!surface || !surface.enabled || !surface.gameObject.activeInHierarchy || !rig.bounds[i].Intersects(surface.bounds)) continue;
                    var c = rig.shapes[i];
                    if (Physics.ComputePenetration(c, c.transform.position, c.transform.rotation,
                        surface, surface.transform.position, surface.transform.rotation, out Vector3 normal, out float depth) && depth > deepest)
                    {
                        deepest = depth;
                        correction = normal * (depth + skin);
                        // 凹凸地表顶部采用竖直脱离：侧向分离会把细触角推入邻接三角形，反复抵消修正。
                        // 除以法线 Y 保持法线方向的分离距离；侧壁仍沿原始法线推出，不能借此爬回台顶。
                        if (surface is MeshCollider mesh && !mesh.convex && normal.y > .5f)
                            correction = Vector3.up * ((depth + skin) / normal.y);
                    }
                }
                if (deepest < .0005f) break;
                rig.Translate(correction);
                moved = true;
            }
            return moved;
        }
        // 验证工具用的当前残余深度测量：包含甲虫之间以及每只甲虫与环境之间的接触。
        // 会更新 LastWorstContact 帮助定位问题，但不是持续执行的性能分析器。
        public float MeasurePenetration(bool describe = true)
        {
            Physics.SyncTransforms(); Cache();
            RequiredSeparation(Vector3.right, out float depth, describe);
            // 游戏循环只测数值；详细部件名按验证工具请求生成，避免每次接触复查分配诊断字符串。
            if (describe) LastWorstContact = "between beetles";
            foreach (var rig in rigs)
            foreach (var surface in arena)
            for (int i = 0; i < rig.shapes.Length; i++)
            {
                if (!surface || !surface.enabled || !surface.gameObject.activeInHierarchy || !rig.bounds[i].Intersects(surface.bounds)) continue;
                var shape = rig.shapes[i];
                if (Physics.ComputePenetration(shape, shape.transform.position, shape.transform.rotation,
                    surface, surface.transform.position, surface.transform.rotation, out _, out float distance) && distance > depth)
                { depth = distance; if (describe) LastWorstContact = shape.name + " against " + surface.name; }
            }
            return depth;
        }
    }
}

