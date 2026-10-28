using System.Collections.Generic;
using BugDuel.Domain;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>Care-only motor: enclosure routes, bounded translation and turn-before-eating.</summary>
    public sealed class CareLocomotion
    {
        public Vector2 FoodPoint => area.FoodPoint;
        public Vector2 FeedingPoint => area.FeedingPoint;
        public Vector2 ShelterPoint => area.ShelterPoint;
        public CareWalkArea Area => area;
        public Vector2 Position { get; private set; }
        public float Heading { get; private set; } = 145;
        public float Speed { get; private set; }
        public bool AtGoal => version >= 0 && Vector2.Distance(Position, target) < .025f &&
            (goal != CareGoal.Food || Mathf.Abs(Mathf.DeltaAngle(Heading, Angle(FoodPoint - Position))) < 8);
        Vector2 target, wanderTarget;
        CareWalkArea area;
        readonly List<Vector2> path = new List<Vector2>();
        int waypoint;
        bool hasWanderTarget;
        CareGoal goal;
        int version = -1;
        uint random;
        float turnSpeed;
        public CareLocomotion(uint seed, CareWalkArea enclosure = null)
        {
            random = seed == 0 ? 1 : seed;
            area = enclosure ?? new CareWalkArea(new Rect(), 0);
            Position = target = area.Spawn;
        }
        public void Configure(CareWalkArea enclosure)
        {
            if (enclosure == null || ReferenceEquals(area, enclosure)) return;
            area = enclosure; Position = area.KeepInside(Position); target = Position;
            version = -1; Speed = turnSpeed = 0; hasWanderTarget = false; path.Clear();
        }
        public void PlaceAt(Vector2 position)
        {
            Position=target=area.KeepInside(position);
            Speed=turnSpeed=0;version=-1;hasWanderTarget=false;path.Clear();waypoint=0;
        }
        public void Tick(CareStateMachine brain, float dt)
        {
            if(dt<=0)return;
            if (version != brain.GoalVersion)
            {
                version = brain.GoalVersion; goal = brain.Goal;
                // Short behavior pauses must not keep reselecting distant goals and trapping the animal in the center.
                if (goal == CareGoal.Wander && (!hasWanderTarget || Vector2.Distance(Position, wanderTarget) < .04f))
                { wanderTarget = area.WanderPoint(Roll()); hasWanderTarget = true; }
                target = goal == CareGoal.Food ? FeedingPoint : goal == CareGoal.Shelter ? ShelterPoint :
                    goal == CareGoal.Wander ? wanderTarget : Position;
                area.FindPath(Position, target, path); waypoint = 0;
            }
            while (waypoint < path.Count && Vector2.Distance(Position, path[waypoint]) < .015f) waypoint++;
            // Look through a corner only after the swept body has a clear route to it.
            while(waypoint+1<path.Count&&area.SegmentClear(Position,path[waypoint+1]))waypoint++;
            var delta = waypoint < path.Count ? path[waypoint] - Position : Vector2.zero;
            bool moving = goal != CareGoal.None && waypoint < path.Count && area.CanWalk;
            float desiredHeading = moving ? Angle(delta) : Heading;
            // Finish approaching from any direction before facing the dish. Facing early can stall the last few centimeters.
            if (!moving && goal == CareGoal.Food || brain.State == CareState.Eating)
                desiredHeading = Angle(FoodPoint - Position);
            float error=Mathf.DeltaAngle(Heading,desiredHeading);
            turnSpeed=Mathf.MoveTowards(turnSpeed,Mathf.Clamp(error*3,-62,62),180*dt);
            float turn=Mathf.Abs(turnSpeed*dt)>Mathf.Abs(error)?error:turnSpeed*dt;
            Heading=Mathf.Repeat(Heading+turn+180,360)-180;
            float alignment=Mathf.Clamp01(1-Mathf.Abs(Mathf.DeltaAngle(Heading,desiredHeading))/65);
            float arrival=Mathf.Clamp01(delta.magnitude/.09f);
            float desiredSpeed=moving?(float)brain.Profile.walkSpeed*alignment*arrival:0;
            Speed=Mathf.MoveTowards(Speed,desiredSpeed,dt*(desiredSpeed>Speed?.32f:.7f));
            if(!moving){Speed=0;return;}
            // Translation follows the thorax heading; an uncleared corner stops the motor
            // while it turns, rather than sliding the animal sideways through the route.
            float radians=Heading*Mathf.Deg2Rad;
            var step=new Vector2(Mathf.Sin(radians),Mathf.Cos(radians))*Mathf.Min(Speed*dt,delta.magnitude);
            var next=Position+step;
            if(area.SegmentClear(Position,next))Position=next;else Speed=0;
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
