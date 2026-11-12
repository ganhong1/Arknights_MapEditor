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
            CheckPredefinedRemoval(copy);
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

        private static void CheckPredefinedRemoval(JObject document)
        {
            foreach (var item in Actions(document))
            {
                var type = item.Item1["actionType"];
                if (IsPredefinedAction(type))
                    throw Error("Predefined action exists; resolve its target before removing an entry", item.Item1.Path);
                if (!KnownAction(type)) throw Error("Unknown action prevents safe predefined removal", item.Item1.Path);
            }
        }

        private static bool IsPredefinedAction(JToken? type) =>
            IsAction(type, "ACTIVATE_PREDEFINED", 6) || IsAction(type, "TRIGGER_PREDEFINED", 8) ||
            IsAction(type, "WITHDRAW_PREDEFINED", 10);

        public IReadOnlyList<LevelDiagnostic> ValidateReferences()
        {
            var diagnostics = new List<LevelDiagnostic>();
            ValidateRouteAndWaveStructure(diagnostics);
            Tuple<JObject, bool>[] actions;
            try { actions = Actions(root).ToArray(); }
            catch (LevelDocumentException error)
            {
                diagnostics.Add(new LevelDiagnostic("error", error.Path, error.Message));
                return diagnostics;
            }
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
            foreach (var item in actions)
            {
                var action = item.Item1;
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
                    var extra = action["useExtraRoute"];
                    if (extra != null && extra.Type != JTokenType.Null && extra.Type != JTokenType.Boolean)
                        diagnostics.Add(new LevelDiagnostic("error", extra.Path, "Ambiguous useExtraRoute value"));
                    else
                    {
                        var library = item.Item2 || extra?.Value<bool?>() == true ? "extraRoutes" : "routes";
                        var index = action["routeIndex"];
                        if (!(root[library] is JArray routes) || index?.Type != JTokenType.Integer || !int.TryParse(index.ToString(), out var number) || number < 0 || number >= routes.Count || !(routes[number] is JObject))
                            diagnostics.Add(new LevelDiagnostic("error", action.Path + ".routeIndex", "Missing or invalid " + library + " reference"));
                    }
                }
                if (IsAction(type, "SPAWN", 0))
                {
                    var key = action["key"];
                    if (key?.Type != JTokenType.String || !ids.Contains(key.Value<string>()!))
                        diagnostics.Add(new LevelDiagnostic("warning", action.Path + ".key", "SPAWN key absent from enemyDbRefs: " + key));
                }
                if (IsPredefinedAction(type))
                    diagnostics.Add(new LevelDiagnostic("warning", action.Path, "Predefined target requires native context; not validated"));
            }
            return diagnostics;
        }

        private static JObject ParseObjectFragment(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            if (json.Length > MaximumUtf8Bytes) throw Error("Fragment exceeds size limit", "$fragment");
            StrictJsonGuard.Validate(json);
            var envelope = Parse("{\"mapData\":{\"map\":[[0]],\"tiles\":[{}]},\"fragment\":" + json + "}");
            return envelope.root["fragment"] as JObject ?? throw Error("Expected object fragment", "$fragment");
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
