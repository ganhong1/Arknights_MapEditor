using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using Arknights.MapEditor.Storage;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Tests
{
    public sealed class MapDataWindowTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private MapEditorWindow window;
        private sealed class Dialogs : IEditorFileDialogs
        {
            public string Open(string title, string directory, string extension) => "";
            public string Save(string title, string directory, string name, string extension) => "";
            public bool Discard(string title, string message) => false;
        }
        private DocumentSession Session => (DocumentSession)typeof(MapEditorWindow).GetField("session", Flags).GetValue(window);
        private object Call(string name, params object[] args) => typeof(MapEditorWindow).GetMethod(name, Flags).Invoke(window, args);
        private TextField Field(string key) => window.rootVisualElement.Q<TextField>("mapdata-json-" + key);
        private void Select(string key) => ((Action<string>)typeof(MapEditorWindow).GetField("selectMapDataField", Flags).GetValue(window))(key);

        [UnitySetUp] public IEnumerator SetUp()
        {
            window = ScriptableObject.CreateInstance<MapEditorWindow>(); window.Dialogs = new Dialogs(); window.Show(); yield return null;
            Session.UseLocalHistory(); Session.Load(LevelDocument.Create(3, 3).WithSection("options", "{\"maxLifePoint\":7,\"future\":true}").ToJson(), "", "");
            typeof(MapEditorWindow).GetField("draft", Flags).SetValue(window, Session.Json);
            Call("ClearInspectorDrafts"); window.CreateGUI();
        }

        [TearDown] public void TearDown()
        {
            if (window == null) return;
            Call("ClearInspectorDrafts"); window.DiscardChanges(); window.Close();
        }

        [Test] public void MapDataKeepsSixNativeFieldsAndOneIndependentTileDataEditor()
        {
            var expected = new[] { "map", "tiles", "blockEdges", "tilesDisallowToLocate", "tags", "effects", "layerRects", "root" };
            var actual = window.rootVisualElement.Q("mapdata-editor").Query<Button>().ToList()
                .Where(b => b.name != null && b.name.StartsWith("mapdata-tab-", StringComparison.Ordinal))
                .Select(b => b.name.Substring("mapdata-tab-".Length)).ToArray();
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(window.rootVisualElement.Query<VisualElement>("tiledata-preview").ToList().Count, Is.EqualTo(1));
            Assert.That(window.rootVisualElement.Q("mapdata-page-tiles").Q("tiledata-preview"), Is.Not.Null);
            Assert.That(window.rootVisualElement.Q("workbench-page-10").Q("mapdata-width"), Is.Null);
            Assert.That(window.rootVisualElement.Q("mapdata-page-map").Q("mapdata-width"), Is.Not.Null);
        }

        [Test] public void TileDataDraftSurvivesMapNavigationAndGuiRebuild()
        {
            Select("tiles");
            window.rootVisualElement.Q<TextField>("tiledata-preview-json").value = "{unfinished tile";
            var before = (string)Call("CaptureInspectorRecovery");
            Select("map"); Select("effects"); Select("tiles"); window.CreateGUI();
            Assert.That(window.rootVisualElement.Q<TextField>("tiledata-preview-json").value, Is.EqualTo("{unfinished tile"));
            Assert.That((string)Call("CaptureInspectorRecovery"), Is.EqualTo(before));
            var original = Session.Json;
            Assert.Throws<TargetInvocationException>(() => Call("ApplyMapDimensions", 4, 4));
            Assert.That(Session.Json, Is.EqualTo(original));
        }

        [Test] public void DimensionsCommitOnlyMapDataAndUndoInOneStep()
        {
            Select("map");
            var original = Session.Json;
            var options = Session.Document.GetSection("options").ToString();
            window.rootVisualElement.Q<IntegerField>("mapdata-width").value = 4;
            Call("ApplyMapDimensions", 4, 3);
            Assert.That(Session.Document.Width, Is.EqualTo(4));
            Assert.That(Session.Document.GetSection("options").ToString(), Is.EqualTo(options));
            Assert.That(Session.Document.GetSection("mapData")["width"], Is.Null);
            Session.StepHistory(false);
            Assert.That(Session.Json, Is.EqualTo(original));
        }

        [Test] public void OptionalFieldDraftDistinguishesMissingNullEmptyAndPreservesOtherSections()
        {
            var originalOptions = Session.Document.GetSection("options").ToString();
            Assert.That(Field("tags").value, Is.EqualTo(""));
            Field("tags").value = "null"; Call("ApplyMapDataDraft", "tags", Field("tags").value);
            Assert.That(Session.Document.GetSection("mapData")["tags"].Type, Is.EqualTo(JTokenType.Null));
            Field("tags").value = "[]"; Call("ApplyMapDataDraft", "tags", Field("tags").value);
            Assert.That(((JArray)Session.Document.GetSection("mapData")["tags"]).Count, Is.Zero);
            Field("tags").value = ""; Call("ApplyMapDataDraft", "tags", Field("tags").value);
            Assert.That(Session.Document.GetSection("mapData")["tags"], Is.Null);
            Assert.That(Session.Document.GetSection("options").ToString(), Is.EqualTo(originalOptions));
        }

        [Test] public void InvalidMapDataDraftSurvivesRebuildAndCannotOverwriteAnotherVersion()
        {
            Field("root").value = "{unfinished map";
            var before = (string)Call("CaptureInspectorRecovery");
            window.CreateGUI();
            Assert.That(Field("root").value, Is.EqualTo("{unfinished map"));
            Assert.That((string)Call("CaptureInspectorRecovery"), Is.EqualTo(before));
            Session.Edit(Session.Document.WithMapDataField("tags", "[\"newer\"]"), "another edit");
            typeof(MapEditorWindow).GetField("draft", Flags).SetValue(window, Session.Json);
            Call("Refresh");
            var current = Session.Json;
            Field("root").value = Session.Document.GetSection("mapData").ToString();
            Assert.Throws<TargetInvocationException>(() => Call("ApplyMapDataDraft", "root", Field("root").value));
            Assert.That(Session.Json, Is.EqualTo(current));
        }

        [Test] public void UnrelatedEditAndUndoKeepMapConfigDraftOnTheSameField()
        {
            const string pending = "[\"keep-me\"]";
            Field("tags").value = pending;
            Call("CommitEdit", Session.Document.WithLevelId("different-module"), "编辑 levelId", null);
            Call("StepEditorHistoryCore", false);
            Call("StepEditorHistoryCore", true);
            Assert.That(Field("tags").value, Is.EqualTo(pending));
            Call("ApplyMapDataDraft", "tags", pending);
            Assert.That(Session.Document.GetSection("mapData")["tags"][0].ToString(), Is.EqualTo("keep-me"));
            Assert.That(Session.Document.LevelId, Is.EqualTo("different-module"));
        }

        [Test] public void RawMapCannotBypassCropCheckForExternalRoute()
        {
            Session.Edit(Session.Document.WithSection("routes", "[{\"startPosition\":{\"row\":2,\"col\":2}}]"), "route");
            typeof(MapEditorWindow).GetField("draft", Flags).SetValue(window, Session.Json); Call("Refresh");
            Field("map").value = "[[0]]";
            var before = Session.Json;
            Assert.Throws<TargetInvocationException>(() => Call("ApplyMapDataDraft", "map", Field("map").value));
            Assert.That(Session.Json, Is.EqualTo(before));
            Assert.That(Field("map").value, Is.EqualTo("[[0]]"));
        }

        [Test] public void EditLockKeepsDraftAndDocumentUnchanged()
        {
            var before = Session.Json;
            Call("SetWorkspaceEditLock", true);
            Field("tags").value = "[\"locked\"]";
            Assert.That(Field("tags").value, Is.EqualTo(""));
            Assert.Throws<TargetInvocationException>(() => Call("ApplyMapDimensions", 5, 5));
            Assert.That(Session.Json, Is.EqualTo(before));
        }

        [Test] public void ClearingInspectorDraftsForNewDocumentDoesNotParseEmptyDimensions()
        {
            window.rootVisualElement.Q<IntegerField>("mapdata-width").value = 8;
            Assert.DoesNotThrow(() => Call("ClearInspectorDrafts"));
            Assert.That(window.rootVisualElement.Q<IntegerField>("mapdata-width").value, Is.EqualTo(3));
            Assert.That(Session.Document.Width, Is.EqualTo(3));
        }
    }
}
