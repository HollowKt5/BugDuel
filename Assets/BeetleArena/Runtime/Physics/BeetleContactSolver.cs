using UnityEngine;

namespace BeetleArena
{
    // Simulation contact dependency interface: implementations can inspect and mutate both fighter states. Falls back to pure-data separation when null.
    public interface IBeetleContactSolver
    {
        void Solve(BeetleSimulation simulation, float dt);
    }

    public interface IBeetleRightingPose
    {
        bool IsRighting(int fighter);
    }

    // Kinematic articulated contact constraints. PhysX supplies narrow-phase convex
    // mesh contacts; this solver owns movement so animation cannot override a rigidbody.
    // Kinematic contact solver over subdivided convex meshes: PhysX handles detection, code resolves position and velocity corrections.
    // Does not use free-body dynamic Rigidbodies, nor does it solve per-tarsus friction, muscle actuators, or true horn-tip leverage.
    public sealed class BeetleContactSolver : IBeetleContactSolver, IBeetleLiftContacts, IBeetleMotionContacts,
        IBeetleForelegTargets, IBeetleDriveSupport, IBeetleForebodySupport, IBeetleRightingPose, IBeetlePairContacts
    {
        // Actor provides pose; ContactRig holds part colliders; arena holds stump and ground colliders.
        readonly BeetleActor[] actors;
        readonly BeetleContactRig[] rigs;
        readonly Collider[] arena;
        // Small separation margin retained to mitigate jitter caused by immediate re-penetration on grazing contacts.
        readonly float skin;
        // Reusable candidate index buffer to eliminate heap allocations from temporary collections during each solve pass.
        readonly int[] candidatesA, candidatesB;
        // Residual margin from the prior mutual separation pass; ramps can squeeze bodies together, so environment quiescence alone does not permit early exit.
        float separationRemaining;
        // Diagnostic: tracks the peak residual overlap measured during Separate; not an absolute upper bound across all times or bodies.
        public float MaximumResidual { get; private set; }

        // Tracks how many times Solve encountered mutual beetle-to-beetle contact, excluding arena-only contact.
        public int ContactFrames { get; private set; }

        public string LastWorstContact { get; private set; }

        Collider separationA, separationB;
        // Formats diagnostics on demand. Deep contact retries only retain references, avoiding name string allocations on candidate updates.
        public string LastSeparationPair => separationA && separationB ? separationA.name + " / " + separationB.name : null;
        public Vector3 LastSeparationNormal { get; private set; }

        readonly BeetleHornSupport hornSupport = new BeetleHornSupport();
        readonly BeetleContactMotion[] motion, viewMotion, retreatFallback;
        bool viewInitialized;
        float viewBlockedSeconds;
        int acceptedHornContacts;
        int acceptedForelegContacts;
        bool recordingContacts;
        readonly Vector2[] lastProgress = new Vector2[2];
        readonly float[] progressClock = new float[2];
        readonly float[] recentContact = new float[2];
        readonly Vector2[] proposedVelocity = new Vector2[2];
        readonly float[] motionBudget = new float[2];
        public int LimitedMotionSteps { get; private set; }
        public int ContactRetreatSteps { get; private set; }
        public int ContactHeadRetreatSteps { get; private set; }
        // Count candidates, not wall-clock time: identical fixed steps must make
        // identical choices on different machines and with different render rates.
        public const int MaxRetreatTrialsPerStep = 6;
        public int LastRetreatTrials { get; private set; }
        int headRetreatCursor, rootRetreatCursor;

        public float AcceptedMotionFraction { get; private set; } = 1;
        // Actual budget consumed during this Solve pass; post-landing velocity may clear to zero, so diagnostics must not reconstruct budgets from retrospective velocity.
        public float MotionBudget(int index) => motionBudget[index];
        public string LiftContact => hornSupport.ContactDescription;
        public float LiftAnchorError => hornSupport.Error;
        public bool TryBeginLift(BeetleSimulation sim, BeetleState attacker, BeetleState defender)
        {
            if (hornSupport.Active(sim) || attacker.Lift.Supported || defender.Lift.Supported)
                return false;
            Physics.SyncTransforms();
            Cache();
            return hornSupport.TryCapture(attacker.Index, rigs[attacker.Index], rigs[defender.Index]);
        }

        // Creates the solver per match, gathers both collider sets, and enables ContactDriven so poses advance synchronously with fixed simulation.
        public BeetleContactSolver(BeetleActor[] actors, Collider[] arena, float skin)
        {
            this.actors = actors;
            this.arena = arena ?? System.Array.Empty<Collider>();
            this.skin = skin;
            motion = new[] { new BeetleContactMotion(actors[0]), new BeetleContactMotion(actors[1]) };
            viewMotion = new[] { new BeetleContactMotion(actors[0]), new BeetleContactMotion(actors[1]) };
            retreatFallback = new[] { new BeetleContactMotion(actors[0]), new BeetleContactMotion(actors[1]) };
            rigs = new[] { actors[0].GetComponent<BeetleContactRig>(), actors[1].GetComponent<BeetleContactRig>() };
            int capacity = Mathf.Max(rigs[0].shapes.Length, rigs[1].shapes.Length);
            candidatesA = new int[capacity];
            candidatesB = new int[capacity];
            foreach (var actor in actors)
                actor.ContactDriven = true;
        }

        // Places Actor at simulation position and updates joints, syncs PhysX transforms and bounds, then executes separation.
        // Corrected root positions are written back to simulation state as baselines for next integration step and ring-out evaluation.
        public void Solve(BeetleSimulation sim, float dt)
        {
            LastRetreatTrials = 0;
            // View projection also records normals. Only this fixed step's contacts
            // may steer authoritative retreat candidates.
            LastSeparationNormal = Vector3.zero;
            for (int i = 0; i < 2; i++)
            {
                motion[i].Begin();
                var b = sim.Fighters[i];
                proposedVelocity[i] = b.Velocity;
                // Preserves true ballistic velocities; generic joint collision corrections share a uniform full-step budget.
                float physicalSpeed = new Vector3(b.Velocity.x, b.Airborne ? b.VerticalVelocity : 0, b.Velocity.y).magnitude;
                motionBudget[i] = Mathf.Max(sim.Settings.contactMotionSpeed, physicalSpeed + .5f) * dt;
                actors[i].PoseSimulation(sim.MotionTime, dt);
            }

            Physics.SyncTransforms();
            Cache();
            if (dt > 0 && (ArticulatedContact(sim.Fighters[0]) || ArticulatedContact(sim.Fighters[1])))
                YieldArticulatedContacts(sim);
            if (hornSupport.Active(sim))
                hornSupport.Follow(sim, rigs[hornSupport.Defender], dt);
            foreach (var m in motion)
                m.Propose();
#if UNITY_EDITOR
            Vector3 debugProposalA = actors[0].transform.position, debugProposalB = actors[1].transform.position;
#endif
            bool contact = Constrain(sim, true);
#if UNITY_EDITOR
            BeetleDebugHooks.Contact(sim, this, 1, separationRemaining, debugProposalA, debugProposalB, actors[0].transform.position, actors[1].transform.position);
#endif
            AcceptedMotionFraction = 1;
            if (dt > 0 && !MotionAccepted())
            {
                LimitedMotionSteps++;
                // First attempts the full motion step; if repeated separation results in teleportation, progressively scales down pose advance.
                // All retries share the original velocity; rejected trials must neither consume nor generate momentum.
                bool accepted = false;
                // Centroid axis can be nearly orthogonal to contact normal: minor horn tip overlaps would project into massive backwards translation,
                // rendering pure step-size reduction useless. Retries the full proposal along true contact normal first, then scales down.
                for (int attempt = 0; attempt <= 7; attempt++)
                {
                    float fraction = Mathf.Pow(.5f, attempt);
                    for (int i = 0; i < 2; i++)
                    {
                        motion[i].Apply(fraction);
                        sim.Fighters[i].Velocity = proposedVelocity[i];
                    }

                    Physics.SyncTransforms();
                    Cache();
                    // Re-evaluates local avoidance after scaling overall pose advance. Essential head/horn retreat must not be scaled to near zero,
                    // or interlocked horns can never decouple; each retry rebuilds from the same pose proposal without accumulating rotations.
                    if (ArticulatedContact(sim.Fighters[0]) || ArticulatedContact(sim.Fighters[1]))
                        YieldArticulatedContacts(sim);
                    contact = Constrain(sim, true, true);
                    AcceptedMotionFraction = fraction;
#if UNITY_EDITOR
                    BeetleDebugHooks.Contact(sim, this, fraction, separationRemaining, debugProposalA, debugProposalB, actors[0].transform.position, actors[1].transform.position);
#endif
                    if (MotionAccepted())
                    {
                        accepted = true;
                        break;
                    }
                }

                if (!accepted || AcceptedMotionFraction < .25f)
                {
                    // A tiny valid joint step is not an escape. Keep it as a fallback, but
                    // also try retreat once sustained blockage has been observed. Previously
                    // accepting even 1/128 of the pose skipped this branch forever.
                    bool hadAccepted = accepted;
                    float savedFraction = AcceptedMotionFraction, savedResidual = separationRemaining;
                    bool savedContact = contact;
                    int savedHorns = acceptedHornContacts, savedForelegs = acceptedForelegContacts;
                    Vector2 savedVelocityA = sim.Fighters[0].Velocity, savedVelocityB = sim.Fighters[1].Velocity;
                    if (hadAccepted)
                        foreach (var m in retreatFallback) m.Begin();
                    accepted = false;
                    Vector3 retreatNormal = Vector3.ProjectOnPlane(LastSeparationNormal, Vector3.up);
                    // A hooked horn may need a small neck movement before either root can
                    // retreat. Test these cheap joint-only routes before the full root/leg
                    // search; rejected trials do not write animation or foot caches.
                    for (int candidate = 0; candidate < 6 && !accepted && LastRetreatTrials < 2; candidate++)
                    {
                        int trial = headRetreatCursor;
                        headRetreatCursor = (headRetreatCursor + 1) % 6;
                        int index = trial % 2;
                        var state = sim.Fighters[index];
                        if (state.Airborne || state.HasFallen || state.IsOverturned || state.ContactBlockedSeconds < .3f ||
                            hornSupport.Active(sim) || state.Lift.Supported || actors[index].animator.IsRighting)
                            continue;
                        LastRetreatTrials++;
                        accepted = TryHeadBeforeRetreat(index, trial / 2, sim, dt, out contact);
                        if (accepted)
                        {
                            AcceptedMotionFraction = .02f;
                            ContactRetreatSteps++;
                            ContactHeadRetreatSteps++;
                        }
                    }
                    // A valid root retreat can be rejected solely by the new articulated pose.
                    // Retry from the accepted joints, one animal at a time, through the same
                    // penetration and distance limits. No collider is disabled and no teleport occurs.
                    // Each direction tries rigid joints, local yielding, then a bounded
                    // downward neck rotation; a valid rigid retreat keeps its leg pose.
                    // Resume the bounded search on later fixed steps. Start with the
                    // actual separation normal, but retain every diagonal/pose route
                    // for horn locks which cannot escape along that normal alone.
                    const int rootCandidates = 9 * 3 * 2;
                    for (int candidate = 0; candidate < rootCandidates && !accepted && LastRetreatTrials < MaxRetreatTrialsPerStep; candidate++)
                    {
                        int trial = rootRetreatCursor;
                        rootRetreatCursor = (rootRetreatCursor + 1) % rootCandidates;
                        int index = trial % 2, pose = trial / 2 % 3, direction = trial / 6;
                        var state = sim.Fighters[index];
                        if (state.Airborne || state.HasFallen || state.IsOverturned || state.ContactBlockedSeconds < .3f)
                            continue;
                        if (pose == 2 && (hornSupport.Active(sim) || state.Lift.Supported || actors[index].animator.IsRighting))
                            continue;
                        for (int i = 0; i < 2; i++)
                        {
                            motion[i].Apply(0);
                            sim.Fighters[i].Velocity = proposedVelocity[i];
                        }

                        Vector3 origin = actors[index].transform.position;
                        Vector3 away = origin - actors[1 - index].transform.position;
                        away.y = 0;
                        away = away.sqrMagnitude > .000001f ? away.normalized : -actors[index].transform.forward;
                        Vector3 side = Vector3.Cross(Vector3.up, away);
                        LastRetreatTrials++;
                        if (pose == 2 && !actors[index].animator.TryLowerHeadForRetreat(Mathf.Min(4, 200 * dt)))
                            continue;

                        Vector3 forward = actors[index].transform.forward;
                        Vector3 retreat = direction switch
                        {
                            0 => retreatNormal.sqrMagnitude > .000001f ? (index == 0 ? retreatNormal : -retreatNormal) : away,
                            1 => -forward,
                            2 => away,
                            3 => side,
                            4 => -side,
                            5 => -forward + side,
                            6 => -forward - side,
                            7 => forward + side,
                            _ => forward - side
                        };
                        retreat.y = 0;
                        retreat = retreat.normalized * Mathf.Min(.025f, motionBudget[index] * .4f);
                        if (!sim.SampleGround(new Vector2(origin.x, origin.z), out var from) ||
                            !sim.SampleGround(new Vector2(origin.x + retreat.x, origin.z + retreat.z), out var to) ||
                            to.normal.y < .5f)
                            continue;
                        retreat.y = to.height - from.height;
                        actors[index].transform.position += retreat;
                        Physics.SyncTransforms();
                        Cache();
                        // Moving a frozen compound rig can wedge a front leg against a
                        // horn even when the torso has room. Re-solve local joint contacts
                        // at this retreat pose before attempting whole-body separation.
                        if (pose != 0) YieldArticulatedContacts(sim);
                        contact = Constrain(sim, true, true);
                        Vector3 actual = actors[index].transform.position - origin;
                        accepted = MotionAccepted() && Vector3.Dot(actual, retreat.normalized) > .002f;
                        if (accepted)
                        {
                            AcceptedMotionFraction = .02f;
                            ContactRetreatSteps++;
                            if (pose == 2) ContactHeadRetreatSteps++;
                        }
                    }
                    // Failed search must not throw away the already validated small step,
                    // its velocity response, or the accepted-contact hit certificates.
                    if (!accepted && hadAccepted)
                    {
                        foreach (var m in retreatFallback) m.Restore();
                        sim.Fighters[0].Velocity = savedVelocityA;
                        sim.Fighters[1].Velocity = savedVelocityB;
                        AcceptedMotionFraction = savedFraction;
                        separationRemaining = savedResidual;
                        contact = savedContact;
                        acceptedHornContacts = savedHorns;
                        acceptedForelegContacts = savedForelegs;
                        Physics.SyncTransforms();
                        Cache();
                        accepted = true;
                    }
                }

                if (!accepted)
                {
                    // The previous step was a verified safe pose. Suspends blocked actions without forcefully translating the beetle or disabling colliders.
                    for (int i = 0; i < 2; i++)
                    {
                        motion[i].Apply(0);
                        sim.Fighters[i].Velocity = proposedVelocity[i];
                    }

                    Physics.SyncTransforms();
                    Cache();
                    AcceptedMotionFraction = 0;
                    contact = HasOpponentContact(0);
                    acceptedHornContacts = 0;
                    acceptedForelegContacts = 0;
                }
            }

            for (int i = 0; i < 2; i++)
            {
                var state = sim.Fighters[i];
                Vector3 p = actors[i].transform.position;
                state.Position = new Vector2(p.x, p.z);
                state.Height = p.y;
                // Accept the actual heading too. Otherwise rejected turns accumulate in the AI
                // while the rendered root keeps rolling back, producing endless spinning retries.
                Vector3 forward = Quaternion.Inverse(Quaternion.FromToRotation(Vector3.up, state.SurfaceNormal)) *
                    actors[i].transform.forward;
                state.Heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                ResolveLandingContact(i, sim, dt);
                // Both fighters share mutual contact detection results for the current step. Does not track individual contact points or normals here.
                state.TouchingOpponent = contact;
                bool standing = !state.Airborne && !state.HasFallen && !state.IsOverturned;
                bool barelyMoved = Vector3.Distance(motion[i].Start, p) < .005f;
                state.ContactBlockedSeconds = standing && AcceptedMotionFraction < .25f && barelyMoved
                    ? state.ContactBlockedSeconds + dt : Mathf.Max(0, state.ContactBlockedSeconds - dt * 2);
                // Keep contact memory across the skin gap and action transitions. Long
                // pry/brace loops with moving legs but no body progress are jams too.
                recentContact[i] = contact ? .2f : Mathf.Max(0, recentContact[i] - dt);
                bool tryingToMove = state.Action == BeetleAction.Approach || state.Action == BeetleAction.Circle ||
                    state.Action == BeetleAction.Disengage || recentContact[i] > 0;
                if (tryingToMove && standing && !hornSupport.Active(sim) && sim.Phase == MatchPhase.Fighting)
                {
                    if (Vector2.Distance(lastProgress[i], state.Position) > .10f)
                    {
                        lastProgress[i] = state.Position;
                        progressClock[i] = 0;
                    }
                    else
                        progressClock[i] += dt;
                    // Allow a normal brief horn lock, but do not restart this clock at every
                    // attack transition. Real displacement, flight or separation resets it.
                    if (progressClock[i] > 1.4f)
                        state.ContactBlockedSeconds = Mathf.Max(.65f, state.ContactBlockedSeconds);
                }
                else
                {
                    progressClock[i] = 0;
                    lastProgress[i] = state.Position;
                }

                if (state.Lift.Supported)
                    state.Lift.BlockedTime = AcceptedMotionFraction < .05f ? state.Lift.BlockedTime + dt : Mathf.Max(0,
                        state.Lift.BlockedTime - 2 * dt);
                UpdateFootSupport(actors[i], sim, dt);
                UpdateRightingSupport(i, sim, dt);
                actors[i].animator.AcceptSolvedPose(AcceptedMotionFraction < 1);
                actors[i].CaptureSimulationPose();
            }

            // Accumulates work done by head-raising into leverage mechanics only after all rollbacks and environment passes complete.
            if (hornSupport.Active(sim))
                hornSupport.Measure(sim, actors[hornSupport.Attacker], dt);
            if (!viewInitialized || dt <= 0)
            {
                foreach (var m in viewMotion)
                    m.Begin();
                viewInitialized = true;
            }

            if (contact)
                ContactFrames++;
        }

        bool TryHeadBeforeRetreat(int index, int direction, BeetleSimulation sim, float dt, out bool contact)
        {
            contact = false;
            for (int i = 0; i < 2; i++)
            {
                motion[i].Apply(0);
                sim.Fighters[i].Velocity = proposedVelocity[i];
            }
            var animator = actors[index].animator;
            Quaternion before = animator.headPivot.localRotation;
            float limit = Mathf.Min(4, 200 * dt);
            bool changed = direction == 0 ? animator.TryLowerHeadForRetreat(limit) :
                animator.TryTurnHeadForRetreat(direction == 1 ? limit : -limit);
            if (!changed) return false;
            Physics.SyncTransforms();
            Cache();
            contact = Constrain(sim, true, true);
            float turn = Quaternion.Angle(before, animator.headPivot.localRotation);
            // This is only a pose proposal. Failed trials leave the animation caches
            // untouched; AcceptSolvedPose publishes the accepted joint at the end of Solve.
            return MotionAccepted() && turn > .1f && turn <= limit + .01f;
        }

        // Resolves friction exclusively on the final accepted pose; trial evaluations and rollbacks must not dissipate momentum prematurely.
        void ResolveLandingContact(int index, BeetleSimulation sim, float dt)
        {
            var state = sim.Fighters[index];
            bool landingContact = false;
            Vector3 landingNormal = Vector3.up;
            bool needsLandingContact = state.HasFallen || (state.Airborne && state.VerticalVelocity <= 0) ||
                state.LandingSliding || state.LandingImpactSpeed > 0;
            if (needsLandingContact)
                landingContact = TryLandingContact(rigs[index], state.HasFallen, sim.Settings.groundHeight, out landingNormal);
            sim.ResolveLandingFriction(state, landingContact, landingNormal, dt);
            if (state.HasFallen)
            {
                state.Fall.SetFloorContact(landingContact, -state.VerticalVelocity);
                if (landingContact && state.VerticalVelocity < 0)
                    state.VerticalVelocity = 0;
            }
        }

        // Probes down 3.5cm from final accepted pose to verify supporting ground contact; post-ring-out only recognizes arena-floor geometry.
        // Uses true upward contact normals; never treats ring walls as ground, nor allows render poses to drain momentum.
        bool TryLandingContact(BeetleContactRig rig, bool forestOnly, float floorHeight, out Vector3 contactNormal)
        {
            contactNormal = Vector3.up;
            foreach (var surface in arena)
            {
                if (!surface || !surface.enabled || !surface.gameObject.activeInHierarchy || forestOnly &&
                    Mathf.Abs(surface.bounds.max.y - floorHeight) > .05f)
                    continue;
                foreach (var shape in rig.shapes)
                {
                    var bounds = shape.bounds;
                    bounds.Expand(.08f);
                    if (!bounds.Intersects(surface.bounds))
                        continue;
                    if (Physics.ComputePenetration(shape, shape.transform.position - Vector3.up * .035f,
                        shape.transform.rotation, surface, surface.transform.position, surface.transform.rotation,
                        out var normal, out _) && normal.y > .5f)
                    {
                        contactNormal = normal;
                        return true;
                    }
                }
            }

            return false;
        }

        public bool HasMotionContact(int attacker, BeetleMotionContact kind, int forelegSlot)
        {
            if (attacker < 0 || attacker > 1 || kind == BeetleMotionContact.None)
                return false;
            // Separation intentionally opens a skin gap. Preserve actual horn collisions from
            // the accepted fixed-step solve; rejected retries and display projection never count.
            if (kind == BeetleMotionContact.Horn && (acceptedHornContacts & (1 << attacker)) != 0)
                return true;
            if (kind == BeetleMotionContact.Foreleg && forelegSlot != 0 && forelegSlot != 3)
                return false;
            if (kind == BeetleMotionContact.Foreleg && (acceptedForelegContacts & (1 << (attacker * 2 + forelegSlot / 3))) != 0)
                return true;
            return HasPartContact(attacker, kind, forelegSlot);
        }

        // Action startup verifies current true geometry, never relying on stale hit caches or solver rollbacks from the previous frame.
        public bool HasOpponentContact(int fighter)
        {
            return fighter >= 0 && fighter < 2 && HasPartContact(fighter, BeetleMotionContact.None, -1);
        }

        bool HasPartContact(int attacker, BeetleMotionContact kind, int forelegSlot)
        {
            Physics.SyncTransforms();
            Cache();
            var a = rigs[attacker];
            var b = rigs[1 - attacker];
            for (int i = 0; i < a.shapes.Length; i++)
            {
                if (kind == BeetleMotionContact.Horn && a.Role(i) != BeetleContactRole.HeadHorn)
                    continue;
                if (kind == BeetleMotionContact.Foreleg && a.LegIndex(i) != forelegSlot)
                    continue;
                for (int j = 0; j < b.shapes.Length; j++)
                {
                    // The attacking collider must be the selected foreleg. It may push a horn
                    // as well as a leg/body; horn-versus-horn alone never passes the filter above.
                    var bounds = a.bounds[i];
                    bounds.Expand(.05f);
                    if (!bounds.Intersects(b.bounds[j]))
                        continue;
                    var ca = a.shapes[i];
                    var cb = b.shapes[j];
                    Vector3 toward = (cb.ClosestPoint(a.bounds[i].center) - a.bounds[i].center).normalized;
                    // Contact resolution preserves skin margin and probes 2cm toward opponent; non-colliding actions still complete without triggering hits.
                    if (Physics.ComputePenetration(ca, ca.transform.position + toward * .02f, ca.transform.rotation, cb,
                        cb.transform.position, cb.transform.rotation, out _, out _))
                        return true;
                }
            }

            return false;
        }

        public Vector3 ForelegAimCorrection(int attacker, int slot, out float targetDistance)
        {
            // BeforeStep restored authoritative bones; do not query stale interpolated PhysX poses.
            Physics.SyncTransforms();
            var anim = actors[attacker].animator;
            var leg = anim.legs[slot];
            // Aim the end of the stroke at a nearby actual surface. This is an IK intent,
            // never a contact certificate; collision and bone-length constraints still apply.
            bool pushing = actors[attacker].State.Motion.Active is ForelegPushMotion;
            Vector3 stroke = pushing ? new Vector3(-leg.side * .18f,
                .52f, .72f) : new Vector3(-leg.side * .28f, .72f, .70f);
            Vector3 end = anim.MotionFootBase(slot, actors[attacker].State) + anim.model.TransformVector(stroke);
            Vector3 closest = end;
            float distance = float.MaxValue;
            var opponentRig = rigs[1 - attacker];
            for (int index = 0; index < opponentRig.shapes.Length; index++)
            {
                // A resisting foreleg reaches the opponent's head/horn or torso.
                // Chasing the nearest ground-level tarsus requires an impossible
                // fully stretched low reach while the long horns hold bodies apart.
                if (opponentRig.LegIndex(index) >= 0)
                    continue;
                var shape = opponentRig.shapes[index];
                Vector3 p = shape.ClosestPoint(end);
                float d = (p - end).sqrMagnitude;
                if (d < distance)
                {
                    distance = d;
                    closest = p;
                }
            }

            Vector3 delta = anim.model.InverseTransformVector(closest - end);
            targetDistance = delta.magnitude;
            if (pushing)
            {
                // Fine-tunes target position using residual gap between accepted forelegs and opponent contact surface, closing the tibia gap.
                // Only adjusts IK intent; HasMotionContact certifies hits using original colliders and tolerances.
                Vector3 contactGap = Vector3.zero;
                float nearestGap = .15f;
                var ownRig = rigs[attacker];
                for (int i = 0; i < ownRig.shapes.Length; i++)
                {
                    if (ownRig.LegIndex(i) != slot)
                        continue;
                    var ownShape = ownRig.shapes[i];
                    foreach (var otherShape in opponentRig.shapes)
                    {
                        Vector3 otherPoint = otherShape.ClosestPoint(ownShape.bounds.center);
                        Vector3 gap = otherPoint - ownShape.ClosestPoint(otherPoint);
                        if (gap.magnitude >= nearestGap)
                            continue;
                        nearestGap = gap.magnitude;
                        contactGap = gap;
                    }
                }
                delta += anim.model.InverseTransformVector(contactGap) * 2;
            }
            // Press target slightly into contact surface to overcome tracking lag; bone lengths and collision still constrain the final pose.
            return Vector3.ClampMagnitude(delta + delta.normalized * (pushing ? .075f : .035f), .55f);
        }

        void UpdateRightingSupport(int index, BeetleSimulation sim, float dt)
        {
            var b = sim.Fighters[index];
            var anim = actors[index].animator;
            var rig = rigs[index];
            Vector3 normal = b.HasFallen ? Vector3.up : b.SurfaceNormal;
            float up = Vector3.Dot(anim.bodyPivot.up, normal);
            b.RightingTorque = Vector3.zero;
            b.RightingSupports = 0;
            b.RightingGrounded = false;
            b.RightingStable = false;
            if (!b.IsOverturned && !anim.IsRighting && up > .7f)
            {
                b.RightingStableSeconds = 0;
                return;
            }

            Vector3 center = anim.bodyPivot.position, pivot = Vector3.zero;
            int bodyContacts = 0, contacts = 0;
            Vector3 allContacts = Vector3.zero;
            var points = new Vector3[7];
            var forces = new float[7];
            for (int i = 0; i < rig.shapes.Length; i++)
            {
                var shape = rig.shapes[i];
                var bounds = shape.bounds;
                var flat = new Vector2(bounds.center.x, bounds.center.z);
                float height;
                Vector3 surfaceNormal;
                if (!b.HasFallen && sim.SampleGround(flat, out var surface))
                {
                    height = surface.height;
                    surfaceNormal = surface.normal;
                }
                else if (b.HasFallen)
                {
                    height = sim.Settings.groundHeight;
                    surfaceNormal = Vector3.up;
                }
                else
                    continue;
                if (bounds.min.y > height + .045f)
                    continue;
                // Probe the lower envelope, not the capsule's centre projection: a long
                // angled tarsus often touches at its tip while its centre is well above ground.
                Vector3 point = shape.ClosestPoint(new Vector3(flat.x, height - 100 * (bounds.size.magnitude + 1), flat.y));
                if (!b.HasFallen)
                {
                    if (!sim.SampleGround(new Vector2(point.x, point.z), out var exact))
                        continue;
                    height = exact.height;
                    surfaceNormal = exact.normal;
                }

                if (Mathf.Abs(point.y - height) > .045f || surfaceNormal.y < .5f)
                    continue;
                contacts++;
                allContacts += point;
                if (rig.Role(i) == BeetleContactRole.Body && !rig.IsHorn(i))
                {
                    pivot += point;
                    bodyContacts++;
                }

                int slot = rig.LegIndex(i);
                if (slot < 0 && rig.IsHorn(i))
                    slot = 6;
                if (slot < 0)
                    continue;
                // A leg/horn can contribute only when its actual accepted collider touches.
                // One force per limb, independent of its number of collision capsules.
                float pulse = .5f + .5f * Mathf.Sin(sim.MotionTime * 10 + slot * 1.1f);
                float force = (slot == 6 ? 4 : 5) * b.Traits.strength * Mathf.Lerp(.35f, 1, b.Stamina01) * pulse;
                if (force > forces[slot])
                {
                    forces[slot] = force;
                    points[slot] = point;
                }
            }

            b.RightingGrounded = contacts > 0;
            if (contacts == 0)
            {
                b.RightingStableSeconds = 0;
                return;
            }

            pivot = bodyContacts > 0 ? pivot / bodyContacts : allContacts / contacts;
            Vector3 wanted = Vector3.Cross(anim.bodyPivot.up, normal);
            if (wanted.sqrMagnitude < .01f)
                wanted = anim.model.forward * b.CircleDirection;
            wanted.Normalize();
            Vector3 torque = Vector3.Cross(center - pivot, -normal * (b.Traits.mass * sim.Settings.gravity));
            for (int i = 0; i < 7; i++)
                if (forces[i] > 0)
                {
                    b.RightingSupports++;
                    Vector3 push = Vector3.Cross(points[i] - pivot, normal * forces[i]);
                    if (Vector3.Dot(push, wanted) > 0)
                        torque += push;
                }

            // Damped torque-driven articulation is a gameplay approximation, not a full rigid-body solver.
            // Gravity can settle an unstable pose; no contact means no active righting torque.
            b.RightingTorque = Vector3.ProjectOnPlane(torque, normal);
            b.RightingStableSeconds = up > .9f && b.RightingSupports >= 2 ? b.RightingStableSeconds + dt : 0;
            b.RightingStable = b.RightingStableSeconds >= .2f;
            if (b.RightingStable && anim.IsRighting)
            {
                // Transfer the recovered body's heading to the locomotion root without
                // rotating the physical body. Avoid snapping its residual flight yaw away.
                Vector3 facing = Vector3.ProjectOnPlane(anim.bodyPivot.forward, normal);
                if (facing.sqrMagnitude > .001f)
                {
                    Quaternion bodyRotation = anim.bodyPivot.rotation;
                    Vector3 bodyPosition = anim.bodyPivot.position;
                    actors[index].transform.rotation = Quaternion.LookRotation(facing, normal);
                    anim.bodyPivot.SetPositionAndRotation(bodyPosition, bodyRotation);
                    Vector3 flatForward = Quaternion.Inverse(Quaternion.FromToRotation(Vector3.up, normal)) * facing;
                    b.Heading = Mathf.Atan2(flatForward.x, flatForward.z) * Mathf.Rad2Deg;
                }
            }

            if (up < .35f && !b.Lift.Supported)
                b.IsOverturned = true;
        }

        public bool IsRighting(int fighter) => actors[fighter].animator.IsRighting;
        public bool ForebodyRaised(int attacker) => Mathf.DeltaAngle(0, actors[attacker].animator.bodyPivot.localEulerAngles.x) < -8;
        public float RearSupport(int attacker, BeetleSimulation sim)
        {
            var state = sim.Fighters[attacker];
            if (state.Airborne || state.HasFallen)
                return 0;
            var anim = actors[attacker].animator;
            float Contact(int slot)
            {
                var leg = anim.legs[slot];
                Vector3 tip = anim.importedRig ? anim.importedRig.bindings[slot].tip.position : leg.lastFoot;
                if (!sim.SampleGround(new Vector2(tip.x, tip.z), out var hit))
                    return 0;
                float scale = Mathf.Abs(anim.model.lossyScale.x);
                float sole = (leg.soleOffset + (anim.importedRig ? anim.importedRig.soleClearance : 0)) * scale;
                float gap = tip.y - hit.height - sole;
                return Mathf.InverseLerp(.12f * scale, .035f * scale, gap) * Mathf.Clamp01(hit.normal.y);
            }

            float left = Contact(2), right = Contact(5);
            // One rear grip can contribute a limited push; two secure contacts support full drive.
            return Mathf.Max(left, right) * Mathf.Lerp(.55f, 1, Mathf.Min(left, right));
        }

        bool MotionAccepted()
        {
            if (separationRemaining > .001f)
                return false;
            for (int i = 0; i < 2; i++)
                if (Vector3.Distance(motion[i].Start, actors[i].transform.position) > motionBudget[i] + .0001f)
                    return false;
            return true;
        }

        static void UpdateFootSupport(BeetleActor actor, BeetleSimulation sim, float dt)
        {
            var state = actor.State;
            var anim = actor.animator;
            if (!state.Airborne || state.HasFallen)
            {
                state.Lift.UnsupportedTime = 0;
                state.Lift.GripReleased = false;
                return;
            }

            bool free = true;
            if (anim.importedRig)
                for (int i = 0; i < anim.legs.Length; i++)
                {
                    Vector3 foot = anim.importedRig.bindings[i].tip.position;
                    float sole = (anim.legs[i].soleOffset + anim.importedRig.soleClearance) * Mathf.Abs(anim.model.lossyScale.x);
                    if (sim.SampleGround(new Vector2(foot.x, foot.z), out var hit) && foot.y - hit.height - sole < .045f)
                    {
                        free = false;
                        break;
                    }
                }
            else
                free = state.Height - state.SurfaceHeight > .14f;
            if (free)
                state.Lift.GripReleased = true;
            state.Lift.UnsupportedTime = state.Lift.GripReleased ? state.Lift.UnsupportedTime + dt : 0;
        }

        // Re-constrains model positions after render interpolation, covering potential inter-frame overlaps between two safe endpoints.
        // Does not mutate simulation velocity or write visual positions back to state; Actor is reset to simulation data on next step.
        public void ProjectView(BeetleSimulation sim, float dt = BeetleSimulation.FixedStep)
        {
            // Starts from the safe pose of the previous display frame. Rolling back fixed-step alpha would cause visual snapping; advancing continuously
            // from the last display pose bounds movement by frame dt. Display cache never participates in authoritative simulation.
            foreach (var m in viewMotion)
                m.Propose();
            bool accepted = false;
            float visibleProgress = 0;
            for (int attempt = 0; attempt <= 8; attempt++)
            {
                if (attempt > 0)
                    foreach (var m in viewMotion)
                        m.Apply(Mathf.Pow(.5f, attempt - 1));
                // Presentation layer requires surface-tangent decoupling paths as well, preventing visual pose stall while simulation has resumed.
                Physics.SyncTransforms();
                Cache();
                Constrain(sim, false, attempt > 0);
                bool safe = separationRemaining <= .001f;
                for (int i = 0; i < 2; i++)
                {
                    var state = sim.Fighters[i];
                    float speed = new Vector3(state.Velocity.x, state.Airborne ? state.VerticalVelocity : 0, state.Velocity.y).magnitude;
                    float budget = Mathf.Max(sim.Settings.contactMotionSpeed, speed + .5f) * Mathf.Max(0, dt) +
                        Mathf.Min(.12f, viewBlockedSeconds * .08f);
                    safe &= Vector3.Distance(viewMotion[i].Start, actors[i].transform.position) <= budget + .0001f;
                }

                if (safe)
                {
                    accepted = true;
                    visibleProgress = attempt == 0 ? 1 : Mathf.Pow(.5f, attempt - 1);
                    break;
                }
            }

            if (!accepted)
                foreach (var m in viewMotion)
                    m.Apply(0);
            // Tiny accepted fractions can still freeze the visible tumble while simulation recovers.
            // Accumulate stalled progress rather than resetting recovery on a 1/128 step.
            viewBlockedSeconds = accepted && visibleProgress >= .25f ? Mathf.Max(0, viewBlockedSeconds - dt * 2) : viewBlockedSeconds + dt;
            Physics.SyncTransforms();
            Cache();
            foreach (var m in viewMotion)
                m.Begin();
            foreach (var actor in actors)
                actor.UpdateMarker();
        }

        // Alternates up to eight iterations between beetle-beetle and environment collision; environment passes may push beetles back into each other.
        // Exits early when environment stops moving the model to avoid exhausting iteration budget unnecessarily.
        bool Constrain(BeetleSimulation sim, bool changeVelocity, bool useContactNormals = true)
        {
            recordingContacts = changeVelocity;
            if (changeVelocity)
            {
                acceptedHornContacts = 0;
                acceptedForelegContacts = 0;
            }

            bool contact = false;
            for (int pass = 0; pass < 8; pass++)
            {
                contact |= Separate(sim, changeVelocity, useContactNormals);
                bool arenaMoved = false;
                foreach (var rig in rigs)
                    arenaMoved |= ResolveArena(rig);
                if (!arenaMoved && separationRemaining <= .0005f)
                    break;
            }

            // The final environment pass might squeeze bodies back into opponent; cannot reuse residual values prior to Separate.
            // Final acceptance requires passing both ring and inter-fighter checks, preventing unsafe poses from being saved as rollback baselines.
            separationRemaining = MeasurePenetration(false);
            recordingContacts = false;
            return contact;
        }

        void RecordPartContact(int a, int b)
        {
            if (!recordingContacts)
                return;
            if (rigs[0].Role(a) == BeetleContactRole.HeadHorn)
                acceptedHornContacts |= 1;
            if (rigs[1].Role(b) == BeetleContactRole.HeadHorn)
                acceptedHornContacts |= 2;
            int left = rigs[0].LegIndex(a), right = rigs[1].LegIndex(b);
            if (left == 0 || left == 3)
                acceptedForelegContacts |= 1 << (left / 3);
            if (right == 0 || right == 3)
                acceptedForelegContacts |= 1 << (2 + right / 3);
        }

        // Updates component and whole-beetle world AABBs from current pose for broadphase culling.
        void Cache()
        {
            foreach (var rig in rigs)
                rig.CacheBounds();
        }

        // Separates along horizontal axis connecting centers for up to 24 iterations. Restricts correction to horizontal to prevent horn normals
        // from unintentionally launching both into the air; true vertical lifts are explicitly driven by combat rules.
        bool Separate(BeetleSimulation sim, bool changeVelocity, bool useContactNormals)
        {
            // Resolves overlaps along true 3D contact normals during lifts. Legacy horizontal separation projected upward horn contacts
            // into backward translation, bouncing fighters sideways even during upward velocity.
            if (hornSupport.Active(sim))
                return SeparateSupported(sim);
            // Retains 3D contact response after horn release until posture recovers; must not abruptly snap back to planar pushing on release frame.
            if (hornSupport.Attacker >= 0 && Mathf.Abs(sim.Fighters[hornSupport.Defender].Lift.Pitch) > .01f)
            {
                if (sim.Fighters[hornSupport.Defender].Airborne)
                    return SeparateSupported(sim);
                bool recoveryContact = SeparateAlongContacts(sim, changeVelocity, hornSupport.Attacker);
                if (separationRemaining > .001f)
                    recoveryContact |= SeparateCentered(sim, changeVelocity);
                return recoveryContact;
            }

            bool insertingA = Inserting(sim.Fighters[0]);
            bool insertingB = Inserting(sim.Fighters[1]);
            if (insertingA || insertingB || useContactNormals)
            {
                bool insertionContact = SeparateAlongContacts(sim, changeVelocity, insertingA || insertingB ? (insertingA ? 0 : 1) : -1);
                // Interlocking legs can cause per-contact projections to cancel out. Reverts to conservative separation only when residual penetration exceeds tolerance.
                if (separationRemaining > .001f)
                    insertionContact |= SeparateCentered(sim, changeVelocity);
                return insertionContact;
            }

            return SeparateCentered(sim, changeVelocity);
        }

        bool SeparateCentered(BeetleSimulation sim, bool changeVelocity)
        {
            bool contact = false;
            Vector3 axis = rigs[0].transform.position - rigs[1].transform.position;
            axis.y = 0;
            axis = axis.sqrMagnitude > .000001f ? axis.normalized : Vector3.right;
            // Displacement weight for A is B.mass / (A.mass + B.mass): heavier A incurs a smaller fraction of the separation displacement.
            float aWeight = sim.Fighters[1].Traits.mass / (sim.Fighters[0].Traits.mass + sim.Fighters[1].Traits.mass);
            for (int iteration = 0; iteration < 24; iteration++)
            {
                float push = RequiredSeparation(axis, out float depth);
                // Halts below tolerance; clamps single-pass correction magnitude to mitigate violent bounce on complex multi-contact manifolds.
                if (depth <= .0005f)
                    break;
                contact = true;
                // Along the centre line keeps horns from lifting both beetles into
                // the air. Actual lifting remains the explicitly simulated action.
                push = Mathf.Clamp(push + skin, .005f, .3f);
                rigs[0].Translate(axis * push * aWeight);
                rigs[1].Translate(-axis * push * (1 - aWeight));
            }

            RequiredSeparation(axis, out float residual);
            separationRemaining = residual;
            MaximumResidual = Mathf.Max(MaximumResidual, residual);
            // Removes relative closing velocity along separation axis during simulation; tangential velocity is preserved.
            // Only eliminates interpenetration; offensive push impulse is computed separately by Simulation.ApplyHit.
            if (contact && changeVelocity)
            {
                Vector2 n = new Vector2(axis.x, axis.z);
                float closing = Vector2.Dot(sim.Fighters[0].Velocity - sim.Fighters[1].Velocity, n);
                if (closing < 0)
                {
                    sim.Fighters[0].Velocity -= n * closing * aWeight;
                    sim.Fighters[1].Velocity += n * closing * (1 - aWeight);
                }
            }

            return contact;
        }

        static bool Inserting(BeetleState state) => state.Action == BeetleAction.PryWindup || state.Action == BeetleAction.Pry;
        // Head/leg local retreat remains permitted during lift and release. Enabling it only on engagement caused interlocked horns on uneven terrain,
        // leaving defender unable to lift safely on any step and locking the beetle rigid until lift expired.
        static bool ArticulatedContact(BeetleState state) => Inserting(state) || state.Action == BeetleAction.Lifting ||
            state.Action == BeetleAction.ReleaseLift || state.Action == BeetleAction.Disengage || state.ContactBlockedSeconds >= .3f;
        void YieldArticulatedContacts(BeetleSimulation sim)
        {
            // Executes one round of local retreat per step, followed by re-syncing capsule colliders; never disables or ignores collision.
            var a = rigs[0];
            var b = rigs[1];
            int changedLegsA = 0, changedLegsB = 0;
            bool headA = false, headB = false, changed = false;
            CollectCandidates(a, b, out int countA, out int countB);
            for (int ai = 0; ai < countA; ai++)
                for (int bi = 0; bi < countB; bi++)
                {
                    int i = candidatesA[ai], j = candidatesB[bi];
                    if (!a.bounds[i].Intersects(b.bounds[j]))
                        continue;
                    var ca = a.shapes[i];
                    var cb = b.shapes[j];
                    if (!Physics.ComputePenetration(ca, ca.transform.position, ca.transform.rotation, cb,
                        cb.transform.position, cb.transform.rotation, out var n, out float d) || d < .0005f)
                        continue;
                    var ra = a.Role(i);
                    var rb = b.Role(j);
                    if (ra == BeetleContactRole.HeadHorn && rb == BeetleContactRole.HeadHorn)
                    {
                        if (n.y < -.25f && !headB)
                        {
                            actors[1].animator.YieldHead(Inserting(sim.Fighters[0]) ? Mathf.Lerp(10, 4, sim.Fighters[1].Balance) : 4);
                            headB = true;
                            changed = true;
                        }
                        else if (n.y > .25f && !headA)
                        {
                            actors[0].animator.YieldHead(Inserting(sim.Fighters[1]) ? Mathf.Lerp(10, 4, sim.Fighters[0].Balance) : 4);
                            headA = true;
                            changed = true;
                        }
                    }

                    int la = a.LegIndex(i), lb = b.LegIndex(j);
                    if (la >= 0 && (changedLegsA & (1 << la)) == 0)
                    {
                        actors[0].animator.YieldLeg(la, n, sim.Fighters[0]);
                        changedLegsA |= 1 << la;
                        changed = true;
                    }

                    if (lb >= 0 && (changedLegsB & (1 << lb)) == 0)
                    {
                        actors[1].animator.YieldLeg(lb, -n, sim.Fighters[1]);
                        changedLegsB |= 1 << lb;
                        changed = true;
                    }
                }

            if (changed)
            {
                Physics.SyncTransforms();
                Cache();
            }
        }

        // Ground engagement phase: slides laterally along horizontal projection of true normal, allowing horns to pass around obstacles.
        // Legacy centroid-axis projection lost lateral glide, magnifying near-vertical contacts into full-body backward retreat.
        // attacker = -1 represents blocked retreat for generic actions, split proportionally by mass without assigning precedence.
        bool SeparateAlongContacts(BeetleSimulation sim, bool changeVelocity, int attacker)
        {
            bool contact = false;
            var a = rigs[0];
            var b = rigs[1];
            float weight = attacker < 0 ? sim.Fighters[1].Traits.mass / (sim.Fighters[0].Traits.mass +
                sim.Fighters[1].Traits.mass) : attacker == 0 ? .75f : .25f;
            for (int pass = 0; pass < 32; pass++)
            {
                float deepest = 0;
                Vector3 n = Vector3.zero;
                CollectCandidates(a, b, out int countA, out int countB);
                for (int ai = 0; ai < countA; ai++)
                    for (int bi = 0; bi < countB; bi++)
                    {
                        int i = candidatesA[ai], j = candidatesB[bi];
                        if (!a.bounds[i].Intersects(b.bounds[j]))
                            continue;
                        var ca = a.shapes[i];
                        var cb = b.shapes[j];
                        if (Penetrates(a, i, b, j, out var normal, out float depth) && depth >
                            deepest)
                        {
                            RecordPartContact(i, j);
                            deepest = depth;
                            n = normal;
                            separationA = ca;
                            separationB = cb;
                            LastSeparationNormal = normal;
#if UNITY_EDITOR
                            BeetleDebugHooks.Pair(this, ca, cb, normal, depth, "normal-separation");
#endif
                        }
                    }

                if (deepest < .0005f)
                    break;
                contact = true;
                Vector3 horizontal = new Vector3(n.x, 0, n.z);
                float projection = horizontal.magnitude;
                if (projection < .001f)
                {
                    horizontal = Vector3.ProjectOnPlane(a.transform.position - b.transform.position, Vector3.up);
                    horizontal = horizontal.sqrMagnitude > 1e-8f ? horizontal.normalized : Vector3.right;
                }
                else
                    horizontal /= projection;
                Vector3 shift = horizontal * Mathf.Min(.08f, (deepest + skin) / Mathf.Max(.2f, projection));
                a.Translate(shift * weight);
                b.Translate(-shift * (1 - weight));
                if (changeVelocity)
                {
                    Vector2 axis = new Vector2(horizontal.x, horizontal.z);
                    float closing = Vector2.Dot(sim.Fighters[0].Velocity - sim.Fighters[1].Velocity, axis);
                    if (closing < 0)
                    {
                        sim.Fighters[0].Velocity -= axis * closing * weight;
                        sim.Fighters[1].Velocity += axis * closing * (1 - weight);
                    }
                }
            }

            RequiredSeparation(Vector3.right, out float residual);
            separationRemaining = residual;
            MaximumResidual = Mathf.Max(MaximumResidual, residual);
            return contact;
        }

        bool SeparateSupported(BeetleSimulation sim)
        {
            var a = rigs[hornSupport.Attacker];
            var b = rigs[hornSupport.Defender];
            bool contact = false;
            for (int pass = 0; pass < 32; pass++)
            {
                float deepest = 0;
                Vector3 correction = Vector3.zero;
                CollectCandidates(a, b, out int countA, out int countB);
                for (int ai = 0; ai < countA; ai++)
                    for (int bi = 0; bi < countB; bi++)
                    {
                        int i = candidatesA[ai], j = candidatesB[bi];
                        if (!a.bounds[i].Intersects(b.bounds[j]))
                            continue;
                        var ca = a.shapes[i];
                        var cb = b.shapes[j];
                        if (Penetrates(b, j, a, i, out var normal, out float depth) && depth >
                            deepest)
                        {
                            deepest = depth;
                            correction = normal * (depth + skin);
#if UNITY_EDITOR
                            BeetleDebugHooks.Pair(this, cb, ca, normal, depth, "supported-separation");
#endif
                        }
                    }

                if (deepest < .0005f)
                    break;
                contact = true;
                // Attacker's six legs provide ground reaction force while defender responds along separation normal, without lifting attacker airborne.
                if (correction.y < 0 && b.transform.position.y < sim.Fighters[hornSupport.Defender].SurfaceHeight + .12f)
                {
                    // When defender remains grounded on the arena, downward-sloping horn normals must not push it into the floor only to bounce back.
                    // Attacker retreats horizontally to resolve these mutually canceling constraints.
                    Vector3 horizontal = new Vector3(correction.x, 0, correction.z);
                    float fraction = horizontal.magnitude / Mathf.Max(.00001f, correction.magnitude);
                    if (fraction > .05f)
                        a.Translate(-Vector3.ClampMagnitude(horizontal / Mathf.Max(.1f, fraction * fraction), .04f));
                    else
                    {
                        Vector3 away = Vector3.ProjectOnPlane(a.transform.position - b.transform.position, Vector3.up).normalized;
                        a.Translate(away * Mathf.Min(.04f, correction.magnitude));
                    }
                }
                else
                    b.Translate(Vector3.ClampMagnitude(correction, .08f));
            }

            RequiredSeparation(Vector3.up, out float residual);
            separationRemaining = residual;
            MaximumResidual = Mathf.Max(MaximumResidual, residual);
            return contact;
        }

        // Rebuild after every root correction. Preserve shape order and all narrow-phase
        // tests, but avoid the full limb x limb scan on every low-throw contact retry.
        void CollectCandidates(BeetleContactRig a, BeetleContactRig b, out int countA, out int countB)
        {
            countA = countB = 0;
            if (!a.worldBounds.Intersects(b.worldBounds)) return;
            for (int i = 0; i < a.shapes.Length; i++)
                if (a.bounds[i].Intersects(b.worldBounds)) candidatesA[countA++] = i;
            for (int i = 0; i < b.shapes.Length; i++)
                if (b.bounds[i].Intersects(a.worldBounds)) candidatesB[countB++] = i;
        }

        static bool Penetrates(BeetleContactRig a, int i, BeetleContactRig b, int j, out Vector3 normal, out float depth)
        {
            a.shapeTransforms[i].GetPositionAndRotation(out var positionA, out var rotationA);
            b.shapeTransforms[j].GetPositionAndRotation(out var positionB, out var rotationB);
            return Physics.ComputePenetration(a.shapes[i], positionA, rotationA, b.shapes[j], positionB, rotationB,
                out normal, out depth);
        }

        // Broadphase checks whole-beetle bounding boxes first, culls distant parts via part AABB, and evaluates exact convex collisions.
        // Culls distant parts via AABB broadphase, precisely testing only candidate proxy pairs.
        float RequiredSeparation(Vector3 axis, out float maximumDepth, bool describe = false)
        {
            float result = 0;
            maximumDepth = 0;
            var a = rigs[0];
            var b = rigs[1];
            CollectCandidates(a, b, out int countA, out int countB);
            for (int ai = 0; ai < countA; ai++)
                for (int bi = 0; bi < countB; bi++)
                {
                    int i = candidatesA[ai], j = candidatesB[bi];
                    if (!a.bounds[i].Intersects(b.bounds[j]))
                        continue;
                    var ca = a.shapes[i];
                    var cb = b.shapes[j];
                    // ComputePenetration returns separation normal and penetration depth without mutating collider transforms.
                    if (!Penetrates(a, i, b, j, out Vector3 normal, out float depth))
                        continue;
                    RecordPartContact(i, j);
#if UNITY_EDITOR
                    BeetleDebugHooks.Pair(this, ca, cb, normal, depth, "beetle-pair");
#endif
                    if (describe && depth > maximumDepth)
                    {
                        separationA = ca;
                        separationB = cb;
                        LastSeparationNormal = normal;
                    }

                    maximumDepth = Mathf.Max(maximumDepth, depth);
                    // Penetration depth is normal-aligned while solver translates along horizontal centroid axis; scales by dot product cosine.
                    // Denominator clamped to 0.15 minimum to prevent division explosion on near-orthogonal vectors; subsequent iterations resolve residual overlap.
                    result = Mathf.Max(result, depth / Mathf.Max(.15f, Mathf.Abs(Vector3.Dot(normal, axis))));
                }

            return result;
        }

        // Resolves deepest part-stump/ground overlap along contact normal up to 16 iterations per pass.
        // Prevents articulated joints from penetrating arena; ContactRig translates cached bounds synchronously with root translation.
        bool ResolveArena(BeetleContactRig rig)
        {
            bool moved = false;
            for (int iteration = 0; iteration < 16; iteration++)
            {
                Vector3 correction = Vector3.zero;
                float deepest = 0;
                foreach (var surface in arena)
                {
                    // Collider can be disabled or destroyed after this solver is constructed.
                    // Check once per surface, not again for every beetle part.
                    if (!surface || !surface.enabled || !surface.gameObject.activeInHierarchy)
                        continue;
                    for (int i = 0; i < rig.shapes.Length; i++)
                    {
                        if (!rig.bounds[i].Intersects(surface.bounds))
                            continue;
                        var c = rig.shapes[i];
                        rig.shapeTransforms[i].GetPositionAndRotation(out var position, out var rotation);
                        if (Physics.ComputePenetration(c, position, rotation, surface,
                            surface.transform.position, surface.transform.rotation, out Vector3 normal,
                            out float depth) && depth > deepest)
                        {
#if UNITY_EDITOR
                            BeetleDebugHooks.Pair(this, c, surface, normal, depth, "arena");
#endif
                            deepest = depth;
                            correction = normal * (depth + skin);
                            // Resolves uneven terrain tops vertically: lateral separation pushes thin antennae into adjacent triangles, canceling corrections.
                            // Scales separation by normal Y; steep side walls still push along raw normal to prevent climbing back onto ring crests.
                            if (surface is MeshCollider mesh && !mesh.convex && normal.y > .5f)
                                correction = Vector3.up * ((depth + skin) / normal.y);
                        }
                    }
                }

                if (deepest < .0005f)
                    break;
                rig.Translate(correction);
                moved = true;
            }

            return moved;
        }

        // Residual depth measurement used by validation tools: includes beetle-to-beetle and beetle-to-environment contacts.
        // Updates LastWorstContact to aid debugging; not an active continuous profiler.
        public float MeasurePenetration(bool describe = true)
        {
            Physics.SyncTransforms();
            Cache();
            RequiredSeparation(Vector3.right, out float depth, describe);
            // Game loop measures raw numerical penetration; detailed part names are generated on demand by validation tools to avoid diagnostic allocations.
            if (describe)
                LastWorstContact = "between beetles";
            foreach (var rig in rigs)
                foreach (var surface in arena)
                {
                    if (!surface || !surface.enabled || !surface.gameObject.activeInHierarchy)
                        continue;
                    for (int i = 0; i < rig.shapes.Length; i++)
                    {
                        if (!rig.bounds[i].Intersects(surface.bounds))
                            continue;
                        var shape = rig.shapes[i];
                        rig.shapeTransforms[i].GetPositionAndRotation(out var position, out var rotation);
                        if (Physics.ComputePenetration(shape, position, rotation,
                            surface, surface.transform.position, surface.transform.rotation, out _,
                            out float distance) && distance > depth)
                        {
                            depth = distance;
                            if (describe)
                                LastWorstContact = shape.name + " against " + surface.name;
                        }
                    }
                }

            return depth;
        }
    }
}
