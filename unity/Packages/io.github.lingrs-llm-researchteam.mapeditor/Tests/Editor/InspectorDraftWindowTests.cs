using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using Arknights.MapEditor.Storage;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.UIElements;
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

        [Test]
        public void ChangingAdvancedFieldCannotEditAnotherFieldsDraft()
        {
            var fields = window.rootVisualElement.Query<PopupField<string>>().ToList().Single(f => f.label == "字段");
            Section.value = "[{\"originalRouteDraft\":true}]";
            fields.value = "options";
            Assert.That(fields.value, Is.EqualTo("routes"));
            Assert.That(Section.value, Is.EqualTo("[{\"originalRouteDraft\":true}]"));
            ((Dialogs)window.Dialogs).Allow = true;
            fields.value = "options";
            Assert.That(Section.value, Does.Contain("characterLimit"));
            Assert.DoesNotThrow(() => Invoke("RequireLocalDraft", "section", "options"));
        }

        [Test]
        public void PresetRecoveryRestoresTheDraftLayerCollectionAndIndex()
        {
            const string target = "[\"hardPredefines\",\"tokenCards\",0]";
            const string raw = "{\"inst\":{\"characterKey\":\"trap_007_ballis\"},\"initialCnt\":4}";
            var store = new InspectorDraftStore();
            store.Begin("preset", Session.Json, target, "{}");
            store.Update("preset", raw);
            Invoke("ApplyInspectorRecovery", store);
            window.CreateGUI();
            Assert.That(window.rootVisualElement.Q<PopupField<string>>("device-edit-section").value, Is.EqualTo("hardPredefines"));
            Assert.That(window.rootVisualElement.Q<PopupField<string>>("device-edit-role").value, Is.EqualTo("tokenCards"));
            Assert.That(window.rootVisualElement.Q<TextField>("device-edit-json").value, Is.EqualTo(raw));
            Assert.That(Session.Document.GetSection("hardPredefines"), Is.Null);
        }

        [Test]
        public void RouteSelectionSurvivesRouteOrderUndoRedo()
        {
            var original = Session.Document.WithSection("routes",
                "[{\"startPosition\":{\"row\":0,\"col\":0},\"id\":\"first\"},{\"startPosition\":{\"row\":0,\"col\":1},\"id\":\"second\"}]");
            var type = typeof(MapEditorWindow);
            var selected = type.GetField("routeListSelection", Private);
            Session.Load(original.ToJson(), "", "");
            type.GetField("draft", Private).SetValue(window, Session.Json);
            selected.SetValue(window, 0);
            Invoke("ChangeRouteOrder", 1);
            Assert.That(selected.GetValue(window), Is.EqualTo(1));
            Invoke("StepEditorHistoryCore", false);
            Assert.That(selected.GetValue(window), Is.EqualTo(0));
            Invoke("StepEditorHistoryCore", true);
            Assert.That(selected.GetValue(window), Is.EqualTo(1));
        }

        [Test]
        public void OpeningRouteThenEditingUnrelatedFieldKeepsRouteEditable()
        {
            var route = "{\"motionMode\":\"WALK\",\"startPosition\":{\"row\":0,\"col\":0},\"endPosition\":{\"row\":0,\"col\":1},\"checkpoints\":[]}";
            Invoke("CommitEdit", Session.Document.WithSection("routes", "[" + route + "]"), "setup", null);
            window.BeginRouteDraft("routes", 0, "WALK");
            Invoke("CommitEdit", Session.Document.WithLevelId("different"), "edit metadata", null);
            window.MoveRouteNode(-2, new Vector2Int(2, 0));
            window.ApplyRouteDraft();
            Assert.That(Session.Document.GetSection("routes")[0]["endPosition"]["col"].Value<int>(), Is.EqualTo(2));
            Assert.That(Session.Document.LevelId, Is.EqualTo("different"));
        }

        [Test]
        public void StandaloneUndoUsesOnlyTheInvokingMapHistory()
        {
            var other = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                var otherSession = (DocumentSession)typeof(MapEditorWindow).GetField("session", Private).GetValue(other);
                var first = Session.Json;
                Invoke("CommitEdit", Session.Document.WithLevelId("first"), "first edit", null);
                typeof(MapEditorWindow).GetMethod("CommitEdit", Private).Invoke(other,
                    new object[] { otherSession.Document.WithLevelId("second"), "second edit", null });
                Invoke("StepEditorHistoryCore", false);
                Assert.That(Session.Json, Is.EqualTo(first));
                Assert.That(otherSession.Document.LevelId, Is.EqualTo("second"));
            }
            finally { UnityEngine.Object.DestroyImmediate(other); }
        }
    }
}
