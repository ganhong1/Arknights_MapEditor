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
        public LevelDocument DeleteRoute(string library, int index)
        {
            if (library != "routes" && library != "extraRoutes") throw new ArgumentException("Expected routes or extraRoutes", nameof(library));
            var copy = (JObject)root.DeepClone();
            var routes = RequiredArray(copy[library], "$." + library);
            if (index < 0 || index >= routes.Count) throw new ArgumentOutOfRangeException(nameof(index));
            RewriteRouteReferences(copy, library, position => position == index ? -1 :
                position > index ? position - 1 : position);
            routes.RemoveAt(index);
            return Parse(copy.ToString(Formatting.None));
        }

        public LevelDocument MoveRoute(string library, int from, int to)
        {
            if (library != "routes" && library != "extraRoutes") throw new ArgumentException("Expected routes or extraRoutes", nameof(library));
            var copy = (JObject)root.DeepClone();
            var routes = RequiredArray(copy[library], "$." + library);
            CheckIndex(from, routes.Count); CheckIndex(to, routes.Count);
            if (from == to) return this;
            RewriteRouteReferences(copy, library, position =>
            {
                if (position == from) return to;
                if (from < to && position > from && position <= to) return position - 1;
                if (to < from && position >= to && position < from) return position + 1;
                return position;
            });
            var moved = routes[from]; routes.RemoveAt(from); routes.Insert(to, moved);
            return Parse(copy.ToString(Formatting.None));
        }

        internal static string RouteLibraryForAction(bool fromBranch) => fromBranch ? "extraRoutes" : "routes";

        public ILookup<int, string> RouteUsePaths(string library) => Actions(root)
            .Where(item => ActionRouteLibrary(root, item.Item1, item.Item2) == library)
            .Select(item => (Action: item.Item1, Index: ReadRouteIndex(item.Item1, item.Item2)))
            .Where(item => item.Index >= 0)
            .ToLookup(item => item.Index!.Value, item => item.Action.Path +
                (IsAction(item.Action["actionType"], "PLAY_OPERA", 7) ? " · 演出" :
                 IsAction(item.Action["actionType"], "SPAWN", 0) ? " · 出怪" :
                 IsAction(item.Action["actionType"], "EMPTY", 13) ? " · 分支路线" : " · 路线指示"));

        private static void RewriteRouteReferences(JObject copy, string library, Func<int, int> remap)
        {
            var understood = new HashSet<JProperty>();
            var routeCount = ((JArray)copy[library]!).Count;
            foreach (var item in Actions(copy))
            {
                var action = item.Item1;
                var type = action["actionType"];
                var property = action.Property("routeIndex");
                var isOpera = IsAction(type, "PLAY_OPERA", 7);
                var optionalIndex = isOpera || IsBranchEmpty(type, item.Item2);
                var position = ReadRouteIndex(action, item.Item2);
                var actionLibrary = ActionRouteLibrary(copy, action, item.Item2);
                if (actionLibrary == null)
                {
                    if (isOpera && OperaRouteUse(copy, action) == OperaRoute.Unknown &&
                        (library == "routes" || AmbiguousOperaRouteLibrary(action)) &&
                        (!position.HasValue || position >= 0 && position < routeCount && remap(position.Value) != position))
                        throw Error("Unresolved opera route reference prevents this edit", action.Path + ".routeIndex");
                    if (KnownAction(type) && property != null) understood.Add(property);
                    continue;
                }
                if (position == null && optionalIndex && actionLibrary != library)
                {
                    if (property != null) understood.Add(property);
                    continue;
                }
                if (position == null || position < 0 && !optionalIndex)
                    throw Error("Invalid route reference", action.Path + ".routeIndex");
                if (!optionalIndex &&
                    position >= RequiredArray(copy[actionLibrary], "$." + actionLibrary).Count)
                    throw Error("Dangling route reference", action.Path + ".routeIndex");
                if (actionLibrary == library && position >= 0 && position < routeCount)
                {
                    var next = remap(position.Value);
                    if (next < 0) throw Error("Route is still referenced", action.Path + ".routeIndex");
                    if (next != position)
                        action["routeIndex"] = next;
                }
                if (action.Property("routeIndex") is JProperty current) understood.Add(current);
            }
            foreach (var property in copy.Descendants().OfType<JProperty>().Where(p => p.Name == "routeIndex"))
                if (!understood.Contains(property)) throw Error("Unknown route reference prevents this edit", property.Path);
        }

        private static int? ReadRouteIndex(JObject action, bool fromBranch)
        {
            var index = action["routeIndex"];
            if (index == null && (IsAction(action["actionType"], "PLAY_OPERA", 7) ||
                IsBranchEmpty(action["actionType"], fromBranch))) return 0;
            return index?.Type == JTokenType.Integer && int.TryParse(index.ToString(), out var number) ? number : null;
        }

        internal static string? ActionRouteLibrary(JObject document, JObject action, bool fromBranch)
        {
            if (IsRouteAction(action["actionType"]) || IsBranchEmpty(action["actionType"], fromBranch))
                return RouteLibraryForAction(fromBranch);
            return IsAction(action["actionType"], "PLAY_OPERA", 7) && OperaRouteUse(document, action) == OperaRoute.Main
                ? "routes" : null;
        }

        private static bool IsBranchEmpty(JToken? type, bool fromBranch) => fromBranch && IsAction(type, "EMPTY", 13);

        internal enum OperaRoute { Unknown, Main, Unused }

        private static bool AmbiguousOperaRouteLibrary(JObject action) =>
            action["useExtraRoute"] is JToken flag &&
            (flag.Type != JTokenType.Boolean || flag.Value<bool>());

        internal static OperaRoute OperaRouteUse(JObject document, JObject action)
        {
            if (document["operaConfig"]?.Type != JTokenType.String || action["key"]?.Type != JTokenType.String)
                return OperaRoute.Unknown;
            var config = document["operaConfig"]!.Value<string>();
            var command = action["key"]!.Value<string>();
            // PC 2.7.81 config_common.ab, cross-checked against all 36 authored pairs in the finished-level archive.
            // PLAY_OPERA leaves the non-serialized useExtraRoute at its default. Preserve and guard ambiguous legacy fields.
            var use = (config, command) switch
            {
                ("act12side", "screen_submerged_start") or ("act34side_09", "cannon_fire" or "kill_all") or
                ("common_guide", "effect_deploy" or "effect_warning_01" or "finish_effect_deploy") or
                ("main_08-15", "fire_effect") or ("main_16_boss2", "move_camera") or
                ("rune_05-01", "dscout_skill_start") => OperaRoute.Main,
                ("act17d7_01", "count_down" or "play_screen_eff_blue" or "switch_screen_eff_any_to_fever" or
                    "switch_screen_eff_blue_to_red" or "switch_screen_eff_red_to_blue") or
                ("act24side", "init" or "move_camera") or ("common_guide", "effect_warning_fullscreen") or
                ("hard_08-04" or "main_08-17", "blast_effect_x" or "blast_effect_y") or
                ("level_act46side_10", "init" or "move_camera_1" or "move_camera_2") or
                ("level_lt17_06", "summon_boss") or ("main_14-09", "main_14_shake") or ("main_16_boss2", "init") or
                ("rogue_4_boss4_formal", "init" or "move_camera") or
                ("rogue_4_boss5_formal", "init" or "move_camera_1" or "move_camera_2") or
                ("rogue_6_boss1", "kill_boss") or ("sandbox_v2_dlc1", "racing_spawn_pause") => OperaRoute.Unused,
                _ => OperaRoute.Unknown
            };
            return use == OperaRoute.Main && AmbiguousOperaRouteLibrary(action) ? OperaRoute.Unknown : use;
        }

        public LevelDocument DeleteEnemy(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Enemy id required", nameof(id));
            var enemies = RequiredArray(root["enemyDbRefs"], "$.enemyDbRefs");
            var matches = enemies.Where(enemy => ReferenceIdentity(enemy) == id).ToArray();
            if (matches.Length != 1) throw Error("Enemy id must identify exactly one reference", "$.enemyDbRefs");
            return DeleteEnemyReference(enemies.IndexOf(matches[0]));
        }

        private static bool IsRouteAction(JToken? value) => value != null &&
            (value.Type == JTokenType.String && (value.Value<string>() == "SPAWN" || value.Value<string>() == "PREVIEW_CURSOR") ||
             value.Type == JTokenType.Integer && (value.ToString() == "0" || value.ToString() == "1"));

        private static JArray RequiredArray(JToken? token, string path) => token as JArray ?? throw Error("Expected array for safe reference analysis", path);

        private static IEnumerable<Tuple<JObject, bool>> Actions(JObject document)
        {
            foreach (var action in MainActions(document)) yield return Tuple.Create(action, false);
            foreach (var action in BranchActions(document)) yield return Tuple.Create(action, true);
        }

        private static IEnumerable<JObject> MainActions(JObject document)
        {
            if (document["waves"] != null && document["waves"]!.Type != JTokenType.Null)
                foreach (var wave in RequiredArray(document["waves"], "$.waves"))
                {
                    if (!(wave is JObject)) throw Error("Expected wave object", wave.Path);
                    foreach (var fragment in RequiredArray(wave["fragments"], wave.Path + ".fragments"))
                        foreach (var action in PartActions(fragment)) yield return action;
                }
        }

        private static IEnumerable<JObject> BranchActions(JObject document)
        {
            if (document["branches"] == null || document["branches"]!.Type == JTokenType.Null) yield break;
            if (!(document["branches"] is JObject branches)) throw Error("Expected branches object", "$.branches");
            foreach (var property in branches.Properties())
            {
                if (!(property.Value is JObject)) throw Error("Expected branch object", property.Path);
                foreach (var phase in RequiredArray(property.Value["phases"], property.Path + ".phases"))
                    foreach (var action in PartActions(phase)) yield return action;
            }
        }

        private static IEnumerable<JObject> PartActions(JToken part)
        {
            if (!(part is JObject)) throw Error("Expected schedule part object", part.Path);
            foreach (var action in RequiredArray(part["actions"], part.Path + ".actions"))
            {
                if (!(action is JObject value)) throw Error("Expected action object", action.Path);
                yield return value;
            }
        }
    }
}
