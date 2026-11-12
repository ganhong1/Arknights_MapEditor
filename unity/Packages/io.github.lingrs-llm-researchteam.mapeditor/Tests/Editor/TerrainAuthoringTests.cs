#nullable enable
using System.Linq;
using System.Collections;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Tests
{
    public sealed class TerrainAuthoringTests
    {
        [Test]
        public void VerifiedChoicesUseTheSpecificTileAndKeepUnknownRulesAsReference()
        {
            var fields = new TerrainBlackboardFields((_, __, ___) => Assert.Fail("Displaying rules must not write."));
            fields.Show(JObject.Parse("{\"tileKey\":\"tile_woodrd\",\"blackboard\":null}"));
            var choice = fields.Q<PopupField<string>>("terrain-blackboard-choice-dynamic");
            Assert.That(choice.choices.Count, Is.EqualTo(10));
            Assert.That(choice.value, Does.Contain("未设置"));

            fields.Show(JObject.Parse("{\"tileKey\":\"tile_steam\",\"blackboard\":[]}"));
            Assert.That(fields.Q("terrain-blackboard-rule-range_id"), Is.Not.Null);
            Assert.That(fields.Q("terrain-blackboard-choice-range_id"), Is.Null);

            fields.Show(JObject.Parse("{\"tileKey\":\"tile_character\",\"blackboard\":[]}"));
            Assert.That(fields.Q("terrain-blackboard-rule-act49side_boss_level_tile_mark"), Is.Not.Null);
            Assert.That(fields.Q("terrain-blackboard-number-act49side_boss_level_tile_mark"), Is.Null);
        }

        [Test]
        public void StructuredWritePreservesUnrelatedPairsAndRejectsAmbiguousKeys()
        {
            var tile = JObject.Parse("{\"blackboard\":[{\"key\":\"mode\",\"value\":1.0,\"valueStr\":null,\"extension\":7},{\"key\":\"other\",\"value\":-99}]}");
            var untouched = tile["blackboard"]![1]!.DeepClone();
            Assert.That(TerrainBlackboardCatalog.SetValue(tile, "mode", "value", new JValue(2)), Is.True);
            Assert.That(tile["blackboard"]![0]!.Value<int>("extension"), Is.EqualTo(7));
            Assert.That(JToken.DeepEquals(tile["blackboard"]![1], untouched), Is.True);

            var board = (JArray)tile["blackboard"]!;
            board.Add(board[0]!.DeepClone());
            var snapshot = tile.DeepClone();
            Assert.That(TerrainBlackboardCatalog.SetValue(tile, "mode", "value", new JValue(0)), Is.False);
            Assert.That(JToken.DeepEquals(tile, snapshot), Is.True);
        }

        [TestCase("integer", "1.5", false)]
        [TestCase("integer", "2147483648", false)]
        [TestCase("integer", "-1", true)]
        [TestCase("number", "Infinity", false)]
        [TestCase("number", "1.25", true)]
        public void NumericFieldsUseVerifiedInputTypes(string kind, string input, bool expected)
        {
            Assert.That(TerrainBlackboardCatalog.TryNumber(kind, input, out _), Is.EqualTo(expected));
        }

        [Test]
        public void SearchUsesChineseNamesPinyinAndStableIds()
        {
            var terrain = EditorCatalog.LoadBundled();
            Assert.That(terrain.Search("linshibudao", "terrain").First().Id, Is.EqualTo("tile_woodrd"));
            Assert.That(terrain.Search("lsbd", "terrain").First().Id, Is.EqualTo("tile_woodrd"));
            Assert.That(terrain.Search("TILE_WOODRD", "terrain").First().Id, Is.EqualTo("tile_woodrd"));

            var source = new[] { "obt/main/level_main_00-01.json", "obt/main/level_main_00-02.json" };
            var search = TerrainSearchRuntime.CreateSourceWorker();
            Assert.That(search.Filter(source, "坍塌", out _).First(), Is.EqualTo(source[0]));
            Assert.That(search.Filter(source, "tanta", out _).First(), Is.EqualTo(source[0]));
        }

        [Test]
        public void AcceleratedSearchMatchesTheToolboxFuseRanking()
        {
            var values = new[]
            {
                "level_main_01-07", "level_main_01-08", "level_main_11-07", "activities/level_act46side_01",
                "temp", "临时步道", "linshibudao", "linshibudso", "level_abcdefghijklmnopqrstuvwxyzz0123456789"
            };
            var search = new CatalogSearch<string>(value => new JObject { ["name"] = value });
            // Expected ranks from the Toolbox's unmodified Fuse.js 7.5.0 with the same options.
            Assert.That(search.Filter(values, "level_maim_01-07", out _), Is.EqualTo(values.Take(3)));
            Assert.That(search.Filter(values, "临时步道", out _), Is.EqualTo(new[] { values[5] }));
            Assert.That(search.Filter(values, "linshibudao", out _), Is.EqualTo(new[] { values[6], values[7] }));
            Assert.That(search.Filter(values, "level_abcdefghijklmnopqrstuvwxyzz0123456799", out _),
                Is.EqualTo(new[] { values[8] }));
        }

        [Test]
        public void SampleSearchCanFindAStageAmongAllSamplesForRoad()
        {
            var samples = TerrainSampleCatalog.LoadBundled().Samples("tile_road");
            var worker = TerrainSearchRuntime.CreateSampleWorker();
            var found = worker.Filter(samples, "level_main_01-07", out var error).ToArray();
            Assert.That(error, Is.Null);
            Assert.That(found.Any(sample => sample.SourcePaths.Contains("obt/main/level_main_01-07.json")), Is.True);
        }

        [Test]
        public void FuseSearchCanFilterTheBundledLevelCorpus()
        {
            const string path = "Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/Catalog/terrain-samples.json";
            var samples = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            var levels = JObject.Parse(samples.text)["levels"]!.Values<string>().OfType<string>().ToArray();
            var search = TerrainSearchRuntime.CreateSourceWorker();
            var timing = System.Diagnostics.Stopwatch.StartNew();

            var matches = search.Filter(levels, "level_main_01-07", out var error).ToArray();
            Assert.That(error, Is.Null);
            Assert.That(matches.First(), Is.EqualTo("obt/main/level_main_01-07.json"));
            TestContext.WriteLine($"Cold search for {levels.Length} sources: {timing.ElapsedMilliseconds} ms");

            timing.Restart();
            matches = search.Filter(levels, "tanta", out error).ToArray();
            Assert.That(error, Is.Null);
            Assert.That(matches.First(), Is.EqualTo("obt/main/level_main_00-01.json"));
            TestContext.WriteLine($"Warm pinyin search: {timing.ElapsedMilliseconds} ms");
        }

        [UnityTest]
        public IEnumerator SourceListAndCardsFilterTheSamePaths()
        {
            var paths = Enumerable.Range(0, 30).Select(i => $"activities/act49side/level_act49side_{i:00}.json")
                .Append("obt/main/level_main_01-07.json");
            var window = ScriptableObject.CreateInstance<SourceTestWindow>();
            try
            {
                window.Show();
                yield return null;
                var browser = new TerrainSourceBrowser(paths, true, _ => { }) { value = true };
                window.rootVisualElement.Add(browser);
                var cards = browser.Q("terrain-source-cards");
                var list = browser.Q<ListView>("terrain-source-list");
                var query = browser.Q<TextField>("terrain-source-search");
                Assert.That(cards.childCount, Is.EqualTo(24));
                Assert.That(list.style.display.value, Is.EqualTo(DisplayStyle.None));

                query.value = "ACT49SIDE _09";
                yield return WaitForSourceSearch(browser);
                Assert.That(cards.childCount, Is.EqualTo(1));
                Assert.That(list.itemsSource.Count, Is.EqualTo(1));
                Assert.That(list.itemsSource[0], Does.EndWith("_09.json"));

                query.value = "level_main_01-07";
                yield return WaitForSourceSearch(browser);
                Assert.That(list.itemsSource[0], Is.EqualTo("obt/main/level_main_01-07.json"));
                query.value = "level_main_00";
                query.value = "no_matching_level";
                yield return WaitForSourceSearch(browser);
                Assert.That(cards.childCount, Is.Zero);
                Assert.That(list.itemsSource.Count, Is.Zero);
            }
            finally { Object.DestroyImmediate(window); }
        }

        private static IEnumerator WaitForSourceSearch(TerrainSourceBrowser browser)
        {
            var label = browser.Q<Label>("terrain-source-status");
            var timing = System.Diagnostics.Stopwatch.StartNew();
            while (label.text == "搜索中…" && timing.Elapsed.TotalSeconds < 10) yield return null;
            Assert.That(label.text, Is.Not.EqualTo("搜索中…"));
        }

        private sealed class SourceTestWindow : EditorWindow { }
    }
}
