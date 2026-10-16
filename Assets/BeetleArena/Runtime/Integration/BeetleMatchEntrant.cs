using System;
using BugDuel.Domain;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>养殖 → 战斗的单向快照。没有个体引用、库存、存档或服务引用。</summary>
    public sealed class BeetleMatchEntrant
    {
        public string IndividualId { get; }
        public string SpeciesId { get; }
        public string DisplayName { get; }
        public string RigId { get; }
        public float VisualScale { get; }
        public bool OverridesMotionModules { get; }
        readonly BeetleTraits traits;
        readonly Vector3[] feet;
        readonly BeetleMotionModule[] modules;
        internal BeetleMotionModule[] ConfiguredModules => modules;
        internal Vector3 SupportFoot(int slot) => feet[slot];
        internal BeetleTraits CreateTraits() => traits.Copy();

        BeetleMatchEntrant(BeetleIndividual individual, BeetleCombatProfile profile, BeetleTraits traits)
        {
            IndividualId = individual.Id;
            SpeciesId = individual.SpeciesId;
            DisplayName = individual.DisplayName;
            RigId = profile.rigId;
            VisualScale = profile.visualScale;
            OverridesMotionModules = profile.overrideMotionModules;
            this.traits = traits.Copy();
            feet = (Vector3[])profile.supportFootOffsets.Clone();
            modules = profile.motionModules == null ? Array.Empty<BeetleMotionModule>() : (BeetleMotionModule[])profile.motionModules.Clone();
        }

        public static BeetleMatchEntrant FromIndividual(BeetleIndividual individual, BeetleCombatProfile profile)
            => FromIndividual(individual, profile, profile && profile.species ? profile.species.ToDefinition() : null);

        // 热更数据定义与静态模型标定独立：不要求修改 ScriptableObject 资产来更新饲养数值。
        public static BeetleMatchEntrant FromIndividual(BeetleIndividual individual, BeetleCombatProfile profile, SpeciesDefinition definition)
        {
            if (individual == null) throw new ArgumentNullException(nameof(individual));
            if (!profile) throw new ArgumentNullException(nameof(profile));
            profile.ValidateForMatch();
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (profile.species.speciesId != definition.Id) throw new InvalidOperationException("Combat profile and live species definition do not match.");
            if (individual.SpeciesId != definition.Id) throw new InvalidOperationException("Individual and combat profile species do not match.");
            if (!definition.CombatEnabled || individual.Stage != LifeStage.Adult || individual.IsOverturned || individual.Condition.Health <= 0 ||
                individual.Condition.Health < profile.minimumCombatHealth || individual.Condition.Stress > profile.maximumCombatStress ||
                individual.Condition.Nutrition <= 0 || individual.Condition.Hydration <= 0)
                throw new InvalidOperationException("Only healthy, nourished adult beetles eligible for combat may enter.");
            var potential = individual.Traits;
            float readiness = Mathf.Clamp01((float)((individual.Condition.Health + individual.Condition.Nutrition + individual.Condition.Hydration) / 300));
            // 显式游戏映射；潜力不在比赛构造时再次随机抽取。物理质量与展示毫米/克各自独立。
            var traits = new BeetleTraits
            {
                size = profile.size, lengthMm = definition.AdultLengthMm * profile.size,
                massGrams = definition.AdultMassGrams * profile.size * profile.size * profile.size,
                mass = profile.normalizedMass * profile.size * profile.size * profile.size,
                strength = profile.strength * Mathf.Lerp(.8f, 1.2f, potential.Strength) * Mathf.Lerp(.65f, 1, readiness),
                grip = profile.grip * Mathf.Lerp(.8f, 1.2f, potential.Defense),
                endurance = profile.endurance * Mathf.Lerp(.8f, 1.2f, potential.Stamina) * Mathf.Lerp(.65f, 1, readiness),
                aggression = Mathf.Lerp(.32f, .95f, potential.Strength), agility = Mathf.Lerp(.72f, 1.40f, potential.Speed),
                hornReach = profile.hornReach * profile.size * profile.hornLengthFactor,
                hornLengthFactor = profile.hornLengthFactor,
                hornLengthMm = definition.AdultLengthMm * profile.size * .48f * profile.hornLengthFactor,
                aggressionScore = potential.Strength * 100, lengthScore = Mathf.InverseLerp(.85f, 1.15f, profile.size) * 100,
                hornScore = Mathf.InverseLerp(.84f, 1.20f, profile.hornLengthFactor) * 100, agilityScore = potential.Speed * 100,
                tempo = Mathf.Lerp(.9f, 1.25f, potential.Speed), drivePreference = potential.Strength,
                liftPreference = potential.Defense, lateralPreference = potential.Speed
            };
            return new BeetleMatchEntrant(individual, profile, traits);
        }
    }
}
