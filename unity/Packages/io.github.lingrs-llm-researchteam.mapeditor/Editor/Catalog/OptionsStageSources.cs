#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Catalog
{
    internal sealed class OptionsStageSources
    {
        private const string CatalogPath = "Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/Catalog/";
        private static OptionsStageSources? cached;
        private readonly string[][] stages;
        private readonly int[][] links;
        private readonly HashSet<int> cachePathMatches;

        internal static OptionsStageSources LoadBundled()
        {
            if (cached != null) return cached;
            var values = AssetDatabase.LoadAssetAtPath<TextAsset>(CatalogPath + "options-values.json");
            var map = AssetDatabase.LoadAssetAtPath<TextAsset>(CatalogPath + "options-stage-sources.json");
            if (values == null || map == null) throw new FileNotFoundException("缺少 options 关卡出处索引。");
            using var sha = SHA256.Create();
            var digest = BitConverter.ToString(sha.ComputeHash(values.bytes)).Replace("-", "");
            var root = JObject.Parse(map.text);
            if (!string.Equals(root.Value<string>("catalogSha256"), digest, StringComparison.OrdinalIgnoreCase))
                throw new FormatException("取值目录与关卡出处索引版本不一致。");
            return cached = new OptionsStageSources(root);
        }

        private OptionsStageSources(JObject root)
        {
            if (!(root["stages"] is JArray stageRows) || !(root["links"] is JArray linkRows) ||
                !(root["cachePathMatches"] is JArray cacheRows))
                throw new FormatException("关卡出处索引结构无效。");
            stages = stageRows.ToObject<string[][]>()!;
            links = linkRows.ToObject<int[][]>()!;
            cachePathMatches = new HashSet<int>(cacheRows.Values<int>());
            if (links.Length != OptionsValueCatalog.LoadBundled().Sources.Count ||
                stages.Any(stage => stage == null || stage.Length != 4 || stage.Any(value => value == null)) ||
                links.Any(indices => indices == null || indices.Any(index => index < 0 || index >= stages.Length)) ||
                cachePathMatches.Any(index => index < 0 || index >= links.Length || links[index].Length == 0))
                throw new FormatException("关卡出处索引结构无效。");
        }

        internal Group[] GroupFiles(IReadOnlyList<OptionsValueCatalog.Source> files)
        {
            var byStage = new SortedDictionary<int, List<OptionsValueCatalog.Source>>();
            foreach (var file in files)
            {
                var indices = links[file.Index];
                if (indices.Length == 0) Add(int.MaxValue, file);
                else foreach (var index in indices) Add(index, file);
            }
            return byStage.Select(pair => new Group(pair.Key == int.MaxValue ? null : stages[pair.Key], pair.Value)).ToArray();

            void Add(int index, OptionsValueCatalog.Source file)
            {
                if (!byStage.TryGetValue(index, out var sources)) byStage.Add(index, sources = new List<OptionsValueCatalog.Source>());
                sources.Add(file);
            }
        }

        internal bool IsCachePathMatch(OptionsValueCatalog.Source source) => cachePathMatches.Contains(source.Index);

        internal sealed class Group
        {
            internal string Id { get; }
            internal string Title { get; }
            internal string Code { get; }
            internal string Name { get; }
            internal string Difficulty { get; }
            internal IReadOnlyList<OptionsValueCatalog.Source> Files { get; }

            internal Group(string[]? stage, IReadOnlyList<OptionsValueCatalog.Source> files)
            {
                Id = stage?[0] ?? "";
                Code = stage?[1] ?? "";
                Name = stage?[2] ?? "";
                Difficulty = stage?[3] ?? "";
                Title = stage == null ? "未匹配关卡条目" :
                    string.Join(" · ", new[] { Code, Name, Id }.Where(text => text.Length > 0).Distinct());
                Files = files;
            }
        }
    }
}
