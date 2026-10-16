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

    /// <summary>存档传输对象。个体与比赛结算账本必须一起保存，加载不自动改变运行中的游戏。</summary>
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
    /// 显式路径的同步 JSON 适配器，无全局存档位置、自动保存或场景引用。
    /// 缺失文件保留 FileNotFoundException；未知容器版本拒绝，个体领域校验由应用层 Restore 负责。
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
            // Overwrite 保留缺失字段的哨兵，避免 DTO 默认值把没有版本/账本的旧文件当成有效新档。
            var data = new BeetleRosterSaveData { Version = 0, Individuals = null, AppliedMatchIds = null, HabitatSelections = null };
            try { JsonUtility.FromJsonOverwrite(json, data); }
            catch (ArgumentException exception) { throw new InvalidDataException("Roster save is not valid JSON.", exception); }
            Validate(data);
            // Unity 的字符串序列化把可选 null 亲本写成空字符串；仅在此传输边界还原其缺省语义。
            // 空白、单亲、重复亲本和非空祖先仍交给严格的领域 Restore 校验，不能静默丢弃。
            foreach (var individual in data.Individuals)
            {
                if (individual.MotherId == string.Empty) individual.MotherId = null;
                if (individual.FatherId == string.Empty) individual.FatherId = null;
            }
            return data;
        }

        public void Save(BeetleRosterSaveData data)
        {
            // 在创建目录或文件前拒绝非法输入；序列化失败也不会触碰已有存档。
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
                // 不以删除旧档作为不支持 Replace 时的降级方式。失败必须保留原档案。
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                // 只清理本次创建的唯一文件；替换成功后它已经不存在。
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
