#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Catalog
{
    internal sealed class OperatorPresetCatalog
    {
        internal const string Revision = "26-10-22-14-02-11-51447808";
        private const int CacheLimit = 12;
        private readonly string archivePath;
        private readonly Dictionary<string, Entry> byId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Entry> byForm = new(StringComparer.Ordinal);
        private readonly Dictionary<string, JObject> details = new(StringComparer.Ordinal);
        private readonly Queue<string> loadOrder = new();
        private readonly int[][] favorFrames;
        private readonly JObject? moduleLabels;
        private JObject? sources;
        private static OperatorPresetCatalog? bundled;
        internal IReadOnlyList<Entry> Entries { get; }

        internal sealed class Entry
        {
            internal string Id { get; }
            internal string Name { get; }
            internal string Profession { get; }
            internal int Rarity { get; }
            internal int Instances { get; }
            internal int Cards { get; }
            internal IReadOnlyList<string> Forms { get; }
            internal IReadOnlyList<FilterForm> FilterForms { get; }

            internal Entry(JObject data)
            {
                Id = (string?)data["id"] ?? throw new FormatException("干员索引缺少 ID。");
                Name = (string?)data["name"] ?? Id;
                Profession = (string?)data["profession"] ?? "";
                Rarity = ParseTier((string?)data["rarity"]);
                Instances = (int)data["instances"]!;
                Cards = (int)data["cards"]!;
                Forms = ((JArray)data["formIds"]!).Values<string>().OfType<string>().ToArray();
                FilterForms = (data["filterForms"] as JArray)?.OfType<JObject>().Select(form => new FilterForm(form)).ToArray()
                    ?? new[] { new FilterForm(data) };
            }
        }

        internal sealed class FilterForm
        {
            internal readonly string Profession, Branch, Position;
            internal readonly IReadOnlyList<string> Tags;

            internal FilterForm(JObject form)
            {
                Profession = (string?)form["profession"] ?? "";
                Branch = (string?)form["subProfession"] ?? "";
                Position = (string?)form["position"] ?? "";
                Tags = (form["tags"] as JArray)?.Values<string>().OfType<string>().ToArray() ?? Array.Empty<string>();
            }
        }

        private OperatorPresetCatalog(JObject index, string archivePath)
        {
            if ((int?)index["version"] != 1 || (string?)index["client"] != "2.7.81" ||
                (string?)index["revision"] != Revision || index["entries"] is not JArray rows)
                throw new FormatException("预设干员索引的版本不匹配。");
            this.archivePath = archivePath;
            Entries = rows.OfType<JObject>().Select(row => new Entry(row)).ToArray();
            foreach (var entry in Entries)
            {
                byId.Add(entry.Id, entry);
                foreach (var form in entry.Forms) byForm.Add(form, entry);
            }
            favorFrames = ((JArray)index["favorFrames"]!).Select(row => row.Values<int>().ToArray()).ToArray();
            moduleLabels = index["moduleLabels"] as JObject;
        }

        internal static OperatorPresetCatalog LoadBundled()
        {
            if (bundled != null) return bundled;
            var path = Path.GetDirectoryName(FinishedLevelCatalog.BundledPath())!;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Editor.PrtsObjectView.CatalogRoot + "operator-index.json");
            if (asset == null) throw new FileNotFoundException("缺少 2.7.81 干员索引。");
            return bundled = new OperatorPresetCatalog(JObject.Parse(asset.text), Path.Combine(path, "operator-details.zip"));
        }

        internal Entry? Find(string? id)
        {
            if (id == null) return null;
            if (byId.TryGetValue(id, out var entry)) return entry;
            return byForm.TryGetValue(id, out entry) ? entry : null;
        }

        internal string ModuleLabel(string id) => (string?)moduleLabels?[id] ?? "模组 " + id;

        internal JObject Details(Entry entry)
        {
            if (details.TryGetValue(entry.Id, out var value)) return value;
            var data = Read(entry.Id + ".json");
            if (details.Count >= CacheLimit) details.Remove(loadOrder.Dequeue());
            details.Add(entry.Id, data);
            loadOrder.Enqueue(entry.Id);
            return data;
        }

        internal JObject Form(Entry entry, string? id)
        {
            var forms = ((JArray)Details(entry)["forms"]!).OfType<JObject>();
            return forms.FirstOrDefault(form => string.IsNullOrEmpty(id) ? (bool?)form["default"] == true : (string?)form["id"] == id)
                ?? throw new FormatException("2.7.81 干员目录中没有所选形态。");
        }

        internal IEnumerable<string> SourceLabels(JObject origin)
        {
            sources ??= Read("sources.json");
            var body = (string)origin["body"]!;
            var suffix = $" · {((string?)origin["section"] == "hardPredefines" ? "困难" : "基础")} #{origin["index"]}";
            if (origin["position"] is JObject cell) suffix += $" · ({cell["row"]}, {cell["col"]}) {origin["direction"]}";
            foreach (var source in (sources[body] as JArray ?? new JArray()).OfType<JObject>())
            {
                var stages = ((JArray)source["stages"]!).OfType<JArray>();
                var names = string.Join(" / ", stages.Select(stage => string.Join(" · ", stage.Values<string>().Where(v => !string.IsNullOrEmpty(v)))));
                var version = $"{source["source"]} {source["revision"]}".Trim();
                yield return (names.Length == 0 ? "" : names + " · ") + source["path"] + suffix + "\n" + version;
            }
        }

        internal int FavorPhase(int points)
        {
            if (points < 0 || points > favorFrames[favorFrames.Length - 1][0])
                throw new FormatException("账号信赖点超出 2.7.81 信赖表范围。");
            var phase = 0;
            foreach (var frame in favorFrames)
            {
                if (frame[0] > points) break;
                phase = frame[1];
            }
            return phase;
        }

        internal int MaximumFavorPhase => favorFrames[favorFrames.Length - 1][1];
        internal CatalogSearch<Entry> CreateSearch() => TerrainSearchRuntime.CreateWorker<Entry>(entry => new JObject
        {
            ["name"] = entry.Name,
            ["targetFile"] = entry.Id,
            ["description"] = string.Join(" ", entry.Forms),
            ["category"] = "预设干员"
        });

        internal static int ParseTier(string? value) =>
            value != null && value.Length > 0 && int.TryParse(value.Substring(value.LastIndexOf('_') + 1), out var index)
                ? index : -1;

        private JObject Read(string name)
        {
            using var stream = File.OpenRead(archivePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = archive.GetEntry(name) ?? throw new InvalidDataException("缺少预设干员详情。");
            if (entry.Length > 8 * 1024 * 1024) throw new InvalidDataException("预设干员详情过大。");
            using var content = entry.Open();
            using var reader = new JsonTextReader(new StreamReader(content, new UTF8Encoding(false, true)))
            { MaxDepth = 32, DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        }
    }
}
