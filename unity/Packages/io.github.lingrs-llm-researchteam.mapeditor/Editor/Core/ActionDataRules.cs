#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Core
{
    internal enum ActionRouteStatus { Unknown, Main, Extra, Unused }

    /// <summary>Shared ActionData contract. Existing untouched fields retain their imported representation.</summary>
    internal static class ActionDataRules
    {
        private static readonly string[] BooleanFields =
        {
            "managedByScheduler", "blockFragment", "autoPreviewRoute", "autoDisplayEnemyInfo",
            "isUnharmfulAndAlwaysCountAsKilled", "dontBlockWave", "forceBlockWaveInBranch"
        };
        private static readonly string[] StringFields = { "key", "hiddenGroup", "randomSpawnGroupKey", "randomSpawnGroupPackKey" };
        private static readonly string[] RefreshValues = { "ALWAYS", "PER_DAY", "NEVER", "PER_SETTLE_DAY", "PER_SEASON" };

        internal static JObject NewAction(string type)
        {
            if (!WaveSchedule.ActionTypes.Contains(type) || type == "E_NUM") throw new FormatException("请选择可用动作类型。");
            return new JObject
            {
                ["actionType"] = type, ["managedByScheduler"] = true, ["count"] = 1,
                ["preDelay"] = 0f, ["interval"] = 0f
            };
        }

        internal static string? RouteLibrary(JObject document, JObject action, bool fromBranch) =>
            LevelDocument.ActionRouteLibrary(document, action, fromBranch);

        internal static ActionRouteStatus RouteStatus(JObject document, JObject action, bool fromBranch)
        {
            var library = RouteLibrary(document, action, fromBranch);
            if (library == "routes") return ActionRouteStatus.Main;
            if (library == "extraRoutes") return ActionRouteStatus.Extra;
            if (WaveSchedule.ActionType(action["actionType"]) == "PLAY_OPERA" &&
                LevelDocument.OperaRouteUse(document, action) == LevelDocument.OperaRoute.Unknown) return ActionRouteStatus.Unknown;
            return ActionRouteStatus.Unused;
        }

        internal static void Require(JObject document, JObject action, JObject? previous,
            IReadOnlyCollection<string> enemyKeys, bool fromBranch)
        {
            RequireSchema(action, previous);
            var type = WaveSchedule.ActionType(action["actionType"]);
            bool ConsumedChanged(string field) => previous == null || WaveSchedule.ActionType(previous["actionType"]) != type ||
                !JToken.DeepEquals(action[field], previous[field]);
            var library = RouteLibrary(document, action, fromBranch);
            var routes = library == null ? null : document[library] as JArray;
            if (type == "SPAWN")
                RequireSpawn(action, enemyKeys, routes, previous, fromBranch ? "额外路线" : "主路线");
            else if (type == "PREVIEW_CURSOR" && ConsumedChanged("routeIndex"))
                RequireRoute(routes, WaveSchedule.Integer(action["routeIndex"]), fromBranch ? "额外路线" : "主路线");
            else if (type == "EMPTY" && fromBranch && ConsumedChanged("routeIndex") && action["routeIndex"] != null &&
                WaveSchedule.Integer(action["routeIndex"]) != -1)
                RequireRoute(routes, WaveSchedule.Integer(action["routeIndex"]), "额外路线");
            else if (type == "PLAY_OPERA" && library != null && (ConsumedChanged("key") || ConsumedChanged("routeIndex")))
                RequireRoute(routes, action["routeIndex"] == null ? 0 : WaveSchedule.Integer(action["routeIndex"]), "主路线");

            if (!ConsumedChanged("key")) return;
            var key = Text(action["key"]);
            if (type == "DISPLAY_ENEMY_INFO" && (key == null || !enemyKeys.Contains(key)))
                throw new FormatException("请选择本关已定义的敌人。");
            if (type is "ACTIVATE_PREDEFINED" or "WITHDRAW_PREDEFINED" or "TRIGGER_PREDEFINED")
                RequireAlias(document, action, type);
            if ((type is "STORY" or "TUTORIAL" or "PLAY_BGM" or "PLAY_OPERA" or "DIALOG") && string.IsNullOrWhiteSpace(key))
                throw new FormatException("key 需要对应资源或命令的名称。");
        }

        internal static void RequireFields(JObject action, JObject previous, JArray? routes)
        {
            RequireSchema(action, previous);
            if (WaveSchedule.ActionType(action["actionType"]) == "PREVIEW_CURSOR" &&
                (WaveSchedule.ActionType(previous["actionType"]) != "PREVIEW_CURSOR" ||
                 !JToken.DeepEquals(action["routeIndex"], previous["routeIndex"])))
                RequireRoute(routes, WaveSchedule.Integer(action["routeIndex"]), "主路线");
        }

        internal static void RequireSpawn(JObject action, IReadOnlyCollection<string> keys, JArray? routes,
            JObject? previous = null, string routeLabel = "主路线")
        {
            RequireSchema(action, previous);
            bool Changed(string field) => previous == null || WaveSchedule.ActionType(previous["actionType"]) != "SPAWN" ||
                !JToken.DeepEquals(previous[field], action[field]);
            var key = Text(action["key"]);
            if (Changed("key") && (key == null || !keys.Contains(key))) throw new FormatException("请选择本关已定义的敌人。");
            if (Changed("routeIndex")) RequireRoute(routes, WaveSchedule.Integer(action["routeIndex"]), routeLabel);
            if (Changed("count") && (WaveSchedule.Integer(action["count"]) is not int count || count < 1))
                throw new FormatException("count 必须是大于 0 的整数。");
            foreach (var field in new[] { "preDelay", "interval" })
                if (Changed(field) && (action[field] == null || WaveSchedule.Time(action[field]) == null))
                    throw new FormatException(field + " 必须是非负的有限秒数。");
        }

        private static void RequireSchema(JObject action, JObject? previous)
        {
            bool Changed(string field) => previous == null || !JToken.DeepEquals(action[field], previous[field]);
            var type = WaveSchedule.ActionType(action["actionType"]);
            if (Changed("actionType") && (!WaveSchedule.ActionTypes.Contains(type) || type == "E_NUM"))
                throw new FormatException("actionType 需要可用的名称或枚举值。");
            foreach (var field in BooleanFields)
                if (Changed(field) && action[field] is JToken value && value.Type != JTokenType.Boolean)
                    throw new FormatException(field + " 必须是布尔值。");
            foreach (var field in StringFields)
                if (Changed(field) && action[field] is JToken value && value.Type is not (JTokenType.Null or JTokenType.String))
                    throw new FormatException(field + " 必须是字符串或 null。");
            foreach (var field in new[] { "preDelay", "interval" })
                if (Changed(field) && (action[field] != null || previous?[field] != null) &&
                    (action[field] == null || WaveSchedule.Time(action[field]) == null))
                    throw new FormatException(field + " 必须是非负的有限秒数。");
            if (Changed("count") && (action["count"] != null || previous?["count"] != null) &&
                (WaveSchedule.Integer(action["count"]) is not int count || count < (type == "SPAWN" ? 1 : 0)))
                throw new FormatException("count 需要有效整数。");
            foreach (var field in new[] { "routeIndex", "weight" })
                if (Changed(field) && action[field] != null && WaveSchedule.Integer(action[field]) == null)
                    throw new FormatException(field + " 需要有效整数。");
            foreach (var field in new[] { "randomType", "refreshType" })
            {
                if (!Changed(field) || action[field] == null) continue;
                var ordinal = WaveSchedule.Integer(action[field]);
                if (!(ordinal is >= 0 and < 5) && !RefreshValues.Contains(Text(action[field]) ?? ""))
                    throw new FormatException(field + " 需要原版刷新枚举值。");
            }
        }

        private static void RequireAlias(JObject document, JObject action, string type)
        {
            var key = Text(action["key"]);
            if (string.IsNullOrWhiteSpace(key)) throw new FormatException("请选择预设别名。");
            var aliases = BranchSchedule.Aliases(document, type).Select(alias => alias.Alias).ToArray();
            foreach (var candidate in LevelDocument.PredefinedActionAliases(action))
                if (!aliases.Contains(candidate)) throw new FormatException("本关预设中未找到别名：" + candidate);
        }

        private static void RequireRoute(JArray? routes, int? index, string label)
        {
            if (!WaveSchedule.HasRoute(routes, index)) throw new FormatException("请选择本关已保存的" + label + "。");
        }
        private static string? Text(JToken? token) => token?.Type == JTokenType.String ? token.Value<string>() : null;
    }
}
