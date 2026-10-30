using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace BeetleDesktop.Native
{
    /// <summary>Independent desktop-pet storage; no BugDuel game data is accessed.</summary>
    public static class PetPersistence
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = true
        };

        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BeetleDesktop", "pets.json");

        public static PetSimulation Load(string path = null)
        {
            path = path ?? DefaultPath;
            try
            {
                if (File.Exists(path))
                    return Read(path);
            }
            catch (Exception exception) when (IsStorageError(exception))
            {
                Trace.TraceWarning("桌宠存档无法读取，尝试使用备份：" + exception.Message);
            }

            try
            {
                if (File.Exists(path + ".bak"))
                    return Read(path + ".bak");
            }
            catch (Exception exception) when (IsStorageError(exception))
            {
                Trace.TraceWarning("桌宠备份无法读取：" + exception.Message);
            }
            return new PetSimulation();
        }

        private static PetSimulation Read(string path)
        {
            PetSaveData data = JsonSerializer.Deserialize<PetSaveData>(File.ReadAllText(path), JsonOptions);
            if (data == null || data.Version != 1 || data.Pets == null || data.Pets.Count == 0)
                throw new JsonException("The desktop pet save has no usable household.");
            return PetSimulation.Restore(data);
        }

        public static bool Save(PetSimulation simulation, string path = null)
        {
            if (simulation == null)
                throw new ArgumentNullException(nameof(simulation));
            path = path ?? DefaultPath;
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(simulation.CaptureSaveData(), JsonOptions));
                if (File.Exists(path))
                    File.Replace(temporary, path, path + ".bak");
                else
                    File.Move(temporary, path);
                return true;
            }
            catch (Exception exception) when (IsStorageError(exception))
            {
                Trace.TraceWarning("桌宠存档未保存：" + exception.Message);
                return false;
            }
        }

        private static bool IsStorageError(Exception exception) =>
            exception is IOException || exception is UnauthorizedAccessException ||
            exception is ArgumentException || exception is JsonException || exception is NotSupportedException;
    }
}
