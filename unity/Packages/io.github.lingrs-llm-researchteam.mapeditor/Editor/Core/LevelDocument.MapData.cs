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
        /// <summary>Replaces only mapData, preserving the immutable document and external sections.</summary>
        public LevelDocument WithMapData(string json)
        {
            var next = WithSection("mapData", json);
            ValidateMapDataIndices(next);
            ValidateChangedMapDataCollections(next);
            // The same conservative check protects both the resize command and raw JSON edits.
            // References removed from the original mapData must be repaired explicitly first.
            ValidateMapCrop(root, next.Width, next.Height);
            ValidateMapCrop(next.root, next.Width, next.Height);
            return next;
        }

        public LevelDocument WithMapDataField(string key, string json)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("MapData field name required", nameof(key));
            if (json == null) throw new ArgumentNullException(nameof(json));
            // Use the document's strict parser to retain raw number lexemes and reject duplicate keys.
            var value = Create(1, 1).WithSection("fragment", json).GetSection("fragment")!;
            var mapData = (JObject)GetSection("mapData")!;
            mapData[key] = value;
            return WithMapData(mapData.ToString(Formatting.None));
        }

        public LevelDocument RemoveMapDataField(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("MapData field name required", nameof(key));
            var mapData = (JObject)GetSection("mapData")!;
            if (mapData.Property(key) == null) return this;
            mapData.Remove(key);
            return WithMapData(mapData.ToString(Formatting.None));
        }

        /// <summary>Unverified spatial semantics; these warnings are not proof that a resize is safe.</summary>
        public IReadOnlyList<string> GetMapDataSpatialWarnings()
        {
            var warnings = new List<string>();
            var mapData = (JObject)root["mapData"]!;
            if (HasMapDataPayload(mapData["blockEdges"]))
                warnings.Add("mapData.blockEdges：仅检查已识别的 row/col 坐标，阻挡边的方向与边界语义尚未完整验证；不自动改写。");
            if (HasMapDataPayload(mapData["effects"]))
                warnings.Add("mapData.effects：效果 offset 的空间关联与资源可用性尚未验证；不随尺寸自动平移或删除。");
            if (HasMapDataPayload(mapData["layerRects"]))
                warnings.Add("mapData.layerRects：字符串格式、坐标方向与边界规则尚未验证；保留原文，不声明裁剪安全。");
            return warnings.AsReadOnly();
        }

        private static bool HasMapDataPayload(JToken? value)
            => value != null && value.Type != JTokenType.Null && (!(value is JArray array) || array.Count != 0);

        private static void ValidateMapDataIndices(LevelDocument document)
        {
            var grid = (JArray)document.root["mapData"]!["map"]!;
            for (var row = 0; row < grid.Count; row++)
            {
                var cells = (JArray)grid[row]!;
                for (var col = 0; col < cells.Count; col++)
                    if (cells[col]!.Value<int>() > short.MaxValue)
                        throw Error("Tile index exceeds native Int16 range (0..32767)", "$.mapData.map[" + row + "][" + col + "]");
            }
        }

        private void ValidateChangedMapDataCollections(LevelDocument next)
        {
            var before = (JObject)root["mapData"]!;
            var after = (JObject)next.root["mapData"]!;
            foreach (var key in new[] { "blockEdges", "tags", "effects", "layerRects" })
            {
                var value = after[key];
                // Imported nonstandard values remain editable through unrelated fields.
                if (JToken.DeepEquals(before[key], value) || value == null || value.Type == JTokenType.Null) continue;
                if (!(value is JArray array)) throw Error("Expected array or null", "$.mapData." + key);
                var expected = key == "tags" || key == "layerRects" ? JTokenType.String : JTokenType.Object;
                for (var i = 0; i < array.Count; i++)
                    if (array[i]!.Type != expected && array[i]!.Type != JTokenType.Null)
                        throw Error("Expected " + expected + " or null entry", "$.mapData." + key + "[" + i + "]");
            }
        }

        private void ValidateMapCrop(JObject documentRoot, int width, int height)
        {
            if (width >= Width && height >= Height) return;
            // Unknown coordinate-shaped extension data is conservative: do not clamp it.
            foreach (var position in documentRoot.Descendants().OfType<JObject>())
            {
                if (position["row"] == null || position["col"] == null) continue;
                if (!TryCoordinate(position["row"]!, out var row) || !TryCoordinate(position["col"]!, out var col))
                    throw Error("Cannot prove coordinate safety during crop", position.Path);
                if (row >= 0 && row < Height && col >= 0 && col < Width && (row >= height || col >= width))
                    throw Error("Crop would remove a referenced coordinate; edit it explicitly first", position.Path);
            }
        }
    }
}
