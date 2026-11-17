#nullable enable
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using Arknights.MapEditor.Storage;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Arknights.MapEditor.Editor.Tests
{
    public sealed class DiagnosticsCoreTests
    {
        private string root = "";
        [SetUp] public void SetUp() { root = Path.Combine(Path.GetTempPath(), "arkmap-diagnostics-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
        [TearDown] public void TearDown() { Directory.Delete(root, true); }

        [TestCase("validation", "VALIDATION_FAILED", "info")]
        [TestCase("io", "STORAGE_FAILED", "warning")]
        [TestCase("conflict", "DOCUMENT_CONFLICT", "warning")]
        [TestCase("timeout", "OPERATION_TIMEOUT", "warning")]
        [TestCase("cancelled", "OPERATION_CANCELLED", "info")]
        [TestCase("internal", "INTERNAL_ERROR", "error")]
        public void FailureClassificationKeepsMessagesAndPathsOut(string category, string code, string level)
        {
            Exception error = category switch
            {
                "validation" => new FormatException("secret-file-path"), "io" => new IOException("secret-file-path"),
                "conflict" => new DocumentFileConflictException("secret-file-path"), "timeout" => new TimeoutException("secret-file-path"),
                "cancelled" => new OperationCanceledException("secret-file-path"), _ => new NullReferenceException("secret-file-path")
            };
            error.Data["TemporaryCleanupFailure"] = new IOException("private-cleanup-path");
            var entry = DiagnosticEvent.Failure("document.save", error, "session");
            Assert.That(entry.Category, Is.EqualTo(category)); Assert.That(entry.Code, Is.EqualTo(code)); Assert.That(entry.Level, Is.EqualTo(level));
            Assert.That(entry.ToJson().ToString(), Does.Not.Contain("secret-file-path"));
            Assert.That(entry.ToJson().ToString(), Does.Not.Contain("private-cleanup-path"));
            Assert.That(entry.CleanupExceptionTypes, Contains.Item(typeof(IOException).FullName));
        }
        [Test]
        public void QueueIsBoundedAndFlushWaitsForTheActualWriter()
        {
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var writer = new DiagnosticDispatcher(_ => { entered.Set(); release.Wait(); }, 1);
            try
            {
                Assert.That(writer.Enqueue(new DiagnosticEvent()), Is.True); Assert.That(entered.Wait(1000), Is.True);
                Assert.That(writer.Enqueue(new DiagnosticEvent()), Is.True); Assert.That(writer.Enqueue(new DiagnosticEvent()), Is.False);
                Assert.That(writer.Health.Dropped, Is.EqualTo(1)); Assert.That(writer.Health.Pending, Is.EqualTo(2));
                Assert.That(writer.Flush(5), Is.False);
                release.Set(); Assert.That(writer.Flush(1000), Is.True); Assert.That(writer.Stop(1000), Is.True);
                Assert.That(writer.Stop(1000), Is.True); Assert.That(writer.HandlesDisposed, Is.True);
                Assert.That(writer.Flush(1), Is.True);
                Assert.That(writer.Enqueue(new DiagnosticEvent()), Is.False);
            }
            finally { release.Set(); writer.Stop(1000); }
        }
        [Test]
        public void WriterFailureCanRecoverAndRepeatedIssuesAreAggregated()
        {
            var attempts = 0;
            var writer = new DiagnosticDispatcher(_ => { if (Interlocked.Increment(ref attempts) == 1) throw new IOException("private-path"); });
            try
            {
                writer.Enqueue(DiagnosticEvent.Failure("read", new IOException(), "s")); Assert.That(writer.Flush(1000), Is.True);
                Assert.That(writer.Health.IsHealthy, Is.False); Assert.That(writer.Health.LastWriteError, Is.EqualTo("IOException"));
                writer.Enqueue(DiagnosticEvent.Failure("read", new IOException(), "s")); Assert.That(writer.Flush(1000), Is.True);
                Assert.That(writer.Health.IsHealthy, Is.True); Assert.That(writer.Health.WriteFailures, Is.EqualTo(1));
                Assert.That(writer.Recent().Count, Is.EqualTo(1)); Assert.That(writer.Recent()[0].Count, Is.EqualTo(2));
                var copy = writer.Recent()[0]; copy.Count = 99; Assert.That(writer.Recent()[0].Count, Is.EqualTo(2));
            }
            finally { writer.Stop(1000); }
        }
        [Test]
        public void RetentionOnlyRemovesOwnedFilesAndRecentReaderToleratesBrokenTail()
        {
            var logs = Path.Combine(root, "logs"); Directory.CreateDirectory(logs);
            var expired = Path.Combine(logs, "plugin.1.jsonl"); File.WriteAllText(expired, "old"); File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-20));
            File.WriteAllText(Path.Combine(logs, "plugin.notes.jsonl"), "user-data");
            File.WriteAllText(Path.Combine(logs, "plugin.99.jsonl"), "out-of-retention");
            var sink = new PluginLogFile(logs, 8192, 3, 14);
            var entry = DiagnosticEvent.Failure("test", new IOException()); sink.Write(entry);
            File.AppendAllText(Path.Combine(logs, "plugin.jsonl"), "{broken-tail\n");
            Assert.That(sink.ReadRecent().Single().Id, Is.EqualTo(entry.Id));
            Assert.That(File.Exists(expired), Is.False); Assert.That(File.Exists(Path.Combine(logs, "plugin.99.jsonl")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(logs, "plugin.notes.jsonl")), Is.EqualTo("user-data"));
        }
        [Test]
        public void BundleContainsOnlyManifestAndLogsAndSettingsRoundTrip()
        {
            var settings = new DiagnosticSettings { DirectoryPath = Path.Combine(root, "logs") };
            var path = Path.Combine(root, "settings.json"); settings.Save(path);
            var restored = DiagnosticSettings.Load(path, "unused"); Assert.That(restored.DirectoryPath, Is.EqualTo(settings.DirectoryPath));
            Assert.That(restored.MaximumBytes, Is.EqualTo(10 * 1024 * 1024)); Assert.That(restored.RetainedFiles, Is.EqualTo(10));
            var sink = new PluginLogFile(settings.DirectoryPath, settings.MaximumBytes, settings.RetainedFiles);
            sink.Write(new DiagnosticEvent { Operation = "test", Id = "known-event" });
            var zip = Path.Combine(root, "report.zip"); sink.Export(zip, new JObject { ["includesMapDocuments"] = false });
            using var stream = File.OpenRead(zip); using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            Assert.That(archive.Entries.Select(entry => entry.FullName), Is.EquivalentTo(new[] { "manifest.json", "plugin.jsonl" }));
            using var reader = new StreamReader(archive.GetEntry("plugin.jsonl")!.Open());
            Assert.That(reader.ReadToEnd(), Does.Contain("known-event"));
            Assert.Throws<IOException>(() => sink.Export(Path.Combine(settings.DirectoryPath, "report.jsonl")));
            settings.DirectoryPath = "relative"; Assert.Throws<ArgumentException>(settings.Validate);
            settings.DirectoryPath = root; settings.MaximumBytes = 1; Assert.Throws<ArgumentOutOfRangeException>(settings.Validate);
            settings.MaximumBytes = 1024; settings.RetainedFiles = 1; Assert.Throws<ArgumentOutOfRangeException>(settings.Validate);
            settings.RetainedFiles = 2; settings.RetentionDays = 0; Assert.Throws<ArgumentOutOfRangeException>(settings.Validate);
        }
        [Test]
        public void OperationScopeCorrelatesEventsAndDeduplicatesTheSameFailure()
        {
            var id = Guid.NewGuid().ToString("N");
            using (var scope = PluginLog.Begin("test.scope", "s", id, "before"))
            {
                scope.Success("after"); scope.Success("ignored");
            }
            var success = PluginLog.Recent().Single(entry => entry.DocumentId == id && entry.Outcome == "success");
            Assert.That(success.Revision, Is.EqualTo("after")); Assert.That(success.OperationId, Is.Not.Empty);
            Assert.That(success.PreviousRevision, Is.EqualTo("before"));
            using (var scope = PluginLog.Begin("test.validation", "s", id + "failure", "before"))
            {
                var issue = new FormatException("private-input"); scope.Fail(issue);
                Assert.That(PluginLog.Failure("test.validation", issue), Is.EqualTo(scope.LastEventId));
            }
            var failure = PluginLog.Recent().Single(entry => entry.DocumentId == id + "failure");
            Assert.That(failure.Count, Is.EqualTo(1)); Assert.That(failure.Category, Is.EqualTo("validation"));
            Assert.That(PluginLog.Flush(), Is.True);
        }
        [Test]
        public void FailurePropagationUsesOneEventUntilANewRootOperationBegins()
        {
            var documentId = Guid.NewGuid().ToString("N");
            var issue = new FormatException("private-input"); string first;
            using (var outer = PluginLog.Begin("test.outer", documentId: documentId))
            {
                using (var inner = PluginLog.Begin("test.inner", documentId: documentId))
                { inner.Fail(issue); first = inner.LastEventId; }
                outer.Fail(issue); Assert.That(outer.LastEventId, Is.EqualTo(first));
            }
            Assert.That(PluginLog.FailureMessage("test.boundary", issue), Does.EndWith(first));
            using (var next = PluginLog.Begin("test.new", documentId: documentId))
            { next.Fail(issue); Assert.That(next.LastEventId, Is.Not.EqualTo(first)); }
            Assert.That(PluginLog.Recent().Count(entry => entry.DocumentId == documentId && entry.Outcome == "failed"), Is.EqualTo(2));
        }
        [Test]
        public void RecentHistoryIncludesRotatedFilesInChronologicalOrder()
        {
            var sink = new PluginLogFile(Path.Combine(root, "logs"), 1200, 10);
            for (var index = 0; index < 8; index++) sink.Write(new DiagnosticEvent { Id = index.ToString(), Operation = "test" });
            Assert.That(sink.ReadRecent().Select(entry => entry.Id), Is.EqualTo(Enumerable.Range(0, 8).Select(index => index.ToString())));
            Assert.That(sink.ReadRecent(3).Select(entry => entry.Id), Is.EqualTo(new[] { "5", "6", "7" }));
        }
        [Test]
        public void AggregatedIssuesRetainSearchableRecentEventAndOperationIds()
        {
            var sink = new PluginLogFile(Path.Combine(root, "logs"), 1024 * 1024, 3);
            var writer = new DiagnosticDispatcher(sink.Write);
            try
            {
                for (var index = 0; index < 70; index++)
                {
                    var entry = DiagnosticEvent.Failure("test.aggregate", new IOException());
                    entry.Id = "event-" + index; entry.OperationId = "operation-" + index; writer.Enqueue(entry);
                }
                Assert.That(writer.Flush(2000), Is.True);
                var aggregate = writer.Recent().Single();
                Assert.That(aggregate.Id, Is.EqualTo("event-0")); Assert.That(aggregate.Count, Is.EqualTo(70));
                Assert.That(aggregate.RelatedEventIds, Has.Length.EqualTo(64));
                Assert.That(aggregate.RelatedEventIds.Last(), Is.EqualTo("event-69"));
                Assert.That(aggregate.RelatedOperationIds.Last(), Is.EqualTo("operation-69"));
                Assert.That(sink.ReadRecent().Last().Id, Is.EqualTo("event-69"));
            }
            finally { writer.Stop(1000); }
        }
        [TestCase(@"D:logs")]
        [TestCase(@"\logs")]
        public void WindowsDriveRelativeAndRootRelativeLogPathsAreRejected(string path)
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("Windows path semantics");
            Assert.Throws<ArgumentException>(() => new DiagnosticSettings { DirectoryPath = path }.Validate());
            Assert.DoesNotThrow(() => new DiagnosticSettings { DirectoryPath = @"D:\logs" }.Validate());
        }
        [Test]
        public void ExportAndRecentHistoryApplyTheSameRetentionCutoff()
        {
            var directory = Path.Combine(root, "logs"); Directory.CreateDirectory(directory);
            var stale = DiagnosticEvent.Failure("expired-operation", new IOException());
            var expiredFile = Path.Combine(directory, "plugin.1.jsonl");
            File.WriteAllText(expiredFile, stale.ToJson().ToString(Newtonsoft.Json.Formatting.None) + "\n"); File.SetLastWriteTimeUtc(expiredFile, DateTime.UtcNow.AddDays(-20));
            var current = new DiagnosticEvent { Operation = "current-operation", Id = "current" };
            File.WriteAllText(Path.Combine(directory, "plugin.jsonl"), current.ToJson().ToString(Newtonsoft.Json.Formatting.None) + "\n");
            var sink = new PluginLogFile(directory, 8192, 3, 14);
            var exported = Path.Combine(root, "export.jsonl"); sink.Export(exported);
            Assert.That(File.ReadAllText(exported), Does.Not.Contain("expired-operation"));
            Assert.That(File.ReadAllText(exported), Does.Contain("current-operation"));
            Assert.That(sink.ReadRecent().Single().Id, Is.EqualTo("current"));
        }
        [Test]
        public void FailedConfigurationPersistenceLeavesCurrentWriterUsable()
        {
            var writes = 0;
            var current = new DiagnosticDispatcher(_ => Interlocked.Increment(ref writes));
            DiagnosticDispatcher? replacement = null;
            try
            {
                Assert.Throws<IOException>(() => DiagnosticDispatcher.PrepareReplacement(current, _ => { }, () => throw new IOException()));
                Assert.That(current.Enqueue(new DiagnosticEvent()), Is.True); Assert.That(current.Flush(1000), Is.True);
                Assert.That(writes, Is.EqualTo(1));
                var saved = false;
                replacement = DiagnosticDispatcher.PrepareReplacement(current, _ => Interlocked.Increment(ref writes), () => saved = true);
                Assert.That(saved, Is.True); current.Stop(0);
                Assert.That(replacement.Enqueue(new DiagnosticEvent()), Is.True); Assert.That(replacement.Flush(1000), Is.True);
                Assert.That(writes, Is.EqualTo(2));
            }
            finally { current.Stop(1000); replacement?.Stop(1000); }
        }
    }
}
