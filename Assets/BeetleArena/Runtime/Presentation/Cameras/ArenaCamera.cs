using UnityEngine;

namespace BeetleArena
{
    [RequireComponent(typeof(Camera))]
    // Orthographic observation camera: subtly tracks fighter midpoint, scales with radial spread from arena center, preserving global ring framing.
    public sealed class ArenaCamera : MonoBehaviour
    {
        // Fixed world offset from look target, defining viewing angle and perspective direction.
        [SerializeField] public Vector3 viewOffset = new Vector3(9, 12, -13);
        // Baseline orthographic half-size; larger values yield a wider field of view.
        [SerializeField, Range(4, 12)] public float overviewSize = 6.25f;
        [Header("Oblique follow framing (60 degrees above ground)")]
        [SerializeField, Min(4)] public float followHeight = 16;
        [SerializeField, Range(2, 8)] public float followSize = 4.2f;
        [SerializeField, Range(.4f, .65f)] public float followViewportHeight = .56f;
        Camera cameraComponent;
        // Smoothed focus center decoupled from beetle root nodes, dampening combat jitter before it reaches camera.
        Vector3 focus;
        float viewDistance, viewportOffset;
        Quaternion viewRotation;
        public Vector3 Focus => focus;
        public int ViewMode { get; private set; }
        public string ViewName => ViewMode == 0 ? "全景" : ViewMode == 1 ? "琥珀上方" : "墨甲上方";
        public int DefeatFocus { get; set; } = -1;
        public void CycleView()
        {
            if (!cameraComponent) ResetView();
            ViewMode = (ViewMode + 1) % 3;
        }
        // Resets to arena center and initial orthographic size upon match transitions, preventing inheriting post-ring-out camera states.
        public void ResetView()
        {
            cameraComponent = GetComponent<Camera>();
            ViewMode = 0;
            DefeatFocus = -1;
            focus = new Vector3(0, .2f, 0);
            cameraComponent.orthographic = true;
            cameraComponent.orthographicSize = overviewSize;
            viewDistance = viewOffset.magnitude;
            viewportOffset = 0;
            viewRotation = Quaternion.LookRotation(-viewOffset, Vector3.up);
            transform.SetPositionAndRotation(focus + viewOffset, viewRotation);
        }
        // Invoked explicitly by controller per frame; zero follow smoothing when dt=0 accommodates local pausing.
        public void Tick(BeetleSimulation simulation, float dt)
        {
            if (simulation == null) return;
            Tick(BeetleCameraFrame.Capture(simulation), dt);
        }

        public void Tick(in BeetleCameraFrame frame, float dt)
        {
            // Zero or invalid dt bypasses camera mutation, keeping camera perfectly stationary during pause and replay validation.
            if (!float.IsFinite(dt) || dt <= 0) return;
            if (!cameraComponent) ResetView();
            var a = frame.First; var b = frame.Second;
            // Clamps focus target within 1.4 units of arena center, preventing camera from chasing fallen fighters entirely off-screen.
            Vector2 aPosition = new Vector2(a.Center.x, a.Center.z), bPosition = new Vector2(b.Center.x, b.Center.z);
            Vector2 middle = Vector2.ClampMagnitude((aPosition + bPosition) * .5f, 1.4f);
            Vector3 target = new Vector3(middle.x, .2f, middle.y);
            if (frame.Resolving) target = new Vector3(0, .2f, 0);
            // Adjusts framing based on peak distance of either fighter from arena center (distinct from inter-fighter distance).
            float spread = Mathf.Max(aPosition.magnitude, bPosition.magnitude);
            // Pushes slightly closer when centered, zooms out to panoramic framing near edges; exponential smoothing prevents pops.
            float zoom = Mathf.Lerp(overviewSize - .75f, overviewSize, Mathf.InverseLerp(1.4f, 4.5f, spread));
            Vector3 offset = viewOffset;
            int defeatIndex = DefeatFocus >= 0 && DefeatFocus < 2 ? DefeatFocus : -1;
            bool following = ViewMode > 0 || defeatIndex >= 0;
            bool subjectFollow = ViewMode > 0 && defeatIndex < 0;
            bool framingThrow = ViewMode == 0 && defeatIndex < 0 && (a.Airborne || b.Airborne) && !a.Fallen && !b.Fallen;
            if (framingThrow)
                target = (a.Center + b.Center) * .5f;
            if (following)
            {
                var followed = frame.Subject(defeatIndex >= 0 ? defeatIndex : ViewMode - 1);
                target = followed.Center;
                float height = Mathf.Max(4, followHeight);
                // Follow behind the displayed heading at 60 degrees to the horizontal ground plane.
                // The root supplies yaw, so lifting/tumbling the torso does not roll the camera.
                offset = defeatIndex >= 0 ? new Vector3(5, 9, -7) :
                    Vector3.up * height - followed.Forward * (height / Mathf.Tan(60 * Mathf.Deg2Rad));
                zoom = defeatIndex >= 0 ? 2.65f : Mathf.Max(followSize, followed.Radius / Mathf.Max(.01f, cameraComponent.aspect));
            }
            focus = Vector3.Lerp(focus, target, 1 - Mathf.Exp(-dt * (following || framingThrow ? 8 : 1.5f)));
            cameraComponent.orthographicSize = Mathf.Lerp(cameraComponent.orthographicSize, zoom, 1 - Mathf.Exp(-dt));
            viewDistance = Mathf.Lerp(viewDistance, offset.magnitude, 1 - Mathf.Exp(-dt * 8));
            // Preserves world up-vector; single-beetle tracking orbits smoothly behind heading without inheriting thoracic roll.
            Quaternion rotation = Quaternion.LookRotation(-offset, Vector3.up);
            viewRotation = Quaternion.Slerp(viewRotation, rotation, 1 - Mathf.Exp(-dt * 8));
            // Smooths focus once; position and rotation share a single view transition, framing subject slightly above screen center.
            float targetViewportOffset = subjectFollow ? followViewportHeight - .5f : 0;
            viewportOffset = Mathf.Lerp(viewportOffset, targetViewportOffset, 1 - Mathf.Exp(-dt * 8));
            // Derives position from orientation to prevent position Lerp and rotation Slerp from tracing divergent arcs in view space.
            transform.SetPositionAndRotation(focus - (viewRotation * Vector3.forward) * viewDistance -
                (viewRotation * Vector3.up) * (2 * cameraComponent.orthographicSize * viewportOffset), viewRotation);
            // Heavy throws can rise above the old fixed overview. Fit both complete bodies
            // in camera space immediately; ease back in through the normal zoom next frame.
            if (framingThrow)
            {
                for (int i = 0; i < 2; i++)
                    FitSubject(frame.Subject(i));
            }
            else if (following)
                FitSubject(frame.Subject(defeatIndex >= 0 ? defeatIndex : ViewMode - 1));
        }

        // Fast ballistic throws outpace smoothed focus; widens framing immediately when necessary so tracked subject never leaves screen.
        void FitSubject(in BeetleCameraSubject subject)
        {
            Vector3 local = transform.InverseTransformPoint(subject.Center);
            float required = Mathf.Max(Mathf.Abs(local.y) + subject.Radius,
                (Mathf.Abs(local.x) + subject.Radius) / Mathf.Max(.01f, cameraComponent.aspect));
            cameraComponent.orthographicSize = Mathf.Max(cameraComponent.orthographicSize, required);
        }
    }
}
