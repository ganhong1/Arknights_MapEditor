using System;
using System.Collections;
using System.Reflection;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Tests
{
    public sealed class TileDataLiveWindowTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private MapEditorWindow window;
        private DocumentSession Session => (DocumentSession)typeof(MapEditorWindow).GetField("session", Flags).GetValue(window);
        private object Call(string method, params object[] args) => typeof(MapEditorWindow).GetMethod(method, Flags).Invoke(window, args);
        [UnitySetUp] public IEnumerator SetUp()
        {
            window = ScriptableObject.CreateInstance<MapEditorWindow>(); window.Show(); yield return null;
            Session.UseLocalHistory(); Session.Load(LevelDocument.Create(1, 1).ToJson(), "", "");
            typeof(MapEditorWindow).GetField("draft", Flags).SetValue(window, Session.Json);
            typeof(MapEditorWindow).GetField("workbenchTab", Flags).SetValue(window, 6); window.CreateGUI();
        }
        [TearDown] public void TearDown() { window.DiscardChanges(); UnityEngine.Object.DestroyImmediate(window); }

        [Test]
        public void ThreeChoicesCancelKeepOrCleanupAndUndoRestoresBindings()
        {
            var original = Session.Json;
            var prompt = typeof(MapEditorWindow).GetProperty("TileReplacementPrompt", Flags);
            prompt.SetValue(window, new Func<int, int>(_ => 1));
            Call("PlaceManagedTiles", new Vector2Int[] { Vector2Int.zero });
            Assert.That(Session.Json, Is.EqualTo(original));
            prompt.SetValue(window, new Func<int, int>(_ => 2));
            Call("PlaceManagedTiles", new Vector2Int[] { Vector2Int.zero });
            Assert.That(((JArray)Session.Document.GetSection("mapData")["tiles"]).Count, Is.EqualTo(2));
            Session.StepHistory(false);
            typeof(MapEditorWindow).GetField("draft", Flags).SetValue(window, Session.Json);
            Assert.That(Session.Json, Is.EqualTo(original));
            prompt.SetValue(window, new Func<int, int>(_ => 0));
            Call("PlaceManagedTiles", new Vector2Int[] { Vector2Int.zero });
            Assert.That(((JArray)Session.Document.GetSection("mapData")["tiles"]).Count, Is.EqualTo(1));
            Assert.That(Session.Document.GetCell(0, 0)["tileKey"].Value<string>(), Is.EqualTo("tile_road"));
        }

        [Test]
        public void InvalidDraftSurvivesRebuildAndPreventsPlacement()
        {
            window.rootVisualElement.Q<TextField>("tiledata-preview-json").value = "{unfinished";
            var recovery = (string)Call("CaptureInspectorRecovery");
            window.CreateGUI();
            Assert.That(window.rootVisualElement.Q<TextField>("tiledata-preview-json").value, Is.EqualTo("{unfinished"));
            Assert.That((string)Call("CaptureInspectorRecovery"), Is.EqualTo(recovery));
            var original = Session.Json;
            Assert.Throws<TargetInvocationException>(() => Call("PlaceManagedTiles", new Vector2Int[] { Vector2Int.zero }));
            Assert.That(Session.Json, Is.EqualTo(original));
        }
    }
}
