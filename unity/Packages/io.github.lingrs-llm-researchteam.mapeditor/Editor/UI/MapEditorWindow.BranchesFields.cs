#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        private sealed partial class BranchesPanel
        {
            private void Save(string key, string initial, string raw, bool rawPending = false)
            {
                owner.RequireWorkspaceEditable(); owner.RequireAppliedDraft();
                var slot = BranchInspectorDrafts.Slot(key);
                if (owner.InspectorStore.HasChanges(slot) && !Current(key, out _))
                    throw new EditorOperationRejectedException("此位置存在旧版本草稿，请先处理待恢复草稿。");
                if (raw == initial) owner.InspectorStore.Remove(slot);
                else owner.InspectorStore.SetDraft(slot, owner.session!.Json, key, initial, raw, rawPending);
                owner.PersistInspectorDrafts();
            }
            private bool Current(string key, out string raw)
            {
                var slot = BranchInspectorDrafts.Slot(key);
                raw = "";
                return BranchInspectorDrafts.IsCurrent(owner.InspectorStore, slot, owner.session!.Json, key) &&
                    owner.InspectorStore.TryRead(slot, out raw);
            }
            private JObject DraftFields(string key, JObject fallback)
            {
                var slot = BranchInspectorDrafts.Slot(key);
                if (!owner.InspectorStore.HasChanges(slot)) return (JObject)fallback.DeepClone();
                owner.InspectorStore.RequireCurrent(slot, owner.session!.Json, key);
                if (owner.InspectorStore.HasPendingRaw(slot))
                    throw new EditorOperationRejectedException("请先读取原始动作 JSON。");
                return LevelDocument.ParseEnemyDefinition(Current(key, out var raw) ? raw : throw new FormatException("草稿未找到。"));
            }
            private void ResetButton(VisualElement parent, string key, string title, Action? onRebuild = null)
            {
                Button(parent, title, () =>
                {
                    if (!owner.Dialogs.Discard(title, "确定移除这份未应用草稿？")) return;
                    owner.InspectorStore.Remove(BranchInspectorDrafts.Slot(key));
                    owner.PersistInspectorDrafts(); Render(); onRebuild?.Invoke();
                });
            }
            private void InvalidRaw(VisualElement parent, string key, string initial, string raw, bool action = false)
            {
                parent.Add(Note("原始 JSON 有待处理的输入。"));
                var field = new TextField { multiline = true, value = raw, name = action ? "branch-action-json" : "" };
                field.RegisterValueChangedCallback(evt => Run(() => Save(key, initial, evt.newValue, true)));
                parent.Add(field);
                Button(parent, action ? "读取为待应用字段" : "读取为字段", () =>
                {
                    Save(key, initial, Json(LevelDocument.ParseEnemyDefinition(field.value)));
                    Render();
                });
                ResetButton(parent, key, "还原字段");
            }
            private void DrawPhaseForm(VisualElement parent, string branch, int phase, JObject record)
            {
                var key = BranchInspectorDrafts.Key(branch, phase);
                var initial = BranchInspectorDrafts.Fields(record, 1);
                JObject fields;
                try { fields = Current(key, out var raw) ? LevelDocument.ParseEnemyDefinition(raw) : initial; }
                catch (FormatException) { Current(key, out var invalid); InvalidRaw(parent, key, Json(initial), invalid); return; }
                var source = new TextField { multiline = true, value = fields.ToString(Formatting.Indented) };
                var delay = new TextField("preDelay · 阶段前延时（秒）") { isDelayed = true, name = "branch-phase-preDelay" };
                delay.SetValueWithoutNotify(fields["preDelay"]?.ToString(Formatting.None) ?? "");
                delay.RegisterValueChangedCallback(evt => Run(() =>
                {
                    var next = (JObject)fields.DeepClone();
                    try
                    {
                        var token = LevelDocument.ParseEnemyDefinition("{\"value\":" + evt.newValue + "}")["value"]!;
                        if (WaveSchedule.Time(token) == null) throw new FormatException("preDelay 需要非负有限秒数。");
                        next["preDelay"] = token;
                        Save(key, Json(initial), Json(next)); fields = next;
                        source.SetValueWithoutNotify(fields.ToString(Formatting.Indented));
                    }
                    catch { delay.SetValueWithoutNotify(evt.previousValue); throw; }
                }));
                parent.Add(delay);
                var advanced = new Foldout { text = "阶段字段 JSON", value = false };
                source.RegisterValueChangedCallback(evt => Run(() => { Save(key, Json(initial), evt.newValue); delay.SetEnabled(false); }));
                advanced.Add(source); parent.Add(advanced);
                Button(advanced, "读取为字段", () => { LevelDocument.ParseEnemyDefinition(source.value); Render(); });
                Button(parent, "应用阶段参数", () =>
                {
                    var next = BranchSchedule.ApplyFields(document, branch, new[] { phase }, DraftFields(key, fields), Array.Empty<string>());
                    Commit(next, "编辑分支阶段", new BranchHistoryStep(BranchHistoryKind.Fields, branch, phase), key);
                });
                ResetButton(parent, key, "还原阶段参数");
            }
            private void Recovered(VisualElement parent)
            {
                var slots = BranchInspectorDrafts.Saved(owner.InspectorStore).Where(slot =>
                {
                    var key = BranchInspectorDrafts.Target(owner.InspectorStore, slot);
                    return key == null || !BranchInspectorDrafts.IsCurrent(owner.InspectorStore, slot, owner.session!.Json, key);
                }).ToArray();
                if (slots.Length == 0) return;
                var fold = new Foldout { text = "待处理分支草稿 · " + slots.Length };
                fold.AddToClassList("wave-settings"); parent.Add(fold);
                foreach (var slot in slots)
                {
                    owner.InspectorStore.TryReadTarget(slot, out var key);
                    owner.InspectorStore.TryRead(slot, out var raw);
                    fold.Add(Note(key));
                    Button(fold, "复制草稿 JSON", () => EditorGUIUtility.systemCopyBuffer = raw);
                    Button(fold, "删除这份草稿", () =>
                    {
                        if (!owner.Dialogs.Discard("删除草稿", "确定删除这份分支草稿？")) return;
                        owner.InspectorStore.Remove(slot); owner.PersistInspectorDrafts(); Render();
                    });
                }
            }
            private void DrawReferences(VisualElement parent, string branch)
            {
                var references = BranchSchedule.References(document, branch);
                var fold = new Foldout { text = "调用与关联 · " + references.Count, value = false };
                fold.AddToClassList("wave-settings"); parent.Add(fold);
                if (references.Count == 0) fold.Add(Note("本关未找到已识别的调用记录。分支通过技能、环境或玩法入口触发。"));
                foreach (var item in references)
                {
                    fold.Add(Note(item.Kind + "\n" + item.Path +
                        (item.ActionIndex.HasValue ? "\naction_index · " + item.ActionIndex.Value : "")));
                    var token = document.Descendants().FirstOrDefault(value => value.Path == item.Path);
                    if (token != null) Button(fold, "定位调用字段", () => owner.LocateDocumentPath(TokenPointer(token)));
                }
            }
            private static string TokenPointer(JToken token)
            {
                var parts = new List<string>();
                for (JToken? current = token; current?.Parent != null; current = current.Parent)
                {
                    if (current.Parent is JProperty property) parts.Add(EscapePointer(property.Name));
                    else if (current.Parent is JArray array) parts.Add(array.IndexOf(current).ToString());
                }
                parts.Reverse(); return "/" + string.Join("/", parts);
            }
            private void Timeline(VisualElement parent, JObject phase, JArray actions)
            {
                var fold = new Foldout { text = "阶段配置时间 · 本次触发起点", value = false };
                fold.AddToClassList("wave-settings"); parent.Add(fold);
                var loaded = false;
                fold.RegisterValueChangedCallback(evt =>
                {
                    if (evt.target != fold || !evt.newValue || loaded) return;
                    loaded = true;
                    var rows = actions.OfType<JObject>().Select(action => (Action: action, Time: WaveSchedule.RelativeTime(phase["preDelay"], action)))
                        .Where(row => row.Time != null).ToArray();
                    var end = rows.Length == 0 ? 1 : Math.Max(.001, rows.Max(row => row.Time!.Value.Last));
                    foreach (var row in rows)
                    {
                        var time = row.Time!.Value;
                        var isSpawn = WaveSchedule.ActionType(row.Action["actionType"]) == "SPAWN";
                        var dontBlock = row.Action["dontBlockWave"]?.Type == JTokenType.Boolean && row.Action.Value<bool>("dontBlockWave");
                        var force = row.Action["forceBlockWaveInBranch"]?.Type == JTokenType.Boolean && row.Action.Value<bool>("forceBlockWaveInBranch");
                        fold.Add(Note($"动作 [{actions.IndexOf(row.Action)}] · {WaveSchedule.ActionType(row.Action["actionType"])} · {time.First:0.###}–{time.Last:0.###} s" +
                            (isSpawn ? (dontBlock || !force ? " · 不阻当前波次" : " · 请求阻当前波次") : "")));
                        var bar = new VisualElement(); bar.AddToClassList("wave-time-track");
                        var marker = new VisualElement(); marker.AddToClassList("wave-time-marker");
                        marker.style.left = Length.Percent((float)(96 * time.First / end));
                        marker.style.width = Length.Percent((float)(4 + 96 * (time.Last - time.First) / end));
                        bar.Add(marker); fold.Add(bar);
                    }
                    if (rows.Length == 0) fold.Add(Note("当前阶段没有可排入时间预览的动作。"));
                    fold.Add(Note("按阶段与动作配置计算相对时间。实际出生时间还受敌人资源和调用者影响。"));
                });
            }
            private LevelDocument? enemiesDocument;
            private List<BranchEnemyChoice> enemies = new();
            private IReadOnlyList<BranchEnemyChoice> Enemies()
            {
                if (ReferenceEquals(enemiesDocument, owner.WorkspaceDocument)) return enemies;
                var found = new List<BranchEnemyChoice>(); var keys = new HashSet<string>(StringComparer.Ordinal);
                EnemyCatalog? catalog = null;
                try { catalog = EnemyCatalog.LoadBundled(); }
                catch (Exception error) { message.text = PluginLog.FailureMessage("document.branches.enemy-catalog", error); }
                foreach (var section in new[] { "enemyDbRefs", "enemies" })
                {
                    if (document[section] is not JArray rows) continue;
                    for (var index = 0; index < rows.Count; index++)
                    {
                        if (rows[index] is not JObject item) continue;
                        string Text(string field) => item[field]?.Type == JTokenType.String ? item[field]!.Value<string>()! : "";
                        var key = section == "enemyDbRefs" ? Text("id") : Text("alias");
                        if (section == "enemies" && key.Length == 0) key = Text("key");
                        if (key.Length == 0 || keys.Contains(key)) continue;
                        try
                        {
                            JObject resolved;
                            if (section == "enemyDbRefs")
                            {
                                var entry = catalog?.Find(Text("id"));
                                var levels = entry == null ? null : catalog!.Details(entry)["databaseLevels"] as JArray;
                                resolved = EnemyDefinitionResolver.ResolveReference(item, levels);
                            }
                            else
                            {
                                if (Text("key").Length == 0 || item["alias"] != null && item["alias"]!.Type is not (JTokenType.Null or JTokenType.String)) continue;
                                resolved = item;
                            }
                            var name = resolved["name"]?.Type == JTokenType.String ? resolved["name"]!.Value<string>() : catalog?.Find(key)?.Name;
                            keys.Add(key); found.Add(new BranchEnemyChoice { Key = key, Label = (string.IsNullOrEmpty(name) ? key : name + " · " + key), Path = "/" + section + "/" + index });
                        }
                        catch (FormatException) { /* Unresolved definitions stay visible in their own editor. */ }
                        catch (InvalidOperationException) { /* A missing database level is not a selectable target. */ }
                    }
                }
                enemiesDocument = owner.WorkspaceDocument; enemies = found; return enemies;
            }
        }
    }
}
