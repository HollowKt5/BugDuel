using UnityEngine;

namespace BeetleArena
{
    // Preserves existing enum values; tier progression is mapped via TierNumber independent of underlying enum integers.
    public enum BeetleThrowKind { None, ShortPry, ArcThrow, Overhead, QuickPry, HighArc, PowerOverhead }

    /// <summary>
    /// Pure computational layer for leverage-based prying and throwing. The contact layer supplies accepted head pitch angle, moment arm, and tangential direction;
    /// Integrates work and impulse only during valid contact accompanied by actual head elevation; stationary pushing cannot charge infinitely.
    /// τ = F_in * l_in, F_out = τ / l_out, W = ∫τ dθ, J = ∫F_out dt.
    /// Uses normalized units and efficiency caps rather than empirical biomechanical fittings.
    /// Leverage foundation: Weber et al. 2023, doi:10.1016/j.cub.2023.08.066.
    /// </summary>
    public static class BeetleThrowMechanics
    {
        // Only amplifies distance throws that would naturally tumble. Evaluates initial launch conditions first to prevent acceleration from expanding trigger boundaries.
        public const float TumblingLaunchSpeedMultiplier = 3f;
        public const float MinimumTumbleRange = 4f;

        public static bool ShouldTumble(BeetleMatchSettings settings, Vector3 launchVelocity)
        {
            float horizontalSpeed = new Vector2(launchVelocity.x, launchVelocity.z).magnitude;
            float estimatedRange = horizontalSpeed * 2 * Mathf.Max(0, launchVelocity.y) / Mathf.Max(1, settings.gravity);
            return estimatedRange >= MinimumTumbleRange && launchVelocity.magnitude >= settings.throwHighArcSpeed;
        }

        public static void Accumulate(BeetleMatchSettings settings, BeetleState attacker,
            BeetleState defender, float dt, float raisedRadians, float arm,
            Vector3 tangent, float contactQuality)
        {
            if (dt <= 0 || !defender.Lift.Supported)
                return;
            var lift = defender.Lift;
            float quality = Mathf.Clamp01(contactQuality);
            lift.ContactSeconds += dt * quality;
            lift.LostContactSeconds = quality > .15f ? 0 : lift.LostContactSeconds + dt;
            lift.LeverArm = Mathf.Max(.12f, arm);
            float delta = Mathf.Clamp(raisedRadians, 0, .35f);
            lift.AngularSpeed = Mathf.Lerp(lift.AngularSpeed, delta / dt, 1 - Mathf.Exp(-dt * 30));
            lift.HeadSweep += delta * Mathf.Rad2Deg;
            lift.PeakAngularSpeed = Mathf.Max(lift.PeakAngularSpeed, lift.AngularSpeed);
            if (quality <= 0 || delta <= .00001f)
                return;

            // Sustained lifting also depletes burst reserves under the same force exertion rules as engagement and driving push, adhering strictly to stamina policy.
            float effort = BeetleEnergyPolicy.AttackEffort(attacker) * lift.Effort;
            if (attacker.CheerRemaining > 0)
                effort *= 1 + settings.cheerEffortBonus;
            float inputArm = settings.throwInputArm * attacker.Traits.size;
            float torque = settings.throwMuscleForce * attacker.Traits.strength * effort * inputArm
                * Mathf.Lerp(.55f, 1, attacker.Footing) * Mathf.Lerp(.6f, 1, attacker.Balance);
            // Overcomes grip while tarsi remain grounded; retains minor dissipation once support is broken.
            float resistance = defender.Lift.GripReleased ? .08f :
                .35f * defender.Traits.grip * defender.Footing * Mathf.Lerp(.3f, 1, defender.Balance);
            lift.OutputForce = Mathf.Max(0, torque / lift.LeverArm - resistance);
            float netTorque = lift.OutputForce * lift.LeverArm;
            lift.Work = Mathf.Min(settings.throwWorkLimit, lift.Work + netTorque * delta * quality);
            lift.Impulse += lift.OutputForce * dt * quality;
            if (tangent.sqrMagnitude > .0001f)
                lift.ReleaseTangent = tangent.normalized;
        }

        public static int TierNumber(BeetleThrowKind kind)
        {
            switch (kind)
            {
                case BeetleThrowKind.ShortPry:
                    return 1;
                case BeetleThrowKind.QuickPry:
                    return 2;
                case BeetleThrowKind.ArcThrow:
                    return 3;
                case BeetleThrowKind.HighArc:
                    return 4;
                case BeetleThrowKind.Overhead:
                    return 5;
                case BeetleThrowKind.PowerOverhead:
                    return 6;
                default:
                    return 0;
            }
        }

        public static string Description(BeetleThrowKind kind)
        {
            switch (kind)
            {
                case BeetleThrowKind.ShortPry:
                    return "近身短促撬飞！";
                case BeetleThrowKind.QuickPry:
                    return "快速扬角，向前抛出！";
                case BeetleThrowKind.ArcThrow:
                    return "持续发力，弧线抛出！";
                case BeetleThrowKind.HighArc:
                    return "强力扬角，高弧抛起！";
                case BeetleThrowKind.Overhead:
                    return "后扬头角，翻抛对手！";
                case BeetleThrowKind.PowerOverhead:
                    return "猛然发力，越顶反抛！";
                default:
                    return "撬投";
            }
        }

        /// <summary>Discretized into six tiers by continuous launch speed and direction; four non-uniform thresholds are configurable.</summary>
        public static BeetleThrowKind Classify(BeetleMatchSettings settings, float speed, bool backwards)
        {
            if (backwards)
                return speed < settings.throwPowerOverheadSpeed ? BeetleThrowKind.Overhead : BeetleThrowKind.PowerOverhead;
            if (speed < settings.throwQuickSpeed)
                return BeetleThrowKind.ShortPry;
            if (speed < settings.throwArcSpeed)
                return BeetleThrowKind.QuickPry;
            return speed < settings.throwHighArcSpeed ? BeetleThrowKind.ArcThrow : BeetleThrowKind.HighArc;
        }

        /// <summary>Outputs continuous velocity and launch angle. Six tiers do not quantize all intra-tier contacts into identical trajectories.</summary>
        public static Vector3 Release(BeetleMatchSettings settings, BeetleState defender)
        {
            var lift = defender.Lift;
            float mass = Mathf.Max(.1f, defender.Traits.mass);
            float speedFromWork = Mathf.Sqrt(2 * lift.Work * settings.throwEfficiency / mass);
            float speedFromImpulse = lift.Impulse / mass;
            // Work bounds total energy while impulse limits brief or glancing contacts; sustained posture without pitch elevation yields no additional speed.
            float speed = Mathf.Min(speedFromWork, speedFromImpulse + .4f * speedFromWork);
            speed = Mathf.Clamp(speed, settings.throwMinimumSpeed, settings.throwMaximumSpeed);
            Vector3 forward = new Vector3(lift.Direction.x, 0, lift.Direction.y).normalized;
            Vector3 tangent = lift.ReleaseTangent.sqrMagnitude > .01f ? lift.ReleaseTangent.normalized : Vector3.up;
            float elevation = Mathf.Atan2(Mathf.Max(.12f, tangent.y), Vector3.Dot(tangent, forward)) * Mathf.Rad2Deg;
            bool overhead = lift.HeadSweep >= settings.throwOverheadSweep &&
                lift.Work / mass >= settings.throwOverheadWork && lift.PeakAngularSpeed >= settings.throwOverheadAngularSpeed;
            // Low-energy contacts slip free early instead of dangling along the horn into backflips. High energy retains backward tangential components;
            // restricts excessively flat backward throws to provide height clearance for clearing the attacker's body while collisions remain fully active.
            float ordinaryLimit = Mathf.Lerp(38, 82, Mathf.Clamp01(lift.Work / mass / 32));
            // Continuously increases permissible launch elevation between pitch-back and backward launch without abrupt trajectory snaps across tiers.
            float overheadLimit = Mathf.Lerp(100, 128, Mathf.InverseLerp(settings.throwHighArcSpeed, settings.throwMaximumSpeed, speed));
            // Strong releases travel outward instead of spending almost all speed vertically.
            // This explicit game gain is continuous and applies only after a real supported lift.
            float heavy = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(settings.throwArcSpeed, settings.throwMaximumSpeed, speed));
            ordinaryLimit = Mathf.Lerp(ordinaryLimit, Mathf.Min(ordinaryLimit, 65), heavy);
            elevation = Mathf.Clamp(elevation, 32, overhead ? overheadLimit : ordinaryLimit);
            float angle = elevation * Mathf.Deg2Rad;
            Vector3 side = Vector3.Cross(Vector3.up, forward);
            float lateral = Mathf.Clamp(Vector3.Dot(tangent, side), -.22f, .22f);
            Vector3 direction = (forward * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle) + side * lateral).normalized;
            float launchSpeed = speed * Mathf.Lerp(1, settings.throwHeavySpeedMultiplier, heavy);
            lift.LaunchVelocity = direction * launchSpeed;
            if (ShouldTumble(settings, lift.LaunchVelocity))
                lift.LaunchVelocity *= TumblingLaunchSpeedMultiplier;
            lift.LaunchElevation = Mathf.Atan2(lift.LaunchVelocity.y, Vector3.Dot(lift.LaunchVelocity, forward)) * Mathf.Rad2Deg;
            // Tier remains based on measured work/impulse speed, before the explicit game gain.
            lift.ThrowKind = Classify(settings, speed, Vector3.Dot(lift.LaunchVelocity, forward) < 0);
            return lift.LaunchVelocity;
        }
    }
}
