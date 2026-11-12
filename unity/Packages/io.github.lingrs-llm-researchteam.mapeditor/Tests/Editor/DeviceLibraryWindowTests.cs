#nullable enable
using System;
using System.Collections;
using System.Reflection;
using System.Linq;
using Arknights.MapEditor.Catalog;
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
    public sealed class DeviceLibraryWindowTests
    {
        private MapEditorWindow window = null!;
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private sealed class Dialogs : IEditorFileDialogs
        {
            public string Open(string title, string directory, string extension) => "";
            public string Save(string title, string directory, string name, string extension) => "";
            public bool Discard(string title, string message) => false;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            window = ScriptableObject.CreateInstance<MapEditorWindow>();
            window.Dialogs = new Dialogs();
            window.Show();
            yield return null;
            window.CreateGUI();
        }

        [TearDown]
        public void TearDown()
        {
            typeof(MapEditorWindow).GetMethod("ClearInspectorDrafts", Private)!.Invoke(window, null);
            window.DiscardChanges();
            UnityEngine.Object.DestroyImmediate(window);
        }

        private Func<string, JObject, bool> Choose =>
            (Func<string, JObject, bool>)typeof(MapEditorWindow).GetField("prepareDevicePreset", Private)!.GetValue(window);

        private static void Click(Button button) => typeof(Clickable).GetMethod("Invoke", Private)!.Invoke(button.clickable, new object?[] { null });


        [Test]
        public void PresetCardsPageAndShareSelectionWithTheList()
        {
            var session = (DocumentSession)typeof(MapEditorWindow).GetField("session", Private)!.GetValue(window);
            var devices = new JArray(Enumerable.Range(0, 15).Select(row => new JObject
            {
                ["inst"] = new JObject { ["characterKey"] = "trap_001_crate" },
                ["position"] = new JObject { ["row"] = row, ["col"] = 1 },
                ["direction"] = "UP"
            }));
            var document = LevelDocument.Create(3, 15).WithSection("predefines",
                new JObject { ["tokenInsts"] = devices }.ToString());
            session.Load(document.ToJson(), "", "");
            window.CreateGUI();
            var root = window.rootVisualElement;
            Click(root.Q<Button>("workbench-tab-5"));
            Click(root.Q<Button>("preset-kind-devices"));
            Click(root.Q<Button>("preset-show-cards"));
            Assert.That(root.Q<Button>("preset-record-card-0").ClassListContains("selected-tab"), Is.True);
            Assert.That(root.Q<Button>("preset-record-card-0").Q<Image>("preset-preview-icon").image, Is.Not.Null);
            Assert.That(root.Q<Button>("preset-record-card-0").Q<Label>("preset-preview-detail").text, Is.EqualTo("(0, 1) · 朝上"));
            Click(root.Q<Button>("preset-next-page"));
            Assert.That(root.Q<Button>("preset-record-card-0"), Is.Null);
            Click(root.Q<Button>("preset-record-card-13"));
            Assert.That(root.Q<Button>("preset-record-card-13").ClassListContains("selected-tab"), Is.True);
            Assert.That(window.SelectedPresetCell, Is.EqualTo(new Vector2Int(1, 13)));
            Click(root.Q<Button>("preset-show-list"));
            Assert.That(root.Q<ListView>("preset-records").selectedIndex, Is.EqualTo(13));
            Assert.That(session.Json, Is.EqualTo(document.ToJson()));
        }

        [Test]
        public void MapPlacementBindsCoordinatesAndDirectionAndCanRepeatWithoutPaintingTerrain()
        {
            var session = (DocumentSession)typeof(MapEditorWindow).GetField("session", Private)!.GetValue(window);
            var terrain = session.Document.GetSection("mapData")!.ToString();
            var raw = JObject.Parse("{\"inst\":{\"characterKey\":\"trap_001_crate\"},\"hidden\":false,\"alias\":null,\"future\":[2,1]}");
            Assert.That(Choose("tokenInsts", raw), Is.True);
            window.rootVisualElement.Q<PopupField<string>>("device-placement-direction").value = "LEFT";
            window.PaintWorkspaceCells(new[] { new Vector2Int(2, 1) });
            window.PaintWorkspaceCells(new[] { new Vector2Int(3, 2) });
            var records = (JArray)session.Document.GetSection("predefines")!["tokenInsts"]!;
            Assert.That(records.Count, Is.EqualTo(2));
            Assert.That(records[0]["position"]!["row"]!.Value<int>(), Is.EqualTo(1));
            Assert.That(records[0]["position"]!["col"]!.Value<int>(), Is.EqualTo(2));
            Assert.That(records[1]["direction"]!.Value<string>(), Is.EqualTo("LEFT"));
            Assert.That(session.Document.GetSection("mapData")!.ToString(), Is.EqualTo(terrain));
            Assert.That(raw.Property("position"), Is.Null);
            var before = session.Json;
            window.SetWorkspaceEditLock(true);
            Assert.Throws<InvalidOperationException>(() => window.PaintWorkspaceCells(new[] { new Vector2Int(1, 1) }));
            Assert.That(session.Json, Is.EqualTo(before));
        }

        [Test]
        public void EraseCancelsDevicePlacementAndCardsNeverEnableIt()
        {
            Assert.That(Choose("tokenInsts", JObject.Parse("{\"inst\":{\"characterKey\":\"trap_001_crate\"}}")), Is.True);
            Assert.That(window.rootVisualElement.Q<Toggle>("device-placement-enabled").value, Is.True);
            window.SetEraseMode(true);
            Assert.That(window.rootVisualElement.Q<Toggle>("device-placement-enabled").value, Is.False);
        }

        [Test]
        public void RecommendationsKeepOriginsExpandedAndSourceRowsCanBePreviewed()
        {
            var overlay = window.rootVisualElement.Q<DeviceDetailOverlay>();
            var entry = DeviceSampleCatalog.LoadBundled().Entries.Single(x => x.Id == "trap_001_crate");
            overlay.Open(entry, new Button());
            var card = overlay.Q("device-role-tokenCards");
            var current = card.Q<TerrainSourceBrowser>("device-current-source-browser");
            var total = card.Q<TerrainSourceBrowser>("device-total-source-browser");
            current.value = true;
            total.value = true;
            card.Q<Foldout>("device-known-configurations").value = true;
            Click(card.Q<Button>("device-recommendation-1"));
            Assert.That(current.value, Is.True);
            Assert.That(total.value, Is.True);
            Assert.That(card.Q<Foldout>("device-known-configurations").value, Is.True);
            var list = current.Q<ListView>("terrain-source-list");
            Assert.That(list.itemsSource.Count, Is.GreaterThan(0));
            list.SetSelection(0);
            Assert.That(current.Q("terrain-source-preview-slot").childCount, Is.EqualTo(1));
            Click(overlay.Q<Button>("device-role-tab-tokenCards"));
            Click(card.Q<Button>("device-use-configuration"));
            Assert.That(window.rootVisualElement.Q<PopupField<string>>("device-edit-role").value, Is.EqualTo("tokenCards"));
        }

        [Test]
        public void ExplicitInsertAddsTheSelectedCardWithoutChangingLibraryData()
        {
            var original = DeviceSampleCatalog.LoadBundled().Load("trap_001_crate").Samples("tokenCards")[0];
            var raw = original.CopyRecord();
            Assert.That(Choose("tokenCards", raw), Is.True);
            Assert.That(window.rootVisualElement.Q<Toggle>("device-placement-enabled").value, Is.False);
            Assert.That(JToken.DeepEquals(JObject.Parse(window.rootVisualElement.Q<TextField>("device-edit-json").value), raw), Is.True);
            var insert = window.rootVisualElement.Query<Button>().ToList().Single(button => button.text == "插入预设");
            Click(insert);
            var session = (DocumentSession)typeof(MapEditorWindow).GetField("session", Private)!.GetValue(window);
            var cards = (JArray)session.Document.GetSection("predefines")!["tokenCards"]!;
            Assert.That(cards.Count, Is.EqualTo(1));
            // The document codec stores number lexemes as JRaw. Compare exported JSON,
            // rather than requiring the catalog's JValue implementation type.
            Assert.That(cards[0]!.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo(raw.ToString(Newtonsoft.Json.Formatting.None)));
            Assert.That(JToken.DeepEquals(original.CopyRecord(), raw), Is.True);
        }

        [Test]
        public void SelectionStagesACompleteCopyAndSurvivesGuiRebuildWithoutEditingMap()
        {
            var session = (DocumentSession)typeof(MapEditorWindow).GetField("session", Private)!.GetValue(window);
            var before = session.Json;
            var raw = JObject.Parse("{\"inst\":{\"characterKey\":\"trap_001_crate\"},\"initialCnt\":17,\"future\":[2,1]}");
            Assert.That(Choose("tokenCards", raw), Is.True);
            Assert.That(window.rootVisualElement.Q<Toggle>("device-placement-enabled").value, Is.False);
            Assert.That(session.Json, Is.EqualTo(before));
            Assert.That((bool)typeof(MapEditorWindow).GetProperty("InspectorDirty", Private)!.GetValue(window), Is.True);
            window.CreateGUI();
            Assert.That(window.rootVisualElement.Q<PopupField<string>>("device-edit-role").value, Is.EqualTo("tokenCards"));
            var field = window.rootVisualElement.Q<TextField>("device-edit-json");
            Assert.That(JToken.DeepEquals(JObject.Parse(field.value), raw), Is.True);
            var target = new JArray("predefines", "tokenCards", 0).ToString(Newtonsoft.Json.Formatting.None);
            Assert.DoesNotThrow(() => typeof(MapEditorWindow).GetMethod("RequireLocalDraft", Private)!.Invoke(window, new object[] { "preset", target }));
        }

        [Test]
        public void CancelKeepsTheExistingLocalDraftAndDestination()
        {
            var field = window.rootVisualElement.Q<TextField>("device-edit-json");
            field.value = "unfinished existing draft";
            Assert.That(Choose("tokenCards", JObject.Parse("{\"inst\":{\"characterKey\":\"trap_001_crate\"}}")), Is.False);
            Assert.That(field.value, Is.EqualTo("unfinished existing draft"));
            Assert.That(window.rootVisualElement.Q<PopupField<string>>("device-edit-role").value, Is.EqualTo("characterInsts"));
        }
    }

}
