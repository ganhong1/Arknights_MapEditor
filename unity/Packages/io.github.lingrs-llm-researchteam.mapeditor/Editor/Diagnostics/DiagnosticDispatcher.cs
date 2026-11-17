#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Arknights.MapEditor.Editor
{
    /// <summary>Single writer with a bounded queue and bounded shutdown. Owns no Unity objects.</summary>
    internal sealed class DiagnosticDispatcher
    {
        private readonly object gate = new();
        private readonly Queue<DiagnosticEvent> queue = new();
        private readonly List<DiagnosticEvent> recent = new();
        private readonly AutoResetEvent wake = new(false);
        private readonly ManualResetEventSlim idle = new(true);
        private readonly Thread worker;
        private readonly Action<DiagnosticEvent> write;
        private readonly int capacity;
        private bool stopping, writing, ended, handlesDisposed;
        private int waiters;
        private long dropped, failures;
        private string lastError = "";
        internal DiagnosticDispatcher(Action<DiagnosticEvent> write, int capacity = 1024, IEnumerable<DiagnosticEvent>? history = null)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.write = write; this.capacity = capacity;
            if (history != null) recent.AddRange(history.TakeLast(256).Select(item => item.Clone()));
            worker = new Thread(Run) { Name = "Arknights diagnostics writer", IsBackground = true }; worker.Start();
        }
        internal bool Enqueue(DiagnosticEvent value)
        {
            var entry = value.Clone();
            entry.RelatedEventIds = new[] { entry.Id };
            entry.RelatedOperationIds = entry.OperationId.Length == 0 ? Array.Empty<string>() : new[] { entry.OperationId };
            lock (gate)
            {
                var prior = recent.LastOrDefault(item => item.Operation == entry.Operation && item.Code == entry.Code &&
                    item.DocumentId == entry.DocumentId && item.Session == entry.Session && item.Outcome == "failed" &&
                    item.Stack.SequenceEqual(entry.Stack) && (entry.Utc - item.LastUtc).TotalSeconds <= 60);
                if (prior != null && entry.Outcome == "failed")
                {
                    prior.Count++; prior.LastUtc = entry.Utc;
                    prior.RelatedEventIds = Remember(prior.RelatedEventIds, entry.Id);
                    prior.RelatedOperationIds = Remember(prior.RelatedOperationIds, entry.OperationId);
                }
                else { recent.Add(entry.Clone()); if (recent.Count > 256) recent.RemoveAt(0); }
                if (stopping || queue.Count >= capacity) { dropped++; return false; }
                queue.Enqueue(entry); idle.Reset(); wake.Set(); return true;
            }
        }
        private static string[] Remember(string[] values, string value) => value.Length == 0 ? values :
            values.Where(item => item != value).Append(value).TakeLast(64).ToArray();
        internal static DiagnosticDispatcher PrepareReplacement(DiagnosticDispatcher current,
            Action<DiagnosticEvent> write, Action persist)
        {
            if (!current.Flush(1500)) throw new TimeoutException("日志写入尚未结束，请稍后保存设置。");
            var candidate = new DiagnosticDispatcher(write, history: current.Recent().Reverse());
            try { persist(); return candidate; }
            catch { candidate.Stop(0); throw; }
        }
        internal IReadOnlyList<DiagnosticEvent> Recent() { lock (gate) return recent.Select(item => item.Clone()).Reverse().ToArray(); }
        internal DiagnosticHealth Health
        {
            get { lock (gate) return new DiagnosticHealth { Pending = queue.Count + (writing ? 1 : 0), Dropped = dropped,
                WriteFailures = failures, LastWriteError = lastError }; }
        }
        internal bool Flush(int milliseconds)
        {
            lock (gate)
            {
                if (handlesDisposed) return queue.Count == 0 && !writing;
                waiters++;
            }
            try { return idle.Wait(milliseconds); }
            finally { lock (gate) { waiters--; DisposeHandlesIfReady(); } }
        }
        internal bool Stop(int milliseconds)
        {
            lock (gate) { if (ended) return true; stopping = true; wake.Set(); }
            return worker.Join(milliseconds);
        }
        internal bool HandlesDisposed { get { lock (gate) return handlesDisposed; } }
        private void DisposeHandlesIfReady()
        {
            if (!ended || waiters != 0 || handlesDisposed) return;
            handlesDisposed = true; wake.Dispose(); idle.Dispose();
        }
        private void Run()
        {
            try { Work(); }
            finally { lock (gate) { ended = true; DisposeHandlesIfReady(); } }
        }
        private void Work()
        {
            while (true)
            {
                DiagnosticEvent? entry = null;
                lock (gate)
                {
                    if (queue.Count > 0) { entry = queue.Dequeue(); writing = true; }
                    else { idle.Set(); if (stopping) return; }
                }
                if (entry == null) { wake.WaitOne(); continue; }
                try { write(entry); lock (gate) lastError = ""; }
                catch (Exception error) { lock (gate) { failures++; lastError = error.GetType().Name; } }
                finally { lock (gate) { writing = false; if (queue.Count == 0) idle.Set(); } }
            }
        }
    }
}
