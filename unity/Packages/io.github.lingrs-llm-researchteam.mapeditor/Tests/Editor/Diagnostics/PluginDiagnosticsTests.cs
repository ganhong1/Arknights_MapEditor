#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Tests
{
    public sealed class PluginDiagnosticsTests
    {
        [Test]
        public void LogRotatesExportsInOrderAndKeepsPrivateTextOut()
        {
            var root = Path.Combine(Path.GetTempPath(), "ark-diagnostics-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var directory = Path.Combine(root, "logs");
                var sink = new PluginLogFile(directory, 1200, 3);
                for (var i = 0; i < 8; i++)
                    sink.Write(i.ToString(), "document.save", "warning", "io", PrivateFailure(), "session-id");
                var export = Path.Combine(root, "report.jsonl");
                sink.Export(export);
                var report = File.ReadAllText(export);
                var events = File.ReadLines(export).Select(JObject.Parse).ToArray();
                Assert.That(events.Last().Value<string>("id"), Is.EqualTo("7"));
                Assert.That(events.Select(e => int.Parse(e.Value<string>("id")!)), Is.Ordered);
                Assert.That(events[0].Value<string>("id"), Is.Not.EqualTo("0"));
                Assert.That(events.Last()["stack"]!.Values<string>(), Has.Some.Contains(nameof(PrivateFailure)));
                Assert.That(report, Does.Not.Contain("private-path-and-draft"));
                Assert.That(Directory.GetFiles(directory).Length, Is.EqualTo(3));
                Assert.Throws<IOException>(() => sink.Export(Path.Combine(directory, "plugin.jsonl")));
                Assert.That(File.ReadAllText(export), Is.EqualTo(report));
                using (new FileStream(Path.Combine(directory, "plugin.jsonl"), FileMode.Open, FileAccess.Read, FileShare.None))
                    Assert.Throws<IOException>(() => sink.Export(export));
                Assert.That(File.ReadAllText(export), Is.EqualTo(report));
            }
            finally { Directory.Delete(root, true); }
        }

        private static Exception PrivateFailure()
        {
            try { throw new IOException("private-path-and-draft"); }
            catch (IOException error) { return error; }
        }

        [Test]
        public void BrokenPortraitsAndEnemyIndexDegradeIndependently()
        {
            var portraits = EnemyPortraitCatalog.LoadOptional(() => throw new FormatException("bad portrait metadata"));
            Assert.That(portraits.Find("enemy_1507_mephi"), Is.Null);
            var parent = new VisualElement();
            var panel = new VisualElement();
            parent.Add(new Label("Map tools") { name = "map-tools" });
            parent.Add(panel);
            MapEditorWindow.BuildEnemyPanelSafely(panel, "test.enemy-catalog", () =>
            {
                panel.Add(new Label("partial") { name = "partial" });
                throw new FormatException("broken index");
            });
            Assert.That(parent.Q<Label>("map-tools"), Is.Not.Null);
            Assert.That(panel.Q<Label>("partial"), Is.Null);
            Assert.That(panel.Q<HelpBox>(), Is.Not.Null);
        }

        [Test]
        public void RecoveryDirectoryFailureKeepsDocumentUsableAndCanRetry()
        {
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var root = Path.Combine(Path.GetTempPath(), "ark-recovery-init-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var blockedDirectory = Path.Combine(root, "recovery");
            try
            {
                File.WriteAllText(blockedDirectory, "collision");
                var journal = typeof(MapEditorWindow).GetField("recovery", flags)!;
                journal.SetValue(window, null);
                typeof(MapEditorWindow).GetField("loadRecoveryDigest", flags)!.SetValue(window, true);
                var open = typeof(MapEditorWindow).GetMethod("TryOpenRecovery", flags)!;
                Assert.That(open.Invoke(window, new object[] { blockedDirectory }), Is.False);
                Assert.That(window.WorkspaceDocument.Width, Is.EqualTo(10));
                Assert.That(journal.GetValue(window), Is.Null);
                File.Delete(blockedDirectory);
                Assert.That(open.Invoke(window, new object[] { blockedDirectory }), Is.True);
                Assert.That(journal.GetValue(window), Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                Directory.Delete(root, true);
            }
        }
    }
}
