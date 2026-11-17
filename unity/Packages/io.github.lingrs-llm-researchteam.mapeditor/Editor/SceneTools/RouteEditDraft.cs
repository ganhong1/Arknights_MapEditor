#nullable enable
using System;
using System.Collections.Generic;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Arknights.MapEditor.Editor
{
    internal enum RouteCheckpointType
    {
        MOVE = 0,
        WAIT_FOR_SECONDS = 1,
        WAIT_FOR_PLAY_TIME = 2,
        WAIT_CURRENT_FRAGMENT_TIME = 3,
        WAIT_CURRENT_WAVE_TIME = 4,
        DISAPPEAR = 5,
        APPEAR_AT_POS = 6,
        ALERT = 7,
        PATROL_MOVE = 8,
        WAIT_BOSSRUSH_WAVE = 9,
        MAP_OFFSET_MOVE = 10
    }

    internal sealed class RouteEditDraft
    {
        internal const int Start = -1;
        internal const int End = -2;
        internal string Library { get; }
        internal int Index { get; }
        internal JObject Route { get; private set; }
        internal bool Complete => TryCell(Route["startPosition"], out _) && TryCell(Route["endPosition"], out _);
        internal JArray? Checkpoints => Route["checkpoints"] as JArray;

        internal RouteEditDraft(string library, int index, JObject route)
        {
            Library = library;
            Index = index;
            Route = (JObject)route.DeepClone();
        }

        internal static JObject Create(string motion) => new JObject
        {
            ["motionMode"] = motion,
            ["startPosition"] = null,
            ["endPosition"] = null,
            ["spawnOffset"] = ZeroOffset(),
            ["spawnRandomRange"] = ZeroOffset(),
            ["checkpoints"] = new JArray(),
            ["allowDiagonalMove"] = true,
            ["visitEveryTileCenter"] = false,
            ["visitEveryNodeCenter"] = false,
            ["visitEveryCheckPoint"] = false
        };

        internal static JObject Point(Vector2Int cell) => new JObject { ["row"] = cell.y, ["col"] = cell.x };
        private static JObject ZeroOffset() => new JObject { ["x"] = 0, ["y"] = 0 };

        internal void Replace(JObject value) => Route = (JObject)value.DeepClone();

        internal void Place(Vector2Int cell)
        {
            if (!TryCell(Route["startPosition"], out _))
                Route["startPosition"] = Point(cell);
            else
            {
                if (TryCell(Route["endPosition"], out var previous))
                    Insert(Checkpoints?.Count ?? 0, "MOVE", previous);
                Route["endPosition"] = Point(cell);
            }
        }

        internal void RemoveLastPoint()
        {
            var list = Checkpoints;
            if (!TryCell(Route["endPosition"], out _))
                Route["startPosition"] = null;
            else if (list is { Count: > 0 } && list[list.Count - 1] is JObject last &&
                     PrtsRoutePreview.Matches(last["type"], "MOVE", "0") && TryCell(last["position"], out var previous))
            {
                Route["endPosition"] = Point(previous);
                list.RemoveAt(list.Count - 1);
            }
            else Route["endPosition"] = null;
        }

        internal JObject MovedRoute(int node, Vector2Int cell)
        {
            var copy = (JObject)Route.DeepClone();
            var position = Position(copy, node);
            if (position == null)
            {
                if (node == Start) copy["startPosition"] = Point(cell);
                else if (node == End) copy["endPosition"] = Point(cell);
                else if (UsesPosition(ReadCheckpointType(Node(node)?["type"])))
                    copy["checkpoints"]![node]!["position"] = Point(cell);
                else throw new InvalidOperationException("此节点没有地图坐标。");
                return copy;
            }
            position["row"] = cell.y;
            position["col"] = cell.x;
            return copy;
        }

        internal void Insert(int index, string type, Vector2Int? cell)
        {
            var list = RequireCheckpoints();
            var step = new JObject
            {
                ["type"] = type,
                ["time"] = 0,
                ["position"] = Point(cell ?? Vector2Int.zero),
                ["reachOffset"] = ZeroOffset(),
                ["randomizeReachOffset"] = false,
                ["reachDistance"] = 0
            };
            list.Insert(index, step);
        }

        internal void Remove(int index) => RequireCheckpoints().RemoveAt(index);

        internal void Reorder(int from, int to)
        {
            var list = RequireCheckpoints();
            var step = list[from];
            list.RemoveAt(from);
            list.Insert(to, step);
        }

        internal JObject? Node(int index) => Checkpoints is JArray list && index >= 0 && index < list.Count
            ? list[index] as JObject : null;

        internal static RouteCheckpointType? ReadCheckpointType(JToken? value)
        {
            if (value == null || value.Type is not (JTokenType.String or JTokenType.Integer)) return null;
            var raw = value.ToString();
            if (raw.IndexOf(',') < 0 && Enum.TryParse<RouteCheckpointType>(raw, true, out var type) &&
                Enum.IsDefined(typeof(RouteCheckpointType), type)) return type;
            return null;
        }

        internal static bool UsesPosition(RouteCheckpointType? type) => type is
            RouteCheckpointType.MOVE or RouteCheckpointType.APPEAR_AT_POS or
            RouteCheckpointType.PATROL_MOVE or RouteCheckpointType.MAP_OFFSET_MOVE;

        internal void SetCheckpointType(int index, RouteCheckpointType type)
        {
            var step = Node(index) ?? throw new InvalidOperationException("请选择有效的检查点。");
            if (ReadCheckpointType(step["type"]) == type) return;
            step["type"] = step["type"]?.Type == JTokenType.Integer
                ? new JValue((int)type) : new JValue(type.ToString());
            if (UsesPosition(type) && (step["position"] == null || step["position"]!.Type == JTokenType.Null))
                step["position"] = Point(SuggestedCheckpointCell(index));
            if ((step["time"] == null || step["time"]!.Type == JTokenType.Null) && type is
                (RouteCheckpointType.WAIT_FOR_SECONDS or RouteCheckpointType.WAIT_FOR_PLAY_TIME or
                RouteCheckpointType.WAIT_CURRENT_FRAGMENT_TIME or RouteCheckpointType.WAIT_CURRENT_WAVE_TIME or
                RouteCheckpointType.WAIT_BOSSRUSH_WAVE))
                step["time"] = 0;
        }

        internal Vector2Int SuggestedCheckpointCell(int index)
        {
            for (var previous = index - 1; previous >= 0; previous--)
                if (TryNodeCell(previous, out var cell)) return cell;
            return TryNodeCell(Start, out var start) ? start : Vector2Int.zero;
        }

        internal bool TryNodeCell(int index, out Vector2Int cell) => TryCell(Position(Route, index), out cell);

        internal IEnumerable<(int Index, Vector3 Point)> Handles()
        {
            if (TryNodeCell(Start, out var start)) yield return (Start, World(start, Route["spawnOffset"]));
            for (var i = 0; i < (Checkpoints?.Count ?? 0); i++)
            {
                var step = Node(i);
                if (ReadCheckpointType(step?["type"]) is (RouteCheckpointType.MOVE or RouteCheckpointType.APPEAR_AT_POS) &&
                    TryNodeCell(i, out var point)) yield return (i, World(point, step?["reachOffset"]));
            }
            if (TryNodeCell(End, out var end)) yield return (End, World(end, null));
        }

        private static JObject? Position(JObject route, int node)
        {
            if (node == Start) return route["startPosition"] as JObject;
            if (node == End) return route["endPosition"] as JObject;
            if (node < 0 || route["checkpoints"] is not JArray checkpoints || node >= checkpoints.Count ||
                checkpoints[node] is not JObject step) return null;
            return UsesPosition(ReadCheckpointType(step["type"])) ? step["position"] as JObject : null;
        }

        private Vector3 World(Vector2Int cell, JToken? offset) => PrtsRoutePreview.World(cell, offset,
            PrtsRoutePreview.Matches(Route["motionMode"], "FLY", "1"));

        internal static bool TryCell(JToken? position, out Vector2Int cell)
        {
            cell = default;
            if (position is not JObject point) return false;
            var row = PrtsRoutePreview.Integer(point["row"], int.MinValue);
            var col = PrtsRoutePreview.Integer(point["col"], int.MinValue);
            if (row == int.MinValue || col == int.MinValue) return false;
            cell = new Vector2Int(col, row);
            return true;
        }

        private JArray RequireCheckpoints()
        {
            if (Route["checkpoints"] is JArray list) return list;
            if (Route["checkpoints"] != null && Route["checkpoints"]!.Type != JTokenType.Null)
                throw new InvalidOperationException("请在完整路线 JSON 中修正 checkpoints。");
            var created = new JArray();
            Route["checkpoints"] = created;
            return created;
        }

        internal LevelDocument Apply(LevelDocument document)
        {
            if (!Complete) throw new InvalidOperationException("请先设置有效的起点和终点。");
            var section = document.GetSection(Library);
            var routes = section as JArray ?? (section == null || section.Type == JTokenType.Null ? new JArray() :
                throw new InvalidOperationException("路线库不是数组。"));
            if (Index < 0) routes.Add(Route.DeepClone());
            else if (Index < routes.Count && routes[Index] is JObject) routes[Index] = Route.DeepClone();
            else throw new InvalidOperationException("路线索引已变更，请重新读取。");
            return document.WithSection(Library, routes.ToString(Formatting.None));
        }
    }
}
