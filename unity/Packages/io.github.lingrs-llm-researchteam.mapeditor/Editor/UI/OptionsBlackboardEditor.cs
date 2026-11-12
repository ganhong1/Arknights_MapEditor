#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class OptionsBlackboardEditor : Foldout
    {
        private readonly Action<JToken?> write;
        private readonly Action<int> remember;
        private readonly Label state = new Label();
        private readonly Label detailPath = new Label();
        private readonly Label diagnostics = new Label();
        private readonly List<int> indices = new List<int>();
        private readonly ListView list;
        private readonly VisualElement body = new VisualElement();
        private readonly VisualElement details = new VisualElement();
        private readonly Dictionary<string, OptionsValueEditor> fields = new Dictionary<string, OptionsValueEditor>();
        private readonly TextField original = new TextField("完整条目（只读，扩展成员保留）") { multiline = true, isReadOnly = true };
        private readonly Button create;
        private readonly Button setNull;
        private readonly Button remove;
        private JToken? currentValue;
        private int selected;
        private bool displaying;
        private bool editingEnabled = true;
        private readonly VisualElement operations;
        private readonly OptionsSampleBrowser observed;
        private readonly OptionsBlackboardSamples knownKeys;

        internal OptionsBlackboardEditor(Action<JToken?> write, int selected, Action<int> remember)
        {
            this.write = write; this.selected = selected; this.remember = remember;
            name = "options-field-configBlackBoard"; text = "configBlackBoard · 配置黑板";
            value = false;
            Add(state);
            var actions = Toolbar(this);
            create = OptionsValueEditor.Action(actions, "添加空列表", () => Write(new JArray()), "options-blackboard-create");
            setNull = OptionsValueEditor.Action(actions, "设为 null", () => Write(JValue.CreateNull()), "options-blackboard-null");
            remove = OptionsValueEditor.Action(actions, "移除字段", () => Write(null), "options-blackboard-remove");
            var summary = new Label("保留条目顺序、重复键和大小写；查询会按读取类型寻找首个匹配项。数值读取要求 valueStr 为空，字符串读取要求非空。");
            summary.style.whiteSpace = WhiteSpace.Normal; Add(summary);
            list = new ListView(indices, 26, () => new Label(), (element, index) =>
            {
                var id = indices[index]; var record = (currentValue as JArray)?[id];
                ((Label)element).text = "[" + id + "] " + (record is JObject pair ? pair["key"]?.ToString() ?? "（无 key）" : "（非标准条目）");
            }) { name = "options-blackboard-list", selectionType = SelectionType.Single };
            list.style.height = 150;
            list.onSelectionChange += items =>
            {
                if (!displaying && items.FirstOrDefault() is int index) { this.selected = index; remember(index); DisplayDetails(); }
            };
            body.Add(list);
            operations = Toolbar(body);
            OptionsValueEditor.Action(operations, "新增条目", AddRecord, "options-blackboard-add");
            OptionsValueEditor.Action(operations, "复制条目", CopyRecord, "options-blackboard-copy");
            OptionsValueEditor.Action(operations, "删除条目", DeleteRecord, "options-blackboard-delete");
            OptionsValueEditor.Action(operations, "上移", () => MoveRecord(-1), "options-blackboard-up");
            OptionsValueEditor.Action(operations, "下移", () => MoveRecord(1), "options-blackboard-down");
            detailPath.style.unityFontStyleAndWeight = FontStyle.Bold; body.Add(detailPath);
            AddPairField("key", "键名", OptionsValueKind.String, "");
            AddPairField("value", "数值", OptionsValueKind.Float, 0f);
            AddPairField("valueStr", "字符串", OptionsValueKind.String, "");
            body.Add(details);
            diagnostics.style.whiteSpace = WhiteSpace.Normal; body.Add(diagnostics);
            original.name = "options-blackboard-record-json"; body.Add(original); Add(body);
            var reference = new Foldout { name = "options-blackboard-reference", text = "原版黑板参考", value = false };
            observed = new OptionsSampleBrowser("configBlackBoard", Write, "替换整个黑板列表为");
            knownKeys = new OptionsBlackboardSamples(AppendObserved);
            reference.Add(observed);
            reference.Add(knownKeys);
            Add(reference);
        }

        private void AddPairField(string key, string title, OptionsValueKind kind, object initial)
        {
            var field = new OptionsValueEditor(new OptionsFieldDefinition(key, title, kind, initial),
                candidate => WriteMember(key, candidate), "options-blackboard-");
            fields.Add(key, field); details.Add(field);
        }

        internal void Display(JToken? current)
        {
            displaying = true;
            currentValue = current?.DeepClone();
            var records = currentValue as JArray;
            state.text = current == null ? "未提供" : current.Type == JTokenType.Null ? "显式 null" :
                records != null ? records.Count + " 条有序记录" : "非标准原值 · " + current.Type + "；可在 options 原始 JSON 中修正。";
            create.SetEnabled(editingEnabled && (current == null || current.Type == JTokenType.Null));
            setNull.SetEnabled(editingEnabled && current != null && current.Type != JTokenType.Null); remove.SetEnabled(editingEnabled && current != null);
            operations.SetEnabled(editingEnabled);
            observed.DisplayCurrent(current, "选用整数组将替换当前 " + (records?.Count ?? 0) + " 条记录；按键浏览则追加完整条目。两者都只修改草稿。");
            observed.SetEditingEnabled(editingEnabled); knownKeys.SetEditingEnabled(editingEnabled);
            body.style.display = records == null ? DisplayStyle.None : DisplayStyle.Flex;
            var scroll = list.Q<ScrollView>(); var offset = scroll?.scrollOffset ?? Vector2.zero;
            var changedCount = indices.Count != (records?.Count ?? 0);
            if (changedCount)
            {
                indices.Clear();
                if (records != null) for (var i = 0; i < records.Count; i++) indices.Add(i);
                list.Rebuild();
            }
            else list.RefreshItems();
            if (scroll != null) scroll.scrollOffset = offset;
            selected = indices.Count == 0 ? -1 : Math.Max(0, Math.Min(selected, indices.Count - 1));
            remember(selected);
            list.SetSelectionWithoutNotify(selected < 0 ? Array.Empty<int>() : new[] { selected });
            displaying = false;
            DisplayDetails();
        }

        internal void SetEditingEnabled(bool enabled)
        {
            editingEnabled = enabled;
            foreach (var field in fields.Values) field.SetEditingEnabled(enabled);
            Display(currentValue);
        }

        private void AppendObserved(JToken candidate)
        {
            if (!editingEnabled) throw new InvalidOperationException("请先创建 options 对象并解除编辑锁。");
            if (!(candidate is JObject)) throw new FormatException("黑板候选必须为完整条目对象。");
            var records = currentValue == null || currentValue.Type == JTokenType.Null ? new JArray() : CopyRecords();
            selected = records.Count; records.Add(candidate.DeepClone()); Write(records);
        }

        private void DisplayDetails()
        {
            var records = currentValue as JArray;
            var record = records != null && selected >= 0 && selected < records.Count ? records[selected] : null;
            details.style.display = record is JObject ? DisplayStyle.Flex : DisplayStyle.None;
            original.style.display = record == null ? DisplayStyle.None : DisplayStyle.Flex;
            detailPath.text = record == null ? "尚未选择条目" : "options.configBlackBoard[" + selected + "]";
            original.SetValueWithoutNotify(record?.ToString(Formatting.Indented) ?? "");
            foreach (var field in fields) field.Value.Display((record as JObject)?[field.Key]);
            if (!(record is JObject pair)) { diagnostics.text = record == null ? "" : "非标准条目保持原文；使用 options 原始 JSON 修正。"; return; }
            var key = pair["key"]?.Type == JTokenType.String ? pair["key"]!.Value<string>() : null;
            var matches = key == null ? 0 : records!.OfType<JObject>().Count(item =>
                item["key"]?.Type == JTokenType.String && string.Equals(item["key"]!.Value<string>(), key, StringComparison.OrdinalIgnoreCase));
            diagnostics.text = (matches > 1 ? "存在 " + matches + " 条同名/大小写近似键，顺序及数值/字符串类型会影响读取。" : "") +
                (pair["valueStr"]?.Type == JTokenType.String && !string.IsNullOrEmpty(pair["valueStr"]!.Value<string>()) ?
                    " 当前为非空字符串通道；数值读取会跳过此条。" : " valueStr 为空/null 时可进入数值读取；具体键的约束仍由消费者决定。");
        }

        private void WriteMember(string key, JToken? candidate)
        {
            var records = CopyRecords();
            if (selected < 0 || selected >= records.Count || !(records[selected] is JObject pair)) return;
            if (candidate == null) pair.Remove(key); else pair[key] = candidate.DeepClone();
            Write(records);
        }
        private JArray CopyRecords() => (JArray)((currentValue as JArray)?.DeepClone() ?? throw new InvalidOperationException("请先创建黑板列表。"));
        private void AddRecord()
        {
            var records = CopyRecords(); selected = records.Count;
            records.Add(new JObject { ["key"] = "", ["value"] = 0, ["valueStr"] = "" }); Write(records);
        }
        private void CopyRecord()
        {
            var records = CopyRecords(); if (selected < 0 || selected >= records.Count) return;
            records.Insert(selected + 1, records[selected].DeepClone()); selected++; Write(records);
        }
        private void DeleteRecord()
        {
            var records = CopyRecords(); if (selected < 0 || selected >= records.Count) return;
            records.RemoveAt(selected); selected = Math.Min(selected, records.Count - 1); Write(records);
        }
        private void MoveRecord(int direction)
        {
            var records = CopyRecords(); var next = selected + direction;
            if (selected < 0 || selected >= records.Count || next < 0 || next >= records.Count) return;
            var record = records[selected].DeepClone(); records.RemoveAt(selected); records.Insert(next, record); selected = next; Write(records);
        }
        private void Write(JToken? candidate)
        {
            try { write(candidate); }
            catch (Exception error) { Display(currentValue); state.text = error.Message; }
        }
        private static VisualElement Toolbar(VisualElement parent)
        { var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexWrap = Wrap.Wrap; parent.Add(row); return row; }
    }
}
