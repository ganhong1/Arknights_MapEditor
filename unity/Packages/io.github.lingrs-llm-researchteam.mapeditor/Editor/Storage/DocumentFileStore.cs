#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Arknights.MapEditor.Storage
{
    /// <summary>
    /// Bounded UTF-8 document persistence. Calls through this class are serialized in-process.
    /// The digest is checked before staging and immediately before replacing a file.
    /// This is optimistic conflict detection, not cross-process compare-and-swap: an external
    /// writer can still replace the destination between the final check and File.Replace.
    /// File.Replace is required for an existing target; no delete-then-move fallback is used.
    /// The caller selects an explicit file path; this class does not resolve project references.
    /// </summary>
    public static class DocumentFileStore
    {
        public const int MaximumDocumentBytes = 16 * 1024 * 1024;

        private static readonly object Gate = new object();
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static DocumentFileSnapshot Read(string path) => Read(path, MaximumDocumentBytes);

        internal static DocumentFileSnapshot Read(string path, int maximumBytes)
        {
            var fullPath = NormalizePath(path);
            lock (Gate) return ReadInternal(fullPath, maximumBytes);
        }

        /// <summary>
        /// Saves UTF-8 without BOM. A null expectedHash means create-only; it never overwrites.
        /// A non-null hash must be the SHA-256 of the last observed target bytes.
        /// Returns the text and digest of this write, not a claim that no later writer changed it.
        /// </summary>
        public static DocumentFileSnapshot Save(string path, string text, string? expectedHash) =>
            Save(path, text, expectedHash, MaximumDocumentBytes);

        internal static DocumentFileSnapshot Save(string path, string text, string? expectedHash, int maximumBytes)
        {
            var fullPath = NormalizePath(path);
            if (text == null) throw new ArgumentNullException(nameof(text));
            ValidateHash(expectedHash);
            if (StrictUtf8.GetByteCount(text) > maximumBytes)
                throw new InvalidDataException($"Document exceeds the {maximumBytes / (1024 * 1024)} MiB storage limit.");
            var bytes = StrictUtf8.GetBytes(text);
            var saved = new DocumentFileSnapshot(text, Hash(bytes));
            lock (Gate)
            {
                CheckExpectedFile(fullPath, expectedHash, maximumBytes);
                var parent = Path.GetDirectoryName(fullPath)!;
                var temporary = Path.Combine(parent, ".arkmap-" + Guid.NewGuid().ToString("N") + ".tmp");
                var ownsTemporary = false;
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        ownsTemporary = true;
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }

                    CheckExpectedFile(fullPath, expectedHash, maximumBytes);
                    if (expectedHash == null)
                        File.Move(temporary, fullPath);
                    else
                        File.Replace(temporary, fullPath, null);

                    ownsTemporary = false;
                    return saved;
                }
                catch (Exception failure)
                {
                    if (ownsTemporary)
                    {
                        try { File.Delete(temporary); }
                        catch (Exception cleanupFailure)
                        {
                            // Preserve the original failure and expose incomplete cleanup.
                            failure.Data["TemporaryCleanupFailure"] = cleanupFailure;
                            failure.Data["TemporaryPath"] = temporary;
                        }
                    }
                    throw;
                }
            }
        }

        private static DocumentFileSnapshot ReadInternal(string path, int maximumBytes)
        {
            RejectReparsePoint(path);
            byte[] bytes;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > maximumBytes)
                    throw new InvalidDataException($"Document exceeds the {maximumBytes / (1024 * 1024)} MiB storage limit.");
                bytes = new byte[(int)stream.Length];
                var offset = 0;
                while (offset < bytes.Length)
                {
                    var count = stream.Read(bytes, offset, bytes.Length - offset);
                    if (count == 0) throw new EndOfStreamException("Document changed while being read.");
                    offset += count;
                }
                if (stream.ReadByte() != -1)
                    throw new IOException("Document grew while being read.");
            }
            var hasBom = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf;
            var start = hasBom ? 3 : 0;
            return new DocumentFileSnapshot(StrictUtf8.GetString(bytes, start, bytes.Length - start), Hash(bytes));
        }

        private static void CheckExpectedFile(string path, string? expectedHash, int maximumBytes)
        {
            if (expectedHash == null)
            {
                if (File.Exists(path) || Directory.Exists(path))
                    throw new DocumentFileConflictException("Destination already exists; use its current digest or choose another path.");
                return;
            }
            if (!File.Exists(path))
                throw new DocumentFileConflictException("Destination was removed or is inaccessible; reload or save to another path.");
            if (!string.Equals(ReadHash(path, maximumBytes), expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new DocumentFileConflictException("Destination changed outside this editing session; reload or save to another path.");
        }

        internal static string ReadHash(string path, int maximumBytes)
        {
            RejectReparsePoint(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var length = stream.Length;
            if (length > maximumBytes)
                throw new InvalidDataException($"Document exceeds the {maximumBytes / (1024 * 1024)} MiB storage limit.");
            using var algorithm = SHA256.Create();
            var hash = algorithm.ComputeHash(stream);
            if (stream.Position != length || stream.Length != length)
                throw new IOException("Document changed while being read.");
            return FormatDigest(hash);
        }

        private static string FormatDigest(byte[] bytes) =>
            BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));
            return Path.GetFullPath(path);
        }

        private static void RejectReparsePoint(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Document paths must target regular files, not links or reparse points.");
        }

        private static void ValidateHash(string? hash)
        {
            if (hash == null) return;
            if (hash.Length != 64) throw new ArgumentException("Expected digest must contain 64 hexadecimal characters.", nameof(hash));
            foreach (var character in hash)
            {
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f') ||
                      (character >= 'A' && character <= 'F')))
                    throw new ArgumentException("Expected digest must contain 64 hexadecimal characters.", nameof(hash));
            }
        }

        private static string Hash(byte[] bytes)
        {
            using (var algorithm = SHA256.Create())
            {
                var digest = algorithm.ComputeHash(bytes);
                return FormatDigest(digest);
            }
        }
    }
}
