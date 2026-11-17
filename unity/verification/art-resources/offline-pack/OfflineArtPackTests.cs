#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;

namespace ArkMapEditor.Verification
{
    public sealed class OfflineArtPackTests
    {
        private const string SceneKey = "scenes/obt/main/level_main_05-10/level_main_05-10";
        private const string Background = "arts/maps/map_lm_center/background_mesh/bg_lmc";
        private const string Effect = "battle/prefabs/effects/map_038_sign_glow_02";
        private const string Water = "battle/prefabs/effects/mapeff_waterternado_01";
        private const string Material = "arts/maps/map_lm_center/res/materials/mt_lmc_n_bg";
        private string root = "";
        private Catalog catalog = null!;

        [Serializable]
        private sealed class Catalog
        {
            public Entry[] assets = Array.Empty<Entry>();
            public Bundle[] bundles = Array.Empty<Bundle>();
        }

        [Serializable]
        private sealed class Entry
        {
            public string key = "";
            public string unityPath = "";
            public string bundle = "";
        }

        [Serializable]
        private sealed class Bundle
        {
            public string path = "";
            public string[] dependencies = Array.Empty<string>();
        }

        [SetUp]
        public void ReadInstalledCatalog()
        {
            root = Environment.GetEnvironmentVariable("ARK_OFFLINE_ART_PACK") ?? "";
            Assert.That(Directory.Exists(root), Is.True, "Set ARK_OFFLINE_ART_PACK to the isolated installation.");
            catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText(Path.Combine(root, "catalog.json")));
        }

        [Test]
        public void CatalogContainsUniqueAssetsAndAllDeclaredDependencies()
        {
            Assert.That(catalog.assets.Length, Is.GreaterThan(5));
            var bundles = catalog.bundles.Select(row => row.path).ToHashSet(StringComparer.Ordinal);
            Assert.That(bundles.Count, Is.EqualTo(catalog.bundles.Length));
            Assert.That(catalog.assets.Select(row => row.key).Distinct().Count(), Is.EqualTo(catalog.assets.Length));
            foreach (var row in catalog.assets)
                Assert.That(bundles.Contains(row.bundle), Is.True, row.key);
            foreach (var row in catalog.bundles)
            {
                Assert.That(row.dependencies.All(bundles.Contains), Is.True, row.path);
                Assert.That(File.Exists(Path.Combine(root, "Bundles", row.path)), Is.True, row.path);
            }
        }

        [Test]
        public void SceneBundleExposesOriginalScenePath()
        {
            WithBundle(SceneKey, (entry, bundle) =>
            {
                Assert.That(bundle.isStreamedSceneAssetBundle, Is.True);
                Assert.That(bundle.GetAllScenePaths(), Has.Member(entry.unityPath));
            });
        }

        [TestCase(Effect)]
        [TestCase(Water)]
        public void EffectLoadsAsGameObjectAndIsolatesInPreviewScene(string key)
        {
            WithBundle(key, (entry, bundle) =>
            {
                var prefab = bundle.LoadAsset<GameObject>(entry.unityPath);
                Assert.That(prefab, Is.Not.Null, entry.unityPath);
                var scene = EditorSceneManager.NewPreviewScene();
                GameObject? instance = null;
                try
                {
                    instance = UnityEngine.Object.Instantiate(prefab);
                    SceneManager.MoveGameObjectToScene(instance, scene);
                    Assert.That(instance.scene, Is.EqualTo(scene));
                    Assert.That(instance.transform, Is.Not.Null);
                    Assert.That(instance.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThan(0));
                }
                finally
                {
                    if (instance != null)
                        UnityEngine.Object.DestroyImmediate(instance);
                    EditorSceneManager.ClosePreviewScene(scene);
                }
                Assert.That(scene.IsValid(), Is.False);
            });
        }

        [UnityTest]
        public IEnumerator BackgroundLoadsViaOfficialAsyncApiAndUnloads()
        {
            var entry = Find(Background);
            var loaded = new System.Collections.Generic.List<AssetBundle>();
            var baseline = AssetBundle.GetAllLoadedAssetBundles().Count();
            try
            {
                foreach (var relative in LoadOrder(entry))
                {
                    var request = AssetBundle.LoadFromFileAsync(Path.Combine(root, "Bundles", relative));
                    while (!request.isDone)
                        yield return null;
                    Assert.That(request.assetBundle, Is.Not.Null, relative);
                    loaded.Add(request.assetBundle);
                }

                var asset = loaded.Last().LoadAssetAsync<GameObject>(entry.unityPath);
                while (!asset.isDone)
                    yield return null;
                var background = asset.asset as GameObject;
                Assert.That(background, Is.Not.Null);
                var renderer = background!.GetComponentInChildren<MeshRenderer>(true);
                Assert.That(renderer, Is.Not.Null);
                Assert.That(renderer.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(0));
            }
            finally
            {
                for (var i = loaded.Count - 1; i >= 0; i--)
                    loaded[i].Unload(true);
            }
            Assert.That(AssetBundle.GetAllLoadedAssetBundles().Count(), Is.EqualTo(baseline));
        }

        [Test]
        public void MaterialRetainsShaderAndReferencedTexture()
        {
            WithBundle(Material, (entry, bundle) =>
            {
                var material = bundle.LoadAsset<Material>(entry.unityPath);
                Assert.That(material, Is.Not.Null);
                Assert.That(material.shader, Is.Not.Null);
                Assert.That(material.GetTexturePropertyNames().Any(property => material.GetTexture(property) != null), Is.True);
            });
        }

        private Entry Find(string key) => catalog.assets.Single(row => row.key == key);

        private string[] LoadOrder(Entry entry)
        {
            var current = catalog.bundles.Single(row => row.path == entry.bundle);
            return current.dependencies.Where(path => path != entry.bundle).Distinct().Append(entry.bundle).ToArray();
        }

        private void WithBundle(string key, Action<Entry, AssetBundle> verify)
        {
            var entry = Find(key);
            var loaded = new System.Collections.Generic.List<AssetBundle>();
            var baseline = AssetBundle.GetAllLoadedAssetBundles().Count();
            try
            {
                foreach (var relative in LoadOrder(entry))
                {
                    var bundle = AssetBundle.LoadFromFile(Path.Combine(root, "Bundles", relative));
                    Assert.That(bundle, Is.Not.Null, relative);
                    loaded.Add(bundle);
                }
                verify(entry, loaded.Last());
            }
            finally
            {
                for (var i = loaded.Count - 1; i >= 0; i--)
                    loaded[i].Unload(true);
            }
            Assert.That(AssetBundle.GetAllLoadedAssetBundles().Count(), Is.EqualTo(baseline));
        }
    }
}
