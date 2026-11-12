using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEditor.SceneManagement;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Tests
{
    public sealed class ChunkedMapTests
    {
        [Test]
        public void DirtyCellRebuildsOnlyItsChunkAndDisposeReleasesAllMeshes()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var view = new ChunkedMapView(scene);
            try
            {
                var document = LevelDocument.Create(35, 17);
                view.Update(document, null);
                Assert.That(view.ChunkCount, Is.EqualTo(6));
                var left = view.MeshAt(0, 0);
                var right = view.MeshAt(0, 2);
                document = document.WithCell(0, 0, LevelDocument.Create(1, 1).GetCell(0, 0));
                view.Update(document, new[] {new Vector2Int(0, 0)});
                Assert.That(view.MeshAt(0, 2), Is.SameAs(right));
                Assert.That(left == null, Is.True);
                view.Dispose();
                Assert.That(right == null, Is.True);
            }
            finally { view.Dispose(); EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void PickingChecksDimensionsAndHeightWithoutColliders()
        {
            var doc = LevelDocument.Create(7, 3);
            Assert.That(ChunkedMapView.TryPick(doc, new Ray(new Vector3(6.5f, 10, 2.5f), Vector3.down), out var cell), Is.True);
            Assert.That(cell, Is.EqualTo(new Vector2Int(6, 2)));
            Assert.That(ChunkedMapView.TryPick(doc, new Ray(new Vector3(7.1f, 10, 2.5f), Vector3.down), out _), Is.False);
            Assert.That(ChunkedMapView.TryPick(doc, new Ray(new Vector3(-.1f, 10, .5f), Vector3.down), out _), Is.False);
        }

        [Test]
        public void MeshHasInsetTopAndFourSidesWithStyleAndIndependentUvs()
        {
            var scene = EditorSceneManager.NewPreviewScene(); var view = new ChunkedMapView(scene);
            try
            {
                var low = JObject.Parse("{\"tileKey\":\"tile_road\",\"heightType\":\"LOWLAND\"}");
                var high = JObject.Parse("{\"tileKey\":\"tile_wall\",\"heightType\":\"HIGHLAND\"}");
                var doc = LevelDocument.Create(2, 1).WithCell(0, 0, low).WithCell(0, 1, high);
                view.Update(doc, null); var mesh = view.MeshAt(0, 0);
                Assert.That(mesh.vertexCount, Is.EqualTo(40));
                Assert.That(mesh.triangles.Length, Is.EqualTo(60));
                var vertices = mesh.vertices; var colors = mesh.colors; var uv = mesh.uv; var patterns = mesh.uv2;
                for (var cell = 0; cell < 2; cell++)
                {
                    var style = SkeletonStyle.Resolve(cell == 0 ? low : high);
                    for (var face = 0; face < 5; face++)
                    {
                        var offset = cell * 20 + face * 4;
                        Assert.That(uv[offset], Is.EqualTo(Vector2.zero));
                        Assert.That(uv[offset + 2], Is.EqualTo(Vector2.one));
                        for (var i = 0; i < 4; i++)
                        {
                            Assert.That(vertices[offset+i].x, Is.InRange(cell+.02f, cell+.98f));
                            Assert.That(vertices[offset+i].z, Is.InRange(.02f, .98f));
                            Assert.That(patterns[offset+i], Is.EqualTo(new Vector2(style.Pattern, face == 0 ? 1 : 0)));
                            Assert.That(colors[offset+i], Is.EqualTo(face == 0 ? style.TopColor : style.SideColor));
                        }
                        if (face == 0)
                        {
                            Assert.That(vertices[offset].y, Is.EqualTo(cell == 0 ? 0f : .4f));
                            Assert.That(Vector3.Dot(mesh.normals[offset], Vector3.up), Is.GreaterThan(.99f));
                        }
                        else
                        {
                            Assert.That(vertices[offset].y, Is.EqualTo(0f));
                            Assert.That(vertices[offset+1].y, Is.EqualTo(cell == 0 ? 0f : .4f));
                        }
                    }
                }
                Assert.That(scene.GetRootGameObjects()[0].GetComponentsInChildren<Collider>(), Is.Empty);
            }
            finally { view.Dispose(); EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void GroundPlatesBlendInFrontOfRecessesAndBehindRaisedBoxes()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var view = new ChunkedMapView(scene);
            try
            {
                var document = LevelDocument.Create(3, 1)
                    .WithCell(0, 0, JObject.Parse("{\"tileKey\":\"tile_start\"}"))
                    .WithCell(0, 1, JObject.Parse("{\"tileKey\":\"tile_hole\"}"))
                    .WithCell(0, 2, JObject.Parse("{\"tileKey\":\"tile_deepwater\"}"));
                view.Update(document, null);
                var mesh = view.MeshAt(0, 0);
                Assert.That(mesh.vertexCount, Is.EqualTo(72));
                Assert.That(mesh.subMeshCount, Is.EqualTo(2));
                var opaque = mesh.GetTriangles(0);
                var transparent = mesh.GetTriangles(1);
                Assert.That(opaque[0], Is.EqualTo(60));
                Assert.That(opaque[6], Is.EqualTo(20));
                Assert.That(transparent[0], Is.EqualTo(8));
                Assert.That(transparent[30], Is.EqualTo(64));
                Assert.That(transparent[36], Is.EqualTo(68));
                Assert.That(mesh.uv2[64].x, Is.EqualTo(31));
                Assert.That(mesh.uv2[68].x, Is.EqualTo(32));
            }
            finally
            {
                view.Dispose();
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [TestCase(-1f,.5f,1f,0f)]
        [TestCase(2f,.5f,-1f,0f)]
        [TestCase(.5f,-1f,0f,1f)]
        [TestCase(.5f,2f,0f,-1f)]
        public void HorizontalRayPicksAllFourExposedSides(float x, float z, float dx, float dz)
        {
            var doc = LevelDocument.Create(1, 1).WithCell(0, 0, JObject.Parse("{\"heightType\":\"HIGHLAND\"}"));
            Assert.That(ChunkedMapView.TryPick(doc, new Ray(new Vector3(x,.2f,z), new Vector3(dx,0,dz)), out var cell), Is.True);
            Assert.That(cell, Is.EqualTo(Vector2Int.zero));
        }

        [Test]
        public void PickingRespectsGapsNearestSideAndHeightInsteadOfUnderlyingGridPlane()
        {
            var low = JObject.Parse("{\"heightType\":\"LOWLAND\"}");
            var high = JObject.Parse("{\"heightType\":\"HIGHLAND\"}");
            var doc = LevelDocument.Create(2, 1).WithCell(0, 0, low).WithCell(0, 1, high);
            Assert.That(ChunkedMapView.TryPick(doc, new Ray(new Vector3(-1,.2f,.5f), Vector3.right), out var highCell), Is.True);
            Assert.That(highCell, Is.EqualTo(new Vector2Int(1, 0)));
            Assert.That(ChunkedMapView.TryPick(doc, new Ray(new Vector3(-1,-.06f,.5f), Vector3.right), out _), Is.False);
            Assert.That(ChunkedMapView.TryPick(doc, new Ray(new Vector3(1,2,.5f), Vector3.down), out _), Is.False);
            Assert.That(ChunkedMapView.TryPick(doc, new Ray(new Vector3(-1,-.2f,.5f), Vector3.right), out _), Is.False);
            Assert.That(ChunkedMapView.TryPick(doc, new Ray(new Vector3(-1,.5f,.5f), Vector3.right), out _), Is.False);
        }
    }
}
