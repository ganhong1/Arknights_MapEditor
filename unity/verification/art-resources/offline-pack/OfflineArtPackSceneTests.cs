#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ArkMapEditor.Verification
{
    public sealed class OfflineArtPackSceneTests
    {
        private const string SceneKey = "scenes/obt/main/level_main_05-10/level_main_05-10";

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

        [UnityTest]
        public IEnumerator OriginalSceneRendersFromOfflinePackageAndUnloads()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null));
            var root = Environment.GetEnvironmentVariable("ARK_OFFLINE_ART_PACK") ?? "";
            var catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText(Path.Combine(root, "catalog.json")));
            var entry = catalog.assets.Single(row => row.key == SceneKey);
            var bundleEntry = catalog.bundles.Single(row => row.path == entry.bundle);
            var originalScene = SceneManager.GetActiveScene();
            var baseline = AssetBundle.GetAllLoadedAssetBundles().Count();
            var loaded = new List<AssetBundle>();
            var scene = default(Scene);
            try
            {
                foreach (var relative in bundleEntry.dependencies.Where(path => path != entry.bundle).Distinct().Append(entry.bundle))
                {
                    var request = AssetBundle.LoadFromFileAsync(Path.Combine(root, "Bundles", relative));
                    while (!request.isDone)
                        yield return null;
                    Assert.That(request.assetBundle, Is.Not.Null, relative);
                    loaded.Add(request.assetBundle);
                }
                Assert.That(loaded.Last().GetAllScenePaths(), Has.Member(entry.unityPath));
                var sceneName = Path.GetFileNameWithoutExtension(entry.unityPath);
                Assert.That(SceneManager.GetSceneByName(sceneName).IsValid(), Is.False, sceneName);
                var loadScene = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                Assert.That(loadScene, Is.Not.Null);
                yield return loadScene;
                scene = SceneManager.GetSceneByName(sceneName);
                Assert.That(scene.IsValid() && scene.isLoaded, Is.True);
                Assert.That(SceneManager.SetActiveScene(scene), Is.True);
                var roots = scene.GetRootGameObjects();
                Assert.That(roots, Has.Length.EqualTo(8));
                var renderers = roots.SelectMany(item => item.GetComponentsInChildren<Renderer>(true)).ToArray();
                Assert.That(renderers, Has.Length.EqualTo(183));
                Assert.That(renderers.Count(item => item.lightmapIndex >= 0 && item.lightmapIndex < LightmapSettings.lightmaps.Length),
                    Is.EqualTo(179));

                var camera = roots.SelectMany(item => item.GetComponentsInChildren<Camera>(true)).First();
                var colors = Render(camera);
                Assert.That(colors.Select(pixel => (pixel.r, pixel.g, pixel.b)).Distinct().Count(), Is.GreaterThan(100));
                Assert.That(colors.Count(pixel => pixel.r > 250 && pixel.b > 250 && pixel.g < 5), Is.LessThan(colors.Length / 20));

                Assert.That(SceneManager.SetActiveScene(originalScene), Is.True);
                yield return SceneManager.UnloadSceneAsync(scene);
                Assert.That(scene.isLoaded, Is.False);
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded)
                {
                    SceneManager.SetActiveScene(originalScene);
                    SceneManager.UnloadSceneAsync(scene);
                }
                for (var index = loaded.Count - 1; index >= 0; index--)
                    loaded[index].Unload(true);
            }
            Assert.That(AssetBundle.GetAllLoadedAssetBundles().Count(), Is.EqualTo(baseline));
        }

        private static Color32[] Render(Camera camera)
        {
            var background = RenderTexture.active;
            var previousTarget = camera.targetTexture;
            var previousRect = camera.rect;
            var previousAspect = camera.aspect;
            var target = RenderTexture.GetTemporary(320, 180, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(320, 180, TextureFormat.RGBA32, false);
            try
            {
                camera.rect = new Rect(0, 0, 1, 1);
                camera.aspect = 16f / 9f;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 320, 180), 0, 0);
                return image.GetPixels32();
            }
            finally
            {
                RenderTexture.active = background;
                camera.targetTexture = previousTarget;
                camera.rect = previousRect;
                camera.aspect = previousAspect;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }
}
