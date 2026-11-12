#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Core
{
    /// <summary>Immutable LevelData document. Cell rows are bottom-origin.</summary>
    public sealed partial class LevelDocument
    {
        public const int MaximumUtf8Bytes = 16 * 1024 * 1024;
        public const int MaximumDepth = 64;
        public const int MaximumDimension = 256;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly JObject root;

        public int Width { get; }
        public int Height { get; }
        public string? LevelId => root["levelId"]?.Type == JTokenType.String ? root["levelId"]!.Value<string>() : null;

        private LevelDocument(JObject root)
        {
            this.root = root;
            var mapData = root["mapData"] as JObject ?? throw Error("Expected mapData object", "$.mapData");
            var map = mapData["map"] as JArray ?? throw Error("Expected map array", "$.mapData.map");
            var tiles = mapData["tiles"] as JArray ?? throw Error("Expected tiles array", "$.mapData.tiles");
            Height = map.Count;
            if (Height < 1 || Height > MaximumDimension) throw Error("Map height outside 1..256", "$.mapData.map");
            Width = (map[0] as JArray)?.Count ?? 0;
            if (Width < 1 || Width > MaximumDimension) throw Error("Map width outside 1..256", "$.mapData.map[0]");
            for (var row = 0; row < Height; row++)
            {
                if (!(map[row] is JArray cells) || cells.Count != Width)
                    throw Error("Map must be rectangular", "$.mapData.map[" + row + "]");
                for (var col = 0; col < Width; col++)
                {
                    var token = cells[col];
                    var path = "$.mapData.map[" + row + "][" + col + "]";
                    if (token.Type != JTokenType.Integer || !int.TryParse(token.ToString(), out var index) || index < 0 || index >= tiles.Count)
                        throw Error("Expected valid integer tile index", path);
                    if (!(tiles[index] is JObject)) throw Error("Referenced tile must be an object", "$.mapData.tiles[" + index + "]");
                }
            }
        }

        public static LevelDocument Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            try
            {
                if (json.Length > MaximumUtf8Bytes || StrictUtf8.GetByteCount(json) > MaximumUtf8Bytes)
                    throw Error("JSON exceeds 16 MiB", "$");
                var fractions = StrictJsonGuard.Validate(json);
                using var input = new StringReader(json);
                using var reader = new JsonTextReader(input)
                {
                    MaxDepth = MaximumDepth,
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Double
                };
                var token = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (!(token is JObject document)) throw Error("LevelData root must be an object", "$");
                foreach (var textValue in document.Descendants().OfType<JValue>().Where(v => v.Type == JTokenType.String))
                    StrictUtf8.GetByteCount(textValue.Value<string>()!);
                foreach (var property in document.Descendants().OfType<JProperty>()) StrictUtf8.GetByteCount(property.Name);
                // Preserve exact fraction lexemes, not the rounded intermediate double.
                // JRaw is generated only after strict syntax validation; edits re-enter Parse.
                var values = document.Descendants().OfType<JValue>().Where(v => v.Type == JTokenType.Float).ToArray();
                if (values.Length != fractions.Count) throw Error("Numeric token mismatch", "$");
                for (var i = 0; i < values.Length; i++)
                {
                    var value = values[i].Value<double>();
                    if (double.IsNaN(value) || double.IsInfinity(value)) throw Error("Numeric value outside finite range", values[i].Path);
                    values[i].Replace(new JRaw(fractions[i]));
                }
                return new LevelDocument(document);
            }
            catch (JsonException error)
            {
                var path = (error as JsonReaderException)?.Path;
                throw new LevelDocumentException("Invalid JSON: " + error.Message, string.IsNullOrEmpty(path) ? "$" : path!, error);
            }
            catch (EncoderFallbackException error)
            {
                throw new LevelDocumentException("JSON contains invalid Unicode", "$", error);
            }
        }

        public static LevelDocument Create(int width, int height)
        {
            if (width < 1 || width > MaximumDimension) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 1 || height > MaximumDimension) throw new ArgumentOutOfRangeException(nameof(height));
            var rows = new JArray();
            for (var row = 0; row < height; row++) rows.Add(new JArray(Enumerable.Repeat(0, width)));
            var tile = new JObject
            {
                ["tileKey"] = "tile_forbidden", ["heightType"] = "HIGHLAND", ["buildableType"] = "NONE",
                ["passableMask"] = "FLY_ONLY", ["playerSideMask"] = "ALL", ["blackboard"] = null, ["effects"] = null
            };
            return Parse(new JObject
            {
                ["levelId"] = "", ["mapId"] = "",
                ["options"] = CreateDefaultOptions(),
                ["mapData"] = new JObject { ["map"] = rows, ["tiles"] = new JArray(tile) },
                ["routes"] = new JArray(), ["extraRoutes"] = new JArray(), ["waves"] = new JArray(),
                ["enemyDbRefs"] = new JArray(), ["runes"] = new JArray(), ["optionalRunes"] = null,
                ["globalBuffs"] = new JArray(), ["branches"] = null
            }.ToString(Formatting.None));
        }

        // PC 2.7.81 Options::.ctor (0x18241EA80) and managed zero initialization.
        // Used for new documents only; Parse preserves fields exactly as imported.
        private static JObject CreateDefaultOptions() => new JObject
        {
            ["characterLimit"] = 99,
            ["maxLifePoint"] = 15,
            ["initialCost"] = 3,
            ["maxCost"] = 99,
            ["costIncreaseTime"] = 1.0,
            ["moveMultiplier"] = 0.5,
            ["steeringEnabled"] = true,
            ["reachableCheckIgnoreStartTile"] = false,
            ["isTrainingLevel"] = false,
            ["isHardTrainingLevel"] = false,
            ["isPredefinedCardsSelectable"] = false,
            ["displayRestTime"] = false,
            ["maxPlayTime"] = -1.0,
            ["functionDisableMask"] = 0,
            ["configBlackBoard"] = null
        };

        public JObject GetCell(int row, int col)
        {
            CheckPosition(row, col);
            var map = (JArray)root["mapData"]!["map"]!;
            var tiles = (JArray)root["mapData"]!["tiles"]!;
            var index = map[Height - 1 - row]![col]!.Value<int>();
            return (JObject)tiles[index].DeepClone();
        }

        public LevelDocument WithCell(int row, int col, JObject tile)
            => WithCells(new[] { new CellEdit(row, col, tile) });

        public LevelDocument WithCells(IEnumerable<CellEdit> edits)
        {
            if (edits == null) throw new ArgumentNullException(nameof(edits));
            var batch = new List<CellEdit>();
            foreach (var edit in edits)
            {
                if (batch.Count >= Width * Height) throw Error("Batch exceeds map cell count", "$edits");
                if (edit == null) throw new ArgumentException("Null cell edit", nameof(edits));
                CheckPosition(edit.Row, edit.Col);
                ValidateEdit(edit.Tile, 0);
                batch.Add(edit);
            }
            if (batch.Count == 0) return this;
            var copy = (JObject)root.DeepClone();
            var tiles = (JArray)copy["mapData"]!["tiles"]!;
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < tiles.Count; i++) indices[tiles[i].ToString(Formatting.None)] = i;
            foreach (var edit in batch)
            {
                var tile = edit.Tile;
                var key = tile.ToString(Formatting.None);
                if (!indices.TryGetValue(key, out var index))
                {
                    index = tiles.Count;
                    tiles.Add(tile);
                    indices.Add(key, index);
                }
                copy["mapData"]!["map"]![Height - 1 - edit.Row]![edit.Col] = index;
            }
            return Parse(copy.ToString(Formatting.None));
        }

        public JToken? GetSection(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            return root[key]?.DeepClone();
        }

        public LevelDocument WithSection(string key, string json)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Section name required", nameof(key));
            if (json == null) throw new ArgumentNullException(nameof(json));
            // Parse the fragment through the same strict boundary in a known-valid envelope.
            if (json.Length > MaximumUtf8Bytes) throw Error("Section exceeds size limit", "$fragment");
            StrictJsonGuard.Validate(json);
            var envelope = Parse("{\"mapData\":{\"map\":[[0]],\"tiles\":[{}]},\"fragment\":" + json + "}");
            var copy = (JObject)root.DeepClone();
            copy[key] = envelope.root["fragment"]!.DeepClone();
            return Parse(copy.ToString(Formatting.None));
        }

        public LevelDocument RemoveSection(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Section name required", nameof(key));
            var copy = (JObject)root.DeepClone();
            copy.Remove(key);
            return Parse(copy.ToString(Formatting.None));
        }

        public LevelDocument Resize(int width, int height, JObject? fill = null)
        {
            if (width < 1 || width > MaximumDimension) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 1 || height > MaximumDimension) throw new ArgumentOutOfRangeException(nameof(height));
            if (width == Width && height == Height) return this;
            ValidateMapCrop(root, width, height);
            var copy = (JObject)root.DeepClone();
            var oldMap = (JArray)root["mapData"]!["map"]!;
            var tiles = (JArray)copy["mapData"]!["tiles"]!;
            var fillIndex = tiles.Count;
            if (width > Width || height > Height)
            {
                var fillTile = fill ?? Create(1, 1).GetCell(0, 0);
                ValidateEdit(fillTile, 0);
                tiles.Add(fillTile.DeepClone());
            }
            var map = new JArray();
            for (var topRow = 0; topRow < height; topRow++)
            {
                var bottomRow = height - 1 - topRow;
                var cells = new JArray();
                for (var col = 0; col < width; col++)
                    cells.Add(bottomRow < Height && col < Width ? oldMap[Height - 1 - bottomRow]![col]!.DeepClone() : new JValue(fillIndex));
                map.Add(cells);
            }
            copy["mapData"]!["map"] = map;
            return Parse(copy.ToString(Formatting.None));
        }

        private static bool TryCoordinate(JToken token, out double value)
        {
            value = 0;
            return (token.Type == JTokenType.Integer || token.Type == JTokenType.Float || token.Type == JTokenType.Raw)
                && double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public LevelDocument WithLevelId(string levelId)
        {
            if (levelId == null) throw new ArgumentNullException(nameof(levelId));
            var copy = (JObject)root.DeepClone();
            copy["levelId"] = levelId;
            return Parse(copy.ToString(Formatting.None));
        }

        public string ToJson(bool indented = true)
        {
            var json = root.ToString(indented ? Formatting.Indented : Formatting.None);
            if (StrictUtf8.GetByteCount(json) > MaximumUtf8Bytes)
                throw Error("Formatted JSON exceeds 16 MiB; export compact JSON instead", "$");
            return json;
        }

        private void CheckPosition(int row, int col)
        {
            if (row < 0 || row >= Height) throw new ArgumentOutOfRangeException(nameof(row));
            if (col < 0 || col >= Width) throw new ArgumentOutOfRangeException(nameof(col));
        }

        private static void ValidateEdit(JToken token, int depth)
        {
            if (depth > MaximumDepth) throw Error("Edited tile exceeds maximum depth", "$tile");
            if (token.Type == JTokenType.Raw) StrictJsonGuard.Validate(token.ToString());
            if (token.Type == JTokenType.Float)
            {
                var value = token.Value<double>();
                if (double.IsNaN(value) || double.IsInfinity(value)) throw Error("Edited tile contains non-finite number", token.Path);
            }
            if (token.Type == JTokenType.Undefined || token.Type == JTokenType.Constructor || token.Type == JTokenType.Comment)
                throw Error("Edited tile contains a non-JSON token", token.Path);
            if (token is JContainer container)
                foreach (var child in container.Children()) ValidateEdit(child, depth + 1);
        }

        private static LevelDocumentException Error(string message, string path) => new LevelDocumentException(message, path);
    }
}
