#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class TerrainBlackboardFields : Foldout
    {
        private readonly Action<string, string, JToken> write;
        private string? tileKey;
        private JToken? board;
        private bool initialized;

        internal TerrainBlackboardFields(Action<string, string, JToken> writeValue)
        {
            write = writeValue;
            name = "terrain-blackboard-fields";
            text = "黑板选项与校验";
            value = false;
        }

        internal void Show(JObject? tile)
        {
            var key = tile?["tileKey"] is JValue { Type: JTokenType.String } tileId
                ? tileId.Value<string>() : null;
            if (initialized && tileKey == key && JToken.DeepEquals(board, tile?["blackboard"])) return;
            initialized = true;
            tileKey = key;
            board = tile?["blackboard"]?.DeepClone();
            contentContainer.Clear();
            SetEnabled(tile != null);
            if (tile == null || key == null) return;

            IReadOnlyList<JObject> rules;
            try { rules = TerrainBlackboardCatalog.Rules(key); }
            catch (JsonException) { Add(new HelpBox("黑板规则无法读取，可继续编辑 JSON。", HelpBoxMessageType.Warning)); return; }
            catch (FormatException error) { Add(new HelpBox(error.Message, HelpBoxMessageType.Warning)); return; }
            if (rules.Count == 0)
            {
                Add(new Label("固定资料中暂无该地块的黑板规则。"));
                return;
            }

            Add(new HelpBox("选项按固定版本和地块提供。缺键沿用游戏默认行为，未确认的值可在高级 JSON 中编辑。", HelpBoxMessageType.Info));
            foreach (var rule in rules)
                Add(BuildRow(tile, rule));
        }

        private VisualElement BuildRow(JObject tile, JObject rule)
        {
            var key = rule.Value<string>("key")!;
            var kind = rule.Value<string>("kind");
            var field = rule.Value<string>("field");
            var row = new VisualElement { name = "terrain-blackboard-rule-" + key };
            row.AddToClassList("terrain-blackboard-rule");
            var label = new Label(key);
            label.AddToClassList("tiledata-preview-important");
            row.Add(label);

            if (field == null)
            {
                row.Add(Wrapped(kind == "inactive" ? "当前组件读取此键，实际范围仍由现有碰撞体决定。"
                    : "仅原版记录，完整规则待证。"));
            }
            else if (!TerrainBlackboardCatalog.TryFindPair(tile, key, out var pair))
            {
                row.Add(new HelpBox("黑板格式异常或键重复，请在高级 JSON 中核对原值。", HelpBoxMessageType.Warning));
            }
            else if (rule["choices"] is JArray choices)
            {
                AddChoice(row, key, field, pair?[field], choices);
            }
            else
            {
                AddNumber(row, key, field, kind!, pair?[field]);
            }

            var details = new Foldout { text = "默认值、生效条件和原版值", value = false };
            foreach (var property in new[] { ("default", "缺键时"), ("condition", "生效条件"), ("guidance", "编辑提示"), ("evidence", "证据") })
                if (rule.Value<string>(property.Item1) is string note)
                    details.Add(Wrapped(property.Item2 + " · " + note));
            if (rule["observed"] is JArray observed && observed.Count > 0)
            {
                var examples = new TextField("原版 [value, valueStr]")
                {
                    isReadOnly = true, multiline = true, value = string.Join("\n", observed.Select(x => x.ToString(Formatting.None)))
                };
                examples.AddToClassList("tiledata-preview-data");
                details.Add(examples);
            }
            row.Add(details);
            return row;
        }

        private void AddChoice(VisualElement row, string key, string field, JToken? original, JArray choices)
        {
            var values = choices.OfType<JObject>().ToArray();
            var labels = values.Select(option => $"{option.Value<string>("label")} · {option["value"]!.ToString(Formatting.None)}").ToList();
            var index = Array.FindIndex(values, option => EqualValue(option["value"], original));
            if (index < 0)
            {
                index = labels.Count;
                labels.Add(original == null ? "未设置 · 继承原生默认" : "保留原值 · " + original.ToString(Formatting.None));
            }
            var input = new PopupField<string>(labels, index) { name = "terrain-blackboard-choice-" + key };
            input.RegisterValueChangedCallback(change =>
            {
                var selected = labels.IndexOf(change.newValue);
                if (selected >= 0 && selected < values.Length)
                    write(key, field, values[selected]["value"]!);
            });
            row.Add(input);
        }

        private void AddNumber(VisualElement row, string key, string field, string kind, JToken? original)
        {
            var input = new TextField { name = "terrain-blackboard-number-" + key, isDelayed = true };
            var initial = original?.ToString(Formatting.None) ?? "";
            input.SetValueWithoutNotify(initial);
            input.tooltip = "留空显示未设置，输入后按回车确认。使用小数点作为小数分隔符。";
            var warning = Wrapped("");
            warning.style.display = DisplayStyle.None;
            input.RegisterValueChangedCallback(change =>
            {
                if (TerrainBlackboardCatalog.TryNumber(kind, change.newValue, out var parsed))
                {
                    warning.style.display = DisplayStyle.None;
                    write(key, field, parsed!);
                    return;
                }
                input.SetValueWithoutNotify(initial);
                warning.text = kind == "integer" ? "请输入 32 位整数。" : "请输入有限数值。";
                warning.style.display = DisplayStyle.Flex;
            });
            row.Add(input);
            row.Add(warning);
        }

        private static bool EqualValue(JToken? option, JToken? original)
        {
            if (JToken.DeepEquals(option, original)) return true;
            return option?.Type == JTokenType.Integer && original?.Type == JTokenType.Float &&
                option.Value<double>() == original.Value<double>();
        }

        private static Label Wrapped(string text)
        {
            var label = new Label(text);
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }
    }
}
