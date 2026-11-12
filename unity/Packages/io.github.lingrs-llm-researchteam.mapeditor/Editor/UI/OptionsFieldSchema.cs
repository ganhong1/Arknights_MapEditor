#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Editor
{
    internal enum OptionsValueKind { Integer, Float, Boolean, String, Mask, Blackboard }

    internal sealed class OptionsFieldDefinition
    {
        internal string Key { get; }
        internal string Title { get; }
        internal OptionsValueKind Kind { get; }
        private readonly JToken initial;
        internal JToken Initial => initial.DeepClone();
        internal OptionsFieldDefinition(string key, string title, OptionsValueKind kind, object initial)
        { Key = key; Title = title; Kind = kind; this.initial = JToken.FromObject(initial); }
    }

    /// <summary>Fixed declaration metadata, not a whitelist of all business-valid values.</summary>
    internal static class OptionsFieldSchema
    {
        // PC 2.7.81: float-to-FP scales by 2^32 then truncates to a signed Int64.
        private const double FpScale = 4294967296d;
        private const double FpMagnitudeLimit = 2147483648d;
        private const long FpComparisonEpsilon = 42950;
        internal const int KnownMask = 524287;
        internal static readonly string[] MaskNames = {
            "CARD_LIST", "CHARACTER_MENU", "CHARACTER_INFO", "SYSTEM_MENU", "PAUSE_BUTTON",
            "SPEED_SWITCHER_BUTTON", "BATTLE_STATUS", "COST_PANEL", "SLOW_MOTION", "PAUSE_BUTTON_INTERACT",
            "SYSTEM_MENU_INTERACT", "SPEED_SWITCHER_BUTTON_INTERACT", "UNIT_HUD_SKILL_CAST_MASK",
            "WITHDRAWABLE_PANEL", "COST_PANEL_KEEP_CHARACTERLIMIT", "CHARACTER_LIMIT",
            "AUTOCHESS_SELL_OR_DESTORY", "CHARACTER_MENU_PANEL", "DRAG_UI_CARD"
        };
        internal static readonly IReadOnlyList<OptionsFieldDefinition> Fields = new[] {
            Field("characterLimit", "部署数量", OptionsValueKind.Integer, 99),
            Field("maxLifePoint", "初始生命参数", OptionsValueKind.Integer, 15),
            Field("initialCost", "初始费用", OptionsValueKind.Integer, 3),
            Field("maxCost", "费用上限", OptionsValueKind.Integer, 99),
            Field("costIncreaseTime", "自然回费周期 · 秒", OptionsValueKind.Float, 1f),
            Field("moveMultiplier", "移动倍率", OptionsValueKind.Float, .5f),
            Field("steeringEnabled", "转向开关", OptionsValueKind.Boolean, true),
            Field("reachableCheckIgnoreStartTile", "可达检查忽略起点格", OptionsValueKind.Boolean, false),
            Field("isTrainingLevel", "教学关标记", OptionsValueKind.Boolean, false),
            Field("isHardTrainingLevel", "困难教学标记", OptionsValueKind.Boolean, false),
            Field("isPredefinedCardsSelectable", "预设卡牌可选标记", OptionsValueKind.Boolean, false),
            Field("displayRestTime", "剩余时间显示", OptionsValueKind.Boolean, false),
            Field("maxPlayTime", "最大游玩时间参数 · 秒", OptionsValueKind.Float, -1f),
            Field("functionDisableMask", "功能禁用掩码", OptionsValueKind.Mask, 0),
            Field("configBlackBoard", "配置黑板", OptionsValueKind.Blackboard, new JArray())
        };
        private static OptionsFieldDefinition Field(string key, string title, OptionsValueKind kind, object initial)
            => new OptionsFieldDefinition(key, title, kind, initial);

        // An empty draft denotes an absent property; JSON "null" remains explicit null.
        internal static JToken? Parse(string raw) => raw.Length == 0 ? null :
            LevelDocument.Create(1, 1).WithSection("optionsFragment", raw).GetSection("optionsFragment");

        internal static bool TryMask(JToken? token, out int bits)
        {
            bits = 0;
            if (token == null) return false;
            if (token.Type == JTokenType.Integer)
                return int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out bits);
            if (token.Type != JTokenType.String) return false;
            var names = token.Value<string>()!.Split(',');
            foreach (var name in names)
            {
                var item = name.Trim();
                if (item == "NONE") continue;
                if (item == "ALL") { bits |= KnownMask; continue; }
                var index = Array.IndexOf(MaskNames, item);
                if (index < 0) return false;
                bits |= 1 << index;
            }
            return names.Length > 0;
        }

        internal static void ValidateChanges(JToken? before, JToken? after)
        {
            if (JToken.DeepEquals(before, after) || after == null || after.Type == JTokenType.Null) return;
            if (!(after is JObject result)) throw new FormatException("options 必须是对象、null 或未提供状态。");
            var previous = before as JObject;
            foreach (var field in Fields)
            {
                var value = result[field.Key];
                if (value == null || JToken.DeepEquals(previous?[field.Key], value)) continue;
                if (field.Kind == OptionsValueKind.Blackboard) ValidateBlackboard(previous?[field.Key], value);
                else ValidateValue(field.Kind, value, "options." + field.Key);
            }
        }

        internal static void ValidateValue(OptionsValueKind kind, JToken? value, string path)
        {
            if (value == null || value.Type == JTokenType.Null) return;
            var valid = false;
            switch (kind)
            {
                case OptionsValueKind.Integer:
                    valid = value.Type == JTokenType.Integer && int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _); break;
                case OptionsValueKind.Float:
                    valid = TryFiniteSingle(value, out _); break;
                case OptionsValueKind.Boolean: valid = value.Type == JTokenType.Boolean; break;
                case OptionsValueKind.String: valid = value.Type == JTokenType.String; break;
                case OptionsValueKind.Mask:
                    // Unknown enum strings are preserved for versions with additional named flags.
                    valid = value.Type == JTokenType.String || value.Type == JTokenType.Integer && int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _); break;
            }
            if (!valid) throw new FormatException(path + " 的值不符合 " + kind + " 存储类型；请修正本次修改。");
        }

        internal static bool TryFiniteSingle(JToken? value, out float single)
        {
            single = default;
            return value != null && IsNumber(value) &&
                float.TryParse(value.ToString(Newtonsoft.Json.Formatting.None), NumberStyles.Float, CultureInfo.InvariantCulture, out single) &&
                !float.IsNaN(single) && !float.IsInfinity(single);
        }

        internal static string? RuntimeNote(string key, JToken? value)
        {
            if (key == "maxLifePoint")
            {
                return value?.Type == JTokenType.Integer &&
                    int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var life) && life <= 0
                    ? "PC 2.7.81 · BattleController.LoadGameWithRune 以非正值初始化生命。"
                    : null;
            }

            if (key != "costIncreaseTime" && key != "moveMultiplier" && key != "maxPlayTime") return null;
            if (!TryFiniteSingle(value, out var number)) return null;
            if (number < -FpMagnitudeLimit || number >= FpMagnitudeLimit)
                return "PC 2.7.81 · 此值超出 FP 转换范围，读取为定点数的路径无法表示此值。";

            if (key == "costIncreaseTime" && (long)(number * FpScale) <= FpComparisonEpsilon)
                return "PC 2.7.81 · BattleCostManager 初始化时，此周期的自然回费计时器不启用。";
            if (key == "moveMultiplier" && number < 0f)
                return "PC 2.7.81 · 固定方向位移的速度项使用此负倍率。";
            return null;
        }

        private static void ValidateBlackboard(JToken? before, JToken after)
        {
            if (after.Type == JTokenType.Null) return;
            if (!(after is JArray records)) throw new FormatException("options.configBlackBoard 必须为数组或 null。");
            var old = before as JArray;
            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (old != null && old.Any(item => JToken.DeepEquals(item, record))) continue;
                if (!(record is JObject pair)) throw new FormatException("configBlackBoard[" + i + "] 必须为条目对象。");
                var previous = old != null && i < old.Count ? old[i] as JObject : null;
                foreach (var key in new[] { "key", "value", "valueStr" })
                    if (!JToken.DeepEquals(previous?[key], pair[key]))
                        ValidateValue(key == "value" ? OptionsValueKind.Float : OptionsValueKind.String, pair[key], "configBlackBoard[" + i + "]." + key);
            }
        }

        internal static bool IsNumber(JToken token) => token.Type == JTokenType.Integer || token.Type == JTokenType.Float || token.Type == JTokenType.Raw;
    }
}
