using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BeetleArena;
using UnityEditor;
using UnityEngine;

/// <summary>Prying throw regression: physical horn engagement, defender ballistics, continuity, work integration, and presentation isolation.</summary>
public static class BeetleThrowValidation
{
    static void Require(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    [MenuItem("Tools/甲壳虫斗/验证/战斗/杠杆投掷", false, 510)]
    public static void Run() => RunCases(true);
    public static void LandingOnly() => RunCases(true, true);
    public static void SlidingBaseline() => SlidingCases(false);
    public static void Sliding() => SlidingCases(true);
    public static void LandingAndEdge()
    {
        Sliding();
        EdgeSlides();
    }

    public static void LandingRegression()
    {
        LandingAndEdge();
        LandingOnly();
        TableRighting();
        TableRighting(true);
        BeetleLiftCatchValidation.CatchSteps();
    }

    public static void EdgeSlides()
    {
        using var fixture = new BeetleValidationScene("EdgeSlideValidation");
        var match = fixture.Match;
        var report = new StringBuilder("CONTROLLED EDGE SLIDES: real support loss; no forced falling flags/rotation\n");
        Directory.CreateDirectory("Artifacts/LandingSlide/EdgeFrames");
        bool passed = true;
        for (int variant = 0; variant < 5; variant++)
        {
            match.SelectArena(variant < 3 ? 0 : variant - 1);
            match.StartMatch(71);
            var sim = match.Simulation;
            while (sim.Phase == MatchPhase.Countdown)
                match.AdvanceFixedStep();
            var a = sim.Fighters[0];
            var b = sim.Fighters[1];
            a.Position = new Vector2(-2.5f, -2);
            b.Position = new Vector2(4.1f, 0);
            b.Heading = variant == 0 ? 90 : variant == 1 ? -90 : 0;
            sim.SetGround(match.ActiveSurface);
            for (int i = 0; i < 2; i++)
                match.actors[i].Bind(sim.Fighters[i], fixture.Settings, match.ActiveSurface);
            match.Contacts.Solve(sim, 0);
            Require(match.Contacts.MeasurePenetration() < .003f, "Edge fixture starts in penetration");
            b.Velocity = new Vector2(8, 0);
            b.Action = BeetleAction.Stagger;
            b.ActionDuration = b.DecisionTime = 10;
            var anim = match.actors[1].animator;
            bool edge = false, floor = false;
            float rotation = 0, overlap = 0, reach = 0, legTravel = 0;
            int edgeTick = -1;
            Quaternion atEdge = anim.bodyPivot.rotation;
            Vector3 lastFoot = anim.bodyPivot.InverseTransformPoint(anim.importedRig.bindings[0].tip.position);
            for (int tick = 0; tick < 250; tick++)
            {
                a.Action = BeetleAction.Brace;
                a.ActionTime = 0;
                a.ActionDuration = a.DecisionTime = 10;
                match.AdvanceFixedStep();
                if (!edge && b.Fall.EdgeDrop)
                {
                    edge = true;
                    edgeTick = tick;
                    atEdge = anim.bodyPivot.rotation;
                }

                if (edge && !b.Fall.OnFloor)
                {
                    rotation = Mathf.Max(rotation, Quaternion.Angle(atEdge, anim.bodyPivot.rotation));
                    legTravel += Vector3.Distance(lastFoot,
                        anim.bodyPivot.InverseTransformPoint(anim.importedRig.bindings[0].tip.position));
                }

                lastFoot = anim.bodyPivot.InverseTransformPoint(anim.importedRig.bindings[0].tip.position);
                floor |= b.Fall.OnFloor;
                reach = Mathf.Max(reach, anim.EdgeReach);
                overlap = Mathf.Max(overlap, match.Contacts.MeasurePenetration());
                match.Present(1, sim.MotionTime, .02f);
                if (variant == 1 && tick % 3 == 0 && tick < 150)
                {
                    match.hud.gameObject.SetActive(false);
                    var camera = match.spectatorCamera.GetComponent<Camera>();
                    Vector3 focus = new Vector3(6.2f, -1.1f, 0);
                    camera.orthographicSize = 4.2f;
                    camera.transform.position = focus + new Vector3(6, 4, -8);
                    camera.transform.LookAt(focus);
                    BeetleLiftCatchValidation.Shot(match, $"Artifacts/LandingSlide/EdgeFrames/Edge-{tick:D3}.png");
                }

                if (floor && tick > edgeTick + 110)
                    break;
            }

            report.AppendLine($"case={variant}, edge={edge}, fallen={b.HasFallen}, floor={floor}, turn={rotation:F1}deg, reach={reach:F3}, forefootTravel={legTravel:F3}, overlap={overlap:F6}");
            passed &= edge && b.HasFallen && floor && rotation > 45 && reach > .2f && legTravel > .3f && overlap < .003f;
        }

        // Friction must not run without ground, even after a previous landing; repeated
        // contact cannot spend the initial impact impulse again, and slope friction dissipates.
        var data = new BeetleSimulation(fixture.Settings, 71);
        var state = data.Fighters[1];
        state.HasFallen = state.Airborne = true;
        state.Velocity = new Vector2(12, 0);
        state.VerticalVelocity = -8;
        data.ResolveLandingFriction(state, false, Vector3.up, .02f);
        Require(state.Velocity.x == 12, "Landing friction changed free flight");
        data.ResolveLandingFriction(state, true, Vector3.up, .02f);
        float first = state.Velocity.x;
        data.ResolveLandingFriction(state, false, Vector3.up, .02f);
        Require(state.Velocity.x == first, "Friction persisted after support was lost");
        // Compare an already-contacting control with zero incoming vertical speed. Both
        // must lose exactly the same sliding speed, regardless of the tuned friction gain.
        var control = data.Fighters[0];
        control.HasFallen = control.Airborne = control.LandingSliding = true;
        control.Velocity = state.Velocity;
        control.VerticalVelocity = 0;
        Vector3 slopeNormal = new Vector3(.3f, .954f, 0).normalized;
        data.ResolveLandingFriction(state, true, slopeNormal, .02f);
        data.ResolveLandingFriction(control, true, slopeNormal, .02f);
        Require(state.Velocity.x < first && Vector2.Distance(state.Velocity, control.Velocity) < .00001f,
            "Repeated contact spent the impact impulse again");
        report.AppendLine("PASS no friction in flight/support gap; no repeated impact impulse; slope dissipates energy.");
        File.WriteAllText("Artifacts/LandingSlide/Edge.txt", report.ToString());
        Require(passed, report.ToString());
    }

    static void SlidingCases(bool verify)
    {
        using var fixture = new BeetleValidationScene("LandingSlideValidation");
        var match = fixture.Match;
        var report = new StringBuilder("CONTROLLED POST-RELEASE LANDING (not natural AI frequency evidence)\n");
        Directory.CreateDirectory("Artifacts/LandingSlide");
        bool passed = true;
        for (int variant = 0; variant < 6; variant++)
        {
            bool forest = variant >= 4;
            match.SelectArena(forest ? 0 : variant);
            match.StartMatch(71);
            var sim = match.Simulation;
            while (sim.Phase == MatchPhase.Countdown)
                match.AdvanceFixedStep();
            var a = sim.Fighters[0];
            var b = sim.Fighters[1];
            a.Position = new Vector2(0, -3.8f);
            b.Position = forest ? new Vector2(8, 0) : new Vector2(-2.2f, 0);
            sim.SetGround(match.ActiveSurface);
            for (int i = 0; i < 2; i++)
                match.actors[i].Bind(sim.Fighters[i], fixture.Settings, match.ActiveSurface);
            b.Action = BeetleAction.Launched;
            b.ActionDuration = 3;
            b.ActionTime = 0;
            b.Airborne = true;
            b.HasFallen = forest;
            b.Height = forest ? fixture.Settings.groundHeight + 4 : b.SurfaceHeight + 1.2f;
            b.VerticalVelocity = forest ? -2 : 5;
            b.Velocity = new Vector2(forest ? 12 : 2.7f, 0);
            b.Lift.LaunchVelocity = new Vector3(b.Velocity.x, b.VerticalVelocity, 0);
            b.Lift.Supporter = -1;
            if (forest)
                b.Fall.Begin(b);
            var anim = match.actors[1].animator;
            if (variant == 5)
                anim.bodyPivot.localRotation = Quaternion.Euler(180, 0, 0);
            match.actors[1].PoseSimulation(sim.MotionTime, 0);
            match.actors[1].CaptureSimulationPose();
            int contactTick = -1, stoppedTick = -1, slowTicks = 0;
            float travel = 0, maxOverlap = 0, firstSpeed = 0;
            Vector2 contactPosition = Vector2.zero, previous = b.Position;
            var csv = new StringBuilder("tick,x,z,height,speed,vy,floor,rightingGrounded,airborne\n");
            for (int tick = 0; tick < 400; tick++)
            {
                a.Action = BeetleAction.Brace;
                a.ActionTime = 0;
                a.ActionDuration = a.DecisionTime = 10;
                if (!b.Airborne && !b.HasFallen && !anim.IsRighting)
                {
                    b.Action = BeetleAction.Brace;
                    b.ActionTime = 0;
                    b.ActionDuration = b.DecisionTime = 10;
                }

                match.AdvanceFixedStep();
                if (contactTick < 0 && (b.Fall.OnFloor || b.RightingGrounded || !b.Airborne))
                {
                    contactTick = tick;
                    contactPosition = b.Position;
                    firstSpeed = b.Velocity.magnitude;
                }
                else if (contactTick >= 0)
                    travel += Vector2.Distance(previous, b.Position);
                previous = b.Position;
                maxOverlap = Mathf.Max(maxOverlap, match.Contacts.MeasurePenetration());
                csv.AppendLine($"{tick},{b.Position.x:F5},{b.Position.y:F5},{b.Height:F5},{b.Velocity.magnitude:F5},{b.VerticalVelocity:F5},{b.Fall.OnFloor},{b.RightingGrounded},{b.Airborne}");
                if (contactTick >= 0)
                {
                    slowTicks = b.Velocity.magnitude < .1f ? slowTicks + 1 : 0;
                    if (slowTicks >= 10 && stoppedTick < 0)
                        stoppedTick = tick - 9;
                    if (stoppedTick >= 0 && tick - contactTick >= 100)
                        break;
                }
            }

            float stopTime = stoppedTick < 0 ? -1 : (stoppedTick - contactTick) * .02f;
            report.AppendLine($"case={variant} arena={(forest ? "forest" : variant.ToString())}, contact={contactTick * .02f:F2}s x={contactPosition.x:F4}, firstSpeed={firstSpeed:F3}, travel={travel:F3}, stop={stopTime:F2}s, overlap={maxOverlap:F6}");
            File.WriteAllText($"Artifacts/LandingSlide/{(verify ? "After" : "Before")}-{variant}.csv", csv.ToString());
            passed &= contactTick >= 0 && stoppedTick >= 0 && stopTime <= .8f && travel <= 1.2f && maxOverlap < .003f;
        }

        File.WriteAllText($"Artifacts/LandingSlide/{(verify ? "After" : "Before")}.txt", report.ToString());
        if (verify)
            Require(passed, report.ToString());
    }

    public static void TableRighting(bool favorable = false)
    {
        using var fixture = new BeetleValidationScene("TableRightingValidation");
        var match = fixture.Match;
        match.SelectArena(0);
        match.StartMatch(71);
        var sim = match.Simulation;
        while (sim.Phase == MatchPhase.Countdown)
            match.AdvanceFixedStep();
        var a = sim.Fighters[0];
        var b = sim.Fighters[1];
        a.Position = new Vector2(0, -3.6f);
        b.Position = new Vector2(-2.2f, 0);
        b.Heading = -90;
        sim.SetGround(match.ActiveSurface);
        for (int i = 0; i < 2; i++)
            match.actors[i].Bind(sim.Fighters[i], fixture.Settings, match.ActiveSurface);
        // Controlled post-release state: real gravity, articulated collisions and ordinary AI.
        // Starting attitude is selected to exercise a back-down landing inside the arena.
        b.Action = BeetleAction.Launched;
        b.ActionTime = 0;
        b.ActionDuration = 3;
        b.Airborne = true;
        b.Height = 1.2f;
        b.VerticalVelocity = 8;
        b.Velocity = new Vector2(2.7f, 0);
        b.Lift.LaunchVelocity = new Vector3(2.7f, 8, 0);
        b.Lift.Direction = Vector2.right;
        b.Lift.Supporter = -1;
        var anim = match.actors[1].animator;
        anim.bodyPivot.localRotation = Quaternion.Euler(180, 0, 0);
        if (favorable)
        {
            // Separate supported-side fixture: already down on a banked flank, no
            // launch impulse or target upright pose. Recovery must come from contact.
            b.Action = BeetleAction.Stagger;
            b.IsOverturned = true;
            b.Position = new Vector2(0, 1.3f);
            b.Height = .25f;
            b.VerticalVelocity = 0;
            b.Velocity = Vector2.zero;
            b.Lift.LaunchVelocity = Vector3.zero;
            anim.bodyPivot.localRotation = Quaternion.Euler(0, 0, 60);
        }

        match.actors[1].PoseSimulation(sim.MotionTime, 0);
        match.actors[1].CaptureSimulationPose();
        bool landed = false, recovered = false;
        int earlyMoves = 0;
        float overlap = 0, landingUp = 1;
        string framesPath = favorable ? "Artifacts/TumbleLimbs/SupportedFrames" : "Artifacts/TumbleLimbs/TableFrames";
        string reportPath = favorable ? "Artifacts/TumbleLimbs/SupportedRighting.txt" : "Artifacts/TumbleLimbs/TableRighting.txt";
        Directory.CreateDirectory(framesPath);
        match.hud.gameObject.SetActive(false);
        for (int tick = 0; tick < 500; tick++)
        {
            a.Action = BeetleAction.Brace;
            a.ActionTime = 0;
            a.ActionDuration = 10;
            a.DecisionTime = 10;
            match.AdvanceFixedStep();
            match.Present(1, sim.MotionTime, .02f);
            if (!landed && !b.Airborne)
            {
                landed = true;
                landingUp = Vector3.Dot(anim.bodyPivot.up, Vector3.up);
            }

            if (landed && sim.Phase == MatchPhase.Fighting && anim.IsRighting && b.Action != BeetleAction.Stagger)
                earlyMoves++;
            if (landed && !anim.IsRighting && Vector3.Dot(anim.bodyPivot.up, Vector3.up) > .9f)
                recovered = true;
            overlap = Mathf.Max(overlap, match.Contacts.MeasurePenetration());
            if (tick % 5 == 0 && !recovered)
            {
                var camera = match.spectatorCamera.GetComponent<Camera>();
                var focus = anim.bodyPivot.position;
                camera.orthographicSize = 2.7f;
                camera.transform.position = focus + new Vector3(4, 4, -7);
                camera.transform.LookAt(focus);
                BeetleLiftCatchValidation.Shot(match, $"{framesPath}/Table-{tick:D3}.png");
            }

            if (recovered && b.Action != BeetleAction.Stagger)
                break;
            if (sim.Phase == MatchPhase.Finished)
                break;
        }

        bool failed = sim.ResultReason == BeetleResultReason.UnableToRight && sim.Winner == 0;
        string report =
            $"Controlled in-arena back landing: landed={landed}, landingUp={landingUp:F3}, back={anim.BackLandingDetected}, struggle={anim.BackStruggleSeconds:F2}s, recovered={recovered}, downSeconds={b.OverturnedSeconds:F3}, supports={b.RightingSupports}, failedRighting={failed}, fallen={b.HasFallen}, phase={sim.Phase}, action={b.Action}, earlyActions={earlyMoves}, overlap={overlap:F6}\n";
        File.WriteAllText(reportPath, report);
        Require(landed && (favorable ? recovered : anim.BackLandingDetected && failed) && !b.HasFallen && earlyMoves ==
            0 && overlap < .003f, report);
        if (!favorable)
            CheckRightingRules(fixture.Settings);
    }

    public static void RunCases(bool assertResults, bool landingOnly = false)
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Stop Play before validation.");
        using var fixture = new BeetleValidationScene("LeverThrowValidation");
        var report = new StringBuilder("LEVER THROW VALIDATION\n");
        Directory.CreateDirectory("Artifacts/LeverThrows");
        try
        {
            var match = fixture.Match;
            var settings = fixture.Settings;
            settings.throwInspectionMode = false; // Controlled tier inputs explicitly choose their burst probability.
            CheckMechanics(settings, report);
            float muscleForce = settings.throwMuscleForce;
            int throws = 0;
            var tiers = new System.Collections.Generic.HashSet<int>();
            // Recalibrates controlled inputs to cover six tiers following burst reserve integration; preserves gameplay thresholds and assertions.
            float[] forceScales = { .30f, .60f, 1f, 1.65f, .62f, 1f };
            for (int arena = 0; arena < 2; arena++)
                for (int variant = landingOnly ? 5 : 0; variant < 6; variant++)
                {
                    settings.tossChance = variant >= 4 ? 1 : 0;
        // Weak prying retains sufficient engagement capability while lowering head output; prevents degenerating into tests of failure to lift.
                    settings.throwMuscleForce = muscleForce * forceScales[variant];
                    match.SelectArena(arena);
                    match.StartMatch(71);
                    var sim = match.Simulation;
                    while (sim.Phase == MatchPhase.Countdown)
                        sim.Step(.02f);
                    var a = sim.Fighters[0];
                    var b = sim.Fighters[1];
                    a.Traits.strength = variant >= 4 ? 2.1f : 1;
                    a.Position = new Vector2(-2.05f, 0);
                    b.Position = new Vector2(2.05f, 0);
                    a.Heading = 90;
                    b.Heading = -90;
                    a.Action = BeetleAction.PryWindup;
                    a.ActionTime = 0;
                    a.ActionDuration = .55f;
                    b.Action = BeetleAction.Brace;
                    b.ActionDuration = 10;
                    b.Balance = .3f;
                    b.Stamina = 35;
                    sim.SetGround(match.ActiveSurface);
                    for (int i = 0; i < 2; i++)
                        match.actors[i].Bind(sim.Fighters[i], settings, match.ActiveSurface);
                    match.Contacts.Solve(sim, 0);
                    int launchTick = -1, supportedTicks = 0, upsideDownTicks = 0;
                    float flightRotation = 0;
                    Quaternion lastBody = match.actors[1].animator.bodyPivot.rotation;
                    float maxLandingTuck = 0, ascentTuck = 0;
                    float peak = 0, overlap = 0, maxStep = 0, worstBudgetExcess = 0, minRelative = 20;
                    var csv = new StringBuilder("t,action,support,x,y,work,impulse,contact,sweep,omega,anchor,acceptance,vx,vy\n");
                    for (int tick = 0; tick < (variant >= 3 ? 600 : 300); tick++)
                    {
                        if (launchTick >= 0)
                        {
                            if (a.Action != BeetleAction.Toss && a.Action != BeetleAction.ReleaseLift)
                            {
                                a.Action = BeetleAction.Brace;
                                a.ActionTime = 0;
                                a.ActionDuration = 10;
                            }

                            if (!b.Airborne && !b.HasFallen)
                            {
                                b.Action = BeetleAction.Brace;
                                b.ActionDuration = 10;
                                b.ActionTime = 0;
                            }
                        }

                        Vector3 old = new Vector3(b.Position.x, b.Height, b.Position.y);
                        foreach (var actor in match.actors)
                            actor.BeforeStep();
                        sim.Step(.02f);
                        if (b.Lift.Supported)
                            supportedTicks++;
                        if (a.Throws > 0 && launchTick < 0)
                        {
                            launchTick = tick;
                            throws++;
                            tiers.Add(BeetleThrowMechanics.TierNumber(b.Lift.ThrowKind));
                            Require(b.VerticalVelocity > 0, "Successful lift gently lowered instead of thrown");
                            Require(!b.HasFallen && sim.Phase == MatchPhase.Fighting, "Launch awarded premature victory");
                        }

                        Quaternion currentBody = match.actors[1].animator.bodyPivot.rotation;
                        if (launchTick >= 0 && b.Airborne && !b.Fall.OnFloor)
                        {
                            flightRotation += Quaternion.Angle(lastBody, currentBody);
                            if (Vector3.Dot(match.actors[1].animator.bodyPivot.up, Vector3.up) < -.35f)
                                upsideDownTicks++;
                        }

                        lastBody = currentBody;
                        var flightAnimator = match.actors[1].animator;
                        maxLandingTuck = Mathf.Max(maxLandingTuck, flightAnimator.LandingTuck);
                        if (launchTick >= 0 && b.VerticalVelocity > 1)
                            ascentTuck = Mathf.Max(ascentTuck, flightAnimator.LandingTuck);
                        float step = Vector3.Distance(old, new Vector3(b.Position.x, b.Height, b.Position.y));
                        float budget = match.Contacts.MotionBudget(1);
                        maxStep = Mathf.Max(maxStep, step);
                        if (!b.HasFallen)
                            worstBudgetExcess = Mathf.Max(worstBudgetExcess, step - budget);
                        peak = Mathf.Max(peak, b.Height - b.SurfaceHeight);
                        // Evaluates relative to attacker forward heading at release; cannot use Forward rotated by subsequent auto-tracking.
                        if (launchTick >= 0)
                            minRelative = Mathf.Min(minRelative, Vector2.Dot(b.Position - a.Position, Vector2.right));
                        match.spectatorCamera.Tick(sim, .02f);
                        if (b.Airborne && !b.HasFallen && !a.HasFallen)
                            foreach (var fighter in sim.Fighters)
                            {
                                var viewport =
                                    match.spectatorCamera.GetComponent<Camera>().WorldToViewportPoint(new Vector3(fighter.Position.x, fighter.Height + .65f, fighter.Position.y));
                                Require(viewport.x > 0 && viewport.x < 1 && viewport.y > 0 && viewport.y < 1,
                                    "Overview lost an airborne fighter");
                            }

                        float work = b.Lift.Work, contact = b.Lift.ContactSeconds;
                        foreach (float alpha in new[] { .25f, .5f, .75f })
                        {
                            foreach (var actor in match.actors)
                                actor.Render(alpha, sim.Elapsed, .02f / 3);
                            match.Contacts.ProjectView(sim, .02f / 3);
                            overlap = Mathf.Max(overlap, match.Contacts.MeasurePenetration());
                        }

                        Require(b.Lift.Work == work && b.Lift.ContactSeconds == contact, "Render modified throw integration");
                        csv.AppendLine($"{tick * .02f:F3},{b.Action},{b.Lift.Supported},{b.Position.x:F4},{b.Height:F4},{b.Lift.Work:F4},{b.Lift.Impulse:F4},{b.Lift.ContactSeconds:F3},{b.Lift.HeadSweep:F2},{b.Lift.AngularSpeed:F2},{match.Contacts.LiftAnchorError:F3},{match.Contacts.AcceptedMotionFraction:F4},{b.Velocity.x:F3},{b.VerticalVelocity:F3}");
                        if (launchTick >= 0 && new[] { 0, 10, 25, 40 }.Contains(tick - launchTick))
                            Capture(match, $"Throw-{arena}-{variant}-{tick - launchTick}.png");
                        if (landingOnly && arena == 0 && launchTick >= 0 && tick % 5 == 0)
                        {
                            Directory.CreateDirectory("Artifacts/TumbleLimbs/Frames");
                            match.hud.gameObject.SetActive(false);
                            var camera = match.spectatorCamera.GetComponent<Camera>();
                            Vector3 focus = match.actors[1].animator.bodyPivot.position;
                            camera.orthographicSize = 2.7f;
                            camera.transform.position = focus + new Vector3(5, 3, -7);
                            camera.transform.LookAt(focus);
                            BeetleLiftCatchValidation.Shot(match, $"Artifacts/TumbleLimbs/Frames/Flight-{tick:D3}.png");
                        }
                    }

                    report.AppendLine($"arena={arena}, strength={a.Traits.strength:F2}, support={supportedTicks * .02f:F2}s, throws={a.Throws}, kind={b.Lift.ThrowKind}, velocity={b.Lift.LaunchVelocity}, elevation={b.Lift.LaunchElevation:F1}, work={b.Lift.Work:F2}, impulse={b.Lift.Impulse:F2}, contact={b.Lift.ContactSeconds:F2}s, sweep={b.Lift.HeadSweep:F1}, peak={peak:F2}, behind={minRelative:F2}, spin={flightRotation:F1}deg upsideDownTicks={upsideDownTicks}, floorSeconds={b.Fall.GroundSeconds:F2} canonicalUp={Vector3.Dot(lastBody * Vector3.up, Vector3.up):F3} viewLag={Vector3.Distance(match.actors[1].transform.position, new Vector3(b.Position.x, b.Height, b.Position.y)):F3} upright={Vector3.Dot(match.actors[1].animator.bodyPivot.up, Vector3.up):F3}, step={maxStep:F3}, excess={worstBudgetExcess:F5}, overlap={overlap:F6}");
                    File.WriteAllText($"Artifacts/LeverThrows/Trajectory-{arena}-{variant}.csv", csv.ToString());
                    var landedAnimator = match.actors[1].animator;
                    report.AppendLine($"Flight stages: approachTuck={maxLandingTuck:F3}, ascentTuck={ascentTuck:F3}, backLanding={landedAnimator.BackLandingDetected}, backStruggle={landedAnimator.BackStruggleSeconds:F2}s");
                    if (assertResults && variant == 5)
                        Require(maxLandingTuck > .7f && ascentTuck < .05f, "Flight tucks too early or fails to prepare for landing");
                    if (assertResults)
                        Require(overlap <= .003f && worstBudgetExcess < .0002f, "Throw contact/continuity failed\n" + report);
                    if (assertResults && arena == 0)
                        Require(a.Throws > 0, "Controlled flat lift did not throw\n" + report);
                    if (assertResults && arena == 0)
                        Require(BeetleThrowMechanics.TierNumber(b.Lift.ThrowKind) == variant + 1,
                            "Unexpected throw tier for controlled fixture\n" + report);
                    if (assertResults && arena == 0 && variant == 5)
                    {
                        Require(minRelative < -.5f, "Strong throw did not pass behind attacker\n" + report);
                        Require(flightRotation > 180 && upsideDownTicks > 0, "Long heavy throw did not actually tumble\n" + report);
                        Require(b.Fall.OnFloor && (landedAnimator.IsRighting || Vector3.Dot(landedAnimator.bodyPivot.up,
                            Vector3.up) > .85f), "Heavy tumble neither settled nor continued grounded righting\n" +
                            report);
                    }
                }

            if (assertResults && !landingOnly)
                Require(throws >= 6 && tiers.Count == 6, "Missing one of six throw tiers\n" + report);
            CheckBodyRelativeLegs(match, report);
            report.AppendLine(assertResults ? "ALL LEVER THROW CHECKS PASSED" : "DIAGNOSTIC RUN COMPLETE (assertions not enforced for trajectories)");
        }
        finally
        {
            File.WriteAllText("Artifacts/LeverThrows/Validation.txt", report.ToString());
        }
    }

    static void CheckRightingRules(BeetleMatchSettings settings)
    {
        BeetleSimulation NewSim()
        {
            var sim = new BeetleSimulation(settings, 71);
            while (sim.Phase == MatchPhase.Countdown)
                sim.Step(.02f);
            foreach (var b in sim.Fighters)
            {
                b.Action = BeetleAction.Brace;
                b.ActionDuration = 100;
                b.DecisionTime = 100;
            }

            return sim;
        }

        var failure = NewSim();
        failure.Fighters[1].IsOverturned = true;
        for (int i = 0; i < 249; i++)
            failure.Step(.02f);
        Require(failure.Phase == MatchPhase.Fighting, "Righting defeat before five seconds");
        failure.Step(.02f);
        Require(failure.ResultReason == BeetleResultReason.UnableToRight && failure.Winner == 0, "Five-second righting defeat missing");
        var rescued = NewSim();
        rescued.Fighters[1].IsOverturned = true;
        for (int i = 0; i < 249; i++)
            rescued.Step(.02f);
        rescued.Fighters[1].RightingStable = true;
        rescued.Step(.02f);
        Require(rescued.Phase == MatchPhase.Fighting && rescued.Fighters[1].OverturnedSeconds == 0, "Stable support failed to reset timer");
        var both = NewSim();
        foreach (var b in both.Fighters)
            b.IsOverturned = true;
        for (int i = 0; i < 250; i++)
            both.Step(.02f);
        Require(both.ResultReason == BeetleResultReason.UnableToRight && both.Winner == -1, "Simultaneous righting defeat has order bias");
        var ring = NewSim();
        ring.Fighters[1].HasFallen = true;
        ring.Fighters[1].OverturnedSeconds = 5;
        for (int i = 0; i < 100 && ring.Phase != MatchPhase.Finished; i++)
            ring.Step(.02f);
        Require(ring.ResultReason == BeetleResultReason.RingOut, "Righting timer overrode ring-out");
        File.AppendAllText("Artifacts/TumbleLimbs/TableRighting.txt",
            "PASS five-second boundary, recovery reset, simultaneous failure draw, ring-out priority.\n");
    }

    static void CheckBodyRelativeLegs(BeetleMatchController match, StringBuilder report)
    {
        // Rigidly rotating a tucked beetle must rotate every joint and foot with it.
        // This detects upright-space clamps and foot frames even when bone lengths stay valid.
        var anim = match.actors[1].animator;
        var state = match.Simulation.Fighters[1];
        state.Airborne = true;
        state.HasFallen = false;
        var reference = new Vector3[18];
        float maxError = 0;
        for (int turn = 0; turn < 4; turn++)
        {
            anim.bodyPivot.localRotation = Quaternion.Euler(turn * 90, 0, turn * 20);
            for (int i = 0; i < 6; i++)
            {
                var leg = anim.legs[i];
                leg.tumbleFrameWeight = 1;
                leg.moduleOwned = false;
                Vector3 tucked = leg.restAnkle - anim.importedRig.bodyRest;
                tucked.x *= .86f;
                tucked.y += .16f;
                anim.importedRig.Solve(leg, anim.bodyPivot.TransformPoint(tucked), state, match.ActiveSurface);
                var binding = anim.importedRig.bindings[i];
                var points = new[] { binding.knee.position, binding.ankle.position, binding.tip.position };
                for (int j = 0; j < 3; j++)
                {
                    var local = anim.bodyPivot.InverseTransformPoint(points[j]);
                    if (turn == 0)
                        reference[i * 3 + j] = local;
                    else
                        maxError = Mathf.Max(maxError, Vector3.Distance(reference[i * 3 + j], local));
                }
            }
        }

        report.AppendLine($"Body-relative tucked legs: maximum rotation invariance error={maxError:F6}");
        Require(maxError < .002f, "Tumbling torso distorts tucked leg pose: " + maxError);
    }

    static void CheckMechanics(BeetleMatchSettings settings, StringBuilder report)
    {
        // Validates boundaries and adjacent zones across non-uniform tiers; directional gates cannot be bypassed by high speed alone.
        float[] cuts = { settings.throwQuickSpeed, settings.throwArcSpeed, settings.throwHighArcSpeed };
        for (int i = 0; i < cuts.Length; i++)
        {
            Require(BeetleThrowMechanics.TierNumber(BeetleThrowMechanics.Classify(settings, cuts[i] - .001f, false)) ==
                i + 1, "Lower tier boundary");
            Require(BeetleThrowMechanics.TierNumber(BeetleThrowMechanics.Classify(settings, cuts[i], false)) == i + 2,
                "Upper tier boundary");
        }

        Require(BeetleThrowMechanics.TierNumber(BeetleThrowMechanics.Classify(settings, settings.throwMaximumSpeed,
            false)) == 4, "Forward throw classified as overhead");
        Require(BeetleThrowMechanics.TierNumber(BeetleThrowMechanics.Classify(settings,
            settings.throwPowerOverheadSpeed - .001f, true)) == 5, "Tier 5 boundary");
        Require(BeetleThrowMechanics.TierNumber(BeetleThrowMechanics.Classify(settings,
            settings.throwPowerOverheadSpeed, true)) == 6, "Tier 6 boundary");
        report.AppendLine("PASS: all six tier boundaries and direction gates.");
        // Compare the exact same contact state with/without the explicit heavy-release gain.
        var probe = new BeetleSimulation(settings, 71).Fighters[1];
        probe.Traits.mass = 1;
        probe.Lift.Direction = Vector2.right;
        probe.Lift.ReleaseTangent = new Vector3(-.6f, .8f, 0);
        probe.Lift.HeadSweep = 100;
        probe.Lift.PeakAngularSpeed = 5;
        probe.Lift.Work = 65;
        probe.Lift.Impulse = 20;
        float gain = settings.throwHeavySpeedMultiplier;
        Vector3 baseLaunch, heavyLaunch;
        try
        {
            settings.throwHeavySpeedMultiplier = 1;
            baseLaunch = BeetleThrowMechanics.Release(settings, probe);
            settings.throwHeavySpeedMultiplier = gain;
            heavyLaunch = BeetleThrowMechanics.Release(settings, probe);
        }
        finally
        {
            settings.throwHeavySpeedMultiplier = gain;
        }

        Require(Mathf.Abs(heavyLaunch.magnitude / baseLaunch.magnitude - gain) < .001f, "Heavy launch gain not applied");
        Require(heavyLaunch.x < 0 && heavyLaunch.y > 0, "Heavy launch lost ballistic direction");
        Require(Mathf.Abs(heavyLaunch.magnitude - settings.throwMaximumSpeed * gain * 3) < .001f,
            "Tumbling heavy release is not exactly three times the preceding maximum speed");
        Require(!BeetleThrowMechanics.ShouldTumble(settings, new Vector3(0, 9, 0)) &&
            !BeetleThrowMechanics.ShouldTumble(settings, new Vector3(4, 4, 0)) &&
            BeetleThrowMechanics.ShouldTumble(settings, new Vector3(4.5f, 4.5f, 0)),
            "Tumble qualification lost its range gate");
        report.AppendLine($"PASS heavy release: same work/impulse speed {baseLaunch.magnitude:F2} -> {heavyLaunch.magnitude:F2}, gain={gain:F2}, velocity={heavyLaunch}.");
        float Sample(float strength, float mass, float duration, bool moving, float reserve = 1)
        {
            var sim = new BeetleSimulation(settings, 71);
            var a = sim.Fighters[0];
            var b = sim.Fighters[1];
            a.Traits.strength = strength;
            a.BurstReserve = reserve;
            b.Traits.mass = mass;
            b.Lift.Supporter = 0;
            b.Lift.Effort = 1;
            b.Lift.GripReleased = true;
            b.Lift.Direction = Vector2.right;
            for (int i = 0; i < Mathf.RoundToInt(duration / .02f); i++)
                BeetleThrowMechanics.Accumulate(settings, a, b, .02f, moving ? .04f : 0, 1, new Vector3(.5f, 1, 0), 1);
            if (!moving)
            {
                Require(b.Lift.Work == 0 && b.Lift.Impulse == 0, "Static contact created energy");
                return 0;
            }

            return BeetleThrowMechanics.Release(settings, b).magnitude;
        }

        float normal = Sample(1, 1, .5f, true), strong = Sample(1.7f, 1, .5f, true);
        float heavy = Sample(1, 1.5f, .5f, true), shortContact = Sample(1, 1, .2f, true);
        Sample(1, 1, 2, false);
        float depleted = Sample(1, 1, .5f, true, .15f);
        Require(depleted < normal, "Depleted burst reserve did not reduce throw speed");
        report.AppendLine($"PASS: burst reserve lowers throw speed, fresh={normal:F3}, depleted={depleted:F3}.");
        Require(strong > normal && heavy < normal && shortContact < normal, "Force/mass/contact monotonicity failed");
        report.AppendLine($"PASS mechanics: normal={normal:F3}, stronger={strong:F3}, heavier={heavy:F3}, shorter={shortContact:F3}; stationary contact adds no work/impulse.");
    }

    static void Capture(BeetleMatchController match, string filename)
    {
        typeof(BeetleArenaCollisionValidation).GetMethod("Capture", BindingFlags.Static |
            BindingFlags.NonPublic).Invoke(null, new object[] { match, filename });
        File.Copy("Artifacts/BeetleArena/" + filename, "Artifacts/LeverThrows/" + filename, true);
    }
}
