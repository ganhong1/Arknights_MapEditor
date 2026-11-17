#nullable enable
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        private sealed partial class MusicPanel
        {
            private const string OpeningField = "bgmEvent";
            private const string OpeningSlot = "global:" + OpeningField;

            private void DrawOpening()
            {
                opening.Clear();
                opening.Add(Label("开局曲"));
                var document = owner.WorkspaceDocument;
                var original = document.GetSection(OpeningField);
                var initial = original?.ToString(Formatting.None) ?? "";
                var store = owner.InspectorStore;
                var raw = store.HasChanges(OpeningSlot) && store.TryRead(OpeningSlot, out var saved) ? saved : initial;
                var stale = IsOpeningStale(initial);
                var key = ReadOpeningKey(raw);
                var keyField = new TextField("音乐键") { name = "music-opening-key", isDelayed = true, value = key };
                opening.Add(keyField);
                var state = Label(OpeningState(raw, stale));
                opening.Add(state);
                Action<string>? choose = null;
                var picker = new MusicEventPicker(true, key, value => choose?.Invoke(value),
                    openLibrary: owner.OpenMusicResourceLibrary);
                opening.Add(picker);
                var advanced = new Foldout { text = "开局曲 JSON", value = false };
                var json = new TextField { name = "global-field-bgmEvent", value = raw, isReadOnly = stale, multiline = true };
                advanced.Add(json);
                opening.Add(advanced);
                var commands = new VisualElement();
                commands.AddToClassList("workbench-tabs");
                opening.Add(commands);
                var apply = AddCommand(commands, "应用开局曲", () =>
                {
                    owner.RequireWorkspaceEditable();
                    owner.RequireAppliedDraft();
                    if (IsOpeningStale(CurrentOpening())) throw new EditorOperationRejectedException("开局曲已改动，请先处理旧草稿。");
                    if (!owner.InspectorStore.HasChanges(OpeningSlot) || !owner.InspectorStore.TryRead(OpeningSlot, out var pending))
                        return;
                    var document = owner.WorkspaceDocument;
                    var next = pending.Length == 0 ? document.RemoveSection(OpeningField) : document.WithSection(OpeningField, pending);
                    if (next.GetSection(OpeningField) is { } value && value.Type is not (JTokenType.Null or JTokenType.String))
                        throw new FormatException("bgmEvent 需要字符串或 null。");
                    owner.CommitDocumentEdit(next, "应用开局曲", null);
                    owner.InspectorStore.Remove(OpeningSlot);
                    owner.PersistInspectorDrafts();
                    DrawOpening();
                }, "music-opening-apply");
                var reset = AddCommand(commands, "还原开局曲草稿", () =>
                {
                    if (!owner.Dialogs.Discard("还原开局曲草稿", "确定移除此草稿？")) return;
                    owner.InspectorStore.Remove(OpeningSlot);
                    owner.PersistInspectorDrafts();
                    DrawOpening();
                });
                void RefreshButtons()
                {
                    var dirty = owner.InspectorStore.HasChanges(OpeningSlot);
                    apply.SetEnabled(dirty && !stale);
                    reset.SetEnabled(dirty);
                }
                void Save(string value)
                {
                    owner.RequireWorkspaceEditable();
                    owner.RequireAppliedDraft();
                    var baseline = CurrentOpening();
                    if (initial != baseline || IsOpeningStale(baseline))
                        throw new EditorOperationRejectedException("开局曲已改动，请先处理旧草稿。");
                    if (string.Equals(value, baseline, StringComparison.Ordinal)) owner.InspectorStore.Remove(OpeningSlot);
                    else owner.InspectorStore.SetDraft(OpeningSlot, owner.session!.Json, OpeningField, baseline, value);
                    owner.PersistInspectorDrafts();
                    picker.SetKey(ReadOpeningKey(value));
                    state.text = OpeningState(value, false);
                    json.SetValueWithoutNotify(value);
                    RefreshButtons();
                }
                choose = value =>
                {
                    try
                    {
                        Save(JsonConvert.ToString(value));
                        keyField.SetValueWithoutNotify(value);
                    }
                    catch (Exception error) { message.text = PluginLog.FailureMessage("document.music", error); }
                };
                AddCommand(commands, "设为 null", () => { Save("null"); keyField.SetValueWithoutNotify(""); });
                AddCommand(commands, "移除字段", () => { Save(""); keyField.SetValueWithoutNotify(""); });
                keyField.SetEnabled(!stale);
                keyField.RegisterValueChangedCallback(evt =>
                {
                    try { Save(JsonConvert.ToString(evt.newValue)); }
                    catch (Exception error)
                    {
                        keyField.SetValueWithoutNotify(evt.previousValue);
                        message.text = PluginLog.FailureMessage("document.music", error);
                    }
                });
                json.RegisterValueChangedCallback(evt =>
                {
                    try
                    {
                        Save(evt.newValue);
                        keyField.SetValueWithoutNotify(ReadOpeningKey(evt.newValue));
                    }
                    catch (Exception error)
                    {
                        json.SetValueWithoutNotify(evt.previousValue);
                        message.text = PluginLog.FailureMessage("document.music", error);
                    }
                });
                RefreshButtons();
            }

            private string CurrentOpening() => owner.WorkspaceDocument.GetSection(OpeningField)?.ToString(Formatting.None) ?? "";

            private bool IsOpeningStale(string initial)
            {
                var store = owner.InspectorStore;
                if (!store.HasChanges(OpeningSlot)) return false;
                if (!store.TryReadTarget(OpeningSlot, out var target) || target != OpeningField) return true;
                return !store.IsCurrent(OpeningSlot, owner.session!.Json, OpeningField) &&
                    (!store.TryReadInitial(OpeningSlot, out var previous) ||
                        !string.Equals(previous, initial, StringComparison.Ordinal));
            }

            private static string ReadOpeningKey(string raw)
            {
                if (raw.Length == 0) return "";
                try
                {
                    using var input = new System.IO.StringReader(raw);
                    using var reader = new JsonTextReader(input) { DateParseHandling = DateParseHandling.None };
                    return JToken.ReadFrom(reader) is JValue { Type: JTokenType.String } value ? value.Value<string>()! : "";
                }
                catch (JsonException) { return ""; }
            }

            private static string OpeningState(string raw, bool stale)
            {
                if (stale) return "开局曲有一份旧草稿，可在 JSON 中查看。";
                if (raw.Length == 0) return "未提供 bgmEvent";
                if (raw == "null") return "bgmEvent 为 null";
                if (raw == "\"\"") return "bgmEvent 为空字符串";
                return ReadOpeningKey(raw).Length == 0 ? "草稿原文需要修正为字符串或 null" : "开局事件 · ON_GAME_READY";
            }
        }
    }
}
