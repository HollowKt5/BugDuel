using UnityEngine;

namespace BeetleArena
{
    /// <summary>镜头只读的表现快照。跟随优先读取已插值的身体位置，不追逐领先显示一帧的模拟根节点。</summary>
    public readonly struct BeetleCameraSubject
    {
        public readonly Vector3 Center;
        // Horizontal heading of the displayed root; body pitch/roll must not overturn the camera.
        public readonly Vector3 Forward;
        public readonly float Radius;
        public readonly bool Airborne, Fallen;
        public BeetleCameraSubject(Vector3 center, float radius, bool airborne, bool fallen)
            : this(center, radius, airborne, fallen, Vector3.forward) { }
        public BeetleCameraSubject(Vector3 center, float radius, bool airborne, bool fallen, Vector3 forward)
        { Center = center; Radius = radius; Airborne = airborne; Fallen = fallen; Forward = forward; }
    }

    public readonly struct BeetleCameraFrame
    {
        public readonly BeetleCameraSubject First, Second;
        public readonly bool Resolving;
        public BeetleCameraFrame(BeetleCameraSubject first, BeetleCameraSubject second, bool resolving)
        { First = first; Second = second; Resolving = resolving; }
        public BeetleCameraSubject Subject(int index) => index == 0 ? First : Second;

        public static BeetleCameraFrame Capture(BeetleSimulation simulation, BeetleActor[] actors = null) =>
            new BeetleCameraFrame(CaptureSubject(simulation, 0, actors), CaptureSubject(simulation, 1, actors),
                simulation.Phase == MatchPhase.Finished || simulation.Phase == MatchPhase.Resolving);

        static BeetleCameraSubject CaptureSubject(BeetleSimulation sim, int index, BeetleActor[] actors)
        {
            var state = sim.Fighters[index];
            Vector3 center = new Vector3(state.Position.x, state.Height + .85f * state.Traits.size, state.Position.y);
            Vector3 forward = new Vector3(state.Forward.x, 0, state.Forward.y);
            if (actors != null && index < actors.Length && actors[index] && actors[index].State == state)
            {
                var displayedForward = Vector3.ProjectOnPlane(actors[index].transform.forward, Vector3.up);
                if (displayedForward.sqrMagnitude > .000001f)
                    forward = displayedForward.normalized;
                var animator = actors[index].animator;
                if (animator && animator.bodyPivot)
                    center = animator.bodyPivot.position;
            }
            return new BeetleCameraSubject(center, 2.1f * state.Traits.size, state.Airborne, state.HasFallen, forward);
        }
    }
}
