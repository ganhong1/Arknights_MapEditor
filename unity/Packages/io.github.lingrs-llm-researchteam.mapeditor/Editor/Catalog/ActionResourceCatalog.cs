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
    /// <summary>Small prebuilt resource-key index; never opens the level archive at runtime.</summary>
    internal sealed class ActionResourceCatalog
    {
        internal sealed class Entry
        {
            internal string Type { get; }
            internal string OperaConfig { get; }
            internal string Key { get; }
            internal string Label { get; }
            internal string Source { get; }
            internal string MatchStatus { get; }
            internal bool Verified { get; }

            internal Entry(JObject value)
            {
                Type = RequiredText(value, "type");
                OperaConfig = RequiredText(value, "operaConfig");
                Key = RequiredText(value, "key");
                Label = RequiredText(value, "label");
                MatchStatus = RequiredText(value, "matchStatus");
                if (value["verified"]?.Type != JTokenType.Boolean || value["examples"] is not JArray examples ||
                    value["resources"] is not JArray resources || examples.Any(item => item is not JObject) ||
                    resources.Any(item => item.Type != JTokenType.String) || string.IsNullOrEmpty(Key))
                    throw new FormatException("动作资源索引条目无效。");
                Verified = value["verified"]!.Value<bool>();
                var origins = examples.OfType<JObject>().Select(example =>
                    RequiredText(example, "stageLabel") + " · " + RequiredText(example, "levelPath") + "\n" +
                    RequiredText(example, "actionPath") + "\n" + RequiredText(example, "source") + " · " +
                    RequiredText(example, "revision") + "\nLevelData " + RequiredText(example, "body"));
                Source = string.Join("\n\n", origins) + "\n" + MatchStatus +
                    (resources.Count == 0 ? "" : "\n" + string.Join("\n", resources.Values<string>()));
            }
        }

        private const string ArchiveSha256 = "a3e15a7e7a526f661fa34d42fc861856ec35083106781125fa566bd587392e45";
        private static ActionResourceCatalog? bundled;
        private readonly Dictionary<(string Type, string Config), IReadOnlyList<Entry>> entries;

        private ActionResourceCatalog(JObject root)
        {
            if ((int?)root["version"] != 1 || (string?)root["identity"]?["finished-levels.zip"] != ArchiveSha256 ||
                root["entries"] is not JArray records || records.Any(item => item is not JObject))
                throw new FormatException("动作资源索引版本不匹配。");
            var rows = records.OfType<JObject>().Select(item => new Entry(item)).ToArray();
            if (rows.GroupBy(item => (item.Type, item.OperaConfig, item.Key)).Any(group => group.Count() != 1))
                throw new FormatException("动作资源索引存在重复条目。");
            entries = rows.GroupBy(item => (item.Type, item.Type == "PLAY_OPERA" ? item.OperaConfig : ""))
                .ToDictionary(group => group.Key, group => (IReadOnlyList<Entry>)Array.AsReadOnly(group.ToArray()));
        }

        internal IReadOnlyList<Entry> Find(string type, string operaConfig) =>
            entries.TryGetValue((type, type == "PLAY_OPERA" ? operaConfig ?? "" : ""), out var values)
                ? values : Array.Empty<Entry>();

        internal static ActionResourceCatalog Parse(string json)
        {
            if (json == null || json.Length > 2 * 1024 * 1024) throw new FormatException("动作资源索引过大。");
            using var input = new StringReader(json);
            using var reader = new JsonTextReader(input) { DateParseHandling = DateParseHandling.None, MaxDepth = 32 };
            return new ActionResourceCatalog(JObject.Load(reader));
        }

        internal static ActionResourceCatalog LoadBundled()
        {
            if (bundled != null) return bundled;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Editor.PrtsObjectView.CatalogRoot + "action-resources.json");
            if (asset == null) throw new InvalidOperationException("缺少动作资源候选索引。");
            return bundled = Parse(asset.text);
        }

        private static string RequiredText(JObject value, string name) => value[name]?.Type == JTokenType.String
            ? value[name]!.Value<string>()! : throw new FormatException("动作资源索引缺少字段：" + name);
    }
}
