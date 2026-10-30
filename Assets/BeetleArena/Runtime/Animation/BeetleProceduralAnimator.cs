using System;
using UnityEngine;

namespace BeetleArena
{
    [Serializable]
    // Binding metadata and gait cache for a single leg. Supplementary bone data for skinned models is stored in BeetleModelRig.
    // Represents a 2-segment analytical IK rig with tarsus, claws, and tibial spines; not a humanoid Unity Animator configuration.
    public sealed class BeetleLegRig
    {
        // hip/knee/ankle denote joint pivots; upper/lower/foot denote geometric leg segments connecting joints.
        public Transform hip, upper, knee, lower, ankle, foot;
        // Distal claws and tibial spines are accessory parts positioned after solving primary leg joints.
        public Transform[] claws, teeth;
        // hipInBody uses bodyPivot local space; restAnkle uses model local space; must not be conflated.
        public Vector3 hipInBody, restAnkle;
        // Segment lengths recorded under unit scale during modeling; scaled by visualScale * size during solve for world lengths.
        public float upperLength, lowerLength;
        // Offset from claw reference point to lowest tarsal vertex, preventing claw meshes from clipping through floor when bones land.
        public float soleOffset;
        // side: Left -1, Right +1; pair: Front 0, Middle 1, Hind 2. Dictates bend poles, workspace limits, and gait phases.
        public int side, pair;
        // Runtime cache not serialized into prefabs. planted is world-space; during swing phase it tracks the airborne target.
        // stepFrom/stepTo represent world endpoints of the current step and do not automatically move with the body.
        [NonSerialized]
        public Vector3 planted, stepFrom, stepTo;
        // Normalized step progress (0..1); not accumulated seconds.
        [NonSerialized]
        public float stepTime;
        // Step duration is fixed at launch; velocity shifts during swing do not cause abrupt speed jumps in foot motion.
        [NonSerialized]
        public float stepDuration;
        [NonSerialized]
        public bool stepping;
        // World-space positions for the previous physical foot placement and landing continuation trajectory.
        [NonSerialized]
        public Vector3 lastFoot, landingFrom;
        [NonSerialized]
        public float landingBlend;
        // Pluggable motion modules smoothly surrender foot targets to gait on exit, preventing floating feet upon module disposal.
        [NonSerialized]
        public bool moduleOwned;
        [NonSerialized]
        public float moduleReturn = 1;
        [NonSerialized]
        public Vector3 moduleReturnFrom;
        [NonSerialized]
        public bool supportAnchored;
        [NonSerialized]
        public Vector3 supportAnchor;
        [NonSerialized]
        public float tumbleFrameWeight;
    }

    // Procedural animation pipeline: combat state -> body/head pitch & roll -> foot trajectory -> analytical bone IK.
    // Consumes combat state without driving attack resolution, gravity, or outcomes; overlaps are resolved by ContactSolver.
    public sealed class BeetleProceduralAnimator : MonoBehaviour
    {
        [Header("Model articulation")]
        // model controls visual scale; bodyPivot controls body pitch/roll; headPivot drives head/horn elevation.
        public Transform model, bodyPivot, headPivot;
        public BeetleModelRig importedRig;
        public BeetleAppendageRig appendages;
        public Transform[] antennae;
        public BeetleLegRig[] legs;
        public Renderer[] shellRenderers;
        // Resting local position of body and antenna rest rotation; additive animations evaluate relative to resting base, preventing drift.
        Vector3 bodyRest;
        // Caches procedural thoracic height; slope terrain avoidance is layered additively without reading and accumulating from Transform.
        float bodyHeightOffset;
        Quaternion[] antennaRest;
        Vector3[] antennaAngles;
        // size is fighter scale multiplier, scale is final visual scale; pitch/roll angles are smoothed in degrees.
        // walkCycle is continuous gait phase. ground is shared with simulation, querying active arena surface.
        float size = 1, scale = .7f, bodyPitch, bodyRoll, headPitch, walkCycle;
        IBeetleGround ground;
        Vector3 previousPosition;
        bool initialized;
        BeetleMatchSettings settings;
        bool previousAirborne;
        BeetleAction previousAction;
        float liftingHeadStart, headYaw, liftingYaw, releaseHeadStart, releaseBodyStart;
        public float StruggleWeight { get; private set; }

        public float StruggleFrequency { get; private set; }

        float strugglePhase;
        bool fallingPose, floorPose;
        bool flightSpinning, flightRecovering;
        bool edgeTumbling;
        public float EdgeReach { get; private set; }

        bool backRighting;
        Vector3 rightingAngularVelocity;
        float stableRightingSeconds;
        public float LandingTuck { get; private set; }

        public bool BackLandingDetected { get; private set; }

        public float BackStruggleSeconds { get; private set; }

        public bool IsRighting => flightRecovering;
        float flightPitchAngle, flightRollAngle, flightPitchRate, flightRollRate, flightSeconds, flightRecovery;
        Quaternion flightOrigin;
        float fallPitchStart, fallRollStart, floorPitchStart, floorRollStart;
        float FallRecovery(BeetleState b) => Ease(Mathf.Clamp01((b.Fall.GroundSeconds - .12f) / settings.fallRecoverySeconds));
        // Analytically integrates SmoothStep frequency curve: preserves continuous phase and velocity.
        // Must not use time * Lerp(slow, fast, time), which would over-accelerate and stutter across transition boundaries.
        void UpdateStruggle(BeetleState state)
        {
            // Fallen fighters continue terrain probing; ground recovery preserves continuous leg swing phase rather than zeroing on disqualification.
            float t = state.Fall.Active ? Mathf.Max(0, Mathf.Max(state.Lift.UnsupportedTime,
                state.Fall.UnsupportedSeconds) - settings.liftStruggleDelay) +
                state.Fall.GroundSeconds : state.Airborne ? Mathf.Max(0, state.Lift.UnsupportedTime -
                settings.liftStruggleDelay) : 0;
            float ramp = Mathf.Max(.1f, settings.struggleRampSeconds);
            float u = Mathf.Clamp01(t / ramp);
            float f0 = settings.struggleStartHz, df = settings.struggleMaxHz - f0;
            StruggleWeight = Ease(Mathf.Clamp01(t / .35f));
            StruggleFrequency = t > 0 ? f0 + df * Ease(u) : 0;
            float cycles = f0 * t + df * (ramp * (u * u * u - .5f * u * u * u * u) + Mathf.Max(0, t - ramp));
            strugglePhase = cycles * (2 * Mathf.PI);
        }

        float SoleHeight(BeetleLegRig leg) => importedRig ? (leg.soleOffset + importedRig.soleClearance) *
            Mathf.Abs(model.lossyScale.x) : .05f * size;

        public void PrepareModel()
        {
            var adapter = GetComponent<BeetleModelAdapter>();
            if (adapter) adapter.ApplyBindings();
            if (!appendages) appendages = GetComponent<BeetleAppendageRig>();
            if (appendages)
            {
                if (!appendages.Validate(out var error)) throw new InvalidOperationException(error);
                appendages.ResetPose();
            }
        }

        public BeetleLegRig LegForSlot(int slot)
        {
            foreach (var leg in legs)
                if (leg != null && (leg.side < 0 ? 0 : 3) + leg.pair == slot) return leg;
            throw new InvalidOperationException("Missing logical beetle leg slot " + slot);
        }
        // Resets scale, posture, gait, and footplants upon per-match rebind. Colors applied via MaterialPropertyBlock
        // to avoid cloning materials across fighters. Actor places root at initial match spawn before this call.
        public void Initialize(BeetleTraits traits, BeetleMatchSettings settings, Color shellColor, IBeetleGround surface = null, float? visualScale = null)
        {
            PrepareModel();
            this.settings = settings;
            previousAirborne = false;
            StruggleWeight = StruggleFrequency = strugglePhase = 0;
            previousAction = BeetleAction.Assess;
            liftingHeadStart = headYaw = liftingYaw = 0;
            fallingPose = floorPose = flightSpinning = flightRecovering = false;
            edgeTumbling = false;
            EdgeReach = 0;
            flightPitchAngle = flightRollAngle = flightSeconds = flightRecovery = 0;
            LandingTuck = BackStruggleSeconds = 0;
            BackLandingDetected = backRighting = false;
            rightingAngularVelocity = Vector3.zero;
            stableRightingSeconds = 0;
            ground = surface;
            size = traits.size;
            scale = (visualScale ?? settings.visualScale) * size;
            model.localScale = Vector3.one * scale;
            model.localPosition = importedRig ? Vector3.zero : new Vector3(0, -.077f * size, 0);
            bodyRest = importedRig ? importedRig.bodyRest : new Vector3(0, 1.10f, 0);
            if (importedRig)
                importedRig.ResetRig();
            bodyPivot.localPosition = bodyRest;
            bodyPivot.localRotation = Quaternion.identity;
            headPivot.localRotation = Quaternion.identity;
            var hornRig = importedRig ? importedRig.GetComponent<BeetleHornRig>() : null;
            if (hornRig && hornRig.lengthPivot) hornRig.SetLength(traits.hornLengthFactor);
            else if (!importedRig) headPivot.localScale = new Vector3(1, 1, traits.hornLengthFactor);
            bodyPitch = bodyRoll = headPitch = walkCycle = bodyHeightOffset = 0;
            antennaAngles = new Vector3[antennae.Length];
            if (antennaRest == null || antennaRest.Length != antennae.Length)
            {
                antennaRest = new Quaternion[antennae.Length];
                for (int i = 0; i < antennae.Length; i++)
                    antennaRest[i] = antennae[i].localRotation;
            }

            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", importedRig ? Color.Lerp(Color.white, shellColor, .20f) : shellColor);
            foreach (var renderer in shellRenderers)
                if (renderer)
                    renderer.SetPropertyBlock(block);
            foreach (var leg in legs)
            {
                leg.tumbleFrameWeight = 0;
                leg.planted = NominalFoot(leg);
                leg.lastFoot = leg.planted;
                leg.landingBlend = 1;
                leg.moduleOwned = false;
                leg.moduleReturn = 1;
                leg.supportAnchored = false;
                leg.stepping = false;
            }

            previousPosition = transform.position;
            initialized = true;
        }

        // Fixed-step body rotation after an actual release. Long throws tumble; a short pry
        // retains its small recoil. The same articulated colliders follow this rotation.
        void UpdateFlightSpin(BeetleState state, float dt)
        {
            if (!state.Fall.EdgeDrop)
                edgeTumbling = false;
            if (!edgeTumbling && state.Fall.EdgeDrop && !state.Fall.OnFloor && !state.Lift.Supported)
            {
                BeginEdgeTumble(state);
            }

            if (state.IsOverturned && !flightSpinning && !flightRecovering)
            {
                flightRecovering = backRighting = BackLandingDetected = true;
                flightRecovery = stableRightingSeconds = 0;
                rightingAngularVelocity = Vector3.zero;
            }

            if (!flightSpinning && !flightRecovering && state.Action == BeetleAction.Launched && state.Airborne && !state.Lift.Supported)
            {
                TryBeginThrowTumble(state);
            }

            if (flightSpinning)
            {
                if (!state.Airborne || state.Fall.OnFloor || state.Lift.Supported)
                {
                    BeginLandingRecovery();
                }
                else
                {
                    AdvanceFlightRotation(state, dt);
                }
            }

            if (flightRecovering)
            {
                AdvanceRightingRecovery(state, dt);
            }
        }

        // Edge departure inherits different rotation axes for pitch vs roll slides; airborne ballistic throws bypass this path.
        void BeginEdgeTumble(BeetleState state)
        {
            edgeTumbling = flightSpinning = true;
            flightRecovering = false;
            flightSeconds = flightPitchAngle = flightRollAngle = 0;
            flightOrigin = bodyPivot.localRotation;
            Vector2 outward = state.Fall.EdgeDirection;
            Vector3 localDirection = model.InverseTransformDirection(new Vector3(outward.x, 0, outward.y));
            float angularSpeed = Mathf.Clamp(state.Fall.EdgeSpeed / Mathf.Max(.4f, size * .7f) * Mathf.Rad2Deg, 75, 200);
            flightPitchRate = localDirection.z * angularSpeed;
            flightRollRate = -localDirection.x * angularSpeed;
            LandingTuck = 0;
        }

        void TryBeginThrowTumble(BeetleState state)
        {
            Vector3 launch = state.Lift.LaunchVelocity;
            if (BeetleThrowMechanics.ShouldTumble(settings, launch))
            {
                flightSpinning = true;
                flightSeconds = flightPitchAngle = flightRollAngle = 0;
                edgeTumbling = false;
                LandingTuck = BackStruggleSeconds = 0;
                BackLandingDetected = backRighting = false;
                flightOrigin = bodyPivot.localRotation;
                float strength = Mathf.InverseLerp(6, 17.6f, launch.magnitude);
                float forwardSpeed = Vector2.Dot(new Vector2(launch.x, launch.z), state.Forward);
                flightPitchRate = (forwardSpeed >= 0 ? 1 : -1) * Mathf.Lerp(180, 340, strength);
                float rollDirection = state.Lift.TwistSign == 0 ? state.CircleDirection : state.Lift.TwistSign;
                flightRollRate = rollDirection * Mathf.Lerp(35, 90, strength);
            }
        }

        void BeginLandingRecovery()
        {
            flightSpinning = false;
            flightRecovering = true;
            flightRecovery = 0;
            backRighting = Vector3.Dot(bodyPivot.up, model.up) < -.25f;
            BackLandingDetected = backRighting;
            rightingAngularVelocity = Vector3.zero;
            stableRightingSeconds = 0;
        }

        void AdvanceFlightRotation(BeetleState state, float dt)
        {
            flightSeconds += dt;
            UpdateLandingTuck(state, dt);
            // Clears lifting horns before ramping to full tumble angular velocity.
            float release = Ease(Mathf.InverseLerp(.06f, .22f, flightSeconds));
            float damping = Mathf.Exp(-.12f * flightSeconds);
            flightPitchAngle += flightPitchRate * release * damping * dt;
            flightRollAngle += flightRollRate * release * damping * dt;
        }

        void UpdateLandingTuck(BeetleState state, float dt)
        {
            // Probes terrain along descending trajectory, tucking legs only when nearing ground impact.
            Vector2 ahead = state.Position + state.Velocity * .2f;
            float surface = ground != null && ground.Sample(ahead, out var hit) ? hit.height : settings.groundHeight;
            float clearance = Mathf.Max(0, bodyPivot.position.y - .38f * size - surface);
            float downwardSpeed = Mathf.Max(0, -state.VerticalVelocity);
            float gravity = Mathf.Max(1, settings.gravity);
            float untilContact = (Mathf.Sqrt(downwardSpeed * downwardSpeed + 2 * gravity * clearance) - downwardSpeed) / gravity;
            float tuck = state.VerticalVelocity < 0 ? 1 - Ease(Mathf.InverseLerp(.10f, .45f, untilContact)) : 0;
            LandingTuck = Mathf.Lerp(LandingTuck, tuck, 1 - Mathf.Exp(-dt * 18));
        }

        void AdvanceRightingRecovery(BeetleState state, float dt)
        {
            flightRecovery += dt;
            if (backRighting)
                BackStruggleSeconds += dt;
            backRighting = Vector3.Dot(bodyPivot.up, model.up) < .35f;
            stableRightingSeconds = state.RightingStable ? stableRightingSeconds + dt : 0;
            // Must right upright and establish stable ground support before restoring gait; elapsed timer does not equal success.
            if (stableRightingSeconds >= .2f)
            {
                flightRecovering = false;
                rightingAngularVelocity = Vector3.zero;
                bodyPitch = Mathf.DeltaAngle(0, bodyPivot.localEulerAngles.x);
                bodyRoll = Mathf.DeltaAngle(0, bodyPivot.localEulerAngles.z);
                floorPitchStart = bodyPitch;
                floorRollStart = bodyRoll;
            }
        }

        // time drives continuous idle sway, dt drives smoothing and step progress; state supplies velocity, phase, and impact reaction.
        // Under ContactDriven mode, PoseSimulation advances pose once per fixed step; Render no longer advances gait.
        public void Pose(BeetleState state, float time, float dt)
        {
            if (!initialized || legs == null)
                return;
            float speed = state.Velocity.magnitude;
            UpdateFlightSpin(state, dt);
            bool approachingEdge = false;
            if (!state.Airborne && !state.HasFallen && !state.Lift.Supported && speed > .25f && ground != null)
                approachingEdge = !ground.Sample(state.Position + state.Velocity.normalized * (.75f * size), out _);
            EdgeReach = Mathf.Lerp(EdgeReach, approachingEdge ? 1 : 0, 1 - Mathf.Exp(-dt * 14));
            if (state.Action == BeetleAction.Lifting && previousAction != BeetleAction.Lifting)
            {
                liftingHeadStart = headPitch;
                liftingYaw = headYaw;
            }

            if ((state.Action == BeetleAction.ReleaseLift || state.Action == BeetleAction.Toss) && previousAction != state.Action)
            {
                releaseHeadStart = headPitch;
                releaseBodyStart = bodyPitch;
            }

            float phase = state.ActionProgress;
            float targetHead = 12f, pitch = 0, roll = state.RollVisual;
            // Visual exertion weight: scales thoracic crouch and subtle twisting; not physical attack impulse or tarsal friction.
            float strain = 0;
            // Maps actions to target poses: subtle nod for probes, crouch for windup, rapid pitch-up for lift/toss, roll for airborne.
            // Pitch sign depends on local model axes; new FBX assets require recalibrating pivot directions.
            switch (state.Action)
            {
                case BeetleAction.Assess:
                    targetHead = 15 + InsectMotionTiming.Bout(time + state.Index * .83f, 1.43f, 203) * 9;
                    break;
                case BeetleAction.Probe:
                    targetHead = 18 + InsectMuscleAnimation.Recruit(phase) * 14;
                    pitch = 2.5f * InsectMuscleAnimation.Recruit(phase);
                    break;
                case BeetleAction.Disengage:
                    targetHead = 17;
                    pitch = -2;
                    break;
                case BeetleAction.ChargeWindup:
                    targetHead = Mathf.Lerp(18, 56, Ease(phase));
                    pitch = 9 * Ease(phase);
                    strain = .8f;
                    break;
                // A charge drives the whole body forward with a low horn; reserve the
                // large upward head sweep for Surge / a physically established lift.
                case BeetleAction.Charge:
                    targetHead = Mathf.Lerp(56, 24, InsectMuscleAnimation.Strike(phase));
                    pitch = -8 * Mathf.Sin(phase * Mathf.PI);
                    strain = 1.65f;
                    break;
                case BeetleAction.HornLock:
                    // Pulses short exertion bursts during clinches with intermediate bracing, avoiding sustained high-frequency mechanical vibration.
                    float effort = InsectMotionTiming.Bout(time + state.Index * .83f, .93f, 227);
                    targetHead = 24 + effort * 26;
                    pitch = 3 + effort * 9;
                    roll += state.CircleDirection * effort * 7;
                    strain = 1.25f;
                    break;
                case BeetleAction.ShoveWindup:
                    targetHead = Mathf.Lerp(14, 30, Ease(phase));
                    pitch = 4;
                    break;
                case BeetleAction.Shove:
                    targetHead = Mathf.Lerp(34, 18, InsectMuscleAnimation.Strike(phase));
                    pitch = -5 * Mathf.Sin(phase * Mathf.PI);
                    strain = 1.25f;
                    break;
                case BeetleAction.PryWindup:
                    targetHead = Mathf.Lerp(20, 82, Ease(phase));
                    strain = .5f;
                    break;
                // Keeps horn low during engagement; transitions to Lifting pitch-up only upon contacting opponent's underbody.
                case BeetleAction.Pry:
                    targetHead = 82;
                    strain = .7f;
                    break;
                case BeetleAction.Lifting:
                    // Explicitly raises cephalic horn; contact anchors move with bone, and physical constraints elevate defender.
                    float raise = Ease(Mathf.Clamp01(state.ActionTime / settings.liftRaiseSeconds));
                    targetHead = Mathf.Lerp(64, -85, raise);
                    pitch = -6 * raise;
                    strain = 1;
                    break;
                case BeetleAction.Lifted:
                    targetHead = 12;
                    pitch = state.Lift.Pitch;
                    roll = state.Lift.Roll;
                    break;
                case BeetleAction.ReleaseLift:
                    // Retracts horn synchronously with retreat, sliding opponent down horn face to prevent snapping down and shoving defender sideways.
                    targetHead = 30;
                    pitch = Mathf.Lerp(releaseBodyStart, 0, Ease(phase));
                    break;
                case BeetleAction.Toss:
                    targetHead = Mathf.Lerp(18, -24, Ease(Mathf.Min(1, phase * 3)));
                    pitch = -12 * Mathf.Sin(phase * Mathf.PI);
                    strain = 1.4f;
                    break;
                case BeetleAction.Launched:
                    targetHead = 5;
                    pitch = Mathf.Lerp(-32, 12, phase);
                    roll += state.CircleDirection * Mathf.Sin(phase * Mathf.PI) * 30;
                    break;
                case BeetleAction.Brace:
                    targetHead = 30;
                    pitch = 5;
                    strain = .65f;
                    break;
                case BeetleAction.Recover:
                    targetHead = 18;
                    break;
                case BeetleAction.Subdued:
                    targetHead = 23;
                    pitch = 3;
                    break;
                case BeetleAction.Stagger:
                    pitch = -state.LiftVisual * 17;
                    break;
                // Fall and loss tumbling blends continuously from entry pose without locking into a fixed canned death orientation.
                case BeetleAction.Falling:
                case BeetleAction.Defeated:
                    targetHead = 16;
                    break;
                case BeetleAction.Winner:
                    targetHead = 6;
                    break;
            }

            pitch -= state.LiftVisual * 8;
            pitch -= state.ImpactVisual * 5;
            if (state.Action != BeetleAction.Lifted)
            {
                pitch += state.Lift.Pitch;
                roll += state.Lift.Roll;
            }

            UpdateStruggle(state);
            var modulePose = state.Motion.PoseSample();
            if (state.Fall.Active)
            {
                // Uses independent fixed-step clock: Elapsed stops on match Finished, but tumbling and righting must continue.
                if (!fallingPose)
                {
                    fallPitchStart = bodyPitch;
                    fallRollStart = bodyRoll;
                    fallingPose = true;
                }

                float ft = state.Fall.Time;
                float turn = 1 - Mathf.Exp(-ft * 2.2f);
                float side = Mathf.Abs(state.Fall.SideSpeed) > .1f ? Mathf.Sign(state.Fall.SideSpeed) : state.CircleDirection;
                pitch = fallPitchStart + Mathf.Clamp(state.Fall.ForwardSpeed * 5, -28, 28) * turn;
                roll = fallRollStart + side * (14 + Mathf.Min(20, Mathf.Abs(state.Fall.SideSpeed) * 6)) * turn;
                roll += Mathf.Sin(ft * 6.5f) * 4 * turn;
                if (state.Fall.OnFloor)
                {
                    if (!floorPose)
                    {
                        floorPitchStart = bodyPitch;
                        floorRollStart = bodyRoll;
                        floorPose = true;
                    }

                    float recover = FallRecovery(state), gt = state.Fall.GroundSeconds;
                    float push = Mathf.Sin(gt * 9) * Mathf.Sin(Mathf.PI * recover) * 7;
                    pitch = Mathf.Lerp(floorPitchStart, 0, recover);
                    roll = Mathf.Lerp(floorRollStart, 0, recover) + side * push;
                    targetHead = 16 + Mathf.Sin(gt * 3) * 3;
                }
                else
                    floorPose = false;
                // Suspends decay of legacy LiftVisual/ImpactVisual during Finished state, as targets have already been overwritten above.
            }
            else
            {
                fallingPose = floorPose = false;
            }

            pitch += modulePose.BodyPitch;
            roll += modulePose.BodyRoll;
            targetHead += modulePose.HeadPitch;
            strain += modulePose.Strain;
            // Exponential smoothing approaches targets gradually, eliminating pops across state transitions; preserves pose when dt=0.
            // Modules can request faster joint response, but lift/throw/fall preserve baseline timing and contact continuity.
            float response = state.Action == BeetleAction.Module && modulePose.PoseResponse >
                0 ? Mathf.Clamp(modulePose.PoseResponse, 13, 29) : InsectMuscleAnimation.Response(state, 13);
            float blend = 1 - Mathf.Exp(-dt * response);
            if (importedRig)
                targetHead = (targetHead - 12) * importedRig.headMotionScale * (state.Committed && state.Action !=
                    BeetleAction.Module ? Mathf.Lerp(1.18f, 1.48f, state.Traits.drivePreference) : 1);
            // Modules already specify their complete pose; do not amplify it a second time.
            if (state.Committed && state.Action != BeetleAction.Module)
                pitch *= Mathf.Lerp(.85f, 1.25f, state.Traits.drivePreference);
            if (state.Action == BeetleAction.Lifting)
                targetHead = liftingHeadStart - state.LiftSweepTarget * state.LiftStrokeProgress;
            // Throw release maintains accepted elevated pitch with brief follow-through, preventing snapping down into defender.
            if (state.Action == BeetleAction.Toss)
            {
                targetHead = releaseHeadStart - 8 * Mathf.Sin(phase * Mathf.PI);
                pitch = Mathf.Lerp(releaseBodyStart, -8, Ease(phase));
            }

            // Premature release or partial lifts recover from actual head pitch rather than assuming full -85 deg elevation.
            if (state.Action == BeetleAction.ReleaseLift)
                targetHead = Mathf.Lerp(releaseHeadStart, importedRig ? (30 - 12) * importedRig.headMotionScale : 30, Ease(phase));
            // During a jam withdrawal keep the accepted horn/chest pitch while backing
            // out. Raising a low horn toward the idle pose pushes it into the abdomen
            // above and can cancel the same retreat that is meant to free it.
            if (state.Action == BeetleAction.Disengage)
            {
                targetHead = Mathf.DeltaAngle(0, headPivot.localEulerAngles.x);
                pitch = Mathf.DeltaAngle(0, bodyPivot.localEulerAngles.x);
            }
            headPitch = Mathf.Lerp(headPitch, targetHead, blend);
            bodyPitch = Mathf.Lerp(bodyPitch, pitch, blend);
            bodyRoll = Mathf.Lerp(bodyRoll, roll, blend);
            // Gait frequency scales with planar speed; phase requires foot placement error; stationary state will not step continuously on phase alone.
            walkCycle += dt * Mathf.Lerp(.8f, 2.4f, Mathf.Clamp01(speed));
            // Exoskeleton does not perform continuous thoracic breathing heave; resting center of mass shifts are handled by support module.
            float bob = Mathf.Sin(walkCycle * Mathf.PI * 4) * Mathf.Min(speed, 1) * .018f;
            float landingCompression = state.Fall.OnFloor ? Mathf.Sin(Mathf.PI *
                Mathf.Clamp01(state.Fall.GroundSeconds / .24f)) * Mathf.Min(.1f, state.Fall.ImpactSpeed * .012f) : 0;
            // Thoracic height interpolates gradually on windup enter/exit, preventing rapid recruitment visuals from popping the body vertically in one frame.
            bodyHeightOffset = Mathf.Lerp(bodyHeightOffset, flightRecovering ? 0 : bob - strain * .055f -
                landingCompression + modulePose.BodyHeight, 1 - Mathf.Exp(-dt * 22));
            bodyPivot.localPosition = bodyRest + Vector3.up * bodyHeightOffset;
            Quaternion uprightPose = Quaternion.Euler(bodyPitch, 0, bodyRoll);
            if (flightSpinning)
            {
                Quaternion spun = flightOrigin * Quaternion.AngleAxis(flightPitchAngle, Vector3.right) *
                    Quaternion.AngleAxis(flightRollAngle, Vector3.forward);
                bodyPivot.localRotation = Quaternion.Slerp(bodyPivot.localRotation, spun, 1 - Mathf.Exp(-dt * 25));
            }
            else if (flightRecovering)
            {
                float inertia = Mathf.Max(.08f, state.Traits.mass * size * size * .36f);
                Vector3 torque = state.RightingGrounded ? state.RightingTorque : Vector3.zero;
                rightingAngularVelocity += torque / inertia * (Mathf.Rad2Deg * dt);
                rightingAngularVelocity *= Mathf.Exp(-dt * 8);
                rightingAngularVelocity = Vector3.ClampMagnitude(rightingAngularVelocity, 65);
                float turn = rightingAngularVelocity.magnitude * dt;
                if (turn > .00001f)
                    bodyPivot.rotation = Quaternion.AngleAxis(turn, rightingAngularVelocity.normalized) * bodyPivot.rotation;
            }
            else
                bodyPivot.localRotation = uprightPose;
            // Quiet assessment uses small irregular sampling; insertion stays sagittal.
            float yawTarget = state.Action == BeetleAction.Assess ? (InsectMotionTiming.Bout(time + state.Index * .83f,
                1.61f, 239) - .5f * InsectMotionTiming.Bout(time + .4f, 2.17f, 241)) * 3 : state.Action ==
                BeetleAction.PryWindup || state.Action == BeetleAction.Pry ? 0 : state.Action == BeetleAction.Lifting ||
                state.Action == BeetleAction.ReleaseLift || state.Action == BeetleAction.Toss ? liftingYaw : 0;
            yawTarget += modulePose.HeadYaw;
            if (state.Action == BeetleAction.Disengage)
                yawTarget = Mathf.DeltaAngle(0, headPivot.localEulerAngles.y);
            headYaw = Mathf.LerpAngle(headYaw, yawTarget, blend);
            headPivot.localRotation = Quaternion.Euler(headPitch, headYaw, 0);
            if (importedRig && !state.Airborne && !state.HasFallen)
                headPitch = importedRig.AvoidHeadGround(headPivot, ground, headPitch, headYaw);
            for (int i = 0; i < antennae.Length; i++)
            {
                state.Motion.SampleAntenna(i, out var targetAngles);
                antennaAngles[i] = Vector3.Lerp(antennaAngles[i], targetAngles, 1 - Mathf.Exp(-dt * 18));
                antennae[i].localRotation = antennaRest[i] * Quaternion.Euler(antennaAngles[i]);
            }

            if (appendages)
            {
                state.Motion.SampleAntenna(0, out var sensing);
                appendages.SetSensing(sensing.x / 30f, sensing.y / 45f);
                appendages.Advance(dt);
            }

            if (importedRig && !state.Airborne && !state.HasFallen)
                importedRig.AvoidFeelerGround(ground, dt);
            // Tallies legs currently in swing phase for the swing budget below. Budget restricts new steps without interrupting active swings.
            int steppingCount = 0;
            foreach (var leg in legs)
                if (leg.stepping)
                    steppingCount++;
            // Uses alternating tripod rhythm only when moving fast enough and uncommitted; forced exertion switches to cautious gait.
            bool catching = !state.Airborne && !state.HasFallen && speed > .12f && (state.ImpactVisual > .08f || state.Balance < .45f);
            bool fast = speed > .55f && !state.Committed && !catching;
            // Resets footplant cache if root teleports > 2 world units in a single step, preventing hyper-extended legs from dragging from distant origins.
            bool teleported = Vector3.Distance(previousPosition, transform.position) > 2;
            foreach (var leg in legs)
            {
                // Nominal foot targets translate/rotate with body; grounded planted targets stay world-locked to establish stance phase.
                leg.tumbleFrameWeight = 0;
                Vector3 nominal = NominalFoot(leg);
                if (importedRig && !state.Airborne && !state.HasFallen)
                    nominal = importedRig.PlanSupport(leg, nominal, state, ground);
                Vector3 ankle;
                int slot = (leg.side < 0 ? 0 : 3) + leg.pair;
                bool ownsLeg = state.Motion.SampleLeg(slot, out var moduleOffset);
                bool anchor = state.Motion.PlantSupportLeg(slot) && !leg.stepping && leg.landingBlend >= 1 &&
                    !teleported && (leg.supportAnchored || leg.lastFoot.y <= FootHeight(leg.lastFoot, leg) + .06f *
                    size);
                if (anchor && !leg.supportAnchored)
                    leg.supportAnchor = leg.lastFoot;
                leg.supportAnchored = anchor;
                if (leg.pair == 2 && LegForSlot(leg.side < 0 ? 5 : 2).stepping)
                    ownsLeg = false;
                if (leg.moduleOwned && !ownsLeg)
                {
                    leg.moduleReturnFrom = leg.lastFoot;
                    leg.moduleReturn = 0;
                }

                leg.moduleOwned = ownsLeg;
                // Airborne state suspends grounded gait in favor of phase-offset local oscillations portraying flailing and tucking.
                // Amplitude is larger when tossed airborne and smaller after ring-out settlement; does not generate physical propelling forces.
                if (flightSpinning || flightRecovering)
                {
                    // Far throws use a compact body-relative pose. Do not mix world-space
                    // foot chasing, ground gait or combat modules into a rotating torso.
                    float upright = Vector3.Angle(bodyPivot.up, model.up);
                    float extend = flightRecovering && !backRighting ? Ease(Mathf.InverseLerp(65, 12, upright)) : 0;
                    float order = (leg.pair == 1 ? 0 : leg.pair == 2 ? .12f : .24f) + (leg.side > 0 ? .06f : 0);
                    extend = Ease(Mathf.InverseLerp(order, 1, extend));
                    leg.tumbleFrameWeight = 1;
                    Vector3 tucked = leg.restAnkle - bodyRest;
                    float curl = flightSpinning ? LandingTuck : 1;
                    tucked.x *= Mathf.Lerp(.96f, .82f, curl);
                    tucked.y += Mathf.Lerp(.06f, .20f, curl);
                    tucked.z += (1 - leg.pair) * -.045f;
                    // Gentle airborne seeking fades out before impact. Back-down landing
                    // recruits alternating sides; all targets stay in their anatomical sector.
                    float limbPhase = flightSpinning ? flightSeconds * 7 : flightRecovery * 10;
                    limbPhase += leg.pair * 1.1f + (leg.side > 0 ? Mathf.PI : 0);
                    float struggle = flightSpinning ? .14f * (1 - LandingTuck) : backRighting ? .21f : .02f;
                    tucked.y += struggle * Mathf.Sin(limbPhase) * (1 - extend);
                    tucked.z += struggle * .45f * Mathf.Cos(limbPhase) * (1 - extend);
                    if (edgeTumbling && flightSpinning)
                    {
                        // Reach forward and rake back with alternating forelegs while the lip
                        // is still nearby. Rear legs keep their own phase; tuck before impact.
                        float reach = Mathf.Exp(-flightSeconds * 1.3f) * (1 - LandingTuck);
                        float rake = .5f + .5f * Mathf.Sin(flightSeconds * 12 + (leg.side > 0 ? Mathf.PI : 0));
                        float front = leg.pair == 0 ? 1 : leg.pair == 1 ? .35f : 0;
                        tucked.z += front * reach * (.25f + .40f * rake);
                        tucked.y += front * reach * (.16f * (1 - rake) - .12f);
                    }

                    if (backRighting)
                    {
                        float push = Mathf.Max(0, Mathf.Sin(limbPhase));
                        tucked.x += leg.side * .16f * push;
                        tucked.y -= .16f * push;
                    }

                    ankle = bodyPivot.TransformPoint(tucked);
                    Vector3 facing = Vector3.ProjectOnPlane(bodyPivot.forward, model.up);
                    if (facing.sqrMagnitude < .001f)
                        facing = model.forward;
                    Quaternion supportFrame = Quaternion.LookRotation(facing, model.up);
                    Vector3 support = bodyPivot.position + supportFrame * ((leg.restAnkle - bodyRest) * scale);
                    support.y = state.Fall.OnFloor ? settings.groundHeight + SoleHeight(leg) : FootHeight(support, leg);
                    ankle = Vector3.Lerp(ankle, support, extend);
                    leg.stepping = leg.supportAnchored = leg.moduleOwned = false;
                    ownsLeg = false;
                    leg.moduleReturn = leg.landingBlend = 1;
                    leg.planted = ankle;
                }
                else if (state.Airborne || state.HasFallen)
                {
                    leg.stepping = false;
                    // Tarsal targets follow pried thoracic/abdominal tilt. Front and middle legs lift first; hind legs grip existing pivot as long as possible.
                    // Tucks/struggles only after pivot is broken; setting Airborne does not snap all six legs airborne simultaneously.
                    float cycle = strugglePhase + leg.pair * 2.2f + leg.side * 1.1f;
                    float amplitude = .36f * StruggleWeight;
                    Vector3 curl = new Vector3(-leg.side * .15f, .26f, -.08f * (1 - leg.pair));
                    Vector3 kick = new Vector3(leg.side * Mathf.Sin(cycle + .7f) * amplitude * .55f, Mathf.Sin(cycle) *
                        amplitude, Mathf.Cos(cycle * .86f + leg.pair) * amplitude);
                    // Immediately seek a foothold during free flight; vigorous struggling
                    // still waits for the existing lost-support delay.
                    if (!state.Lift.Supported)
                        kick += new Vector3(leg.side * .05f * Mathf.Sin(cycle), .09f * Mathf.Sin(cycle + 1), .12f * Mathf.Cos(cycle));
                    ankle = bodyPivot.TransformPoint(leg.restAnkle - bodyRest + curl + kick);
                    if (state.Fall.Active)
                    {
                        // Initial extension search followed by out-of-phase extension-flexion. Blends into stance targets only after contacting out-of-ring terrain.
                        float ft = state.Fall.Time;
                        float reach = 1 - Ease(Mathf.Clamp01(ft / settings.fallReachSeconds));
                        float search = (1 - reach) * .16f;
                        float legPhase = ft * 7 + leg.pair * 1.8f + leg.side * 1.2f;
                        Vector3 seek = new Vector3(leg.side * (.16f * reach + search * Mathf.Sin(legPhase)), -.16f *
                            reach + search * Mathf.Cos(legPhase), (1 - leg.pair) * .09f);
                        ankle = bodyPivot.TransformPoint(leg.restAnkle - bodyRest + curl * (1 - reach) + kick + seek);
                        if (state.Fall.OnFloor)
                        {
                            float recovery = FallRecovery(state);
                            Vector3 support = model.TransformPoint(leg.restAnkle);
                            support.y = settings.groundHeight + SoleHeight(leg);
                            // Staggers tactile probing across sides and front/mid/hind pairs; braces stably upon recovery, avoiding synchronized paddling or endless flailing.
                            float probe = Mathf.Max(0, Mathf.Sin(legPhase)) * .10f * (1 - recovery);
                            support.y += probe;
                            float settle = Ease(Mathf.Clamp01(state.Fall.GroundSeconds / .24f));
                            ankle = Vector3.Lerp(ankle, support, settle);
                            ankle.y = Mathf.Max(ankle.y, settings.groundHeight + SoleHeight(leg));
                        }

                        // Airborne preserves continuity with prior foot targets and limits IK target speed; contact solver still verifies all legs and horns.
                        ankle = Vector3.MoveTowards(leg.lastFoot, ankle, (4 * size + speed + Mathf.Abs(state.VerticalVelocity)) * dt);
                    }

                    if (state.Lift.Supported)
                    {
                        float start = leg.pair == 0 ? .05f : leg.pair == 1 ? .25f : .5f;
                        float letGo = Ease(Mathf.InverseLerp(start, start + .4f, state.Lift.RaiseProgress));
                        ankle = Vector3.Lerp(leg.planted, ankle, letGo);
                    }

                    // Claws above arena surface must not penetrate terrain; out-of-ring tarsi dangle naturally without snapping to arena rim.
                    if (!state.HasFallen && ground != null && ground.Sample(new Vector2(ankle.x, ankle.z), out _))
                        ankle.y = Mathf.Max(ankle.y, FootHeight(ankle, leg));
                }
                else
                {
                    if (previousAirborne)
                    {
                        leg.landingFrom = leg.lastFoot;
                        leg.landingBlend = 0;
                        leg.stepping = false;
                    }

                    if (leg.landingBlend < 1)
                    {
                        leg.landingBlend = Mathf.Min(1, leg.landingBlend + dt / .18f);
                        leg.planted = Vector3.Lerp(leg.landingFrom, nominal, Ease(leg.landingBlend));
                        leg.planted.y = Mathf.Max(leg.planted.y, FootHeight(leg.planted, leg));
                        SolveLeg(leg, leg.planted, state);
                        leg.lastFoot = ActualFoot(leg, leg.planted);
                        continue;
                    }

                    if (teleported)
                    {
                        if (leg.stepping)
                            steppingCount--;
                        leg.planted = nominal;
                        leg.stepping = false;
                    }

                    // Contact correction and fast turns can leave a planted foot on
                    // the wrong side of its own abdomen. Replant before solving IK.
                    // Collision corrections or abrupt yaw may displace old footplants across body centerline or beyond reach.
                    // Standard deviations trigger accelerated recovery steps; only severe teleportation or centerline crossing resets instantly.
                    Vector3 localPlant = model.InverseTransformPoint(leg.planted);
                    float plantError = Vector3.Distance(leg.planted, nominal);
                    bool urgent = localPlant.x * leg.side < (importedRig ? Mathf.Abs(leg.restAnkle.x) * .55f : 1.10f) ||
                        plantError > .40f * size;
                    // Middle legs with longer tarsi have narrower reachable margins than uniform thresholds, triggering early steps to preserve flex.
                    urgent |= importedRig && plantError > .04f * size && importedRig.SupportReach(leg, leg.planted, state, ground) > .96f;
                    if (localPlant.x * leg.side < 0 || plantError > 1.5f * size)
                    {
                        if (leg.stepping)
                            steppingCount--;
                        leg.planted = nominal;
                        leg.stepping = false;
                    }

                    // Tripod gait at high speeds: Left-Front + Left-Hind + Right-Middle, with opposite triplet out of phase; wave gait at low speeds.
                    float tripodOffset = ((leg.side < 0 && leg.pair != 1) || (leg.side > 0 && leg.pair == 1)) ? 0 : .5f;
                    float waveOffset = leg.side < 0 ? leg.pair / 3f : Mathf.Repeat(leg.pair / 3f + 1f / 3f, 1);
                    // Repeat wraps continuous phase into 0..1; initiates a step only when entering that leg's swing phase window.
                    float cycle = Mathf.Repeat(walkCycle + (fast ? tripodOffset : waveOffset), 1);
                    float error = Vector3.Distance(leg.planted, nominal);
                    // Exertion and stance holding permit at most 1 swinging leg; standard tripod caps at 3, wave gait caps at 2.
                    int maxLifted = catching || state.Committed || state.Action == BeetleAction.Brace ? 1 : fast ? 3 : 2;
                    urgent |= catching && error > .05f * size;
                    // Requires: not currently stepping, displacement > 0.16 * size from rest, leg phase window open, swing quota available.
                    var oppositeHind = leg.pair == 2 ? LegForSlot(leg.side < 0 ? 5 : 2) : null;
                    bool oppositeHindBusy = oppositeHind != null && (oppositeHind.stepping || oppositeHind.moduleOwned ||
                        state.Motion.SampleLeg(leg.side < 0 ? 5 : 2, out _) || oppositeHind.moduleReturn < 1);
                    if (!leg.stepping && !state.Motion.PlantSupportLeg(slot) && !oppositeHindBusy && (urgent || error >
                        .16f * size && cycle < (fast ? .42f : .24f)) && steppingCount <
                        (catching ? 1 : urgent ? 3 : maxLifted))
                    {
                        leg.stepFrom = leg.planted;
                        // Predicts ground intercept 0.17s ahead along current velocity, planting foot forward; target latches once step begins.
                        leg.stepTo = nominal + new Vector3(state.Velocity.x, 0, state.Velocity.y) * .17f;
                        leg.stepTo.y = FootHeight(leg.stepTo, leg);
                        if (importedRig)
                            leg.stepTo = importedRig.PlanSupport(leg, leg.stepTo, state, ground);
                        // Speed transitions only affect current swing; grounded tarsi remain world-locked rather than accelerating all six legs.
                        float vigor = Mathf.Lerp(.82f, 1, state.BurstReserve);
                        leg.stepDuration = (catching ? .14f : urgent ? .12f : fast ? .16f : .22f) / vigor;
                        leg.stepDuration *= Mathf.Lerp(.93f, 1.07f, InsectMotionTiming.Value(Mathf.FloorToInt(walkCycle), slot + 331));
                        leg.stepTime = 0;
                        leg.stepping = true;
                        steppingCount++;
                    }

                    if (leg.stepping)
                    {
                        // Step duration is latched at launch; normalized progress is elapsed time divided by latched duration.
                        leg.stepTime += dt / Mathf.Max(.08f, leg.stepDuration);
                        float t = Mathf.Clamp01(leg.stepTime);
                        // Horizontal trajectory follows segmented Swing curve; vertical offset adds sin(pi * t) * 0.13 * size.
                        // Extra elevation is zero at t=0 and t=1, peaking at t=0.5; this arc serves as IK input.
                        leg.planted = Vector3.Lerp(leg.stepFrom, leg.stepTo, InsectMuscleAnimation.Swing(t)) +
                            Vector3.up * (Mathf.Sin(t * Mathf.PI) * (catching ? .22f : .13f) * size);
                        // Samples terrain elevation along entire swing arc to provide foot clearance over convex bumps, not just endpoints.
                        leg.planted.y = Mathf.Max(leg.planted.y, FootHeight(leg.planted, leg) + Mathf.Sin(t * Mathf.PI) * .08f * size);
                        if (t >= 1)
                        {
                            leg.stepping = false;
                            steppingCount--;
                        }
                    }

                    ankle = leg.planted;
                }

                // Modules supply tarsal intent; host unifies ground projection, velocity clamping, and IK/collision passes.
                if (leg.supportAnchored)
                {
                    // Keep the accepted ground point in world space as the torso rises/drives.
                    // IK may lose reach; the resulting real foot gap reduces simulation drive.
                    ankle = leg.supportAnchor;
                    leg.planted = ankle;
                    leg.moduleReturn = 1;
                    leg.stepping = false;
                }
                else if (ownsLeg)
                {
                    Vector3 target = state.Fall.OnFloor ? model.TransformPoint(leg.restAnkle) : nominal;
                    if (state.Fall.OnFloor)
                        target.y = settings.groundHeight + SoleHeight(leg);
                    target += model.TransformVector(moduleOffset);
                    target.y = Mathf.Max(target.y, state.Fall.OnFloor ? settings.groundHeight + SoleHeight(leg) : FootHeight(target, leg));
                    ankle = Vector3.MoveTowards(leg.lastFoot, target, (state.Motion.LegSpeed(slot) * size + speed) * dt);
                    leg.moduleReturn = 1;
                    leg.planted = ankle;
                    leg.stepping = false;
                }
                else if (leg.moduleReturn < 1)
                {
                    // Resumes from physical foot position at exit, preventing instantaneous snapping to rest position when plugins terminate.
                    leg.moduleReturn = Mathf.Min(1, leg.moduleReturn + dt / .18f);
                    Vector3 target = state.Fall.OnFloor ? model.TransformPoint(leg.restAnkle) : state.Airborne ? ankle : nominal;
                    if (state.Fall.OnFloor)
                        target.y = settings.groundHeight + SoleHeight(leg);
                    ankle = Vector3.Lerp(leg.moduleReturnFrom, target, Ease(leg.moduleReturn));
                    leg.planted = ankle;
                    leg.stepping = false;
                }

                // Connects leg base to ankle target using identical solver regardless of whether leg is grounded, stepping, or airborne.
                if (EdgeReach > .01f && !state.Airborne && !state.HasFallen && !flightSpinning && !flightRecovering &&
                    leg.pair == 0 && !ownsLeg)
                {
                    float rake = Mathf.Max(0, Mathf.Sin(time * 12 + (leg.side > 0 ? Mathf.PI : 0)));
                    Vector3 reaching = nominal + model.TransformVector(new Vector3(0, .18f * rake, .45f * rake));
                    if (ground != null && ground.Sample(new Vector2(reaching.x, reaching.z), out _))
                        reaching.y = Mathf.Max(reaching.y, FootHeight(reaching, leg));
                    ankle = Vector3.Lerp(ankle, reaching, EdgeReach);
                    leg.planted = ankle;
                }

                SolveLeg(leg, ankle, state);
                leg.lastFoot = ActualFoot(leg, ankle);
            }

            previousAirborne = state.Airborne || state.HasFallen;
            previousAction = state.Action;
            previousPosition = transform.position;
        }

        // Contact constraints may accept only partial pose advance. Next step resumes from actual accepted joint angles and foot tips.
        // Grounded planted targets remain world-space anchor points; translating with whole-beetle corrections would cause foot slippage.
        public void AcceptSolvedPose(bool limited)
        {
            if (limited)
            {
                bodyPitch = Mathf.DeltaAngle(0, bodyPivot.localEulerAngles.x);
                bodyRoll = Mathf.DeltaAngle(0, bodyPivot.localEulerAngles.z);
                headPitch = Mathf.DeltaAngle(0, headPivot.localEulerAngles.x);
                headYaw = Mathf.DeltaAngle(0, headPivot.localEulerAngles.y);
            }

            foreach (var leg in legs)
                leg.lastFoot = ActualFoot(leg, leg.lastFoot);
            previousPosition = transform.position;
        }

        Vector3 ActualFoot(BeetleLegRig leg, Vector3 fallback) => importedRig ? importedRig.BindingFor(leg).tip.position : fallback;
        // Local feedback for contact constraints: horn under-forces yield head joint first, avoiding translating entire root backwards.
        public void YieldHead(float degrees)
        {
            // Contact retries restore candidate Transforms without rolling back animation smoothing buffers.
            // Samples angles from current candidate trial pose, preventing failed trials from accumulating retreat or overwriting yaw.
            headPitch = Mathf.DeltaAngle(0, headPivot.localEulerAngles.x);
            headYaw = Mathf.DeltaAngle(0, headPivot.localEulerAngles.y);
            // Preserves angle when already above retreat threshold. Calling Max(-32, ...) blindly would pull a head already at -46 deg
            // back to -32 deg, instantly re-penetrating opponent and triggering root position corrections.
            headPitch = Mathf.Min(headPitch, Mathf.Max(-55, headPitch - degrees));
            headPivot.localRotation = Quaternion.Euler(headPitch, headYaw, 0);
        }

        // A withdrawing horn can need to lower under an abdomen before the root can move.
        // This only proposes a joint pose: the contact solver must still accept every
        // collider and the root budget. Do not change smoothing caches on a rejected trial.
        public bool TryLowerHeadForRetreat(float degrees)
        {
            float pitch = Mathf.DeltaAngle(0, headPivot.localEulerAngles.x);
            float target = Mathf.Min(35, pitch + Mathf.Max(0, degrees));
            if (target <= pitch + .0001f)
                return false;
            float yaw = Mathf.DeltaAngle(0, headPivot.localEulerAngles.y);
            headPivot.localRotation = Quaternion.Euler(target, yaw, 0);
            return true;
        }

        public bool TryTurnHeadForRetreat(float degrees)
        {
            float yaw = Mathf.DeltaAngle(0, headPivot.localEulerAngles.y);
            float target = degrees > 0 ? Mathf.Min(24, yaw + degrees) : Mathf.Max(-24, yaw + degrees);
            if (degrees > 0 ? target <= yaw + .0001f : target >= yaw - .0001f) return false;
            float pitch = Mathf.DeltaAngle(0, headPivot.localEulerAngles.x);
            headPivot.localRotation = Quaternion.Euler(pitch, target, 0);
            return true;
        }

        // Adjusts IK target when articulated legs are blocked by horns or opponent legs, preserving bone lengths; rigid thorax absorbs whole-body separation.
        public void YieldLeg(int index, Vector3 direction, BeetleState state)
        {
            var leg = LegForSlot(index);
            Vector3 outward = model.right * leg.side;
            Vector3 move = Vector3.ProjectOnPlane(direction, Vector3.up) + outward * .5f;
            if (move.sqrMagnitude < .001f)
                move = outward;
            Vector3 target = ActualFoot(leg, leg.planted) + move.normalized * .035f + Vector3.up * .025f;
            target.y = Mathf.Max(target.y, FootHeight(target, leg));
            leg.planted = target;
            leg.stepping = false;
            SolveLeg(leg, target, state);
            leg.lastFoot = ActualFoot(leg, target);
        }

        // Transforms initial ankle position from model local to world space, then establishes resting ankle height above ground.
        Vector3 NominalFoot(BeetleLegRig leg)
        {
            var foot = model.TransformPoint(leg.restAnkle);
            foot.y = FootHeight(foot, leg);
            return foot;
        }

        public Vector3 MotionFootBase(int slot, BeetleState state)
        {
            var leg = LegForSlot(slot);
            var target = NominalFoot(leg);
            return importedRig && !state.Airborne && !state.HasFallen ? importedRig.PlanSupport(leg, target, state, ground) : target;
        }

        // While the body is supported, a foot beyond the rim is held above the
        // edge. Lowering its target through the stump wall made the whole compound
        // body get pushed sideways by its own misplaced foot.
        // Queries physical ground surface below ankle; off-ring misses tuck near root height rather than stretching to forest floor.
        float FootHeight(Vector3 p, BeetleLegRig leg = null)
        {
            float clearance = importedRig ? (importedRig.soleClearance + (leg == null ? 0 : leg.soleOffset)) * scale : .13f * size;
            if (ground == null)
                return clearance;
            return (ground.Sample(new Vector2(p.x, p.z), out var hit) ? hit.height : transform.position.y) + clearance;
        }

        // Analytical two-bone IK: given hip A, target C, lengths a and b, computes intermediate joint B.
        // Triangle calculations operate in world space; state parameter is reserved.
        void SolveLeg(BeetleLegRig leg, Vector3 target, BeetleState state)
        {
            if (importedRig)
            {
                importedRig.Solve(leg, target, state, ground);
                return;
            }

            // Transforms into model local space to constrain reachable workspace: prevents crossing centerline or colliding with adjacent legs.
            Quaternion legFrame = Quaternion.Slerp(model.rotation, bodyPivot.rotation, leg.tumbleFrameWeight);
            Vector3 legOrigin = Vector3.Lerp(model.TransformPoint(bodyRest), bodyPivot.position, leg.tumbleFrameWeight);
            Vector3 localTarget = Quaternion.Inverse(legFrame) * (target - legOrigin) / scale + bodyRest;
            localTarget.x = leg.side * Mathf.Max(1.02f, localTarget.x * leg.side);
            // Keep each leg in its anatomical sector instead of crossing neighbours.
            localTarget.z = Mathf.Clamp(localTarget.z, leg.restAnkle.z - .32f, leg.restAnkle.z + .32f);
            target = legOrigin + legFrame * ((localTarget - bodyRest) * scale);
            // Workspace clamps alter XZ plane, requiring re-projecting support target Y to prevent clipping into slopes post-clamp.
            if (!state.Airborne && !state.HasFallen)
                target.y = Mathf.Max(target.y, FootHeight(target));
            Vector3 hip = bodyPivot.TransformPoint(leg.hipInBody);
            float upper = leg.upperLength * scale, lower = leg.lowerLength * scale;
            Vector3 offset = target - hip;
            // Reachable distance spans |a - b| to a + b; leaves 0.001 margin to prevent hyperextension or total collapse singularities.
            // Ankle target is clamped when out of reach, so final foot position may differ from input target.
            float distance = Mathf.Clamp(offset.magnitude, Mathf.Abs(upper - lower) + .001f, upper + lower - .001f);
            Vector3 direction = offset.sqrMagnitude > .00001f ? offset.normalized : Vector3.down;
            target = hip + direction * distance;
            // Bend pole points outwards and slightly upwards, with slight fore/aft bias for front/hind legs.
            // Projects onto plane orthogonal to hip-to-ankle vector, ensuring along and bend axes are strictly orthogonal.
            // Relies on configured workspace; if pole aligns with target direction, multi-segment/FBX setups require fallback poles.
            Vector3 pole = legFrame * new Vector3(leg.side, .4f, (1 - leg.pair) * .18f);
            pole = Vector3.ProjectOnPlane(pole, direction).normalized;
            // Law of Cosines: projection of intermediate joint along A->C is x = (a^2 - b^2 + d^2) / (2d).
            float along = (upper * upper - lower * lower + distance * distance) / (2 * distance);
            // Pythagorean theorem: bend elevation from A->C axis is h = sqrt(a^2 - x^2); Max(0, ...) guards against negative square roots.
            float bend = Mathf.Sqrt(Mathf.Max(0, upper * upper - along * along));
            // Final knee position: B = A + direction * x + pole * h; pole selects an unambiguous bend plane.
            Vector3 knee = hip + direction * along + pole * bend;
            // Writes joint positions and fits capsule colliders between joints; manipulates procedural primitives directly without skinned meshes.
            leg.hip.position = hip;
            leg.knee.position = knee;
            leg.ankle.position = target;
            Segment(leg.upper, hip, knee);
            Segment(leg.lower, knee, target);
            // After primary leg solve, tarsus, claws, and spines are positioned from ankle; accessory segments do not solve separate IK.
            Vector3 footNormal = state.SurfaceNormal;
            if (!state.Airborne && ground != null && ground.Sample(new Vector2(target.x, target.z), out var footHit))
                footNormal = footHit.normal;
            Quaternion footFrame = Quaternion.FromToRotation(Vector3.up, footNormal) * Quaternion.Euler(0, state.Heading, 0);
            footFrame = Quaternion.Slerp(footFrame, bodyPivot.rotation, leg.tumbleFrameWeight);
            Vector3 toe = target + footFrame * new Vector3(leg.side * .15f, -.11f, .16f) * scale;
            Segment(leg.foot, target, toe);
            for (int i = 0; i < leg.claws.Length; i++)
                Segment(leg.claws[i], toe, toe + footFrame * new Vector3(leg.side * .075f, -.02f, i == 0 ? -.07f : .07f) * scale);
            for (int i = 0; i < leg.teeth.Length; i++)
            {
                Vector3 p = Vector3.Lerp(knee, target, .39f + i * .24f);
                Segment(leg.teeth[i], p, p + transform.TransformDirection(new Vector3(leg.side * .1f, .015f, .065f)) * scale);
            }
        }

        // Aligns capsule along local Y axis between two world endpoints: midpoints, orient along delta, scale height.
        // Drives basic primitives; FBX skeletons should retain bone lengths and rotate joints.
        static void Segment(Transform segment, Vector3 a, Vector3 b)
        {
            segment.position = (a + b) * .5f;
            segment.rotation = Quaternion.FromToRotation(Vector3.up, b - a);
            Vector3 s = segment.localScale;
            // Unity default capsule height is 2 along Y; divides by parent world Y scale to avoid compounding scale.
            // Assumes uniform hierarchy scaling conventions, not arbitrary non-uniform shear hierarchies.
            s.y = Vector3.Distance(a, b) / (2 * Mathf.Max(.001f, segment.parent.lossyScale.y));
            segment.localScale = s;
        }

        // Smoothstep polynomial 3t^2 - 2t^3: endpoints at 0/1 with zero first derivatives, ensuring smooth velocity transitions.
        static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3 - 2 * t);
        }
    }
}
