#nullable enable
using System;
using System.Collections.Generic;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        private void BuildMapRuleEditor(VisualElement parent, string key)
        {
            var edge = key == "blockEdges";
            var slot = edge ? "mapData:blockEdges" : key;
            var target = edge ? "mapData.blockEdges" : key;
            parent.Add(new Label(edge ? "阻挡边 · LevelData.mapData.blockEdges" : "禁止部署格 · LevelData.tilesDisallowToLocate"));
            parent.Add(new HelpBox(edge ? "按格子、方向和移动类型配置阻挡边。" : "原生地图初始化将这些格子的部署类型改为 NONE；原 TileData 保留。", HelpBoxMessageType.Info));
            BuildMapRuleSceneTools(parent, key);
            var list = new VisualElement { name = "maprules-list-" + key };
            parent.Add(list);
            var raw = new TextField("原始 JSON") { multiline = true, name = "maprules-json-" + key };
            raw.style.minHeight = 90;
            var foldout = new Foldout { text = "原始 JSON", value = false };
            foldout.Add(raw); parent.Add(foldout);
            string Read() => session!.Document.GetMapRules(key)?.ToString(Formatting.Indented) ?? "";
            JToken? Parse(string text) => string.IsNullOrWhiteSpace(text) ? null : LevelDocument.Create(1, 1).WithSection("rule", text).GetSection("rule");
            void Capture(string text)
            {
                RequireWorkspaceEditable();
                var binding = inspectorBindings[slot];
                InspectorStore.Begin(slot, binding.Document, binding.Target, binding.Initial);
                InspectorStore.Update(slot, text);
                PersistInspectorDrafts();
            }
            void Change(Action<JArray> edit)
            {
                RequireWorkspaceEditable();
                var current = Parse(raw.value);
                if (current != null && current.Type != JTokenType.Null && !(current is JArray))
                    throw new EditorOperationRejectedException("请先将原始 JSON 调整为数组。");
                var next = current is JArray array ? (JArray)array.DeepClone() : new JArray();
                edit(next);
                Capture(next.ToString(Formatting.Indented));
                DisplayLocalDraft(inspectorBindings[slot], next.ToString(Formatting.Indented));
            }
            void Render(string text)
            {
                raw.SetValueWithoutNotify(text); list.Clear(); list.SetEnabled(true);
                JToken? token;
                try { token = Parse(text); }
                catch (Exception error) when (error is JsonException || error is LevelDocumentException) { list.Add(new Label("JSON 未完成，可继续在原始 JSON 中编辑。")); return; }
                if (!(token is JArray rows))
                {
                    list.Add(new Label(token == null ? "未提供字段" : token.Type == JTokenType.Null ? "null" : "当前值不是数组")); return;
                }
                list.Add(new Label(rows.Count + " 项"));
                for (var i = 0; i < rows.Count; i++)
                {
                    var index = i;
                    var card = new VisualElement(); card.AddToClassList("map-rule-card");
                    card.style.marginBottom = 8; card.style.paddingLeft = 8; card.style.paddingRight = 8;
                    card.style.borderLeftWidth = card.style.borderRightWidth = card.style.borderTopWidth = card.style.borderBottomWidth = 1;
                    card.style.borderLeftColor = card.style.borderRightColor = card.style.borderTopColor = card.style.borderBottomColor = new UnityEngine.Color(.32f,.42f,.49f);
                    list.Add(card); card.Add(new Label("#" + index));
                    if (rows[i] is JObject item && (edge ? item["pos"] : item) is JObject position)
                    {
                        foreach (var axis in new[] { "row", "col" })
                        {
                            var axisKey = axis;
                            if (position[axis]?.Type != JTokenType.Integer) { card.Add(new Label(axis + " · " + position[axis])); continue; }
                            var integer = position.Value<long>(axis);
                            if (integer < int.MinValue || integer > int.MaxValue) { card.Add(new Label(axis + " · " + integer)); continue; }
                            var input = new IntegerField(axis == "row" ? "行 row" : "列 col") { value = (int)integer, isDelayed = true };
                            input.RegisterValueChangedCallback(e =>
                            {
                                try { Change(a => { var obj = (JObject)a[index]; ((JObject)(edge ? obj["pos"]! : obj))[axisKey] = e.newValue; }); }
                                catch (Exception error) { input.SetValueWithoutNotify(e.previousValue); if (status != null) status.text = error.Message; }
                            });
                            card.Add(input);
                        }
                        if (edge)
                        {
                            AddMapRuleEnum(card, "方向", "direction", item, new[] { "UP", "RIGHT", "DOWN", "LEFT" }, v => Change(a => ((JObject)a[index])["direction"] = v));
                            AddMapRuleEnum(card, "阻挡移动类型", "blockMask", item, new[] { "NONE", "WALK_ONLY", "FLY_ONLY", "ALL" }, v => Change(a => ((JObject)a[index])["blockMask"] = v));
                        }
                    }
                    else card.Add(new Label(rows[i].ToString(Formatting.None)));
                    AddButton(card, "删除此项", () => Change(a => a.RemoveAt(index)));
                }
            }
            BindLocalDraft(slot, Read(), target, Render);
            raw.RegisterValueChangedCallback(e =>
            {
                try { Capture(e.newValue); list.SetEnabled(false); }
                catch (Exception error) { raw.SetValueWithoutNotify(e.previousValue); if (status != null) status.text = error.Message; }
            });
            var actions = new VisualElement(); actions.AddToClassList("workbench-tabs"); parent.Add(actions);
            AddButton(actions, "新增坐标", () => Change(a => a.Add(edge ? new JObject { ["pos"] = new JObject { ["row"] = 0, ["col"] = 0 }, ["direction"] = "UP", ["blockMask"] = "ALL" } : new JObject { ["row"] = 0, ["col"] = 0 })));
            AddButton(actions, "刷新表单", () => Render(raw.value));
            AddButton(actions, "读取当前值", () => ReloadLocalDraft(slot, target, Read()));
            AddButton(actions, "应用到地图", () =>
            {
                RequireMapDataMutation(slot); RequireLocalDraft(slot, target);
                var next = string.IsNullOrWhiteSpace(raw.value) ? session!.Document.RemoveMapRules(key) : session!.Document.SetMapRules(key, raw.value);
                CommitEdit(next, "编辑 " + target, null); AppliedLocalDraft(slot);
            });
            foreach (var pair in new[] { ("设为空数组", "[]"), ("设为 null", "null"), ("移除此字段", "") })
            {
                var choice = pair;
                AddButton(actions, choice.Item1, () => { Capture(choice.Item2); Render(choice.Item2); });
            }
            refreshFieldSummaries.Add(() => RefreshCleanMapDraft(slot, target, Read()));
        }

        private void AddMapRuleEnum(VisualElement parent, string label, string property, JObject item, string[] names, Action<JToken> change)
        {
            var original = item[property];
            var value = original?.ToString() ?? "未提供";
            var numeric = original?.Type == JTokenType.Integer;
            if (numeric && int.TryParse(value, out var number) && number >= 0 && number < names.Length) value = names[number];
            var choices = new List<string>(names);
            if (!choices.Contains(value)) choices.Insert(0, value);
            var input = new PopupField<string>(label, choices, value);
            input.RegisterValueChangedCallback(e =>
            {
                try
                {
                    var index = Array.IndexOf(names, e.newValue);
                    if (index < 0) return;
                    change(numeric ? new JValue(index) : new JValue(e.newValue));
                }
                catch (Exception error) { input.SetValueWithoutNotify(e.previousValue); if (status != null) status.text = error.Message; }
            });
            parent.Add(input);
        }
    }
}
