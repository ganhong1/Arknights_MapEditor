#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Arknights.MapEditor.Editor
{
    [InitializeOnLoad]
    internal static class PluginLog
    {
        private static readonly string DefaultDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/ArknightsMapEditor/Logs"));
        private static readonly string SettingsPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../UserSettings/ArknightsMapEditor.Diagnostics.json"));
        private static readonly string UnityVersion = Application.unityVersion;
        private static readonly string EditorSessionId = ReadSessionId();
        private static readonly AsyncLocal<DiagnosticOperation?> Current = new();
        private static readonly ConcurrentQueue<string> ConsoleQueue = new();
        private static readonly HashSet<string> ConsoleKeys = new();
        private static readonly object Gate = new();
        private sealed class RecordedFailure { internal string Id = "", OperationId = ""; }
        private static readonly ConditionalWeakTable<Exception, RecordedFailure> Recorded = new();
        private static DiagnosticSettings settings = new() { DirectoryPath = DefaultDirectory };
        private static PluginLogFile sink;
        private static DiagnosticDispatcher dispatcher;
        private static bool shuttingDown;
        private static DateTimeOffset verboseUntil;

        static PluginLog()
        {
            Exception? loadError = null;
            try { settings = DiagnosticSettings.Load(SettingsPath, DefaultDirectory); }
            catch (Exception error) { loadError = error; }
            sink = CreateSink(settings);
            IReadOnlyList<DiagnosticEvent> history = Array.Empty<DiagnosticEvent>();
            try { history = sink.ReadRecent(); } catch (Exception error) { loadError ??= error; }
            dispatcher = new DiagnosticDispatcher(sink.Write, history: history);
            EditorApplication.update += PumpConsole;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;
            if (loadError != null) Failure("diagnostics.initialize", loadError);
        }
        internal static string DirectoryPath { get { lock (Gate) return settings.DirectoryPath; } }
        internal static DiagnosticSettings Settings { get { lock (Gate) return settings.Clone(); } }
        internal static DiagnosticHealth Health { get { lock (Gate) return dispatcher.Health; } }
        internal static DateTimeOffset VerboseUntil { get { lock (Gate) return verboseUntil; } }
        internal static IReadOnlyList<DiagnosticEvent> Recent() { lock (Gate) return dispatcher.Recent(); }
        internal static bool Flush(int milliseconds = 1500) { lock (Gate) return dispatcher.Flush(milliseconds); }
        internal static void SetVerbose(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero || duration > TimeSpan.FromHours(1)) throw new ArgumentOutOfRangeException(nameof(duration));
            lock (Gate) verboseUntil = DateTimeOffset.UtcNow + duration;
        }
        internal static DiagnosticOperation Begin(string operation, string? session = null, string? documentId = null, string? revision = null)
        {
            var scope = new DiagnosticOperation(operation, session ?? EditorSessionId, documentId, revision, Current.Value);
            Current.Value = scope;
            if (VerboseUntil > DateTimeOffset.UtcNow) Emit(scope.Event("started", "OPERATION_STARTED"));
            return scope;
        }
        internal static void End(DiagnosticOperation scope, DiagnosticOperation? parent)
        {
            if (ReferenceEquals(Current.Value, scope)) Current.Value = parent;
        }
        internal static string Failure(string operation, Exception error, string? session = null)
        {
            var scope = Current.Value;
            lock (Gate)
            {
                if (Recorded.TryGetValue(error, out var previous) && (scope == null || previous.OperationId == scope.RootOperationId))
                {
                    scope?.RecordFailure(error, previous.Id);
                    return previous.Id;
                }
            }
            var entry = DiagnosticEvent.Failure(operation, error, session);
            if (scope != null) { scope.Attach(entry); scope.RecordFailure(error, entry.Id); }
            entry.Session ??= EditorSessionId;
            lock (Gate)
            {
                Recorded.Remove(error);
                Recorded.Add(error, new RecordedFailure { Id = entry.Id, OperationId = scope?.RootOperationId ?? "" });
            }
            Emit(entry);
            return entry.Id;
        }
        internal static string FailureMessage(string operation, Exception error, string? session = null) =>
            error.Message + " · 日志 " + Failure(operation, error, session);
        internal static void Recovered(string operation, string? session = null)
        {
            var entry = new DiagnosticEvent { Operation = operation, Session = session, Category = "recovered", Code = "OPERATION_RECOVERED" };
            Current.Value?.Attach(entry); Emit(entry);
        }
        private static string ReadSessionId()
        {
            const string key = "ArknightsMapEditor.Diagnostics.Session";
            var id = SessionState.GetString(key, "");
            if (id.Length == 0) { id = Guid.NewGuid().ToString("N"); SessionState.SetString(key, id); }
            return id;
        }
        internal static void Emit(DiagnosticEvent entry)
        {
            entry.LastUtc = entry.Utc;
            entry.Session ??= EditorSessionId;
            lock (Gate)
            {
                dispatcher.Enqueue(entry);
                if (entry.Level is "warning" or "error")
                {
                    var key = entry.Operation + "|" + entry.Code + "|" + entry.DocumentId;
                    if (ConsoleKeys.Count < 256 && ConsoleKeys.Add(key))
                        ConsoleQueue.Enqueue($"[Arknights Map Editor] {entry.Operation} · {entry.Code} · {entry.Id}");
                }
            }
        }
        private static void PumpConsole()
        {
            for (var i = 0; i < 4 && ConsoleQueue.TryDequeue(out var message); i++) Debug.LogWarning(message);
        }
        private static PluginLogFile CreateSink(DiagnosticSettings value) =>
            new(value.DirectoryPath, value.MaximumBytes, value.RetainedFiles, value.RetentionDays);

        internal static void Configure(DiagnosticSettings value)
        {
            var next = value.Clone(); next.Validate();
            // Probe the requested directory before retiring the current writer.
            Directory.CreateDirectory(next.DirectoryPath);
            var probe = Path.Combine(next.DirectoryPath, ".arkmap-probe-" + Guid.NewGuid().ToString("N"));
            using (File.Create(probe)) { }
            File.Delete(probe);
            lock (Gate)
            {
                if (shuttingDown) throw new InvalidOperationException("编辑器正在关闭日志。");
                var candidateSink = CreateSink(next);
                var candidate = DiagnosticDispatcher.PrepareReplacement(dispatcher, candidateSink.Write, () => next.Save(SettingsPath));
                var previous = dispatcher;
                sink = candidateSink; settings = next; dispatcher = candidate;
                previous.Stop(0);
            }
        }
        internal static void Export(string path, bool bundle = false)
        {
            lock (Gate)
            {
                if (!dispatcher.Flush(1500)) throw new TimeoutException("日志尚未写完，请稍后导出。");
                JObject? manifest = null;
                if (bundle)
                {
                    var health = dispatcher.Health;
                    manifest = new JObject
                    {
                        ["schemaVersion"] = 1, ["utc"] = DateTimeOffset.UtcNow.ToString("O"),
                        ["unityVersion"] = UnityVersion, ["pluginVersion"] = "0.1.0-preview.1", ["resourceBaseline"] = "2.7.81",
                        ["pending"] = health.Pending, ["dropped"] = health.Dropped, ["writeFailures"] = health.WriteFailures,
                        ["lastWriteError"] = health.LastWriteError, ["maximumBytes"] = settings.MaximumBytes,
                        ["retainedFiles"] = settings.RetainedFiles, ["retentionDays"] = settings.RetentionDays,
                        ["includesMapDocuments"] = false
                    };
                }
                sink.Export(path, manifest);
            }
        }
        private static void Shutdown()
        {
            lock (Gate)
            {
                if (shuttingDown) return;
                shuttingDown = true;
                dispatcher.Stop(1500);
            }
            EditorApplication.update -= PumpConsole;
        }
        [MenuItem("Tools/Arknights/Diagnostics/Open Plugin Logs")]
        private static void OpenLogs()
        {
            try { Directory.CreateDirectory(DirectoryPath); EditorUtility.RevealInFinder(DirectoryPath); }
            catch (Exception error) { EditorUtility.DisplayDialog("插件日志", FailureMessage("logs.open", error), "确定"); }
        }
        [MenuItem("Tools/Arknights/Diagnostics/Export Plugin Logs")]
        private static void ExportLogs()
        {
            var path = EditorUtility.SaveFilePanel("导出插件日志", "", "arknights-map-editor", "jsonl");
            if (path.Length == 0) return;
            try { Export(path); }
            catch (Exception error) { EditorUtility.DisplayDialog("插件日志", FailureMessage("logs.export", error), "确定"); }
        }
    }

    internal sealed class DiagnosticOperation : IDisposable
    {
        private readonly Stopwatch timer = Stopwatch.StartNew();
        private readonly DiagnosticOperation? parent;
        private readonly string operation, id = Guid.NewGuid().ToString("N");
        private readonly string? session, documentId;
        private readonly string? previousRevision;
        private string? revision;
        private bool completed, disposed;
        internal string OperationId => id;
        internal string RootOperationId => parent?.RootOperationId ?? id;
        internal Exception? LastError { get; private set; }
        internal string LastEventId { get; private set; } = "";
        internal DiagnosticOperation(string operation, string? session, string? documentId, string? revision, DiagnosticOperation? parent)
        { this.operation = operation; this.session = session; this.documentId = documentId; this.revision = revision; previousRevision = revision; this.parent = parent; }
        internal void Attach(DiagnosticEvent value)
        {
            value.OperationId = id; value.Session ??= session; value.DocumentId = documentId;
            value.Revision = revision; value.DurationMs = timer.Elapsed.TotalMilliseconds;
            value.PreviousRevision = previousRevision; value.ParentOperationId = parent?.OperationId;
        }
        internal DiagnosticEvent Event(string outcome, string code)
        {
            var value = new DiagnosticEvent { Operation = operation, Outcome = outcome, Code = code,
                Level = outcome == "started" ? "debug" : "info", Category = outcome == "cancelled" ? "cancelled" : "operation" };
            Attach(value); return value;
        }
        internal void RecordFailure(Exception error, string eventId) { completed = true; LastError = error; LastEventId = eventId; }
        internal void Success(string? revision = null)
        {
            if (completed || disposed) return;
            if (revision != null) this.revision = revision;
            completed = true; PluginLog.Emit(Event("success", "OPERATION_COMPLETED"));
        }
        internal void Fail(Exception error) { if (!completed && !disposed) PluginLog.Failure(operation, error, session); }
        internal void Cancel()
        {
            if (completed || disposed) return;
            completed = true; PluginLog.Emit(Event("cancelled", "OPERATION_CANCELLED"));
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; timer.Stop(); PluginLog.End(this, parent);
        }
    }
}
