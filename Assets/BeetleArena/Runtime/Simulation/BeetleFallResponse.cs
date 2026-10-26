using UnityEngine;

namespace BeetleArena
{
    /// <summary>
    /// Motion feedback after falling off the ring. HasFallen denotes disqualification only, not death or motion freeze.
    /// Simulation advances an independent clock, ContactSolver confirms out-of-ring ground contact, and Animator only consumes this data.
    /// Does not re-enter combat from this module, nor mutate resolved match outcome.
    /// </summary>
    public sealed class BeetleFallResponse
    {
        public bool Active { get; private set; }

        public bool OnFloor { get; private set; }

        public float Time { get; private set; }

        public float UnsupportedSeconds { get; private set; }

        public float GroundSeconds { get; private set; }

        public float ImpactSpeed { get; private set; }

        public float ForwardSpeed { get; private set; }

        public float SideSpeed { get; private set; }

        public bool EdgeDrop { get; private set; }

        public Vector2 EdgeDirection { get; private set; }

        public float EdgeSpeed { get; private set; }

        public void BeginEdgeDrop(BeetleState state)
        {
            Begin(state);
            if (EdgeDrop)
                return;
            EdgeDrop = true;
            EdgeSpeed = state.Velocity.magnitude;
            EdgeDirection = EdgeSpeed > .05f ? state.Velocity.normalized : state.Position.normalized;
        }

        public void Begin(BeetleState state)
        {
            if (Active)
                return;
            Active = true;
            UnsupportedSeconds = state.Lift.UnsupportedTime;
            ForwardSpeed = Vector2.Dot(state.Velocity, state.Forward);
            SideSpeed = Vector2.Dot(state.Velocity, new Vector2(state.Forward.y, -state.Forward.x));
        }

        public void Tick(float dt)
        {
            if (!Active)
                return;
            Time += dt;
            if (OnFloor)
                GroundSeconds += dt;
            else
                UnsupportedSeconds += dt;
        }

        // Must be invoked on the final accepted collision pose; rolled-back candidate trials must never be treated as landing.
        public void SetFloorContact(bool supported, float downwardSpeed)
        {
            if (!Active)
                return;
            if (supported && !OnFloor)
            {
                ImpactSpeed = Mathf.Max(0, downwardSpeed);
                GroundSeconds = 0;
            }

            if (!supported)
                GroundSeconds = 0;
            OnFloor = supported;
        }

        public void Reset()
        {
            Active = OnFloor = false;
            EdgeDrop = false;
            EdgeDirection = Vector2.zero;
            EdgeSpeed = 0;
            Time = UnsupportedSeconds = GroundSeconds = ImpactSpeed = ForwardSpeed = SideSpeed = 0;
        }
    }
}
