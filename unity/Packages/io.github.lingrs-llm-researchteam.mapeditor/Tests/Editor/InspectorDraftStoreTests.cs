using System;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using NUnit.Framework;

namespace Arknights.MapEditor.Tests
{
    public sealed class InspectorDraftStoreTests
    {
        private static string Document => LevelDocument.Create(2, 2).ToJson();

        [Test]
        public void BeginAndCapturePreserveOriginalBaselineAndInvalidRawText()
        {
            var store = new InspectorDraftStore();
            store.Begin("route", Document, "routes/0", "{}");
            Assert.That(store.IsDirty, Is.False);
            store.Capture("route", Document, "routes/0", "{unfinished");
            store.Begin("route", "another document", "routes/1", "replacement");
            var restored = InspectorDraftStore.Parse(store.ToJson());
            Assert.That(restored.TryRead("route", out var raw), Is.True);
            Assert.That(raw, Is.EqualTo("{unfinished"));
            Assert.That(restored.IsDirty, Is.True);
            restored.RequireCurrent("route", Document, "routes/0");
            Assert.Throws<InvalidOperationException>(() => restored.RequireCurrent("route", Document, "routes/1"));
            Assert.Throws<InvalidOperationException>(() => restored.RequireCurrent("route", "changed", "routes/0"));
            Assert.Throws<InvalidOperationException>(() => restored.Capture("route", "changed", "routes/0", "lost"));
            restored.TryRead("route", out raw);
            Assert.That(raw, Is.EqualTo("{unfinished"));
        }

        [Test]
        public void UpdateRetainsStaleDraftAndRevertingTextClearsDirty()
        {
            var store = new InspectorDraftStore();
            Assert.Throws<InvalidOperationException>(() => store.Update("missing", "x"));
            store.Begin("a", Document, "target", "initial");
            store.Update("a", "changed");
            store.Begin("b", Document, "other", "second");
            store.Update("a", "initial");
            Assert.That(store.IsDirty, Is.False);
            store.Update("b", "bad json");
            store.Remove("a");
            Assert.That(store.IsDirty, Is.True);
            store.Clear();
            Assert.That(store.IsDirty, Is.False);
            Assert.That(store.TryRead("b", out _), Is.False);
        }

        [TestCase("{\"version\":1,\"version\":1,\"slots\":[]}")]
        [TestCase("{\"version\":2,\"slots\":[]}")]
        [TestCase("{\"version\":1,\"slots\":[],\"future\":true}")]
        [TestCase("{version:1,slots:[]}")]
        [TestCase("{\"version\":1,\"slots\":[]} true")]
        public void CorruptEnvelopesAreRejected(string json)
        {
            Assert.Catch<FormatException>(() => InspectorDraftStore.Parse(json));
        }

        [Test]
        public void DuplicateSlotsInvalidHashesAndOversizeWritesAreRejectedAtomically()
        {
            var store = new InspectorDraftStore();
            store.Begin("a", Document, "target", "initial");
            var root = Newtonsoft.Json.Linq.JObject.Parse(store.ToJson());
            var slots = (Newtonsoft.Json.Linq.JArray)root["slots"];
            slots.Add(slots[0].DeepClone());
            Assert.Catch<FormatException>(() => InspectorDraftStore.Parse(root.ToString()));
            slots.RemoveAt(1); slots[0]["documentHash"] = "invalid";
            Assert.Catch<FormatException>(() => InspectorDraftStore.Parse(root.ToString()));
            var before = store.ToJson();
            Assert.Catch<FormatException>(() => store.Update("a", new string('x', InspectorDraftStore.MaximumBytes + 1)));
            Assert.That(store.ToJson(), Is.EqualTo(before));
        }
    }
}
