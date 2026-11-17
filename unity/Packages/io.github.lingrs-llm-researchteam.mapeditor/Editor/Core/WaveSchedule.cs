#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Core
{
    internal static class WaveSchedule
    {
        internal static readonly string[] ActionFields =
        {
            "actionType", "managedByScheduler", "key", "count", "preDelay", "interval", "routeIndex",
            "blockFragment", "autoPreviewRoute", "autoDisplayEnemyInfo", "isUnharmfulAndAlwaysCountAsKilled",
            "hiddenGroup", "randomSpawnGroupKey", "randomSpawnGroupPackKey", "randomType", "refreshType",
            "weight", "dontBlockWave", "forceBlockWaveInBranch"
        };
        internal static readonly string[] ActionTypes =
        {
            "SPAWN", "PREVIEW_CURSOR", "STORY", "TUTORIAL", "PLAY_BGM", "DISPLAY_ENEMY_INFO",
            "ACTIVATE_PREDEFINED", "PLAY_OPERA", "TRIGGER_PREDEFINED", "BATTLE_EVENTS",
            "WITHDRAW_PREDEFINED", "DIALOG", "SHOW_ALL_HIDDEN_CARDS", "EMPTY", "E_NUM"
        };

        internal static string ChildField(int depth) => depth == 1 ? "fragments" : "actions";
        internal static string Key(IEnumerable<int> address) => string.Join("/", address);
        internal static string Path(IEnumerable<int> address)
        {
            var parts = address.ToArray();
            var path = "waves";
            for (var i = 0; i < parts.Length; i++)
                path += (i == 0 ? "" : "." + ChildField(i)) + "[" + parts[i] + "]";
            return path;
        }

        internal static int[] Append(int[] parent, int index) => parent.Append(index).ToArray();

        internal static JToken? Read(JToken? waves, int[] address)
        {
            var value = waves;
            for (var i = 0; i < address.Length; i++)
            {
                if (i > 0) value = (value as JObject)?[ChildField(i)];
                if (value is not JArray children || address[i] < 0 || address[i] >= children.Count) return null;
                value = children[address[i]];
            }
            return value;
        }

        internal static JArray GetOrCreateChildren(ref JToken? waves, int[] parent)
        {
            if (parent.Length == 0)
            {
                if (waves == null || waves.Type == JTokenType.Null) waves = new JArray();
                return waves as JArray ?? throw new FormatException("waves 必须是数组。");
            }
            var item = Read(waves, parent) as JObject ?? throw new FormatException("找不到 " + Path(parent));
            var key = ChildField(parent.Length);
            if (item[key] == null || item[key]!.Type == JTokenType.Null) item[key] = new JArray();
            return item[key] as JArray ?? throw new FormatException(Path(parent) + "." + key + " 必须是数组。");
        }

        internal static JObject NewNode(int depth)
        {
            switch (depth)
            {
                case 1: return new JObject
                {
                    ["preDelay"] = 0f, ["postDelay"] = 0f, ["maxTimeWaitingForNextWave"] = -1f,
                    ["fragments"] = new JArray(NewNode(2)), ["advancedWaveTag"] = JValue.CreateNull()
                };
                case 2: return new JObject { ["preDelay"] = 0f, ["actions"] = new JArray() };
                case 3: return ActionDataRules.NewAction("SPAWN");
                default: throw new ArgumentOutOfRangeException(nameof(depth));
            }
        }

        internal static JObject Fields(JObject record, int depth)
        {
            var copy = (JObject)record.DeepClone();
            if (depth < 3) copy.Remove(ChildField(depth));
            return copy;
        }

        internal static void ApplyFields(JObject record, JObject fields, int depth)
        {
            var child = depth < 3 ? ChildField(depth) : null;
            if (child != null && fields.Property(child) != null)
                throw new FormatException("请在字段树中单独编辑 " + child + "。");
            foreach (var property in record.Properties().ToArray())
            {
                if (property.Name == child) continue;
                if (fields.TryGetValue(property.Name, out var value)) property.Value = value.DeepClone();
                else property.Remove();
            }
            foreach (var property in fields.Properties())
                if (record.Property(property.Name) == null) record.Add(property.Name, property.Value.DeepClone());
        }

        internal static string ActionType(JToken? token)
        {
            int? ordinal = Integer(token);
            if (ordinal is int index && index >= 0 && index < ActionTypes.Length) return ActionTypes[index];
            return token?.Type == JTokenType.String ? token.Value<string>()! :
                token?.ToString(Formatting.None) ?? "未提供";
        }

        internal static int? Integer(JToken? value) =>
            value?.Type == JTokenType.Integer && int.TryParse(value.ToString(Formatting.None),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

        internal static double? Time(JToken? value, double minimum = 0d)
        {
            if (value == null) return 0d;
            if (value.Type != JTokenType.Integer && value.Type != JTokenType.Float && value.Type != JTokenType.Raw)
                return null;
            if (!double.TryParse(value.ToString(Formatting.None), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                double.IsNaN(number) || number < minimum || number > float.MaxValue) return null;
            return number;
        }

        internal static void RequireWaveFields(JObject fields, JObject previous, int depth)
        {
            var timings = depth == 1 ? new[] { "preDelay", "postDelay", "maxTimeWaitingForNextWave" } : new[] { "preDelay" };
            foreach (var field in timings)
                if (!JToken.DeepEquals(previous[field], fields[field]) &&
                    (fields[field] == null || Time(fields[field], field == "maxTimeWaitingForNextWave" ? -1d : 0d) == null))
                    throw new FormatException(field + " 需要有效秒数。");
        }

        internal static void RequireActionFields(JObject action, JObject previous, JArray? routes) =>
            ActionDataRules.RequireFields(action, previous, routes);
        internal static (double First, double Last, int Count)? RelativeTime(JToken? fragmentDelay, JObject action)
        {
            var type = ActionType(action["actionType"]);
            if (type == "EMPTY" || type == "E_NUM" || !ActionTypes.Contains(type)) return null;
            var start = Time(fragmentDelay) + Time(action["preDelay"]);
            var count = type == "SPAWN" ? Integer(action["count"]) ?? (action["count"] == null ? 1 : 0)
                : type == "PREVIEW_CURSOR" ? 2 : 1;
            var interval = type == "SPAWN" ? Time(action["interval"]) : type == "PREVIEW_CURSOR" ? .3d : 0d;
            if (start == null || count < 1 || interval == null) return null;
            return (start.Value, start.Value + (count - 1) * interval.Value, count);
        }

        internal static void RequireSpawnTargets(JObject action, IReadOnlyCollection<string> keys, JArray? routes,
            JObject? previous = null) => ActionDataRules.RequireSpawn(action, keys, routes, previous);
        internal static bool HasRoute(JArray? routes, int? index) =>
            routes != null && index is int position && position >= 0 && position < routes.Count &&
            routes[position] is JObject route && IsRouteDefinition(route);

        private static bool IsRouteDefinition(JObject route)
        {
            bool HasCell(string field) => route[field] is JObject cell &&
                Integer(cell["row"]) is int row && row != int.MinValue &&
                Integer(cell["col"]) is int col && col != int.MinValue;
            var motion = route["motionMode"];
            return (motion == null || motion.Type == JTokenType.String &&
                (motion.Value<string>() == "WALK" || motion.Value<string>() == "FLY") ||
                Integer(motion) is 0 or 1) && HasCell("startPosition") && HasCell("endPosition");
        }
    }
}
