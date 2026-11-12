#nullable enable
using System;
using System.IO;
using Arknights.MapEditor.Storage;
using NUnit.Framework;

namespace Arknights.MapEditor.Tests.Storage
{
    public sealed class ProjectAndRecoveryTests
    {
        private string directory = null!;
        [SetUp] public void SetUp() { directory = Path.Combine(Path.GetTempPath(), "ark-project-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); }
        [TearDown] public void TearDown() { Directory.Delete(directory, true); }
        private static RecoverySnapshot Snapshot(string draft = "draft") => new RecoverySnapshot("{}", draft, "{}", "source.json", "source-hash");

        [Test] public void RecoverySessionsAreIsolatedAndDiscardRequiresOwnership()
        {
            var first = new AutoRecovery(directory, Guid.NewGuid());
            var second = new AutoRecovery(directory, Guid.NewGuid());
            first.Save(Snapshot()); second.Save(Snapshot());
            var restored = new AutoRecovery(directory, first.SessionId);
            Assert.Throws<DocumentFileConflictException>(() => restored.Discard());
            Assert.That(restored.Load().SourceHash, Is.EqualTo("source-hash"));
            restored.Discard();
            Assert.That(File.Exists(first.CachePath), Is.False);
            Assert.That(File.Exists(second.CachePath), Is.True);
        }

        [Test] public void RecoveryCannotOverwriteOrDeleteExternalChanges()
        {
            var recovery = new AutoRecovery(directory, Guid.NewGuid()); recovery.Save(Snapshot());
            File.WriteAllText(recovery.CachePath, "external");
            Assert.Throws<DocumentFileConflictException>(() => recovery.Save(Snapshot("next")));
            Assert.Throws<DocumentFileConflictException>(() => recovery.Discard());
            Assert.That(File.ReadAllText(recovery.CachePath), Is.EqualTo("external"));
        }

        [Test] public void ProjectMovesWithoutChangingRelativeReferences()
        {
            var project = new ArkMapProject("maps/level.json", new[] { new ArtMapping("ground", "art/ground.mat", new string('a',32)) });
            var path = Path.Combine(directory, "one.arkmap");
            var hash = ArkMapProjectStore.Save(path, project, null);
            var loaded = ArkMapProjectStore.Read(path, out var actual);
            Assert.That(actual, Is.EqualTo(hash));
            Assert.That(loaded.ArtMappings[0].UnityGuid, Is.EqualTo(new string('a',32)));
            var moved = Path.Combine(directory, "moved"); Directory.CreateDirectory(moved);
            var next = Path.Combine(moved, "one.arkmap"); File.Move(path, next);
            Assert.That(ArkMapProjectStore.ResolveLevelDataPath(next, ArkMapProjectStore.Read(next,out _)), Is.EqualTo(Path.Combine(moved,"maps","level.json")));
        }

        [TestCase("../outside.json")][TestCase("maps/../../outside.json")][TestCase("C:/outside.json")][TestCase("/outside.json")][TestCase("maps\\level.json")][TestCase("https://host/map.json")][TestCase("NUL.json")][TestCase("maps/COM1")][TestCase("maps/line\n.json")]
        public void ProjectRejectsUnsafeRelativePaths(string path)
        { Assert.Throws<ArgumentException>(() => new ArkMapProject(path, Array.Empty<ArtMapping>())); }

        [Test] public void ProjectRejectsDuplicateArtKeys()
        { Assert.Throws<ArgumentException>(() => new ArkMapProject("map.json",new[] {new ArtMapping("a","a.mat"),new ArtMapping("a","b.mat")})); }

        [TestCase("{\"version\":2,\"levelDataPath\":\"map.json\",\"artMappings\":[]}")]
        [TestCase("{\"version\":1,\"version\":1,\"levelDataPath\":\"map.json\",\"artMappings\":[]}")]
        [TestCase("{\"version\":1,\"levelDataPath\":42,\"artMappings\":[]}")]
        public void ProjectRejectsMalformedEnvelope(string json)
        {
            var path=Path.Combine(directory,"bad.arkmap"); File.WriteAllText(path,json);
            Assert.Throws<InvalidDataException>(() => ArkMapProjectStore.Read(path,out _));
        }
    }
}
