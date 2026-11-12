#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Catalog
{
    /// <summary>Observed values and exact provenance, never a whitelist of legal gameplay values.</summary>
    internal sealed class OptionsValueCatalog
    {
        private static OptionsValueCatalog? cached;
        private readonly Dictionary<string, Group> fields = new Dictionary<string, Group>(StringComparer.Ordinal);
        private readonly Dictionary<string, Group> blackboard = new Dictionary<string, Group>(StringComparer.Ordinal);
        internal string Revision { get; }
        internal IReadOnlyList<Source> Sources { get; }
        internal IReadOnlyList<string> BlackboardKeys { get; }

        internal static OptionsValueCatalog LoadBundled()
        {
            if (cached != null) return cached;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/Catalog/options-values.json");
            if (asset == null) throw new FileNotFoundException("缺少 options 原版取值目录。");
            return cached = Parse(asset.text);
        }

        internal static OptionsValueCatalog Parse(string json)
        {
            // Reuse the document's strict syntax gate and exact fractional lexemes.
            // Parsing the catalogue with Json.NET alone rounds original values through Double.
            var envelope = LevelDocument.Create(1, 1).WithSection("optionsValueCatalog", json);
            return new OptionsValueCatalog(Object(envelope.GetSection("optionsValueCatalog")));
        }

        private OptionsValueCatalog(JObject root)
        {
            if (root.Value<int>("version") != 1 || !(root["levels"] is JArray levels) ||
                !(root["options"] is JArray options) || !(root["fields"] is JObject fieldRows) || !(root["blackboard"] is JObject keyRows))
                throw new FormatException("options 值目录结构无效。");
            Revision = RequiredString(root, "revision");
            var sources = levels.Select((row, index) => new Source(index, Object(row), options)).ToArray();
            if (sources.Select(source => source.Kind + "\n" + source.Revision + "\n" + source.Path).Distinct(StringComparer.Ordinal).Count() != sources.Length)
                throw new FormatException("options 出处路径重复。");
            Sources = Array.AsReadOnly(sources);
            foreach (var row in fieldRows.Properties()) fields.Add(row.Name, new Group(row.Name, false, Object(row.Value), sources));
            foreach (var row in keyRows.Properties()) blackboard.Add(row.Name, new Group(row.Name, true, Object(row.Value), sources));
            BlackboardKeys = Array.AsReadOnly(blackboard.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());
        }

        internal Group? Field(string key) => fields.TryGetValue(key, out var value) ? value : null;
        internal Group? Blackboard(string key) => blackboard.TryGetValue(key, out var value) ? value : null;
        internal static string Compact(JToken value)
        {
            if (value is JArray array) return "列表 · " + array.Count + " 项";
            var text = value.ToString(Formatting.None);
            return text.Length > 90 ? text.Substring(0, 87) + "…" : text;
        }

        private static readonly Regex Fraction = new Regex(
            @"\A(?<sign>-?)(?<whole>0|[1-9][0-9]*)(?:\.(?<fraction>[0-9]+))?(?:[eE](?<exponent>[+-]?[0-9]+))?\z",
            RegexOptions.CultureInvariant);

        internal static bool ExactValuesEqual(JToken? left, JToken? right)
        {
            if (left == null || right == null) return left == null && right == null;
            if (left.Type == JTokenType.Raw || left.Type == JTokenType.Float || right.Type == JTokenType.Raw || right.Type == JTokenType.Float)
                return TryFraction(left, out var a) && TryFraction(right, out var b) && a == b;
            if (left is JObject first && right is JObject second)
                return first.Count == second.Count && first.Properties().All(property =>
                    second.Property(property.Name, StringComparison.Ordinal) is JProperty other && ExactValuesEqual(property.Value, other.Value));
            if (left is JArray firstArray && right is JArray secondArray)
                return firstArray.Count == secondArray.Count && firstArray.Select((item, index) => ExactValuesEqual(item, secondArray[index])).All(equal => equal);
            return JToken.DeepEquals(left, right);
        }

        private static bool TryFraction(JToken token, out string normalized)
        {
            normalized = "";
            if (token.Type != JTokenType.Raw && token.Type != JTokenType.Float) return false;
            var match = Fraction.Match(token.ToString(Formatting.None));
            if (!match.Success || !match.Groups["fraction"].Success && !match.Groups["exponent"].Success) return false;
            var fraction = match.Groups["fraction"].Value;
            var digits = (match.Groups["whole"].Value + fraction).TrimStart('0');
            if (digits.Length == 0) { normalized = "0"; return true; }
            var coefficient = digits.TrimEnd('0');
            var exponent = match.Groups["exponent"].Success ? BigInteger.Parse(match.Groups["exponent"].Value,
                NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : BigInteger.Zero;
            exponent += digits.Length - coefficient.Length - fraction.Length;
            normalized = match.Groups["sign"].Value + coefficient + "e" + exponent.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        private static JObject Object(JToken? value) => value as JObject ?? throw new FormatException("options 目录条目不是对象。");
        private static string RequiredString(JObject row, string key) => row[key]?.Type == JTokenType.String ?
            row[key]!.Value<string>()! : throw new FormatException("options 目录缺少字符串 " + key);
        private static int Integer(JToken? value) => value?.Type == JTokenType.Integer && value.Value<long>() >= 0 && value.Value<long>() <= int.MaxValue ?
            value.Value<int>() : throw new FormatException("options 目录索引/计数无效。");
        private static int[] Indices(JToken? value, int length)
        {
            if (!(value is JArray array)) throw new FormatException("options 目录缺少索引数组。");
            var result = array.Select(Integer).ToArray();
            if (result.Distinct().Count() != result.Length || result.Any(index => index >= length))
                throw new FormatException("options 目录索引重复或越界。");
            return result;
        }

        internal sealed class Source
        {
            private readonly JToken options;
            internal int Index { get; }
            internal string Path { get; }
            internal string Sha256 { get; }
            internal string DocumentId { get; }
            internal string State { get; }
            internal string LevelId { get; }
            internal string MapId { get; }
            internal string Kind { get; }
            internal string? Revision { get; }
            internal JToken CopyOptions() => options.DeepClone();
            internal JToken? Field(string key) => (options as JObject)?[key]?.DeepClone();
            internal Source(int index, JObject row, JArray snapshots)
            {
                Index = index; Path = RequiredString(row, "path"); Sha256 = RequiredString(row, "sha256");
                DocumentId = RequiredString(row, "documentId");
                Kind = RequiredString(row, "source");
                if (string.IsNullOrWhiteSpace(Kind)) throw new FormatException("options 出处来源无效。");
                Revision = row["sourceRevision"]?.Type == JTokenType.String ? row["sourceRevision"]!.Value<string>() : null;
                if (Path.Length == 0 || Path.Contains('\\') || Path.StartsWith("/", StringComparison.Ordinal) ||
                    Path.Split('/').Any(segment => segment == "..") || Sha256.Length != 64 || DocumentId.Length != 64)
                    throw new FormatException("options 出处身份无效。");
                LevelId = row["levelId"]?.ToString() ?? ""; MapId = row["mapId"]?.ToString() ?? "";
                var optionIndex = Integer(row["options"]);
                if (optionIndex >= snapshots.Count) throw new FormatException("options 上下文索引越界。");
                var snapshot = Object(snapshots[optionIndex]); State = RequiredString(snapshot, "state");
                options = snapshot["value"]?.DeepClone() ?? throw new FormatException("缺少 options 上下文值。");
                if (State == "object" ? !(options is JObject) : State == "missing" || State == "null" ? options.Type != JTokenType.Null :
                    State != "other" || options.Type == JTokenType.Object || options.Type == JTokenType.Null)
                    throw new FormatException("options 上下文状态与类型不符。");
            }
        }

        internal sealed class Group
        {
            internal string Key { get; }
            internal bool IsBlackboard { get; }
            internal IReadOnlyList<Sample> Samples { get; }
            internal IReadOnlyList<Sample> Recommended { get; }
            internal IReadOnlyList<Source> MissingSources { get; }
            internal Group(string key, bool isBlackboard, JObject row, Source[] sources)
            {
                Key = key; IsBlackboard = isBlackboard;
                if (!(row["samples"] is JArray samples)) throw new FormatException("options 目录缺少样例。");
                var values = samples.Select((value, index) => new Sample(key, isBlackboard, index, Object(value), sources)).ToArray();
                Samples = Array.AsReadOnly(values);
                var recommended = Indices(row["recommended"], values.Length);
                if (recommended.Length > 3) throw new FormatException("options 推荐值超过3个。");
                Recommended = Array.AsReadOnly(recommended.Select(index => values[index]).ToArray());
                if (Recommended.Any(sample => sample.PrimaryCount <= 0)) throw new FormatException("推荐值不含主快照出处。");
                var missing = isBlackboard ? Array.Empty<int>() : Indices(row["missingSources"], sources.Length);
                if (missing.Any(index => sources[index].Field(key) != null)) throw new FormatException("options 缺失记录实际提供了字段。");
                MissingSources = Array.AsReadOnly(missing.Select(index => sources[index]).ToArray());
            }
        }

        internal sealed class Sample
        {
            private readonly JToken value;
            private readonly Dictionary<int, int[]> references = new Dictionary<int, int[]>();
            private readonly string key;
            internal int Index { get; }
            internal int Count { get; }
            internal int PrimaryCount { get; }
            internal string Label => Compact(value);
            internal string Json => value.ToString(Formatting.Indented);
            internal IReadOnlyList<Source> Sources { get; }
            internal JToken CopyValue() => value.DeepClone();
            internal bool Matches(JToken? candidate) => ExactValuesEqual(value, candidate);
            internal string Pointers(Source source) => references.TryGetValue(source.Index, out var items) ?
                string.Join(", ", items.Select(index => "/options/configBlackBoard/" + index)) : "/options/" + key.Replace("~", "~0").Replace("/", "~1");

            internal Sample(string key, bool isBlackboard, int index, JObject row, Source[] sources)
            {
                this.key = key; Index = index;
                value = row["value"]?.DeepClone() ?? throw new FormatException("options 样例缺少原值。");
                var ids = Indices(row["sources"], sources.Length);
                Sources = Array.AsReadOnly(ids.Select(id => sources[id]).ToArray());
                Count = Integer(row["count"]);
                PrimaryCount = Integer(row["primaryCount"]);
                if (ids.Length == 0 || Count != Sources.Select(source => source.DocumentId).Distinct(StringComparer.Ordinal).Count())
                    throw new FormatException("options 样例计数与去重出处不一致。");
                if (PrimaryCount != Sources.Where(source => source.Kind == "ArknightsGameData").Select(source => source.DocumentId).Distinct(StringComparer.Ordinal).Count())
                    throw new FormatException("options 主快照计数不一致。");
                if (isBlackboard) ReadReferences(row["references"], sources, ids);
                else if (Sources.Any(source => !ExactValuesEqual(source.Field(key), value)))
                    throw new FormatException("options 样例与出处原值不一致。");
            }

            private void ReadReferences(JToken? token, Source[] sources, int[] sourceIds)
            {
                if (!(token is JArray rows) || !(value is JObject pair) || pair.Value<string>("key") != key)
                    throw new FormatException("黑板样例缺少条目索引或键名不匹配。");
                var positions = new HashSet<string>(StringComparer.Ordinal);
                var entries = new Dictionary<int, List<int>>();
                foreach (var row in rows)
                {
                    if (!(row is JArray reference) || reference.Count != 2) throw new FormatException("黑板出处索引无效。");
                    var sourceId = Integer(reference[0]); var entryId = Integer(reference[1]);
                    if (!sourceIds.Contains(sourceId) || !positions.Add(sourceId + ":" + entryId)) throw new FormatException("黑板出处重复或源不匹配。");
                    var array = sources[sourceId].Field("configBlackBoard") as JArray;
                    if (array == null || entryId >= array.Count || !ExactValuesEqual(array[entryId], value))
                        throw new FormatException("黑板样例与原始位置不一致。");
                    if (!entries.TryGetValue(sourceId, out var indices)) entries.Add(sourceId, indices = new List<int>());
                    indices.Add(entryId);
                }
                if (entries.Count != sourceIds.Length) throw new FormatException("黑板出处缺少位置。");
                foreach (var entry in entries) references.Add(entry.Key, entry.Value.ToArray());
            }
        }
    }
}
