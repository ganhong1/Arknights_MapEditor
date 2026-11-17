#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Catalog
{
    internal static class TerrainBlackboardCatalog
    {
        private const string AssetPath =
            "Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/Catalog/terrain-blackboard-rules.json";
        private static string? loadedText;
        private static JObject? tiles;
        private static JObject? templates;
        private static JObject? newer;

        private static JObject Load2791()
        {
            if (newer != null) return newer;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Editor.PrtsObjectView.CatalogRoot + "terrain-blackboard-2791.json");
            if (asset == null) throw new FormatException("缺少 2.7.91 地块黑板资源。");
            var root = JObject.Parse(asset.text);
            if (root.Value<int>("version") != 1 || root.Value<string>("assetVersion") != TerrainSampleCatalog.ExpansionRevision ||
                root["tiles"] is not JObject || root["templates"] is not JObject)
                throw new FormatException("2.7.91 地块黑板资源版本无效。");
            return newer = root;
        }

        internal static IReadOnlyList<JObject> Rules(string tileKey)
        {
            Load();
            var rules = (tiles![tileKey] ?? Load2791()["tiles"]?[tileKey]) as JArray;
            return rules?.OfType<JObject>().ToArray() ?? Array.Empty<JObject>();
        }

        internal static JObject? Template(string tileKey)
        {
            Load();
            var tile = (templates?[tileKey] ?? Load2791()["templates"]?[tileKey]) as JObject;
            return (JObject?)tile?.DeepClone();
        }

        private static void Load()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetPath);
            if (asset == null) throw new FormatException("缺少地块黑板规则资源。");
            if (tiles == null || !string.Equals(loadedText, asset.text, StringComparison.Ordinal))
            {
                var root = JObject.Parse(asset.text, new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                });
                if (root.Value<int>("version") != 1 || !(root["tiles"] is JObject entries))
                    throw new FormatException("地块黑板规则资源版本无效。");
                tiles = entries;
                templates = root["templates"] as JObject;
                loadedText = asset.text;
            }
        }

        internal static bool TryNumber(string kind, string text, out JValue? value)
        {
            value = null;
            if (kind == "integer")
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                    return false;
                value = new JValue(integer);
                return true;
            }

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                double.IsNaN(number) || double.IsInfinity(number))
                return false;
            value = new JValue(number);
            return true;
        }

        internal static bool TryFindPair(JObject tile, string key, out JObject? pair)
        {
            pair = null;
            var token = tile["blackboard"];
            if (token == null || token.Type == JTokenType.Null) return true;
            if (!(token is JArray board)) return false;
            foreach (var item in board.OfType<JObject>())
            {
                if (item["key"]?.Type != JTokenType.String || item.Value<string>("key") != key) continue;
                if (pair != null) return false;
                pair = item;
            }
            return true;
        }

        internal static bool SetValue(JObject tile, string key, string field, JToken value)
        {
            if (!TryFindPair(tile, key, out var pair)) return false;
            if (pair == null)
            {
                if (!(tile["blackboard"] is JArray board))
                {
                    board = new JArray();
                    tile["blackboard"] = board;
                }
                pair = new JObject { ["key"] = key, ["value"] = 0, ["valueStr"] = JValue.CreateNull() };
                board.Add(pair);
            }
            pair[field] = value.DeepClone();
            return true;
        }
    }
}
