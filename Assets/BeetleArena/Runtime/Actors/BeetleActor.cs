using UnityEngine;

namespace BeetleArena
{
    // Adapter between simulation data and scene GameObjects: buffers previous root state, interpolates presentation, invokes procedural animation.
    public sealed class BeetleActor : MonoBehaviour
    {
        [SerializeField] public BeetleProceduralAnimator animator;
        [SerializeField] public Transform teamMarker;
        [Tooltip("模型适配版本，必须匹配参赛快照的 RigId；新模型不能只改物种名称复用旧骨架标定。")]
        public string rigId = BeetleCombatProfile.LegacyRigId;
        public BeetleState State { get; private set; }
        // Enabled by contact solver; once enabled, joint poses advance strictly during fixed-step PoseSimulation.
        public bool ContactDriven { get; set; }
        // World position and heading prior to previous simulation step, used for display frame interpolation.
        Vector3 previous;
        Quaternion previousRotation, currentRotation;
        IBeetleGround ground;
        BeetlePoseBuffer poses;
        internal Transform[] PoseBones => poses.Bones;
        BeetleGroundMarker marker;

        // Binds new state to scene model per match, initializing root position, heading, scale, and faction shell color.
        public void Bind(BeetleState state, BeetleMatchSettings settings, IBeetleGround surface = null)
        {
            State = state;
            ground = surface;
            previous = WorldPosition(state);
            previousRotation = currentRotation = GroundRotation(state);
            transform.SetPositionAndRotation(previous, previousRotation);
            // Toon material uses legible amber/cyan team tints; alters display color only, free of trait or combat simulation effects.
            animator.Initialize(state.Traits, settings, state.Index == 0 ? new Color(.72f, .30f, .085f) : new Color(.10f, .37f, .40f), surface, state.VisualScale(settings));
            animator.Pose(state, 0, 0);
            poses = new BeetlePoseBuffer(animator.model);
            marker?.Dispose(); marker = null;
            // Procedural temporary meshes are created within Play lifecycle only; scene generation/saving never writes temporary meshes to assets.
            if (Application.isPlaying && teamMarker) marker = new BeetleGroundMarker(teamMarker, ground, state.Traits.size);
        }
        // Caches prior position before each fixed simulation step rather than per display frame; prevents corrupting interpolation endpoints.
        public void BeforeStep()
        {
            poses?.BeginStep();
            previous = WorldPosition(State); previousRotation = currentRotation;
            // Contact acquisition occurs prior to PoseSimulation; must be restored together with root node.
            // Otherwise monitor refresh rate / prior alpha alters horn tip contact position, biasing lift outcomes.
            transform.SetPositionAndRotation(previous, currentRotation);
        }
        // Root node and all joints share the same alpha, eliminating visual jitter caused by pairing fixed-step joints with display-frame root.
        public void Render(float alpha, float time, float dt)
        {
            if (State == null) return;
            transform.SetPositionAndRotation(Vector3.Lerp(previous, WorldPosition(State), alpha), Quaternion.Slerp(previousRotation, currentRotation, alpha));
            if (!ContactDriven) animator.Pose(State, time, dt);
            else poses?.Render(alpha);
            if (!ContactDriven) UpdateMarker();
        }

        // Invoked by contact solver once final display position is determined, preventing ground markers from lagging at uncorrected positions.
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
                // Tilted discs clip into curved arena surfaces; terrain supplies height/normal, slightly elevating marker on gentle slopes.
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
        // Restores root node to authoritative simulation state, then synthesizes current joint pose for contact queries.
        public void PoseSimulation(float time, float dt)
        {
            transform.SetPositionAndRotation(WorldPosition(State), GroundRotation(State));
            animator.Pose(State, time, dt);
        }
        // Coordinate mapping: 2D simulation Position = (World X, World Z); independent Height maps to World Y.
        static Vector3 WorldPosition(BeetleState b) => new Vector3(b.Position.x, b.Height, b.Position.y);
        // Combines terrain slope tilt with yaw heading; combat pitch and roll remain additively applied to bodyPivot.
        static Quaternion GroundRotation(BeetleState b)
        {
            // Smoothly unloads terrain inclination upon departing arena, preventing retaining arena surface normals on flat forest floor.
            Vector3 normal = b.Fall.Active
                ? Vector3.Slerp(b.SurfaceNormal, Vector3.up, 1 - Mathf.Exp(-b.Fall.Time * 4)) : b.SurfaceNormal;
            return Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0, b.Heading, 0);
        }
    }
}
