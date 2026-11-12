using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using Arknights.MapEditor.Storage;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;

namespace Arknights.MapEditor.Tests
{
    public sealed class InspectorDraftWindowTests
    {
        private MapEditorWindow window;
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private sealed class Dialogs : IEditorFileDialogs
        {
            internal bool Allow;
            internal int Questions;
            internal string OpenPath = "";
            public string Open(string title, string directory, string extension) => OpenPath;
            public string Save(string title, string directory, string name, string extension) => "";
            public bool Discard(string title, string message) { Questions++; return Allow; }
        }
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            window = ScriptableObject.CreateInstance<MapEditorWindow>(); window.Dialogs = new Dialogs();
            window.Show(); yield return null; window.CreateGUI();
        }
        [TearDown]
        public void TearDown()
        {
            if (window == null) return;
            Invoke("ClearInspectorDrafts"); window.DiscardChanges();
            UnityEngine.Object.DestroyImmediate(window);
        }
        private object Invoke(string name, params object[] arguments) => typeof(MapEditorWindow).GetMethod(name, Private).Invoke(window, arguments);
        private DocumentSession Session => (DocumentSession)typeof(MapEditorWindow).GetField("session", Private).GetValue(window);
        private TextField Section => window.rootVisualElement.Query<TextField>().ToList().Single(f => f.label == "字段 JSON（保留未知内容）");

        [Test]
        public void SectionDraftSurvivesGuiRebuildAndCannotApplyToAnotherDocumentOrTarget()
        {
            Section.value = "{unfinished";
            var recovery = (string)Invoke("CaptureInspectorRecovery");
            window.CreateGUI();
            Assert.That(Section.value, Is.EqualTo("{unfinished"));
            var targetError = Assert.Throws<TargetInvocationException>(() => Invoke("RequireLocalDraft", "section", "waves"));
            Assert.That(targetError.InnerException, Is.TypeOf<InvalidOperationException>());
            Session.Edit(Session.Document.WithLevelId("other"), "changed document");
            typeof(MapEditorWindow).GetField("draft", Private).SetValue(window, Session.Json);
            var docError = Assert.Throws<TargetInvocationException>(() => Invoke("RequireLocalDraft", "section", "routes"));
            Assert.That(docError.InnerException, Is.TypeOf<InvalidOperationException>());
            Invoke("ApplyInspectorRecovery", InspectorDraftStore.Parse(recovery));
            window.CreateGUI();
            Assert.That(Section.value, Is.EqualTo("{unfinished"));
        }

        [Test]
        public void ExplicitReloadRequiresConfirmationAndSuccessfulApplyRemovesSlot()
        {
            Section.value = "broken";
            var dialogs = (Dialogs)window.Dialogs;
            Assert.That(Invoke("ReloadLocalDraft", "section", "routes", "[]"), Is.False);
            Assert.That(Section.value, Is.EqualTo("broken"));
            dialogs.Allow = true;
            Assert.That(Invoke("ReloadLocalDraft", "section", "routes", "[]"), Is.True);
            Assert.That(Section.value, Is.EqualTo("[]"));
            Assert.That(dialogs.Questions, Is.EqualTo(2));
            Invoke("RequireLocalDraft", "section", "routes");
            Invoke("AppliedLocalDraft", "section");
            Assert.That(InspectorDraftStore.Parse((string)Invoke("CaptureInspectorRecovery")).TryRead("section", out _), Is.False);
        }

        [Test]
        public void OversizeVisibleEditIsRolledBackToPreservedDraft()
        {
            Section.value = "kept";
            Section.value = new string('x', InspectorDraftStore.MaximumBytes + 1);
            Assert.That(Section.value, Is.EqualTo("kept"));
            var recovery = InspectorDraftStore.Parse((string)Invoke("CaptureInspectorRecovery"));
            recovery.TryRead("section", out var raw);
            Assert.That(raw, Is.EqualTo("kept"));
        }

        [TestCase("{broken")]
        [TestCase("{\"startRow\":0,\"startCol\":0,\"endRow\":1,\"endCol\":1e100,\"checkpoints\":\"[]\"}")]
        [TestCase("{\"startRow\":0,\"startCol\":0,\"endRow\":1,\"endCol\":1,\"checkpoints\":[]}")]
        public void RestoreRejectsMalformedCoordinateDraftBeforeReplacingSession(string raw)
        {
            Section.value = "original draft";
            var oldJson = Session.Json;
            var oldRecovery = (string)Invoke("CaptureInspectorRecovery");
            var changed = LevelDocument.Create(3, 3).ToJson();
            var store = new InspectorDraftStore(); store.Begin("routePoints", changed, "target", raw);
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/InspectorDraftTests-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(directory);
            try
            {
                var path = Path.Combine(directory, "invalid.json");
                RecoveryStore.Save(path, new RecoverySnapshot(changed, changed, changed, "", "", "", store.ToJson()), null);
                var dialogs = (Dialogs)window.Dialogs; dialogs.Allow = true; dialogs.OpenPath = path;
                var error = Assert.Throws<TargetInvocationException>(() => Invoke("RestoreRecovery"));
                Assert.That(error.InnerException, Is.InstanceOf<FormatException>());
                Assert.That(Session.Json, Is.EqualTo(oldJson));
                Assert.That(Section.value, Is.EqualTo("original draft"));
                Assert.That(Invoke("CaptureInspectorRecovery"), Is.EqualTo(oldRecovery));
            }
            finally { File.Delete(Path.Combine(directory, "invalid.json")); Directory.Delete(directory); }
        }

        [Test]
        public void InvalidInitialCoordinateEnvelopeIsRejectedEvenWhenCurrentRawIsValid()
        {
            var before = (string)Invoke("CaptureInspectorRecovery");
            var store = new InspectorDraftStore(); store.Begin("routePoints", Session.Json, "target", "broken initial");
            store.Update("routePoints", "{\"startRow\":0,\"startCol\":0,\"endRow\":1,\"endCol\":1,\"checkpoints\":\"{unfinished\"}");
            var error = Assert.Throws<TargetInvocationException>(() => Invoke("ApplyInspectorRecovery", store));
            Assert.That(error.InnerException, Is.InstanceOf<FormatException>());
            Assert.That(Invoke("CaptureInspectorRecovery"), Is.EqualTo(before));
        }

        [Test]
        public void InitialCoordinateControlsUseExistingRoutePositions()
        {
            var document = Session.Document.WithSection("routes", "[{\"startPosition\":{\"row\":2,\"col\":3},\"endPosition\":{\"row\":4,\"col\":5},\"checkpoints\":[]}]");
            Session.Load(document.ToJson(), "", "");
            typeof(MapEditorWindow).GetField("draft", Private).SetValue(window, Session.Json);
            Invoke("ClearInspectorDrafts"); window.CreateGUI();
            var store = InspectorDraftStore.Parse((string)Invoke("CaptureInspectorRecovery"));
            store.TryRead("routePoints", out var raw);
            var points = Newtonsoft.Json.Linq.JObject.Parse(raw);
            Assert.That((float)points["startRow"], Is.EqualTo(2));
            Assert.That((float)points["endCol"], Is.EqualTo(5));
        }
    }
}
