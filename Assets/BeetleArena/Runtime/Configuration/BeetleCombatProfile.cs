using System;
using UnityEngine;

namespace BeetleArena
{
    public enum BeetleCombatArchetype { HornWrestling, MandibleGrappling }
    /// <summary>Species combat/model profile, decoupled from husbandry attributes. Each new model requires calibrating this asset against its skeleton.</summary>
    [CreateAssetMenu(menuName = "BugDuel/Combat Profile", fileName = "BeetleCombatProfile")]
    public sealed class BeetleCombatProfile : ScriptableObject
    {
        public const string LegacyRigId = "bugduel.horn-rig.v1";
        public BeetleSpeciesAsset species;
        public BeetleCombatArchetype archetype = BeetleCombatArchetype.HornWrestling;
        [Tooltip("Must match rigId of scene Actor. This build implements horn-wrestling behavior only; mandible combat requires bespoke motion/contact adapters.")]
        public string rigId = LegacyRigId;
        public BeetleActor actorPrefab;
        [Range(.4f, .9f)] public float visualScale = .70f;
        [Range(.85f, 1.15f)] public float size = 1;
        [Min(.01f)] public float normalizedMass = 1, strength = 1, grip = 1;
        [Min(1)] public float endurance = 100;
        [Min(.01f)] public float hornReach = 1.65f;
        [Range(.84f, 1.20f)] public float hornLengthFactor = 1;
        [Tooltip("Model-local six-foot positions: Left/Right Front/Middle/Hind. Must be calibrated independently.")]
        public Vector3[] supportFootOffsets = Array.Empty<Vector3>();
        [Tooltip("When checked, uses this list exclusively; empty list denotes no modules. Otherwise inherits action list from MatchSettings.")]
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
