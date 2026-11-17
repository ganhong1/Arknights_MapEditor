#nullable enable
using System;
using System.IO;

namespace Arknights.MapEditor.Storage
{
    /// <summary>Explicit per-session journal. The caller owns scheduling and chooses when to restore.</summary>
    public sealed class AutoRecovery
    {
        public Guid SessionId { get; }
        public string CachePath { get; }
        public string? ExpectedHash { get; private set; }

        public AutoRecovery(string cacheDirectory, Guid sessionId)
        {
            if (sessionId == Guid.Empty) throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
            if (string.IsNullOrWhiteSpace(cacheDirectory)) throw new ArgumentException("Cache directory is required.", nameof(cacheDirectory));
            var directory = Path.GetFullPath(cacheDirectory);
            SafeProjectPath.RejectLinks(directory);
            Directory.CreateDirectory(directory);
            SessionId = sessionId;
            CachePath = Path.Combine(directory, sessionId.ToString("N") + ".recovery.json");
        }

        public void Save(RecoverySnapshot snapshot)
        {
            SafeProjectPath.RejectLinks(CachePath);
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            ExpectedHash = RecoveryStore.Save(CachePath, snapshot, ExpectedHash);
        }

        public RecoverySnapshot Load()
        {
            SafeProjectPath.RejectLinks(CachePath);
            var snapshot = RecoveryStore.Read(CachePath, out var hash);
            ExpectedHash = hash;
            return snapshot;
        }

        /// <summary>Optimistic digest guard, not cross-process compare-and-delete.</summary>
        public void Discard()
        {
            if (ExpectedHash == null) throw new DocumentFileConflictException("Restore or create this session before discarding its journal.");
            SafeProjectPath.RejectLinks(CachePath);
            var current = RecoveryStore.ReadHash(CachePath);
            if (!string.Equals(current, ExpectedHash, StringComparison.Ordinal))
                throw new DocumentFileConflictException("Recovery journal was changed externally.");
            File.Delete(CachePath);
            ExpectedHash = null;
        }
    }
}
