using UnityEngine;

namespace BeetleArena
{
    // Defender lift response: Simulation owns timing, contact solver owns the two local contact points, and animator only consumes state.
    // Approximates cephalic horn leverage lift and traction loss without assuming artificial sticky forces at the horn tip.
    // Reference: Weber et al. 2023, https://doi.org/10.1016/j.cub.2023.08.066
    public sealed class BeetleLiftResponse
    {
        public int Supporter = -1;
        public float Time, RaiseSeconds, HoldSeconds;
        public float Pitch, Roll, UnsupportedTime, TwistSign;
        // Duration for which contact solver has failed to advance. Used to release stuck lifts rather than forcing elevation via position corrections.
        public float BlockedTime;
        // Indicates whether explosive force was triggered this step; standard successful lifts still throw without being gated by this flag.
        public bool BurstEffort;
        public float Effort, SnapSeconds, SweepTarget, StrokeProgress;
        // The contact layer only writes the following measurements once the final fixed-step pose is accepted; display interpolation does not accumulate.
        public float ContactSeconds, LostContactSeconds, HeadSweep, AngularSpeed, PeakAngularSpeed;
        public float LeverArm, OutputForce, Work, Impulse;
        public Vector3 ReleaseTangent, LaunchVelocity;
        public float LaunchElevation;
        public BeetleThrowKind ThrowKind;
        public float SnapStart => RaiseSeconds + HoldSeconds;
        public float StrokeDuration => SnapStart + SnapSeconds;
        public void ResetMechanics()
        {
            ContactSeconds = LostContactSeconds = HeadSweep = AngularSpeed = PeakAngularSpeed = 0;
            LeverArm = OutputForce = Work = Impulse = StrokeProgress = LaunchElevation = 0;
            ReleaseTangent = LaunchVelocity = Vector3.zero; ThrowKind = BeetleThrowKind.None;
        }
        // Latches when all six tarsi lose support simultaneously; airborne flailing that grazes the surface does not count as regrowing grip.
        public bool GripReleased;
        public Vector2 Direction;
        public bool Supported => Supporter >= 0;
        public float RaiseProgress => Mathf.Clamp01(Time / Mathf.Max(.05f, RaiseSeconds));
    }

    // A lift can only be established through physical scene contact. Non-contact or leg-to-leg clashes must not degenerate into generic upward impulses.
    public interface IBeetleLiftContacts
    {
        bool TryBeginLift(BeetleSimulation simulation, BeetleState attacker, BeetleState defender);
    }
}
