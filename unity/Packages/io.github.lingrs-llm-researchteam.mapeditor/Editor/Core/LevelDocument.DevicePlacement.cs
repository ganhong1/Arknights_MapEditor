#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Core
{
    public sealed partial class LevelDocument
    {
        // Coordinates are map grid coordinates, not Unity world-space positions.
        public LevelDocument PlaceDevices(string section, string configuration, string direction,
            IEnumerable<(int Row, int Col)> positions) =>
            PlacePredefined(section, "tokenInsts", configuration, direction, positions);

        public LevelDocument CreateOperatorPresets(string section, string configuration, string direction,
            (int Row, int Col) position, bool onMap, bool inHand)
        {
            if (!onMap && !inHand) return this;
            var next = onMap
                ? PlacePredefined(section, "characterInsts", configuration, direction, new[] { position })
                : this;
            if (!inHand) return next;
            var card = ParseObjectFragment(configuration);
            card.Remove("position");
            card.Remove("direction");
            // A preset alias identifies one record within the layer.
            if (onMap && !string.IsNullOrEmpty((string?)card["alias"])) card["alias"] = JValue.CreateNull();
            var count = ((next.root[section] as JObject)?["characterCards"] as JArray)?.Count ?? 0;
            return next.SavePredefined(section, "characterCards", count, card.ToString(Formatting.None), false);
        }

        public LevelDocument PlacePredefined(string section, string collection, string configuration, string direction,
            IEnumerable<(int Row, int Col)> positions)
        {
            if (collection != "characterInsts" && collection != "tokenInsts")
                throw new ArgumentException("请选择场上预设。", nameof(collection));
            if (direction != "UP" && direction != "RIGHT" && direction != "DOWN" && direction != "LEFT")
                throw new ArgumentException("预设朝向无效。", nameof(direction));
            var cells = positions.Distinct().ToArray();
            if (cells.Length == 0) return this;
            if (cells.Any(cell => cell.Row < 0 || cell.Col < 0 || cell.Row >= Height || cell.Col >= Width))
                throw new ArgumentOutOfRangeException(nameof(positions), "预设位置超出地图。");
            var template = ParseObjectFragment(configuration);
            RequirePredefinedIdentity(template);
            var copy = (JObject)root.DeepClone();
            var list = PredefinedCollection(copy, section, collection, create: true);
            var alias = template.Value<string>("alias");
            if (!string.IsNullOrEmpty(alias) && cells.Length > 1)
                throw new InvalidOperationException("有别名的预设每次放置一格。");
            RequireUniquePresetAlias((JObject)copy[section]!, alias);
            foreach (var cell in cells)
            {
                var record = (JObject)template.DeepClone();
                record["position"] = new JObject { ["row"] = cell.Row, ["col"] = cell.Col };
                record["direction"] = direction;
                list.Add(record);
            }
            return Parse(copy.ToString(Formatting.None));
        }

        public LevelDocument SavePredefined(string section, string collection, int index, string configuration, bool replace)
        {
            var entry = ParseObjectFragment(configuration);
            RequirePredefinedIdentity(entry);
            if (collection == "characterInsts" || collection == "tokenInsts")
            {
                if (entry["position"] is not JObject cell || !TryCellIndex(cell["row"], out var row) ||
                    !TryCellIndex(cell["col"], out var col) || row >= Height || col >= Width)
                    throw new FormatException("请填写地图内的预设坐标，或在地图上放置。");
                if (entry["direction"]?.ToString() is not ("UP" or "RIGHT" or "DOWN" or "LEFT" or "0" or "1" or "2" or "3"))
                    throw new FormatException("请选择有效的预设朝向。");
            }
            var copy = (JObject)root.DeepClone();
            var list = PredefinedCollection(copy, section, collection, create: !replace);
            CheckIndex(index, list.Count + (replace ? 0 : 1));
            var previous = replace ? list[index] as JObject : null;
            var alias = entry.Value<string>("alias");
            var oldAlias = previous?["alias"]?.Type == JTokenType.String ? previous["alias"]!.Value<string>() : null;
            if (alias != oldAlias)
                RequireUniquePresetAlias((JObject)copy[section]!, alias);
            var oldIdentity = EffectivePredefinedAlias(previous);
            if (!string.IsNullOrEmpty(oldIdentity) && oldIdentity != EffectivePredefinedAlias(entry) &&
                Actions(copy).Any(item => ReferencesPredefinedAlias(item.Item1, oldIdentity!, IsCardCollection(collection))))
                throw new InvalidOperationException("有波次或分支动作引用这个别名，请先调整引用。");
            if (replace) list[index] = entry;
            else list.Insert(index, entry);
            return Parse(copy.ToString(Formatting.None));
        }

        private static bool TryCellIndex(JToken? token, out int value)
        {
            value = -1;
            return token?.Type == JTokenType.Integer && int.TryParse(token.ToString(), out value) && value >= 0;
        }

        private static void RequirePredefinedIdentity(JObject entry)
        {
            if (entry["inst"] is not JObject inst || inst["characterKey"]?.Type != JTokenType.String ||
                string.IsNullOrWhiteSpace((string?)inst["characterKey"]))
                throw new FormatException("预设配置缺少 inst.characterKey。");
            if (entry["alias"]?.Type is not (null or JTokenType.String or JTokenType.Null))
                throw new FormatException("预设别名必须为字符串或 null。");
        }

        private static void RequireUniquePresetAlias(JObject layer, string? alias)
        {
            if (!string.IsNullOrEmpty(alias) && layer.Properties().Where(property => property.Value is JArray)
                .SelectMany(property => (JArray)property.Value).OfType<JObject>()
                .Any(record => record["alias"]?.Type == JTokenType.String && (string?)record["alias"] == alias))
                throw new InvalidOperationException("当前预设层已使用该别名。");
        }
    }
}
