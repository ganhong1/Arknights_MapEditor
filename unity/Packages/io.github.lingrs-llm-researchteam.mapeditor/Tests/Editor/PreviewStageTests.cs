using System.Collections;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Arknights.MapEditor.Tests
{
    public sealed class PreviewStageTests
    {
        [Test]
        public void InvalidPreviewClosesSceneCreatedBeforeValidation()
        {
            var stage = ScriptableObject.CreateInstance<MapPreviewStage>();
            try
            {
                var open = typeof(MapPreviewStage).GetMethod("OnOpenStage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.Throws<System.Reflection.TargetInvocationException>(() => open.Invoke(stage, null));
                Assert.That(stage.scene.IsValid(), Is.False);
            }
            finally { Object.DestroyImmediate(stage); }
        }

        [UnityTest]
        public IEnumerator ClosingWindowReleasesItsRepeatedPreview()
        {
            var original = StageUtility.GetCurrentStage();
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            var open = typeof(MapEditorWindow).GetMethod("OpenPreview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Mesh first = null;
            Mesh second = null;
            try
            {
                open.Invoke(window, null);
                first = ((MapPreviewStage)StageUtility.GetCurrentStage()).OwnedMesh;
                open.Invoke(window, null);
                second = ((MapPreviewStage)StageUtility.GetCurrentStage()).OwnedMesh;
                Assert.That(first == null, Is.True);
                Object.DestroyImmediate(window);
                yield return null;
                Assert.That(second == null, Is.True);
                Assert.That(StageUtility.GetCurrentStage(), Is.SameAs(original));
            }
            finally
            {
                if (window != null) Object.DestroyImmediate(window);
            }
        }

        [UnityTest]
        public IEnumerator NewDocumentReplacesOldPreviewWithEditableBlankMap()
        {
            var original = StageUtility.GetCurrentStage();
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(MapEditorWindow);
            try
            {
                type.GetMethod("OpenPreview", flags).Invoke(window, null);
                var oldStage = (MapPreviewStage)StageUtility.GetCurrentStage();
                var oldMesh = oldStage.OwnedMesh;
                type.GetMethod("NewDocument", flags).Invoke(window, null);
                yield return null;

                var newStage = (MapPreviewStage)StageUtility.GetCurrentStage();
                Assert.That(newStage, Is.SameAs(type.GetField("ownedPreview", flags).GetValue(window)));
                Assert.That(newStage, Is.Not.SameAs(oldStage));
                Assert.That(oldMesh == null, Is.True);
                Assert.That(newStage.OwnedMesh.vertexCount, Is.EqualTo(10 * 6 * 20));
                Assert.That(window.hasUnsavedChanges, Is.False);

                var paint = (System.Action<System.Collections.Generic.IReadOnlyList<Vector2Int>>)
                    typeof(MapPreviewStage).GetField("paint", flags).GetValue(newStage);
                var confirmations = 0;
                window.TileReplacementPrompt = count =>
                {
                    Assert.That(count, Is.EqualTo(1));
                    confirmations++;
                    return 0;
                };
                paint(new[] { Vector2Int.zero });
                Assert.That(confirmations, Is.EqualTo(1));
                Assert.That(window.WorkspaceDocument.GetCell(0, 0)["tileKey"]?.ToString(), Is.EqualTo("tile_road"));
                Assert.That(window.hasUnsavedChanges, Is.True);
            }
            finally { Object.DestroyImmediate(window); }
            Assert.That(StageUtility.GetCurrentStage(), Is.SameAs(original));
        }

        [UnityTest]
        public IEnumerator EnterAndExitPreviewPreservesUserSceneAndCleansOwnedMesh()
        {
            var original = SceneManager.GetActiveScene();
            var originalDirty = original.isDirty;
            var count = original.rootCount;
            var stage = ScriptableObject.CreateInstance<MapPreviewStage>();
            stage.Configure(LevelDocument.Create(7, 3));
            StageUtility.GoToStage(stage, false);
            yield return null;
            Assert.That(StageUtility.GetCurrentStage(), Is.SameAs(stage));
            Assert.That(stage.scene.IsValid(), Is.True);
            Assert.That(stage.OwnedMesh.vertexCount, Is.EqualTo(7 * 3 * 20));
            var mesh = stage.OwnedMesh;
            StageUtility.GoBackToPreviousStage();
            yield return null;
            Assert.That(mesh == null, Is.True);
            Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(original.handle));
            Assert.That(original.rootCount, Is.EqualTo(count));
            Assert.That(original.isDirty, Is.EqualTo(originalDirty));
        }
    }
}
