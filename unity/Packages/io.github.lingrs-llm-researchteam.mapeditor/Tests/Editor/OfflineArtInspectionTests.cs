#nullable enable
using System;
using System.Linq;
using System.IO;
using System.Collections;
using System.Threading;
using Arknights.MapEditor.Editor;
using UnityEngine.TestTools;
using Arknights.MapEditor.Catalog;
using NUnit.Framework;
using UnityEngine;

namespace Arknights.MapEditor.EditorTests
{
    public sealed class OfflineArtInspectionTests
    {
        [Test]
        public void CatalogKeepsMaterialAtlasAndAnimationSeparate()
        {
            OfflineArtEntry Entry(string ext) => new OfflineArtEntry("arts/maps/test", "dyn/test" + ext, "x.ab", "map-art");
            Assert.That(Entry(".tga").SourceType, Is.EqualTo("贴图"));
            Assert.That(Entry(".mat").SourceType, Is.EqualTo("材质"));
            Assert.That(Entry(".anim").SourceType, Is.EqualTo("动画"));
            Assert.That(Entry(".asset").SourceType, Is.EqualTo("环境配置"));
        }

        [Test]
        public void ZoomKeepsPointerOverTheSameImagePositionAndPanStaysInsideFrame()
        {
            var pointer = new Vector2(80f, -50f);
            var oldPan = new Vector2(30f, 10f);
            var zoomed = OfflineArtSceneImageView.AnchoredPan(oldPan, pointer, 2f, 4f);
            Assert.That((pointer - zoomed) / 4f, Is.EqualTo((pointer - oldPan) / 2f));
            Assert.That(OfflineArtSceneImageView.ClampPan(new Vector2(500f, -400f), new Vector2(1000f, 200f),
                new Vector2(400f, 300f)), Is.EqualTo(new Vector2(300f, 0f)));
        }

        [Test]
        public void SceneFrameDecodesAtExpectedSizeAndRejectsWrongDimensions()
        {
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                var png = ImageConversion.EncodeToPNG(image);
                var decoded = OfflineArtScenePreview.DecodeFrame(png);
                try { Assert.That((decoded.width, decoded.height), Is.EqualTo((1280, 720))); }
                finally { UnityEngine.Object.DestroyImmediate(decoded); }

                png[16] = 127;
                Assert.Throws<InvalidDataException>(() => OfflineArtScenePreview.DecodeFrame(png));
            }
            finally { UnityEngine.Object.DestroyImmediate(image); }
        }

        [Test]
        public void SceneCacheChangesWhenSceneSourceOrBundleChanges()
        {
            var file = Path.GetTempFileName();
            try
            {
                var bundles = new[] { file };
                var original = OfflineArtScenePreview.CacheKey("2781", "map.unity", bundles, "worker-1");
                Assert.That(OfflineArtScenePreview.CacheKey("2781", "map.unity", bundles, "worker-1"), Is.EqualTo(original));
                Assert.That(OfflineArtScenePreview.CacheKey("2781", "other.unity", bundles, "worker-1"), Is.Not.EqualTo(original));
                Assert.That(OfflineArtScenePreview.CacheKey("2781", "map.unity", bundles, "worker-2"), Is.Not.EqualTo(original));
                File.WriteAllText(file, "changed bundle");
                Assert.That(OfflineArtScenePreview.CacheKey("2781", "map.unity", bundles, "worker-1"), Is.Not.EqualTo(original));
            }
            finally { File.Delete(file); }
        }

        [Test]
        public void BattleProjectionKeepsTheOriginalCameraHeadingAndScreenCenter()
        {
            var actor = new GameObject("Projection test");
            try
            {
                var view = actor.AddComponent<Camera>();
                var sourceOffset = new Vector3(-19f, 0f, 0f);
                OfflineArtBattleProjection.Apply(view, 12, 8, 16f / 9f, sourceOffset, true);
                Assert.That(view.orthographic, Is.False);
                Assert.That(view.fieldOfView, Is.EqualTo(40f));
                Assert.That(view.transform.right, Is.EqualTo(Vector3.right));
                Assert.That(view.transform.forward.x, Is.Zero.Within(.01f));
                Assert.That(Vector3.Distance(OfflineArtBattleProjection.ToPreviewOffset(new Vector3(0, -4, 0)),
                    new Vector3(0, 0, -4)), Is.LessThan(.001f));

                var focus = view.WorldToViewportPoint(new Vector3(0f, .18f, 0f));
                Assert.That(focus.x, Is.EqualTo(.5f).Within(.001f));
                Assert.That(focus.y, Is.EqualTo(.5f).Within(.001f));
            }
            finally { UnityEngine.Object.DestroyImmediate(actor); }
        }

        [UnityTest]
        public IEnumerator AllReflectionEntriesAreNativeCubemaps()
        {
            var root = Environment.GetEnvironmentVariable("ARK_OFFLINE_ART_PACK");
            if (string.IsNullOrWhiteSpace(root)) Assert.Ignore("Set ARK_OFFLINE_ART_PACK to the installed art pack.");
            var catalog = OfflineArtCatalog.Open(root!);
            var entries = catalog.Entries.Where(entry => entry.Category == OfflineArtCategory.Lighting && !entry.RequiresMetadata).ToArray();
            Assert.That(entries.Length, Is.EqualTo(18));
            var baseline = AssetBundle.GetAllLoadedAssetBundles().Count();
            foreach (var entry in entries)
            {
                using var bundles = new OfflineArtBundles();
                var loading = bundles.LoadAsync(catalog, new[] { entry }, CancellationToken.None);
                yield return new WaitUntil(() => loading.IsCompleted);
                loading.GetAwaiter().GetResult();
                var asset = bundles.Load(entry);
                Assert.That(bundles.Contains(entry), Is.True, entry.Key);
                Assert.That(asset, Is.TypeOf<Cubemap>(), entry.Key);
                Assert.That(((Cubemap)asset!).width, Is.GreaterThan(0), entry.Key);
            }
            Assert.That(AssetBundle.GetAllLoadedAssetBundles().Count(), Is.EqualTo(baseline));
        }

        [Test]
        public void InstalledPackHasMetadataForEveryControllerAndScriptableProfile()
        {
            var root = Environment.GetEnvironmentVariable("ARK_OFFLINE_ART_PACK");
            if (string.IsNullOrWhiteSpace(root)) Assert.Ignore("Set ARK_OFFLINE_ART_PACK to the installed art pack.");
            var catalog = OfflineArtCatalog.Open(root!);
            var index = OfflineArtMetadata.Open(catalog);
            var records = catalog.Entries.Where(entry => entry.RequiresMetadata).ToArray();
            Assert.That(records.Length, Is.EqualTo(318));
            foreach (var entry in records)
                Assert.That(index.TryGet(entry, out _), Is.True, entry.Key);
            var camera = records.Single(entry => entry.Key.EndsWith("/act55side_camera_move_plugin", StringComparison.Ordinal));
            Assert.That(index.TryGet(camera, out var record), Is.True);
            var motion = OfflineArtCameraDemo.From(record);
            Assert.That(motion.Duration, Is.EqualTo(3f));
            Assert.That(motion.Offset, Is.EqualTo(new Vector3(-19f, 0f, 0f)));
            Assert.That(motion.OffsetAt(0f), Is.EqualTo(motion.Offset));
            Assert.That(motion.OffsetAt(3f), Is.EqualTo(Vector3.zero));
            Assert.That(motion.ReferenceOffset, Is.False);
        }
    }
}
