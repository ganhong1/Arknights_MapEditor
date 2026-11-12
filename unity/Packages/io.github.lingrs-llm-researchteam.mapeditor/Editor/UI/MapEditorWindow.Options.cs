#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        private const string OptionsDraftSlot = "global:options";
        [SerializeField] private bool optionsRawView;
        [SerializeField] private bool optionsBlackboardExpanded;
        [SerializeField] private int optionsBlackboardSelection;

        private void BuildOptionsPanel(VisualElement parent)
        {
            var panel = new OptionsInspector(this);
            parent.Add(panel);
            refreshFieldSummaries.Add(panel.RefreshDocument);
        }

        private string ReadOptionsRaw() => session!.Document.GetSection("options")?.ToString(Formatting.Indented) ?? "";

        private void ApplyOptionsDraft()
        {
            RequireWorkspaceEditable();
            if (!InspectorStore.TryRead(OptionsDraftSlot, out var raw)) return;
            RequireLocalDraft(OptionsDraftSlot, "options");
            if (!InspectorStore.HasChanges(OptionsDraftSlot)) return;
            var before = OptionsFieldSchema.Parse(inspectorBindings[OptionsDraftSlot].Initial);
            var candidate = OptionsFieldSchema.Parse(raw);
            OptionsFieldSchema.ValidateChanges(before, candidate);
            var next = candidate == null ? session!.Document.RemoveSection("options") : session!.Document.WithSection("options", raw);
            CommitEdit(next, "编辑 options", null);
            AppliedLocalDraft(OptionsDraftSlot);
            Refresh();
        }

        private sealed class OptionsInspector : VisualElement
        {
            private readonly MapEditorWindow owner;
            private readonly Label summary = new Label();
            private readonly Label feedback = new Label();
            private readonly VisualElement editable = new VisualElement();
            private readonly VisualElement form = new VisualElement { name = "options-form" };
            private readonly TextField raw = new TextField("options 原始 JSON（空白表示未提供字段）")
                { name = "global-field-options", multiline = true };
            private readonly Dictionary<string, OptionsValueEditor> fields = new Dictionary<string, OptionsValueEditor>();
            private readonly OptionsBlackboardEditor blackboard;
            private readonly Button formButton;
            private readonly Button rawButton;
            private readonly Button create;
            private readonly Button remove;
            private readonly Button nullValue;
            private readonly Foldout extensions = new Foldout { text = "未识别成员（保留原值）", value = false };
            private readonly TextField extensionJson = new TextField { multiline = true, isReadOnly = true };
            private readonly Button apply;
            private JToken? token;
            private bool parsed;
            private string shownRaw = "", synchronizedDocument = "";

            internal OptionsInspector(MapEditorWindow owner)
            {
                this.owner = owner; name = "options-inspector";
                var title = new Label("options · LevelData.options"); title.AddToClassList("brush-title"); Add(title);
                summary.style.whiteSpace = feedback.style.whiteSpace = WhiteSpace.Normal;
                Add(summary); Add(feedback);
                var modes = Toolbar(this);
                formButton = OptionsValueEditor.Action(modes, "字段表单", () => ShowMode(false), "options-show-form");
                rawButton = OptionsValueEditor.Action(modes, "原始 JSON", () => ShowMode(true), "options-show-json");
                Add(editable);
                var actions = Toolbar(editable);
                create = OptionsValueEditor.Action(actions, "创建 options 对象", () => Change(new JObject()), "options-create");
                nullValue = OptionsValueEditor.Action(actions, "设为 null", () => Change(JValue.CreateNull()), "options-null");
                remove = OptionsValueEditor.Action(actions, "移除 options 字段", () => Change(null), "options-remove");
                foreach (var definition in OptionsFieldSchema.Fields.Where(item => item.Kind != OptionsValueKind.Blackboard))
                {
                    var field = new OptionsValueEditor(definition, value => WriteField(definition.Key, value));
                    field.AddToClassList("options-property");
                    fields.Add(definition.Key, field); form.Add(field);
                }
                blackboard = new OptionsBlackboardEditor(value => WriteField("configBlackBoard", value), owner.optionsBlackboardSelection,
                    selected => owner.optionsBlackboardSelection = selected);
                blackboard.AddToClassList("options-property");
                blackboard.SetValueWithoutNotify(owner.optionsBlackboardExpanded);
                blackboard.RegisterValueChangedCallback(e => { if (e.target == blackboard) owner.optionsBlackboardExpanded = e.newValue; });
                form.Add(blackboard);
                extensionJson.name = "options-extension-json"; extensions.Add(extensionJson); form.Add(extensions);
                editable.Add(form); raw.style.minHeight = 180; editable.Add(raw);
                raw.RegisterValueChangedCallback(e => ChangeRaw(e.newValue));
                var commit = Toolbar(editable);
                apply = OptionsValueEditor.Action(commit, "应用 options 修改", () => Try(owner.ApplyOptionsDraft), "options-apply");
                OptionsValueEditor.Action(commit, "放弃未应用修改", Reload, "options-discard");
                OptionsValueEditor.Action(commit, "重新读取 options", Reload, "options-reload");
                owner.BindLocalDraft(OptionsDraftSlot, owner.ReadOptionsRaw(), "options", Display);
                RefreshDocument();
                ShowMode(owner.optionsRawView || !parsed);
            }

            private static VisualElement Toolbar(VisualElement parent)
            { var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexWrap = Wrap.Wrap; parent.Add(row); return row; }

            internal void RefreshDocument()
            {
                if (synchronizedDocument != owner.session!.Json && !owner.InspectorStore.HasChanges(OptionsDraftSlot))
                {
                    owner.ReloadLocalDraft(OptionsDraftSlot, "options", owner.ReadOptionsRaw());
                    synchronizedDocument = owner.session.Json;
                }
                RefreshAccess();
                RefreshSummary();
            }

            private void RefreshSummary()
            {
                var current = owner.session!.Document.GetSection("options");
                summary.text = "已应用文档：" + (current == null ? "未提供 options" : current.Type == JTokenType.Null ? "options = null" : "options 类型 " + current.Type) +
                    (owner.InspectorStore.HasChanges(OptionsDraftSlot) ? "；有未应用修改" : "；无未应用修改");
                try { owner.InspectorStore.RequireCurrent(OptionsDraftSlot, owner.session.Json, "options"); }
                catch (InvalidOperationException)
                {
                    if (owner.InspectorStore.TryRead(OptionsDraftSlot, out _))
                        summary.text += "；草稿基线已过期，请保留草稿后显式重新读取";
                }
            }

            private void Display(string value)
            {
                shownRaw = value; raw.SetValueWithoutNotify(value);
                feedback.text = "";
                try { token = OptionsFieldSchema.Parse(value); parsed = true; }
                catch (Exception error) { token = null; parsed = false; feedback.text = "JSON 草稿已保留：" + error.Message; }
                var obj = token as JObject;
                foreach (var field in fields) field.Value.Display(obj?[field.Key]);
                blackboard.Display(obj?["configBlackBoard"]);
                var unknown = new JObject();
                if (obj != null)
                    foreach (var property in obj.Properties().Where(item => !OptionsFieldSchema.Fields.Any(field => field.Key == item.Name)))
                        unknown[property.Name] = property.Value.DeepClone();
                extensions.style.display = unknown.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
                extensionJson.SetValueWithoutNotify(unknown.ToString(Formatting.Indented));
                if (parsed && obj == null) feedback.text = token == null ? "未提供 options。创建对象只创建空对象，不自动补字段。" :
                    token.Type == JTokenType.Null ? "当前草稿为显式 null。" : "当前原值不是对象；使用原始 JSON 修正，字段表单暂不可编辑。";
                formButton.SetEnabled(parsed); RefreshAccess();
                RefreshSummary();
            }

            private void RefreshAccess()
            {
                var unlocked = !owner.WorkspaceEditLocked;
                var writable = unlocked && parsed && token is JObject;
                foreach (var field in fields.Values) field.SetEditingEnabled(writable);
                blackboard.SetEditingEnabled(writable);
                create.SetEnabled(unlocked && parsed && (token == null || token.Type == JTokenType.Null));
                remove.SetEnabled(unlocked && parsed && token != null);
                nullValue.SetEnabled(unlocked && parsed && token != null && token.Type != JTokenType.Null);
                raw.SetEnabled(unlocked); apply.SetEnabled(unlocked && parsed);
            }

            private void ShowMode(bool showRaw)
            {
                if (!showRaw && !parsed) return;
                owner.optionsRawView = showRaw;
                form.style.display = showRaw ? DisplayStyle.None : DisplayStyle.Flex;
                raw.style.display = showRaw ? DisplayStyle.Flex : DisplayStyle.None;
                formButton.EnableInClassList("selected-tab", !showRaw); rawButton.EnableInClassList("selected-tab", showRaw);
            }

            private void WriteField(string key, JToken? value)
            {
                owner.RequireWorkspaceEditable();
                if (!(token is JObject source)) throw new InvalidOperationException("请先创建 options 对象。");
                var candidate = (JObject)source.DeepClone();
                if (value == null) candidate.Remove(key); else candidate[key] = value.DeepClone();
                Change(candidate);
            }

            private void Change(JToken? value) => ChangeRaw(value?.ToString(Formatting.Indented) ?? "");

            private void ChangeRaw(string value)
            {
                try
                {
                    owner.RequireWorkspaceEditable();
                    owner.CaptureLocalDraft(OptionsDraftSlot, value);
                    // Capture can reject oversized input and roll the UI back to the stored draft.
                    if (owner.InspectorStore.TryRead(OptionsDraftSlot, out var stored)) Display(stored);
                }
                catch (Exception error) { Display(shownRaw); feedback.text = error.Message; }
            }

            private void Reload() => Try(() =>
            {
                owner.RequireWorkspaceEditable();
                if (owner.ReloadLocalDraft(OptionsDraftSlot, "options", owner.ReadOptionsRaw()))
                { synchronizedDocument = owner.session!.Json; RefreshSummary(); }
            });

            private void Try(Action action)
            {
                try { action(); RefreshDocument(); }
                catch (Exception error) { feedback.text = error.Message; }
            }
        }
    }
}
