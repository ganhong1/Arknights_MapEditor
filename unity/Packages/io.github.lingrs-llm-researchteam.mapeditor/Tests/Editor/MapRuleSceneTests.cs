#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Storage;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arknights.MapEditor.Editor.Tests
{
    public sealed class MapRuleSceneTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        [Test] public void SharedBoundaryMatchesNativeStringDirectionAndKeepsCoordinateParsingStrict()
        {
            var edge = JObject.Parse("{\"pos\":{\"row\":1,\"col\":1},\"direction\":\"UP\",\"blockMask\":\"ALL\"}");
            Assert.That(MapEditorWindow.RuleMatches("blockEdges", edge, new Vector3Int(1,2,2)), Is.True);
            Assert.That(MapEditorWindow.RuleMatches("blockEdges", edge, new Vector3Int(2,1,3)), Is.False);
            Assert.That(MapEditorWindow.RuleMask(edge["blockMask"]), Is.EqualTo(3));
            Assert.That(MapEditorWindow.RuleInt(new JValue("UP")), Is.Null);
        }
        [Test] public void RectangleEditsPreserveImportedRulesAndDeduplicateCoordinates()
        {
            const int size = 128;
            var cells = Enumerable.Range(0, size * size).Select(i => new Vector3Int(i % size, i / size, 0)).ToArray();
            var imported = JArray.Parse("[{\"row\":0,\"col\":0,\"extension\":true},{\"row\":-1,\"col\":0}]");
            var initial = LevelDocument.Create(size, size).WithSection("tilesDisallowToLocate", imported.ToString());
            var painted = MapEditorWindow.EditMapRuleRows("tilesDisallowToLocate", imported, cells, false, 3);
            Assert.That(painted.Count, Is.EqualTo(cells.Length + 1));
            Assert.That(painted[0]!["extension"]!.Value<bool>(), Is.True);
            var document = initial.SetMapRules("tilesDisallowToLocate", painted.ToString());
            var erased = MapEditorWindow.EditMapRuleRows("tilesDisallowToLocate", painted, cells, true, 3);
            Assert.That(erased.Count, Is.EqualTo(1));
            Assert.That(document.SetMapRules("tilesDisallowToLocate", erased.ToString()).GetMapRules("tilesDisallowToLocate"),
                Is.EqualTo(erased).Using(JToken.EqualityComparer));
        }

        [UnityTest] public IEnumerator GestureCommitsOneUndoAndRejectsLockedOrStaleDocuments()
        {
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.ShowUtility(); yield return null;
                var session = (DocumentSession)typeof(MapEditorWindow).GetField("session", Flags)!.GetValue(window)!;
                session.UseLocalHistory();
                var doc = LevelDocument.Create(4,4).WithMapDataField("blockEdges", "[{\"pos\":{\"row\":1,\"col\":1,\"future\":3},\"direction\":\"UP\",\"blockMask\":\"ALL\",\"custom\":7}]");
                session.Load(doc.ToJson(), "", ""); typeof(MapEditorWindow).GetField("draft", Flags)!.SetValue(window, session.Json);
                typeof(MapEditorWindow).GetMethod("ClearInspectorDrafts", Flags)!.Invoke(window,null); window.CreateGUI();
                var before = session.Json; var expected = window.WorkspaceDocument;
                window.ApplyMapRuleGesture("blockEdges", new[] {new Vector3Int(1,2,2),new Vector3Int(2,2,0)}, false, 1, expected);
                var rows = (JArray)session.Document.GetMapRules("blockEdges")!;
                Assert.That(rows.Count, Is.EqualTo(2)); Assert.That(rows[0]!["blockMask"]!.Value<string>(), Is.EqualTo("WALK_ONLY"));
                Assert.That(rows[0]!["custom"]!.Value<int>(), Is.EqualTo(7));
                Assert.That(rows[0]!["pos"]!["future"]!.Value<int>(), Is.EqualTo(3));
                Assert.Throws<EditorOperationRejectedException>(() => window.ApplyMapRuleGesture("blockEdges", new[] {Vector3Int.zero}, false, 3, expected));
                window.SetWorkspaceEditLock(true);
                Assert.Throws<EditorOperationRejectedException>(() => window.ApplyMapRuleGesture("blockEdges", new[] {Vector3Int.zero}, false, 3, window.WorkspaceDocument));
                window.SetWorkspaceEditLock(false); session.StepHistory(false); Assert.That(session.Json, Is.EqualTo(before));
            }
            finally
            {
                typeof(MapEditorWindow).GetMethod("ClearInspectorDrafts", Flags)!.Invoke(window,null);
                window.DiscardChanges(); UnityEngine.Object.DestroyImmediate(window);
            }
        }
        [UnityTest] public IEnumerator EmptyEraseDoesNotCreateMissingRootRuleField()
        {
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.ShowUtility(); yield return null;
                var before = window.WorkspaceDocument;
                window.ApplyMapRuleGesture("tilesDisallowToLocate", new[] {Vector3Int.zero}, true, 3, before);
                Assert.That(window.WorkspaceDocument, Is.SameAs(before));
            }
            finally { window.DiscardChanges(); UnityEngine.Object.DestroyImmediate(window); }
        }
    }
}
