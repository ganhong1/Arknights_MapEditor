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
            var references = RouteReferences(copy);
            foreach (var item in references.Where(r => r.Item2 == library))
            {
                var position = item.Item1["routeIndex"]!.Value<int>();
                if (position == index) throw Error("Route is still referenced", item.Item1.Path + ".routeIndex");
                if (position > index) item.Item1["routeIndex"] = position - 1;
            }
            routes.RemoveAt(index);
            return Parse(copy.ToString(Formatting.None));
        }

        public LevelDocument MoveRoute(string library, int from, int to)
        {
            if (library != "routes" && library != "extraRoutes") throw new ArgumentException("Expected routes or extraRoutes", nameof(library));
            var copy = (JObject)root.DeepClone();
            var routes = RequiredArray(copy[library], "$." + library);
            CheckIndex(from, routes.Count); CheckIndex(to, routes.Count);
            foreach (var item in RouteReferences(copy).Where(r => r.Item2 == library))
            {
                var position = item.Item1["routeIndex"]!.Value<int>();
                if (position == from) position = to;
                else if (from < to && position > from && position <= to) position--;
                else if (to < from && position >= to && position < from) position++;
                item.Item1["routeIndex"] = position;
            }
            var moved = routes[from]; routes.RemoveAt(from); routes.Insert(to, moved);
            return Parse(copy.ToString(Formatting.None));
        }

        private static List<Tuple<JObject, string>> RouteReferences(JObject copy)
        {
            var understood = new List<JToken>();
            var result = new List<Tuple<JObject, string>>();
            foreach (var item in Actions(copy))
            {
                var action = item.Item1;
                if (!IsRouteAction(action["actionType"]))
                {
                    // Web's shared action schema includes an unused routeIndex even
                    // on known non-route actions. Only this direct standard field is exempt.
                    if (KnownAction(action["actionType"]) && action["routeIndex"] != null)
                        understood.Add(action["routeIndex"]!);
                    continue;
                }
                var extra = action["useExtraRoute"];
                if (extra != null && extra.Type != JTokenType.Null && extra.Type != JTokenType.Boolean)
                    throw Error("Ambiguous useExtraRoute value", extra.Path);
                var actionLibrary = item.Item2 || extra?.Value<bool?>() == true ? "extraRoutes" : "routes";
                var routeIndex = action["routeIndex"];
                if (routeIndex == null || routeIndex.Type != JTokenType.Integer || !int.TryParse(routeIndex.ToString(), out var position))
                    throw Error("Invalid route reference", action.Path + ".routeIndex");
                var targetRoutes = RequiredArray(copy[actionLibrary], "$." + actionLibrary);
                if (position < 0 || position >= targetRoutes.Count) throw Error("Dangling route reference", routeIndex.Path);
                understood.Add(routeIndex);
                result.Add(Tuple.Create(action, actionLibrary));
            }
            // Extension fields may also reference route indices. Do not guess their library.
            foreach (var property in copy.Descendants().OfType<JProperty>().Where(p => p.Name == "routeIndex"))
                if (!understood.Any(value => ReferenceEquals(value, property.Value))) throw Error("Unknown route reference prevents deletion", property.Path);
            return result;
        }

        public LevelDocument DeleteEnemy(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Enemy id required", nameof(id));
            var copy = (JObject)root.DeepClone();
            var enemies = RequiredArray(copy["enemyDbRefs"], "$.enemyDbRefs");
            var matches = enemies.OfType<JObject>().Where(e => e["id"]?.Type == JTokenType.String && e["id"]!.Value<string>() == id).ToArray();
            if (matches.Length != 1) throw Error("Enemy id must identify exactly one reference", "$.enemyDbRefs");
            // Web model validates SPAWN.key against enemyDbRefs.id. Unknown same-key uses
            // are also retained rather than assumed unrelated to the deleted enemy.
            foreach (var action in Actions(copy))
                if (action.Item1["key"]?.Type == JTokenType.String && action.Item1["key"]!.Value<string>() == id)
                    throw Error("Enemy id is still used by an action", action.Item1.Path + ".key");
            foreach (var property in copy.Descendants().OfType<JProperty>().Where(p => p.Name == "key"))
                if (property.Value.Type == JTokenType.String && property.Value.Value<string>() == id)
                    throw Error("Unresolved enemy-key reference prevents deletion", property.Path);
            matches[0].Remove();
            return Parse(copy.ToString(Formatting.None));
        }

        private static bool IsRouteAction(JToken? value) => value != null &&
            (value.Type == JTokenType.String && (value.Value<string>() == "SPAWN" || value.Value<string>() == "PREVIEW_CURSOR") ||
             value.Type == JTokenType.Integer && (value.ToString() == "0" || value.ToString() == "1"));

        private static JArray RequiredArray(JToken? token, string path) => token as JArray ?? throw Error("Expected array for safe reference analysis", path);

        private static IEnumerable<Tuple<JObject, bool>> Actions(JObject document)
        {
            if (document["waves"] != null && document["waves"]!.Type != JTokenType.Null)
                foreach (var wave in RequiredArray(document["waves"], "$.waves"))
                {
                    if (!(wave is JObject)) throw Error("Expected wave object", wave.Path);
                    foreach (var fragment in RequiredArray(wave["fragments"], wave.Path + ".fragments"))
                        foreach (var action in PartActions(fragment)) yield return Tuple.Create(action, false);
                }
            if (document["branches"] == null || document["branches"]!.Type == JTokenType.Null) yield break;
            if (!(document["branches"] is JObject branches)) throw Error("Expected branches object", "$.branches");
            foreach (var property in branches.Properties())
            {
                if (!(property.Value is JObject)) throw Error("Expected branch object", property.Path);
                foreach (var phase in RequiredArray(property.Value["phases"], property.Path + ".phases"))
                    foreach (var action in PartActions(phase)) yield return Tuple.Create(action, true);
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
