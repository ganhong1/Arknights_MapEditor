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
    public sealed class MapRuleFormsTests
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
        private TextField Raw => window.rootVisualElement.Q<TextField>("maprules-json-blockEdges");
        private VisualElement List => window.rootVisualElement.Q("maprules-list-blockEdges");

        [UnitySetUp] public IEnumerator SetUp()
        {
            window = ScriptableObject.CreateInstance<MapEditorWindow>(); window.Dialogs = new Dialogs(); window.Show(); yield return null;
            Session.UseLocalHistory();
            Session.Load(LevelDocument.Create(4, 3).AddMapRule("blockEdges", "{\"pos\":{\"row\":1,\"col\":2,\"future\":true},\"direction\":0,\"blockMask\":1,\"extension\":1.234567890123456789,\"date\":\"2026-11-14T00:00:00Z\"}").ToJson(), "", "");
            typeof(MapEditorWindow).GetField("draft", Flags).SetValue(window, Session.Json);
            Call("ClearInspectorDrafts"); window.CreateGUI();
            ((Action<string>)typeof(MapEditorWindow).GetField("selectMapDataField", Flags).GetValue(window))("blockEdges");
        }
        [TearDown] public void TearDown()
        {
            if (window == null) return;
            Call("ClearInspectorDrafts"); window.DiscardChanges(); window.Close();
        }

        [Test] public void CoordinateEditRetainsUnknownDataAndOnlyUpdatesDraft()
        {
            var before = Session.Json;
            List.Query<IntegerField>().ToList().First(input => input.label == "列 col").value = 3;
            Assert.That(Session.Json, Is.EqualTo(before));
            StringAssert.Contains("1.234567890123456789", Raw.value);
            StringAssert.Contains("2026-11-14T00:00:00Z", Raw.value);
            var row = JArray.Parse(Raw.value)[0];
            Assert.That(row["pos"]["future"].Value<bool>(), Is.True);
            Assert.That(row["direction"].Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(row["pos"]["col"].Value<int>(), Is.EqualTo(3));
        }

        [Test] public void EnumSelectionKeepsImportedNumericRepresentation()
        {
            List.Query<PopupField<string>>().ToList().First(input => input.label == "方向").value = "LEFT";
            Assert.That(JArray.Parse(Raw.value)[0]["direction"].Value<int>(), Is.EqualTo(3));
            Assert.That(Session.Document.GetMapRules("blockEdges")[0]["direction"].Value<int>(), Is.Zero);
        }

        private static void Click(VisualElement parent, string text)
        {
            var button = parent.Query<Button>().ToList().First(item => item.text == text);
            typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(button.clickable, new object[] { null });
        }

        [Test] public void RootDisallowApplyAndUndoLeaveTileDataUnchanged()
        {
            ((Action<string>)typeof(MapEditorWindow).GetField("selectMapDataField", Flags).GetValue(window))("tilesDisallowToLocate");
            var page = window.rootVisualElement.Q("mapdata-page-tilesDisallowToLocate");
            var before = Session.Json;
            var map = Session.Document.GetSection("mapData");
            page.Q<TextField>("maprules-json-tilesDisallowToLocate").value = "[{\"row\":1,\"col\":2}]";
            Assert.That(Session.Json, Is.EqualTo(before));
            Click(page, "应用到地图");
            Assert.That(Session.Document.GetSection("tilesDisallowToLocate")[0]["col"].Value<int>(), Is.EqualTo(2));
            Assert.That(Session.Document.GetSection("mapData")["tilesDisallowToLocate"], Is.Null);
            Assert.That(JToken.DeepEquals(map, Session.Document.GetSection("mapData")), Is.True);
            Session.StepHistory(false);
            Assert.That(Session.Json, Is.EqualTo(before));
        }
        [Test] public void RawArrayReplacementDisablesStaleCardsUntilRebuilt()
        {
            Raw.value = "[]";
            Assert.That(!List.enabledSelf || List.childCount == 0, Is.True, "旧索引卡片需禁用或重新构建");
            Assert.That(((JArray)Session.Document.GetMapRules("blockEdges")).Count, Is.EqualTo(1));
        }
    }
}
