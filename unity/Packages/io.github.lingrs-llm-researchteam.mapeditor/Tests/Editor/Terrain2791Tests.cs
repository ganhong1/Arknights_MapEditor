#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arknights.MapEditor.Tests
{
    public sealed class Terrain2791Tests
    {
        private static readonly string[] Keys = { "tile_acbag", "tile_acempty", "tile_duel_grassland", "tile_speedup", "tile_trench" };

        [Test]
        public void NewTerrainRoundTripsOriginalConfigurationsAndHasMatchingSkeletons()
        {
            var baseline = EditorCatalog.LoadBaseline();
            var expanded = EditorCatalog.LoadBundled();
            Assert.That(expanded.Entries.Select(entry => entry.Id).Except(baseline.Entries.Select(entry => entry.Id)), Is.EquivalentTo(Keys));
            Assert.That(expanded.Find("tile_duel_grassland")!.Name, Is.EqualTo("隐匿模块"));
            Assert.That(expanded.Find("tile_speedup")!.Name, Is.EqualTo("优化模块"));
            Assert.That(expanded.Find("tile_trench")!.Name, Is.EqualTo("战壕"));

            var portraits = TerrainPortraitCatalog.Load();
            var samples = TerrainSampleCatalog.Load2791();
            var document = LevelDocument.Create(Keys.Length, 1);
            var sourceCounts = new[] { 30, 10, 4, 4, 22 };
            for (var col = 0; col < Keys.Length; col++)
            {
                var key = Keys[col];
                var config = samples.Default(key)!;
                Assert.That(TerrainBlackboardCatalog.Template(key)?.Value<string>("tileKey"), Is.EqualTo(key));
                Assert.That(portraits.Find(key).Image, Is.Not.Null, key);
                Assert.That(SkeletonStyle.Resolve(config).Known, Is.True, key);
                Assert.That(SkeletonStyle.Resolve(config).Label, Is.Empty, key);
                var source = samples.Samples(key)[0];
                Assert.That(source.SourceCount, Is.EqualTo(sourceCounts[col]), key);
                Assert.That(new TerrainSourceMap(source.ExamplePath, key, source.Number, samples.Revision).HighlightCount, Is.Positive);
                document = document.WithCell(0, col, config);
            }
            var readback = LevelDocument.Parse(document.ToJson());
            for (var col = 0; col < Keys.Length; col++)
            {
                var actual = readback.GetCell(0, col).ToString(Formatting.None);
                var expected = samples.Default(Keys[col])!.ToString(Formatting.None);
                Assert.That(actual, Is.EqualTo(expected), Keys[col]);
            }
            Assert.That(SkeletonStyle.SurfaceHeight(readback.GetCell(0, 4)), Is.EqualTo(-.4f));
            Assert.That(SkeletonStyle.Resolve(readback.GetCell(0, 1)).Pattern, Is.EqualTo(1));
            var trenchSamples = samples.Samples("tile_trench");
            Assert.That(trenchSamples.Count, Is.EqualTo(2));
            var secondTrench = trenchSamples[1].CopyTile();
            var export = LevelDocument.Parse(document.WithCell(0, 4, secondTrench).ToJson());
            Assert.That(export.GetCell(0, 4).ToString(Formatting.None), Is.EqualTo(secondTrench.ToString(Formatting.None)));
        }

        [UnityTest]
        public IEnumerator SupplementalArtPackLoadsRealAssetsAndRendersTheTrenchScene()
        {
            var path = Environment.GetEnvironmentVariable("ARK_OFFLINE_ART_PACK_2791");
            if (string.IsNullOrWhiteSpace(path)) Assert.Ignore("Set ARK_OFFLINE_ART_PACK_2791 to test installed data.");
            var catalog = OfflineArtCatalog.Open(path!);
            Assert.That(catalog.GameBaseline, Is.EqualTo(OfflineArtCatalog.Terrain2791));
            Assert.That(catalog.ResourceVersion, Is.EqualTo("26-11-13-06-34-18-84612175"));
            Assert.That(catalog.Entries.SelectMany(row => Keys.Where(id => row.SearchTerms.Contains(id))).Distinct(),
                Is.EquivalentTo(Keys.Where(id => id != "tile_acempty")));
            var oldCount = AssetBundle.GetAllLoadedAssetBundles().Count();
            foreach (var asset in catalog.Entries.Where(item => item.Category != OfflineArtCategory.Scene))
            {
                using var bundles = new OfflineArtBundles();
                var load = bundles.LoadAsync(catalog, new[] { asset }, CancellationToken.None);
                yield return new WaitUntil(() => load.IsCompleted);
                load.GetAwaiter().GetResult();
                Assert.That(bundles.Contains(asset), Is.True, asset.Key);
                using var preview = new OfflineArtPreviewScene();
                var item = bundles.Load(asset);
                Assert.That(item, Is.Not.Null, asset.Key);
                Assert.That(preview.AddAsset(item!, null, Vector3.zero, asset.Key, asset.Category == OfflineArtCategory.Effect),
                    Is.True, asset.Key);
            }
            Assert.That(AssetBundle.GetAllLoadedAssetBundles().Count(), Is.EqualTo(oldCount));
            var scene = catalog.Entries.Single(item => item.Category == OfflineArtCategory.Scene);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var render = OfflineArtScenePreview.RenderAsync(catalog, scene, timeout.Token);
            yield return new WaitUntil(() => render.IsCompleted);
            var result = render.GetAwaiter().GetResult();
            try { Assert.That(result.Image.width, Is.Positive); }
            finally { UnityEngine.Object.DestroyImmediate(result.Image); }
        }
    }
}
