#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Core
{
    public sealed partial class LevelDocument
    {
        public JToken? GetMapRules(string field)
        {
            RequireMapRuleField(field);
            return (field == "blockEdges" ? root["mapData"]![field] : root[field])?.DeepClone();
        }

        public LevelDocument AddMapRule(string field, string entryJson)
        {
            var rows = MapRuleArray(field, true);
            var entry = ParseMapRuleValue(entryJson);
            ValidateMapRule(field, entry, null);
            rows.Add(entry);
            return WriteMapRules(field, rows);
        }

        public LevelDocument UpdateMapRule(string field, int index, string entryJson)
        {
            var rows = MapRuleArray(field, false);
            RequireMapRuleIndex(rows, index);
            var entry = ParseMapRuleValue(entryJson);
            ValidateMapRule(field, entry, rows[index]);
            rows[index] = entry;
            return WriteMapRules(field, rows);
        }

        public LevelDocument PatchMapRule(string field, int index, JObject patch)
        {
            if (patch == null) throw new ArgumentNullException(nameof(patch));
            var rows = MapRuleArray(field, false);
            RequireMapRuleIndex(rows, index);
            if (rows[index] is not JObject record) throw Error("Expected rule object", field + "[" + index + "]");
            void Merge(JObject target, JObject changes)
            {
                foreach (var property in changes.Properties())
                    if (target[property.Name] is JObject child && property.Value is JObject delta) Merge(child, delta);
                    else target[property.Name] = property.Value.DeepClone();
            }
            var candidate = (JObject)record.DeepClone();
            Merge(candidate, patch);
            return UpdateMapRule(field, index, candidate.ToString(Formatting.None));
        }

        public LevelDocument DeleteMapRule(string field, int index)
        {
            var rows = MapRuleArray(field, false);
            RequireMapRuleIndex(rows, index);
            rows.RemoveAt(index);
            return WriteMapRules(field, rows);
        }

        public LevelDocument SetMapRules(string field, string json)
        {
            var previous = GetMapRules(field);
            var value = ParseMapRuleValue(json);
            if (JToken.DeepEquals(previous, value)) return this;
            if (value.Type == JTokenType.Null) return WriteMapRules(field, value);
            if (value is not JArray rows) throw Error("Expected array or null", field);
            var old = previous as JArray;
            var unchanged = old == null ? null : new HashSet<JToken>(old, JToken.EqualityComparer);
            for (var i = 0; i < rows.Count; i++)
            {
                // An unchanged imported row remains valid when another row is deleted or reordered.
                if (unchanged?.Contains(rows[i]) == true) continue;
                ValidateMapRule(field, rows[i], old != null && i < old.Count ? old[i] : null);
            }
            return WriteMapRules(field, rows);
        }

        public LevelDocument RemoveMapRules(string field)
        {
            RequireMapRuleField(field);
            if (field != "blockEdges") return RemoveSection(field);
            var map = (JObject)GetSection("mapData")!;
            map.Remove(field);
            return WithSection("mapData", map.ToString(Formatting.None));
        }

        private LevelDocument WriteMapRules(string field, JToken value)
        {
            RequireMapRuleField(field);
            if (field != "blockEdges") return WithSection(field, value.ToString(Formatting.None));
            var map = (JObject)GetSection("mapData")!;
            map[field] = value.DeepClone();
            return WithSection("mapData", map.ToString(Formatting.None));
        }

        private JArray MapRuleArray(string field, bool create)
        {
            var value = GetMapRules(field);
            if (create && (value == null || value.Type == JTokenType.Null)) return new JArray();
            return value as JArray ?? throw Error("Expected rule array", field);
        }

        private static JToken ParseMapRuleValue(string json) => Create(1, 1).WithSection("rule", json).GetSection("rule")!;
        private static void RequireMapRuleField(string field)
        {
            if (field != "blockEdges" && field != "tilesDisallowToLocate")
                throw new ArgumentException("Unknown map rule field", nameof(field));
        }
        private static void RequireMapRuleIndex(JArray rows, int index)
        {
            if (index < 0 || index >= rows.Count) throw new ArgumentOutOfRangeException(nameof(index));
        }

        private static int? ReadMapRuleInteger(JToken? value) =>
            value?.Type == JTokenType.Integer && int.TryParse(value.ToString(Formatting.None),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

        private void ValidateMapRule(string field, JToken candidate, JToken? previous)
        {
            if (JToken.DeepEquals(candidate, previous)) return;
            if (candidate is not JObject value) throw Error("Expected rule object", field);
            var old = previous as JObject;
            var position = field == "blockEdges" ? value["pos"] : value;
            var oldPosition = field == "blockEdges" ? old?["pos"] : old;
            if (old == null || !JToken.DeepEquals(position, oldPosition))
            {
                if (position is not JObject pos) throw Error("Expected row/col position", field);
                foreach (var coordinate in new[] { "row", "col" })
                {
                    if (oldPosition is JObject oldPos && JToken.DeepEquals(pos[coordinate], oldPos[coordinate])) continue;
                    var number = ReadMapRuleInteger(pos[coordinate]);
                    if (number == null || number < 0 || number >= (coordinate == "row" ? Height : Width))
                        throw Error("Coordinate outside map", field + "." + coordinate);
                }
            }
            if (field != "blockEdges") return;
            ValidateEnum("direction", new[] { "UP", "RIGHT", "DOWN", "LEFT" });
            ValidateEnum("blockMask", new[] { "NONE", "WALK_ONLY", "FLY_ONLY", "ALL" });
            void ValidateEnum(string key, string[] names)
            {
                if (old != null && JToken.DeepEquals(value[key], old[key])) return;
                var number = ReadMapRuleInteger(value[key]);
                var token = value[key];
                if (number is >= 0 and <= 3 || token?.Type == JTokenType.String && names.Contains(token.Value<string>())) return;
                throw Error("Expected native enum name or integer 0..3", field + "." + key);
            }
        }
    }
}
