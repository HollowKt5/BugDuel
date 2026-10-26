using System;
using System.Collections.Generic;
using BugDuel.Domain;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>Explicit catalog: errors on missing/duplicate IDs without silently remapping legacy saves to another species.</summary>
    [CreateAssetMenu(menuName = "BugDuel/Species Catalog", fileName = "BeetleSpeciesCatalog")]
    public sealed class BeetleSpeciesCatalog : ScriptableObject
    {
        public BeetleSpeciesAsset[] species = Array.Empty<BeetleSpeciesAsset>();
        public BeetleCombatProfile[] combatProfiles = Array.Empty<BeetleCombatProfile>();

        public IReadOnlyDictionary<string, SpeciesDefinition> BuildDefinitions()
        {
            var result = new Dictionary<string, SpeciesDefinition>(StringComparer.Ordinal);
            foreach (var asset in species ?? Array.Empty<BeetleSpeciesAsset>())
            {
                if (!asset) throw new InvalidOperationException("Species catalog contains a missing definition.");
                var definition = asset.ToDefinition();
                if (result.ContainsKey(definition.Id)) throw new InvalidOperationException("Duplicate species ID: " + definition.Id);
                result.Add(definition.Id, definition);
            }
            return new System.Collections.ObjectModel.ReadOnlyDictionary<string, SpeciesDefinition>(result);
        }

        public IReadOnlyDictionary<string, BeetleCombatProfile> BuildCombatProfiles()
        {
            var definitions = BuildDefinitions();
            var result = new Dictionary<string, BeetleCombatProfile>(StringComparer.Ordinal);
            foreach (var profile in combatProfiles ?? Array.Empty<BeetleCombatProfile>())
            {
                if (!profile) throw new InvalidOperationException("Species catalog contains a missing combat profile.");
                profile.ValidateForMatch();
                string id = profile.species.speciesId;
                if (!definitions.ContainsKey(id) || Array.IndexOf(species, profile.species) < 0)
                    throw new InvalidOperationException("Combat profile must reference the catalog's species asset: " + id);
                if (result.ContainsKey(id)) throw new InvalidOperationException("Duplicate combat profile for: " + id);
                result.Add(id, profile);
            }
            return new System.Collections.ObjectModel.ReadOnlyDictionary<string, BeetleCombatProfile>(result);
        }
    }
}
