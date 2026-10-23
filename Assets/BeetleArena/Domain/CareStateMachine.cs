using System;

namespace BugDuel.Domain
{
    public enum CareState
    {
        Idle, Wandering, Inspecting, SeekingFood, Eating, Grooming,
        SeekingShelter, Resting, Startled, Recovering, Struggling, Growing, Dead, Unavailable
    }
    public enum CareGoal { None, Wander, Food, Shelter }

    /// <summary>Facts supplied by a host. No combat state, engine objects, wall clock or save IO.</summary>
    public struct CareObservation
    {
        public bool Dead, Overturned, Unavailable, Growing, Paused, FoodAvailable, AtGoal;
        public double Nutrition, HealthFraction, Hour, HabitatActivity, RestPreference;
    }

    /// <summary>One brain per individual. Deterministic private RNG never consumes battle/economy RNG.</summary>
    public sealed class CareStateMachine
    {
        public CareState State { get; private set; } = CareState.Idle;
        public CareGoal Goal { get; private set; }
        public double Elapsed { get; private set; }
        public double Fatigue { get; private set; }
        public int GoalVersion { get; private set; }
        public bool MealReady { get; private set; }
        public double MealProgress => State == CareState.Eating ? Math.Min(1, Elapsed / profile.eatSeconds) : 0;
        public CareBehaviorProfile Profile => profile;
        CareBehaviorProfile profile;
        uint random;
        double duration, calmSeconds, restCooldown;
        bool wasOverturned, startled;

        public CareStateMachine(CareBehaviorProfile profile, uint seed)
        {
            Configure(profile); random = seed == 0 ? 1u : seed;
            Enter(CareState.Idle);
        }
        public void Configure(CareBehaviorProfile value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            value.Validate(); profile = CareBehaviorProfile.Resolve(value);
        }
        // A resized/replaced enclosure may move a destination; eating/resting must require arrival again.
        public void InvalidateArrival()
        {
            if (State == CareState.Eating) Enter(CareState.SeekingFood);
            else if (State == CareState.Resting) Enter(CareState.SeekingShelter);
        }
        // A nearby interaction can startle a calm beetle, but repeated clicks cannot hold it forever.
        public void Disturb()
        {
            if (calmSeconds >= 4 && State != CareState.Dead && State != CareState.Struggling &&
                State != CareState.Recovering && State != CareState.Growing && State != CareState.Unavailable) startled = true;
        }

        public void Step(double seconds, CareObservation o)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || seconds > 1)
                throw new ArgumentOutOfRangeException(nameof(seconds), "Use a bounded simulation step (0..1s).");
            if (o.Paused) return;
            MealReady = false;
            if (o.Dead) { Force(CareState.Dead); return; }
            if (o.Overturned) { wasOverturned = true; Force(CareState.Struggling); Elapsed += seconds; return; }
            if (o.Unavailable) { Force(CareState.Unavailable); return; }
            if (o.Growing) { Force(CareState.Growing); return; }
            if (wasOverturned) { wasOverturned = false; Enter(CareState.Recovering); }
            else if (State == CareState.Dead || State == CareState.Unavailable || State == CareState.Growing)
                Enter(CareState.Idle);

            Elapsed += seconds; calmSeconds += seconds; restCooldown = Math.Max(0, restCooldown - seconds);
            Fatigue = Math.Max(0, Math.Min(1, Fatigue + seconds *
                (State == CareState.Resting ? -.09 : Goal != CareGoal.None ? .012 : -.002)));
            if (startled && State != CareState.Recovering)
            { startled = false; calmSeconds = 0; Enter(CareState.Startled); }
            if (State == CareState.Startled || State == CareState.Recovering)
            {
                if (Elapsed >= duration) Enter(CareState.Idle);
                return;
            }
            if (o.FoodAvailable)
            {
                if (State != CareState.SeekingFood && State != CareState.Eating) { Enter(CareState.SeekingFood); return; }
                if (State == CareState.SeekingFood && o.AtGoal) Enter(CareState.Eating);
                if (State == CareState.Eating) MealReady = Elapsed >= profile.eatSeconds;
                return;
            }
            if (State == CareState.Eating) { Enter(CareState.Grooming); return; }
            if (State == CareState.SeekingFood) { Enter(CareState.Inspecting); return; }
            if (restCooldown <= 0 && (o.HealthFraction < .35 || Fatigue > .8) &&
                State != CareState.SeekingShelter && State != CareState.Resting)
            { Enter(CareState.SeekingShelter); return; }
            switch (State)
            {
                case CareState.Idle:
                    if (Elapsed < duration) break;
                    double night = o.Hour >= 18 || o.Hour < 6 ? 1 : .55;
                    double active = profile.activity * Math.Max(.1, o.HabitatActivity) * (profile.nocturnal ? night : 1);
                    if (o.Nutrition < profile.hungerThreshold) Enter(CareState.Wandering);
                    else if (restCooldown <= 0 && Roll() < Math.Min(.8, (1 - active) * .65 + o.RestPreference * .2)) Enter(CareState.SeekingShelter);
                    else Enter(Roll() < active ? CareState.Wandering : CareState.Inspecting);
                    break;
                case CareState.Wandering:
                    if (o.AtGoal || Elapsed >= duration) Enter(CareState.Inspecting);
                    break;
                case CareState.Inspecting:
                    if (Elapsed >= duration) Enter(Roll() < .4 ? CareState.Grooming : CareState.Idle);
                    break;
                case CareState.Grooming:
                    if (Elapsed >= duration) Enter(CareState.Idle);
                    break;
                case CareState.SeekingShelter:
                    if (o.AtGoal) Enter(CareState.Resting);
                    break;
                case CareState.Resting:
                    if (Elapsed >= duration) { restCooldown = 15; Enter(CareState.Idle); }
                    break;
            }
        }

        void Force(CareState value) { if (State != value) Enter(value); }
        void Enter(CareState value)
        {
            State = value; Elapsed = 0; MealReady = false;
            Goal = value == CareState.Wandering ? CareGoal.Wander : value == CareState.SeekingFood ? CareGoal.Food :
                value == CareState.SeekingShelter ? CareGoal.Shelter : CareGoal.None;
            GoalVersion++;
            switch (value)
            {
                case CareState.Idle: duration = profile.idleMin + Roll() * (profile.idleMax - profile.idleMin); break;
                case CareState.Wandering: duration = profile.walkSeconds; break;
                case CareState.Inspecting: duration = profile.inspectSeconds; break;
                case CareState.Grooming: duration = profile.groomSeconds; break;
                case CareState.Resting: duration = profile.restSeconds; break;
                case CareState.Recovering: duration = profile.recoverSeconds; break;
                case CareState.Startled: duration = profile.startleSeconds; break;
                default: duration = 0; break;
            }
        }
        double Roll() { uint x = random; x ^= x << 13; x ^= x >> 17; x ^= x << 5; random = x; return x / ((double)uint.MaxValue + 1); }
    }
}
