using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;

namespace BeetleDesktop.Native
{
    /// <summary>Screen-space pet state. Hunger is 0 when full; all needs are in [0, 1].</summary>
    [Serializable]
    public sealed class PetState
    {
        public Vector2 Position;
        public string Action = "idle";
        public float Heading;
        public float Speed;
        public float Hunger = 0.28f;
        public float Energy = 0.9f;
        public string Name;
        public bool ShowName = true;
        public int NameColorRgb = 0xE5EEDC;
        public int Wins;
        public bool Iridescent;
        public bool Overturned;
        public float UntilOverturn = 1800f;
        public float StillRemaining;

        // Motion telemetry belongs to the live gait, not the saved household.
        [NonSerialized, JsonIgnore] public float TurnRate;
        [NonSerialized, JsonIgnore] public float LookYaw;
        [NonSerialized, JsonIgnore] public float TravelDistance;

        [NonSerialized] internal float ActionTime;
        [NonSerialized] internal Vector2 Target;
        [NonSerialized] internal bool SeekingFood;
        [NonSerialized] internal Vector2 StepStartPosition;
        [NonSerialized] internal float StepStartHeading;
        [NonSerialized] internal float DesiredLookYaw;
    }

    [Serializable]
    public sealed class PetSaveData
    {
        public int Version = 1;
        public List<PetState> Pets = new List<PetState>();
        public Vector2 BowlPosition = new Vector2(0.62f, 0.28f);
        public float Food = 0.65f;
        public bool Paused;
        public float FightCooldown;
        public uint RandomState;
    }

    /// <summary>
    /// Deterministic desktop behavior, independent of scene objects and frame rate.
    /// Positions are normalized screen coordinates; travel speed uses screen height.
    /// Heading is measured clockwise from positive normalized Y: 0 points upward.
    /// </summary>
    public sealed class PetSimulation
    {
        public const int MaximumPets = 2;
        public const float DuelCooldown = 40f;

        public List<PetState> Pets { get; } = new List<PetState>();
        public Vector2 BowlPosition = new Vector2(0.62f, 0.28f);
        public float Food = 0.65f;
        public bool Paused;
        // Host supplies measured sprite sizes in units of the usable screen height.
        // These are presentation settings, intentionally excluded from saved data.
        public float VisualLength = 0.20f;
        public float BowlRadius = 0.052f;
        public bool FightInProgress => fightPhase != 0;
        public float FightCooldownRemaining => fightCooldown;
        public string LastFightWinner { get; private set; } = "";
        public string StatusMessage { get; private set; } = "长按右键可以抱起甲虫，点击食盆可以添粮。";

        private uint randomState;
        private float aspect = 16f / 9f;
        private Vector2 placementMinimum = Vector2.Zero;
        private Vector2 placementMaximum = Vector2.One;
        private float fightCooldown;
        private float autoFightTimer;
        private float fightTime;
        private int fightPhase;
        private Vector2 fightCenter;
        private float fightAxis;

        public PetSimulation(int? seed = null)
        {
            int initialSeed = seed ?? Random.Shared.Next(1, int.MaxValue);
            randomState = initialSeed == 0 ? 1u : unchecked((uint)initialSeed);
            autoFightTimer = Range(35f, 60f);
            AddPet();
        }

        public bool AddPet()
        {
            if (Pets.Count >= MaximumPets)
            {
                StatusMessage = "桌面上最多可以养两只甲虫。";
                return false;
            }

            int index = Pets.Count;
            var pet = new PetState
            {
                Name = index == 0 || Pets[0].Name == "阿壳" ? "小角" : "阿壳",
                Position = index == 0 ? new Vector2(0.35f, 0.38f) : new Vector2(0.48f, 0.42f),
                Heading = Range(0f, 360f),
                ActionTime = Range(1f, 3f),
                Iridescent = Random01() < 0.1f,
                UntilOverturn = Range(1500f, 2100f)
            };
            Pets.Add(pet);
            StatusMessage = index == 0 ? "小角来桌面上陪你了。" : "阿壳也来了，它们可以一起散步和切磋。";
            return true;
        }

        public bool RemovePet(int index)
        {
            if (Pets.Count <= 1 || index < 0 || index >= Pets.Count)
                return false;
            CancelFight();
            string name = Pets[index].Name;
            Pets.RemoveAt(index);
            StatusMessage = name + "回去休息了。";
            return true;
        }

        public bool RenamePet(int index, string name)
        {
            if (index < 0 || index >= Pets.Count)
            {
                StatusMessage = "没有找到这只甲虫。";
                return false;
            }
            if (name != null)
                foreach (char character in name)
                    if (char.IsControl(character) || character == '\u2028' || character == '\u2029')
                    {
                        StatusMessage = "名字不能包含换行或控制字符。";
                        return false;
                    }
            string trimmed = name?.Trim() ?? "";
            int length = new StringInfo(trimmed).LengthInTextElements;
            if (length == 0 || length > 12)
            {
                StatusMessage = "请给甲虫取一个 1 到 12 个字的名字。";
                return false;
            }
            Pets[index].Name = trimmed;
            StatusMessage = "以后就叫它" + trimmed + "啦。";
            return true;
        }

        public bool RightPet(int index)
        {
            if (index < 0 || index >= Pets.Count || !Pets[index].Overturned)
                return false;
            PetState pet = Pets[index];
            pet.Overturned = false;
            pet.UntilOverturn = Range(1500f, 2100f);
            if (pet.Action != "held")
            {
                SetAction(pet, "startled", 0.9f);
                pet.Target = pet.Position;
            }
            StatusMessage = "扶正了" + pet.Name + "，它正在重新站稳。";
            return true;
        }

        public void Feed()
        {
            Food = Clamp01(Food + 0.7f);
            StatusMessage = "食盆添满啦，饿了的甲虫会自己过来吃。";
            foreach (PetState pet in Pets)
            {
                if (!pet.Overturned && pet.StillRemaining <= 0f && pet.Action != "held" && pet.Action != "fight" && pet.Hunger >= 0.3f)
                    pet.ActionTime = 0f;
            }
        }

        public void MoveBowl(Vector2 position)
        {
            BowlPosition = ClampPlacedPosition(position);
            foreach (PetState pet in Pets)
                if (pet.Action != "held") pet.Position = OutsideBowl(pet.Position);
        }

        public void SetPlacementBounds(Vector2 minimum, Vector2 maximum)
        {
            if (!Finite(minimum.X) || !Finite(minimum.Y) || !Finite(maximum.X) || !Finite(maximum.Y) ||
                minimum.X >= maximum.X || minimum.Y >= maximum.Y)
                return;
            placementMinimum = minimum;
            placementMaximum = maximum;
            BowlPosition = ClampPlacedPosition(BowlPosition);
            foreach (PetState pet in Pets) pet.Position = ClampPlacedPosition(pet.Position);
        }

        private Vector2 ClampPlacedPosition(Vector2 position) => new Vector2(
            Finite(position.X) ? Clamp(position.X, placementMinimum.X, placementMaximum.X) : 0.5f,
            Finite(position.Y) ? Clamp(position.Y, placementMinimum.Y, placementMaximum.Y) : 0.5f);

        public void Hold(int index, Vector2 position)
        {
            if (index < 0 || index >= Pets.Count)
                return;
            if (FightInProgress)
                CancelFight();
            PetState pet = Pets[index];
            pet.Position = ClampPlacedPosition(position);
            // Picking it up is the only interaction that interrupts a still bout.
            // On release the usual landing recovery leads to a new action choice.
            pet.StillRemaining = 0f;
            pet.Action = "held";
            pet.Speed = 0f;
            pet.TurnRate = 0f;
            pet.LookYaw = 0f;
            pet.SeekingFood = false;
            pet.ActionTime = 0f;
        }

        public void Release(int index)
        {
            if (index < 0 || index >= Pets.Count || Pets[index].Action != "held")
                return;
            Pets[index].Position = OutsideBowl(Pets[index].Position);
            if (Pets[index].Overturned)
            {
                SetAction(Pets[index], "overturned", 0f);
                StatusMessage = Pets[index].Name + "还没有翻过来，左键点它帮帮忙。";
                return;
            }
            SetAction(Pets[index], "startled", Range(0.7f, 1.2f));
            Pets[index].Target = Pets[index].Position;
            StatusMessage = Pets[index].Name + "落地了，正在整理小脚。";
        }

        public bool RequestFight()
        {
            if (Paused)
            {
                StatusMessage = "先继续活动，再让甲虫切磋吧。";
                return false;
            }
            if (Pets.Count < 2)
            {
                StatusMessage = "先领养第二只甲虫，它们就能切磋了。";
                return false;
            }
            if (FightInProgress)
                return false;
            if (fightCooldown > 0f)
            {
                StatusMessage = "它们刚玩过，让它们休息一会儿。";
                return false;
            }
            foreach (PetState pet in Pets)
            {
                if (pet.StillRemaining > 0f)
                {
                    StatusMessage = "甲虫正在静止，等它自己恢复活动后再切磋吧。";
                    return false;
                }
                if (pet.Overturned)
                {
                    StatusMessage = "先左键点一下翻倒的甲虫，帮它站起来吧。";
                    return false;
                }
                if (pet.Action == "held" || pet.Energy < 0.2f || pet.Hunger > 0.9f)
                {
                    StatusMessage = "先把甲虫放下，并让它们吃饱、休息好。";
                    return false;
                }
            }
            BeginFight();
            return true;
        }

        public void Tick(float deltaTime, float screenAspect)
        {
            if (Paused || !Finite(deltaTime) || deltaTime <= 0f)
                return;
            aspect = Finite(screenAspect) ? Clamp(screenAspect, 0.3f, 8f) : 16f / 9f;
            VisualLength = Finite(VisualLength) ? Clamp(VisualLength, 0.04f, 0.7f) : 0.20f;
            BowlRadius = Finite(BowlRadius) ? Clamp(BowlRadius, 0.005f, 0.25f) : 0.052f;
            // A resumed/suspended desktop must not fast-forward an entire afternoon.
            float remaining = Math.Min(deltaTime, 30f);
            while (remaining > 0.00001f)
            {
                float step = Math.Min(remaining, 0.05f);
                TickStep(step);
                remaining -= step;
            }
        }

        private void TickStep(float dt)
        {
            fightCooldown = Math.Max(0f, fightCooldown - dt);
            autoFightTimer -= dt;
            Food = Clamp01(Food);
            foreach (PetState pet in Pets)
            {
                pet.Position = pet.Action == "held" ? ClampPlacedPosition(pet.Position) : OutsideBowl(pet.Position);
                pet.StepStartPosition = pet.Position;
                pet.StepStartHeading = pet.Heading;
                pet.DesiredLookYaw = 0f;
                pet.Hunger = Clamp01(pet.Hunger + dt / 660f);
                pet.Energy = Clamp01(pet.Energy + dt * (pet.Action == "sleep" ? 0.018f : -0.00075f));
                pet.UntilOverturn = Math.Max(0f, pet.UntilOverturn - dt);
                if (!pet.Overturned && pet.StillRemaining <= 0f && pet.UntilOverturn <= 0f && !FightInProgress &&
                    pet.Action != "held" && pet.Action != "eat" && pet.Action != "sleep")
                {
                    pet.Overturned = true;
                    pet.SeekingFood = false;
                    SetAction(pet, "overturned", 0f);
                    StatusMessage = pet.Name + "不小心翻过来了，左键点它就能帮它站起来。";
                }
            }

            if (FightInProgress)
            {
                TickFight(dt);
                FinishMotion(dt);
                return;
            }

            for (int i = 0; i < Pets.Count; i++)
                TickPet(Pets[i], i, dt);
            SeparatePets(dt);
            foreach (PetState pet in Pets)
                if (pet.Action != "held") pet.Position = OutsideBowl(pet.Position);
            FinishMotion(dt);

            if (autoFightTimer <= 0f)
            {
                autoFightTimer = Range(30f, 65f);
                if (CanAutoFight())
                    BeginFight();
            }
        }

        private void TickPet(PetState pet, int index, float dt)
        {
            if (pet.Action == "held")
                return;
            if (pet.Overturned)
            {
                pet.Action = "overturned";
                pet.Speed = 0f;
                return;
            }
            if (pet.StillRemaining > 0f)
            {
                pet.StillRemaining = Math.Max(0f, pet.StillRemaining - dt);
                pet.Action = "still";
                pet.Speed = pet.TurnRate = pet.LookYaw = 0f;
                if (pet.StillRemaining <= 0f)
                {
                    SetAction(pet, "inspect", 2f);
                    StatusMessage = pet.Name + "静止结束，继续活动了。";
                }
                return;
            }
            pet.ActionTime -= dt;
            if (pet.Action == "eat")
            {
                pet.Speed = 0f;
                Vector2 eatingSpot = EatingSpot(index);
                if (Distance(pet.Position, eatingSpot) > 0.045f)
                {
                    SeekFood(pet, index);
                    return;
                }
                Face(pet, BowlPosition, dt);
                float consumed = Math.Min(Food, dt * 0.024f);
                Food -= consumed;
                pet.Hunger = Clamp01(pet.Hunger - consumed * 2.7f);
                pet.Energy = Clamp01(pet.Energy + dt * 0.008f);
                if (Food <= 0.0001f || pet.Hunger <= 0.035f)
                {
                    pet.SeekingFood = false;
                    SetAction(pet, "groom", Range(3f, 5f));
                    StatusMessage = pet.Name + (Food <= 0.0001f ? "吃完了最后一口，食盆空了。" : "吃饱了，开始擦擦触角。 ");
                }
                return;
            }

            if (pet.Action == "sleep")
            {
                pet.Speed = 0f;
                if (pet.Energy >= 0.94f || (pet.ActionTime <= 0f && pet.Energy >= 0.62f) || pet.Hunger > 0.9f)
                    SetAction(pet, "inspect", Range(2f, 4f));
                return;
            }

            if (pet.Action == "wander" || pet.Action == "startled")
            {
                if (pet.SeekingFood)
                {
                    if (Food <= 0.0001f)
                    {
                        pet.SeekingFood = false;
                        SetAction(pet, "inspect", 2f);
                        return;
                    }
                    pet.Target = EatingSpot(index);
                }
                // Body lengths per second keep the crawl equally deliberate on
                // different desktop sizes (the host draws a 164-DIP-long beetle).
                float speed = VisualLength * (pet.Action == "startled" ? 0.12f : pet.SeekingFood ? 0.18f : 0.095f);
                bool arrived = Move(pet, pet.Target, speed, dt);
                if (arrived && pet.SeekingFood)
                {
                    SetAction(pet, "eat", 30f);
                    return;
                }
                if (arrived || pet.ActionTime <= 0f)
                {
                    if (pet.SeekingFood)
                        pet.ActionTime = 12f;
                    else if (pet.Action == "startled" && pet.ActionTime > 0f)
                        pet.Speed = 0f;
                    else if (pet.Action == "wander")
                        SetAction(pet, "inspect", Range(1.8f, 3.4f));
                    else
                        ChooseAction(pet, index);
                }
                return;
            }

            pet.Speed = 0f;
            if (pet.ActionTime <= 0f || (pet.Hunger > 0.78f && Food > 0f) || pet.Energy < 0.18f)
                ChooseAction(pet, index);
        }

        private void ChooseAction(PetState pet, int index)
        {
            if (pet.Hunger >= 0.3f && Food > 0.0001f)
            {
                SeekFood(pet, index);
                return;
            }
            if (pet.Energy < 0.28f)
            {
                SetAction(pet, "sleep", Range(25f, 38f));
                return;
            }

            float choice = Random01();
            if (choice < 0.48f)
            {
                float angle = Range(0f, (float)Math.PI * 2f);
                float distance = VisualLength * Range(0.35f, 0.8f);
                pet.Target = ClampPosition(pet.Position + new Vector2((float)Math.Cos(angle) * distance / aspect, (float)Math.Sin(angle) * distance));
                SetAction(pet, "wander", Range(7f, 13f));
            }
            else if (choice < 0.66f)
                SetAction(pet, "groom", Range(3.2f, 6.5f));
            else if (choice < 0.82f)
                SetAction(pet, "inspect", Range(2.5f, 4.5f));
            else if (choice < 0.87f && pet.Energy < 0.8f)
                SetAction(pet, "sleep", Range(12f, 22f));
            else if (choice >= 0.87f && choice < 0.94f)
            {
                pet.StillRemaining = 180f;
                pet.SeekingFood = false;
                pet.Target = pet.Position;
                pet.TurnRate = pet.LookYaw = 0f;
                SetAction(pet, "still", 180f);
                StatusMessage = pet.Name + "安静地停下来了，过3分钟会自己继续活动。";
            }
            else
                SetAction(pet, "idle", Range(2f, 5.5f));
        }

        private void SeekFood(PetState pet, int index)
        {
            pet.SeekingFood = true;
            pet.Target = EatingSpot(index);
            SetAction(pet, "wander", 20f);
        }

        private Vector2 EatingSpot(int index)
        {
            float radius = VisualLength * 0.50f + BowlRadius;
            float x = index == 0 ? -radius : radius;
            Vector2 preferred = BowlPosition + new Vector2(x / aspect, -0.01f);
            if (BowlPosition.X >= 0.08f && BowlPosition.X <= 0.92f && BowlPosition.Y >= 0.08f && BowlPosition.Y <= 0.92f)
                return ClampPlacedPosition(preferred);
            // Approach a rim-side bowl from the screen's interior, with separate
            // angles for the two pets instead of clamping both onto the same spot.
            float inward = (float)Math.Atan2(0.5f - BowlPosition.Y, (0.5f - BowlPosition.X) * aspect);
            float angle = inward + (index == 0 ? -0.42f : 0.42f);
            return ClampPlacedPosition(BowlPosition + new Vector2((float)Math.Cos(angle) * radius / aspect, (float)Math.Sin(angle) * radius));
        }

        private bool CanAutoFight()
        {
            if (Pets.Count != 2 || fightCooldown > 0f || Distance(Pets[0].Position, Pets[1].Position) > 0.24f)
                return false;
            foreach (PetState pet in Pets)
            {
                if (pet.Overturned || pet.StillRemaining > 0f || pet.Action == "held" || pet.Action == "sleep" || pet.Action == "eat" || pet.SeekingFood || pet.Energy < 0.4f || pet.Hunger > 0.7f)
                    return false;
            }
            return Random01() < 0.6f;
        }

        private void BeginFight()
        {
            fightPhase = 1;
            fightTime = 0f;
            fightCenter = ClampPosition((Pets[0].Position + Pets[1].Position) * 0.5f, 0.14f);
            Vector2 delta = Pets[1].Position - Pets[0].Position;
            fightAxis = (float)Math.Atan2(delta.Y, delta.X * aspect);
            foreach (PetState pet in Pets)
            {
                pet.SeekingFood = false;
                SetAction(pet, "wander", 20f);
            }
            StatusMessage = "两只甲虫正在靠近，准备友好切磋。";
        }

        private void TickFight(float dt)
        {
            if (Pets.Count != 2 || Pets[0].Action == "held" || Pets[1].Action == "held")
            {
                CancelFight();
                return;
            }
            fightTime += dt;
            float separation = fightPhase == 1 ? VisualLength * 0.60f
                : VisualLength * (0.47f + 0.02f * (float)Math.Sin(fightTime * 9f));
            Vector2 axis = new Vector2((float)Math.Cos(fightAxis) / aspect, (float)Math.Sin(fightAxis));
            Vector2 first = ClampPosition(fightCenter - axis * separation);
            Vector2 second = ClampPosition(fightCenter + axis * separation);
            if (fightPhase == 1)
            {
                bool firstReady = Move(Pets[0], first, VisualLength * 0.23f, dt);
                bool secondReady = Move(Pets[1], second, VisualLength * 0.23f, dt);
                if (firstReady)
                    Face(Pets[0], Pets[1].Position, dt);
                if (secondReady)
                    Face(Pets[1], Pets[0].Position, dt);
                if (firstReady && secondReady)
                {
                    fightPhase = 2;
                    fightTime = 0f;
                    SetAction(Pets[0], "fight", 5f);
                    SetAction(Pets[1], "fight", 5f);
                    StatusMessage = "顶一顶、推一推，只是玩耍，不会受伤。";
                }
                else if (fightTime > 30f)
                    CancelFight();
                return;
            }

            Move(Pets[0], first, VisualLength * 0.12f, dt);
            Move(Pets[1], second, VisualLength * 0.12f, dt);
            Face(Pets[0], Pets[1].Position, dt);
            Face(Pets[1], Pets[0].Position, dt);
            if (fightTime >= 4.8f)
                CompleteFight(axis);
        }

        private void CompleteFight(Vector2 axis)
        {
            float firstScore = Pets[0].Energy * 0.25f + Random01();
            float secondScore = Pets[1].Energy * 0.25f + Random01();
            int winnerIndex = firstScore >= secondScore ? 0 : 1;
            PetState winner = Pets[winnerIndex];
            PetState loser = Pets[1 - winnerIndex];
            winner.Wins++;
            LastFightWinner = winner.Name;
            foreach (PetState pet in Pets)
            {
                pet.Energy = Clamp01(pet.Energy - 0.07f);
                pet.Hunger = Clamp01(pet.Hunger + 0.035f);
            }
            SetAction(winner, "inspect", 3f);
            SetAction(loser, "startled", 4f);
            loser.Target = ClampPosition(loser.Position + axis * (winnerIndex == 0 ? 0.16f : -0.16f));
            fightPhase = 0;
            fightCooldown = DuelCooldown;
            autoFightTimer = Range(55f, 90f);
            StatusMessage = winner.Name + "赢了这一回！两只都平安，休息一下再玩。";
        }

        private void CancelFight()
        {
            if (!FightInProgress)
                return;
            fightPhase = 0;
            fightCooldown = Math.Max(fightCooldown, 10f);
            autoFightTimer = Range(40f, 70f);
            foreach (PetState pet in Pets)
            {
                if (pet.Action != "held")
                    SetAction(pet, "inspect", 2f);
            }
        }

        private void SeparatePets(float dt)
        {
            if (Pets.Count != 2)
                return;
            PetState first = Pets[0];
            PetState second = Pets[1];
            if (first.Action == "held" || second.Action == "held")
                return;
            Vector2 delta = second.Position - first.Position;
            float distance = Distance(first.Position, second.Position);
            float personalSpace = VisualLength * 0.5f;
            if (distance >= personalSpace)
                return;
            if (distance < 0.001f)
                delta = new Vector2(1f / aspect, 0f);
            else
                delta = delta / distance;
            float amount = Math.Min((personalSpace - distance) * 0.5f, dt * VisualLength * 0.075f);
            // A sleeping or feeding pet stays put; its neighbor walks around it.
            bool firstStill = first.Overturned || first.StillRemaining > 0f || first.Action == "sleep" || first.Action == "eat";
            bool secondStill = second.Overturned || second.StillRemaining > 0f || second.Action == "sleep" || second.Action == "eat";
            if (!firstStill)
                first.Position = ClampPlacedPosition(first.Position - delta * amount * (secondStill ? 2f : 1f));
            if (!secondStill)
                second.Position = ClampPlacedPosition(second.Position + delta * amount * (firstStill ? 2f : 1f));
        }

        private bool Move(PetState pet, Vector2 target, float speed, float dt)
        {
            float distance = Distance(pet.Position, target);
            // Do not snap the body sideways onto the final pixel of a path.
            if (distance <= VisualLength * 0.004f)
            {
                pet.Speed = 0f;
                return true;
            }
            Vector2 steeringTarget = RouteAroundBowl(pet.Position, target);
            Face(pet, steeringTarget, dt);

            float headingError = Math.Abs(HeadingDifference(HeadingTo(pet.Position, steeringTarget), pet.Heading));
            float alignment = headingError >= 65f ? 0f : (float)Math.Cos(headingError * Math.PI / 180.0);
            alignment *= alignment;
            // Slow down before a tight turn or arrival. Translation follows the body,
            // so feet can stay planted instead of skating sideways toward the target.
            float desiredSpeed = speed * alignment * Clamp(distance / (VisualLength * 0.125f), 0f, 1f);
            float acceleration = VisualLength * (desiredSpeed > pet.Speed ? 0.12f : 0.2f);
            float nextSpeed = Approach(pet.Speed, desiredSpeed, acceleration * dt);
            float radians = pet.Heading * (float)Math.PI / 180f;
            Vector2 forward = new Vector2((float)Math.Sin(radians) / aspect, (float)Math.Cos(radians));
            pet.Position = OutsideBowl(pet.Position + forward * Math.Min(nextSpeed * dt, distance));
            pet.Speed = nextSpeed;
            return false;
        }

        private float BowlClearance => BowlRadius + VisualLength * 0.40f;

        private Vector2 OutsideBowl(Vector2 position)
        {
            position = ClampPlacedPosition(position);
            Vector2 delta = new Vector2((position.X - BowlPosition.X) * aspect, position.Y - BowlPosition.Y);
            float radius = BowlClearance;
            if (delta.LengthSquared() >= radius * radius - 0.000001f) return position;
            if (delta.LengthSquared() < 0.000001f)
                delta = new Vector2((0.5f - BowlPosition.X) * aspect, 0.5f - BowlPosition.Y);
            if (delta.LengthSquared() < 0.000001f) delta = Vector2.UnitX;
            delta = Vector2.Normalize(delta) * radius;
            Vector2 result = BowlPosition + new Vector2(delta.X / aspect, delta.Y);
            if (result.X < placementMinimum.X || result.X > placementMaximum.X || result.Y < placementMinimum.Y || result.Y > placementMaximum.Y)
            {
                delta = new Vector2((0.5f - BowlPosition.X) * aspect, 0.5f - BowlPosition.Y);
                if (delta.LengthSquared() < 0.000001f) delta = Vector2.UnitX;
                delta = Vector2.Normalize(delta) * radius;
                result = BowlPosition + new Vector2(delta.X / aspect, delta.Y);
            }
            return ClampPlacedPosition(result);
        }

        private Vector2 RouteAroundBowl(Vector2 position, Vector2 destination)
        {
            Vector2 from = new Vector2((position.X - BowlPosition.X) * aspect, position.Y - BowlPosition.Y);
            Vector2 to = new Vector2((destination.X - BowlPosition.X) * aspect, destination.Y - BowlPosition.Y);
            Vector2 direction = to - from;
            float lengthSquared = direction.LengthSquared();
            if (lengthSquared < 0.000001f) return destination;
            float along = Clamp(-Vector2.Dot(from, direction) / lengthSquared, 0f, 1f);
            float radius = BowlClearance + VisualLength * 0.055f;
            if (along <= 0f || (from + direction * along).LengthSquared() >= radius * radius)
                return destination;
            float cross = from.X * to.Y - from.Y * to.X;
            float angle = (float)Math.Atan2(from.Y, from.X) + (cross >= 0f ? 0.65f : -0.65f);
            return ClampPlacedPosition(BowlPosition + new Vector2((float)Math.Cos(angle) * radius / aspect, (float)Math.Sin(angle) * radius));
        }

        private void Face(PetState pet, Vector2 target, float dt)
        {
            Vector2 delta = target - pet.Position;
            if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 0.00001f)
                return;
            float difference = HeadingDifference(HeadingTo(pet.Position, target), pet.Heading);
            pet.DesiredLookYaw = Clamp(difference * 0.32f, -18f, 18f);
            float turnSpeed = 60f - 10f * Clamp01(pet.Speed / (VisualLength * 0.18f));
            pet.Heading = (pet.Heading + Clamp(difference, -turnSpeed * dt, turnSpeed * dt) + 360f) % 360f;
        }

        private void FinishMotion(float dt)
        {
            foreach (PetState pet in Pets)
            {
                float distance = Distance(pet.StepStartPosition, pet.Position);
                pet.Speed = pet.Action == "held" ? 0f : distance / dt;
                pet.TravelDistance += distance;
                pet.TurnRate = HeadingDifference(pet.Heading, pet.StepStartHeading) / dt;
                pet.LookYaw += (pet.DesiredLookYaw - pet.LookYaw) * (1f - (float)Math.Exp(-8f * dt));
            }
        }

        private float HeadingTo(Vector2 from, Vector2 to) =>
            (float)(Math.Atan2((to.X - from.X) * aspect, to.Y - from.Y) * 180.0 / Math.PI);
        private static float HeadingDifference(float target, float current) => ((target - current + 540f) % 360f) - 180f;
        private static float Approach(float current, float target, float amount) => current + Clamp(target - current, -amount, amount);

        private static void SetAction(PetState pet, string action, float duration)
        {
            pet.Action = action;
            pet.ActionTime = duration;
            pet.Speed = 0f;
        }

        private float Distance(Vector2 first, Vector2 second)
        {
            float x = (first.X - second.X) * aspect;
            float y = first.Y - second.Y;
            return (float)Math.Sqrt(x * x + y * y);
        }

        private float Random01()
        {
            randomState ^= randomState << 13;
            randomState ^= randomState >> 17;
            randomState ^= randomState << 5;
            return (randomState & 0x00ffffffu) / 16777216f;
        }

        private float Range(float minimum, float maximum) => minimum + Random01() * (maximum - minimum);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float Clamp01(float value) => Finite(value) ? Clamp(value, 0f, 1f) : 0.5f;
        private static float Clamp(float value, float minimum, float maximum) => Math.Max(minimum, Math.Min(maximum, value));
        private static Vector2 ClampPosition(Vector2 value, float margin = 0.045f) => new Vector2(
            Finite(value.X) ? Clamp(value.X, margin, 1f - margin) : 0.5f,
            Finite(value.Y) ? Clamp(value.Y, margin, 1f - margin) : 0.5f);

        public PetSaveData CaptureSaveData()
        {
            var data = new PetSaveData
            {
                BowlPosition = BowlPosition,
                Food = Food,
                Paused = Paused,
                FightCooldown = FightInProgress ? DuelCooldown : fightCooldown,
                RandomState = randomState
            };
            foreach (PetState pet in Pets)
            {
                data.Pets.Add(new PetState
                {
                    Position = pet.Position,
                    Heading = pet.Heading,
                    Hunger = pet.Hunger,
                    Energy = pet.Energy,
                    Name = pet.Name,
                    ShowName = pet.ShowName,
                    NameColorRgb = pet.NameColorRgb,
                    Wins = pet.Wins,
                    Iridescent = pet.Iridescent,
                    Overturned = pet.Overturned,
                    UntilOverturn = pet.UntilOverturn,
                    StillRemaining = pet.StillRemaining,
                    Action = pet.Overturned ? "overturned" : pet.StillRemaining > 0f ? "still" : "idle"
                });
            }
            return data;
        }

        public static PetSimulation Restore(PetSaveData data)
        {
            var simulation = new PetSimulation();
            if (data == null || data.Version != 1 || data.Pets == null || data.Pets.Count == 0)
                return simulation;
            simulation.Pets.Clear();
            // Host applies the current display bounds before showing the sprites.
            // Preserve legitimate edge placements until those bounds are known.
            simulation.BowlPosition = ClampPosition(data.BowlPosition, -8f);
            simulation.Food = Clamp01(data.Food);
            simulation.Paused = data.Paused;
            simulation.fightCooldown = Finite(data.FightCooldown) ? Clamp(data.FightCooldown, 0f, DuelCooldown) : 0f;
            if (data.RandomState != 0u)
                simulation.randomState = data.RandomState;
            foreach (PetState saved in data.Pets)
            {
                if (saved == null || simulation.Pets.Count >= MaximumPets)
                    continue;
                var pet = new PetState
                {
                    Position = ClampPosition(saved.Position, -8f),
                    Heading = Finite(saved.Heading) ? (saved.Heading % 360f + 360f) % 360f : 0f,
                    Hunger = Clamp01(saved.Hunger),
                    Energy = Clamp01(saved.Energy),
                    Name = string.IsNullOrWhiteSpace(saved.Name) ? (simulation.Pets.Count == 0 ? "小角" : "阿壳") : saved.Name,
                    ShowName = saved.ShowName,
                    NameColorRgb = saved.NameColorRgb >= 0 && saved.NameColorRgb <= 0xFFFFFF ? saved.NameColorRgb : 0xE5EEDC,
                    Wins = Math.Max(0, saved.Wins),
                    Iridescent = saved.Iridescent,
                    Overturned = saved.Overturned,
                    UntilOverturn = Finite(saved.UntilOverturn) ? Clamp(saved.UntilOverturn, 0f, 2100f) : 1800f,
                    StillRemaining = Finite(saved.StillRemaining) ? Clamp(saved.StillRemaining, 0f, 180f) : 0f,
                    Action = saved.Overturned ? "overturned" : Finite(saved.StillRemaining) && saved.StillRemaining > 0f ? "still" : "idle",
                    ActionTime = simulation.Range(1f, 3f)
                };
                simulation.Pets.Add(pet);
            }
            if (simulation.Pets.Count == 0)
                simulation.AddPet();
            simulation.StatusMessage = "甲虫们回来了，食盆和养成进度已恢复。";
            return simulation;
        }
    }
}

