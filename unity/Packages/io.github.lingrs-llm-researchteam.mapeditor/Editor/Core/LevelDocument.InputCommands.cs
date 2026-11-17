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
        public LevelDocument InsertAction(ScheduleAddress address, int index, string json)
        {
            var action = ParseObjectFragment(json);
            var copy = (JObject)root.DeepClone();
            var actions = AddressedActions(copy, address);
            CheckIndex(index, actions.Count + 1);
            actions.Insert(index, action);
            return Parse(copy.ToString(Formatting.None));
        }

        public LevelDocument DeleteAction(ScheduleAddress address, int index)
        {
            var copy = (JObject)root.DeepClone();
            var actions = AddressedActions(copy, address);
            CheckIndex(index, actions.Count);
            actions.RemoveAt(index);
            return Parse(copy.ToString(Formatting.None));
        }

        public LevelDocument MoveAction(ScheduleAddress address, int from, int to)
        {
            var copy = (JObject)root.DeepClone();
            var actions = AddressedActions(copy, address);
            CheckIndex(from, actions.Count); CheckIndex(to, actions.Count);
            var moved = actions[from]; actions.RemoveAt(from); actions.Insert(to, moved);
            return Parse(copy.ToString(Formatting.None));
        }

        public LevelDocument InsertPredefined(string section, string collection, int index, string json)
        {
            var entry = ParseObjectFragment(json);
            var copy = (JObject)root.DeepClone();
            var list = PredefinedCollection(copy, section, collection);
            CheckIndex(index, list.Count + 1);
            list.Insert(index, entry);
            return Parse(copy.ToString(Formatting.None));
        }

        public LevelDocument DeletePredefined(string section, string collection, int index)
        {
            var copy = (JObject)root.DeepClone();
            var list = PredefinedCollection(copy, section, collection);
            CheckIndex(index, list.Count);
            CheckPredefinedRemoval(copy, EffectivePredefinedAlias(list[index] as JObject), IsCardCollection(collection));
            list.RemoveAt(index);
            return Parse(copy.ToString(Formatting.None));
        }

        public LevelDocument MovePredefined(string section, string collection, int from, int to)
        {
            var copy = (JObject)root.DeepClone();
            var list = PredefinedCollection(copy, section, collection);
            CheckIndex(from, list.Count);
            CheckIndex(to, list.Count);
            if (from == to) return this;
            var item = list[from]!;
            list.RemoveAt(from);
            list.Insert(to, item);
            return Parse(copy.ToString(Formatting.None));
        }

        private static void CheckPredefinedRemoval(JObject document, string? alias, bool card)
        {
            foreach (var item in Actions(document))
            {
                var action = item.Item1;
                var type = action["actionType"];
                if (CanReferencePredefined(type, card) &&
                    (string.IsNullOrEmpty(alias) || !PredefinedActionAliases(action).Any() ||
                     ReferencesPredefinedAlias(action, alias, card)))
                    throw Error("Predefined action exists; resolve its target before removing an entry", item.Item1.Path);
                if (!KnownAction(type)) throw Error("Unknown action prevents safe predefined removal", item.Item1.Path);
            }
        }

        private static bool IsCardCollection(string collection) => collection.EndsWith("Cards", StringComparison.Ordinal);

        internal static string? EffectivePredefinedAlias(JObject? record)
        {
            var alias = record?["alias"];
            if (alias?.Type == JTokenType.String && !string.IsNullOrEmpty(alias.Value<string>()))
                return alias.Value<string>();
            var characterKey = (record?["inst"] as JObject)?["characterKey"];
            return characterKey?.Type == JTokenType.String ? characterKey.Value<string>() : null;
        }

        private static bool CanReferencePredefined(JToken? type, bool card) =>
            IsPredefinedAction(type) && !(card && IsAction(type, "WITHDRAW_PREDEFINED", 10));

        private static bool ReferencesPredefinedAlias(JObject action, string alias, bool card) =>
            CanReferencePredefined(action["actionType"], card) &&
            PredefinedActionAliases(action).Contains(alias, StringComparer.Ordinal);

        internal static IEnumerable<string> PredefinedActionAliases(JObject action)
        {
            var key = action["key"];
            if (key?.Type != JTokenType.String || string.IsNullOrWhiteSpace(key.Value<string>())) yield break;
            var text = key.Value<string>()!;
            if (!IsAction(action["actionType"], "TRIGGER_PREDEFINED", 8))
            {
                yield return text;
                yield break;
            }
            foreach (var candidate in text.Split('|'))
            {
                var separator = candidate.IndexOf(':');
                yield return separator < 0 ? candidate : candidate.Substring(0, separator);
            }
        }

        private static bool IsPredefinedAction(JToken? type) =>
            IsAction(type, "ACTIVATE_PREDEFINED", 6) || IsAction(type, "TRIGGER_PREDEFINED", 8) ||
            IsAction(type, "WITHDRAW_PREDEFINED", 10);

        public IReadOnlyList<LevelDiagnostic> ValidateReferences() => ValidateReferences(false, null);

        internal IReadOnlyList<LevelDiagnostic> ValidateWaveReferences(IEnumerable<string> resolvedEnemies) =>
            ValidateReferences(true, new HashSet<string>(resolvedEnemies, StringComparer.Ordinal));

        private IReadOnlyList<LevelDiagnostic> ValidateReferences(bool wavesOnly, HashSet<string>? resolvedEnemies)
        {
            var diagnostics = new List<LevelDiagnostic>();
            ValidateRouteAndWaveStructure(diagnostics);
            var actions = new List<(JObject Action, bool FromBranch)>();
            void ReadActions(IEnumerable<JObject> records, bool branch)
            {
                try { foreach (var action in records) actions.Add((action, branch)); }
                catch (LevelDocumentException error)
                {
                    var path = error.Path.Length == 0 ? (branch ? "$.branches" : "$.waves") : error.Path;
                    diagnostics.Add(new LevelDiagnostic("error", path, error.Message));
                }
            }
            if (root["waves"]?.Type == JTokenType.Null)
                diagnostics.Add(new LevelDiagnostic("error", "$.waves", "Expected waves array, got null"));
            else ReadActions(MainActions(root), false);
            if (!wavesOnly) ReadActions(BranchActions(root), true);
            foreach (var field in new[] { "routes", "extraRoutes", "enemyDbRefs" })
                if (root[field] != null && root[field]!.Type != JTokenType.Null && !(root[field] is JArray))
                    diagnostics.Add(new LevelDiagnostic("error", "$." + field, "Expected array"));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (root["enemyDbRefs"] is JArray enemies)
                foreach (var enemy in enemies)
                {
                    if (!(enemy is JObject entry) || entry["id"]?.Type != JTokenType.String || string.IsNullOrEmpty(entry["id"]!.Value<string>()))
                        diagnostics.Add(new LevelDiagnostic("error", enemy.Path, "Enemy reference needs a string id"));
                    else if (!ids.Add(entry["id"]!.Value<string>()!))
                        diagnostics.Add(new LevelDiagnostic("error", entry.Path + ".id", "Duplicate enemy reference id"));
                }
            if (root["enemies"] is JArray inlineEnemies)
                foreach (var enemy in inlineEnemies.OfType<JObject>())
                {
                    var alias = enemy["alias"]?.Type == JTokenType.String ? enemy["alias"]!.Value<string>() : null;
                    var key = string.IsNullOrEmpty(alias) && enemy["key"]?.Type == JTokenType.String
                        ? enemy["key"]!.Value<string>() : alias;
                    if (!string.IsNullOrEmpty(key)) ids.Add(key!);
                }
            foreach (var item in actions)
            {
                var action = item.Action;
                var type = action["actionType"];
                NumberDiagnostic(action["count"], action.Path + ".count", 0, true, diagnostics);
                NumberDiagnostic(action["preDelay"], action.Path + ".preDelay", 0, false, diagnostics);
                NumberDiagnostic(action["interval"], action.Path + ".interval", 0, false, diagnostics);
                if (IsAction(type, "E_NUM", 14)) diagnostics.Add(new LevelDiagnostic("error", action.Path + ".actionType", "Action sentinel is not executable"));
                if (!KnownAction(type))
                {
                    diagnostics.Add(new LevelDiagnostic("warning", action.Path + ".actionType", "Unknown action semantics; retained without runtime validation"));
                    continue;
                }
                if (IsRouteAction(type))
                {
                    var library = RouteLibraryForAction(item.FromBranch);
                    var index = action["routeIndex"];
                    var routes = root[library] as JArray;
                    var route = WaveSchedule.Integer(index);
                    if (wavesOnly ? !WaveSchedule.HasRoute(routes, route) :
                        routes == null || route is not int position || position < 0 || position >= routes.Count ||
                        routes[position] is not JObject)
                        diagnostics.Add(new LevelDiagnostic("error", action.Path + ".routeIndex", "Missing or invalid " + library + " reference"));
                }
                var spawn = IsAction(type, "SPAWN", 0);
                if (spawn || wavesOnly && IsAction(type, "DISPLAY_ENEMY_INFO", 5))
                {
                    var key = action["key"];
                    if (key?.Type != JTokenType.String || !(resolvedEnemies ?? ids).Contains(key.Value<string>()!))
                        diagnostics.Add(new LevelDiagnostic("warning", action.Path + ".key",
                            (spawn ? "SPAWN" : "DISPLAY_ENEMY_INFO") + " key unresolved in document enemy definitions; check mode/runtime inputs: " + key));
                }
                if (IsPredefinedAction(type))
                    diagnostics.Add(new LevelDiagnostic("warning", action.Path, "Predefined target requires native context; not validated"));
            }
            return diagnostics;
        }

        private static JObject ParseObjectFragment(string json) =>
            ParseValueFragment(json) as JObject ?? throw Error("Expected object fragment", "$fragment");

        internal static JToken ParseValueFragment(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            if (json.Length > MaximumUtf8Bytes) throw Error("Fragment exceeds size limit", "$fragment");
            StrictJsonGuard.Validate(json);
            var envelope = Parse("{\"mapData\":{\"map\":[[0]],\"tiles\":[{}]},\"fragment\":" + json + "}");
            return envelope.root["fragment"]!;
        }

        private static JArray AddressedActions(JObject document, ScheduleAddress address)
        {
            if (address == null) throw new ArgumentNullException(nameof(address));
            JArray parts;
            if (address.BranchKey == null)
            {
                var waves = RequiredArray(document["waves"], "$.waves");
                CheckIndex(address.Wave, waves.Count);
                if (!(waves[address.Wave] is JObject wave)) throw Error("Expected wave object", waves[address.Wave].Path);
                parts = RequiredArray(wave["fragments"], wave.Path + ".fragments");
            }
            else
            {
                if (!(document["branches"] is JObject branches) || !(branches[address.BranchKey] is JObject branch))
                    throw Error("Missing branch", "$.branches");
                parts = RequiredArray(branch["phases"], branch.Path + ".phases");
            }
            CheckIndex(address.Part, parts.Count);
            if (!(parts[address.Part] is JObject part)) throw Error("Expected part object", parts[address.Part].Path);
            return RequiredArray(part["actions"], part.Path + ".actions");
        }

        private static JArray PredefinedCollection(JObject document, string section, string collection, bool create = false)
        {
            if (section != "predefines" && section != "hardPredefines") throw new ArgumentException("Unknown predefined section", nameof(section));
            if (collection != "characterInsts" && collection != "tokenInsts" && collection != "characterCards" && collection != "tokenCards")
                throw new ArgumentException("Unknown predefined collection", nameof(collection));
            if (create && (document[section] == null || document[section]!.Type == JTokenType.Null))
                document[section] = new JObject();
            if (!(document[section] is JObject predefined)) throw Error("Predefined section must exist as an object", "$." + section);
            if (create && (predefined[collection] == null || predefined[collection]!.Type == JTokenType.Null))
                predefined[collection] = new JArray();
            return RequiredArray(predefined[collection], predefined.Path + "." + collection);
        }

        private static void CheckIndex(int index, int count)
        {
            if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        }

        private static bool IsAction(JToken? value, string name, int number) => value != null &&
            (value.Type == JTokenType.String && value.Value<string>() == name || value.Type == JTokenType.Integer && value.ToString() == number.ToString(System.Globalization.CultureInfo.InvariantCulture));

        private static bool KnownAction(JToken? value)
        {
            // schema.js ActionType, excluding E_NUM (14), which is a sentinel.
            var names = new[] { "SPAWN", "PREVIEW_CURSOR", "STORY", "TUTORIAL", "PLAY_BGM", "DISPLAY_ENEMY_INFO", "ACTIVATE_PREDEFINED", "PLAY_OPERA", "TRIGGER_PREDEFINED", "BATTLE_EVENTS", "WITHDRAW_PREDEFINED", "DIALOG", "SHOW_ALL_HIDDEN_CARDS", "EMPTY" };
            for (var i = 0; i < names.Length; i++) if (IsAction(value, names[i], i)) return true;
            return false;
        }
    }
}
