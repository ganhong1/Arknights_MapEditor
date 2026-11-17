#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Arknights.MapEditor.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Tests
{
    public sealed class DiagnosticsWindowTests
    {
        private static DiagnosticEvent[] Records()
        {
            var now = DateTimeOffset.UtcNow;
            return new[]
            {
                new DiagnosticEvent { Id = "event-branch", Operation = "document.branch.rename", Code = "VALIDATION_FAILED",
                    Category = "validation", Level = "info", Count = 3, Utc = now, LastUtc = now, DocumentId = "map-a" },
                new DiagnosticEvent { Id = "event-save", Operation = "document.save", Code = "STORAGE_FAILED",
                    Category = "io", Level = "warning", Utc = now.AddMinutes(-1), LastUtc = now.AddMinutes(-1), DocumentId = "map-b" },
                new DiagnosticEvent { Id = "event-resource", Operation = "catalog.load", Code = "INTERNAL_ERROR",
                    Category = "internal", Level = "error", Utc = now.AddMinutes(-2), LastUtc = now.AddMinutes(-2) }
            };
        }

        private static void Click(VisualElement root, string name)
        {
            var button = root.Q<Button>(name);
            Assert.That(button, Is.Not.Null);
            typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(button.clickable, new object?[] { null });
        }

        private static void Prepare(DiagnosticsWindow window)
        {
            window.CreateGUI();
            window.rootVisualElement.Q<Toggle>("diagnostics-auto").value = false;
            window.RefreshFrom(Records(), true);
        }

        [UnityTest]
        public IEnumerator FiltersAndRefreshPreserveSelectionAndReadOnlyDetails()
        {
            var window = ScriptableObject.CreateInstance<DiagnosticsWindow>();
            try
            {
                window.ShowUtility(); yield return null;
                Prepare(window);
                var root = window.rootVisualElement;
                var list = root.Q<ListView>("diagnostics-events");
                var detail = root.Q<TextField>("diagnostics-detail");
                Assert.That(list.itemsSource.Count, Is.EqualTo(3));
                Assert.That(detail.isReadOnly, Is.True);
                list.SetSelection(0);
                Assert.That(detail.value, Does.Contain("event-branch"));
                Assert.That(detail.value, Does.Contain("map-a"));
                var originalControl = root.Q<ListView>("diagnostics-events");
                window.RefreshFrom(Records(), true);
                Assert.That(root.Q<ListView>("diagnostics-events"), Is.SameAs(originalControl));
                Assert.That(list.selectedIndex, Is.Zero);
                Assert.That(root.Q<Label>("diagnostics-count").text, Does.Contain("5 次"));
                root.Q<PopupField<string>>("diagnostics-module").value = "catalog";
                Assert.That(list.itemsSource.Count, Is.EqualTo(1));
                Assert.That(detail.value, Is.EqualTo("选择事件查看详情。"));
                root.Q<PopupField<string>>("diagnostics-module").value = "全部";
                Assert.That(list.selectedIndex, Is.Zero);
                Assert.That(detail.value, Does.Contain("event-branch"));
                root.Q<PopupField<string>>("diagnostics-level").value = "warning";
                Assert.That(list.itemsSource.Count, Is.EqualTo(1));
                Assert.That(((DiagnosticEvent)list.itemsSource[0]).Id, Is.EqualTo("event-save"));
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        [UnityTest]
        public IEnumerator EventSearchUsesExistingCatalogSearchAndKeepsSnapshotsUnchanged()
        {
            var window = ScriptableObject.CreateInstance<DiagnosticsWindow>();
            try
            {
                window.ShowUtility(); yield return null;
                Prepare(window);
                var records = Records();
                const string repeatedEvent = "7e6c1a845d2b4cbcaeb0d1f693202e91";
                const string repeatedOperation = "f092b7dba0e942759effdc17b684130c";
                records[0].RelatedEventIds = new[] { records[0].Id, repeatedEvent };
                records[0].RelatedOperationIds = new[] { repeatedOperation };
                var before = string.Join("\n", records.Select(item => item.ToJson().ToString()));
                window.RefreshFrom(records, true);
                var root = window.rootVisualElement;
                var list = root.Q<ListView>("diagnostics-events");
                root.Q<TextField>("diagnostics-search").value = "event-resource";
                var deadline = EditorApplication.timeSinceStartup + 10;
                while (root.Q<Label>("diagnostics-count").text == "搜索中…" && EditorApplication.timeSinceStartup < deadline)
                    yield return null;
                Assert.That(root.Q<Label>("diagnostics-count").text, Does.Not.Contain("搜索中"));
                Assert.That(root.Q<Label>("diagnostics-message").text, Is.Empty);
                Assert.That(list.itemsSource.Cast<DiagnosticEvent>().Select(item => item.Id), Does.Contain("event-resource"));
                foreach (var related in new[] { repeatedEvent, repeatedOperation })
                {
                    root.Q<TextField>("diagnostics-search").value = related;
                    deadline = EditorApplication.timeSinceStartup + 10;
                    while (root.Q<Label>("diagnostics-count").text == "搜索中…" && EditorApplication.timeSinceStartup < deadline)
                        yield return null;
                    Assert.That(root.Q<Label>("diagnostics-count").text, Does.Not.Contain("搜索中"));
                    Assert.That(root.Q<Label>("diagnostics-message").text, Is.Empty);
                    Assert.That(list.itemsSource.Cast<DiagnosticEvent>().Select(item => item.Id),
                        Is.EquivalentTo(new[] { "event-branch" }), related);
                    list.SetSelection(0);
                    Assert.That(root.Q<TextField>("diagnostics-detail").value, Does.Contain(related));
                }
                root.Q<TextField>("diagnostics-search").value = "";
                Assert.That(list.itemsSource.Count, Is.EqualTo(3));
                Assert.That(string.Join("\n", records.Select(item => item.ToJson().ToString())), Is.EqualTo(before));
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        [UnityTest]
        public IEnumerator InvalidSettingsStayInThePanelWithoutChangingConfiguration()
        {
            var window = ScriptableObject.CreateInstance<DiagnosticsWindow>();
            var initial = PluginLog.Settings;
            try
            {
                window.ShowUtility(); yield return null;
                Prepare(window);
                var root = window.rootVisualElement;
                root.Q<DoubleField>("diagnostics-size").value = double.NaN;
                Click(root, "diagnostics-configure");
                Assert.That(root.Q<Label>("diagnostics-message").text, Does.Contain("失败"));
                Assert.That(PluginLog.Settings.MaximumBytes, Is.EqualTo(initial.MaximumBytes));
                Assert.That(PluginLog.Settings.DirectoryPath, Is.EqualTo(initial.DirectoryPath));
                Assert.That(root.Q<DoubleField>("diagnostics-size").value, Is.NaN);
                root.Q<DoubleField>("diagnostics-size").value = 10;
                root.Q<TextField>("diagnostics-directory").value = "relative/logs";
                Click(root, "diagnostics-configure");
                Assert.That(root.Q<Label>("diagnostics-message").text, Does.Contain("绝对路径"));
                Assert.That(PluginLog.Settings.DirectoryPath, Is.EqualTo(initial.DirectoryPath));
                Assert.That(root.Q<Label>("diagnostics-health").text, Does.Contain("队列"));
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }
    }
}
