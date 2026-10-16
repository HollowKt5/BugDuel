using UnityEngine;

namespace BeetleArena
{
    // 数据与场景对象之间的适配层：保存前一步根节点状态、插值显示、调用程序动画。
    public sealed class BeetleActor : MonoBehaviour
    {
        [SerializeField] public BeetleProceduralAnimator animator;
        [SerializeField] public Transform teamMarker;
        [Tooltip("模型适配版本，必须匹配参赛快照的 RigId；新模型不能只改物种名称复用旧骨架标定。")]
        public string rigId = BeetleCombatProfile.LegacyRigId;
        public BeetleState State { get; private set; }
        // 由接触求解器开启；开启后只在固定模拟的 PoseSimulation 中推进关节姿态。
        public bool ContactDriven { get; set; }
        // 上一模拟步之前的世界位置和朝向，用于本显示帧插值。
        Vector3 previous;
        Quaternion previousRotation, currentRotation;
        IBeetleGround ground;
        BeetlePoseBuffer poses;
        internal Transform[] PoseBones => poses.Bones;
        BeetleGroundMarker marker;

        // 每局将新状态绑定到场景模型，设置初始根节点位置、方向、体型和阵营外壳颜色。
        public void Bind(BeetleState state, BeetleMatchSettings settings, IBeetleGround surface = null)
        {
            State = state;
            ground = surface;
            previous = WorldPosition(state);
            previousRotation = currentRotation = GroundRotation(state);
            transform.SetPositionAndRotation(previous, previousRotation);
            // 卡通材质使用较清楚的琥珀/青绿色分队；仅改变显示颜色，不参与体型或战斗计算。
            animator.Initialize(state.Traits, settings, state.Index == 0 ? new Color(.72f, .30f, .085f) : new Color(.10f, .37f, .40f), surface, state.VisualScale(settings));
            animator.Pose(state, 0, 0);
            poses = new BeetlePoseBuffer(animator.model);
            marker?.Dispose(); marker = null;
            // 临时网格仅在 Play 生命周期内创建，场景生成/保存不会把临时 Mesh 写进资产。
            if (Application.isPlaying && teamMarker) marker = new BeetleGroundMarker(teamMarker, ground, state.Traits.size);
        }
        // 每次固定模拟前保存旧位置，而非每个显示帧保存；否则插值端点会混乱。
        public void BeforeStep()
        {
            poses?.BeginStep();
            previous = WorldPosition(State); previousRotation = currentRotation;
            // 接触捕获发生在 PoseSimulation 之前，必须连根节点一起恢复。
            // 否则屏幕刷新率/上次 alpha 会影响角尖接触位置，进而改变托举结果。
            transform.SetPositionAndRotation(previous, currentRotation);
        }
        // 根节点和全部关节使用同一个 alpha，消除固定步关节搭配可视帧根节点造成的跳动。
        public void Render(float alpha, float time, float dt)
        {
            if (State == null) return;
            transform.SetPositionAndRotation(Vector3.Lerp(previous, WorldPosition(State), alpha), Quaternion.Slerp(previousRotation, currentRotation, alpha));
            if (!ContactDriven) animator.Pose(State, time, dt);
            else poses?.Render(alpha);
            if (!ContactDriven) UpdateMarker();
        }

        // 由接触求解器在最终显示位置确定后调用，标记不会滞留在碰撞修正前的位置。
        public void UpdateMarker()
        {
            if (teamMarker && State != null)
            {
                teamMarker.gameObject.SetActive(!State.HasFallen && !State.Airborne);
                if (marker != null)
                {
                    marker.Update(new Vector2(transform.position.x, transform.position.z), !State.HasFallen && !State.Airborne);
                    return;
                }
                bool supported = ground != null && ground.Sample(State.Position, out _);
                // 倾斜圆盘在弯曲台面会切入表面；由地形提供高度/法线，在缓坡上略抬高标识。
                teamMarker.position = new Vector3(transform.position.x, (supported ? State.SurfaceHeight : 0) + .035f, transform.position.z);
                teamMarker.rotation = Quaternion.FromToRotation(Vector3.up, State.SurfaceNormal);
            }
        }
        public void CaptureSimulationPose()
        { currentRotation = transform.rotation; poses?.Capture(); }
#if UNITY_EDITOR
        public void EditorRefreshMarker()
        {
            marker?.Dispose();marker=null;
            if(Application.isPlaying && teamMarker && State!=null)marker=new BeetleGroundMarker(teamMarker,ground,State.Traits.size);
            UpdateMarker();
        }
#endif
        void OnDestroy() { marker?.Dispose(); marker = null; }
        // 将根节点恢复到权威模拟状态，再生成本步关节姿态，供接触检测使用。
        public void PoseSimulation(float time, float dt)
        {
            transform.SetPositionAndRotation(WorldPosition(State), GroundRotation(State));
            animator.Pose(State, time, dt);
        }
        // 坐标映射：模拟二维 Position=(世界X, 世界Z)，独立 Height 对应世界Y。
        static Vector3 WorldPosition(BeetleState b) => new Vector3(b.Position.x, b.Height, b.Position.y);
        // 坡面倾斜与偏航组合，战斗时额外的俯仰/侧倾仍在 bodyPivot 上叠加。
        static Quaternion GroundRotation(BeetleState b)
        {
            // 离开起伏擂台后平滑释放旧坡面倾角，不能在森林平地上永久保留台面的斜法线。
            Vector3 normal = b.Fall.Active
                ? Vector3.Slerp(b.SurfaceNormal, Vector3.up, 1 - Mathf.Exp(-b.Fall.Time * 4)) : b.SurfaceNormal;
            return Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0, b.Heading, 0);
        }
    }
}
