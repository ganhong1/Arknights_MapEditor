#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Editor
{
    /// <summary>Serialized, bounded JSONL persistence. This type never calls Unity APIs.</summary>
    internal sealed class PluginLogFile
    {
        private static readonly UTF8Encoding Utf8 = new(false);
        private readonly object gate = new();
        private readonly string directory;
        private readonly long maximumBytes;
        private readonly int retainedFiles;
        private readonly int retentionDays;
        internal PluginLogFile(string directory, long maximumBytes, int retainedFiles, int retentionDays = 14)
        {
            if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            if (retainedFiles < 2 || retainedFiles > 100) throw new ArgumentOutOfRangeException(nameof(retainedFiles));
            if (retentionDays < 1) throw new ArgumentOutOfRangeException(nameof(retentionDays));
            this.directory = Path.GetFullPath(directory); this.maximumBytes = maximumBytes;
            this.retainedFiles = retainedFiles; this.retentionDays = retentionDays;
        }
        private string PathFor(int index) => Path.Combine(directory, index == 0 ? "plugin.jsonl" : $"plugin.{index}.jsonl");
        internal void Write(string id, string operation, string level, string category, Exception? error, string? session)
        {
            var entry = error == null ? new DiagnosticEvent() : DiagnosticEvent.Failure(operation, error, session);
            entry.Id = id; entry.Operation = operation; entry.Level = level; entry.Category = category; entry.Session = session;
            Write(entry);
        }
        internal void Write(DiagnosticEvent entry)
        {
            var line = entry.ToJson().ToString(Formatting.None) + "\n";
            var bytes = Utf8.GetByteCount(line);
            if (bytes > maximumBytes) throw new InvalidDataException("Diagnostic event is too large.");
            lock (gate)
            {
                Directory.CreateDirectory(directory);
                Prune();
                var active = new FileInfo(PathFor(0));
                if (active.Exists && active.Length + bytes > maximumBytes) Rotate();
                File.AppendAllText(active.FullName, line, Utf8);
            }
        }
        private void Prune()
        {
            var before = DateTime.UtcNow.AddDays(-retentionDays);
            foreach (var path in Directory.EnumerateFiles(directory, "plugin*.jsonl"))
            {
                var name = Path.GetFileName(path);
                var known = name == "plugin.jsonl";
                var index = 0;
                if (!known && name.StartsWith("plugin.", StringComparison.Ordinal))
                    known = int.TryParse(name.Substring(7, name.Length - 7 - 6), out index) && index > 0 && name == $"plugin.{index}.jsonl";
                if (known && (index >= retainedFiles || File.GetLastWriteTimeUtc(path) < before)) File.Delete(path);
            }
        }
        private void Rotate()
        {
            for (var i = retainedFiles - 1; i > 0; i--)
            {
                var source = PathFor(i - 1); var destination = PathFor(i);
                if (!File.Exists(source)) continue;
                if (!File.Exists(destination)) File.Move(source, destination); else File.Replace(source, destination, null);
            }
        }
        internal IReadOnlyList<DiagnosticEvent> ReadRecent(int limit = 256)
        {
            if (limit < 1 || limit > 256) throw new ArgumentOutOfRangeException(nameof(limit));
            var result = new List<DiagnosticEvent>();
            lock (gate)
            {
                var budget = 1024 * 1024L;
                for (var index = 0; index < retainedFiles && result.Count < limit && budget > 0; index++)
                {
                    var path = PathFor(index);
                    if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-retentionDays)) continue;
                    using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var bytes = Math.Min(Math.Min(stream.Length, 256 * 1024), budget);
                    budget -= bytes;
                    var tail = ReadTail(stream, bytes, limit - result.Count);
                    result.InsertRange(0, tail);
                }
            }
            return result;
        }
        private static IReadOnlyList<DiagnosticEvent> ReadTail(FileStream stream, long bytes, int limit)
        {
            var result = new List<DiagnosticEvent>();
            var skipped = stream.Length > bytes;
            if (skipped) stream.Seek(-bytes, SeekOrigin.End);
            using var reader = new StreamReader(stream, Utf8, true, 4096, true);
            if (skipped) reader.ReadLine();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                try
                {
                    using var text = new StringReader(line);
                    using var json = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None };
                    result.Add(DiagnosticEvent.FromJson(JObject.Load(json)));
                    if (result.Count > limit) result.RemoveAt(0);
                }
                catch (Exception error) when (error is JsonException || error is FormatException || error is OverflowException || error is InvalidCastException || error is ArgumentException) { }
            }
            return result;
        }
        internal void Export(string path) => Export(path, null);
        internal void Export(string path, JObject? bundleManifest)
        {
            lock (gate)
            {
                var destination = Path.GetFullPath(path);
                if (string.Equals(Path.GetDirectoryName(destination)?.TrimEnd(Path.DirectorySeparatorChar), directory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("请将诊断导出保存到日志目录之外。");
                var temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".arkmap-log-" + Guid.NewGuid().ToString("N") + ".tmp");
                Exception? primary = null;
                var ownsTemporary = false;
                try
                {
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        ownsTemporary = true;
                        if (bundleManifest == null) CopyLogs(output);
                        else
                        {
                            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
                            {
                                using (var manifest = new StreamWriter(archive.CreateEntry("manifest.json").Open(), Utf8))
                                    manifest.Write(bundleManifest.ToString(Formatting.Indented));
                                using var logs = archive.CreateEntry("plugin.jsonl").Open(); CopyLogs(logs);
                            }
                        }
                        output.Flush(true);
                    }
                    if (File.Exists(destination)) File.Replace(temporary, destination, null); else File.Move(temporary, destination);
                    ownsTemporary = false;
                }
                catch (Exception error) { primary = error; throw; }
                finally
                {
                    try { if (ownsTemporary) File.Delete(temporary); }
                    catch (Exception cleanup) when (cleanup is IOException || cleanup is UnauthorizedAccessException)
                    {
                        if (primary != null) primary.Data["TemporaryCleanupFailure"] = cleanup;
                        else throw;
                    }
                }
            }
        }
        private void CopyLogs(Stream destination)
        {
            for (var i = retainedFiles - 1; i >= 0; i--)
            {
                var path = PathFor(i);
                if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-retentionDays)) continue;
                using var source = File.OpenRead(path); source.CopyTo(destination);
            }
        }
    }
}
