using UnityEngine;

namespace BeetleArena
{
    [RequireComponent(typeof(Camera))]
    // 正交观察镜头：轻微跟随双方中点、按距台中心的展开程度缩放，保留擂台全局视野。
    public sealed class ArenaCamera : MonoBehaviour
    {
        // 镜头相对观察中心的固定世界偏移，决定俯视角度与观察方向。
        [SerializeField] public Vector3 viewOffset = new Vector3(9, 12, -13);
        // 正交视野的半高度基准；数值越大看到的范围越宽。
        [SerializeField, Range(4, 12)] public float overviewSize = 6.25f;
        [Header("Overhead follow framing")]
        [SerializeField, Min(4)] public float followHeight = 16;
        [SerializeField, Range(2, 8)] public float followSize = 4.2f;
        [SerializeField, Range(.4f, .65f)] public float followViewportHeight = .56f;
        Camera cameraComponent;
        // 平滑后的观察中心，独立于甲虫根节点，减少交锋抖动传到镜头。
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
        // 换局时恢复台面中心和初始正交尺寸，避免沿用上一局落台后的镜头状态。
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
        // 每帧由控制器显式调用；dt=0 时跟随平滑量为零，适应本地暂停。
        public void Tick(BeetleSimulation simulation, float dt)
        {
            if (simulation == null) return;
            Tick(BeetleCameraFrame.Capture(simulation), dt);
        }

        public void Tick(in BeetleCameraFrame frame, float dt)
        {
            // 零时间与无效时间不初始化/改变镜头；暂停和回放检查保持完全静止。
            if (!float.IsFinite(dt) || dt <= 0) return;
            if (!cameraComponent) ResetView();
            var a = frame.First; var b = frame.Second;
            // 限制跟随中心最多偏离台中心 1.4 单位，避免某只落台后镜头追出整个擂台。
            Vector2 aPosition = new Vector2(a.Center.x, a.Center.z), bPosition = new Vector2(b.Center.x, b.Center.z);
            Vector2 middle = Vector2.ClampMagnitude((aPosition + bPosition) * .5f, 1.4f);
            Vector3 target = new Vector3(middle.x, .2f, middle.y);
            if (frame.Resolving) target = new Vector3(0, .2f, 0);
            // 依据选手离台中心最远距离调整取景；这不是两只之间的直接距离。
            float spread = Mathf.Max(aPosition.magnitude, bPosition.magnitude);
            // 集中在中央时稍微推近，靠近边沿时退回全景；指数平滑避免跳变。
            float zoom = Mathf.Lerp(overviewSize - .75f, overviewSize, Mathf.InverseLerp(1.4f, 4.5f, spread));
            Vector3 offset = viewOffset;
            int defeatIndex = DefeatFocus >= 0 && DefeatFocus < 2 ? DefeatFocus : -1;
            bool following = ViewMode > 0 || defeatIndex >= 0;
            bool overhead = ViewMode > 0 && defeatIndex < 0;
            bool framingThrow = ViewMode == 0 && defeatIndex < 0 && (a.Airborne || b.Airborne) && !a.Fallen && !b.Fallen;
            if (framingThrow)
                target = (a.Center + b.Center) * .5f;
            if (following)
            {
                var followed = frame.Subject(defeatIndex >= 0 ? defeatIndex : ViewMode - 1);
                target = followed.Center;
                offset = defeatIndex >= 0 ? new Vector3(5, 9, -7) : Vector3.up * Mathf.Max(4, followHeight);
                zoom = defeatIndex >= 0 ? 2.65f : Mathf.Max(followSize, followed.Radius / Mathf.Max(.01f, cameraComponent.aspect));
            }
            focus = Vector3.Lerp(focus, target, 1 - Mathf.Exp(-dt * (following || framingThrow ? 8 : 1.5f)));
            cameraComponent.orthographicSize = Mathf.Lerp(cameraComponent.orthographicSize, zoom, 1 - Mathf.Exp(-dt));
            viewDistance = Mathf.Lerp(viewDistance, offset.magnitude, 1 - Mathf.Exp(-dt * 8));
            // 正上方使用明确的朝向/屏幕上方向；LookAt(worldUp) 在竖直视线处退化，会突然翻转或旋转。
            Quaternion rotation = overhead ? Quaternion.Euler(90, 0, 0) : Quaternion.LookRotation(-offset, Vector3.up);
            viewRotation = Quaternion.Slerp(viewRotation, rotation, 1 - Mathf.Exp(-dt * 8));
            // 焦点仅平滑一次，位置与旋转共享同一视图过渡；构图将主体放在画面略高于中心处。
            float targetViewportOffset = overhead ? followViewportHeight - .5f : 0;
            viewportOffset = Mathf.Lerp(viewportOffset, targetViewportOffset, 1 - Mathf.Exp(-dt * 8));
            // 由朝向推导位置，避免位置Lerp与旋转Slerp沿不同轨迹导致焦点在画面里画弧。
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

        // 快速抛飞时平滑焦点会落后，必要时立即放宽视野，不能让所跟随的主体飞出画面。
        void FitSubject(in BeetleCameraSubject subject)
        {
            Vector3 local = transform.InverseTransformPoint(subject.Center);
            float required = Mathf.Max(Mathf.Abs(local.y) + subject.Radius,
                (Mathf.Abs(local.x) + subject.Radius) / Mathf.Max(.01f, cameraComponent.aspect));
            cameraComponent.orthographicSize = Mathf.Max(cameraComponent.orthographicSize, required);
        }
    }
}
