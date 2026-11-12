#nullable enable
using System;
using System.IO;
using Arknights.MapEditor.Storage;
using NUnit.Framework;

namespace Arknights.MapEditor.Tests.Storage
{
    public sealed class RecoveryStoreTests
    {
        private string directory = null!;
        private string path = null!;
        [SetUp] public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "ark-recovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "recovery.json");
        }
        [TearDown] public void TearDown() => Directory.Delete(directory, true);

        [Test] public void RoundTripPreservesInvalidDraftWithoutWritingSource()
        {
            var source = Path.Combine(directory, "source.json");
            File.WriteAllText(source, "unchanged");
            var value = new RecoverySnapshot("{}", "{broken 草稿", "{\"old\":true}", source, "oldhash");
            var hash = RecoveryStore.Save(path, value, null);
            var read = RecoveryStore.Read(path, out var readHash);
            Assert.That(readHash, Is.EqualTo(hash));
            Assert.That(read.DocumentJson, Is.EqualTo(value.DocumentJson));
            Assert.That(read.DraftJson, Is.EqualTo(value.DraftJson));
            Assert.That(read.SavedJson, Is.EqualTo(value.SavedJson));
            Assert.That(read.SourcePath, Is.EqualTo(source));
            Assert.That(read.SourceHash, Is.EqualTo("oldhash"));
            Assert.That(File.ReadAllText(source), Is.EqualTo("unchanged"));
        }

        [Test] public void RequiresCallerOwnedHashToReplaceCache()
        {
            var value = new RecoverySnapshot("{}", "", "", "", "");
            var hash = RecoveryStore.Save(path, value, null);
            Assert.Throws<DocumentFileConflictException>(() => RecoveryStore.Save(path, value, null));
            RecoveryStore.Save(path, new RecoverySnapshot("{}", "next", "", "", ""), hash);
            Assert.Throws<DocumentFileConflictException>(() => RecoveryStore.Save(path, value, hash));
        }

        [TestCase("{\"version\":2}")]
        [TestCase("{\"version\":1,\"version\":1}")]
        [TestCase("{\"version\":1,\"DocumentJson\":null}")]
        [TestCase("{\"version\":1,\"DocumentJson\":4}")]
        [TestCase("[]")]
        [TestCase("{} {}")]
        public void RejectsDamagedEnvelope(string json)
        {
            File.WriteAllText(path, json);
            Assert.Throws<InvalidDataException>(() => RecoveryStore.Read(path, out _));
        }

        [Test] public void RejectsInvalidUtf8()
        {
            File.WriteAllBytes(path, new byte[] { 0xc3, 0x28 });
            Assert.Throws<System.Text.DecoderFallbackException>(() => RecoveryStore.Read(path, out _));
        }

        [Test] public void UnifiedSnapshotFailurePreservesBothDocumentAndProject()
        {
            var old = new RecoverySnapshot("old-document", "old-draft", "saved", "", "", "old-project", "old-inspector");
            var hash = RecoveryStore.Save(path, old, null);
            var next = new RecoverySnapshot("new-document", "draft", "saved", "", "", new string('x', DocumentFileStore.MaximumDocumentBytes), "new-inspector");
            Assert.Throws<InvalidDataException>(() => RecoveryStore.Save(path, next, hash));
            var read = RecoveryStore.Read(path, out var unchanged);
            Assert.That(read.DocumentJson, Is.EqualTo("old-document"));
            Assert.That(read.ProjectJson, Is.EqualTo("old-project"));
            Assert.That(read.InspectorJson, Is.EqualTo("old-inspector"));
            Assert.That(unchanged, Is.EqualTo(hash));
            Assert.That(Directory.GetFiles(directory).Length, Is.EqualTo(1));
        }

        [Test] public void LegacyVersionOneHasExplicitlyEmptyAdditionalDrafts()
        {
            File.WriteAllText(path,"{\"version\":1,\"DocumentJson\":\"{}\",\"DraftJson\":\"x\",\"SavedJson\":\"{}\",\"SourcePath\":\"\",\"SourceHash\":\"\"}");
            var read=RecoveryStore.Read(path,out _);
            Assert.That(read.ProjectJson,Is.Empty);Assert.That(read.InspectorJson,Is.Empty);
        }

        [Test] public void VersionTwoRejectsMissingOrNonStringProjectFields()
        {
            var hash=RecoveryStore.Save(path,new RecoverySnapshot("{}","","","","","{}","{}"),null);
            var root=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));root.Remove("InspectorJson");
            File.WriteAllText(path,root.ToString());
            Assert.Throws<InvalidDataException>(()=>RecoveryStore.Read(path,out _));
            root["InspectorJson"]=4;File.WriteAllText(path,root.ToString());
            Assert.Throws<InvalidDataException>(()=>RecoveryStore.Read(path,out _));
        }
    }
}
