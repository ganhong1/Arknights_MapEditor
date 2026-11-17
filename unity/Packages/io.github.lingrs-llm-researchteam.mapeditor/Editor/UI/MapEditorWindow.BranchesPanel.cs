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
        private sealed partial class BranchesPanel : VisualElement
        {
            private readonly MapEditorWindow owner;
            private readonly VisualElement tree = new();
            private readonly Label message = new();
            private readonly TextField searchField = new() { label = "搜索分支", name = "branches-search" };
            private readonly TextField newKey = new() { label = "新分支键", name = "branches-new-key" };
            private readonly CatalogSearchInput<SearchEntry> search;
            private JObject document = new();
            private LevelDocument? displayed;
            private HashSet<string>? matches;
            private readonly Dictionary<string, int> limits = new();
            private sealed class SearchEntry { internal string Key = "", Text = ""; }

            internal BranchesPanel(MapEditorWindow owner)
            {
                this.owner = owner;
                name = "branches-editor";
                Add(Note("分支 · branches"));
                var tools = Bar(this);
                Button(tools, "定位 branches", () => owner.LocateDocumentPath("/branches"));
                Button(tools, "检查引用", () =>
                {
                    var issues = BranchSchedule.Validate(document, Enemies().Select(item => item.Key).ToArray());
                    message.text = issues.Count == 0 ? "分支字段与已知引用检查通过。" : string.Join("\n", issues);
                });
                Add(searchField); Add(newKey);
                searchField.RegisterValueChangedCallback(_ => { revealedMusicBranch = null; revealedMusicTarget = null; });
                Button(this, "新建分支", () =>
                {
                    var key = newKey.value;
                    Commit(BranchSchedule.AddBranch(document, key), "新增分支", new BranchHistoryStep(BranchHistoryKind.Create, key));
                    Open(key); newKey.SetValueWithoutNotify(""); Refresh(true);
                });
                message.style.whiteSpace = WhiteSpace.Normal;
                Add(message); Add(tree);
                search = new CatalogSearchInput<SearchEntry>(searchField,
                    () => CatalogSearchRuntime.CreateWorker<SearchEntry>(row => new JObject
                    { ["code"] = row.Key, ["name"] = row.Key, ["description"] = row.Text }),
                    Array.Empty<SearchEntry>(), () => message.text = "正在搜索…", (rows, error) =>
                    {
                        matches = string.IsNullOrWhiteSpace(searchField.value) || error != null ? null :
                            new HashSet<string>(rows.Select(row => row.Key), StringComparer.Ordinal);
                        message.text = error ?? ""; Render();
                    });
            }

            internal void Refresh(bool force = false)
            {
                if (!force && ReferenceEquals(displayed, owner.WorkspaceDocument)) return;
                displayed = owner.WorkspaceDocument;
                document = ReadBranchDocument(displayed);
                enemiesDocument = null;
                search.SetItems((document["branches"] as JObject)?.Properties().Select(item =>
                    new SearchEntry { Key = item.Name, Text = item.Value.ToString(Formatting.None) }).ToArray() ?? Array.Empty<SearchEntry>());
                Render();
            }

            private void Render()
            {
                tree.Clear();
                Recovered(tree);
                if (document["branches"] == null || document["branches"]!.Type == JTokenType.Null)
                { tree.Add(Note("尚无分支。输入分支键后新建。")); return; }
                if (document["branches"] is not JObject branches)
                { tree.Add(Note("branches 必须是对象。可在中央原始 JSON 中修正。")); return; }
                foreach (var item in branches.Properties())
                {
                    if (matches != null && !matches.Contains(item.Name) && item.Name != revealedMusicBranch) continue;
                    DrawBranch(item.Name, item.Value);
                }
            }

            private void DrawBranch(string key, JToken value)
            {
                var fold = Card(tree, key, Array.Empty<int>(), key + " · " + (((value as JObject)?["phases"] as JArray)?.Count.ToString() ?? "—") + " 个阶段");
                Populate(fold, () =>
                {
                    if (value is not JObject branch) { fold.Add(Note("分支必须是对象。")); return; }
                    var tools = Bar(fold);
                    var target = new TextField("目标分支键") { name = "branches-target-key" };
                    fold.Add(target);
                    Button(tools, "复制", () =>
                    {
                        Commit(BranchSchedule.AddBranch(document, target.value, key), "复制分支",
                            new BranchHistoryStep(BranchHistoryKind.Create, target.value));
                        Open(target.value); Refresh(true);
                    });
                    Button(tools, "重命名", () =>
                    {
                        var next = BranchSchedule.RenameBranch(document, key, target.value);
                        if (!owner.Dialogs.Discard("重命名分支", "确定重命名并同步已解析的本关调用字段？")) return;
                        Commit(next, "重命名分支", new BranchHistoryStep(BranchHistoryKind.Rename, key, newBranch: target.value));
                    });
                    Button(tools, "删除", () =>
                    {
                        var next = BranchSchedule.DeleteBranch(document, key);
                        if (ConfirmDelete()) Commit(next, "删除分支", new BranchHistoryStep(BranchHistoryKind.DeleteBranch, key));
                    });
                    Button(tools, "字段树", () => owner.LocateDocumentPath(Pointer(key)));
                    DrawReferences(fold, key);
                    if (branch["phases"] is not JArray phases) { fold.Add(Note("phases 必须是数组。")); return; }
                    var children = Section(fold, "phases · 阶段");
                    Button(children, "新增阶段", () =>
                    {
                        Commit(BranchSchedule.ChangeNode(document, key, null, phases.Count, BranchEdit.Insert), "新增分支阶段",
                            new BranchHistoryStep(BranchHistoryKind.Insert, key, index: phases.Count));
                        Open(key, phases.Count); Refresh(true);
                    });
                    Page(children, key, phases.Count, index => DrawPhase(children, key, index, phases));
                });
            }

            private void DrawPhase(VisualElement parent, string key, int index, JArray phases)
            {
                var value = phases[index];
                var record = value as JObject;
                var fold = Card(parent, key, new[] { index }, $"阶段 [{index}] · {(record?["actions"] as JArray)?.Count.ToString() ?? "—"} 条动作");
                Populate(fold, () =>
                {
                    NodeTools(fold, key, null, index, phases.Count);
                    if (record == null) { fold.Add(Note("阶段必须是对象。")); return; }
                    var settings = Section(fold, "阶段参数");
                    DrawPhaseForm(settings, key, index, record);
                    if (record["actions"] is not JArray actions) { fold.Add(Note("actions 必须是数组。")); return; }
                    var children = Section(fold, "actions · 动作");
                    Button(children, "新增动作", () =>
                    {
                        var pending = BranchInspectorDrafts.Key(key, index, int.MaxValue);
                        var slot = BranchInspectorDrafts.Slot(pending);
                        if (!owner.InspectorStore.HasChanges(slot))
                            Save(pending, "", Json(BranchSchedule.NewAction()));
                        Open(key, index); Refresh(true);
                    });
                    Page(children, BranchInspectorDrafts.Key(key, index), actions.Count,
                        action => DrawAction(children, key, index, action, actions[action], actions.Count));
                    var pendingKey = BranchInspectorDrafts.Key(key, index, int.MaxValue);
                    if (Current(pendingKey, out _)) DrawAction(children, key, index, int.MaxValue, BranchSchedule.NewAction(), actions.Count);
                    Timeline(fold, record, actions);
                });
            }

            private void DrawAction(VisualElement parent, string key, int phase, int index, JToken value, int count)
            {
                var pending = index == int.MaxValue;
                var record = value as JObject;
                var title = pending ? "新增动作草稿" : $"动作 [{index}] · {WaveSchedule.ActionType(record?["actionType"])} · {record?["key"]?.ToString(Formatting.None)}";
                var fold = Card(parent, key, new[] { phase, index }, title, pending);
                Populate(fold, () =>
                {
                    if (!pending) NodeTools(fold, key, phase, index, count);
                    if (record == null) { fold.Add(Note("动作必须是对象。")); return; }
                    DrawActionFields(fold, key, phase, index, record, count);
                });
            }

            private void DrawActionFields(VisualElement fold, string key, int phase, int index, JObject record, int count,
                bool musicOnly = false, Action? onRebuild = null, Action? onApplied = null, Action? onDraftChanged = null)
            {
                var pending = index == int.MaxValue;
                var address = BranchInspectorDrafts.Key(key, phase, index);
                var initial = pending ? "" : Json(record);
                var raw = Current(address, out var saved) ? saved : Json(record);
                var location = new MusicActionAddress(key, -1, phase, pending ? -1 : index);
                void ShowRaw()
                {
                    if (!musicOnly) InvalidRaw(fold, address, initial, raw, action: true);
                    else
                    {
                        fold.Add(Note("原始动作 JSON 有待处理的输入。"));
                        Button(fold, "前往分支处理", () => owner.OpenMusicActionInSchedule(location));
                    }
                }
                if (owner.InspectorStore.HasPendingRaw(BranchInspectorDrafts.Slot(address))) { ShowRaw(); return; }
                JObject fields;
                try { fields = LevelDocument.ParseEnemyDefinition(raw); }
                catch (FormatException)
                {
                    ShowRaw(); return;
                }
                if (musicOnly && WaveSchedule.ActionType(fields["actionType"]) != "PLAY_BGM")
                {
                    fold.Add(Note("这份动作草稿当前使用其他动作类型。"));
                    Button(fold, "前往分支编辑草稿", () => owner.OpenMusicActionInSchedule(location));
                    return;
                }
                var siblings = document["branches"]?[key]?["phases"]?[phase]?["actions"] as JArray;
                var actionIndex = pending ? count : index;
                var form = new ActionDataForm(fields, document, Enemies(),
                    next =>
                    {
                        Save(address, initial, Json(next));
                        onDraftChanged?.Invoke();
                    }, owner.LocateDocumentPath,
                    (library, route, play) => owner.ShowWaveRoute(route, play, library), true,
                    text => Save(address, initial, text, true), prefix: musicOnly ? "music" : "branch",
                    siblingActions: siblings, actionIndex: actionIndex, musicOnly: musicOnly,
                    locateMusic: musicOnly ? null : () => owner.OpenActionInMusic(location),
                    previousMusic: () => owner.PreviousMusic(siblings, actionIndex, DraftFields(address, fields)),
                    openMusicLibrary: owner.OpenMusicResourceLibrary);
                fold.Add(form);
                var tools = Bar(fold);
                Button(tools, pending ? "创建动作" : "应用动作", () =>
                {
                    if (form.HasPendingRaw) throw new EditorOperationRejectedException("请先读取原始 JSON 为待应用字段。");
                    var nextFields = DraftFields(address, fields);
                    JObject next;
                    BranchHistoryStep step;
                    if (pending)
                    {
                        BranchSchedule.RequireNewAction(document, nextFields, Enemies().Select(item => item.Key).ToArray());
                        next = BranchSchedule.ChangeNode(document, key, phase, count, BranchEdit.Insert, value: nextFields);
                        step = new BranchHistoryStep(BranchHistoryKind.Insert, key, phase, count);
                    }
                    else
                    {
                        next = BranchSchedule.ApplyFields(document, key, new[] { phase, index }, nextFields, Enemies().Select(item => item.Key).ToArray());
                        step = new BranchHistoryStep(BranchHistoryKind.Fields, key, phase, index);
                    }
                    Commit(next, pending ? "新增分支动作" : "编辑分支动作", step, address);
                    Open(key, phase, pending ? count : index); Refresh(true); onApplied?.Invoke();
                });
                ResetButton(tools, address, pending ? "取消新增" : "还原字段", onRebuild);
                if (!musicOnly) fold.Add(Note("分支阶段由调用者触发。阶段内不使用主片段的完成计数等待。"));
            }

            private void NodeTools(VisualElement parent, string key, int? phase, int index, int count)
            {
                var row = Bar(parent);
                Button(row, "字段树", () => owner.LocateDocumentPath(phase.HasValue ? Pointer(key, phase.Value, index) : Pointer(key, index)));
                foreach (var edit in new[] { BranchEdit.Copy, BranchEdit.Move, BranchEdit.Delete })
                {
                    if (edit == BranchEdit.Move)
                    {
                        Button(row, "上移", () => Change(key, phase, index, edit, index - 1)).SetEnabled(index > 0);
                        Button(row, "下移", () => Change(key, phase, index, edit, index + 1)).SetEnabled(index + 1 < count);
                    }
                    else Button(row, edit == BranchEdit.Copy ? "复制" : "删除", () => Change(key, phase, index, edit));
                }
            }

            private void Change(string key, int? phase, int index, BranchEdit edit, int target = -1)
            {
                var next = BranchSchedule.ChangeNode(document, key, phase, index, edit, target);
                if (edit == BranchEdit.Delete && !ConfirmDelete()) return;
                var kind = edit == BranchEdit.Copy ? BranchHistoryKind.Copy : edit == BranchEdit.Move ? BranchHistoryKind.Move : BranchHistoryKind.Delete;
                Commit(next, "编辑分支结构", new BranchHistoryStep(kind, key, phase ?? -1, index,
                    edit == BranchEdit.Copy ? index + 1 : target));
            }

            private void Commit(JObject next, string label, BranchHistoryStep step, string? applied = null)
            {
                owner.CommitDocumentEdit(LevelDocument.Parse(Json(next)), label, null, branchStep: step, appliedBranch: applied);
                owner.RemapBranchView(key => step.Remap(key, true));
                Refresh(true);
            }

            private bool ConfirmDelete() => owner.Dialogs.Discard("删除分支记录", "确定删除此项及其子项？撤销可恢复记录及关联草稿。");
            private void Open(string branch, params int[] address)
            {
                for (var depth = 0; depth <= address.Length; depth++)
                {
                    var key = BranchInspectorDrafts.Key(branch, address.Take(depth).ToArray());
                    if (!owner.expandedBranchRecords.Contains(key)) owner.expandedBranchRecords.Add(key);
                }
            }
            private Foldout Card(VisualElement parent, string branch, int[] address, string title, bool open = false)
            {
                var key = BranchInspectorDrafts.Key(branch, address);
                var fold = new Foldout { text = title, name = "branch-node:" + key,
                    value = open || owner.expandedBranchRecords.Contains(key) };
                fold.AddToClassList("wave-record"); fold.AddToClassList("wave-depth-" + (address.Length + 1));
                fold.style.marginLeft = 0; fold.contentContainer.style.marginLeft = 0;
                fold.EnableInClassList("selected-wave-record", key == revealedMusicTarget);
                fold.RegisterValueChangedCallback(evt =>
                {
                    if (evt.target != fold) return;
                    if (evt.newValue) Open(branch, address); else owner.expandedBranchRecords.Remove(key);
                });
                parent.Add(fold); return fold;
            }
            private void Populate(Foldout fold, Action load)
            {
                var loaded = false;
                void Load() { if (loaded) return; loaded = true; Run(load); }
                fold.RegisterValueChangedCallback(evt => { if (evt.target == fold && evt.newValue) Load(); });
                if (fold.value) Load();
            }
            private void Page(VisualElement parent, string key, int count, Action<int> draw)
            {
                var limit = limits.TryGetValue(key, out var saved) ? saved : 30;
                for (var index = 0; index < Math.Min(limit, count); index++) draw(index);
                if (limit < count) Button(parent, "显示后续 30 项", () => { limits[key] = limit + 30; Render(); });
            }
            private void Run(Action action)
            {
                try { action(); }
                catch (Exception error) { message.text = PluginLog.FailureMessage("document.branches", error); }
            }
            private Button Button(VisualElement parent, string label, Action action)
            {
                var button = new Button(() => Run(action)) { text = label }; parent.Add(button); return button;
            }
            private static VisualElement Bar(VisualElement parent)
            {
                var row = new VisualElement(); row.AddToClassList("workbench-tabs"); parent.Add(row); return row;
            }
            private static VisualElement Section(VisualElement parent, string heading)
            {
                var section = new VisualElement(); section.AddToClassList("wave-settings"); section.Add(Note(heading)); parent.Add(section); return section;
            }
            private static Label Note(string text) { var label = new Label(text); label.style.whiteSpace = WhiteSpace.Normal; return label; }
            private static string Json(JToken token) => token.ToString(Formatting.None);
            private static string Pointer(string branch, params int[] address) => "/branches/" + EscapePointer(branch) +
                (address.Length > 0 ? "/phases/" + address[0] : "") + (address.Length > 1 ? "/actions/" + address[1] : "");
        }
    }
}
