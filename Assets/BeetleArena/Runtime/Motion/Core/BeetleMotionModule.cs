using UnityEngine;

namespace BeetleArena
{
    public enum BeetleMotionChannel { Combat, SupportOverlay }
    public enum BeetleMotionContact { None, Horn, Foreleg }
    public enum BeetleMotionFamily { General, RaisedDrive, Foreleg }

    /// <summary>Value snapshot passed to modules. Lacks references to State, Transform, RNG, or Solver, preventing modules from mutating the match.</summary>
    public readonly struct BeetleMotionContext
    {
        public readonly float Time, SimulationTime, Speed, Stamina, Footing, Balance, Distance, Facing, Side, FloorTime, Impact;
        public readonly bool Fighting, Grounded, Fallen, OnFloor, Touching, OpponentAirborne, Committed, ForebodyRaised, CatchingBalance;
        public readonly bool OpponentContactConfirmed, ContactConfirmedAtStart;
        public readonly BeetleIntent Intent;
        public readonly float Burst;
        public readonly float Tempo, Drive, LiftStyle, Lateral, ForelegSide, RearSupport, ForelegTargetDistance;
        public readonly BeetleAction Action;
        public readonly ExchangePhase Exchange;
        public readonly Vector2 OpponentLocal;
        public readonly Vector3 ForelegAimCorrection;
        public BeetleMotionContext(BeetleSimulation sim, BeetleState b)
        {
            var other = sim.Fighters[1 - b.Index];
            Vector2 delta = other.Position - b.Position;
            var right = new Vector2(b.Forward.y, -b.Forward.x);
            Tempo = b.Traits.tempo;
            Drive = b.Traits.drivePreference;
            LiftStyle = b.Traits.liftPreference;
            Lateral = b.Traits.lateralPreference;
            Time = sim.MotionTime * Tempo + b.Index * .83f;
            Speed = b.Velocity.magnitude;
            SimulationTime = sim.MotionTime;
            Stamina = b.Stamina01;
            Footing = b.Footing;
            Balance = b.Balance;
            Distance = delta.magnitude;
            Facing = Vector2.Dot(b.Forward, delta.normalized);
            Side = b.CircleDirection;
            FloorTime = b.Fall.GroundSeconds;
            Impact = Mathf.Clamp01(b.ImpactVisual);
            Fighting = sim.Phase == MatchPhase.Fighting;
            Grounded = !b.Airborne && !b.HasFallen;
            Fallen = b.HasFallen;
            OnFloor = b.Fall.OnFloor;
            Touching = b.TouchingOpponent;
            OpponentContactConfirmed = sim.ContactSolver is IBeetlePairContacts pair && pair.HasOpponentContact(b.Index);
            ContactConfirmedAtStart = b.Motion.ContactConfirmedAtStart;
            OpponentAirborne = other.Airborne || other.HasFallen;
            Committed = b.Committed;
            Action = b.Action;
            Exchange = b.CombatPhase;
            Intent = b.Intent;
            Burst = b.BurstReserve;
            OpponentLocal = new Vector2(Vector2.Dot(delta, right), Vector2.Dot(delta, b.Forward));
            // Choose the nearer foreleg, alternate frontal attempts, then lock it for the action.
            ForelegSide = b.Motion.HasActive ? b.Motion.ForelegSide : Mathf.Abs(OpponentLocal.x) > .25f
                ? Mathf.Sign(OpponentLocal.x) : b.CircleDirection * (b.Motion.Starts % 2 == 0 ? 1 : -1);
            float reach = float.PositiveInfinity;
            ForelegAimCorrection = sim.ContactSolver is IBeetleForelegTargets targets
                ? targets.ForelegAimCorrection(b.Index, ForelegSide < 0 ? 0 : 3, out reach) : Vector3.zero;
            if (!b.Motion.HasActive && sim.ContactSolver is IBeetleForelegTargets both)
            {
                var alternative = both.ForelegAimCorrection(b.Index, ForelegSide < 0 ? 3 : 0, out float otherReach);
                if (otherReach + .08f < reach)
                { ForelegSide = -ForelegSide; ForelegAimCorrection = alternative; reach = otherReach; }
            }
            ForelegTargetDistance = reach;
            RearSupport = sim.ContactSolver is IBeetleDriveSupport support ? support.RearSupport(b.Index, sim) : b.Footing;
            ForebodyRaised = sim.ContactSolver is IBeetleForebodySupport posture && posture.ForebodyRaised(b.Index);
            CatchingBalance = Grounded && Speed > .12f && (Impact > .08f || Balance < .45f);
        }
    }

    /// <summary>Modules deliver intent only. Locomotion, energy burn, collision validation, and one-off impulses are handled uniformly by host.</summary>
    public struct BeetleMotionSample
    {
        public float BodyPitch, BodyRoll, HeadPitch, HeadYaw, BodyHeight, Strain, StaminaPerSecond;
        // Non-zero values override joint response speed for this action; defaults to retaining legacy motion/lift smoothing parameters.
        public float PoseResponse;
        public Vector2 LocalVelocity;
        public BeetleMotionContact Contact;
        public Vector2 LocalImpulse;
        public float Impulse, BalanceCost;
        // Vulnerability window following a valid hit; defaults to 0, not interrupting defensive actions of existing modules.
        public float StaggerSeconds;
        // 0 retains legacy selection; 1/4 explicitly identify left/right front leg.
        public int ContactLegPlusOne;
        public bool PlantRearFeet, RequiresRearSupport;
    }

    /// <summary>
    /// Stateless motion asset. Each module is an isolated file; Inspector reference list acts as registry without requiring core switch/case additions.
    /// Shared assets must never store timer/hit state. Instance data belongs strictly to BeetleMotionRuntime.
    /// </summary>
    public abstract class BeetleMotionModule : ScriptableObject
    {
        public bool enabledInMatch = true;
        [Min(0)] public float weight = 1;
        [Range(.25f, 3)] public float duration = .9f;
        [Range(.2f, 12)] public float cooldown = 2.5f;
        [TextArea] public string evidence;
        public abstract string DisplayName { get; }
        public abstract BeetleMotionChannel Channel { get; }
        public virtual bool AppliesContactResponse => true;
        // Shared recovery belongs to the fighter runtime, never the shared asset.
        public virtual BeetleMotionFamily Family => BeetleMotionFamily.General;
        public virtual float SelectionWeight(in BeetleMotionContext c) => weight;
        public abstract bool CanStart(in BeetleMotionContext context);
        public virtual bool CanContinue(in BeetleMotionContext c) => c.Fighting && c.Grounded && !c.OpponentAirborne && c.Distance < 4.8f;
        public abstract BeetleMotionSample Sample(in BeetleMotionContext context, float phase);
        // offset uses model local coordinates and unit scale. slot = Left/Right Front/Middle/Hind; at most covers its own anatomical workspace.
        public virtual bool SampleLeg(in BeetleMotionContext context, float phase, int slot, out Vector3 offset)
        { offset = Vector3.zero; return false; }
        public virtual float LegSpeed(in BeetleMotionContext context, float phase, int slot) => 3;
        // Antenna is an independent optional channel: returns local Euler angles relative to bound rest pose, smoothed by host.
        public virtual bool SampleAntenna(in BeetleMotionContext context, float phase, int slot, out Vector3 angles)
        { angles = Vector3.zero; return false; }
        // Default parameters/evidence for new assets are provided by their own class; the assembler requires no bespoke action type knowledge.
        public virtual void SetDefaults() { }
        protected virtual void OnEnable() { if (string.IsNullOrWhiteSpace(evidence)) SetDefaults(); }
        protected static float Pulse(float phase) => Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(phase)), 2);
        protected virtual void OnValidate()
        { weight = Mathf.Max(0, weight); duration = Mathf.Clamp(duration, .25f, 3); cooldown = Mathf.Clamp(cooldown, .2f, 12); }
    }

    /// <summary>Optional physical capability. In the absence of this interface, new module attacks will not fabricate hits via distance approximations.</summary>
    public interface IBeetleMotionContacts
    {
        bool HasMotionContact(int attacker, BeetleMotionContact kind, int forelegSlot);
    }
    public interface IBeetlePairContacts
    {
        bool HasOpponentContact(int fighter);
    }
    public interface IBeetleForelegTargets
    {
        Vector3 ForelegAimCorrection(int attacker, int slot, out float distance);
    }
    public interface IBeetleForebodySupport { bool ForebodyRaised(int attacker); }
    public interface IBeetleDriveSupport
    {
        float RearSupport(int attacker, BeetleSimulation sim);
    }
}
