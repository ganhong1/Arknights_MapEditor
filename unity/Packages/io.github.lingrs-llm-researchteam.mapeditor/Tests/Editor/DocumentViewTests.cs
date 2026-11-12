#nullable enable
using System.Linq;
using System.Collections;
using System.Reflection;
using Arknights.MapEditor.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using UnityEditor.UIElements;

namespace Arknights.MapEditor.Tests
{
    public sealed class DocumentViewTests
    {
        private MapEditorWindow window = null!;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [UnitySetUp] public IEnumerator SetUp()
        {
            window = ScriptableObject.CreateInstance<MapEditorWindow>();
            window.Show(); yield return null; window.CreateGUI();
        }
        [TearDown] public void TearDown()
        {
            typeof(MapEditorWindow).GetMethod("ClearInspectorDrafts", Private)!.Invoke(window, null);
            window.DiscardChanges(); window.Close();
        }
        private void Click(string name) => Invoke(window.rootVisualElement.Q<Button>(name));
        private static void Invoke(Button button) => typeof(Clickable).GetMethod("Invoke", Private)!.Invoke(button.clickable, new object?[] { null });

        [Test]
        public void GlobalFieldsShowNewOptionsDefaultsAndPreserveUnappliedDrafts()
        {
            var root = window.rootVisualElement;
            Assert.That(Newtonsoft.Json.Linq.JObject.Parse(root.Q<TextField>("leveldata-json").value)["options"]!.Count(), Is.EqualTo(15));
            Assert.That(root.Q<Foldout>("leveldata-node:/options"), Is.Not.Null);
            Assert.That(root.Q<IntegerField>("options-field-characterLimit-integer").value, Is.EqualTo(99));
            Assert.That(root.Q("workbench-page-10").Q("global-field-bgmEvent"), Is.Null);
            Assert.That(root.Q("workbench-page-10").Q("global-field-environmentSe"), Is.Null);
            Click("workbench-tab-11");
            Assert.That(root.Q("workbench-page-11").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(root.Q("workbench-page-11").Q("global-field-bgmEvent"), Is.Not.Null);
            Click("workbench-tab-12");
            Assert.That(root.Q("workbench-page-12").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(root.Q("workbench-page-12").Q("global-field-environmentSe"), Is.Not.Null);
            root.Q<TextField>("global-field-options").value = "{\"futureSetting\":42}";
            window.CreateGUI(); root = window.rootVisualElement;
            Assert.That(root.Q<TextField>("global-field-options").value, Does.Contain("futureSetting"));
            Click("options-apply");
            var applied = Newtonsoft.Json.Linq.JObject.Parse(root.Q<TextField>("leveldata-json").value);
            Assert.That((int)applied["options"]!["futureSetting"]!, Is.EqualTo(42));
            Assert.That(applied["bgmEvent"], Is.Null);
            Assert.That(applied["environmentSe"], Is.Null);
        }

        [Test]
        public void ModuleEntriesKeepPlaceholdersReadOnlyAndGlobalDimensionsSeparate()
        {
            var root = window.rootVisualElement;
            var before = root.Q<TextField>("leveldata-json").value;
            Assert.That(root.Q("workbench-tab-0"), Is.Null);
            Assert.That(root.Q<Button>("workbench-tab-6").text, Is.EqualTo("地图数据 · mapData"));
            var map = root.Q("mapdata-page-map");
            Assert.That(map.Q<IntegerField>("mapdata-width"), Is.Not.Null);
            Assert.That(map.Q<IntegerField>("mapdata-height"), Is.Not.Null);
            Click("global-settings");
            Assert.That(root.Q("workbench-page-10").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            var global = root.Q("workbench-page-10");
            Assert.That(global.Q("options-form"), Is.Not.Null);
            Assert.That(global.Q("mapdata-width"), Is.Null);
            Assert.That(global.Q("mapdata-height"), Is.Null);
            foreach (var id in new[] { 7, 8, 9 }) Click("workbench-tab-" + id);
            Assert.That(root.Q<Label>("field-summary-runes").text, Does.Contain("数组"));
            Assert.That(root.Q<Label>("field-summary-optionalRunes").text, Does.Contain("null"));
            Click("leveldata-json-mode");
            Click("locate-field-enemyDbRefs");
            Assert.That(root.Q("leveldata-tree").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(root.Q<Foldout>("leveldata-node:/enemyDbRefs").value, Is.True);
            Assert.That(root.Q<TextField>("leveldata-json").value, Is.EqualTo(before));
        }

        [Test]
        public void DrawersToggleIndependentlyWithoutReplacingUnappliedFields()
        {
            var root = window.rootVisualElement;
            var local = root.Q<TextField>("device-edit-json"); local.value = "unfinished";
            Click("editor-drawer-toggle");
            Assert.That(root.Q("editor-drawer").style.display.value, Is.EqualTo(DisplayStyle.Flex));
            var rightBefore = root.Q("resource-drawer").style.display.value;
            Click("editor-drawer-toggle");
            Assert.That(root.Q("resource-drawer").style.display.value, Is.EqualTo(rightBefore));
            Click("resource-drawer-toggle");
            Assert.That(root.Q("editor-drawer").style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(local.value, Is.EqualTo("unfinished"));
            Assert.That(root.Q("leveldata-document").parent, Is.SameAs(root.Q("workbench-body")));
            window.CreateGUI();
            Assert.That(window.rootVisualElement.Q<TextField>("device-edit-json").value, Is.EqualTo("unfinished"));
        }

        [Test]
        public void RawDraftDoesNotReplaceTreeUntilExplicitApplyAndUnknownFieldsAreShown()
        {
            var root = window.rootVisualElement;
            var field = root.Q<TextField>("leveldata-json");
            var doc = Newtonsoft.Json.Linq.JObject.Parse(field.value);
            doc["futureField"] = new Newtonsoft.Json.Linq.JArray(1, null, "x");
            doc["precise"] = new Newtonsoft.Json.Linq.JRaw("0.1234567890123456789");
            field.value = doc.ToString();
            Assert.That(root.Q("leveldata-node:/futureField"), Is.Null);
            Invoke(root.Query<Button>().ToList().Single(x => x.text == "校验并应用 JSON"));
            Assert.That(root.Q("leveldata-tree").Query<Label>().ToList().Any(x => x.text == "precise : 0.1234567890123456789"), Is.True);
            var fold = root.Q<Foldout>("leveldata-node:/futureField");
            Assert.That(fold, Is.Not.Null);
            fold.value = true;
            Assert.That(fold.Query<Label>().ToList().Any(x => x.text == "[1] : null"), Is.True);
            window.CreateGUI();
            Assert.That(window.rootVisualElement.Q<Foldout>("leveldata-node:/futureField").value, Is.True);
        }

        [Test]
        public void WideArraysAreLazyAndPagedWithoutLosingTheTail()
        {
            var root = window.rootVisualElement;
            var field = root.Q<TextField>("leveldata-json");
            var doc = Newtonsoft.Json.Linq.JObject.Parse(field.value);
            doc["many"] = new Newtonsoft.Json.Linq.JArray(Enumerable.Range(0, 123));
            field.value = doc.ToString();
            Invoke(root.Query<Button>().ToList().Single(x => x.text == "校验并应用 JSON"));
            var fold = root.Q<Foldout>("leveldata-node:/many");
            Assert.That(fold.contentContainer.childCount, Is.Zero);
            fold.value = true;
            Assert.That(fold.contentContainer.childCount, Is.EqualTo(51));
            Invoke(fold.Q<Button>()); Invoke(fold.Q<Button>());
            Assert.That(fold.contentContainer.childCount, Is.EqualTo(123));
            Assert.That(fold.Q<Button>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator SplittersResizeSidePanelsAndCenterRemainsVisible()
        {
            window.position = new Rect(30, 60, 1100, 700);
            typeof(MapEditorWindow).GetField("editorDrawerOpen", Private)!.SetValue(window, true);
            typeof(MapEditorWindow).GetField("resourceDrawerOpen", Private)!.SetValue(window, true);
            window.CreateGUI(); yield return null;
            var root = window.rootVisualElement;
            foreach (var name in new[] { "editor-splitter", "resource-splitter" })
            {
                var handle = root.Q(name);
                var panel = root.Q(name == "editor-splitter" ? "editor-drawer" : "resource-drawer");
                var before = panel.resolvedStyle.width;
                var point = handle.worldBound.center;
                using (var down = MouseDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = point })) handle.SendEvent(down);
                yield return null;
                var delta = name == "editor-splitter" ? 30 : -30;
                using (var move = MouseMoveEvent.GetPooled(new Event { type = EventType.MouseMove, mousePosition = point + new Vector2(delta, 0) })) handle.SendEvent(move);
                using (var up = MouseUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0 })) handle.SendEvent(up);
                yield return null;
                Assert.That(panel.resolvedStyle.width, Is.GreaterThan(before + 20));
            }
            var main = root.Q("leveldata-document");
            Assert.That(main.worldBound.xMin, Is.GreaterThanOrEqualTo(root.Q("editor-drawer").worldBound.xMax));
            Assert.That(main.worldBound.xMax, Is.LessThanOrEqualTo(root.Q("resource-drawer").worldBound.xMin));
            var centerWidth = main.resolvedStyle.width;
            Click("editor-drawer-toggle"); yield return null;
            Assert.That(main.resolvedStyle.width, Is.GreaterThan(centerWidth));
        }
    }
}
