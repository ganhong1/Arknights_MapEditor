using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using Arknights.MapEditor.Storage;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Tests
{
    public sealed class SessionTests
    {
        [Test]
        public void RecoveryPreservesAppliedDocumentCheckpointAndInvalidDraftSeparately()
        {
            var source = ScriptableObject.CreateInstance<DocumentSession>();
            var restored = ScriptableObject.CreateInstance<DocumentSession>();
            try
            {
                var checkpoint = LevelDocument.Create(7, 3).WithLevelId("checkpoint").ToJson();
                source.Load(checkpoint, "map.json", new string('a', 64));
                source.Edit(source.Document.WithLevelId("applied"), "Edit before recovery");
                const string invalidDraft = "{ not yet valid JSON";
                var envelope = source.CaptureRecovery(invalidDraft);
                Assert.That(restored.RestoreRecovery(envelope), Is.EqualTo(invalidDraft));
                Assert.That(restored.Document.LevelId, Is.EqualTo("applied"));
                Assert.That(restored.IsDirty, Is.True);
                var captured = restored.CaptureRecovery(invalidDraft);
                Assert.That(captured.SavedJson, Is.EqualTo(checkpoint));
                Assert.That(captured.SourcePath, Is.EqualTo("map.json"));
                Assert.That(captured.SourceHash, Is.EqualTo(new string('a', 64)));
                Assert.That(captured.DraftJson, Is.EqualTo(invalidDraft));
                restored.Edit(LevelDocument.Parse(checkpoint), "Return to checkpoint");
                Assert.That(restored.IsDirty, Is.False);
            }
            finally
            {
                Undo.ClearUndo(source); Undo.ClearUndo(restored);
                Object.DestroyImmediate(source); Object.DestroyImmediate(restored);
            }
        }

        [TestCase("saved-json")]
        [TestCase("hash")]
        [TestCase("missing-hash")]
        [TestCase("missing-path")]
        public void InvalidRecoveryDoesNotMutateExistingSessionOrCheckpoint(string invalidPart)
        {
            var session = ScriptableObject.CreateInstance<DocumentSession>();
            try
            {
                session.Load(LevelDocument.Create(3, 2).ToJson(), "original.json", new string('a', 64));
                session.Edit(session.Document.WithLevelId("keep"), "Original edit");
                var before = session.CaptureRecovery("keep draft");
                var other = LevelDocument.Create(9, 4).WithLevelId("must-not-apply").ToJson();
                var envelope = new RecoverySnapshot(other, "incoming draft",
                    invalidPart == "saved-json" ? "broken" : other,
                    invalidPart == "missing-path" ? "" : "other.json",
                    invalidPart == "hash" ? "not-a-hash" : invalidPart == "missing-hash" ? "" : new string('b', 64));
                Assert.That(() => session.RestoreRecovery(envelope), Throws.InstanceOf<System.FormatException>());
                var after = session.CaptureRecovery("keep draft");
                Assert.That(after.DocumentJson, Is.EqualTo(before.DocumentJson));
                Assert.That(after.SavedJson, Is.EqualTo(before.SavedJson));
                Assert.That(after.SourcePath, Is.EqualTo(before.SourcePath));
                Assert.That(after.SourceHash, Is.EqualTo(before.SourceHash));
                Assert.That(session.IsDirty, Is.True);
                Undo.PerformUndo();
                Assert.That(session.IsDirty, Is.False, "Rejected restore must not clear the existing undo history.");
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
        }

        [Test]
        public void RecoveredSessionStillRejectsExternallyChangedSourceOnSave()
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "arkmap-recovery-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "map.json");
            var source = ScriptableObject.CreateInstance<DocumentSession>();
            var restored = ScriptableObject.CreateInstance<DocumentSession>();
            try
            {
                source.Load(LevelDocument.Create(3, 2).ToJson(), "", "");
                source.Save(path);
                source.Edit(source.Document.WithLevelId("pending"), "Pending edit");
                var snapshot = source.CaptureRecovery("unapplied draft");
                restored.RestoreRecovery(snapshot);
                var external = LevelDocument.Create(3, 2).WithLevelId("external").ToJson();
                System.IO.File.WriteAllText(path, external);
                Assert.Throws<DocumentFileConflictException>(() => restored.Save(path));
                Assert.That(System.IO.File.ReadAllText(path), Is.EqualTo(external));
                Assert.That(restored.Document.LevelId, Is.EqualTo("pending"));
                Assert.That(restored.IsDirty, Is.True);
                Assert.That(restored.CaptureRecovery("draft").SourceHash, Is.EqualTo(snapshot.SourceHash));
            }
            finally
            {
                Undo.ClearUndo(source); Undo.ClearUndo(restored);
                Object.DestroyImmediate(source); Object.DestroyImmediate(restored);
                System.IO.Directory.Delete(directory, true);
            }
        }

        [Test]
        public void SaveThenUndoDoesNotUndoTheDiskCheckpoint()
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "arkmap-session-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "map.json");
            var session = ScriptableObject.CreateInstance<DocumentSession>();
            try
            {
                session.Load(LevelDocument.Create(3, 2).ToJson(), "", "");
                session.Edit(session.Document.WithLevelId("saved"), "Rename");
                session.Save(path);
                Assert.That(session.IsDirty, Is.False);
                Undo.PerformUndo();
                Assert.That(session.IsDirty, Is.True);
                Assert.That(LevelDocument.Parse(System.IO.File.ReadAllText(path)).LevelId, Is.EqualTo("saved"));
                Undo.PerformRedo();
                Assert.That(session.IsDirty, Is.False);
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); System.IO.Directory.Delete(directory, true); }
        }

        [Test]
        public void EditUndoRedoRestoresDocumentAndDirtyState()
        {
            var session = ScriptableObject.CreateInstance<DocumentSession>();
            try
            {
                session.Load(LevelDocument.Create(3, 2).ToJson(), "", "");
                var original = session.Json;
                session.Edit(session.Document.WithLevelId("custom_one"), "Set level id");
                Assert.That(session.IsDirty, Is.True);
                Undo.PerformUndo();
                Assert.That(session.Json, Is.EqualTo(original));
                Assert.That(session.IsDirty, Is.False);
                Undo.PerformRedo();
                Assert.That(session.Document.LevelId, Is.EqualTo("custom_one"));
                Assert.That(session.IsDirty, Is.True);
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
        }

        [Test]
        public void SerializedSessionRestoresWithoutKeepingJsonTreeAlive()
        {
            var source = ScriptableObject.CreateInstance<DocumentSession>();
            var restored = ScriptableObject.CreateInstance<DocumentSession>();
            try
            {
                source.Load(LevelDocument.Create(7, 3).ToJson(), "", "");
                source.Edit(source.Document.WithLevelId("restored"), "Rename");
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), restored);
                Assert.That(restored.Document.Width, Is.EqualTo(7));
                Assert.That(restored.Document.Height, Is.EqualTo(3));
                Assert.That(restored.Document.LevelId, Is.EqualTo("restored"));
                Assert.That(restored.IsDirty, Is.True);
            }
            finally { Undo.ClearUndo(source); Object.DestroyImmediate(source); Object.DestroyImmediate(restored); }
        }
    }
}
