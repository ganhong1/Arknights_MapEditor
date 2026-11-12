#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Catalog
{
    internal sealed class DeviceSampleCatalog
    {
        internal static readonly string[] Roles = { "tokenInsts", "tokenCards" };
        private const string Root = "Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/Catalog/";
        private static DeviceSampleCatalog? cached;
        private readonly string[] levels;
        private readonly Dictionary<string, Details> loaded = new Dictionary<string, Details>(StringComparer.Ordinal);
        private readonly Queue<string> loadOrder = new Queue<string>();
        internal IReadOnlyList<Entry> Entries { get; }
        internal string Revision { get; }

        private DeviceSampleCatalog(JObject root)
        {
            if (root.Value<int>("version") != 3 || !(root["entries"] is JArray entries) || !(root["levels"] is JArray paths))
                throw new FormatException("装置目录格式无效。");
            Revision = root.Value<string>("revision") ?? throw new FormatException("缺少装置数据版本。");
            levels = paths.Values<string>().OfType<string>().ToArray();
            Entries = Array.AsReadOnly(entries.OfType<JObject>().Select(row => new Entry(row)).ToArray());
            if (Entries.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != Entries.Count)
                throw new FormatException("装置目录存在重复 ID。");
        }

        internal static DeviceSampleCatalog LoadBundled() => cached ??= new DeviceSampleCatalog(Read("device-index.json"));

        internal Details Load(string id)
        {
            if (loaded.TryGetValue(id, out var existing)) return existing;
            var entry = Entries.FirstOrDefault(x => x.Id == id) ?? throw new FormatException("装置不在目录中。");
            if (!entry.HasSamples) return new Details(id, new JObject(), levels);
            var root = Read("DeviceSamples/" + entry.File);
            if (root.Value<int>("version") != 3 || root.Value<string>("id") != id || !(root["roles"] is JObject roles))
                throw new FormatException("装置配置身份不匹配。");
            var details = new Details(id, roles, levels);
            if (loaded.Count >= 4) loaded.Remove(loadOrder.Dequeue());
            loaded.Add(id, details);
            loadOrder.Enqueue(id);
            return details;
        }

        internal CatalogSearch<Entry> CreateSearch() => TerrainSearchRuntime.CreateWorker<Entry>(entry => new JObject
        {
            ["name"] = entry.Name, ["targetFile"] = entry.Id, ["description"] = entry.Description,
            ["namePinyin"] = entry.Pinyin(0), ["nameInitials"] = entry.Pinyin(1), ["category"] = "装置"
        });

        private static JObject Read(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + path);
            if (asset == null) throw new FileNotFoundException("缺少装置资源库文件。", path);
            using var input = new StringReader(asset.text);
            using var reader = new JsonTextReader(input) { DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        }

        internal sealed class Entry
        {
            private readonly JObject row;
            internal string Id { get; }
            internal string Name { get; }
            internal string Description => row.Value<string>("description") ?? "";
            internal string? File => row.Value<string>("file");
            internal bool HasSamples => File != null;
            internal int Configurations(string role) => row["roles"]?[role]?.Value<int>("configurations") ?? 0;
            internal string? Pinyin(int index) => (row["pinyin"] as JArray)?.ElementAtOrDefault(index)?.Value<string>();
            internal Entry(JObject row)
            {
                this.row = (JObject)row.DeepClone();
                Id = row.Value<string>("id") ?? throw new FormatException("缺少装置 ID。");
                var label = (row.Value<string>("name") ?? "").Trim();
                Name = label.Length == 0 ? Id : label;
                if (File != null && File != Id + ".json") throw new FormatException("装置配置文件名不匹配。");
                if (Id.Length == 0 || Id.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_'))
                    throw new FormatException("装置 ID 无效。");
            }
        }

        internal sealed class Details
        {
            private readonly Dictionary<string, Sample[]> samples = new Dictionary<string, Sample[]>(StringComparer.Ordinal);
            private readonly Dictionary<string, int[]> recommended = new Dictionary<string, int[]>(StringComparer.Ordinal);
            internal Details(string id, JObject roles, string[] levels)
            {
                foreach (var role in Roles)
                {
                    var data = roles[role];
                    if (data == null) continue;
                    var rows = ((JArray)data["samples"]!).OfType<JObject>().Select((row, index) => new Sample(id, role, index + 1, row, levels)).ToArray();
                    var choices = ((JArray)data["recommended"]!).Values<int>().ToArray();
                    if (choices.Length > 3 || choices.Distinct().Count() != choices.Length || choices.Any(index => index < 0 || index >= rows.Length))
                        throw new FormatException("装置推荐索引无效。");
                    samples.Add(role, rows);
                    recommended.Add(role, choices);
                }
            }
            internal IReadOnlyList<Sample> Samples(string role) => samples.TryGetValue(role, out var value) ? Array.AsReadOnly(value) : Array.Empty<Sample>();
            internal int[] Recommended(string role) => recommended.TryGetValue(role, out var value) ? (int[])value.Clone() : Array.Empty<int>();
        }

        internal sealed class Sample
        {
            internal int Number { get; }
            private readonly JObject data;
            internal IReadOnlyList<Original> Originals { get; }
            internal IReadOnlyList<string> SourcePaths { get; }
            internal JObject CopyRecord() => (JObject)data.DeepClone();
            internal Sample(string id, string role, int number, JObject row, string[] levels)
            {
                Number = number;
                data = (JObject)row["data"]!.DeepClone();
                if (data["inst"]?.Value<string>("characterKey") != id ||
                    role == "tokenInsts" && (data.Property("position") != null || data.Property("direction") != null))
                    throw new FormatException("装置配置身份或放置字段无效。");
                Originals = Array.AsReadOnly(((JArray)row["originals"]!).OfType<JObject>()
                    .Select(value => new Original(id, role, value, levels)).ToArray());
                if (Originals.Count == 0) throw new FormatException("装置配置缺少出处。");
                foreach (var original in Originals)
                {
                    var comparable = original.CopyRecord();
                    if (role == "tokenInsts") { comparable.Remove("position"); comparable.Remove("direction"); }
                    if (!JToken.DeepEquals(data, comparable)) throw new FormatException("装置配置与出处不匹配。");
                }
                SourcePaths = Array.AsReadOnly(Originals.SelectMany(x => x.SourcePaths).Distinct(StringComparer.Ordinal)
                    .OrderBy(x => x, StringComparer.Ordinal).ToArray());
            }
        }

        internal sealed class Original
        {
            private readonly JObject data;
            private readonly JArray references;
            private readonly string[] levels;
            internal string Role { get; }
            internal IReadOnlyList<string> SourcePaths { get; }
            internal Original(string id, string role, JObject row, string[] levels)
            {
                Role = role;
                this.levels = levels;
                data = (JObject)row["data"]!.DeepClone();
                references = (JArray)row["references"]!.DeepClone();
                if (data["inst"]?.Value<string>("characterKey") != id || references.Count == 0)
                    throw new FormatException("装置原始记录身份无效。");
                foreach (var token in references)
                {
                    if (!(token is JArray reference) || reference.Count != 3 || reference[0]!.Type != JTokenType.Integer ||
                        reference[0]!.Value<int>() < 0 || reference[0]!.Value<int>() >= levels.Length ||
                        (reference[1]!.Value<string>() != "predefines" && reference[1]!.Value<string>() != "hardPredefines") ||
                        reference[2]!.Type != JTokenType.Integer || reference[2]!.Value<int>() < 0)
                        throw new FormatException("装置出处索引无效。");
                }
                SourcePaths = Array.AsReadOnly(references.Select(x => levels[x[0]!.Value<int>()]).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray());
            }
            internal JObject CopyRecord() => (JObject)data.DeepClone();
            internal IEnumerable<string> Pointers(string path) => references.Where(x => levels[x[0]!.Value<int>()] == path)
                .Select(x => $"{x[1]!.Value<string>()}.{Role}[{x[2]!.Value<int>()}]");
        }
    }
}
