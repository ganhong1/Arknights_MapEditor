using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Arknights.MapEditor.Tests
{
    public sealed class DomainReloadTests
    {
        private const string Key = "Arknights.MapEditor.Tests.DomainReload.";
        private const string Draft = "unapplied draft retained through actual compilation";
        private static readonly string DomainIdentity = Guid.NewGuid().ToString("N");

        [UnityTest]
        public IEnumerator ActualCompilationPreservesAppliedDocumentDraftAndSingleUndoSubscription()
        {
            // SessionState, not static references, survives the Test Runner's domain reload.
            if (SessionState.GetString(Key + "folder", "").Length == 0)
            {
                var suffix = Guid.NewGuid().ToString("N");
                var folder = "Assets/MapEditorReloadTest_" + suffix;
                SessionState.SetString(Key + "folder", folder);
                SessionState.SetString(Key + "domainBefore", DomainIdentity);
                var window = ScriptableObject.CreateInstance<MapEditorWindow>();
                SessionState.SetInt(Key + "window", window.GetInstanceID());
                window.Show();
                window.CreateGUI();
                var session = Session(window);
                session.Edit(LevelDocument.Create(7, 3).WithLevelId("reload-applied"), "Reload test edit");
                window.rootVisualElement.Q<TextField>("leveldata-json").value = session.Json;
                typeof(MapEditorWindow).GetMethod("OpenPreview", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
                window.rootVisualElement.Q<TextField>("leveldata-json").value = Draft;
                window.rootVisualElement.Query<TextField>().ToList().Single(f => f.label == "字段 JSON（保留未知内容）").value = "{local-unfinished";
                window.rootVisualElement.Query<TextField>().ToList().Single(f => f.label == "美术映射 JSON").value = "{art-unfinished";
                AssertSingleSubscription(window);
                var fullFolder = Path.Combine(Application.dataPath, "MapEditorReloadTest_" + suffix);
                Directory.CreateDirectory(fullFolder);
                File.WriteAllText(Path.Combine(fullFolder, "ReloadProbe.cs"), "internal sealed class ReloadProbe_" + suffix + " {}\n");
            }

            // Official 1.1.33 implementation calls AssetDatabase.Refresh itself, waits for
            // compilation, unlocks reload and requires a successful domain reload.
            yield return new RecompileScripts();
            yield return null;

            var previousDomain = SessionState.GetString(Key + "domainBefore", "");
            Assert.That(previousDomain, Is.Not.Empty);
            Assert.That(DomainIdentity, Is.Not.EqualTo(previousDomain), "A new static domain identity is required; a passed yield alone is insufficient.");

            var restored = EditorUtility.InstanceIDToObject(SessionState.GetInt(Key + "window", 0)) as MapEditorWindow;
            Assert.That(restored, Is.Not.Null, "The original window must survive, not be recreated by the test.");
            restored.CreateGUI();
            var restoredSession = Session(restored);
            Assert.That(restoredSession.Document.LevelId, Is.EqualTo("reload-applied"));
            Assert.That(restoredSession.Document.Width, Is.EqualTo(7));
            Assert.That(restoredSession.Document.Height, Is.EqualTo(3));
            Assert.That(restored.rootVisualElement.Q<TextField>("leveldata-json").value, Is.EqualTo(Draft));
            Assert.That(restored.hasUnsavedChanges, Is.True);
            Assert.That(restored.rootVisualElement.Query<TextField>().ToList().Single(f => f.label == "字段 JSON（保留未知内容）").value, Is.EqualTo("{local-unfinished"));
            Assert.That(restored.rootVisualElement.Query<TextField>().ToList().Single(f => f.label == "美术映射 JSON").value, Is.EqualTo("{art-unfinished"));
            var stage = typeof(MapEditorWindow).GetField("ownedPreview", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(restored) as MapPreviewStage;
            Assert.That(stage, Is.Not.Null);
            Assert.That(typeof(MapPreviewStage).GetField("paint", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(stage), Is.Not.Null, "Paint callback must be rebound after domain reload.");
            Assert.That(stage.OwnedMesh.vertexCount, Is.GreaterThan(0));
            AssertSingleSubscription(restored);
            restored.CreateGUI();
            AssertSingleSubscription(restored);
        }

        [UnityTearDown]
        public IEnumerator CleanupOnlyOwnedProbeAndWindow()
        {
            var window = EditorUtility.InstanceIDToObject(SessionState.GetInt(Key + "window", 0)) as MapEditorWindow;
            if (window != null) Object.DestroyImmediate(window);
            SessionState.EraseInt(Key + "window");
            SessionState.EraseString(Key + "domainBefore");
            var folder = SessionState.GetString(Key + "folder", "");
            SessionState.EraseString(Key + "folder");
            if (folder.Length == 0) yield break;
            Assert.That(folder, Does.StartWith("Assets/MapEditorReloadTest_"));
            var full = Path.GetFullPath(Path.Combine(Application.dataPath, folder.Substring("Assets/".Length)));
            var assetRoot = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            Assert.That(full.StartsWith(assetRoot, StringComparison.OrdinalIgnoreCase), Is.True);
            var hadScript = File.Exists(Path.Combine(full, "ReloadProbe.cs"));
            if (Directory.Exists(full)) Directory.Delete(full, true);
            if (File.Exists(full + ".meta")) File.Delete(full + ".meta");
            if (hadScript) yield return new RecompileScripts();
        }

        private static DocumentSession Session(MapEditorWindow window)
        {
            var field = typeof(MapEditorWindow).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (DocumentSession)field.GetValue(window);
        }

        private static void AssertSingleSubscription(MapEditorWindow window)
        {
            // Read-only test instrumentation; no private Unity UI or event mutation.
            var field = typeof(Undo).GetField("undoRedoPerformed", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Update this test instrumentation if the pinned Unity API representation changes.");
            var callbacks = field.GetValue(null) as Delegate;
            var count = callbacks == null ? 0 : callbacks.GetInvocationList().Count(callback =>
                ReferenceEquals(callback.Target, window) && callback.Method.Name == "OnUndo");
            Assert.That(count, Is.EqualTo(1), "This window must own exactly one Undo subscription.");
        }
    }
}
