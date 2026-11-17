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
        private sealed partial class WavesPanel : VisualElement
        {
            private const int PageSize = 40;
            private readonly MapEditorWindow owner;
            private readonly VisualElement tree = new() { name = "waves-field-tree" };
            private readonly VisualElement stale = new() { name = "waves-recovered-drafts" };
            private readonly Label message = new() { name = "waves-message" };
            private readonly TextField filter = new() { name = "waves-search", label = "搜索波次" };
            private readonly CatalogSearchInput<SearchRow> search;
            private readonly Dictionary<string, int> visibleRows = new();
            private HashSet<string>? matches;
            private readonly HashSet<string> matchParents = new(StringComparer.Ordinal);
            private LevelDocument? displayed;
            private JToken? waves;
            private int renderDeferrals;
            private sealed class SearchRow
            {
                internal string Address = "", Text = "", Detail = "";
            }

            internal WavesPanel(MapEditorWindow owner)
            {
                this.owner = owner;
                name = "waves-editor";
                Add(new Label("波次 · waves"));
                var toolbar = Toolbar(this);
                Command(toolbar, "定位 waves", () => owner.LocateWaveField(Array.Empty<int>()));
                Command(toolbar, "检查引用", CheckReferences);
                Add(filter);
                message.style.whiteSpace = WhiteSpace.Normal;
                Add(message); Add(stale); Add(tree);
                search = new CatalogSearchInput<SearchRow>(filter,
                    () => CatalogSearchRuntime.CreateWorker<SearchRow>(item => new JObject
                    {
                        ["code"] = item.Address, ["name"] = item.Text, ["description"] = item.Detail
                    }), Array.Empty<SearchRow>(), () => message.text = "正在搜索…", FilterResults);
                filter.RegisterValueChangedCallback(evt =>
                {
                    revealedMusic = null;
                    if (!string.IsNullOrWhiteSpace(evt.newValue)) PopulateSearch();
                });
            }

            private void CheckReferences()
            {
                var document = owner.WorkspaceDocument;
                var items = document.ValidateWaveReferences(ReadEnemies().Select(choice => choice.Key))
                    .Where(item => item.Path.StartsWith("waves", StringComparison.Ordinal) ||
                        item.Path.StartsWith("$.waves", StringComparison.Ordinal)).ToArray();
                message.text = items.Length == 0 ? "波次字段与已知引用检查通过。" :
                    string.Join("\n", items.Take(12).Select(item => item.Path + " · " + item.Message)) +
                    (items.Length > 12 ? $"\n另有 {items.Length - 12} 项" : "");
            }

            internal void Refresh(bool force = false)
            {
                if (renderDeferrals > 0) return;
                var document = owner.WorkspaceDocument;
                if (!force && ReferenceEquals(displayed, document)) return;
                displayed = document;
                waves = document.GetSection("waves");
                Render();
                if (!string.IsNullOrWhiteSpace(filter.value)) PopulateSearch();
            }

            private LevelDocument? searchDocument;
            private void PopulateSearch()
            {
                if (ReferenceEquals(searchDocument, displayed)) return;
                searchDocument = displayed;
                search.SetItems(EnumerateRows(waves, Array.Empty<int>()).ToArray());
            }

            private void FilterResults(IReadOnlyList<SearchRow> rows, string? error)
            {
                var next = string.IsNullOrWhiteSpace(filter.value) || error != null ? null :
                    new HashSet<string>(rows.Select(row => row.Address), StringComparer.Ordinal);
                if (next == null && matches == null && error == null) return;
                matches = next;
                matchParents.Clear();
                if (next != null)
                    foreach (var key in next)
                        for (var end = key.IndexOf('/'); end >= 0; end = key.IndexOf('/', end + 1))
                            matchParents.Add(key.Substring(0, end));
                message.text = error ?? (matches == null ? "" : $"{rows.Count} 个搜索结果");
                Render();
            }

            private static IEnumerable<SearchRow> EnumerateRows(JToken? value, int[] parent)
            {
                if (value is not JArray rows) yield break;
                for (var i = 0; i < rows.Count; i++)
                {
                    var path = WaveSchedule.Append(parent, i);
                    var row = rows[i];
                    yield return new SearchRow
                    {
                        Address = WaveSchedule.Key(path), Text = Title(row, path),
                        Detail = row is JObject record ? WaveSchedule.Fields(record, path.Length).ToString(Formatting.None) : ""
                    };
                    if (path.Length < 3 && row is JObject item)
                        foreach (var child in EnumerateRows(item[WaveSchedule.ChildField(path.Length)], path))
                            yield return child;
                }
            }

            private void Render()
            {
                tree.Clear();
                stale.Clear();
                DrawRecoveredDrafts();
                DrawCollection(tree, waves, Array.Empty<int>());
            }

            private void DrawRecoveredDrafts()
            {
                var slots = WaveInspectorDrafts.Saved(owner.InspectorStore)
                    .Where(slot =>
                    {
                        var key = WaveInspectorDrafts.Target(owner.InspectorStore, slot);
                        return key == null || !WaveInspectorDrafts.IsCurrent(owner.InspectorStore, slot, owner.session!.Json, key);
                    }).ToArray();
                if (slots.Length == 0) return;
                var recovered = new Foldout { text = $"待处理波次草稿 · {slots.Length}", value = false };
                foreach (var slot in slots)
                {
                    if (!owner.InspectorStore.TryRead(slot, out var raw)) continue;
                    owner.InspectorStore.TryReadTarget(slot, out var address);
                    recovered.Add(Note(address + " · 原编辑位置"));
                    Command(recovered, "复制草稿 JSON", () => EditorGUIUtility.systemCopyBuffer = raw);
                    Command(recovered, "删除这份草稿", () =>
                    {
                        if (!owner.Dialogs.Discard("删除波次草稿", "确定删除这份草稿？")) return;
                        owner.InspectorStore.Remove(slot);
                        Persist();
                        Render();
                    });
                }
                stale.Add(recovered);
            }

            private void DrawCollection(VisualElement container, JToken? value, int[] parent)
            {
                var field = parent.Length == 0 ? "waves" : WaveSchedule.ChildField(parent.Length);
                var rows = value as JArray;
                var outer = Toolbar(container);
                outer.Add(Note(field + " [] · " + (rows != null ? rows.Count.ToString() : Raw(value))));
                if (rows != null || value == null || value.Type == JTokenType.Null)
                    Command(outer, parent.Length == 0 ? "新增波次" : parent.Length == 1 ? "新增片段" : "新增动作",
                        () => AddNode(parent));
                if (rows == null && value != null && value.Type != JTokenType.Null) return;

                var parentKey = WaveSchedule.Key(parent);
                var pendingKey = parentKey + "/new";
                var count = rows?.Count ?? 0;
                var shown = Math.Max(PageSize, visibleRows.TryGetValue(parentKey, out var cached) ? cached : 0);
                if (rows != null)
                {
                    var rendered = 0;
                    for (var i = 0; i < rows.Count; i++)
                    {
                        var address = WaveSchedule.Append(parent, i);
                        var key = WaveSchedule.Key(address);
                        if (matches != null && !matches.Contains(key) && !matchParents.Contains(key) && !MatchesRevealedMusic(key)) continue;
                        if (++rendered > shown)
                        {
                            Command(container, "显示后续 40 项", () =>
                            {
                                visibleRows[parentKey] = shown + PageSize;
                                Render();
                            });
                            break;
                        }
                        DrawNode(container, rows[i], address, count);
                    }
                }
                if (parent.Length == 2 && TryDraft(pendingKey, out var raw))
                    DrawNode(container, WaveSchedule.NewNode(3), WaveSchedule.Append(parent, count), count, raw);
            }

            private static string Title(JToken value, int[] address)
            {
                var heading = $"[{address.Last()}] " +
                    (address.Length == 1 ? "Wave · 波次" : address.Length == 2 ? "Fragment · 片段" : "Action");
                if (value is not JObject record) return heading + " · " + Raw(value);
                if (address.Length == 1)
                    return heading + (record["advancedWaveTag"]?.Type == JTokenType.String ? " · " + record["advancedWaveTag"] : "");
                if (address.Length == 2)
                    return heading + $" · {((record["actions"] as JArray)?.Count.ToString() ?? "—")} 条动作";
                var type = WaveSchedule.ActionType(record["actionType"]);
                if (type != "SPAWN") return heading + " · " + type +
                    (record["key"]?.Type == JTokenType.String ? " · " + record["key"] : "");
                return heading + $" · SPAWN · {Raw(record["key"])} ×{Raw(record["count"])} · +{Raw(record["preDelay"])}s" +
                    $" / {Raw(record["interval"])}s · routes[{Raw(record["routeIndex"])}]" +
                    (record["blockFragment"]?.Type == JTokenType.Boolean && record.Value<bool>("blockFragment") ? " · 阻塞片段" : "");
            }

            private void DrawNode(VisualElement parent, JToken value, int[] address, int count, string? pending = null)
            {
                var isNew = pending != null;
                var key = isNew ? WaveSchedule.Key(address.Take(address.Length - 1)) + "/new" : WaveSchedule.Key(address);
                var fold = new Foldout
                {
                    name = "waves-node-" + key.Replace('/', '-'),
                    text = (isNew ? "新增动作草稿" : Title(value, address)) +
                        (owner.InspectorStore.HasChanges(WaveInspectorDrafts.Slot(key)) ? " ●" : ""),
                    value = isNew || owner.expandedWaveRecords.Contains(key) || matchParents.Contains(key)
                };
                fold.style.marginLeft = 0;
                fold.contentContainer.style.marginLeft = 0;
                fold.AddToClassList("wave-record");
                fold.AddToClassList("wave-depth-" + address.Length);
                fold.EnableInClassList("selected-wave-record", key == owner.selectedWaveRecord);
                parent.Add(fold);
                var loaded = false;
                void Load()
                {
                    if (loaded) return;
                    loaded = true;
                    var collection = address.Take(address.Length - 1).ToArray();
                    var index = address.Last();
                    var buttons = Toolbar(fold);
                    if (!isNew)
                    {
                        Command(buttons, "字段树", () => owner.LocateWaveField(address));
                        Command(buttons, "复制", () => ChangeOrder(collection, index, Order.Copy)).SetEnabled(value is JObject);
                        Command(buttons, "上移", () => ChangeOrder(collection, index, Order.Up)).SetEnabled(index > 0);
                        Command(buttons, "下移", () => ChangeOrder(collection, index, Order.Down)).SetEnabled(index + 1 < count);
                        Command(buttons, "删除", () => ChangeOrder(collection, index, Order.Delete));
                    }
                    if (value is not JObject record) return;
                    if (address.Length == 3)
                    {
                        DrawAction(fold, address, key, record, isNew);
                        return;
                    }
                    var settings = new VisualElement();
                    settings.AddToClassList("wave-settings");
                    settings.Add(Note(address.Length == 1 ? "波次参数" : "片段参数"));
                    fold.Add(settings);
                    var form = CreateForm(key, record, address, isNew);
                    if (form != null)
                    {
                        DrawField(settings, form, "preDelay", FieldKind.Time);
                        if (address.Length == 1)
                        {
                            DrawField(settings, form, "postDelay", FieldKind.Time);
                            DrawField(settings, form, "maxTimeWaitingForNextWave", FieldKind.Wait);
                            settings.Add(Note(index + 1 == count ? "末波不使用下一波等待上限。" :
                                "等待上限作用于清场等待，片段完成后开始计时。"));
                        }
                    }
                    if (form != null)
                    {
                        if (address.Length == 1) DrawField(settings, form, "advancedWaveTag", FieldKind.Text);
                        DrawApply(settings, form);
                    }
                    var child = new VisualElement();
                    child.AddToClassList("wave-children");
                    DrawCollection(child, record[WaveSchedule.ChildField(address.Length)], address);
                    fold.Add(child);
                    if (address.Length == 2 && record["actions"] is JArray actions)
                        DrawTimeline(fold, record["preDelay"], actions);
                }
                fold.RegisterValueChangedCallback(change =>
                {
                    if (change.target != fold || isNew) return;
                    if (change.newValue)
                    {
                        owner.selectedWaveRecord = key;
                        foreach (var row in tree.Query<Foldout>(className: "wave-record").ToList())
                            row.EnableInClassList("selected-wave-record", row == fold);
                        if (!owner.expandedWaveRecords.Contains(key)) owner.expandedWaveRecords.Add(key);
                        Load();
                    }
                    else owner.expandedWaveRecords.Remove(key);
                });
                if (fold.value) Load();
            }

            private enum Order { Copy, Up, Down, Delete }

            private void AddNode(int[] parent)
            {
                owner.RequireWorkspaceEditable();
                owner.RequireAppliedDraft();
                var candidate = waves?.DeepClone();
                var rows = WaveSchedule.GetOrCreateChildren(ref candidate, parent);
                if (parent.Length == 2)
                {
                    var key = WaveSchedule.Key(parent) + "/new";
                    var slot = WaveInspectorDrafts.Slot(key);
                    if (owner.InspectorStore.HasChanges(slot))
                    {
                        if (!TryDraft(key, out _)) throw new EditorOperationRejectedException("请先处理这个片段旧的动作草稿。");
                    }
                    else
                    {
                        owner.InspectorStore.SetDraft(slot, owner.session!.Json, key, "", Json(WaveSchedule.NewNode(3)));
                        Persist();
                    }
                    Expand(parent);
                    Render();
                    return;
                }
                var address = WaveSchedule.Append(parent, rows.Count);
                rows.Add(WaveSchedule.NewNode(parent.Length + 1));
                Commit(candidate!, parent.Length == 0 ? "新增波次" : "新增片段",
                    new WaveHistoryStep(WaveOrder.Append, parent, address.Last()));
                Expand(address);
                Refresh(true);
            }

            private void ChangeOrder(int[] parent, int index, Order order)
            {
                owner.RequireWorkspaceEditable();
                owner.RequireAppliedDraft();
                var candidate = waves?.DeepClone();
                var rows = WaveSchedule.GetOrCreateChildren(ref candidate, parent);
                var target = order == Order.Up ? index - 1 : index + 1;
                if (order == Order.Delete)
                {
                    if (!owner.Dialogs.Discard("删除 " + WaveSchedule.Path(WaveSchedule.Append(parent, index)),
                        "确定删除此项及其子项？后续索引会前移，相关未应用草稿将移入待处理列表。撤销删除可恢复绑定。")) return;
                    rows.RemoveAt(index);
                }
                else if (order == Order.Copy) rows.Insert(target, rows[index].DeepClone());
                else
                {
                    var moved = rows[index];
                    rows.RemoveAt(index);
                    rows.Insert(target, moved);
                }
                var step = new WaveHistoryStep(order == Order.Delete ? WaveOrder.Delete :
                    order == Order.Copy ? WaveOrder.Copy : WaveOrder.Move, parent, index, target);
                var label = order == Order.Delete ? "删除" : order == Order.Copy ? "复制" : "重排";
                Commit(candidate!, label + " " + WaveSchedule.Path(WaveSchedule.Append(parent, index)), step);
                owner.RemapWaveView(key => step.Remap(key, true));
                if (order == Order.Copy || order == Order.Up || order == Order.Down)
                    Expand(WaveSchedule.Append(parent, target));
                else if (owner.selectedWaveRecord.Length == 0) owner.selectedWaveRecord = WaveSchedule.Key(parent);
                Refresh(true);
            }

            internal void ResetPaging() => visibleRows.Clear();

            private void Expand(int[] address)
            {
                for (var length = 1; length <= address.Length; length++)
                {
                    var key = WaveSchedule.Key(address.Take(length));
                    if (!owner.expandedWaveRecords.Contains(key)) owner.expandedWaveRecords.Add(key);
                    var parent = WaveSchedule.Key(address.Take(length - 1));
                    if (address[length - 1] >= PageSize)
                        visibleRows[parent] = Math.Max(address[length - 1] + 1,
                            visibleRows.TryGetValue(parent, out var count) ? count : 0);
                }
                owner.selectedWaveRecord = WaveSchedule.Key(address);
            }

            private void Commit(JToken candidate, string label, WaveHistoryStep step, string? applied = null)
            {
                var next = owner.WorkspaceDocument.WithSection("waves", Json(candidate));
                renderDeferrals++;
                try { owner.CommitDocumentEdit(next, label, null, step, applied); }
                finally { renderDeferrals--; }
            }

            private bool TryDraft(string key, out string raw)
            {
                raw = "";
                var slot = WaveInspectorDrafts.Slot(key);
                return owner.InspectorStore.HasChanges(slot) &&
                    WaveInspectorDrafts.IsCurrent(owner.InspectorStore, slot, owner.session!.Json, key) &&
                    owner.InspectorStore.TryRead(slot, out raw);
            }

            private void Persist()
            {
                owner.PersistInspectorDrafts();
            }

            private Button Command(VisualElement parent, string text, Action action)
            {
                var button = new Button(() => Run(action)) { text = text };
                parent.Add(button);
                return button;
            }

            private void Run(Action action)
            {
                try { action(); }
                catch (Exception error)
                {
                    owner.ReportWindowFailure("document.waves", error);
                    message.text = error.Message;
                }
            }

            private static VisualElement Toolbar(VisualElement parent)
            {
                var row = new VisualElement();
                row.AddToClassList("workbench-tabs");
                parent.Add(row);
                return row;
            }

            private static Label Note(string text)
            {
                var label = new Label(text);
                label.style.whiteSpace = WhiteSpace.Normal;
                return label;
            }

            private static string Json(JToken token) => token.ToString(Formatting.None);
            private static string Raw(JToken? token) => token == null ? "未提供" : Json(token);
        }
    }
}
