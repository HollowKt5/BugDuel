using BugDuel.Domain;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>Bounded, obstacle-free observation pen. Replace this motor for a full terrarium/NavMesh.</summary>
    public sealed class CareLocomotion
    {
        public static readonly Vector2 FoodPoint = new Vector2(.35f, -.65f);
        public static readonly Vector2 FeedingPoint = new Vector2(-.1f, .1f);
        public static readonly Vector2 ShelterPoint = new Vector2(.15f, .35f);
        public Vector2 Position { get; private set; }
        public float Heading { get; private set; } = 145;
        public float Speed { get; private set; }
        public bool AtGoal => Vector2.Distance(Position, target) < .025f &&
            (goal != CareGoal.Food || Mathf.Abs(Mathf.DeltaAngle(Heading, Angle(FoodPoint - Position))) < 8);
        Vector2 target;
        CareGoal goal;
        int version = -1;
        uint random;
        public CareLocomotion(uint seed) { random = seed == 0 ? 1 : seed; }
        public void Tick(CareStateMachine brain, float dt)
        {
            if (version != brain.GoalVersion)
            {
                version = brain.GoalVersion; goal = brain.Goal;
                target = goal == CareGoal.Food ? FeedingPoint : goal == CareGoal.Shelter ? ShelterPoint :
                    goal == CareGoal.Wander ? new Vector2(Roll() * .8f - .4f, Roll() * .6f - .3f) : Position;
            }
            var delta = target - Position;
            bool moving = goal != CareGoal.None && delta.magnitude > .015f;
            float desiredHeading = moving ? Angle(delta) : Heading;
            if (goal == CareGoal.Food && delta.magnitude < .12f || brain.State == CareState.Eating)
                desiredHeading = Angle(FoodPoint - Position);
            Heading = Mathf.MoveTowardsAngle(Heading, desiredHeading, 40 * dt);
            float alignment = Mathf.Clamp01(1 - Mathf.Abs(Mathf.DeltaAngle(Heading, desiredHeading)) / 75);
            float desiredSpeed = moving ? (float)brain.Profile.walkSpeed * alignment : 0;
            Speed = Mathf.MoveTowards(Speed, desiredSpeed, dt * .8f);
            // Interruptions stop translation immediately; the pose presenter blends limbs separately.
            if (!moving) Speed = 0;
            Position = Vector2.MoveTowards(Position, target, Mathf.Min(Speed * dt, delta.magnitude));
        }
        static float Angle(Vector2 v) => Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;
        float Roll() { uint x = random; x ^= x << 13; x ^= x >> 17; x ^= x << 5; random = x; return x / ((float)uint.MaxValue + 1); }
        public static uint Seed(string id)
        {
            uint hash = 2166136261; foreach (char c in id ?? "care") hash = (hash ^ c) * 16777619;
            return hash == 0 ? 1u : hash;
        }
    }
}
