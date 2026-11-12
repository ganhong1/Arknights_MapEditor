#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Arknights.MapEditor.Editor
{
    internal enum RoutePreviewMode { Single, Time, Order }
    internal enum RoutePreviewSpeed { Normal, Slow, VerySlow, Constant }
    internal enum RouteMarkerKind { Start, End, Checkpoint, Wait, Disappear, Appear, Alert }

    internal sealed class PrtsRoutePreview
    {
        internal sealed class Entry
        {
            internal readonly string Library;
            internal readonly int RouteIndex;
            internal readonly string Enemy;
            internal readonly double Time;
            internal readonly long First;
            internal readonly int Count;
            internal readonly string Group;
            internal Entry(string library, int index, string enemy, double time, long first, int count, string group)
            {
                Library = library;
                RouteIndex = index;
                Enemy = enemy;
                Time = time;
                First = first;
                Count = count;
                Group = group;
            }
        }

        internal sealed class Path
        {
            internal readonly bool Air;
            internal readonly List<Leg> Legs = new();
            internal readonly List<Marker> Markers = new();
            internal float Length { get; private set; }
            internal bool HasFallback { get; private set; }
            internal Path(bool air) => Air = air;

            internal void Add(Vector3 from, Vector3 to, bool fallback)
            {
                var length = Vector3.Distance(from, to);
                if (length < .0001f) return;
                Legs.Add(new Leg(from, to, Length, length, fallback));
                Length += length;
                HasFallback |= fallback;
            }
        }

        internal readonly struct Leg
        {
            internal readonly Vector3 From, To;
            internal readonly float Offset, Length;
            internal readonly bool Fallback;
            internal Leg(Vector3 from, Vector3 to, float offset, float length, bool fallback)
                => (From, To, Offset, Length, Fallback) = (from, to, offset, length, fallback);
        }

        internal readonly struct Marker
        {
            internal readonly Vector3 Point;
            internal readonly RouteMarkerKind Kind;
            internal readonly string Label;
            internal readonly float Offset;
            internal Marker(Vector3 point, RouteMarkerKind kind, string label, float offset)
                => (Point, Kind, Label, Offset) = (point, kind, label, offset);
        }

        private readonly LevelDocument document;
        private readonly Dictionary<string, JArray> libraries = new();
        private readonly Dictionary<(string, int), Path?> paths = new();
        private PrtsRouteGrid? grid;
        internal readonly List<Entry> Spawns = new();
        internal readonly List<Entry> BranchSpawns = new();
        internal readonly List<Entry> Definitions = new();

        internal PrtsRoutePreview(LevelDocument document, bool includeHidden)
        {
            this.document = document;
            foreach (var library in new[] { "routes", "extraRoutes" })
            {
                if (document.GetSection(library) is not JArray routes) continue;
                libraries.Add(library, routes);
                for (var i = 0; i < routes.Count; ++i)
                    if (IsRoute(routes[i])) Definitions.Add(new Entry(library, i, "", i, i + 1, 1, ""));
            }

            long number = 1;
            var time = 0d;
            if (document.GetSection("waves") is JArray waves)
                foreach (var wave in waves.OfType<JObject>())
                {
                    time += Number(wave["preDelay"]);
                    time = ReadFragments(wave["fragments"], time, "routes", "", includeHidden, Spawns, ref number);
                    time += Number(wave["postDelay"]) + 5;
                }
            if (document.GetSection("branches") is JObject branches)
                foreach (var branch in branches.Properties())
                {
                    number = 1;
                    var fragments = branch.Value as JArray ?? (branch.Value as JObject)?["phases"];
                    ReadFragments(fragments, 0, "extraRoutes", branch.Name, includeHidden, BranchSpawns, ref number);
                }
        }

        private double ReadFragments(JToken? fragments, double time, string library, string group, bool includeHidden,
            List<Entry> entries, ref long number)
        {
            if (fragments is not JArray array) return time;
            foreach (var fragment in array.OfType<JObject>())
            {
                time += Number(fragment["preDelay"]);
                var end = time;
                if (fragment["actions"] is not JArray actions) continue;
                foreach (var action in actions.OfType<JObject>().OrderBy(action => Number(action["preDelay"])))
                {
                    var start = time + Number(action["preDelay"]);
                    var count = Mathf.Max(1, Integer(action["count"], 1));
                    end = Math.Max(end, start + (count - 1) * Number(action["interval"]));
                    if (!Matches(action["actionType"], "SPAWN", "0")) continue;
                    var hidden = action["hiddenGroup"]?.ToString();
                    if (!includeHidden && !string.IsNullOrEmpty(hidden)) continue;
                    var actionLibrary = Matches(action["useExtraRoute"], "true", "1") ? "extraRoutes" : library;
                    var index = Integer(action["routeIndex"], -1);
                    if (!libraries.TryGetValue(actionLibrary, out var routes) || index < 0 || index >= routes.Count ||
                        !IsRoute(routes[index])) continue;
                    entries.Add(new Entry(actionLibrary, index, action["key"]?.ToString() ?? "", start, number, count, group));
                    number += count;
                }
                time = end;
            }
            return time;
        }

        internal static List<int> Window(IReadOnlyList<Entry> entries, int selected, RoutePreviewMode mode, float before, float after)
        {
            var result = new List<int>();
            if (selected < 0 || selected >= entries.Count) return result;
            var focus = entries[selected];
            for (var i = 0; i < entries.Count; ++i)
            {
                if (i == selected) { result.Add(i); continue; }
                var entry = entries[i];
                if (mode == RoutePreviewMode.Single || entry.Group != focus.Group) continue;
                var distance = mode == RoutePreviewMode.Time ? entry.Time - focus.Time : i - selected;
                if (distance >= -before && distance <= after) result.Add(i);
            }
            return result;
        }

        internal Path? GetPath(Entry entry)
        {
            var key = (entry.Library, entry.RouteIndex);
            if (paths.TryGetValue(key, out var cached)) return cached;
            var route = (JObject)libraries[entry.Library][entry.RouteIndex];
            var path = BuildPath(route);
            paths.Add(key, path);
            return path;
        }

        private Path? BuildPath(JObject route)
        {
            var air = Matches(route["motionMode"], "FLY", "1");
            if (!TryPoint(route["startPosition"], route["spawnOffset"], air, out var startCell, out var start) ||
                !TryPoint(route["endPosition"], null, air, out var endCell, out var end)) return null;
            var path = new Path(air);
            var point = start;
            var cell = startCell;
            var hidden = false;
            var diagonal = Matches(route["allowDiagonalMove"], "true", "1");
            var visit = Matches(route["visitEveryCheckPoint"], "true", "1");
            var centers = Matches(route["visitEveryTileCenter"], "true", "1") ||
                Matches(route["visitEveryNodeCenter"], "true", "1");
            void Mark(RouteMarkerKind kind, string label = "") => path.Markers.Add(new Marker(point, kind, label, path.Length));
            void Travel(Vector2Int targetCell, Vector3 target)
            {
                if (air) path.Add(point, target, false);
                else
                {
                    grid ??= new PrtsRouteGrid(document);
                    var points = grid.Find(cell, targetCell, diagonal, visit, centers);
                    if (points == null) path.Add(point, target, true);
                    else
                    {
                        for (var i = 1; i < points.Count - 1; ++i)
                        {
                            var next = World(points[i], null, air);
                            path.Add(point, next, false);
                            point = next;
                        }
                        path.Add(point, target, false);
                    }
                }
                cell = targetCell;
                point = target;
            }

            Mark(RouteMarkerKind.Start);
            if (route["checkpoints"] is JArray checkpoints)
                foreach (var step in checkpoints.OfType<JObject>())
                {
                    var type = step["type"]?.ToString();
                    var time = Number(step["time"]).ToString("0.##", CultureInfo.InvariantCulture);
                    switch (type)
                    {
                        case "1": case "WAIT_FOR_SECONDS": Mark(RouteMarkerKind.Wait, time + "s"); break;
                        case "3": case "WAIT_CURRENT_FRAGMENT_TIME": Mark(RouteMarkerKind.Wait, "片段 " + time + "s"); break;
                        case "4": case "WAIT_CURRENT_WAVE_TIME": Mark(RouteMarkerKind.Wait, "波次 " + time + "s"); break;
                        case "5": case "DISAPPEAR":
                            Mark(RouteMarkerKind.Disappear, "消失");
                            hidden = true;
                            break;
                        case "7": case "ALERT": Mark(RouteMarkerKind.Alert, "!"); break;
                        case "6": case "APPEAR_AT_POS":
                            if (!TryPoint(step["position"], step["reachOffset"], air, out cell, out point)) return null;
                            hidden = false;
                            Mark(RouteMarkerKind.Appear, "出现");
                            break;
                        default:
                            if (!hidden && TryPoint(step["position"], step["reachOffset"], air, out var targetCell, out var target))
                            {
                                Travel(targetCell, target);
                                Mark(RouteMarkerKind.Checkpoint);
                            }
                            break;
                    }
                }
            if (!hidden) { Travel(endCell, end); Mark(RouteMarkerKind.End); }
            return path;
        }

        internal static double Duration(float length, RoutePreviewSpeed speed)
        {
            var (factor, multiplier) = speed switch
            {
                RoutePreviewSpeed.Slow => (.63, 1d),
                RoutePreviewSpeed.VerySlow => (.75, 1d),
                RoutePreviewSpeed.Constant => (1d, .1),
                _ => (.5, 1d)
            };
            return .04 * Math.Pow(length * 50, factor) * multiplier;
        }

        private static bool IsRoute(JToken? token) => token is JObject route &&
            route["startPosition"] is JObject && route["endPosition"] is JObject &&
            !Matches(route["motionMode"], "E_NUM", "2");

        private static bool TryPoint(JToken? token, JToken? offset, bool air, out Vector2Int cell, out Vector3 point)
        {
            cell = default;
            point = default;
            if (token is not JObject position) return false;
            var col = Integer(position["col"], int.MinValue);
            var row = Integer(position["row"], int.MinValue);
            if (col == int.MinValue || row == int.MinValue) return false;
            cell = new Vector2Int(col, row);
            point = World(cell, offset, air);
            return !float.IsInfinity(point.x) && !float.IsInfinity(point.z);
        }

        private static Vector3 World(Vector2Int cell, JToken? offset, bool air) => new(
            cell.x + .5f + (float)Number((offset as JObject)?["x"]), air ? .95f : .62f,
            cell.y + .5f + (float)Number((offset as JObject)?["y"]));

        internal static bool Matches(JToken? token, string text, string number)
        {
            var value = token?.ToString();
            return string.Equals(value, text, StringComparison.OrdinalIgnoreCase) || value == number;
        }

        internal static int Integer(JToken? token, int fallback) =>
            int.TryParse(token?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

        internal static double Number(JToken? token) =>
            double.TryParse(token?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            !double.IsNaN(value) && !double.IsInfinity(value) ? value : 0;
    }
}
