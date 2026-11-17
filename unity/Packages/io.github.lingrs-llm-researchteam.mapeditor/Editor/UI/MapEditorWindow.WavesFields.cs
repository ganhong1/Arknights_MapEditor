#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        private sealed partial class WavesPanel
        {
            private enum FieldKind { Text, Bool, Count, Time, Wait, Raw }

            private sealed class Form
            {
                internal readonly string Key, Initial;
                internal readonly int[] Address;
                internal readonly bool IsNew;
                internal JObject Fields;
                internal Action? Changed;
                internal Action? DraftChanged;
                internal bool RawPending;

                internal Form(string key, int[] address, string initial, JObject fields, bool isNew)
                {
                    Key = key; Address = address; Initial = initial; Fields = fields; IsNew = isNew;
                }
            }

            private Form? CreateForm(string key, JObject record, int[] address, bool isNew)
            {
                var initial = WaveSchedule.Fields(record, address.Length);
                var candidate = initial;
                if (TryDraft(key, out var text))
                {
                    try { candidate = ParseFields(text); }
                    catch (FormatException) { return null; }
                }
                return new Form(key, address, isNew ? "" : Json(initial), candidate, isNew);
            }

            private static JObject ParseFields(string json) =>
                LevelDocument.Create(1, 1).WithSection("fields", json).GetSection("fields") as JObject ??
                throw new FormatException("当前记录必须是一个 JSON 对象。");

            private void DrawAction(VisualElement target, int[] address, string key, JObject record, bool isNew,
                bool musicOnly = false, Action? onRebuild = null, Action? onApplied = null, Action? onDraftChanged = null)
            {
                var form = CreateForm(key, record, address, isNew);
                if (form == null || owner.InspectorStore.HasPendingRaw(WaveInspectorDrafts.Slot(key)))
                {
                    if (TryDraft(key, out var raw))
                    {
                        var initial = isNew ? "" : Json(record);
                        target.Add(Note("原始动作 JSON 有待处理的输入。"));
                        if (!musicOnly) DrawRaw(target, new Form(key, address, initial, record, isNew), raw);
                        else Command(target, "前往波次处理", () => owner.OpenMusicActionInSchedule(
                            new MusicActionAddress(null, address[0], address[1], isNew ? -1 : address[2])));
                    }
                    return;
                }
                var choices = ReadEnemies().Select(item => new BranchEnemyChoice
                {
                    Key = item.Key, Label = item.Label,
                    Path = "/" + item.Section + "/" + item.Index
                }).ToArray();
                var location = new MusicActionAddress(null, address[0], address[1], isNew ? -1 : address[2]);
                if (musicOnly && WaveSchedule.ActionType(form.Fields["actionType"]) != "PLAY_BGM")
                {
                    target.Add(Note("这份动作草稿当前使用其他动作类型。"));
                    Command(target, "前往波次编辑草稿", () => owner.OpenMusicActionInSchedule(location));
                    return;
                }
                var siblings = (WaveSchedule.Read(waves, address.Take(2).ToArray()) as JObject)?["actions"] as JArray;
                var shared = new ActionDataForm(form.Fields, LevelDocument.ParseEnemyDefinition(owner.session!.Json), choices,
                    next => { SaveFields(form, next); form.RawPending = false; form.DraftChanged?.Invoke(); }, owner.LocateDocumentPath,
                    (library, route, play) => owner.ShowWaveRoute(route, play, library), false,
                    raw => { SaveRaw(form, raw, true); form.RawPending = true; form.DraftChanged?.Invoke(); },
                    owner.showAllWaveFields, all => owner.showAllWaveFields = all, musicOnly ? "music" : "waves",
                    siblings, address.Last(), musicOnly: musicOnly,
                    locateMusic: musicOnly ? null : () => owner.OpenActionInMusic(location),
                    previousMusic: () => owner.PreviousMusic(siblings, address.Last(), form.Fields),
                    openMusicLibrary: owner.OpenMusicResourceLibrary);
                target.Add(shared);
                var timing = Note("");
                target.Add(timing);
                void ShowTiming() => timing.text = Timing(
                    (WaveSchedule.Read(waves, address.Take(2).ToArray()) as JObject)?["preDelay"], form.Fields);
                form.Changed += ShowTiming;
                form.DraftChanged += onDraftChanged;
                ShowTiming();
                DrawApply(target, form, onRebuild, onApplied);
            }

            private void DrawField(VisualElement target, Form form, string key, FieldKind kind)
            {
                var card = new VisualElement { name = "waves-field-" + key };
                card.AddToClassList("wave-field");
                target.Add(card);
                var value = form.Fields[key];
                var caption = Note(key + " · " + Raw(value));
                card.Add(caption);
                if (kind == FieldKind.Raw) return;

                void Update(JToken? candidate)
                {
                    SetValue(form, key, candidate);
                    caption.text = key + " · " + Raw(form.Fields[key]);
                }

                if (kind == FieldKind.Bool)
                {
                    var select = new PopupField<string>(new List<string> { "true", "false", "未提供" },
                        value?.Type == JTokenType.Boolean ? ((bool)value ? 0 : 1) : 2);
                    select.name = "waves-input-" + key;
                    select.RegisterValueChangedCallback(change => Run(() =>
                        Update(change.newValue == "未提供" ? null : new JValue(change.newValue == "true"))));
                    card.Add(select);
                    return;
                }

                var field = new TextField { isDelayed = true, name = "waves-input-" + key };
                field.SetValueWithoutNotify(kind == FieldKind.Text
                    ? (value?.Type == JTokenType.String ? value.Value<string>()! : "")
                    : value != null && value.Type != JTokenType.Null ? Raw(value) : "");
                field.RegisterValueChangedCallback(change => Run(() =>
                {
                    try { Update(ParseValue(key, kind, change.newValue)); }
                    catch
                    {
                        field.SetValueWithoutNotify(change.previousValue);
                        throw;
                    }
                }));
                card.Add(field);
                var buttons = Toolbar(card);
                if (kind == FieldKind.Text)
                    Command(buttons, "设为 null", () => { Update(JValue.CreateNull()); field.SetValueWithoutNotify(""); });
                if (kind == FieldKind.Text)
                    Command(buttons, "移除字段", () => { Update(null); field.SetValueWithoutNotify(""); });
            }

            private static JToken ParseValue(string key, FieldKind type, string value)
            {
                if (type == FieldKind.Text) return new JValue(value);
                if (type == FieldKind.Count)
                {
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) &&
                        integer >= 1) return new JValue(integer);
                    throw new FormatException(key + " 需要大于 0 的整数。");
                }
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
                    !float.IsNaN(number) && !float.IsInfinity(number) &&
                    number >= (type == FieldKind.Wait ? -1 : 0)) return new JValue(number);
                throw new FormatException(key + (type == FieldKind.Wait ? " 需要大于或等于 -1 的有限秒数。" : " 需要非负有限秒数。"));
            }

            private void SetValue(Form form, string key, JToken? value)
            {
                if (form.RawPending) throw new FormatException("请先将原始动作 JSON 读取为待应用字段。");
                var candidate = (JObject)form.Fields.DeepClone();
                if (value == null) candidate.Remove(key);
                else candidate[key] = value;
                SaveFields(form, candidate);
            }

            private void SaveFields(Form form, JObject candidate)
            {
                SaveRaw(form, Json(candidate));
                form.Fields = candidate;
                form.Changed?.Invoke();
                form.DraftChanged?.Invoke();
            }

            private void SaveRaw(Form form, string raw, bool rawPending = false)
            {
                owner.RequireWorkspaceEditable();
                owner.RequireAppliedDraft();
                var slot = WaveInspectorDrafts.Slot(form.Key);
                if (owner.InspectorStore.HasChanges(slot) && !TryDraft(form.Key, out _))
                    throw new EditorOperationRejectedException("这个位置有旧版本草稿，请先在待处理列表中处理。");
                if (raw == form.Initial) owner.InspectorStore.Remove(slot);
                else owner.InspectorStore.SetDraft(slot, owner.session!.Json, form.Key, form.Initial, raw, rawPending);
                Persist();
            }

            private void DrawApply(VisualElement parent, Form form, Action? onRebuild = null, Action? onApplied = null)
            {
                var row = Toolbar(parent);
                var slot = WaveInspectorDrafts.Slot(form.Key);
                var apply = Command(row, form.IsNew ? "创建动作" : "应用字段", () => { Apply(form); onApplied?.Invoke(); });
                var reset = Command(row, form.IsNew ? "取消新增" : "还原为已应用值", () =>
                {
                    if (!owner.Dialogs.Discard("还原波次字段", "确定移除这份未应用草稿？")) return;
                    owner.InspectorStore.Remove(slot);
                    Persist(); Render(); onRebuild?.Invoke();
                });
                void RefreshControls()
                {
                    var dirty = TryDraft(form.Key, out _);
                    var validNew = true;
                    if (form.IsNew)
                    {
                        try { ActionDataRules.Require(LevelDocument.ParseEnemyDefinition(owner.session!.Json), form.Fields, null,
                            ReadEnemies().Select(choice => choice.Key).ToArray(), false); }
                        catch (FormatException) { validNew = false; }
                    }
                    apply.SetEnabled(dirty && !form.RawPending && validNew);
                    reset.SetEnabled(dirty);
                }
                form.DraftChanged += RefreshControls;
                RefreshControls();
                if (form.IsNew) parent.Add(Note("选择动作类型并填写对应目标后创建。"));
            }

            private void Apply(Form form)
            {
                owner.RequireWorkspaceEditable();
                owner.RequireAppliedDraft();
                var slot = WaveInspectorDrafts.Slot(form.Key);
                owner.InspectorStore.RequireCurrent(slot, owner.session!.Json, form.Key);
                if (owner.InspectorStore.HasPendingRaw(slot))
                    throw new EditorOperationRejectedException("请先读取原始动作 JSON。");
                if (!TryDraft(form.Key, out var raw)) throw new InvalidOperationException("找不到当前草稿。");
                form.Fields = ParseFields(raw);
                var candidate = waves?.DeepClone();
                if (form.IsNew)
                {
                    ActionDataRules.Require(LevelDocument.ParseEnemyDefinition(owner.session!.Json), form.Fields, null, ReadEnemies().Select(choice => choice.Key).ToArray(), false);
                    var parent = form.Address.Take(2).ToArray();
                    WaveSchedule.GetOrCreateChildren(ref candidate, parent).Add(form.Fields.DeepClone());
                }
                else
                {
                    var record = WaveSchedule.Read(candidate, form.Address) as JObject ??
                        throw new FormatException("找不到 " + WaveSchedule.Path(form.Address));
                    if (form.Address.Length < 3) WaveSchedule.RequireWaveFields(form.Fields, record, form.Address.Length);
                    else ActionDataRules.Require(LevelDocument.ParseEnemyDefinition(owner.session!.Json), form.Fields, record,
                        ReadEnemies().Select(choice => choice.Key).ToArray(), false);
                    WaveSchedule.ApplyFields(record, form.Fields, form.Address.Length);
                }
                var step = new WaveHistoryStep(form.IsNew ? WaveOrder.Append : WaveOrder.Fields,
                    form.Address.Take(form.Address.Length - 1).ToArray(), form.Address.Last());
                Commit(candidate!, form.IsNew ? "新增动作" : "应用 " + WaveSchedule.Path(form.Address), step, form.Key);
                Expand(form.Address);
                Refresh(true);
            }

            private void DrawRaw(VisualElement target, Form form, string? invalid = null)
            {
                var fold = new Foldout { text = "高级 · 原始动作 JSON", value = invalid != null };
                var input = new TextField { name = "waves-action-json", multiline = true };
                input.SetValueWithoutNotify(invalid ?? form.Fields.ToString(Formatting.Indented));
                form.Changed += () =>
                {
                    form.RawPending = false;
                    input.SetValueWithoutNotify(form.Fields.ToString(Formatting.Indented));
                };
                input.RegisterValueChangedCallback(change => Run(() =>
                {
                    try
                    {
                        SaveRaw(form, change.newValue, true);
                        form.RawPending = true;
                        form.DraftChanged?.Invoke();
                    }
                    catch
                    {
                        input.SetValueWithoutNotify(change.previousValue);
                        throw;
                    }
                }));
                fold.Add(input);
                Command(fold, "读取为待应用字段", () =>
                {
                    SaveFields(form, ParseFields(input.value));
                    Render();
                });
                if (invalid != null)
                    Command(fold, "还原为已应用值", () =>
                    {
                        if (!owner.Dialogs.Discard("还原原始动作", "确定移除这份未应用草稿？")) return;
                        owner.InspectorStore.Remove(WaveInspectorDrafts.Slot(form.Key));
                        Persist(); Render();
                    });
                target.Add(fold);
            }

            private readonly struct EnemyChoice
            {
                internal readonly string Key, Label, Section;
                internal readonly int Index;
                internal EnemyChoice(string key, string name, string section, int index)
                {
                    Key = key; Section = section; Index = index;
                    Label = (name.Length == 0 || name == key ? key : name + " · " + key) + $" · {section}[{index}]";
                }
            }

            private LevelDocument? enemyChoicesDocument;
            private List<EnemyChoice> enemyChoices = new();

            private List<EnemyChoice> ReadEnemies()
            {
                var document = owner.WorkspaceDocument;
                if (ReferenceEquals(enemyChoicesDocument, document)) return enemyChoices;
                var found = new List<EnemyChoice>();
                var keys = new HashSet<string>(StringComparer.Ordinal);
                EnemyCatalog? catalog = null;
                try { catalog = EnemyCatalog.LoadBundled(); }
                catch (Exception error) when (error is InvalidOperationException || error is FormatException ||
                    error is System.IO.IOException || error is JsonException)
                {
                    if (message.text.Length == 0) message.text = "敌人目录 · " + error.Message;
                }
                foreach (var section in new[] { "enemyDbRefs", "enemies" })
                {
                    if (document.GetSection(section) is not JArray entries) continue;
                    for (var index = 0; index < entries.Count; index++)
                    {
                        if (entries[index] is not JObject item) continue;
                        var fromDatabase = section == "enemyDbRefs";
                        var key = fromDatabase ? WaveKey(item, "id") : WaveKey(item, "alias");
                        if (!fromDatabase && key.Length == 0) key = WaveKey(item);
                        if (key.Length == 0 || keys.Contains(key)) continue;
                        var resolved = ResolveWaveEnemy(item, fromDatabase, catalog);
                        if (resolved == null) continue;
                        keys.Add(key);
                        var name = WaveKey(resolved, "name");
                        if (name.Length == 0) name = catalog?.Find(WaveKey(resolved))?.Name ?? catalog?.Find(key)?.Name ?? "";
                        found.Add(new EnemyChoice(key, name, section, index));
                    }
                }
                enemyChoicesDocument = document;
                enemyChoices = found;
                return enemyChoices;
            }

            private JObject? ResolveWaveEnemy(JObject item, bool reference, EnemyCatalog? catalog)
            {
                if (!reference)
                    return !string.IsNullOrWhiteSpace(WaveKey(item)) &&
                        (item["alias"] == null || item["alias"]!.Type is JTokenType.String or JTokenType.Null)
                        ? item : null;
                try
                {
                    var entry = item["useDb"]?.Type == JTokenType.Boolean && item.Value<bool>("useDb")
                        ? catalog?.Find(WaveKey(item, "id")) : null;
                    var levels = entry == null ? null : catalog!.Details(entry)["databaseLevels"] as JArray;
                    var resolved = EnemyDefinitionResolver.ResolveReference(item, levels);
                    return string.IsNullOrWhiteSpace(WaveKey(resolved)) ? null : resolved;
                }
                catch (Exception error) when (error is FormatException || error is InvalidOperationException)
                {
                    return null;
                }
                catch (Exception error) when (error is System.IO.IOException || error is JsonException)
                {
                    message.text = PluginLog.FailureMessage("document.waves.enemy-source", error);
                    return null;
                }
            }

            private static string WaveKey(JObject record, string field = "key") =>
                record[field]?.Type == JTokenType.String ? record[field]!.Value<string>()! : "";

            private static string Timing(JToken? delay, JObject action)
            {
                var relative = WaveSchedule.RelativeTime(delay, action);
                var block = action["blockFragment"]?.Type == JTokenType.Boolean && action.Value<bool>("blockFragment");
                var leave = action["dontBlockWave"]?.Type == JTokenType.Boolean && action.Value<bool>("dontBlockWave");
                string Seconds(double value) => value.ToString("0.###", CultureInfo.InvariantCulture) + " s";
                var timing = relative is { } range ? Seconds(range.First) +
                    (range.Count > 1 ? " → " + Seconds(range.Last) + " · " + range.Count + " 次" : "") : "无配置时间";
                return timing + (block ? " · 等待动作回调" : "") +
                    (WaveSchedule.ActionType(action["actionType"]) == "SPAWN" && leave ? " · 不阻塞波次" : "");
            }

            private void DrawTimeline(VisualElement target, JToken? delay, JArray actions)
            {
                var fold = new Foldout { text = "片段配置时间 · 相对起点", value = false };
                fold.AddToClassList("wave-settings");
                var loaded = false;
                fold.RegisterValueChangedCallback(evt =>
                {
                    if (evt.target != fold || !evt.newValue || loaded) return;
                    loaded = true;
                    var entries = actions.OfType<JObject>().Select(action => (Action: action,
                        Time: WaveSchedule.RelativeTime(delay, action))).Where(entry => entry.Time != null).ToList();
                    double end = entries.Count == 0 ? 1d : Math.Max(.001d, entries.Max(entry => entry.Time!.Value.Last));
                    var view = new VisualElement();
                    fold.Add(view);
                    void Page(int from)
                    {
                        for (var i = from; i < Math.Min(from + PageSize, entries.Count); i++)
                        {
                            var entry = entries[i];
                            var range = entry.Time!.Value;
                            view.Add(Note($"[{actions.IndexOf(entry.Action)}] {WaveSchedule.ActionType(entry.Action["actionType"])} · {Timing(delay, entry.Action)}"));
                            var bar = new VisualElement();
                            bar.AddToClassList("wave-time-track");
                            var marker = new VisualElement();
                            marker.AddToClassList("wave-time-marker");
                            marker.style.left = Length.Percent((float)(96 * range.First / end));
                            marker.style.width = Length.Percent((float)(4 + 96 * (range.Last - range.First) / end));
                            bar.Add(marker);
                            view.Add(bar);
                        }
                        if (from + PageSize < entries.Count)
                        {
                            Button? more = null;
                            more = Command(view, "显示后续 40 项", () => { more!.RemoveFromHierarchy(); Page(from + PageSize); });
                        }
                    }
                    if (entries.Count == 0) view.Add(Note("当前片段没有可排入预览的动作。"));
                    else Page(0);
                });
                target.Add(fold);
            }
        }
    }
}
