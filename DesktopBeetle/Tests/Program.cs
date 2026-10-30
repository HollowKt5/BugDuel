using System;
using System.IO;
using BeetleDesktop.Native;
using System.Numerics;

internal static class Program
{
    private static int passed;

    private static int Main()
    {
        try
        {
            Run("One initial pet, two-pet limit and one-pet minimum", Population);
            Run("Hungry pet finds the bowl and consumes actual food", Feeding);
            Run("Empty food bowl cannot fill a pet", EmptyBowl);
            Run("Held pet stays put and cannot start a duel", Held);
            Run("Grabbing during a duel cancels it without a winner", GrabCancelsFight);
            Run("Manual duel concludes with two healthy pets and cooldown", Duel);
            Run("Nearby pets sometimes duel by themselves", AutomaticDuel);
            Run("Distant pets do not automatically duel", DistantPets);
            Run("Pause freezes behavior, needs and cooldown", Pause);
            Run("Long simulation keeps all needs and positions valid", LongRun);
            Run("Identical seeds and inputs reproduce behavior", Deterministic);
            Run("Save restores pets, bowl, needs and wins without held state", Persistence);
            Run("Malformed save recovers the atomic-write backup", BackupRecovery);
            Run("Invalid saved counts and numeric fields are sanitized", InvalidSave);
            Run("Fixed seeds reproduce each one-time appearance draw", AppearanceDeterministic);
            Run("Both adoption slots independently draw about ten percent iridescence", AppearanceDistribution);
            Run("Legacy saves stay normal through repeated loading", LegacyAppearance);
            Run("Sparring distance accounts for actual model length", FightVisualSpacing);
            Run("Eating position accounts for model length and bowl radius", FoodVisualSpacing);
            Run("A beetle turns before advancing and never slides across its heading", ForwardCrawl);
            Run("Crawling accelerates gradually and reports actual ground travel", GroundMotion);
            Run("Beetle crawl, feeding approach and duel approach keep their physical speed across desktop sizes", DesktopScaleSpeed);
            Run("Turning stays deliberate and a completed walk pauses to inspect", DeliberateMotion);
            Run("Short and distant paths finish without orbiting their destination", Arrival);
            Run("Motion telemetry is frozen with the paused simulation", PausedMotion);
            Run("Release preserves hand-rotated heading during the landing pause", ReleaseHeading);
            Run("Pet names trim, preserve Unicode text elements and persist", Rename);
            Run("Invalid pet names and indexes leave existing names unchanged", InvalidRename);
            Run("Each beetle has an independent roughly half-hour overturn timer", OverturnTiming);
            Run("Overturning waits for safe actions and freezes while paused", OverturnDeferral);
            Run("Helping an overturned beetle restores it immediately and restarts its timer", Righting);
            Run("Overturned state and remaining timer survive saving and old saves stay safe", OverturnPersistence);
            Run("Invalid saved overturn timers are sanitized", InvalidOverturnTimer);
            Console.WriteLine("PASS: " + passed + " simulation checks.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL: " + exception);
            return 1;
        }
    }

    private static void Run(string name, Action check)
    {
        check();
        passed++;
        Console.WriteLine("PASS " + name);
    }

    private static void Advance(PetSimulation sim, float seconds)
    {
        for (int frame = 0; frame < (int)Math.Ceiling(seconds * 20f); frame++)
            sim.Tick(0.05f, 16f / 9f);
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Near(float actual, float expected, string message, float epsilon = 0.0001f)
    {
        Check(Math.Abs(actual - expected) < epsilon, message + ": " + actual + " != " + expected);
    }

    private static PetSimulation Pair(int seed = 72)
    {
        var sim = new PetSimulation(seed);
        sim.AddPet();
        return sim;
    }

    private static void Population()
    {
        var sim = new PetSimulation();
        Check(sim.Pets.Count == 1, "New household starts with one pet");
        Check(sim.AddPet(), "Second pet accepted");
        Check(!sim.AddPet() && sim.Pets.Count == 2, "Third pet rejected");
        Check(sim.RemovePet(1), "Second pet can return home");
        Check(!sim.RemovePet(0) && sim.Pets.Count == 1, "Last pet remains");
        Check(!sim.RemovePet(10), "Unknown index rejected");
    }

    private static void Feeding()
    {
        var sim = new PetSimulation();
        sim.Pets[0].Position = new Vector2(0.6f, 0.3f);
        sim.Pets[0].Hunger = 0.8f;
        sim.Food = 0f;
        sim.Feed();
        float before = sim.Food;
        bool ate = false;
        for (int i = 0; i < 600; i++)
        {
            sim.Tick(0.05f, 16f / 9f);
            ate |= sim.Pets[0].Action == "eat";
        }
        Check(ate, "Pet reached the bowl and ate");
        Check(sim.Pets[0].Hunger < 0.2f, "Hunger falls after eating");
        Check(sim.Food < before && sim.Food >= 0f, "Eating consumes a bounded food supply");
        sim.Feed();
        sim.Feed();
        Near(sim.Food, 1f, "Bowl cannot overflow");
    }

    private static void EmptyBowl()
    {
        var sim = new PetSimulation();
        sim.Food = 0f;
        sim.Pets[0].Hunger = 0.8f;
        Advance(sim, 20f);
        Check(sim.Pets[0].Hunger >= 0.8f, "Empty bowl cannot feed pets");
        Near(sim.Food, 0f, "Food stays empty");
    }

    private static void Held()
    {
        var sim = Pair();
        sim.Hold(0, new Vector2(0.7f, 0.6f));
        Check(!sim.RequestFight(), "Held pets cannot fight");
        Advance(sim, 100f);
        Near(sim.Pets[0].Position.X, 0.7f, "Held X stays still");
        Near(sim.Pets[0].Position.Y, 0.6f, "Held Y stays still");
        Check(sim.Pets[0].Action == "held" && !sim.FightInProgress, "Holding suppresses automatic actions");
        sim.Release(0);
        Check(sim.Pets[0].Action == "startled", "Release starts a short landing reaction");
        Advance(sim, 3f);
        Check(sim.Pets[0].Action != "held", "Pet returns to activity");
    }

    private static void GrabCancelsFight()
    {
        var sim = Pair();
        Check(sim.RequestFight(), "Duel starts");
        Advance(sim, 2f);
        sim.Hold(1, new Vector2(0.5f, 0.5f));
        Check(!sim.FightInProgress, "Holding cancels the duel");
        Check(sim.Pets[0].Wins + sim.Pets[1].Wins == 0, "Interrupted duel has no winner");
        Check(sim.Pets[0].Action != "fight", "Other pet exits its fight pose");
    }

    private static void Duel()
    {
        var sim = Pair();
        Check(sim.RequestFight(), "Manual duel starts");
        bool fightPose = false;
        for (int i = 0; i < 700 && sim.FightInProgress; i++)
        {
            sim.Tick(0.05f, 16f / 9f);
            fightPose |= sim.Pets[0].Action == "fight";
        }
        Check(fightPose, "Approach proceeds to actual sparring");
        Check(!sim.FightInProgress, "Duel finishes in bounded time");
        Check(sim.Pets.Count == 2 && sim.Pets[0].Wins + sim.Pets[1].Wins == 1, "Exactly one winner and no lost pet");
        Check(sim.Pets[0].Energy > 0f && sim.Pets[1].Energy > 0f, "Both pets remain healthy");
        Check(sim.FightCooldownRemaining > 0f && !sim.RequestFight(), "Cooldown prevents repeated immediate duel");
        Advance(sim, PetSimulation.DuelCooldown + 0.1f);
        Check(sim.RequestFight(), "Manual duel is available after rest");
    }

    private static void AutomaticDuel()
    {
        bool foundFight = false;
        for (int seed = 1; seed <= 12 && !foundFight; seed++)
        {
            var sim = Pair(seed);
            sim.Food = 0f;
            for (int i = 0; i < 2600 && !sim.FightInProgress; i++)
            {
                // Keep neighbors together, as if the user has arranged a meeting.
                sim.Pets[0].Position = new Vector2(0.45f, 0.5f);
                sim.Pets[1].Position = new Vector2(0.5f, 0.5f);
                sim.Pets[0].Hunger = sim.Pets[1].Hunger = 0.1f;
                sim.Pets[0].Energy = sim.Pets[1].Energy = 0.9f;
                sim.Tick(0.05f, 16f / 9f);
            }
            foundFight |= sim.FightInProgress;
        }
        Check(foundFight, "Nearby rested pets should occasionally begin a spontaneous duel");
    }

    private static void DistantPets()
    {
        var sim = Pair();
        sim.Food = 0f;
        for (int i = 0; i < 8000; i++)
        {
            sim.Pets[0].Position = new Vector2(0.1f, 0.5f);
            sim.Pets[1].Position = new Vector2(0.9f, 0.5f);
            sim.Tick(0.05f, 16f / 9f);
            Check(!sim.FightInProgress, "Automatic duels require nearby pets");
        }
    }

    private static void Pause()
    {
        var sim = Pair();
        sim.RequestFight();
        Advance(sim, 1f);
        sim.Paused = true;
        Vector2 position = sim.Pets[0].Position;
        float hunger = sim.Pets[0].Hunger;
        float energy = sim.Pets[0].Energy;
        float food = sim.Food;
        Advance(sim, 60f);
        Near(sim.Pets[0].Position.X, position.X, "Paused X");
        Near(sim.Pets[0].Position.Y, position.Y, "Paused Y");
        Near(sim.Pets[0].Hunger, hunger, "Paused hunger");
        Near(sim.Pets[0].Energy, energy, "Paused energy");
        Near(sim.Food, food, "Paused food");
        Check(sim.FightInProgress, "Paused duel has not progressed");
    }

    private static void LongRun()
    {
        var sim = Pair();
        for (int second = 0; second < 7200; second++)
        {
            if (second % 90 == 0) sim.Feed();
            if (second % 131 == 0) sim.RequestFight();
            sim.Tick(1f, second % 2 == 0 ? 0.6f : 3.5f);
            foreach (PetState pet in sim.Pets)
            {
                Check(pet.Position.X >= 0f && pet.Position.X <= 1f && pet.Position.Y >= 0f && pet.Position.Y <= 1f, "Position is finite and on screen");
                Check(pet.Hunger >= 0f && pet.Hunger <= 1f && pet.Energy >= 0f && pet.Energy <= 1f, "Needs are finite and bounded");
                Check(pet.Heading >= 0f && pet.Heading < 360f, "Heading is valid");
            }
        }
        Check(sim.Pets.Count == 2, "Long-running pets never disappear");
    }

    private static void Deterministic()
    {
        var first = Pair(123);
        var second = Pair(123);
        Advance(first, 300f);
        Advance(second, 300f);
        for (int i = 0; i < 2; i++)
        {
            Near(first.Pets[i].Position.X, second.Pets[i].Position.X, "Seeded X");
            Near(first.Pets[i].Position.Y, second.Pets[i].Position.Y, "Seeded Y");
            Near(first.Pets[i].Hunger, second.Pets[i].Hunger, "Seeded hunger");
            Check(first.Pets[i].Action == second.Pets[i].Action && first.Pets[i].Wins == second.Pets[i].Wins, "Seeded actions");
        }
    }

    private static void Persistence()
    {
        string directory = Path.Combine(Path.GetTempPath(), "beetle-sim-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "pets.json");
        try
        {
            var sim = Pair();
            sim.Pets[0].Name = "栗子";
            sim.Pets[0].Wins = 7;
            sim.Pets[0].Hunger = 0.6f;
            sim.Pets[0].Energy = 0.4f;
            sim.Pets[0].Iridescent = true;
            sim.Pets[1].Iridescent = false;
            sim.Hold(1, new Vector2(0.3f, 0.7f));
            sim.MoveBowl(new Vector2(0.65f, 0.25f));
            sim.Food = 0.23f;
            Check(PetPersistence.Save(sim, path), "Save writes successfully");
            PetSimulation restored = PetPersistence.Load(path);
            Check(restored.Pets.Count == 2 && restored.Pets[0].Name == "栗子" && restored.Pets[0].Wins == 7, "Identity and record restored");
            Near(restored.Pets[0].Hunger, 0.6f, "Hunger restored");
            Near(restored.Pets[0].Energy, 0.4f, "Energy restored");
            Near(restored.Pets[1].Position.Y, 0.7f, "Pet position restored");
            Near(restored.BowlPosition.X, 0.65f, "Bowl restored");
            Near(restored.Food, 0.23f, "Food restored");
            Check(restored.Pets[1].Action == "idle", "Pet cannot remain held after restarting");
            Check(restored.Pets[0].Iridescent && !restored.Pets[1].Iridescent, "Both appearance flags survive saving");
            for (int reload = 0; reload < 20; reload++)
            {
                restored = PetPersistence.Load(path);
                Check(restored.Pets[0].Iridescent && !restored.Pets[1].Iridescent, "Loading must never reroll either appearance");
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void BackupRecovery()
    {
        string directory = Path.Combine(Path.GetTempPath(), "beetle-sim-recovery-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "pets.json");
        try
        {
            var sim = Pair();
            sim.Pets[0].Wins = 4;
            Check(PetPersistence.Save(sim, path), "Initial save");
            sim.Pets[0].Wins = 5;
            Check(PetPersistence.Save(sim, path), "Atomic replacement");
            Check(File.Exists(path + ".bak"), "Backup preserved");
            File.WriteAllText(path, "{ broken");
            PetSimulation restored = PetPersistence.Load(path);
            Check(restored.Pets.Count == 2 && restored.Pets[0].Wins == 4, "Previous valid save is recovered");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void InvalidSave()
    {
        var data = new PetSaveData { Food = float.NaN, BowlPosition = new Vector2(-50f, 100f) };
        for (int i = 0; i < 5; i++)
            data.Pets.Add(new PetState { Position = new Vector2(float.NaN, -20f), Energy = float.PositiveInfinity, Hunger = -10f, Wins = -3, Name = "" });
        PetSimulation sim = PetSimulation.Restore(data);
        Check(sim.Pets.Count == 2, "Loaded pet count is capped");
        Check(sim.Pets[0].Position.X >= 0f && sim.Pets[0].Position.Y > 0f, "Loaded coordinates sanitized");
        Check(sim.Pets[0].Energy <= 1f && sim.Pets[0].Hunger == 0f && sim.Pets[0].Wins == 0, "Loaded needs and wins sanitized");
        Check(sim.Food >= 0f && sim.Food <= 1f, "Loaded food sanitized");
        data.Version = 99;
        Check(PetSimulation.Restore(data).Pets.Count == 1, "Unknown format starts a valid household");
    }

    private static void AppearanceDeterministic()
    {
        Check(!new PetState().Iridescent, "A default or legacy PetState has normal texture");
        var first = new PetSimulation(29817);
        var second = new PetSimulation(29817);
        Check(first.Pets[0].Iridescent == second.Pets[0].Iridescent, "Initial seeded appearance is reproducible");
        for (int adoption = 0; adoption < 50; adoption++)
        {
            first.AddPet();
            second.AddPet();
            Check(first.Pets[1].Iridescent == second.Pets[1].Iridescent, "Each subsequent seeded appearance is reproducible");
            bool appearance = first.Pets[1].Iridescent;
            Advance(first, 1f);
            Advance(second, 1f);
            Check(first.Pets[1].Iridescent == appearance, "Activity cannot change an adopted pet's appearance");
            first.RemovePet(1);
            second.RemovePet(1);
        }
    }

    private static void AppearanceDistribution()
    {
        const int households = 10000;
        int firstCount = 0, secondCount = 0, bothCount = 0;
        for (int seed = 1; seed <= households; seed++)
        {
            var sim = Pair(seed);
            if (sim.Pets[0].Iridescent) firstCount++;
            if (sim.Pets[1].Iridescent) secondCount++;
            if (sim.Pets[0].Iridescent && sim.Pets[1].Iridescent) bothCount++;
        }
        Check(firstCount > households * 0.08 && firstCount < households * 0.12, "First adoption has approximately ten percent probability");
        Check(secondCount > households * 0.08 && secondCount < households * 0.12, "Second adoption has approximately ten percent probability");
        Check(bothCount > households * 0.005 && bothCount < households * 0.015, "Two iridescent pets together occur approximately one percent of the time");
        Console.WriteLine($"  fixed-seed sample: first={firstCount}/{households}, second={secondCount}/{households}, both={bothCount}/{households}");
    }

    private static void LegacyAppearance()
    {
        string directory = Path.Combine(Path.GetTempPath(), "beetle-sim-legacy-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "pets.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "{\"Version\":1,\"Pets\":[{\"Name\":\"旧一\"},{\"Name\":\"旧二\"}],\"RandomState\":1234}");
            for (int reload = 0; reload < 20; reload++)
            {
                PetSimulation restored = PetPersistence.Load(path);
                Check(restored.Pets.Count == 2 && restored.Pets[0].Name == "旧一", "Legacy identity is restored");
                Check(!restored.Pets[0].Iridescent && !restored.Pets[1].Iridescent, "Absent legacy appearance fields stay normal");
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static float WorldDistance(Vector2 first, Vector2 second)
    {
        return new Vector2((first.X - second.X) * 16f / 9f, first.Y - second.Y).Length();
    }

    private static void FightVisualSpacing()
    {
        var sim = Pair();
        sim.VisualLength = 0.2f;
        Check(sim.RequestFight(), "Sized duel can start");
        for (int frame = 0; frame < 600 && sim.Pets[0].Action != "fight"; frame++)
            sim.Tick(0.05f, 16f / 9f);
        Check(sim.Pets[0].Action == "fight", "Sized duel reaches its contact phase");
        Advance(sim, 2f);
        float distance = WorldDistance(sim.Pets[0].Position, sim.Pets[1].Position);
        Check(distance >= sim.VisualLength * 0.86f && distance <= sim.VisualLength * 1.04f,
            "Fighting pets meet at their horns instead of overlapping their bodies");
    }

    private static void FoodVisualSpacing()
    {
        var sim = new PetSimulation(72);
        sim.VisualLength = 0.2f;
        sim.BowlRadius = 0.052f;
        sim.Pets[0].Hunger = 0.8f;
        for (int frame = 0; frame < 600 && sim.Pets[0].Action != "eat"; frame++)
            sim.Tick(0.05f, 16f / 9f);
        Check(sim.Pets[0].Action == "eat", "Sized pet still reaches its bowl");
        float distance = WorldDistance(sim.Pets[0].Position, sim.BowlPosition);
        Check(distance >= sim.VisualLength * 0.4f + sim.BowlRadius * 0.7f,
            "Pet body stays outside the bowl while its head reaches the food");
        float hunger = sim.Pets[0].Hunger;
        Advance(sim, 1f);
        Check(sim.Pets[0].Hunger < hunger, "Correctly spaced pet actually eats");
        sim.VisualLength = float.NaN;
        sim.BowlRadius = float.PositiveInfinity;
        sim.Tick(0.05f, 16f / 9f);
        Check(float.IsFinite(sim.VisualLength) && float.IsFinite(sim.BowlRadius), "Invalid host dimensions are sanitized");
    }

    private static PetSimulation Walking(float heading, Vector2 target)
    {
        var sim = new PetSimulation(72) { Food = 0f };
        PetState pet = sim.Pets[0];
        pet.Position = new Vector2(0.35f, 0.45f);
        pet.Heading = heading;
        pet.Hunger = 0.1f;
        pet.Action = "wander";
        pet.ActionTime = 60f;
        pet.Target = target;
        return sim;
    }

    private static void ForwardCrawl()
    {
        var sim = Walking(270f, new Vector2(0.65f, 0.45f));
        PetState pet = sim.Pets[0];
        Vector2 start = pet.Position;
        Advance(sim, 0.5f);
        Near(WorldDistance(start, pet.Position), 0f, "A resting beetle first pivots toward a target behind it");
        bool walked = false;
        for (int i = 0; i < 120; i++)
        {
            Vector2 before = pet.Position;
            sim.Tick(0.05f, 16f / 9f);
            Vector2 displacement = new Vector2((pet.Position.X - before.X) * 16f / 9f, pet.Position.Y - before.Y);
            if (displacement.Length() < 0.000001f) continue;
            walked = true;
            float radians = pet.Heading * (float)Math.PI / 180f;
            Vector2 forward = new Vector2((float)Math.Sin(radians), (float)Math.Cos(radians));
            Check(Vector2.Dot(Vector2.Normalize(displacement), forward) > 0.999f,
                "Every crawl step follows the current body heading instead of the target vector");
        }
        Check(walked, "The turn is followed by real forward walking");
    }

    private static void GroundMotion()
    {
        var sim = Walking(90f, new Vector2(0.8f, 0.45f));
        PetState pet = sim.Pets[0];
        float accumulated = 0f;
        for (int i = 0; i < 50; i++)
        {
            Vector2 before = pet.Position;
            float oldSpeed = pet.Speed;
            sim.Tick(0.05f, 16f / 9f);
            float distance = WorldDistance(before, pet.Position);
            accumulated += distance;
            Near(pet.Speed, distance / 0.05f, "Gait speed equals actual displacement", 0.00001f);
            Check(pet.Speed - oldSpeed <= sim.VisualLength * 0.12f * 0.05f + 0.00001f, "Walking does not jump instantly to cruising speed");
            Near(pet.TravelDistance, accumulated, "Gait phase is driven by real ground travel", 0.00001f);
        }
        Near(pet.Speed, sim.VisualLength * 0.095f, "Acceleration reaches the intended deliberate cruising speed", 0.00001f);
        Near(pet.TurnRate, 0f, "Straight walking has no false turning telemetry");
    }

    private static void DesktopScaleSpeed()
    {
        foreach (float visualLength in new[] { 0.1f, 0.2f, 0.4f })
        {
            float usableHeightDip = 164f / visualLength;
            var walking = Walking(90f, new Vector2(0.8f, 0.45f));
            walking.VisualLength = visualLength;
            Advance(walking, 2.5f);
            float walkDipPerSecond = walking.Pets[0].Speed * usableHeightDip;
            Check(walkDipPerSecond >= 14f && walkDipPerSecond <= 17f, "Ordinary crawling stays between 14 and 17 DIP/s");

            var feeding = Walking(90f, new Vector2(0.8f, 0.45f));
            feeding.VisualLength = visualLength;
            feeding.BowlPosition = new Vector2(0.8f, 0.46f);
            feeding.Food = 1f;
            feeding.Pets[0].Hunger = 0.8f;
            feeding.Pets[0].SeekingFood = true;
            Advance(feeding, 2.5f);
            float foodDipPerSecond = feeding.Pets[0].Speed * usableHeightDip;
            Check(foodDipPerSecond >= 26f && foodDipPerSecond <= 32f, "Seeking food stays between 26 and 32 DIP/s");

            var duel = Pair(72);
            duel.VisualLength = visualLength;
            duel.Pets[0].Position = new Vector2(0.1f, 0.5f);
            duel.Pets[1].Position = new Vector2(0.9f, 0.5f);
            duel.Pets[0].Heading = 90f;
            duel.Pets[1].Heading = 270f;
            Check(duel.RequestFight(), "Scale-check duel starts");
            Advance(duel, 3f);
            float duelDipPerSecond = duel.Pets[0].Speed * usableHeightDip;
            Check(duelDipPerSecond >= 35f && duelDipPerSecond <= 40f, "Duel approach stays between 35 and 40 DIP/s");
        }
    }

    private static void DeliberateMotion()
    {
        var turning = Walking(270f, new Vector2(0.65f, 0.45f));
        for (int i = 0; i < 50; i++)
        {
            turning.Tick(0.05f, 16f / 9f);
            Check(Math.Abs(turning.Pets[0].TurnRate) <= 60.02f, "A normal turn never snaps faster than sixty degrees per second");
        }
        var arriving = Walking(90f, new Vector2(0.37f, 0.45f));
        PetState pet = arriving.Pets[0];
        for (int i = 0; i < 400 && pet.Action == "wander"; i++)
            arriving.Tick(0.05f, 16f / 9f);
        Check(pet.Action == "inspect" && pet.ActionTime >= 1.8f, "Finishing a walk starts an observation pause before any new path");
        Vector2 position = pet.Position;
        Advance(arriving, 1f);
        Near(WorldDistance(position, pet.Position), 0f, "Observation pause holds its ground for at least a second");
    }

    private static void Arrival()
    {
        foreach (float heading in new[] { 0f, 57f, 180f, 270f })
        foreach (float distance in new[] { 0.003f, 0.025f, 0.16f })
        {
            Vector2 target = new Vector2(0.35f + distance / (16f / 9f), 0.45f);
            var sim = Walking(heading, target);
            bool arrived = false;
            for (int i = 0; i < 600; i++)
            {
                sim.Tick(0.05f, 16f / 9f);
                if (WorldDistance(sim.Pets[0].Position, target) <= 0.0008f)
                {
                    arrived = true;
                    break;
                }
            }
            Check(arrived, $"Path arrives from heading {heading} over distance {distance} without orbiting");
        }
    }

    private static void PausedMotion()
    {
        var sim = Walking(0f, new Vector2(0.65f, 0.45f));
        Advance(sim, 0.6f);
        PetState pet = sim.Pets[0];
        float heading = pet.Heading, speed = pet.Speed, travel = pet.TravelDistance, turn = pet.TurnRate, look = pet.LookYaw;
        sim.Paused = true;
        Advance(sim, 30f);
        Near(pet.Heading, heading, "Paused heading");
        Near(pet.Speed, speed, "Paused gait speed");
        Near(pet.TravelDistance, travel, "Paused travel phase");
        Near(pet.TurnRate, turn, "Paused turning gait");
        Near(pet.LookYaw, look, "Paused head lead");
    }

    private static void ReleaseHeading()
    {
        var sim = new PetSimulation(72) { Food = 0f };
        sim.Hold(0, new Vector2(0.5f, 0.5f));
        sim.Pets[0].Heading = 223f; // The host rotates this field while the right button is held.
        sim.Release(0);
        Advance(sim, 0.6f);
        Near(sim.Pets[0].Heading, 223f, "Landing does not overwrite the angle selected with the wheel");
        Near(WorldDistance(sim.Pets[0].Position, new Vector2(0.5f, 0.5f)), 0f, "Landing keeps the placement position");
        Check(sim.Pets[0].Action == "startled", "Landing pause lasts for its promised settling time");
    }

    private static void Rename()
    {
        var sim = Pair();
        Check(sim.RenamePet(1, "  小栗子  "), "A normal name can be set");
        Check(sim.Pets[1].Name == "小栗子", "Whitespace around the name is trimmed");
        string emojiName = "";
        for (int i = 0; i < 12; i++) emojiName += "🪲";
        Check(sim.RenamePet(0, emojiName), "Twelve Unicode text elements fit even when they use surrogate pairs");
        sim.Pets[0].TravelDistance = 10f;
        sim.Pets[0].TurnRate = 5f;
        sim.Pets[0].LookYaw = 8f;
        PetSimulation restored = PetSimulation.Restore(sim.CaptureSaveData());
        Check(restored.Pets[0].Name == emojiName && restored.Pets[1].Name == "小栗子", "Both custom names survive saving and restoring");
        Near(restored.Pets[0].TravelDistance, 0f, "Movement phase does not leak into restored pets");
        Near(restored.Pets[0].TurnRate, 0f, "Turn rate is runtime state");
        Near(restored.Pets[0].LookYaw, 0f, "Head lead is runtime state");
    }

    private static void InvalidRename()
    {
        var sim = new PetSimulation(72);
        string original = sim.Pets[0].Name;
        foreach (string invalid in new[] { null, "", "   ", "1234567890123", "两\n行", "制\t表", "\u0001名字", "段\u2028落" })
        {
            Check(!sim.RenamePet(0, invalid), "An invalid name is rejected");
            Check(sim.Pets[0].Name == original, "Rejecting a name preserves the original name");
        }
        Check(!sim.RenamePet(-1, "新名") && !sim.RenamePet(1, "新名"), "Invalid pet indexes are rejected");
    }

    private static void OverturnTiming()
    {
        var sim = Pair(72);
        foreach (PetState pet in sim.Pets)
            Check(pet.UntilOverturn >= 1500f && pet.UntilOverturn <= 2100f, "New timers fall between 25 and 35 minutes");
        Check(sim.Pets[0].UntilOverturn != sim.Pets[1].UntilOverturn, "Pets draw independent timers");
        PetState first = sim.Pets[0];
        first.UntilOverturn = 0.1f;
        sim.Pets[1].UntilOverturn = 120f;
        first.ActionTime = sim.Pets[1].ActionTime = 60f;
        first.Hunger = sim.Pets[1].Hunger = 0.1f;
        Advance(sim, 0.15f);
        Check(first.Overturned && first.Action == "overturned", "An elapsed timer overturns a calm pet");
        Check(!sim.Pets[1].Overturned && sim.Pets[1].UntilOverturn > 119f, "The second pet keeps its own timer");
        Vector2 position = first.Position;
        float heading = first.Heading;
        first.Hunger = 0.9f;
        sim.Feed();
        float food = sim.Food;
        Advance(sim, 3f);
        Near(WorldDistance(first.Position, position), 0f, "Overturned pets cannot walk away");
        Near(first.Heading, heading, "Overturned pets do not turn their body toward food");
        Near(sim.Food, food, "An overturned pet cannot eat without help");
        Check(!sim.RequestFight(), "Overturned pets cannot duel");
    }

    private static void OverturnDeferral()
    {
        foreach (string action in new[] { "held", "eat", "sleep" })
        {
            var sim = new PetSimulation(72);
            PetState pet = sim.Pets[0];
            pet.UntilOverturn = 0.01f;
            pet.Action = action;
            pet.ActionTime = 60f;
            pet.Energy = action == "sleep" ? 0.1f : 0.9f;
            pet.Hunger = action == "eat" ? 0.8f : 0.1f;
            if (action == "eat")
                pet.Position = new Vector2(sim.BowlPosition.X - (sim.VisualLength * 0.45f + sim.BowlRadius * 0.85f) / (16f / 9f), sim.BowlPosition.Y - 0.01f);
            Advance(sim, 0.2f);
            Check(!pet.Overturned && pet.UntilOverturn == 0f, "An overdue timer waits during " + action);
            pet.Action = "idle";
            sim.Tick(0.05f, 16f / 9f);
            Check(pet.Overturned, "An overdue timer triggers when " + action + " ends");
        }
        var duel = Pair(72);
        Check(duel.RequestFight(), "Deferral duel starts");
        duel.Pets[0].UntilOverturn = 0.01f;
        Advance(duel, 0.2f);
        Check(!duel.Pets[0].Overturned && duel.Pets[0].UntilOverturn == 0f, "A duel defers overturning");
        var paused = new PetSimulation(72) { Paused = true };
        paused.Pets[0].UntilOverturn = 0.1f;
        Advance(paused, 10f);
        Near(paused.Pets[0].UntilOverturn, 0.1f, "Pause freezes the overturn timer");
        Check(!paused.Pets[0].Overturned, "Paused pets cannot suddenly overturn");
    }

    private static void Righting()
    {
        var sim = new PetSimulation(72);
        PetState pet = sim.Pets[0];
        pet.UntilOverturn = 0.01f;
        sim.Tick(0.05f, 16f / 9f);
        float heading = pet.Heading;
        Check(sim.RightPet(0) && !pet.Overturned, "Click help immediately restores the pet");
        Check(pet.UntilOverturn >= 1500f && pet.UntilOverturn <= 2100f, "Helping restarts a full timer");
        Check(pet.Action == "startled", "Help includes a short recovery pose");
        Advance(sim, 0.3f);
        Near(pet.Heading, heading, "Help does not overwrite the chosen heading");
        Check(!sim.RightPet(0) && !sim.RightPet(-1) && !sim.RightPet(1), "Healthy pets and invalid indexes are ignored");
        pet.Overturned = true;
        sim.Hold(0, new Vector2(0.4f, 0.5f));
        sim.Release(0);
        Check(pet.Overturned && pet.Action == "overturned", "Picking up and dropping without help preserves overturned state");
        sim.Hold(0, pet.Position);
        Check(sim.RightPet(0) && pet.Action == "held" && !pet.Overturned, "Helping during a grab retains the grab");
        sim.Release(0);
        Check(pet.Action == "startled", "A helped held pet can land normally");
    }

    private static void OverturnPersistence()
    {
        string directory = Path.Combine(Path.GetTempPath(), "beetle-overturn-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "pets.json");
        try
        {
            var sim = Pair(72);
            sim.Pets[0].Overturned = true;
            sim.Pets[0].UntilOverturn = 0f;
            sim.Pets[1].UntilOverturn = 472.5f;
            sim.Hold(0, sim.Pets[0].Position);
            Check(PetPersistence.Save(sim, path), "Overturned household saves");
            PetSimulation restored = PetPersistence.Load(path);
            Check(restored.Pets[0].Overturned && restored.Pets[0].Action == "overturned", "Restart preserves an overturned pet even if it was held");
            Near(restored.Pets[0].UntilOverturn, 0f, "Overdue timer persists");
            Near(restored.Pets[1].UntilOverturn, 472.5f, "The other timer retains its remaining time");
            File.WriteAllText(path, "{\"Version\":1,\"Pets\":[{\"Name\":\"旧甲虫\"}],\"RandomState\":1234}");
            restored = PetPersistence.Load(path);
            Check(!restored.Pets[0].Overturned, "Legacy pets default to upright");
            Near(restored.Pets[0].UntilOverturn, 1800f, "Legacy missing timers default to thirty minutes");
            restored.Tick(0.05f, 16f / 9f);
            Check(!restored.Pets[0].Overturned, "Legacy pets do not overturn immediately on loading");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void InvalidOverturnTimer()
    {
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -3f, 100000f })
        {
            var data = new PetSaveData();
            data.Pets.Add(new PetState { UntilOverturn = invalid });
            PetSimulation restored = PetSimulation.Restore(data);
            float timer = restored.Pets[0].UntilOverturn;
            Check(float.IsFinite(timer) && timer >= 0f && timer <= 2100f, "Loaded timers are finite and bounded");
            if (!float.IsFinite(invalid)) Near(timer, 1800f, "Unusable timer gets a safe default");
        }
    }
}

