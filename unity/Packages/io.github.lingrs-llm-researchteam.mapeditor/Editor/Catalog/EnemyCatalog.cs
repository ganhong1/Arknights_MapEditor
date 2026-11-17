#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Catalog
{
    internal sealed class EnemyCatalog
    {
        internal const string Revision = "26-10-22-14-02-11-51447808";
        private readonly string archivePath;
        private readonly Dictionary<string, Entry> byId;
        private readonly Dictionary<string, JObject> cache = new(StringComparer.Ordinal);
        private readonly Queue<string> loadOrder = new();
        private JObject? sources;
        private static EnemyCatalog? bundled;
        internal IReadOnlyList<Entry> Entries { get; }

        internal sealed class Entry
        {
            internal string Id { get; }
            internal string Name { get; }
            internal string Description { get; }
            internal IReadOnlyList<int> Levels { get; }
            internal int ReferenceSamples { get; }
            internal int InlineSamples { get; }
            internal bool HasDatabase { get; }
            internal string DetailPath { get; }
            internal EnemyFilterCatalog.Record Filters { get; }

            internal Entry(JObject data, EnemyFilterCatalog filters)
            {
                Id = (string?)data["id"] ?? throw new FormatException("敌人索引缺少 ID。");
                Filters = filters.Get(Id);
                Name = Filters.Name;
                Description = (string?)data["description"] ?? "";
                Levels = (data["levels"] as JArray)?.Values<int>().ToArray() ?? Array.Empty<int>();
                ReferenceSamples = (int?)data["referenceSamples"] ?? 0;
                InlineSamples = (int?)data["inlineSamples"] ?? 0;
                HasDatabase = (bool?)data["hasDatabase"] == true;
                DetailPath = (string?)data["detail"] ?? throw new FormatException("敌人索引缺少详情路径。");
                if (DetailPath.Length != 69 || !DetailPath.EndsWith(".json", StringComparison.Ordinal) ||
                    DetailPath.Take(64).Any(c => !Uri.IsHexDigit(c)))
                    throw new FormatException("敌人详情路径格式错误。");
            }
        }

        private EnemyCatalog(JObject index, string archivePath)
        {
            if ((int?)index["version"] != 1 || (string?)index["client"] != "2.7.81" ||
                (string?)index["revision"] != Revision || index["entries"] is not JArray rows)
                throw new FormatException("敌人目录版本不匹配。");
            this.archivePath = archivePath;
            using (var stream = File.OpenRead(archivePath))
            using (var hash = SHA256.Create())
            {
                var actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (actual != (string?)index["detailsSha256"])
                    throw new InvalidDataException("敌人详情包校验失败。");
            }
            var filters = EnemyFilterCatalog.LoadBundled();
            Entries = rows.OfType<JObject>().Select(row => new Entry(row, filters)).ToArray();
            byId = Entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        }

        internal static EnemyCatalog LoadBundled()
        {
            if (bundled != null) return bundled;
            var path = Path.GetDirectoryName(FinishedLevelCatalog.BundledPath())!;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Editor.PrtsObjectView.CatalogRoot + "enemy-index.json");
            if (asset == null) throw new FileNotFoundException("缺少敌人目录。");
            return bundled = new EnemyCatalog(JObject.Parse(asset.text), Path.Combine(path, "enemy-details.zip"));
        }

        internal Entry? Find(string? id) => id != null && byId.TryGetValue(id, out var entry) ? entry : null;

        internal JObject Details(Entry entry)
        {
            if (!cache.TryGetValue(entry.Id, out var value))
            {
                value = Read(entry.DetailPath);
                if ((string?)value["id"] != entry.Id) throw new InvalidDataException("敌人详情身份不匹配。");
                if (cache.Count >= 12) cache.Remove(loadOrder.Dequeue());
                cache.Add(entry.Id, value);
                loadOrder.Enqueue(entry.Id);
            }
            return (JObject)value.DeepClone();
        }

        internal IEnumerable<string> SourceLabels(JObject origin)
        {
            sources ??= Read("sources.json");
            var body = (string?)origin["body"] ?? "";
            return GameStageLabel.FromOrigins(sources[body], "stageLabels").Distinct(StringComparer.Ordinal);
        }

        internal CatalogSearch<Entry> CreateSearch() => CatalogSearchRuntime.CreateWorker<Entry>(entry => new JObject
        {
            ["name"] = entry.Name,
            ["targetFile"] = new JArray(entry.Id, entry.Filters.Number),
            ["description"] = new JArray(entry.Filters.SearchText),
            ["category"] = "敌人"
        });

        private JObject Read(string name)
        {
            using var stream = File.OpenRead(archivePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = archive.GetEntry(name) ?? throw new InvalidDataException("缺少敌人详情。");
            if (entry.Length > 32 * 1024 * 1024) throw new InvalidDataException("敌人详情超过读取大小限制。");
            using var content = entry.Open();
            using var reader = new JsonTextReader(new StreamReader(content, new UTF8Encoding(false, true)))
            { MaxDepth = 64, DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        }
    }
}
