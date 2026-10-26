using System.Collections.Generic;
using BugDuel.Campaign;
using BugDuel.Domain;
using UnityEngine;

namespace BeetleArena.Campaign
{
    /// <summary>Greenhouse adapter: observes campaign data, owns per-individual brains, commits meals through the app.</summary>
    public sealed class CampaignCareController : MonoBehaviour
    {
        public sealed class Resident
        {
            public CareStateMachine Brain { get; }
            public CareLocomotion Motor { get; }
            public float PoseSeconds { get; internal set; }
            internal CampaignCatalog catalog;
            internal string habitat;
            internal float retryAt;
            public Resident(string id, CareBehaviorProfile profile, CareWalkArea area)
            { uint seed = CareLocomotion.Seed(id); Brain = new CareStateMachine(profile, seed); Motor = new CareLocomotion(seed ^ 0x5831u, area); }
        }
        readonly Dictionary<string, Resident> residents = new Dictionary<string, Resident>();
        readonly List<string> removed = new List<string>();
        CampaignApp owner;
        CareWalkArea area;
        public void ConfigureArea(CareWalkArea value)
        {
            area = value;
            foreach (var resident in residents.Values)
            {
                var before=resident.Motor.Position;var old=resident.Motor.Area;
                resident.Motor.Configure(value);
                if(value!=null&&(before!=resident.Motor.Position||old.FeedingPoint!=value.FeedingPoint||old.ShelterPoint!=value.ShelterPoint))resident.Brain.InvalidateArrival();
            }
        }
        public bool Paused { get; private set; }
        public Resident Find(string id) => id != null && residents.TryGetValue(id, out var value) ? value : null;
        public void Disturb(string id) { if (!Paused) Find(id)?.Brain.Disturb(); }
        void OnEnable()
        {
            owner = CampaignApp.Instance;
            if (owner) owner.IsCareEating = IsEating;
        }
        void OnDisable()
        {
            if (owner && owner.IsCareEating == IsEating) owner.IsCareEating = null;
            owner = null;
        }
        bool IsEating(string id) => Find(id)?.Brain.State == CareState.Eating;
        void Update()
        {
            var clock = CampaignApp.Instance?.Session?.State.clock;
            if (clock != null) Advance(Mathf.Min(Time.unscaledDeltaTime, .05f), clock.Hour + clock.Minute / 60.0);
        }
        public void Advance(float dt, double hour)
        {
            var app = CampaignApp.Instance;
            if (!app || app.Session == null) return;
            Paused = !app.CalendarRunning || app.Session.State.clock.paused;
            var session = app.Session; removed.Clear();
            foreach (var id in residents.Keys) if (!session.State.beetles.Exists(b => b.id == id)) removed.Add(id);
            foreach (var id in removed) residents.Remove(id);
            foreach (var b in session.State.beetles)
            {
                if (!residents.TryGetValue(b.id, out var resident))
                { resident = new Resident(b.id, session.Catalog.Species(b.speciesId).behavior ?? new CareBehaviorProfile(), area); residents.Add(b.id, resident); }
                if (resident.catalog != session.Catalog)
                {
                    resident.Brain.Configure(session.Catalog.Species(b.speciesId).behavior ?? new CareBehaviorProfile());
                    resident.catalog = session.Catalog;
                }
                if (resident.habitat != null && resident.habitat != b.habitat) resident.Brain.Disturb();
                resident.habitat = b.habitat;
                var housing = session.Catalog.Housing(b.habitat);
                var observation = new CareObservation
                {
                    Dead=b.dead, Overturned=b.overturned, Growing=!b.Adult(session.State.week),
                    Unavailable=b.listed || session.State.battle?.beetleId==b.id || session.State.auction?.beetleId==b.id,
                    Paused=Paused, FoodAvailable=b.meal!=null, AtGoal=resident.Motor.AtGoal,
                    Nutrition=b.nutrition, HealthFraction=b.hearts/b.maxHearts, Hour=hour,
                    HabitatActivity=housing.behavior?.Activity??1, RestPreference=housing.behavior?.Rest??.5
                };
                resident.PoseSeconds = Paused ? 0 : dt;
                resident.Brain.Step(dt, observation);
                if (Paused) continue;
                resident.Motor.Tick(resident.Brain, dt);
                if (resident.Brain.MealReady && b.meal != null && Time.unscaledTime >= resident.retryAt)
                {
                    string id=b.id, mealId=b.meal.id;
                    resident.retryAt=Time.unscaledTime+2; // Do not hammer a failing disk every frame.
                    if (app.Execute(s=>s.CompleteMeal(id,mealId)))
                    { Paused=true; break; } // Only one durable transaction at a time.
                }
            }
        }
    }
}
