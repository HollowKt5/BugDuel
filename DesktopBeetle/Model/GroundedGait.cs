#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media.Media3D;

namespace BeetleDesktop.Native;

/// <summary>Read-only measurements of the articulated native renderer, in screen pixels unless stated otherwise.</summary>
public sealed class BeetleFootDiagnostics
{
    public int SupportCount { get; internal set; }
    public int StepCount { get; internal set; }
    public int AnimatedVertexCount { get; internal set; }
    public double GaitPhase { get; internal set; }
    public double TravelPixels { get; internal set; }
    public double MaximumTargetErrorPixels { get; internal set; }
    public double MaximumSupportDriftPixels { get; internal set; }
    public double MaximumJointLengthError { get; internal set; }
    public IReadOnlyList<Point3D> WorldFeet { get; internal set; } = Array.Empty<Point3D>();
    public IReadOnlyList<bool> Supporting { get; internal set; } = Array.Empty<bool>();
}

/// <summary>
/// World-locked contacts alternate in two tripods. Distance (including the arc swept by a turn)
/// initiates walking steps. At rest, each tripod can settle once into a neutral stance.
/// </summary>
internal sealed class GroundedGait
{
    sealed class Contact
    {
        public Point3D Anchor, From, To;
        public double FootHeading, FromHeading, ToHeading, Progress, Duration, Delay;
        public bool Swing;
    }
    // A compact beetle carries its trunk through a deliberate support stroke, rather
    // than constantly replacing its feet with tiny scuttling steps.
    const double HalfStride = .065;
    readonly ArticulatedLeg[] legs;
    readonly Contact[] contacts = Enumerable.Range(0, 6).Select(_ => new Contact()).ToArray();
    readonly Point3D[] debugFeet = new Point3D[6];
    readonly bool[] debugSupports = new bool[6];
    Point3D bodyWorld;
    Point? previousDesktop;
    double previousHeading, previousTravel = double.NaN, stepBudget = HalfStride, cycleTravel, stationaryTime;
    bool initialized, previouslyHeld;
    int nextTripod;
    public BeetleFootDiagnostics Diagnostics { get; } = new();
    public bool IsRestSettled { get; private set; }

    public GroundedGait(ArticulatedLeg[] legs)
    {
        this.legs = legs;
        Diagnostics.AnimatedVertexCount = legs.Sum(x => x.VertexCount);
        Diagnostics.WorldFeet = debugFeet;
        Diagnostics.Supporting = debugSupports;
    }

    Point3D World(Point3D p, double heading) => bodyWorld + LegMath.Rotate((Vector3D)p, -heading);
    Point3D Local(Point3D p, double heading) => (Point3D)LegMath.Rotate(p - bodyWorld, heading);
    static double Angle(double value) => (value % 360 + 540) % 360 - 180;

    void Reset(double heading, Matrix3D body)
    {
        for (int i = 0; i < legs.Length; i++)
        {
            Contact contact = contacts[i]; ArticulatedLeg leg = legs[i];
            contact.Anchor = World(leg.PlanReach(leg.NeutralTip, leg.FootOffset, body), heading);
            contact.FootHeading = heading; contact.Swing = false; contact.Progress = 0; contact.Delay = 0;
        }
        stepBudget = HalfStride;
        initialized = true;
    }

    public void Update(double speed, double dt, double heading, double turnRate, double travelDistance, Point? desktopPosition,
        double pixelsPerUnit, Matrix3D body, double clock, double held, double sleep, double groom, double fight, double eat, double startled, bool isHeld, double overturned, bool resting = false)
    {
        IsRestSettled = false;
        double headingDelta = initialized ? Angle(heading - previousHeading) : 0;
        Vector3D movement = new();
        if (initialized && dt > 0)
        {
            if (desktopPosition.HasValue && previousDesktop.HasValue)
                movement = new Vector3D((desktopPosition.Value.X - previousDesktop.Value.X) / pixelsPerUnit,
                    -(desktopPosition.Value.Y - previousDesktop.Value.Y) / pixelsPerUnit, 0);
            else
            {
                double distance = double.IsFinite(travelDistance) && double.IsFinite(previousTravel)
                    ? Math.Max(0, travelDistance - previousTravel) : Math.Abs(speed) * dt;
                movement = LegMath.Rotate(new Vector3D(0, distance / pixelsPerUnit, 0), -heading);
            }
        }
        previousDesktop = desktopPosition; previousTravel = travelDistance; previousHeading = heading;
        bodyWorld += movement;
        bool teleported = movement.Length > .65 || Math.Abs(headingDelta) > 80;
        if (!initialized || isHeld != previouslyHeld || teleported) Reset(heading, body);
        previouslyHeld = isHeld;
        bool locomotion = !isHeld && held < .15 && overturned < .15 && sleep < .5 && !teleported;
        double motion = locomotion ? movement.Length + Math.Abs(headingDelta) * Math.PI / 180 * .18 : 0;
        bool canSettle = resting && locomotion && motion < 1e-7
            && held < .01 && sleep < .01 && groom < .01 && fight < .01
            && eat < .01 && startled < .01 && overturned < .01;
        stationaryTime = canSettle ? stationaryTime + dt : 0;
        if (motion > 0)
        {
            stepBudget = Math.Min(HalfStride * 2, stepBudget + motion);
            cycleTravel += motion;
            Diagnostics.TravelPixels += movement.Length * pixelsPerUnit;
        }
        double motionSpeed = dt > 0 ? motion / dt : 0;
        Vector3D localVelocity = dt > 0 ? LegMath.Rotate(movement / dt, heading) : new Vector3D();
        // Finish the current step before arranging the feet for rest.
        for (int i = 0; i < contacts.Length; i++)
        {
            Contact contact = contacts[i];
            if (!contact.Swing) continue;
            double swingDt = dt;
            if (contact.Delay > 0)
            {
                swingDt = Math.Max(0, dt - contact.Delay);
                contact.Delay = Math.Max(0, contact.Delay - dt);
            }
            contact.Progress = Math.Min(1, contact.Progress + swingDt / contact.Duration);
            if (contact.Progress >= 1)
            {
                contact.Anchor = contact.To; contact.FootHeading = contact.ToHeading; contact.Swing = false;
            }
        }
        bool anySwing = contacts.Any(x => x.Swing);
        bool mustRecover = false;
        if (locomotion && motion > 1e-7 && !anySwing)
            for (int i = 0; i < legs.Length; i++)
            {
                Vector3D offset = LegMath.Rotate(legs[i].FootOffset, heading - contacts[i].FootHeading);
                if (legs[i].Tripod == nextTripod && legs[i].Reach(Local(contacts[i].Anchor, heading), offset, body) > .97)
                    mustRecover = true;
            }
        if (locomotion && motion > 1e-7 && !anySwing && (stepBudget >= HalfStride || mustRecover))
        {
            double halfCycle = HalfStride / Math.Max(.02, motionSpeed);
            double duration = Math.Clamp(halfCycle * .40, .105, .24);
            // Predict BOTH the travel during recovery and half of the next power
            // stroke. Ignoring recovery travel lands the middle foot behind the
            // body and forces rapid corrective steps even at a slow crawl.
            double leadTime = duration + Math.Max(0, halfCycle * 2 - duration) * .49;
            double turnLead = Math.Clamp(turnRate * leadTime * .65, -12, 12);
            Vector3D forwardLead = localVelocity * leadTime;
            if (forwardLead.Length > .082) forwardLead *= .082 / forwardLead.Length;
            for (int i = 0; i < legs.Length; i++)
            {
                ArticulatedLeg leg = legs[i]; Contact contact = contacts[i];
                if (leg.Tripod != nextTripod) continue;
                Point3D target = LegMath.Rotate(leg.NeutralTip, -turnLead) + forwardLead;
                // Preserve the imported beetle's compact lateral silhouette on a
                // tight turn; an outer recovery foot must not fan far sideways.
                target.X = leg.Side * Math.Clamp(target.X * leg.Side,
                    Math.Abs(leg.NeutralTip.X) * .72, Math.Abs(leg.NeutralTip.X) + .026);
                Vector3D offset = LegMath.Rotate(leg.FootOffset, -turnLead);
                target = leg.PlanReach(target, offset, body);
                contact.From = contact.Anchor; contact.To = World(target, heading);
                contact.FromHeading = contact.FootHeading; contact.ToHeading = heading + turnLead;
                // A small front-to-hind offset avoids three feet twitching in the
                // same rendered frame, while retaining the stable opposite tripod.
                contact.Delay = leg.Pair * .014 * Math.Clamp(.17 / Math.Max(.02, motionSpeed), .4, 1);
                contact.Duration = duration; contact.Progress = 0; contact.Swing = true;
            }
            Diagnostics.StepCount += 3;
            nextTripod = 1 - nextTripod;
            stepBudget = Math.Max(0, stepBudget - HalfStride);
        }

        if (stationaryTime >= .15 && !contacts.Any(x => x.Swing))
        {
            // A turn leaves world-locked feet at different angles and distances.
            // Do not freeze that support stroke for the entire three-minute rest:
            // lift and replace one tripod, keeping the opposite three feet planted.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                int tripod = (nextTripod + attempt) % 2;
                int adjusted = 0;
                for (int i = 0; i < legs.Length; i++)
                {
                    ArticulatedLeg leg = legs[i]; Contact contact = contacts[i];
                    if (leg.Tripod != tripod) continue;
                    Point3D target = World(leg.PlanReach(leg.NeutralTip, leg.FootOffset, body), heading);
                    if ((target - contact.Anchor).Length < .008
                        && Math.Abs(Angle(heading - contact.FootHeading)) < 3) continue;
                    contact.From = contact.Anchor; contact.To = target;
                    contact.FromHeading = contact.FootHeading; contact.ToHeading = heading;
                    contact.Delay = leg.Pair * .025;
                    contact.Duration = .28; contact.Progress = 0; contact.Swing = true;
                    adjusted++;
                }
                if (adjusted == 0) continue;
                Diagnostics.StepCount += adjusted;
                nextTripod = 1 - tripod;
                stepBudget = 0;
                break;
            }
        }

        // Evaluate after both tripods have had a chance to request a settling
        // step. A momentary gap between walking steps is not a settled pose.
        IsRestSettled = canSettle && stationaryTime >= .15 && !contacts.Any(x => x.Swing);
        Diagnostics.SupportCount = 0;
        Diagnostics.MaximumTargetErrorPixels = Diagnostics.MaximumSupportDriftPixels = Diagnostics.MaximumJointLengthError = 0;
        Diagnostics.GaitPhase = (cycleTravel / (HalfStride * 2)) % 1;
        for (int i = 0; i < legs.Length; i++)
        {
            ArticulatedLeg leg = legs[i]; Contact contact = contacts[i];
            Point3D worldTarget = contact.Anchor;
            double footHeading = contact.FootHeading;
            double swingLift = 0;
            bool recovering = contact.Swing && contact.Delay <= 0;
            if (recovering)
            {
                double eased = LegMath.Smooth(contact.Progress);
                worldTarget = contact.From + (contact.To - contact.From) * eased;
                // Lift, carry forward, and lower vertically at touchdown; the claws do not skate along the desktop.
                // Zero vertical velocity at lift-off and touchdown avoids a
                // sharp kick at the start/end of every tripod's recovery.
                double liftArc = Math.Sin(contact.Progress * Math.PI);
                swingLift = liftArc * liftArc * (leg.Pair == 0 ? .022 : .018);
                worldTarget.Z += swingLift;
                footHeading += Angle(contact.ToHeading - contact.FromHeading) * eased;
            }
            Point3D target = Local(worldTarget, heading);
            Vector3D footOffset = LegMath.Rotate(leg.FootOffset, heading - footHeading);
            // Lift the tarsus slightly during recovery while maintaining the original tarsus length/curvature.
            if (recovering)
            {
                var curl = Matrix3D.Identity;
                curl.Rotate(new Quaternion(new Vector3D(1, 0, 0), -leg.Pair * swingLift * 70));
                footOffset = curl.Transform(footOffset);
            }
            double air = Math.Clamp(Math.Max(held, overturned), 0, 1);
            if (sleep > .001)
            {
                Point3D folded = leg.NeutralTip + new Vector3D(-leg.Side * .020, (leg.Hip.Y - leg.NeutralTip.Y) * .07, .008);
                target += (folded - target) * sleep;
            }
            if (leg.Pair == 0)
            {
                if (groom > .001)
                {
                    double phase = clock * 6.5 + (leg.Side < 0 ? 0 : Math.PI);
                    Point3D cleaning = new(leg.Side * (.105 + Math.Sin(phase) * .012), .272 + Math.Cos(phase) * .018, -.033);
                    target += (cleaning - target) * groom;
                }
                target += new Vector3D(leg.Side * fight * .012, fight * (.012 + Math.Sin(clock * 7 + leg.Side) * .017),
                    fight * (.031 + Math.Sin(clock * 7 + leg.Side) * .010) + eat * (1 + Math.Sin(clock * 4 + leg.Side)) * .002);
            }
            target.Z += startled * .012;
            double kickPhase = clock * (8 - overturned * 3.5) + leg.Pair * 1.9 + (leg.Side < 0 ? .6 : 2.7);
            leg.Solve(target, footOffset, body, air, kickPhase);
            debugFeet[i] = World(leg.ActualTip, heading);
            bool supporting = !recovering && air < .1 && (leg.Pair != 0 || groom < .15 && fight < .15);
            debugSupports[i] = supporting;
            if (supporting)
            {
                Diagnostics.SupportCount++;
                if (sleep < .01 && eat < .01 && startled < .01)
                    Diagnostics.MaximumSupportDriftPixels = Math.Max(Diagnostics.MaximumSupportDriftPixels,
                        (debugFeet[i] - contact.Anchor).Length * pixelsPerUnit);
            }
            Diagnostics.MaximumTargetErrorPixels = Math.Max(Diagnostics.MaximumTargetErrorPixels, leg.TargetError * pixelsPerUnit);
            Diagnostics.MaximumJointLengthError = Math.Max(Diagnostics.MaximumJointLengthError, leg.LengthError);
        }
    }
}
