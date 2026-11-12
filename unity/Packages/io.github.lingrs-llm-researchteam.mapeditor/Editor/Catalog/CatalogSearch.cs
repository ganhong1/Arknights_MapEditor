#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Jint;
using Jint.Native;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Catalog
{
    internal sealed class CatalogSearch<T>
    {
        private readonly Func<T, JObject> describe;
        private readonly Func<Engine> getEngine;
        private readonly Func<T[], string?>? prebuilt;
        private IEnumerable<T>? indexedSource;
        private T[] items = Array.Empty<T>();
        private JsValue? index;

        internal CatalogSearch(Func<T, JObject> describe)
            : this(describe, () => TerrainSearchRuntime.Engine, null) { }

        internal CatalogSearch(Func<T, JObject> describe, Func<Engine> getEngine, Func<T[], string?>? prebuilt)
        {
            this.describe = describe;
            this.getEngine = getEngine;
            this.prebuilt = prebuilt;
        }

        internal IEnumerable<T> Filter(IEnumerable<T> entries, string? text, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(text)) return entries;
            try
            {
                var runtime = getEngine();
                if (!ReferenceEquals(indexedSource, entries) || index == null)
                {
                    var indexedItems = entries.ToArray();
                    var prepared = prebuilt?.Invoke(indexedItems);
                    var documents = prepared == null ? new JArray(indexedItems.Select(describe)) :
                        new JArray(Enumerable.Repeat(0, indexedItems.Length));
                    var next = runtime.Invoke("buildCatalogIndex", documents.ToString(Formatting.None), prepared ?? "");
                    items = indexedItems;
                    index = next;
                    indexedSource = entries;
                }

                var result = runtime.Invoke("findCatalogMatches", index, text.Trim().ToLowerInvariant());
                return JArray.Parse(result.ToString()).Values<int>().Select(i => items[i]).ToArray();
            }
            catch (Exception failure) when (failure is InvalidOperationException || failure is JsonException ||
                failure is Jint.Runtime.JavaScriptException || failure is TimeoutException)
            {
                error = "搜索暂不可用，已显示完整列表。";
                Debug.LogWarning("目录搜索失败 · " + failure.Message);
                return entries;
            }
        }
    }

    internal static class TerrainSearchRuntime
    {
        private const string Root = "Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/Catalog/";
        private static Engine? engine;
        private static JObject? metadata;
        private static JObject? stageIndex;
        private static Dictionary<string, JToken>? stageRecords;

        internal static Engine Engine => engine ??= BuildEngine(
            ReadAsset("SearchDependencies/Fuse.js.txt"), ReadAsset("SearchDependencies/TerrainSearch.js.txt"));

        private static JObject Metadata => metadata ??= JObject.Parse(ReadAsset("terrain-search-metadata.json"));

        internal static CatalogSearch<string> CreateSourceWorker()
        {
            // Load Unity assets on the editor thread. The worker uses only immutable data and its own engine.
            _ = Metadata;
            var index = stageIndex ??= JObject.Parse(ReadAsset("terrain-source-index.json"));
            stageRecords ??= ((JArray)index["paths"]!).Values<string>().OfType<string>()
                .Select((path, i) => (path, record: index["index"]!["records"]![i]!))
                .ToDictionary(entry => entry.path, entry => entry.record, StringComparer.Ordinal);
            return CreateWorker<string>(Stage, PrepareStageIndex);
        }

        internal static CatalogSearch<TerrainSampleCatalog.Sample> CreateSampleWorker() => CreateWorker<TerrainSampleCatalog.Sample>(Sample);

        internal static CatalogSearch<T> CreateWorker<T>(Func<T, JObject> describe, Func<T[], string?>? prebuilt = null)
        {
            _ = Metadata;
            var fuse = ReadAsset("SearchDependencies/Fuse.js.txt");
            var bridge = ReadAsset("SearchDependencies/TerrainSearch.js.txt");
            Engine? workerEngine = null;
            return new CatalogSearch<T>(describe, () => workerEngine ??= BuildEngine(fuse, bridge), prebuilt);
        }

        private static string? PrepareStageIndex(string[] paths)
        {
            var records = new JArray();
            foreach (var path in paths)
            {
                if (!stageRecords!.TryGetValue(path, out var source)) return null;
                var record = (JObject)source.DeepClone();
                record["i"] = records.Count;
                records.Add(record);
            }
            return new JObject { ["keys"] = stageIndex!["index"]!["keys"]!.DeepClone(), ["records"] = records }
                .ToString(Formatting.None);
        }

        private static Engine BuildEngine(string fuse, string bridge)
        {
            var runtime = new Engine(options => options.TimeoutInterval(TimeSpan.FromSeconds(8)));
            runtime.Execute("var module = { exports: {} };");
            runtime.Execute(fuse);
            runtime.SetValue("terrainBitapScore", new Func<string, string, double>(new TerrainBitap().Score));
            runtime.Execute(bridge);
            return runtime;
        }

        private static string ReadAsset(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + path);
            if (asset == null) throw new InvalidOperationException("缺少搜索资源 " + path);
            return asset.text;
        }

        internal static JObject Terrain(CatalogEntry entry)
        {
            var reading = Metadata["terrain"]?[entry.Id] as JArray;
            return new JObject
            {
                ["name"] = entry.DisplayName,
                ["targetFile"] = entry.Id,
                ["namePinyin"] = reading?[0]?.DeepClone(),
                ["nameInitials"] = reading?[1]?.DeepClone(),
                ["category"] = entry.Family ?? entry.Category,
                ["description"] = entry.Description
            };
        }

        internal static string? StageName(string path) =>
            (Metadata["stages"]?[path]?["names"] as JArray)?.First?.Value<string>();

        internal static JObject Stage(string path)
        {
            var stage = Metadata["stages"]?[path];
            var ids = stage?["ids"] as JArray;
            return new JObject
            {
                ["name"] = stage?["names"]?.DeepClone(),
                ["targetFile"] = new JArray(new[] { System.IO.Path.GetFileNameWithoutExtension(path) }
                    .Concat(ids?.Values<string>().OfType<string>() ?? Enumerable.Empty<string>())),
                ["namePinyin"] = stage?["pinyin"]?.DeepClone(),
                ["nameInitials"] = stage?["initials"]?.DeepClone(),
                ["category"] = path
            };
        }

        internal static JObject GroupedStage(IEnumerable<string> titles, IEnumerable<string> names,
            IEnumerable<string> identities, IEnumerable<string> paths, IEnumerable<string> descriptions)
        {
            var sources = paths.Where(path => path.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
            var readings = names.Distinct(StringComparer.Ordinal)
                .Select(name => Metadata["stageNames"]?[name.Trim()] as JArray)
                .OfType<JArray>().ToArray();
            return new JObject
            {
                ["name"] = Terms(titles),
                ["targetFile"] = Terms(identities.Concat(sources.Select(path => System.IO.Path.GetFileNameWithoutExtension(path)))),
                ["namePinyin"] = Terms(readings.Select(reading => reading[0]!.Value<string>()!)),
                ["nameInitials"] = Terms(readings.Select(reading => reading[1]!.Value<string>()!)),
                ["category"] = new JArray(sources),
                ["description"] = Terms(descriptions)
            };

            static JArray Terms(IEnumerable<string> values) =>
                new JArray(values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal));
        }

        internal static JObject FinishedLevel(FinishedLevelCatalog.StageGroup group) => GroupedStage(
            group.Variants.Select(variant => variant.Title).Prepend(group.Title),
            group.Variants.Select(variant => variant.Stage?.Name ?? ""),
            group.Variants.SelectMany(variant => new[] { variant.Stage?.Id ?? "", variant.Stage?.Code ?? "" }),
            group.Variants.Select(variant => variant.Source.Path),
            group.Variants.Select(variant => variant.Stage?.Mode ?? ""));

        internal static JObject Sample(TerrainSampleCatalog.Sample sample)
        {
            var stages = sample.SourcePaths.Select(Stage).ToArray();
            var document = new JObject
            {
                ["category"] = new JArray(sample.SourcePaths),
                ["description"] = sample.Compact
            };
            foreach (var key in new[] { "name", "targetFile", "namePinyin", "nameInitials" })
                document[key] = new JArray(stages.SelectMany(stage =>
                    (stage[key] as JArray)?.Values<string>().OfType<string>() ?? Enumerable.Empty<string>())
                    .Distinct(StringComparer.Ordinal));
            return document;
        }
    }
}
