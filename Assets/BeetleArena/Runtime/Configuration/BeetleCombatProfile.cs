using System;
using UnityEngine;

namespace BeetleArena
{
    public enum BeetleCombatArchetype { HornWrestling, MandibleGrappling }
    /// <summary>物种的战斗/模型适配，与饲养参数分离。每种新模型都需要校准此配置与真实骨架。</summary>
    [CreateAssetMenu(menuName = "BugDuel/Combat Profile", fileName = "BeetleCombatProfile")]
    public sealed class BeetleCombatProfile : ScriptableObject
    {
        public const string LegacyRigId = "bugduel.horn-rig.v1";
        public BeetleSpeciesAsset species;
        public BeetleCombatArchetype archetype = BeetleCombatArchetype.HornWrestling;
        [Tooltip("必须与场景 Actor 的 rigId 一致。此版本仅实现角斗行为；大颚夹持需要另做动作和接触适配。")]
        public string rigId = LegacyRigId;
        public BeetleActor actorPrefab;
        [Range(.4f, .9f)] public float visualScale = .70f;
        [Range(.85f, 1.15f)] public float size = 1;
        [Min(.01f)] public float normalizedMass = 1, strength = 1, grip = 1;
        [Min(1)] public float endurance = 100;
        [Min(.01f)] public float hornReach = 1.65f;
        [Range(.84f, 1.20f)] public float hornLengthFactor = 1;
        [Tooltip("模型局部六足位置：左前/中/后、右前/中/后。必须独立校准。")]
        public Vector3[] supportFootOffsets = Array.Empty<Vector3>();
        [Tooltip("勾选后仅使用此列表；空列表表示无模块。否则继承 MatchSettings 的动作列表。")]
        public bool overrideMotionModules;
        public BeetleMotionModule[] motionModules = Array.Empty<BeetleMotionModule>();
        [Range(0, 100)] public float minimumCombatHealth = 50, maximumCombatStress = 80;

        public void ValidateForMatch()
        {
            if (!species) throw new InvalidOperationException("Combat profile requires a species definition.");
            if (archetype != BeetleCombatArchetype.HornWrestling)
                throw new InvalidOperationException("This combat runtime currently supports calibrated horn-wrestling rigs only. Implement a mandible combat adapter before enabling it.");
            species.ToDefinition();
            if (string.IsNullOrWhiteSpace(rigId)) throw new InvalidOperationException("Combat profile requires a rig ID.");
            if (actorPrefab && actorPrefab.rigId != rigId) throw new InvalidOperationException("Actor prefab rig ID does not match the combat profile.");
            RequireRange(visualScale, .4f, .9f, nameof(visualScale));
            RequireRange(size, .85f, 1.15f, nameof(size));
            RequireRange(normalizedMass, .1f, 10, nameof(normalizedMass));
            RequireRange(strength, .1f, 10, nameof(strength));
            RequireRange(grip, .1f, 10, nameof(grip));
            RequireRange(endurance, 1, 1000, nameof(endurance));
            RequireRange(hornReach, .1f, 5, nameof(hornReach));
            RequireRange(hornLengthFactor, .84f, 1.20f, nameof(hornLengthFactor));
            RequireRange(minimumCombatHealth, 0, 100, nameof(minimumCombatHealth));
            RequireRange(maximumCombatStress, 0, 100, nameof(maximumCombatStress));
            if (supportFootOffsets == null || supportFootOffsets.Length != 6)
                throw new InvalidOperationException("Calibrate exactly six support foot offsets before entering a species in combat.");
            foreach (var foot in supportFootOffsets)
                if (!float.IsFinite(foot.x) || !float.IsFinite(foot.y) || !float.IsFinite(foot.z))
                    throw new InvalidOperationException("Foot offsets must be finite.");
        }

        static void RequireRange(float value, float minimum, float maximum, string name)
        {
            if (!float.IsFinite(value) || value < minimum || value > maximum)
                throw new InvalidOperationException(name + " is outside the supported combat range.");
        }
    }
}
