#nullable enable
using System.Collections.Generic;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Arknights.MapEditor.Editor
{
    internal sealed class PrtsRouteGrid
    {
        private static readonly Vector2Int[] Directions =
        {
            new(0, 1), new(1, 1), new(1, 0), new(1, -1),
            new(0, -1), new(-1, -1), new(-1, 0), new(-1, 1)
        };

        private static readonly HashSet<string> BlockingPresets = new()
        {
            "trap_001_crate", "trap_002_emp", "trap_008_farm", "trap_027_stone",
            "trap_032_mound", "trap_037_airsup"
        };

        private readonly int width, height;
        private readonly string[] keys;
        private readonly bool[] walkable;
        private readonly bool hasUnmodeledEdges;

        internal PrtsRouteGrid(LevelDocument document)
        {
            width = document.Width;
            height = document.Height;
            keys = new string[width * height];
            walkable = new bool[keys.Length];
            if (document.GetSection("mapData") is JObject map)
            {
                hasUnmodeledEdges = map["blockEdges"] is JArray edges && edges.Count != 0;
                var rows = (JArray)map["map"]!;
                var tiles = (JArray)map["tiles"]!;
                for (var row = 0; row < height; ++row)
                    for (var col = 0; col < width; ++col)
                    {
                        var cell = row * width + col;
                        var tile = (JObject)tiles[rows[height - row - 1]![col]!.Value<int>()];
                        keys[cell] = tile["tileKey"]?.ToString() ?? "";
                        var mask = tile["passableMask"]?.ToString();
                        walkable[cell] = mask == "WALK_ONLY" || mask == "ALL" ||
                            (PrtsRoutePreview.Integer(tile["passableMask"], 0) & 1) != 0;
                    }
            }
            if ((document.GetSection("predefines") as JObject)?["tokenInsts"] is not JArray presets) return;
            foreach (var preset in presets)
            {
                if (preset is not JObject item || item["position"] is not JObject position ||
                    PrtsRoutePreview.Matches(item["hidden"], "true", "1") ||
                    !BlockingPresets.Contains((item["inst"] as JObject)?["characterKey"]?.ToString() ?? "")) continue;
                var cell = new Vector2Int(PrtsRoutePreview.Integer(position["col"], -1),
                    PrtsRoutePreview.Integer(position["row"], -1));
                if (Contains(cell)) walkable[Index(cell)] = false;
            }
        }

        internal List<Vector2Int>? Find(Vector2Int from, Vector2Int to, bool diagonal, bool visit, bool keepCenters)
        {
            if (hasUnmodeledEdges || !CanWalk(from, visit) || !CanWalk(to, visit)) return null;
            var start = Index(from);
            var target = Index(to);
            var cost = new double[keys.Length];
            var parents = new int[keys.Length];
            var incoming = new int[keys.Length];
            var visited = new bool[keys.Length];
            for (var i = 0; i < cost.Length; ++i) cost[i] = double.PositiveInfinity;
            var open = new SortedSet<(double Cost, long Order, int Cell)>();
            var queued = new Dictionary<int, List<(double Cost, long Order, int Cell)>>();
            long order = 0;
            cost[start] = 0;
            open.Add((0, order++, start));

            while (open.Count > 0)
            {
                var current = open.Min;
                open.Remove(current);
                if (queued.TryGetValue(current.Cell, out var pending)) pending.Remove(current);
                if (current.Cell == target)
                {
                    var points = new List<Vector2Int> { to };
                    var next = target;
                    while (next != start)
                    {
                        next = parents[next];
                        points.Add(Position(next));
                    }
                    points.Reverse();
                    return keepCenters ? points : Simplify(points, visit);
                }
                visited[current.Cell] = true;
                var cell = Position(current.Cell);
                for (var direction = 0; direction < Directions.Length; direction += diagonal ? 1 : 2)
                {
                    var next = cell + Directions[direction];
                    if (!CanWalk(next, visit)) continue;
                    var index = Index(next);
                    if (visited[index]) continue;
                    var acrossCorner = (direction & 1) != 0;
                    if (diagonal && !CanCross(cell, next, acrossCorner, visit)) continue;
                    var extra = diagonal ? keys[index] switch
                    {
                        "tile_hole" => 1e6,
                        "tile_yinyang_switch" => -1,
                        _ => 0
                    } : 0;
                    var nextCost = current.Cost + (acrossCorner ? 1.414 : 1) + extra;
                    var back = (direction + 4) % Directions.Length;
                    if (nextCost < cost[index] || (nextCost == cost[index] && back < incoming[index]))
                    {
                        cost[index] = nextCost;
                        parents[index] = current.Cell;
                        incoming[index] = back;
                    }
                    if (!queued.TryGetValue(index, out var candidates))
                    {
                        candidates = new List<(double, long, int)>();
                        queued.Add(index, candidates);
                    }
                    // PRTSMap replaces prior queued visits when inserting inside the queue.
                    // An append retains those visits, while the predecessor keeps its own best cost.
                    if (open.Count != 0 && nextCost <= open.Max.Cost)
                    {
                        foreach (var candidate in candidates) open.Remove(candidate);
                        candidates.Clear();
                    }
                    var visitCandidate = (nextCost, order++, index);
                    candidates.Add(visitCandidate);
                    open.Add(visitCandidate);
                }
            }
            return null;
        }

        private bool CanCross(Vector2Int cell, Vector2Int next, bool diagonal, bool visit)
        {
            var flag = PassFlag(cell);
            if (diagonal)
            {
                var a = new Vector2Int(cell.x, next.y);
                var b = new Vector2Int(next.x, cell.y);
                return CanWalk(a, visit) && CanWalk(b, visit) &&
                    flag == PassFlag(a) && flag == PassFlag(b) && flag == PassFlag(next);
            }
            return flag == PassFlag(next) || IsStair(cell) || IsStair(next);
        }

        private List<Vector2Int> Simplify(List<Vector2Int> path, bool visit)
        {
            var points = new List<Vector2Int> { path[0] };
            var anchor = 0;
            for (var i = 2; i < path.Count; ++i)
            {
                if (!IsStair(path[i - 1]) && ClearRectangle(path[anchor], path[i], visit)) continue;
                points.Add(path[i - 1]);
                anchor = i - 1;
            }
            if (path.Count > 1) points.Add(path[path.Count - 1]);
            return points;
        }

        private bool ClearRectangle(Vector2Int from, Vector2Int to, bool visit)
        {
            var flag = PassFlag(from);
            for (var row = Mathf.Min(from.y, to.y); row <= Mathf.Max(from.y, to.y); ++row)
                for (var col = Mathf.Min(from.x, to.x); col <= Mathf.Max(from.x, to.x); ++col)
                {
                    var point = new Vector2Int(col, row);
                    if (!CanWalk(point, visit) || flag != PassFlag(point)) return false;
                }
            return true;
        }

        private bool Contains(Vector2Int cell) => cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;
        private int Index(Vector2Int cell) => cell.y * width + cell.x;
        private Vector2Int Position(int index) => new(index % width, index / width);
        private bool IsStair(Vector2Int cell) => keys[Index(cell)] == "tile_stairs";
        private bool PassFlag(Vector2Int cell) => keys[Index(cell)] is "tile_passable_wall" or "tile_passable_wall_forbidden";
        private bool CanWalk(Vector2Int cell, bool visit)
        {
            if (!Contains(cell)) return false;
            var index = Index(cell);
            return walkable[index] && keys[index] is not ("tile_empty" or "tile_fence_bound") &&
                (visit || keys[index] != "tile_hole");
        }
    }
}
