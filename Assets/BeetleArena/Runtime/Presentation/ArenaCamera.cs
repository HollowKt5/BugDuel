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
        Camera cameraComponent;
        // 平滑后的观察中心，独立于甲虫根节点，减少交锋抖动传到镜头。
        Vector3 focus;
        // 换局时恢复台面中心和初始正交尺寸，避免沿用上一局落台后的镜头状态。
        public void ResetView()
        {
            cameraComponent = GetComponent<Camera>();
            focus = new Vector3(0, .2f, 0);
            cameraComponent.orthographic = true;
            cameraComponent.orthographicSize = overviewSize;
            transform.position = focus + viewOffset;
            transform.LookAt(focus);
        }
        // 每帧由控制器显式调用；dt=0 时跟随平滑量为零，适应本地暂停。
        public void Tick(BeetleSimulation simulation, float dt)
        {
            if (!cameraComponent) ResetView();
            var a = simulation.Fighters[0]; var b = simulation.Fighters[1];
            // 限制跟随中心最多偏离台中心 1.4 单位，避免某只落台后镜头追出整个擂台。
            Vector2 middle = Vector2.ClampMagnitude((a.Position + b.Position) * .5f, 1.4f);
            Vector3 target = new Vector3(middle.x, .2f, middle.y);
            if (simulation.Phase == MatchPhase.Finished || simulation.Phase == MatchPhase.Resolving) target = new Vector3(0, -.2f, 0);
            focus = Vector3.Lerp(focus, target, 1 - Mathf.Exp(-dt * 1.5f));
            // 依据选手离台中心最远距离调整取景；这不是两只之间的直接距离。
            float spread = Mathf.Max(a.Position.magnitude, b.Position.magnitude);
            // 集中在中央时稍微推近，靠近边沿时退回全景；指数平滑避免跳变。
            float zoom = Mathf.Lerp(overviewSize - .75f, overviewSize, Mathf.InverseLerp(1.4f, 4.5f, spread));
            cameraComponent.orthographicSize = Mathf.Lerp(cameraComponent.orthographicSize, zoom, 1 - Mathf.Exp(-dt));
            transform.position = focus + viewOffset;
            transform.LookAt(focus);
        }
    }
}
