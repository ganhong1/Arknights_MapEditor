#nullable enable
using System;
using System.IO;
using System.Text;
using Arknights.MapEditor.Storage;
using NUnit.Framework;

namespace Arknights.MapEditor.Tests.Storage
{
    public sealed class DocumentFileStoreTests
    {
        private string directory = null!;
        private string path = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "ark-map-storage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "map.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void SaveNewFileReturnsExactTextAndByteDigest()
        {
            const string text = "{\"name\":\"地图🌲\",\"unknown\":null}";
            var saved = DocumentFileStore.Save(path, text, null);
            var read = DocumentFileStore.Read(path);
            Assert.That(read.Text, Is.EqualTo(text));
            Assert.That(read.Sha256, Is.EqualTo(saved.Sha256));
            Assert.That(read.Sha256, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(new UTF8Encoding(false, true).GetBytes(text)));
            Assert.That(Directory.GetFiles(directory).Length, Is.EqualTo(1));
        }

        [Test]
        public void MatchingExpectedDigestAllowsAtomicReplacement()
        {
            var original = DocumentFileStore.Save(path, "{}", null);
            var updated = DocumentFileStore.Save(path, "{\"map\":1}", original.Sha256.ToUpperInvariant());
            Assert.That(DocumentFileStore.Read(path).Text, Is.EqualTo("{\"map\":1}"));
            Assert.That(updated.Sha256, Is.Not.EqualTo(original.Sha256));
            Assert.That(Directory.GetFiles(directory).Length, Is.EqualTo(1));
        }

        [Test]
        public void NullExpectedDigestNeverOverwritesExistingFile()
        {
            DocumentFileStore.Save(path, "original", null);
            Assert.Throws<DocumentFileConflictException>(() => DocumentFileStore.Save(path, "replacement", null));
            Assert.That(File.ReadAllText(path), Is.EqualTo("original"));
        }

        [Test]
        public void ExternalEditIsPreservedWhenDigestDoesNotMatch()
        {
            var original = DocumentFileStore.Save(path, "original", null);
            File.WriteAllText(path, "external");
            Assert.Throws<DocumentFileConflictException>(() => DocumentFileStore.Save(path, "replacement", original.Sha256));
            Assert.That(File.ReadAllText(path), Is.EqualTo("external"));
            Assert.That(Directory.GetFiles(directory).Length, Is.EqualTo(1));
        }

        [Test]
        public void DeletedDestinationIsConflictRatherThanSilentRecreation()
        {
            var original = DocumentFileStore.Save(path, "original", null);
            File.Delete(path);
            Assert.Throws<DocumentFileConflictException>(() => DocumentFileStore.Save(path, "replacement", original.Sha256));
            Assert.That(File.Exists(path), Is.False);
        }

        [Test]
        public void InvalidDigestIsRejectedBeforeWriting()
        {
            Assert.Throws<ArgumentException>(() => DocumentFileStore.Save(path, "{}", "not-a-sha"));
            Assert.That(Directory.GetFiles(directory), Is.Empty);
        }

        [Test]
        public void ReadAcceptsUtf8BomButDigestIncludesOriginalBomBytes()
        {
            File.WriteAllText(path, "{}", new UTF8Encoding(true, true));
            var withBom = DocumentFileStore.Read(path);
            Assert.That(withBom.Text, Is.EqualTo("{}"));
            var withoutBom = DocumentFileStore.Save(path, "{}", withBom.Sha256);
            Assert.That(withoutBom.Sha256, Is.Not.EqualTo(withBom.Sha256));
        }

        [Test]
        public void ReadRejectsInvalidUtf8InsteadOfReplacingCharacters()
        {
            File.WriteAllBytes(path, new byte[] { 0xc3, 0x28 });
            Assert.Throws<DecoderFallbackException>(() => DocumentFileStore.Read(path));
        }

        [Test]
        public void InvalidUtf16InputDoesNotDamageOldFile()
        {
            var original = DocumentFileStore.Save(path, "original", null);
            Assert.Throws<EncoderFallbackException>(() => DocumentFileStore.Save(path, "\ud800", original.Sha256));
            Assert.That(DocumentFileStore.Read(path).Text, Is.EqualTo("original"));
            Assert.That(Directory.GetFiles(directory).Length, Is.EqualTo(1));
        }

        [Test]
        public void SaveDoesNotCreateMissingParentDirectories()
        {
            var absent = Path.Combine(directory, "absent", "map.json");
            Assert.Throws<DirectoryNotFoundException>(() => DocumentFileStore.Save(absent, "{}", null));
            Assert.That(Directory.Exists(Path.GetDirectoryName(absent)), Is.False);
        }

        [Test]
        public void LockedFileFailurePreservesExistingBytes()
        {
            var original = DocumentFileStore.Save(path, "original", null);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Throws<IOException>(() => DocumentFileStore.Save(path, "replacement", original.Sha256));
            }
            Assert.That(DocumentFileStore.Read(path).Text, Is.EqualTo("original"));
            Assert.That(Directory.GetFiles(directory).Length, Is.EqualTo(1));
        }

        [Test]
        public void ReadEnforcesBoundedFileSize()
        {
            using (var file = File.Create(path)) file.SetLength(DocumentFileStore.MaximumDocumentBytes + 1L);
            Assert.Throws<InvalidDataException>(() => DocumentFileStore.Read(path));
        }
    }
}
