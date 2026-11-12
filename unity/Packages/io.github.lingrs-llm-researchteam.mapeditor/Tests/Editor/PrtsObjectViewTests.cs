using System.IO;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Arknights.MapEditor.Tests
{
    public sealed class PrtsObjectViewTests
    {
        [Test]
        public void CompleteOverviewContainsEveryTileAndIconAtDistinctPositions()
        {
            const string root = "Packages/io.github.lingrs-llm-researchteam.mapeditor/";
            var document = LevelDocument.Parse(File.ReadAllText(root + "Samples~/CompletePrtsGallery/level.json"));
            var index = JObject.Parse(File.ReadAllText(root + "Samples~/CompletePrtsGallery/index.json"));
            Assert.That(document.Width, Is.EqualTo(72)); Assert.That(document.Height, Is.EqualTo(34));
            var positions = new System.Collections.Generic.HashSet<Vector2Int>();
            var tiles = new System.Collections.Generic.HashSet<string>();
            var objects = new System.Collections.Generic.HashSet<string>();
            foreach (var entry in (JArray)index["entries"])
            {
                var row = (int)entry["row"]; var col = (int)entry["col"]; var id = (string)entry["id"];
                Assert.That(positions.Add(new Vector2Int(col,row)), Is.True, id);
                if ((string)entry["section"] == "terrain")
                {
                    var tile = document.GetCell(row, col);
                    Assert.That((string)tile["tileKey"], Is.EqualTo(id));
                    Assert.That(SkeletonStyle.Resolve(tile).Known, Is.True, id);
                    tiles.Add(id);
                }
                else { objects.Add(id); }
            }
            Assert.That(tiles.Count, Is.EqualTo(120)); Assert.That(objects.Count, Is.EqualTo(1056));
            var scene = EditorSceneManager.NewPreviewScene();
            using (var view = new PrtsObjectView(scene))
            {
                try
                {
                    view.Update(document);
                    Assert.That(view.VisibleCount, Is.EqualTo(1056));
                    Assert.That(view.UnresolvedMarkers, Is.Empty);
                    var renderer = scene.GetRootGameObjects()[0].GetComponent<MeshRenderer>();
                    Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(2));
                    Assert.That(renderer.sharedMaterials[0].mainTexture.name, Is.EqualTo("prts-object-atlas"));
                    Assert.That(renderer.sharedMaterials[1].mainTexture.name, Is.EqualTo("prts-object-supplement-atlas"));
                    var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    Assert.That(mesh.GetIndexCount(0), Is.EqualTo(685 * 6));
                    Assert.That(mesh.GetIndexCount(1), Is.EqualTo(371 * 6));
                }
                finally { view.Dispose(); EditorSceneManager.ClosePreviewScene(scene); }
            }
        }

        [Test]
        public void EveryOfficialIconHasDistinctBoundedAtlasRegionAndNativeGeometry()
        {
            Assert.That(PrtsObjectView.IconCount, Is.EqualTo(685));
            var catalog = JObject.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(PrtsObjectView.CatalogRoot + "prts-object-icons.json").text);
            var regions = new System.Collections.Generic.HashSet<Rect>();
            foreach (var entry in ((JObject)catalog["icons"]).Properties())
            {
                Assert.That(PrtsObjectView.TryGetRect(entry.Name, out var rect), Is.True, entry.Name);
                Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(0)); Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(0));
                Assert.That(rect.xMax, Is.LessThanOrEqualTo(1)); Assert.That(rect.yMax, Is.LessThanOrEqualTo(1));
                Assert.That(regions.Add(rect), Is.True, entry.Name);
            }
            var path = "Packages/io.github.lingrs-llm-researchteam.mapeditor/Samples~/ObjectGallery/level.json";
            var document = LevelDocument.Parse(File.ReadAllText(path));
            var before = document.ToJson(false);
            var scene = EditorSceneManager.NewPreviewScene();
            var view = new PrtsObjectView(scene);
            try
            {
                view.Update(document); Assert.That(view.VisibleCount, Is.EqualTo(685));
                Assert.That(view.UnresolvedMarkers, Is.Empty);
                var roots = scene.GetRootGameObjects(); Assert.That(roots.Length, Is.EqualTo(1));
                var mesh = roots[0].GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh.vertexCount, Is.EqualTo(685 * 4));
                view.Dispose(); Assert.That(mesh == null, Is.True);
                Assert.That(scene.GetRootGameObjects(), Is.Empty);
                Assert.That(document.ToJson(false), Is.EqualTo(before));
            }
            finally { view.Dispose(); EditorSceneManager.ClosePreviewScene(scene); }
        }
        [Test]
        public void PresetOperatorsUseFixedNativeAtlasAlongsideDevices()
        {
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(PrtsObjectView.CatalogRoot + "preset-operator-icons.json");
            Assert.That(text, Is.Not.Null);
            var catalog = JObject.Parse(text.text);
            Assert.That((string)catalog["client"], Is.EqualTo("2.7.81"));
            Assert.That((string)catalog["resource"], Is.EqualTo("26-10-22-14-02-11-51447808"));
            var entries = new JArray();
            foreach (var icon in ((JObject)catalog["icons"]).Properties())
            {
                Assert.That(PrtsObjectView.TryGetRect(icon.Name, out _), Is.True, icon.Name);
                entries.Add(new JObject
                {
                    ["position"] = new JObject { ["row"] = entries.Count / 22, ["col"] = entries.Count % 22 },
                    ["inst"] = new JObject { ["characterKey"] = icon.Name }
                });
            }
            Assert.That(entries.Count, Is.EqualTo(461));
            var presets = new JObject
            {
                ["characterInsts"] = entries,
                ["tokenInsts"] = JArray.Parse("[{\"position\":{\"row\":21,\"col\":21},\"inst\":{\"characterKey\":\"trap_001_crate\"}}]")
            };
            var document = LevelDocument.Create(22, 22).WithSection("predefines", presets.ToString());
            var scene = EditorSceneManager.NewPreviewScene();
            using var view = new PrtsObjectView(scene);
            try
            {
                view.Update(document);
                Assert.That(view.VisibleCount, Is.EqualTo(462));
                Assert.That(view.UnresolvedMarkers, Is.Empty);
                var renderer = scene.GetRootGameObjects()[0].GetComponent<MeshRenderer>();
                Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(3));
                Assert.That(renderer.sharedMaterials[0].mainTexture.name, Is.EqualTo("prts-object-atlas"));
                Assert.That(renderer.sharedMaterials[1].mainTexture.name, Is.EqualTo("preset-operator-atlas"));
                Assert.That(renderer.sharedMaterials[2].mainTexture.name, Is.EqualTo("preset-operator-extra-atlas"));
                Assert.That(renderer.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(1), Is.EqualTo(223 * 6));
                Assert.That(renderer.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(2), Is.EqualTo(238 * 6));
            }
            finally { view.Dispose(); EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void HiddenUnknownAndCardsNeverBecomeOrdinaryVisibleEntities()
        {
            var document = LevelDocument.Parse("{\"mapData\":{\"map\":[[0,0]],\"tiles\":[{\"tileKey\":\"tile_road\"}]},\"predefines\":{\"tokenInsts\":[{\"position\":{\"row\":0,\"col\":0},\"hidden\":true,\"inst\":{\"characterKey\":\"trap_001_crate\"}},{\"position\":{\"row\":0,\"col\":1},\"inst\":{\"characterKey\":\"unknown\"}}],\"tokenCards\":[{\"inst\":{\"characterKey\":\"trap_001_crate\"}}]}}");
            var scene = EditorSceneManager.NewPreviewScene();
            using (var view = new PrtsObjectView(scene))
            {
                try { view.Update(document); Assert.That(view.VisibleCount, Is.Zero); Assert.That(view.UnresolvedMarkers.Count, Is.EqualTo(2)); }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
        }
    }
}
