using System.Reflection;
using System.Collections;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;

namespace Arknights.MapEditor.Tests
{
    public sealed class WindowTests
    {
        [UnityTest]
        public IEnumerator WorkbenchDrawerAndRoutePresetTabsKeepSeparateState()
        {
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.Show();
                yield return null;
                window.CreateGUI();
                var root = window.rootVisualElement;
                var drawer = root.Q("resource-drawer");
                var toggle = root.Q<Button>("resource-drawer-toggle");
                Assert.That(drawer.style.display.value, Is.EqualTo(DisplayStyle.None));
                Submit(toggle);
                Assert.That(drawer.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(drawer.Q<ListView>("terrain-library"), Is.Not.Null);
                var categories = new[] { "装置", "敌人", "美术" };
                for (var i = 1; i <= categories.Length; i++)
                {
                    var category = drawer.Q<Button>("resource-category-" + i);
                    Assert.That(category.text, Is.EqualTo(categories[i - 1]));
                    Submit(category);
                    var panel = drawer.Q("resource-panel-" + i);
                    Assert.That(panel.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                    if (i == 1) Assert.That(panel.Q<ListView>("device-library"), Is.Not.Null);
                    else Assert.That(panel.Q<HelpBox>(), Is.Not.Null);
                    Assert.That(drawer.Q("resource-panel-0").style.display.value, Is.EqualTo(DisplayStyle.None));
                }

                Assert.That(root.Q<Button>("workbench-tab-1").text, Is.EqualTo("路线"));
                Assert.That(root.Q<Button>("workbench-tab-5").text, Is.EqualTo("关卡预设"));
                Assert.That(root.Q("workbench-page-1").Q("route-editor"), Is.Not.Null);
                Assert.That(root.Q("workbench-page-1").Q<TextField>("device-edit-json"), Is.Null);
                Assert.That(root.Q("workbench-page-5").Q<TextField>("device-edit-json"), Is.Not.Null);
                Submit(root.Q<Button>("workbench-tab-5"));
                Submit(toggle);
                Assert.That(drawer.style.display.value, Is.EqualTo(DisplayStyle.None));
                Submit(toggle);
                Assert.That(drawer.Q("resource-panel-3").style.display.value, Is.EqualTo(DisplayStyle.Flex));

                window.CreateGUI();
                Assert.That(root.Q("resource-drawer").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(root.Q("resource-panel-3").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(root.Q("workbench-page-5").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(window.hasUnsavedChanges, Is.False);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [UnityTest]
        public IEnumerator TerrainDetailsBrowseVariantsWithoutChangingSelection()
        {
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.ShowUtility();
                yield return null;
                window.CreateGUI();
                var root = window.rootVisualElement;
                Submit(root.Q<Button>("resource-drawer-toggle"));
                root.Q<TextField>("terrain-search").value = "字格";
                yield return null;
                yield return null;

                var brush = typeof(MapEditorWindow).GetField("brushJson", BindingFlags.Instance | BindingFlags.NonPublic);
                var initial = brush.GetValue(window);
                var button = root.Q<Button>("terrain-info-tile_character");
                Assert.That(button, Is.Not.Null);
                Assert.That(button.Q<Image>().image, Is.Not.Null);
                var name = button.parent.Q<Label>(className: "catalog-name");
                Assert.That(name.text, Is.EqualTo("字格"));
                var font = name.resolvedStyle.unityFontDefinition.fontAsset;
                Assert.That(font, Is.Not.Null);
                Assert.That(font.name, Is.EqualTo("NotoSansSC-Workbench"));
                Assert.That(font.HasCharacter('字'), Is.True);
                Assert.That(font.atlasPopulationMode, Is.EqualTo(UnityEngine.TextCore.Text.AtlasPopulationMode.Static));
                Submit(button);

                var overlay = root.Q("terrain-detail-overlay");
                Assert.That(overlay.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(overlay.Q<TextField>("terrain-detail-tiledata").value, Does.Contain("Empty"));
                Assert.That(overlay.Q("terrain-original-samples").childCount, Is.EqualTo(8));
                Assert.That(overlay.Q<Button>("terrain-recommendation-3"), Is.Null);
                var knownConfigurations = overlay.Q<Foldout>("terrain-known-configurations");
                Assert.That(knownConfigurations.text, Is.EqualTo("全部既有已知配置 · 10"));
                var totalSources = overlay.Q<Foldout>("terrain-total-source-browser");
                Assert.That(totalSources.text, Is.EqualTo("总出处 · 25 个关卡"));
                Assert.That(totalSources.Q<ListView>("terrain-source-list").itemsSource.Count, Is.EqualTo(25));
                totalSources.value = true;
                totalSources.Q<ListView>("terrain-source-list").SetSelection(0);
                var totalMap = totalSources.Q<TerrainSourceMap>();
                Assert.That(totalMap?.HighlightCount, Is.GreaterThan(0));
                var firstSourceCount = overlay.Q<Label>("terrain-current-source-count").text;
                var browser = overlay.Q<Foldout>("terrain-source-browser");
                browser.value = true;
                var sourceList = browser.Q<ListView>("terrain-source-list");
                sourceList.SetSelection(0);
                Assert.That(browser.Q<TerrainSourceMap>()?.HighlightCount, Is.GreaterThan(0));
                Submit(browser.Q<Button>("terrain-source-show-cards"));
                var card = browser.Q("terrain-source-cards").Q<Button>();
                Submit(card);
                Assert.That(browser.Q<TerrainSourceMap>()?.HighlightCount, Is.GreaterThan(0));
                var path = card.tooltip;
                var configJson = overlay.Q<TextField>("terrain-detail-tiledata");
                yield return null;
                var sourceTop = browser.worldBound.yMin;
                var sourceHeight = browser.worldBound.height;
                var jsonViewport = overlay.Q<ScrollView>("terrain-current-config-json-viewport");
                var jsonHeight = jsonViewport.worldBound.height;

                Submit(overlay.Q<Button>("terrain-recommendation-1"));
                yield return null;
                Assert.That(overlay.Q<Foldout>("terrain-source-browser"), Is.SameAs(browser));
                Assert.That(browser.value, Is.True);
                Assert.That(sourceList.style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(overlay.Q<TextField>("terrain-detail-tiledata"), Is.SameAs(configJson));
                Assert.That(browser.worldBound.yMin, Is.EqualTo(sourceTop).Within(1f));
                Assert.That(browser.worldBound.height, Is.EqualTo(sourceHeight).Within(1f));
                Assert.That(jsonViewport.worldBound.height, Is.EqualTo(jsonHeight).Within(1f));
                Assert.That(overlay.Q<TextField>("terrain-detail-tiledata").value, Does.Contain("Fortune"));
                Assert.That(overlay.Q<Foldout>("terrain-total-source-browser"), Is.SameAs(totalSources));
                Assert.That(totalSources.Q<TerrainSourceMap>(), Is.SameAs(totalMap));
                Assert.That(overlay.Q<Label>("terrain-current-source-count").text, Is.Not.EqualTo(firstSourceCount));
                Assert.That(browser.Q<TerrainSourceMap>() != null, Is.EqualTo(sourceList.itemsSource.Contains(path)));
                knownConfigurations.value = true;
                Submit(knownConfigurations.Q<Button>("terrain-sample-next"));
                Assert.That(knownConfigurations.Q("terrain-original-samples").childCount, Is.EqualTo(2));
                var tenth = knownConfigurations.Q<Button>("terrain-sample-card-10");
                Submit(tenth);
                Assert.That(tenth.ClassListContains("selected-tab"), Is.True);
                Assert.That(overlay.Q<Label>("terrain-current-source-count").text, Does.Contain("#10"));
                Assert.That(browser.value, Is.True);
                Assert.That(overlay.Q<Foldout>("terrain-total-source-browser"), Is.SameAs(totalSources));
                Submit(overlay.Q<Button>("terrain-variant-tile_characterf"));
                Assert.That(overlay.Q<TextField>("terrain-detail-tiledata").value, Does.Contain("tile_characterf"));
                Assert.That(overlay.Q("terrain-original-samples").childCount, Is.EqualTo(7));
                Assert.That(overlay.Q<Button>("terrain-variant-tile_characterf").ClassListContains("selected-tab"), Is.True);
                Submit(overlay.Q<Button>("terrain-detail-close"));
                Assert.That(overlay.style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(brush.GetValue(window), Is.EqualTo(initial));
                Submit(button.parent.Q<Button>(className: "terrain-select"));
                Assert.That((string)brush.GetValue(window), Does.Contain("Empty"));
                Submit(button);
                var configurations = overlay.Q<Foldout>("terrain-known-configurations");
                configurations.value = true;
                Submit(configurations.Q<Button>("terrain-sample-card-2"));
                Submit(overlay.Q<Button>("terrain-use-sample"));
                Assert.That((string)brush.GetValue(window), Does.Contain("Fortune"));
                Assert.That(overlay.style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(window.hasUnsavedChanges, Is.False);
            }
            finally { Object.DestroyImmediate(window); }
        }

        private static void Submit(Button button)
        {
            using var submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        [UnityTest]
        public IEnumerator NarrowWorkbenchKeepsToolbarAndDrawerRowsSeparate()
        {
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.ShowUtility();
                window.position = new Rect(40, 80, 430, 420);
                window.CreateGUI();
                var root = window.rootVisualElement;
                Submit(root.Q<Button>("resource-drawer-toggle"));
                yield return null;
                yield return null;

                var toolbar = root.Q<VisualElement>(className: "workbench-toolbar");
                var header = root.Q<VisualElement>(className: "workbench-heading-row");
                var title = header.Q<Label>();
                var body = root.Q("workbench-body");
                var drawer = root.Q("resource-drawer");
                var categoryTabs = drawer.Q<VisualElement>(className: "workbench-tabs");
                var search = drawer.Q<TextField>("terrain-search");
                var filter = drawer.Q<Toggle>("terrain-filter");
                var list = drawer.Q<ListView>("terrain-library");
                Assert.That(root.layout.width, Is.InRange(420f, 450f));
                Assert.That(title.layout.height, Is.GreaterThan(12f));
                Assert.That(toolbar.worldBound.yMax, Is.LessThanOrEqualTo(header.worldBound.yMin + 1));
                Assert.That(header.worldBound.yMax, Is.LessThanOrEqualTo(body.worldBound.yMin + 1));
                Assert.That(categoryTabs.worldBound.yMax, Is.LessThanOrEqualTo(search.worldBound.yMin + 1));
                Assert.That(search.worldBound.yMax, Is.LessThanOrEqualTo(filter.worldBound.yMin + 1));
                Assert.That(filter.worldBound.yMax, Is.LessThanOrEqualTo(list.worldBound.yMin + 1));
                Assert.That(list.layout.height, Is.GreaterThan(20f));
                Assert.That(drawer.worldBound.xMax, Is.LessThanOrEqualTo(body.worldBound.xMax + 1));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [UnityTest]
        public IEnumerator InvalidDraftSaveReportsErrorAndRetainsDirtyText()
        {
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.Show();
                yield return null;
                window.CreateGUI();
                var field = window.rootVisualElement.Q<TextField>("leveldata-json");
                field.value = "incomplete JSON";
                Assert.DoesNotThrow(() => window.SaveChanges());
                Assert.That(window.hasUnsavedChanges, Is.True);
                Assert.That(field.value, Is.EqualTo("incomplete JSON"));
                Assert.That(window.rootVisualElement.Q<Label>().text, Is.Not.Empty);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [UnityTest]
        public IEnumerator RebuildingUiPreservesUnappliedDraft()
        {
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.Show();
                yield return null;
                window.CreateGUI();
                window.rootVisualElement.Q<TextField>("leveldata-json").value = "invalid but unsaved draft";
                Assert.That(window.hasUnsavedChanges, Is.True);
                window.CreateGUI();
                Assert.That(window.rootVisualElement.Q<TextField>("leveldata-json").value, Is.EqualTo("invalid but unsaved draft"));
                Assert.That(window.hasUnsavedChanges, Is.True);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [UnityTest]
        public IEnumerator ApplyingDraftThenUndoAndRedoUpdatesVisibleText()
        {
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.Show();
                yield return null;
                window.CreateGUI();
                var field = window.rootVisualElement.Q<TextField>("leveldata-json");
                var original = field.value;
                field.value = LevelDocument.Parse(original).WithLevelId("draft_b").ToJson();
                typeof(MapEditorWindow).GetMethod("ApplyDraft", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
                Undo.PerformUndo();
                Assert.That(field.value, Is.EqualTo(original));
                Undo.PerformRedo();
                Assert.That(LevelDocument.Parse(field.value).LevelId, Is.EqualTo("draft_b"));
            }
            finally { Object.DestroyImmediate(window); }
        }
    }
}
