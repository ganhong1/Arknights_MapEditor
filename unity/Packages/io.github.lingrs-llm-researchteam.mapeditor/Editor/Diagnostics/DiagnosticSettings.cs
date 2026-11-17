#nullable enable
using System;
using System.IO;
using Newtonsoft.Json;

namespace Arknights.MapEditor.Editor
{
    internal sealed class DiagnosticSettings
    {
        public string DirectoryPath { get; set; } = "";
        public long MaximumBytes { get; set; } = 10 * 1024 * 1024;
        public int RetainedFiles { get; set; } = 10;
        public int RetentionDays { get; set; } = 14;
        internal DiagnosticSettings Clone() => new() { DirectoryPath = DirectoryPath, MaximumBytes = MaximumBytes,
            RetainedFiles = RetainedFiles, RetentionDays = RetentionDays };
        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(DirectoryPath) || !Path.IsPathFullyQualified(DirectoryPath))
                throw new ArgumentException("日志目录需要绝对路径。");
            DirectoryPath = Path.GetFullPath(DirectoryPath);
            if (MaximumBytes < 1024 || MaximumBytes > 100L * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(MaximumBytes), "单文件大小范围为 1 KiB–100 MiB。");
            if (RetainedFiles < 2 || RetainedFiles > 100) throw new ArgumentOutOfRangeException(nameof(RetainedFiles), "保留文件数范围为 2–100。");
            if (RetentionDays < 1 || RetentionDays > 365) throw new ArgumentOutOfRangeException(nameof(RetentionDays), "保留天数范围为 1–365。");
        }
        internal static DiagnosticSettings Load(string path, string defaultDirectory)
        {
            if (!File.Exists(path)) return new DiagnosticSettings { DirectoryPath = defaultDirectory };
            var value = JsonConvert.DeserializeObject<DiagnosticSettings>(File.ReadAllText(path)) ?? throw new FormatException("日志设置为空。");
            value.Validate(); return value;
        }
        internal void Save(string path)
        {
            Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            Exception? primary = null;
            try
            {
                File.WriteAllText(temporary, JsonConvert.SerializeObject(this, Formatting.Indented));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            catch (Exception error) { primary = error; throw; }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception cleanup) when (cleanup is IOException || cleanup is UnauthorizedAccessException)
                {
                    if (primary != null) primary.Data["TemporaryCleanupFailure"] = cleanup;
                    else throw;
                }
            }
        }
    }
}
