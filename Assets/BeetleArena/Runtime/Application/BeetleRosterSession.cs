using System;
using System.Collections.Generic;
using System.Globalization;
using BugDuel.Domain;

namespace BeetleArena
{
    /// <summary>
    /// 应用层事务边界：拥有档案、参赛占用和结算账本。只在游戏主线程调用；不持有 UI、场景或文件路径。
    /// 读取与导入导出均复制，战斗只接收快照；领域服务不反向依赖此会话。
    /// </summary>
    public sealed class BeetleRosterSession
    {
        SpeciesCatalog catalog;
        public ContentCatalog Content { get; private set; }
        public FoodCatalog Foods { get; }
        readonly Dictionary<string, BeetleCombatProfile> profiles;
        readonly RecoveryPolicy recoveryPolicy;
        readonly HusbandryService husbandry = new HusbandryService();
        readonly ContestRecoveryService recovery = new ContestRecoveryService();
        Dictionary<string, BeetleIndividual> individuals = new Dictionary<string, BeetleIndividual>(StringComparer.Ordinal);
        HashSet<string> appliedMatchIds = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, string> habitatSelections = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly HashSet<string> occupied = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, ActiveMatch> activeMatches = new Dictionary<string, ActiveMatch>(StringComparer.Ordinal);

        sealed class ActiveMatch
        {
            internal BeetleSimulation Simulation;
            internal string FirstId, SecondId;
        }

        public BeetleRosterSession(SpeciesCatalog catalog, IDictionary<string, BeetleCombatProfile> combatProfiles,
            RecoveryPolicy recoveryPolicy = null, FoodCatalog foods = null) : this(new ContentCatalog(1,
                catalog?.Definitions ?? throw new ArgumentNullException(nameof(catalog)), HabitatDefinition.CreateDefaults()), combatProfiles, recoveryPolicy, foods) { }

        public BeetleRosterSession(ContentCatalog content, IDictionary<string, BeetleCombatProfile> combatProfiles,
            RecoveryPolicy recoveryPolicy = null, FoodCatalog foods = null)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            catalog = content.Species;
            Foods = foods ?? FoodCatalog.CreateDefaults();
            if (combatProfiles == null) throw new ArgumentNullException(nameof(combatProfiles));
            profiles = new Dictionary<string, BeetleCombatProfile>(combatProfiles, StringComparer.Ordinal);
            this.recoveryPolicy = recoveryPolicy ?? new RecoveryPolicy();
            // Installed combat adapters may be dormant until a later data package introduces their species.
            foreach (KeyValuePair<string, BeetleCombatProfile> entry in profiles)
                if (!entry.Value || !entry.Value.species || entry.Key != entry.Value.species.speciesId)
                    throw new ArgumentException("Combat adapter keys must match their authored species IDs.", nameof(combatProfiles));
        }

        public DomainResult TryUpdateContent(ContentCatalog content)
        {
            if (content == null || content.Version <= Content.Version)
                return DomainResult.Rejected("InvalidContentVersion", "A complete content snapshot with a strictly increasing version is required.");
            if (activeMatches.Count != 0) return DomainResult.Rejected("ActiveMatches", "Complete or cancel contests before publishing content.");
            var candidates = new Dictionary<string, BeetleIndividual>(StringComparer.Ordinal);
            foreach (BeetleIndividual individual in individuals.Values)
            {
                RestoreResult restored = BeetleSaveMapper.Restore(BeetleSaveMapper.Export(individual), content.Species);
                if (!restored.Success) return DomainResult.Rejected(restored.ErrorCode, restored.ErrorMessage);
                candidates.Add(individual.Id, restored.Individual);
            }
            foreach (KeyValuePair<string, string> selection in habitatSelections)
            {
                if (!content.TryGetHabitat(selection.Value, out HabitatDefinition habitat))
                    return DomainResult.Rejected("SelectedHabitatRemoved", "A selected habitat must remain available until its occupants are relocated.");
                BeetleIndividual occupant = candidates[selection.Key];
                if (occupant.Stage != LifeStage.Dead && !habitat.Allows(occupant.Stage))
                    return DomainResult.Rejected("SelectedHabitatIncompatible", "Relocate living occupants before publishing a habitat that excludes their current stage.");
            }
            // Single publication point; content and all validated records move to the new revision together.
            individuals = candidates;
            catalog = content.Species;
            Content = content;
            return DomainResult.Accepted();
        }

        public IReadOnlyList<HabitatDefinition> AvailableHabitats(string individualId)
        {
            if (individualId == null || !individuals.TryGetValue(individualId, out BeetleIndividual individual))
                return Array.Empty<HabitatDefinition>();
            var available = new List<HabitatDefinition>();
            foreach (HabitatDefinition habitat in Content.Habitats) if (habitat.Allows(individual.Stage)) available.Add(habitat);
            return available.AsReadOnly();
        }

        public string GetSelectedHabitatId(string individualId) => individualId != null && habitatSelections.TryGetValue(individualId, out string id) ? id : null;

        public DomainResult SelectHabitat(string individualId, string habitatId)
        {
            DomainResult invalid = Available(individualId, out BeetleIndividual individual);
            if (invalid != null) return invalid;
            if (!Content.TryGetHabitat(habitatId, out HabitatDefinition habitat) || !habitat.Allows(individual.Stage))
                return DomainResult.Rejected("HabitatUnavailable", "Choose a known habitat suitable for the individual's current life stage.");
            habitatSelections[individualId] = habitatId;
            return DomainResult.Accepted();
        }

        public DomainResult AdvanceInHabitat(string individualId, string habitatId, double elapsedDays)
        {
            DomainResult invalid = Available(individualId, out BeetleIndividual individual);
            if (invalid != null) return invalid;
            if (!Content.TryGetHabitat(habitatId, out HabitatDefinition habitat))
                return DomainResult.Rejected("HabitatUnavailable", "Choose a known habitat before advancing.");
            SpeciesDefinition species = Definition(individual.SpeciesId);
            double allowedDays = AllowedDays(individual, species, habitat);
            // A shortened stage may already be due in a stage-specific box. Complete that zero-time
            // boundary so the player can select the next stage's box; consume no requested care time.
            if (habitat.Allows(individual.Stage) && allowedDays == 0 && elapsedDays > 0 && elapsedDays <= 1000000)
                elapsedDays = 0;
            if (!habitat.Allows(individual.Stage) || elapsedDays > allowedDays)
                return DomainResult.Rejected("RelocationRequired", "Advance at most " + allowedDays.ToString("R", CultureInfo.InvariantCulture) +
                    " game days, then relocate to a compatible habitat. No time has been consumed.");
            DomainResult result = husbandry.Advance(individual, species, habitat.Environment, elapsedDays);
            if (result.Success) habitatSelections[individualId] = habitatId;
            return result;
        }

        public DomainResult AdvanceInSelectedHabitat(string individualId, double elapsedDays) =>
            AdvanceInHabitat(individualId, GetSelectedHabitatId(individualId), elapsedDays);

        static double AllowedDays(BeetleIndividual individual, SpeciesDefinition species, HabitatDefinition habitat)
        {
            if (!habitat.Allows(individual.Stage)) return 0;
            double days = Math.Max(0, species.Lifecycle.Duration(individual.Stage) - individual.StageAgeDays);
            for (int index = (int)individual.Stage + 1; index < (int)LifeStage.Dead; index++)
            {
                LifeStage stage = (LifeStage)index;
                if (!habitat.Allows(stage)) return days;
                days += species.Lifecycle.Duration(stage);
            }
            return double.PositiveInfinity; // Death freezes all further simulation and never requires relocation.
        }

        public int Count => individuals.Count;
        public int ActiveMatchCount => activeMatches.Count;
        public bool IsOccupied(string individualId) => individualId != null && occupied.Contains(individualId);

        public void Add(BeetleIndividual individual)
        {
            if (individual == null) throw new ArgumentNullException(nameof(individual));
            if (individuals.ContainsKey(individual.Id)) throw new InvalidOperationException("Individual ID already exists in this roster.");
            RestoreResult restored = BeetleSaveMapper.Restore(BeetleSaveMapper.Export(individual), catalog);
            if (!restored.Success) throw new ArgumentException(restored.ErrorMessage, nameof(individual));
            individuals.Add(individual.Id, restored.Individual);
        }

        public bool TryGet(string individualId, out BeetleIndividual individual)
        {
            individual = null;
            if (individualId == null || !individuals.TryGetValue(individualId, out BeetleIndividual stored)) return false;
            individual = stored.Clone();
            return true;
        }

        public IReadOnlyList<BeetleIndividual> Snapshot()
        {
            var result = new List<BeetleIndividual>();
            foreach (BeetleIndividual individual in individuals.Values) result.Add(individual.Clone());
            result.Sort((first, second) => StringComparer.Ordinal.Compare(first.Id, second.Id));
            return result.AsReadOnly();
        }

        public DomainResult Feed(string individualId, FoodPortion food)
        {
            DomainResult invalid = Available(individualId, out BeetleIndividual individual);
            if (invalid != null) return invalid;
            return husbandry.Feed(individual, Definition(individual.SpeciesId), food);
        }

        public DomainResult Feed(string individualId, string foodId)
        {
            if (!Foods.TryGet(foodId, out FoodDefinition food)) return DomainResult.Rejected("UnknownFood", "Choose a registered food definition.");
            return Feed(individualId, food.Portion);
        }

        public DomainResult MarkOverturned(string individualId)
        {
            DomainResult invalid = Available(individualId, out BeetleIndividual individual);
            return invalid ?? husbandry.MarkOverturned(individual, Definition(individual.SpeciesId));
        }

        public DomainResult HelpRight(string individualId)
        {
            DomainResult invalid = Available(individualId, out BeetleIndividual individual);
            return invalid ?? husbandry.HelpRight(individual, Definition(individual.SpeciesId));
        }

        public DomainResult Advance(string individualId, HabitatConditions habitat, double elapsedDays)
        {
            DomainResult invalid = Available(individualId, out BeetleIndividual individual);
            if (invalid != null) return invalid;
            return husbandry.Advance(individual, Definition(individual.SpeciesId), habitat, elapsedDays);
        }

        public BreedingResult Breed(string motherId, string fatherId, IReadOnlyList<string> offspringIds,
            IRandomSource random, IEnumerable<IBreedingRule> additionalRules = null)
        {
            DomainResult firstInvalid = Available(motherId, out BeetleIndividual mother);
            DomainResult secondInvalid = Available(fatherId, out BeetleIndividual father);
            DomainResult invalid = firstInvalid ?? secondInvalid;
            if (invalid != null) return BreedingResult.Rejected(invalid.ErrorCode, invalid.ErrorMessage);
            if (offspringIds != null)
                foreach (string id in offspringIds)
                    if (id != null && individuals.ContainsKey(id))
                        return BreedingResult.Rejected("DuplicateIndividualId", "A child ID is already registered in this roster.");
            BeetleIndividual motherCopy = mother.Clone(), fatherCopy = father.Clone();
            BreedingResult result = new BreedingService(additionalRules).Breed(Definition(mother.SpeciesId), motherCopy, fatherCopy, offspringIds, random);
            if (!result.Success) return result;
            individuals[motherId] = motherCopy;
            individuals[fatherId] = fatherCopy;
            foreach (BeetleIndividual child in result.Offspring) individuals.Add(child.Id, child.Clone());
            return result;
        }

        public BeetleSimulation BeginMatch(string firstId, string secondId, BeetleMatchSettings settings, int seed)
        {
            DomainResult firstInvalid = Available(firstId, out BeetleIndividual first);
            DomainResult secondInvalid = Available(secondId, out BeetleIndividual second);
            DomainResult invalid = firstInvalid ?? secondInvalid;
            if (invalid != null) throw new InvalidOperationException(invalid.ErrorMessage);
            if (firstId == secondId) throw new ArgumentException("Two distinct individuals are required.");
            if (first.IsOverturned || second.IsOverturned) throw new InvalidOperationException("Help both beetles upright before entering a contest.");
            BeetleMatchEntrant firstEntrant = BeetleMatchEntrant.FromIndividual(first, Profile(first.SpeciesId), Definition(first.SpeciesId));
            BeetleMatchEntrant secondEntrant = BeetleMatchEntrant.FromIndividual(second, Profile(second.SpeciesId), Definition(second.SpeciesId));
            string matchId;
            do { matchId = Guid.NewGuid().ToString("N"); } while (activeMatches.ContainsKey(matchId) || appliedMatchIds.Contains(matchId));
            var simulation = new BeetleSimulation(settings, seed, firstEntrant, secondEntrant, matchId);
            activeMatches.Add(matchId, new ActiveMatch { Simulation = simulation, FirstId = firstId, SecondId = secondId });
            occupied.Add(firstId);
            occupied.Add(secondId);
            return simulation;
        }

        /// <summary>Only the registered simulation's frozen result is accepted; duplicate settlement returns false.</summary>
        public bool Complete(BeetleMatchResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (appliedMatchIds.Contains(result.MatchId)) return false;
            if (!activeMatches.TryGetValue(result.MatchId, out ActiveMatch match) || !ReferenceEquals(result, match.Simulation.Result))
                throw new InvalidOperationException("Only the registered simulation's completed result can settle this roster.");
            if (result.Fighters.Count != 2 || result.Fighters[0].IndividualId != match.FirstId || result.Fighters[1].IndividualId != match.SecondId)
                throw new InvalidOperationException("Result participants do not match the registered contest.");
            BeetleIndividual first = individuals[match.FirstId].Clone(), second = individuals[match.SecondId].Clone();
            ApplyResult(first, result.Fighters[0], result.MatchId);
            ApplyResult(second, result.Fighters[1], result.MatchId);
            individuals[first.Id] = first;
            individuals[second.Id] = second;
            appliedMatchIds.Add(result.MatchId);
            Release(result.MatchId, match);
            return true;
        }

        public bool Cancel(string matchId)
        {
            if (matchId == null || !activeMatches.TryGetValue(matchId, out ActiveMatch match)) return false;
            Release(matchId, match);
            return true;
        }

        public BeetleRosterSaveData Export()
        {
            RequireNoActiveMatches();
            var records = new List<BeetleSaveData>();
            foreach (BeetleIndividual individual in Snapshot()) records.Add(BeetleSaveMapper.Export(individual));
            var ledger = new List<string>(appliedMatchIds);
            ledger.Sort(StringComparer.Ordinal);
            var selections = new List<BeetleHabitatSelectionData>();
            foreach (BeetleSaveData record in records)
                if (habitatSelections.TryGetValue(record.Id, out string habitatId))
                    selections.Add(new BeetleHabitatSelectionData { IndividualId = record.Id, HabitatId = habitatId });
            return new BeetleRosterSaveData { Individuals = records.ToArray(), AppliedMatchIds = ledger.ToArray(), HabitatSelections = selections.ToArray() };
        }

        /// <summary>Validate the entire container first; invalid imports leave the current roster and ledger untouched.</summary>
        public void Restore(BeetleRosterSaveData data)
        {
            RequireNoActiveMatches();
            if (data == null || data.Version != BeetleRosterSaveData.CurrentVersion || data.Individuals == null || data.AppliedMatchIds == null || data.HabitatSelections == null)
                throw new ArgumentException("A supported roster version and both record arrays are required.", nameof(data));
            var restored = new Dictionary<string, BeetleIndividual>(StringComparer.Ordinal);
            foreach (BeetleSaveData record in data.Individuals)
            {
                RestoreResult result = BeetleSaveMapper.Restore(record, catalog);
                if (!result.Success) throw new ArgumentException(result.ErrorMessage, nameof(data));
                if (restored.ContainsKey(result.Individual.Id)) throw new ArgumentException("Duplicate individual ID in save.", nameof(data));
                restored.Add(result.Individual.Id, result.Individual);
            }
            var ledger = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in data.AppliedMatchIds)
                if (string.IsNullOrWhiteSpace(id) || id != id.Trim() || !ledger.Add(id))
                    throw new ArgumentException("Settlement IDs must be non-empty, trimmed and unique.", nameof(data));
            var selections = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (BeetleHabitatSelectionData selection in data.HabitatSelections)
            {
                if (selection == null || selection.IndividualId == null || !restored.ContainsKey(selection.IndividualId) ||
                    !Content.TryGetHabitat(selection.HabitatId, out _) || selections.ContainsKey(selection.IndividualId))
                    throw new ArgumentException("Habitat selections must reference unique saved individuals and known habitats.", nameof(data));
                // A stage transition or death retains its last location; relocation is required before the next advance.
                selections.Add(selection.IndividualId, selection.HabitatId);
            }
            individuals = restored;
            appliedMatchIds = ledger;
            habitatSelections = selections;
        }

        DomainResult Available(string id, out BeetleIndividual individual)
        {
            individual = null;
            if (id == null || !individuals.TryGetValue(id, out individual)) return DomainResult.Rejected("UnknownIndividual", "The individual is not registered in this roster.");
            return occupied.Contains(id) ? DomainResult.Rejected("IndividualInMatch", "The individual is currently reserved for a match.") : null;
        }

        SpeciesDefinition Definition(string id)
        {
            if (!catalog.TryGet(id, out SpeciesDefinition definition)) throw new InvalidOperationException("Species definition is missing.");
            return definition;
        }

        BeetleCombatProfile Profile(string speciesId)
        {
            if (!profiles.TryGetValue(speciesId, out BeetleCombatProfile profile) || !profile || !profile.species)
                throw new InvalidOperationException("A combat profile is required for the selected species.");
            Definition(speciesId);
            if (speciesId != profile.species.speciesId)
                throw new InvalidOperationException("Combat profile species ID does not match the selected species.");
            return profile;
        }

        void ApplyResult(BeetleIndividual individual, BeetleFighterResult fighter, string matchId)
        {
            if (fighter.SpeciesId != individual.SpeciesId || float.IsNaN(fighter.RemainingStaminaFraction) || float.IsInfinity(fighter.RemainingStaminaFraction))
                throw new InvalidOperationException("Contest species and stamina must be valid before settlement.");
            float effort = Math.Max(0, Math.Min(1, 1 - fighter.RemainingStaminaFraction));
            DomainResult result = recovery.Apply(individual, new ContestAftermath(matchId, individual.Id, effort), recoveryPolicy);
            if (!result.Success) throw new InvalidOperationException(result.ErrorMessage);
        }

        void Release(string matchId, ActiveMatch match)
        { occupied.Remove(match.FirstId); occupied.Remove(match.SecondId); activeMatches.Remove(matchId); }
        void RequireNoActiveMatches()
        { if (activeMatches.Count != 0) throw new InvalidOperationException("Complete or cancel active matches before exporting or restoring the roster."); }
    }
}
