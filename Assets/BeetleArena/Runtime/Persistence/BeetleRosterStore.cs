using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BugDuel.Domain;
using UnityEngine;

namespace BeetleArena
{
    [Serializable]
    public sealed class BeetleHabitatSelectionData
    {
        public string IndividualId;
        public string HabitatId;
    }

    /// <summary>Save transfer DTO. Individuals and match settlement ledger must be saved together; loading does not alter active game state.</summary>
    [Serializable]
    public sealed class BeetleRosterSaveData
    {
        public const int CurrentVersion = 1;
        public int Version = CurrentVersion;
        public BeetleSaveData[] Individuals = Array.Empty<BeetleSaveData>();
        public string[] AppliedMatchIds = Array.Empty<string>();
        public BeetleHabitatSelectionData[] HabitatSelections = Array.Empty<BeetleHabitatSelectionData>();
    }

    public interface IBeetleRosterStore
    {
        BeetleRosterSaveData Load();
        void Save(BeetleRosterSaveData data);
    }

    /// <summary>
    /// Synchronous JSON adapter with explicit path; contains no global save paths, auto-saving, or scene references.
    /// Missing files throw FileNotFoundException; unknown container versions are rejected; domain validation handled by Restore.
    /// </summary>
    public sealed class BeetleRosterJsonStore : IBeetleRosterStore
    {
        readonly string path;

        public BeetleRosterJsonStore(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath) || !Path.IsPathFullyQualified(absolutePath))
                throw new ArgumentException("An explicit absolute save file path is required.", nameof(absolutePath));
            path = Path.GetFullPath(absolutePath);
            if (string.IsNullOrEmpty(Path.GetFileName(path)))
                throw new ArgumentException("The save path must name a file.", nameof(absolutePath));
        }

        public BeetleRosterSaveData Load()
        {
            string json;
            try { json = File.ReadAllText(path, new UTF8Encoding(false, true)); }
            catch (DecoderFallbackException exception) { throw new InvalidDataException("Roster save contains invalid text encoding.", exception); }
            if (string.IsNullOrWhiteSpace(json) || json.TrimStart()[0] != '{')
                throw new InvalidDataException("Roster save must contain a JSON object.");
            // Overwrite preserves sentinels for missing fields, preventing DTO defaults from treating legacy saves lacking versions/ledgers as valid.
            var data = new BeetleRosterSaveData { Version = 0, Individuals = null, AppliedMatchIds = null, HabitatSelections = null };
            try { JsonUtility.FromJsonOverwrite(json, data); }
            catch (ArgumentException exception) { throw new InvalidDataException("Roster save is not valid JSON.", exception); }
            Validate(data);
            // Unity string serialization writes optional null parents as empty strings; restores null semantics at this serialization boundary.
            // Whitespace, single-parent, duplicate parent, and non-empty ancestor checks remain enforced by strict domain Restore validation.
            foreach (var individual in data.Individuals)
            {
                if (individual.MotherId == string.Empty) individual.MotherId = null;
                if (individual.FatherId == string.Empty) individual.FatherId = null;
            }
            return data;
        }

        public void Save(BeetleRosterSaveData data)
        {
            // Rejects invalid inputs before creating directories or files; serialization errors leave existing save files intact.
            Validate(data);
            string json = JsonUtility.ToJson(data, true);
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
                    { writer.Write(json); writer.Flush(); }
                    stream.Flush(true);
                }
                // Does not delete old save as fallback when Replace is unsupported; failures must preserve the original save.
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                // Cleans up only the unique temporary file created in this pass; no-op once replacement succeeds.
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        static void Validate(BeetleRosterSaveData data)
        {
            if (data == null) throw new InvalidDataException("Roster save must contain an object.");
            if (data.Version != BeetleRosterSaveData.CurrentVersion)
                throw new NotSupportedException("Unsupported roster save version: " + data.Version + ". Migrate explicitly before loading.");
            if (data.Individuals == null || data.AppliedMatchIds == null || data.HabitatSelections == null)
                throw new InvalidDataException("Roster save requires individual, applied-match and habitat-selection arrays, including when empty.");
            var individualIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var individual in data.Individuals)
                if (individual == null || !ValidId(individual.Id) || !individualIds.Add(individual.Id))
                    throw new InvalidDataException("Roster individuals must be non-null and have unique, non-empty, trimmed IDs.");
            var matchIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in data.AppliedMatchIds)
                if (!ValidId(id) || !matchIds.Add(id))
                    throw new InvalidDataException("Applied-match IDs must be unique, non-empty and trimmed.");
            var assignedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var selection in data.HabitatSelections)
                if (selection == null || !ValidId(selection.IndividualId) || !ValidId(selection.HabitatId) ||
                    !individualIds.Contains(selection.IndividualId) || !assignedIds.Add(selection.IndividualId))
                    throw new InvalidDataException("Habitat selections require existing, distinct individual IDs and non-empty, trimmed habitat IDs.");
        }

        static bool ValidId(string id) => !string.IsNullOrWhiteSpace(id) && id == id.Trim();
    }
}
