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
        public LevelDocument AddTileConfiguration(JObject tile)
        {
            var map = (JObject)GetSection("mapData")!;
            ((JArray)map["tiles"]!).Add(tile.DeepClone());
            return WithSection("mapData", map.ToString(Formatting.None));
        }

        public LevelDocument EditTileConfiguration(int index, JObject tile)
        {
            var map = (JObject)GetSection("mapData")!;
            var tiles = (JArray)map["tiles"]!;
            if (index < 0 || index >= tiles.Count) throw new ArgumentOutOfRangeException(nameof(index));
            tiles[index] = tile.DeepClone();
            return WithSection("mapData", map.ToString(Formatting.None));
        }

        public LevelDocument DeleteTileConfiguration(int index)
        {
            var map = (JObject)GetSection("mapData")!;
            RemoveTile(map, index);
            return WithSection("mapData", map.ToString(Formatting.None));
        }

        public LevelDocument BindTileConfiguration(int index, IEnumerable<(int Row, int Col)> positions, bool cleanup)
            => ReplaceTileBindings(index, null, positions, cleanup);

        public LevelDocument PlaceTileConfigurations(JObject tile, IEnumerable<(int Row, int Col)> positions, bool cleanup)
            => ReplaceTileBindings(null, tile, positions, cleanup);

        private LevelDocument ReplaceTileBindings(int? index, JObject? tile, IEnumerable<(int Row, int Col)> positions, bool cleanup)
        {
            var cells = positions.Distinct().ToArray();
            foreach (var cell in cells) CheckPosition(cell.Row, cell.Col);
            var map = (JObject)GetSection("mapData")!;
            var tiles = (JArray)map["tiles"]!;
            var grid = (JArray)map["map"]!;
            if (index.HasValue && (index < 0 || index >= tiles.Count)) throw new ArgumentOutOfRangeException(nameof(index));
            var replaced = new HashSet<int>();
            foreach (var cell in cells)
            {
                var row = (JArray)grid[Height - 1 - cell.Row]!;
                var old = row[cell.Col]!.Value<int>();
                if (old == index) continue;
                replaced.Add(old);
                // Scene placement creates an independent configuration for each cell.
                // Sharing happens only through an explicit binding operation.
                var next = index ?? tiles.Count;
                if (!index.HasValue) tiles.Add(tile!.DeepClone());
                row[cell.Col] = next;
            }
            if (cleanup)
                foreach (var old in replaced.OrderByDescending(value => value))
                    if (!IsReferenced(grid, old)) RemoveTile(map, old);
            return WithSection("mapData", map.ToString(Formatting.None));
        }

        private static bool IsReferenced(JArray grid, int index)
            => grid.Children<JArray>().SelectMany(row => row).Any(value => value.Value<int>() == index);

        private static void RemoveTile(JObject map, int index)
        {
            var tiles = (JArray)map["tiles"]!;
            var grid = (JArray)map["map"]!;
            if (index < 0 || index >= tiles.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (IsReferenced(grid, index)) throw new InvalidOperationException("配置仍有绑定格，请先为这些格绑定替代配置。");
            tiles.RemoveAt(index);
            foreach (var row in grid.Children<JArray>())
                for (var col = 0; col < row.Count; col++)
                    if (row[col]!.Value<int>() > index) row[col] = row[col]!.Value<int>() - 1;
        }
    }
}
