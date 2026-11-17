#nullable enable
using System;
using System.IO;
using System.Collections;
using System.Threading;
using Arknights.MapEditor.Editor;
using UnityEngine;
using UnityEngine.TestTools;
using System.Linq;
using Arknights.MapEditor.Catalog;
using NUnit.Framework;

namespace Arknights.MapEditor.EditorTests
{
    public sealed class OfflineArtCatalogTests
    {
        [TestCase("../cross.ab")]
        [TestCase("file:escape.ab")]
        [TestCase("C:/escape.ab")]
        [TestCase("nested/../escape.ab")]
        [TestCase("/escape.ab")]
        public void RejectsBundlePathsOutsidePack(string path)
        {
            Assert.Throws<InvalidDataException>(() => OfflineArtCatalog.ValidateBundlePath(path));
        }

        [Test]
        public void ReadsFixedWindowsLz4CatalogAndResolvesDependenciesBeforeRoot()
        {
            var catalog = OfflineArtCatalog.Parse(Manifest("b.ab", "deps/a.ab"), Path.GetTempPath());
            var entry = catalog.Entries.Single();
            Assert.That(entry.Category, Is.EqualTo(OfflineArtCategory.Surface));
            Assert.That(catalog.LoadOrder(new[] { entry, entry }), Is.EqualTo(new[] { "deps/a.ab", "b.ab" }));
            Assert.That(catalog.ResourceVersion, Is.EqualTo("test-version"));
        }

        [Test]
        public void RejectsUndeclaredDependencies()
        {
            Assert.Throws<InvalidDataException>(() => OfflineArtCatalog.Parse(
                Manifest("b.ab", "absent.ab"), Path.GetTempPath()));
        }

        [Test]
        public void UniformSamplingIncludesBothEndsAndHasDeterministicMiddle()
        {
            Assert.That(OfflineArtGallery.SampleIndices(10, 4), Is.EqualTo(new[] { 0, 3, 6, 9 }));
            Assert.That(OfflineArtGallery.SampleIndices(6, 3), Is.EqualTo(new[] { 0, 2, 5 }));
            Assert.That(OfflineArtGallery.SampleIndices(2, 4), Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void RealPackHasAllGalleryStrataAndFixedVersion()
        {
            var path = Environment.GetEnvironmentVariable("ARK_OFFLINE_ART_PACK");
            if (string.IsNullOrWhiteSpace(path)) Assert.Ignore("Set ARK_OFFLINE_ART_PACK to test installed data.");
            var catalog = OfflineArtCatalog.Open(path!);
            var samples = OfflineArtGallery.Select(catalog);
            Assert.That(catalog.Entries.Count, Is.EqualTo(7874));
            Assert.That(samples.GroupBy(row => row.Layer).Select(group => group.Count()), Is.EqualTo(new[] { 3, 3, 3, 3 }));
            Assert.That(samples.Select(sample => sample.Entry.Key).Distinct().Count(), Is.EqualTo(12));
            Assert.That(catalog.ResourceVersion, Is.EqualTo("26-10-20-11-10-38-04170990"));
        }

        [UnityTest]
        public IEnumerator InstalledGalleryLoadsNativeObjectsAndReleasesOwnedBundles()
        {
            var pack = Environment.GetEnvironmentVariable("ARK_OFFLINE_ART_PACK");
            if (string.IsNullOrWhiteSpace(pack)) Assert.Ignore("Set ARK_OFFLINE_ART_PACK to test installed data.");
            var catalog = OfflineArtCatalog.Open(pack!);
            var samples = OfflineArtGallery.Select(catalog);
            var baseline = AssetBundle.GetAllLoadedAssetBundles().Count();
            using (var lease = new OfflineArtBundles())
            {
                var loading = lease.LoadAsync(catalog, samples.SelectMany(sample => sample.ModelMaterial == null
                    ? new[] { sample.Entry } : new[] { sample.Entry, sample.ModelMaterial! }), CancellationToken.None);
                yield return new UnityEngine.WaitUntil(() => loading.IsCompleted);
                loading.GetAwaiter().GetResult();
                using var scene = new OfflineArtPreviewScene();
                foreach (var sample in samples)
                {
                    var location = OfflineArtPreviewScene.Position((int)sample.Layer, sample.Column);
                    var asset = lease.Load(sample.Entry);
                    var material = sample.ModelMaterial == null ? null : lease.Load(sample.ModelMaterial) as Material;
                    Assert.That(asset, Is.Not.Null, sample.Entry.Key);
                    if (sample.ModelMaterial != null) Assert.That(material, Is.Not.Null, sample.ModelMaterial.Key);
                    scene.AddTile(location, sample.Entry.Key);
                    Assert.That(scene.AddAsset(asset!, material, location, sample.Entry.Key,
                        sample.Layer == OfflineArtGalleryLayer.Effect), Is.True, sample.Entry.Key);
                }
                Assert.That(scene.RenderedAssets, Is.EqualTo(12));
            }
            Assert.That(AssetBundle.GetAllLoadedAssetBundles().Count(), Is.EqualTo(baseline));
        }

        [Test]
        public void GalleryTilesUseEqualGridSpacingAndDefaultLevelData()
        {
            var map = OfflineArtGallery.CreateMap();
            Assert.That((map.Width, map.Height), Is.EqualTo((3, 4)));
            var first = OfflineArtPreviewScene.Position(0, 0);
            var sameRow = OfflineArtPreviewScene.Position(0, 1);
            var nextRow = OfflineArtPreviewScene.Position(1, 0);
            Assert.That(sameRow.x - first.x, Is.EqualTo(nextRow.z - first.z));
            Assert.That(nextRow.y, Is.GreaterThan(first.y));
        }
        private static string Manifest(string bundle, string dependency) => $@"{{
          ""formatVersion"":1,""gameBaseline"":""PC 2.7.81"",""platform"":""Windows"",""resourceVersion"":""test-version"",
          ""compression"":""LZ4"",""unityVersion"":""2021.3.39f1"",
          ""assets"":[{{""key"":""arts/maps/test/res/materials/mt_test"",""kind"":""map-art"",""unityPath"":""dyn/mt_test.mat"",""bundle"":""{bundle}""}}],
          ""bundles"":[{{""path"":""deps/a.ab"",""dependencies"":[]}},{{""path"":""{bundle}"",""dependencies"":[""{dependency}""]}}]
        }}";
    }
}
