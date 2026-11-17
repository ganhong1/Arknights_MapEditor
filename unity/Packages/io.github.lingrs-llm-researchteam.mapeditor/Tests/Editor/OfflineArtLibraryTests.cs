#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Arknights.MapEditor.EditorTests
{
    public sealed class OfflineArtLibraryTests
    {
        private string directory = null!;
        private MapEditorWindow owner = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "ark-art-library-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, "Bundles"));
            File.WriteAllText(Path.Combine(directory, "catalog.json"), new JObject
            {
                ["formatVersion"] = 1, ["gameBaseline"] = "PC 2.7.81", ["platform"] = "Windows",
                ["resourceVersion"] = "test", ["compression"] = "LZ4", ["unityVersion"] = "2021.3.39f1",
                ["bundles"] = new JArray(new JObject { ["path"] = "test.ab", ["dependencies"] = new JArray() }),
                ["assets"] = new JArray(Entry("level_main_05-10", "scene", ".unity"),
                    Entry("electric_tower", "map-art", ".fbx"), Entry("absin_skill", "effect", ".prefab"))
            }.ToString());
            owner = ScriptableObject.CreateInstance<MapEditorWindow>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(owner);
            Directory.Delete(directory, true);
        }

        [TestCase("", "environment-pack", "environment-pack")]
        [TestCase(" ", "environment-pack", "environment-pack")]
        [TestCase("saved-pack", "environment-pack", "saved-pack")]
        [TestCase("", null, "")]
        public void PackLocationUsesEnvironmentOnlyWithoutASavedSelection(string saved, string? environment, string expected)
        {
            Assert.That(MapEditorWindow.ResolveArtPackFolder(saved, environment), Is.EqualTo(expected));
        }

        [Test]
        public void AutomaticConnectionPersistsVerifiedLocationAndInitiallyListsEveryCategory()
        {
            var root = new VisualElement();
            var saved = "";
            var session = Field<DocumentSession>("session");
            var document = session.Json;
            var draft = Field<string>("draft");
            var connect = owner.BuildArtLibrary(root, () => directory, path => saved = path);
            connect();

            Assert.That(saved, Is.EqualTo(Path.GetFullPath(directory)), "Environment-provided paths must survive a later launch.");
            Assert.That(root.Q<PopupField<string>>("offline-art-category").value, Is.EqualTo("全部分类"));
            Assert.That(Items(root).Select(entry => entry.Category), Is.EquivalentTo(new[]
                { OfflineArtCategory.Scene, OfflineArtCategory.Model, OfflineArtCategory.Effect }));
            Assert.That(root.Q<TextField>("offline-art-query").enabledSelf, Is.True);
            Assert.That(root.Q<HelpBox>("offline-art-empty").style.display.value, Is.EqualTo(DisplayStyle.None));

            var reopened = new VisualElement();
            owner.BuildArtLibrary(reopened, () => MapEditorWindow.ResolveArtPackFolder(saved, null), path => saved = path)();
            Assert.That(Items(reopened).Length, Is.EqualTo(3));
            Assert.That(session.Json, Is.EqualTo(document));
            Assert.That(Field<string>("draft"), Is.EqualTo(draft));
            Assert.That(owner.hasUnsavedChanges, Is.False);
        }

        [Test]
        public void UnconnectedLibraryExplainsEmptyListAndDisablesSearch()
        {
            var root = new VisualElement();
            var location = "";
            var saved = "";
            var activate = owner.BuildArtLibrary(root, () => location, path => saved = path);
            activate();
            Assert.That(root.Q<TextField>("offline-art-query").enabledSelf, Is.False);
            Assert.That(root.Q<HelpBox>("offline-art-empty").text, Does.Contain("尚未连接美术包"));
            Assert.That(root.Q<Button>("offline-art-gallery").enabledSelf, Is.False);
            Assert.That(saved, Is.Empty, "An unverified path must not be saved.");

            location = directory;
            activate();
            Assert.That(Items(root).Length, Is.EqualTo(3), "A previously unconfigured library must retry on activation.");
            Assert.That(root.Q<TextField>("offline-art-query").enabledSelf, Is.True);
        }

        [Test]
        public void IncrementalArtSharesOneListAndUsesUpdatedEntriesByKey()
        {
            var update = Path.Combine(directory, "update");
            Directory.CreateDirectory(Path.Combine(update, "Bundles"));
            var data = JObject.Parse(File.ReadAllText(Path.Combine(directory, "catalog.json")));
            data["gameBaseline"] = OfflineArtCatalog.Terrain2791;
            var replacement = Entry("electric_tower", "map-art", ".fbx");
            replacement["searchTerms"] = "updated";
            data["assets"] = new JArray(replacement, Entry("new_device", "effect", ".prefab"));
            File.WriteAllText(Path.Combine(update, "catalog.json"), data.ToString());

            var root = new VisualElement();
            owner.BuildArtLibrary(root, () => directory, _ => { }, () => update, _ => { })();
            Assert.That(root.Q("offline-art-game-version"), Is.Null);
            Assert.That(Items(root).Length, Is.EqualTo(4));
            Assert.That(Items(root).Single(entry => entry.Key == "test/electric_tower").SearchTerms, Is.EqualTo("updated"));
            Assert.That(Items(root).Any(entry => entry.Key == "test/new_device"), Is.True);
        }

        [UnityTest]
        public IEnumerator SearchFindsModelsAcrossCategoriesAndClearingRestoresTheList()
        {
            var host = ScriptableObject.CreateInstance<ArtLibraryHost>();
            try
            {
                host.ShowUtility();
                var root = host.rootVisualElement;
                owner.BuildArtLibrary(root, () => directory, _ => { })();
                var query = root.Q<TextField>("offline-art-query");
                var category = root.Q<PopupField<string>>("offline-art-category");
                query.value = "electric_tower";
                yield return SearchFinished(root);
                Assert.That(Items(root).Select(entry => entry.Key), Is.EqualTo(new[] { "test/electric_tower" }));
                query.value = "";
                Assert.That(Items(root).Length, Is.EqualTo(3));

                category.value = MapEditorWindow.CategoryName(OfflineArtCategory.Effect);
                Assert.That(Items(root).Single().Key, Is.EqualTo("test/absin_skill"));
                query.value = "no_such_art_zzz";
                yield return SearchFinished(root);
                Assert.That(Items(root), Is.Empty);
                Assert.That(root.Q<HelpBox>("offline-art-empty").text, Does.Contain("没有匹配资源"));
                query.value = "";
                Assert.That(Items(root).Single().Key, Is.EqualTo("test/absin_skill"));
                category.value = "全部分类";
                Assert.That(Items(root).Length, Is.EqualTo(3));
            }
            finally { Object.DestroyImmediate(host); }
        }

        private static IEnumerator SearchFinished(VisualElement root)
        {
            var deadline = EditorApplication.timeSinceStartup + 12;
            while (root.Q<Label>("offline-art-count").text == "正在搜索…" && EditorApplication.timeSinceStartup < deadline)
                yield return null;
            Assert.That(root.Q<Label>("offline-art-count").text, Does.Not.Contain("正在搜索"));
            Assert.That(root.Q<Label>("offline-art-count").text, Does.Not.Contain("搜索暂不可用"));
        }

        private T Field<T>(string name) => (T)typeof(MapEditorWindow).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

        private static OfflineArtEntry[] Items(VisualElement root) =>
            root.Q<ListView>("offline-art-items").itemsSource.Cast<OfflineArtEntry>().ToArray();

        private static JObject Entry(string name, string kind, string extension) => new JObject
        {
            ["key"] = "test/" + name, ["kind"] = kind,
            ["unityPath"] = "dyn/test/" + name + extension, ["bundle"] = "test.ab"
        };

        public sealed class ArtLibraryHost : EditorWindow { }
    }
}
