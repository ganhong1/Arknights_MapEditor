#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Jint;
using Jint.Native;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
            : this(describe, () => CatalogSearchRuntime.Engine, null) { }

        internal CatalogSearch(Func<T, JObject> describe, Func<Engine> getEngine, Func<T[], string?>? prebuilt)
        {
            this.describe = describe;
            this.getEngine = getEngine;
            this.prebuilt = prebuilt;
        }

        internal IEnumerable<T> Filter(IEnumerable<T> entries, string? text, out string? error)
        {
            error = null;
            if (text == null || string.IsNullOrWhiteSpace(text)) return entries;
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
                error = "搜索暂不可用，已显示完整列表。 · 日志 " + Editor.PluginLog.Failure("catalog.search", failure);
                return entries;
            }
        }
    }

    internal static class TerrainSearchRuntime
    {
        private static JObject? metadata;
        private static JObject? metadata2791;
        private static JObject? stageIndex;
        private static Dictionary<string, JToken>? stageRecords;

        private static JObject Metadata => metadata ??= JObject.Parse(CatalogSearchRuntime.ReadAsset("terrain-search-metadata.json"));

        private static JObject Metadata2791 => metadata2791 ??=
            JObject.Parse(CatalogSearchRuntime.ReadAsset("terrain-search-metadata-2791.json"));

        private static JToken? StageMetadata(string path) =>
            Metadata["stages"]?[path] ?? Metadata2791["stages"]?[path];

        internal static CatalogSearch<string> CreateSourceWorker()
        {
            // Load Unity assets on the editor thread. The worker uses only immutable data and its own engine.
            _ = Metadata2791;
            _ = Metadata;
            var index = stageIndex ??= JObject.Parse(CatalogSearchRuntime.ReadAsset("terrain-source-index.json"));
            stageRecords ??= ((JArray)index["paths"]!).Values<string>().OfType<string>()
                .Select((path, i) => (path, record: index["index"]!["records"]![i]!))
                .ToDictionary(entry => entry.path, entry => entry.record, StringComparer.Ordinal);
            return CatalogSearchRuntime.CreateWorker<string>(Stage, PrepareStageIndex);
        }

        internal static CatalogSearch<T> CreateWorker<T>(Func<T, JObject> describe) =>
            CatalogSearchRuntime.CreateWorker(describe);

        internal static CatalogSearch<TerrainSampleCatalog.Sample> CreateSampleWorker() =>
            CreateStageWorker<TerrainSampleCatalog.Sample>(Sample);

        internal static CatalogSearch<T> CreateStageWorker<T>(Func<T, JObject> describe)
        {
            // Unity metadata must be loaded before the worker starts indexing on a background thread.
            _ = Metadata2791;
            _ = Metadata;
            return CatalogSearchRuntime.CreateWorker(describe);
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

        internal static JObject Terrain(CatalogEntry entry)
        {
            var reading = (Metadata["terrain"]?[entry.Id] ?? Metadata2791["terrain"]?[entry.Id]) as JArray;
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
            (StageMetadata(path)?["names"] as JArray)?.First?.Value<string>();

        internal static string StageCaption(string source) => source.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? StageName(source) ?? GameStageLabel.LevelId(source)
            : source;

        internal static JObject Stage(string path)
        {
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var parts = path.Split(new[] { " · " }, StringSplitOptions.None);
                return GroupedStage(new[] { path }, parts, parts.Take(1), Array.Empty<string>(), parts.Skip(2));
            }
            var stage = StageMetadata(path);
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

        internal static CatalogSearch<FinishedLevelCatalog.StageGroup> CreateFinishedLevelWorker()
        {
            _ = Metadata;
            var index = stageIndex ??= JObject.Parse(CatalogSearchRuntime.ReadAsset("terrain-source-index.json"));
            var keys = (JArray)index["index"]!["keys"]!;
            return CatalogSearchRuntime.CreateWorker<FinishedLevelCatalog.StageGroup>(FinishedLevel, groups =>
            {
                var records = new JArray();
                for (var i = 0; i < groups.Length; i++)
                {
                    var document = FinishedLevel(groups[i]);
                    var fields = new JObject();
                    for (var k = 0; k < keys.Count; k++)
                    {
                        var values = (JArray)document[keys[k]!["id"]!.Value<string>()!]!;
                        fields[k.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new JArray(
                            values.Values<string>().Select((value, index) => new JObject
                            {
                                ["v"] = value,
                                ["i"] = index,
                                ["n"] = Math.Round(1 / Math.Sqrt(Regex.Matches(value!, @"[^\u0009-\u000D\u0020\u00A0]+").Count), 3, MidpointRounding.AwayFromZero)
                            }));
                    }
                    records.Add(new JObject { ["i"] = i, ["$"] = fields });
                }
                // Reuse the pinned Fuse key definitions and score field normalization.
                return new JObject { ["keys"] = keys.DeepClone(), ["records"] = records }.ToString(Formatting.None);
            });
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
