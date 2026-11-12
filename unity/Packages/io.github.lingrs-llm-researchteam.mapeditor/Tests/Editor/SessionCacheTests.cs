using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Tests
{
    public sealed class SessionCacheTests
    {
        [Test]
        public void RepeatedDocumentAccessSharesImmutableTreeAndExternalReadsCannotMutateIt()
        {
            var session = ScriptableObject.CreateInstance<DocumentSession>();
            try
            {
                session.Load(LevelDocument.Create(2, 2).ToJson(), "", "");
                var document = session.Document;
                Assert.That(session.Document, Is.SameAs(document));
                var cell = document.GetCell(0, 0); cell["tileKey"] = "mutated";
                var map = document.GetSection("mapData"); map["tiles"][0]["tileKey"] = "mutated-again";
                Assert.That(session.Document.GetCell(0, 0)["tileKey"].Value<string>(), Is.EqualTo("tile_forbidden"));
                Assert.That(session.Document, Is.SameAs(document));
                Assert.That(session.IsDirty, Is.False);
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
        }

        [Test]
        public void EditUndoRedoLoadAndRecoveryAlwaysResolveCurrentRevision()
        {
            var session = ScriptableObject.CreateInstance<DocumentSession>();
            try
            {
                session.Load(LevelDocument.Create(2, 2).WithLevelId("before").ToJson(), "", "");
                var original = session.Document;
                var next = original.WithLevelId("after");
                session.Edit(next, "Rename");
                Assert.That(session.Document, Is.SameAs(next));
                Undo.PerformUndo();
                Assert.That(session.Document.LevelId, Is.EqualTo("before"));
                Assert.That(session.Document, Is.SameAs(session.Document));
                Undo.PerformRedo();
                Assert.That(session.Document.LevelId, Is.EqualTo("after"));
                var snapshot = session.CaptureRecovery("invalid draft");
                session.Load(LevelDocument.Create(3, 4).WithLevelId("loaded").ToJson(), "", "");
                Assert.That(session.Document.Width, Is.EqualTo(3));
                Assert.That(session.Document.LevelId, Is.EqualTo("loaded"));
                Assert.That(session.RestoreRecovery(snapshot), Is.EqualTo("invalid draft"));
                Assert.That(session.Document.LevelId, Is.EqualTo("after"));
                Assert.That(session.Document.Width, Is.EqualTo(2));
                Assert.That(session.Document, Is.SameAs(session.Document));
            }
            finally { Undo.ClearUndo(session); Object.DestroyImmediate(session); }
        }

        [Test]
        public void SerializedJsonChangeInvalidatesExistingCacheAndFreshSessionLazilyRebuilds()
        {
            var source = ScriptableObject.CreateInstance<DocumentSession>();
            var populated = ScriptableObject.CreateInstance<DocumentSession>();
            var fresh = ScriptableObject.CreateInstance<DocumentSession>();
            try
            {
                source.Load(LevelDocument.Create(7, 3).WithLevelId("restored").ToJson(), "", "");
                populated.Load(LevelDocument.Create(2, 2).ToJson(), "", "");
                var oldCache = populated.Document;
                var serialized = EditorJsonUtility.ToJson(source);
                Assert.That(serialized, Does.Not.Contain("cachedDocument"));
                // Exercise JSON restoration without sharing another session's owned
                // checkpoint ScriptableObject (or orphaning the populated checkpoint).
                var fields = JObject.Parse(serialized); fields.Remove("saved"); serialized = fields.ToString();
                EditorJsonUtility.FromJsonOverwrite(serialized, populated);
                EditorJsonUtility.FromJsonOverwrite(serialized, fresh);
                Assert.That(populated.Document, Is.Not.SameAs(oldCache));
                foreach (var session in new[] { populated, fresh })
                {
                    Assert.That(session.Document.Width, Is.EqualTo(7));
                    Assert.That(session.Document.LevelId, Is.EqualTo("restored"));
                    Assert.That(session.Document, Is.SameAs(session.Document));
                }
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(populated); Object.DestroyImmediate(fresh); }
        }

        [Test]
        public void InvalidLoadRetainsDocumentCacheAndDirtyCheckpoint()
        {
            var session = ScriptableObject.CreateInstance<DocumentSession>();
            try
            {
                session.Load(LevelDocument.Create(2, 2).ToJson(), "", "");
                var document = session.Document;
                Assert.Throws<LevelDocumentException>(() => session.Load("{bad", "", ""));
                Assert.That(session.Document, Is.SameAs(document));
                Assert.That(session.IsDirty, Is.False);
            }
            finally { Object.DestroyImmediate(session); }
        }
    }
}
