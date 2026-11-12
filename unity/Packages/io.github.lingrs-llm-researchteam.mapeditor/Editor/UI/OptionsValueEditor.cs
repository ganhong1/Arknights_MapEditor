#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Arknights.MapEditor.Catalog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    /// <summary>Stable controls for one native property; missing/null never become implicit defaults.</summary>
    internal sealed class OptionsValueEditor : VisualElement
    {
        private readonly OptionsFieldDefinition definition;
        private readonly Action<JToken?> write;
        private readonly Label state = new Label();
        private readonly IntegerField integer = new IntegerField { isDelayed = true };
        private readonly FloatField number = new FloatField { isDelayed = true };
        private readonly Toggle boolean = new Toggle();
        private readonly TextField text = new TextField();
        private readonly TextField raw = new TextField("原值 JSON") { isDelayed = true };
        private readonly VisualElement controls = new VisualElement();
        private readonly Button add;
        private readonly Button setNull;
        private readonly Button remove;
        private readonly Foldout mask = new Foldout { text = "展开功能禁用位", value = false };
        private readonly Label maskSummary = new Label();
        private readonly Toggle[] flags = new Toggle[OptionsFieldSchema.MaskNames.Length];
        private JToken? value;
        private readonly OptionsSampleBrowser? observed;
        private readonly HelpBox? runtimeNote;
        private bool editingEnabled = true;

        internal OptionsValueEditor(OptionsFieldDefinition definition, Action<JToken?> write, string namePrefix = "options-field-")
        {
            this.definition = definition; this.write = write;
            name = namePrefix + definition.Key;
            style.marginBottom = 8;
            var title = new Label(definition.Key + " · " + definition.Title);
            title.style.whiteSpace = WhiteSpace.Normal;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            Add(title); Add(state);
            integer.name = name + "-integer"; number.name = name + "-float";
            boolean.name = name + "-boolean"; text.name = name + "-string"; raw.name = name + "-raw";
            controls.Add(integer); controls.Add(number); controls.Add(boolean); controls.Add(text); controls.Add(raw); Add(controls);
            integer.RegisterValueChangedCallback(e => Write(new JValue(e.newValue)));
            number.RegisterValueChangedCallback(e => Write(new JValue(e.newValue)));
            boolean.RegisterValueChangedCallback(e => Write(new JValue(e.newValue)));
            text.RegisterValueChangedCallback(e => Write(new JValue(e.newValue)));
            raw.RegisterValueChangedCallback(e =>
            {
                try { Write(OptionsFieldSchema.Parse(e.newValue)); }
                catch (Exception error) { Display(value); state.text = "输入未应用：" + error.Message; }
            });
            var actions = new VisualElement(); actions.style.flexDirection = FlexDirection.Row; actions.style.flexWrap = Wrap.Wrap;
            add = Action(actions, "添加字段", () => Write(definition.Initial), name + "-add");
            add.tooltip = "显式填写参考值：" + definition.Initial.ToString(Formatting.None) + "；不会自动写入文档。";
            setNull = Action(actions, "设为 null", () => Write(JValue.CreateNull()), name + "-null");
            remove = Action(actions, "移除字段", () => Write(null), name + "-remove");
            Add(actions);
            if (definition.Kind == OptionsValueKind.Mask) BuildMask();
            if (namePrefix == "options-field-")
            {
                runtimeNote = new HelpBox("", HelpBoxMessageType.Warning) { name = name + "-runtime-note" };
                Insert(IndexOf(controls) + 1, runtimeNote);
                observed = new OptionsSampleBrowser(definition.Key, ChooseObserved);
                Add(observed);
            }
        }

        internal void Display(JToken? current)
        {
            value = current?.DeepClone();
            var present = value != null;
            var editable = present && value!.Type != JTokenType.Null;
            state.text = !present ? "未提供" : !editable ? "显式 null" : "已提供 · " + value!.Type;
            integer.style.display = number.style.display = boolean.style.display = text.style.display = raw.style.display = DisplayStyle.None;
            if (editable) DisplayInput();
            if (runtimeNote != null)
            {
                var note = OptionsFieldSchema.RuntimeNote(definition.Key, value);
                runtimeNote.text = note ?? "";
                runtimeNote.style.display = note == null ? DisplayStyle.None : DisplayStyle.Flex;
            }
            add.text = !present ? "添加字段" : "填写值";
            add.style.display = editable ? DisplayStyle.None : DisplayStyle.Flex;
            add.SetEnabled(editingEnabled); setNull.SetEnabled(editingEnabled && present && editable); remove.SetEnabled(editingEnabled && present);
            controls.SetEnabled(editingEnabled);
            observed?.DisplayCurrent(current, definition.Kind == OptionsValueKind.Mask ? "选用候选只调整已知位，保留当前未知位；完整替换可用原始 JSON。" : null);
            observed?.SetEditingEnabled(editingEnabled);
            if (definition.Kind == OptionsValueKind.Mask) RefreshMask();
        }

        internal void SetEditingEnabled(bool enabled) { editingEnabled = enabled; Display(value); }

        private void ChooseObserved(JToken candidate)
        {
            if (!editingEnabled) throw new InvalidOperationException("请先创建 options 对象并解除编辑锁。");
            if (definition.Kind != OptionsValueKind.Mask) { Write(candidate); return; }
            if (!OptionsFieldSchema.TryMask(candidate, out var desired))
                throw new InvalidOperationException("此候选不能解析为掩码，请查看原文并自行编辑。");
            if (value == null || value.Type == JTokenType.Null) { Write(candidate); return; }
            if (!OptionsFieldSchema.TryMask(value, out var current))
                throw new InvalidOperationException("当前掩码无法解析，已保留原文；请在原始 JSON 中明确修改。");
            Write(new JValue((current & ~OptionsFieldSchema.KnownMask) | (desired & OptionsFieldSchema.KnownMask)));
        }

        private void DisplayInput()
        {
            var valid = true;
            try { OptionsFieldSchema.ValidateValue(definition.Kind, value, definition.Key); }
            catch (FormatException error) { valid = false; state.text = "保留非标准原值 · " + error.Message; }
            if (!valid || definition.Kind == OptionsValueKind.Mask)
            { raw.SetValueWithoutNotify(value!.ToString(Formatting.None)); raw.style.display = DisplayStyle.Flex; return; }
            switch (definition.Kind)
            {
                case OptionsValueKind.Integer:
                    integer.SetValueWithoutNotify(int.Parse(value!.ToString(), CultureInfo.InvariantCulture)); integer.style.display = DisplayStyle.Flex; break;
                case OptionsValueKind.Float:
                    OptionsFieldSchema.TryFiniteSingle(value, out var single);
                    number.SetValueWithoutNotify(single); number.style.display = DisplayStyle.Flex; break;
                case OptionsValueKind.Boolean:
                    boolean.SetValueWithoutNotify(value!.Value<bool>()); boolean.style.display = DisplayStyle.Flex; break;
                case OptionsValueKind.String:
                    text.SetValueWithoutNotify(value!.Value<string>()!); text.style.display = DisplayStyle.Flex; break;
            }
        }

        private void Write(JToken? candidate)
        {
            try
            {
                OptionsFieldSchema.ValidateValue(definition.Kind, candidate, definition.Key);
                write(candidate?.DeepClone());
            }
            catch (Exception error) { Display(value); state.text = error.Message; }
        }

        private void BuildMask()
        {
            mask.name = name + "-bits"; mask.Add(maskSummary);
            var find = new TextField("筛选位名称") { name = "options-mask-search" };
            mask.Add(find);
            for (var i = 0; i < flags.Length; i++)
            {
                var bit = 1 << i;
                var toggle = new Toggle(OptionsFieldSchema.MaskNames[i]) { name = "options-mask-bit-" + bit };
                toggle.RegisterValueChangedCallback(e =>
                {
                    if (OptionsFieldSchema.TryMask(value, out var bits)) Write(new JValue(e.newValue ? bits | bit : bits & ~bit));
                });
                flags[i] = toggle; mask.Add(toggle);
            }
            var search = new CatalogSearch<string>(flag => new JObject { ["name"] = flag });
            var feedback = new Label();
            feedback.style.display = DisplayStyle.None;
            mask.Add(feedback);
            find.RegisterValueChangedCallback(e =>
            {
                var matches = new HashSet<string>(search.Filter(OptionsFieldSchema.MaskNames, e.newValue, out var error),
                    StringComparer.Ordinal);
                feedback.text = error ?? "";
                feedback.style.display = error == null ? DisplayStyle.None : DisplayStyle.Flex;
                for (var i = 0; i < flags.Length; i++) flags[i].style.display =
                    matches.Contains(OptionsFieldSchema.MaskNames[i]) ? DisplayStyle.Flex : DisplayStyle.None;
            });
            var actions = new VisualElement(); actions.style.flexDirection = FlexDirection.Row; actions.style.flexWrap = Wrap.Wrap;
            Action(actions, "全选已知位", () => ChangeKnownMask(true), "options-mask-all");
            Action(actions, "清除已知位", () => ChangeKnownMask(false), "options-mask-none");
            mask.Add(actions); Add(mask);
        }

        private void RefreshMask()
        {
            var valid = OptionsFieldSchema.TryMask(value, out var bits);
            mask.SetEnabled(valid && editingEnabled);
            maskSummary.text = valid ? "数值 " + bits + "；未知位 0x" + (bits & ~OptionsFieldSchema.KnownMask).ToString("X8") +
                (value!.Type == JTokenType.String ? "；使用位选项后改为数值表示。" : "") : "此值暂不可解析为已知位，原文保持不变。";
            for (var i = 0; i < flags.Length; i++) flags[i].SetValueWithoutNotify(valid && (bits & (1 << i)) != 0);
        }

        private void ChangeKnownMask(bool enabled)
        {
            if (OptionsFieldSchema.TryMask(value, out var bits))
                Write(new JValue(enabled ? bits | OptionsFieldSchema.KnownMask : bits & ~OptionsFieldSchema.KnownMask));
        }

        internal static Button Action(VisualElement parent, string text, Action action, string name)
        { var button = new Button(action) { text = text, name = name }; parent.Add(button); return button; }
    }
}
