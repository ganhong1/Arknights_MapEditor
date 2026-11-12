using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Arknights.MapEditor.Tests
{
    public sealed class PrtsLabelViewTests
    {
        [Test]
        public void LabelsUsePlanarNativeGlyphsAndReleaseTheirMesh()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var view = new PrtsLabelView(scene);
            try
            {
                Assert.DoesNotThrow(() => view.Update(LevelDocument.Create(1, 1), false, false));
                Assert.That(scene.GetRootGameObjects(), Is.Empty);
                var doc = LevelDocument.Parse("{\"mapData\":{\"map\":[[0]],\"tiles\":[{\"tileKey\":\"tile_telout\",\"heightType\":\"LOWLAND\"}]}}");
                view.Update(doc, false, false);
                var root = scene.GetRootGameObjects()[0];
                var mesh = root.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh.vertexCount, Is.EqualTo(12));
                foreach(var vertex in mesh.vertices) Assert.That(vertex.y, Is.EqualTo(.02f).Within(.001));
                view.Update(doc, true, true);
                Assert.That(mesh.vertexCount, Is.GreaterThan(12));
                view.Update(LevelDocument.Create(1, 1), false, false);
                Assert.That(mesh.vertexCount, Is.Zero);
                view.Dispose(); Assert.That(mesh == null, Is.True);
                Assert.That(scene.GetRootGameObjects(), Is.Empty);
            }
            finally { view.Dispose(); EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
