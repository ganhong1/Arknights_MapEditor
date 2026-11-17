#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        private void BuildRouteEditing(VisualElement parent)
        {
            var panel = new VisualElement { name = "scene-route-editor" };
            var library = AddRouteCard(panel, "路线库");
            var libraries = new PopupField<string>("路线库", new List<string> { "主路线", "额外路线" }, routeLibraryChoice);
            var motion = new PopupField<string>("新建路线移动方式", new List<string> { "地面", "飞行" }, 0);
            library.Add(libraries);
            library.Add(motion);
            var routes = new ListView { name = "scene-route-list", fixedItemHeight = 24,
                selectionType = SelectionType.Single, makeItem = () => new Label() };
            routes.style.height = 120;
            routes.AddToClassList("route-list");
            library.Add(routes);
            var toolbar = RouteRow(library);
            AddButton(toolbar, "新建路线", () => BeginRouteDraft(RouteLibrary, -1, RouteMotions[motion.index]));
            AddButton(toolbar, "编辑所选路线", () =>
            {
                if (routeListSelection < 0) throw new EditorOperationRejectedException("请先选择路线。");
                BeginRouteDraft(RouteLibrary, routeListSelection, "WALK");
            });
            AddButton(toolbar, "定位", () =>
            {
                if (DisplayedRoute() is not JObject route || !RouteEditDraft.TryCell(route["startPosition"], out var cell))
                    throw new EditorOperationRejectedException("请选择有起点的路线。");
                workspaceOwner?.FocusRouteScene(this, cell);
            });
            var draftNotice = new HelpBox("先应用或取消当前路线的修改，即可新建路线。", HelpBoxMessageType.Info)
            {
                name = "scene-route-draft-notice"
            };
            library.Add(draftNotice);
            var listActions = RouteRow(library);
            AddButton(listActions, "复制路线", () =>
            {
                var original = DisplayedRoute() ?? throw new EditorOperationRejectedException("请先选择路线。");
                BeginRouteDraft(RouteLibrary, -1, "WALK");
                routeDraft!.Replace(original);
                SaveRouteDraft();
            });
            AddButton(listActions, "上移", () => ChangeRouteOrder(routeListSelection - 1));
            AddButton(listActions, "下移", () => ChangeRouteOrder(routeListSelection + 1));
            AddButton(listActions, "删除路线", DeleteSelectedRoute);
            var detail = new VisualElement();
            panel.Add(detail);
            parent.Add(panel);

            LevelDocument? displayedDocument = null;
            string? displayedLibrary = null;
            ILookup<int, string>? routeUses = null;
            string? referenceError = null;
            RouteEditDraft? displayedDraft = null;
            var expanded = new HashSet<string>(StringComparer.Ordinal);
            refreshRouteEditing = () =>
            {
                var section = ReadRouteList();
                if (displayedDocument != WorkspaceDocument || displayedLibrary != RouteLibrary)
                {
                    try { routeUses = ReadRouteUses(); referenceError = null; }
                    catch (LevelDocumentException error) { routeUses = null; referenceError = error.Message; }
                    var summaries = section?.Select((route, index) => RouteSummary(route, index, routeUses?[index].Count())).ToArray()
                        ?? Array.Empty<string>();
                    routes.itemsSource = summaries;
                    routes.bindItem = (row, index) =>
                    {
                        ((Label)row).text = summaries[index];
                        row.tooltip = summaries[index];
                    };
                    routes.Rebuild();
                    displayedDocument = WorkspaceDocument;
                    displayedLibrary = RouteLibrary;
                }
                routes.SetSelectionWithoutNotify(routeListSelection >= 0 && routeListSelection < (section?.Count ?? 0)
                    ? new[] { routeListSelection } : Array.Empty<int>());
                libraries.SetValueWithoutNotify(libraries.choices[Mathf.Clamp(routeLibraryChoice, 0, 1)]);
                libraries.SetEnabled(routeDraft == null);
                toolbar.SetEnabled(routeDraft == null);
                draftNotice.style.display = routeDraft == null ? DisplayStyle.None : DisplayStyle.Flex;
                listActions.SetEnabled(routeDraft == null && routeListSelection >= 0);
                routes.SetEnabled(routeDraft == null);
                if (displayedDraft != routeDraft) expanded.Clear();
                displayedDraft = routeDraft;
                detail.Clear();
                if (routeDraft != null) DrawRouteDraftFields(detail, routeDraft, expanded);
                if (referenceError != null) detail.Add(new HelpBox(referenceError, HelpBoxMessageType.Warning));
                if (routeListSelection >= 0 && routeUses != null)
                    AddRouteUseDetails(detail, routeUses[routeListSelection].ToArray());
            };
            libraries.RegisterValueChangedCallback(_ =>
            {
                if (routeDraft != null || routeLibraryChoice == libraries.index) return;
                routeLibraryChoice = libraries.index;
                routeListSelection = -1;
                RouteVisualRevision++;
                RefreshRouteEditing();
                SceneView.RepaintAll();
            });
            routes.onSelectionChange += _ =>
            {
                if (routeDraft != null) return;
                routeListSelection = routes.selectedIndex;
                RouteVisualRevision++;
                RefreshRouteEditing();
                SceneView.RepaintAll();
            };
            if (routeDraft == null && InspectorStore.TryRead(SceneRouteSlot, out _)) RestoreRouteEditing();
            else RefreshRouteEditing();
        }

        private static string RouteSummary(JToken route, int index, int? uses)
        {
            if (route is not JObject data) return $"#{index} · 无效路线对象";
            return $"#{index} · {MotionLabel(data)} · {Coordinate(data["startPosition"])} → " +
                $"{Coordinate(data["endPosition"])} · {(uses.HasValue ? $"{uses} 处引用" : "引用待核对")}";
        }

        private static string Coordinate(JToken? value) => RouteEditDraft.TryCell(value, out var cell)
            ? $"({cell.y}, {cell.x})" : "未设置";

        private static string MotionLabel(JObject route) => PrtsRoutePreview.Matches(route["motionMode"], "FLY", "1") ? "飞行" :
            PrtsRoutePreview.Matches(route["motionMode"], "WALK", "0") ? "地面" : route["motionMode"]?.ToString() ?? "未设置";

        private static VisualElement RouteRow(VisualElement parent)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            parent.Add(row);
            return row;
        }

        private static void AddRouteUseDetails(VisualElement parent, IReadOnlyList<string> paths)
        {
            var uses = RouteSection($"使用位置 · {paths.Count}");
            if (paths.Count == 0) uses.Add(new Label("当前没有已识别的动作引用。"));
            foreach (var path in paths) uses.Add(new Label(path) { tooltip = path });
            parent.Add(uses);
        }

        private static Foldout RouteDraftSection(string title, string name, ISet<string> expanded)
        {
            var section = RouteSection(title, expanded.Contains(name), name);
            section.RegisterValueChangedCallback(change =>
            {
                if (change.target != section) return;
                if (change.newValue) expanded.Add(name);
                else expanded.Remove(name);
            });
            return section;
        }

        private void DrawRouteDraftFields(VisualElement parent, RouteEditDraft active, ISet<string> expanded)
        {
            var editing = AddRouteCard(parent, "当前路线");
            if (!RouteDraftIsCurrent(active))
                editing.Add(new HelpBox("路线库或地图尺寸已变化。草稿仍保存在完整路线 JSON 中。", HelpBoxMessageType.Warning));
            var actions = RouteRow(editing);
            if (workspaceOwner != null)
            {
                if (RouteSceneActive) AddButton(actions, "退出场景工具", ExitRouteScene);
                else
                {
                    AddButton(actions, active.Complete ? "在场景编辑" : "在场景绘制", () => EnterRouteScene(!active.Complete));
                    if (active.Index < 0 && active.Complete) AddButton(actions, "继续绘制", () => EnterRouteScene(true));
                }
                if (RouteSceneDrawing)
                {
                    AddButton(actions, "完成绘制", () => FinishRouteDrawing());
                    AddButton(actions, "撤销上一个点", RemoveLastRoutePoint);
                }
            }
            if (RouteDraftIsCurrent(active)) AddButton(actions, "应用", ApplyRouteDraft);
            AddButton(actions, "取消修改", () =>
            {
                if (!InspectorStore.HasChanges(SceneRouteSlot) ||
                    EditorUtility.DisplayDialog("取消路线修改", "清除当前路线草稿？", "确定", "取消"))
                    CancelRouteDraft();
            });
            var motion = new PopupField<string>("移动方式", new List<string> { "地面", "飞行" },
                MotionLabel(active.Route) == "飞行" ? 1 : 0);
            motion.RegisterValueChangedCallback(_ => EditRouteValue(() => RequireRouteDraft().Route["motionMode"] = RouteMotions[motion.index]));
            editing.Add(motion);
            var checkpoints = AddRouteCard(parent, "起终点与检查点");
            DrawRouteNodes(checkpoints, active, expanded);
            if (active.Checkpoints?.Any(step => !IsPreviewedRouteNode(step)) == true)
                checkpoints.Add(new HelpBox("路线含高级检查点，静态预览可能不完整。", HelpBoxMessageType.Info));
            var fields = RouteDraftSection("路线参数", "scene-route-parameters", expanded);
            foreach (var key in new[] { "allowDiagonalMove", "visitEveryTileCenter", "visitEveryNodeCenter", "visitEveryCheckPoint" })
            {
                var toggle = new Toggle(key) { value = PrtsRoutePreview.Matches(active.Route[key], "true", "1") };
                toggle.RegisterValueChangedCallback(e => EditRouteValue(() => RequireRouteDraft().Route[key] = e.newValue, false));
                fields.Add(toggle);
            }
            AddOffsetFields(fields, active.Route, "spawnOffset");
            AddOffsetFields(fields, active.Route, "spawnRandomRange");
            parent.Add(fields);
            var advanced = RouteDraftSection("完整路线 JSON", "scene-route-json", expanded);
            var json = new TextField { multiline = true, value = active.Route.ToString(Formatting.Indented) };
            json.style.height = 135;
            advanced.Add(json);
            AddButton(advanced, "载入为路线草稿", () =>
            {
                RequireRouteDraft().Replace(ParseRouteObject(json.value));
                routeNode = NoRouteNode;
                SaveRouteDraft();
            });
            parent.Add(advanced);
        }

        private void DrawRouteNodes(VisualElement parent, RouteEditDraft active, ISet<string> expanded)
        {
            var items = new List<int> { RouteEditDraft.Start };
            if (active.Checkpoints is JArray checkpoints) items.AddRange(Enumerable.Range(0, checkpoints.Count));
            items.Add(RouteEditDraft.End);
            var nodes = new ListView { name = "scene-route-nodes", fixedItemHeight = 24, itemsSource = items,
                selectionType = SelectionType.Single, makeItem = () => new Label() };
            nodes.AddToClassList("route-list");
            nodes.bindItem = (item, i) => ((Label)item).text = RouteNodeLabel(active, items[i]);
            nodes.style.height = Mathf.Min(180, items.Count * 24 + 2);
            nodes.SetSelectionWithoutNotify(routeNode == NoRouteNode ? Array.Empty<int>() : new[] { items.IndexOf(routeNode) }
                .Where(i => i >= 0));
            nodes.onSelectionChange += selection =>
            {
                if (selection.FirstOrDefault() is int selected) SelectRouteNode(selected);
            };
            parent.Add(nodes);
            if (active.Checkpoints is { Count: > 1 })
            {
                var orderMode = RouteDraftSection("拖动调整检查点顺序", "scene-route-checkpoint-order", expanded);
                var orderItems = Enumerable.Range(0, active.Checkpoints.Count).ToList();
                var reorder = new ListView { fixedItemHeight = 24, itemsSource = orderItems, reorderable = true,
                    reorderMode = ListViewReorderMode.Animated, makeItem = () => new Label() };
                reorder.AddToClassList("route-list");
                reorder.bindItem = (item, index) => ((Label)item).text = RouteNodeLabel(active, orderItems[index]);
                reorder.style.height = Mathf.Min(144, orderItems.Count * 24 + 2);
                reorder.itemIndexChanged += (from, to) => orderMode.schedule.Execute(() => EditRouteValue(() =>
                {
                    var draft = RequireRouteDraft();
                    if (draft != active) return;
                    draft.Reorder(from, to);
                    routeNode = to;
                }));
                orderMode.Add(reorder);
                parent.Add(orderMode);
            }
            var row = RouteRow(parent);
            var before = routeNode >= 0 ? routeNode + 1 : routeNode == RouteEditDraft.Start ? 0 : active.Checkpoints?.Count ?? 0;
            AddButton(row, "在此后插入移动点", () =>
            {
                if (!active.TryNodeCell(routeNode, out var cell) && !active.TryNodeCell(RouteEditDraft.Start, out cell))
                    throw new EditorOperationRejectedException("请先设置路线起点。");
                InsertRouteNode(before, cell);
            });
            AddButton(row, "等待", () => AddSpecialRouteNode(before, "WAIT_FOR_SECONDS"));
            AddButton(row, "消失", () => AddSpecialRouteNode(before, "DISAPPEAR"));
            if (workspaceOwner != null) AddButton(row, "再出现", () => { EnterRouteScene(false); pendingCheckpoint = before; });
            if (routeNode == NoRouteNode)
            {
                parent.Add(new HelpBox("选中列表中的检查点，即可编辑类型和参数。", HelpBoxMessageType.Info));
                return;
            }
            if (routeNode < 0)
            {
                if (active.TryNodeCell(routeNode, out _)) DrawRouteNodePosition(parent, active, routeNode);
                return;
            }
            if (active.Node(routeNode) is not JObject node) return;
            DrawCheckpointFields(parent, active, node, routeNode);
        }

        private void DrawCheckpointFields(VisualElement parent, RouteEditDraft active, JObject node, int index)
        {
            var settings = AddRouteCard(parent, $"检查点 {index + 1} 设置");
            settings.name = "scene-route-checkpoint-settings";
            var type = RouteEditDraft.ReadCheckpointType(node["type"]);
            var choices = CheckpointTypes.Select(typeOption => $"{CheckpointTypeName(typeOption)} · {typeOption}").ToList();
            var selected = type.HasValue ? Array.IndexOf(CheckpointTypes, type.Value) : -1;
            if (selected < 0)
            {
                choices.Insert(0, "原始值 · " + (node["type"]?.ToString(Formatting.None) ?? "未提供"));
                selected = 0;
            }
            var includeOriginal = !type.HasValue;
            var typeField = new PopupField<string>("检查点类型", choices, selected) { name = "scene-route-checkpoint-type" };
            typeField.RegisterValueChangedCallback(_ =>
            {
                var choice = typeField.index - (includeOriginal ? 1 : 0);
                if (choice >= 0) EditRouteValue(() => RequireRouteDraft().SetCheckpointType(index, CheckpointTypes[choice]));
            });
            settings.Add(typeField);
            if (RouteEditDraft.UsesPosition(type))
            {
                DrawRouteNodePosition(settings, active, index);
                AddOffsetFields(settings, node, "reachOffset", "到达偏移");
                if (type != RouteCheckpointType.APPEAR_AT_POS)
                {
                    AddRouteNumber(settings, "到达距离", node, "reachDistance");
                    var randomize = new Toggle("随机化到达偏移") { value = PrtsRoutePreview.Matches(node["randomizeReachOffset"], "true", "1") };
                    randomize.RegisterValueChangedCallback(e => EditRouteValue(() => node["randomizeReachOffset"] = e.newValue, false));
                    settings.Add(randomize);
                }
            }
            var timeLabel = type switch
            {
                RouteCheckpointType.WAIT_FOR_SECONDS => "等待时长（秒）",
                RouteCheckpointType.WAIT_FOR_PLAY_TIME => "战斗时间（秒）",
                RouteCheckpointType.WAIT_CURRENT_FRAGMENT_TIME => "当前片段时间（秒）",
                RouteCheckpointType.WAIT_CURRENT_WAVE_TIME => "当前波次时间（秒）",
                RouteCheckpointType.WAIT_BOSSRUSH_WAVE => "首领战波次（time）",
                _ => null
            };
            if (timeLabel != null) AddRouteNumber(settings, timeLabel, node, "time", refresh: true);
            if (type is RouteCheckpointType.DISAPPEAR or RouteCheckpointType.ALERT)
                settings.Add(new Label("此类型无需设置坐标或时间。"));
            var order = RouteRow(settings);
            AddButton(order, "上移节点", () => MoveSelectedNode(-1));
            AddButton(order, "下移节点", () => MoveSelectedNode(1));
            AddButton(order, "删除节点", () =>
            {
                RequireRouteDraft().Remove(routeNode);
                routeNode = NoRouteNode;
                SaveRouteDraft();
            });
        }

        private void DrawRouteNodePosition(VisualElement parent, RouteEditDraft active, int index)
        {
            var current = active.TryNodeCell(index, out var point) ? point : active.SuggestedCheckpointCell(index);
            AddRouteInteger(parent, "行 row", current.y, v => SetRouteNodeCoordinate(index, true, v));
            AddRouteInteger(parent, "列 col", current.x, v => SetRouteNodeCoordinate(index, false, v));
        }

        private void SetRouteNodeCoordinate(int index, bool row, int value)
        {
            var active = RequireRouteDraft();
            var cell = active.TryNodeCell(index, out var point) ? point : active.SuggestedCheckpointCell(index);
            var next = row ? new Vector2Int(cell.x, value) : new Vector2Int(value, cell.y);
            RequireRouteCell(next);
            active.Replace(active.MovedRoute(index, next));
        }

        private static readonly RouteCheckpointType[] CheckpointTypes =
            (RouteCheckpointType[])Enum.GetValues(typeof(RouteCheckpointType));

        private static string CheckpointTypeName(RouteCheckpointType type) => type switch
        {
            RouteCheckpointType.MOVE => "移动",
            RouteCheckpointType.WAIT_FOR_SECONDS => "原地等待",
            RouteCheckpointType.WAIT_FOR_PLAY_TIME => "等到战斗时间",
            RouteCheckpointType.WAIT_CURRENT_FRAGMENT_TIME => "等到片段时间",
            RouteCheckpointType.WAIT_CURRENT_WAVE_TIME => "等到波次时间",
            RouteCheckpointType.DISAPPEAR => "消失",
            RouteCheckpointType.APPEAR_AT_POS => "在指定位置再出现",
            RouteCheckpointType.ALERT => "警示",
            RouteCheckpointType.PATROL_MOVE => "巡逻移动",
            RouteCheckpointType.WAIT_BOSSRUSH_WAVE => "等待首领战波次",
            RouteCheckpointType.MAP_OFFSET_MOVE => "地图偏移移动",
            _ => type.ToString()
        };

        private static bool IsPreviewedRouteNode(JToken step) => step is JObject node &&
            RouteEditDraft.ReadCheckpointType(node["type"]) is RouteCheckpointType type && (int)type <= (int)RouteCheckpointType.ALERT;

        private static string RouteNodeLabel(RouteEditDraft active, int node)
        {
            var step = active.Node(node);
            var type = RouteEditDraft.ReadCheckpointType(step?["type"]);
            var label = node == RouteEditDraft.Start ? "起点" : node == RouteEditDraft.End ? "终点" :
                type.HasValue ? CheckpointTypeName(type.Value) : $"{step?["type"] ?? JValue.CreateNull()} · 原始类型";
            if (type is RouteCheckpointType.WAIT_FOR_SECONDS or RouteCheckpointType.WAIT_FOR_PLAY_TIME or
                RouteCheckpointType.WAIT_CURRENT_FRAGMENT_TIME or RouteCheckpointType.WAIT_CURRENT_WAVE_TIME)
                label += " " + step?["time"] + "s";
            var position = active.TryNodeCell(node, out var cell) ? $" · ({cell.y}, {cell.x})" : "";
            return (node < 0 ? "" : $"{node + 1}. ") + label + position;
        }

        private void AddSpecialRouteNode(int before, string type)
        {
            RequireRouteDraft().Insert(before, type, null);
            routeNode = before;
            SaveRouteDraft();
        }

        private void MoveSelectedNode(int by)
        {
            var active = RequireRouteDraft();
            var target = routeNode + by;
            if (target < 0 || target >= (active.Checkpoints?.Count ?? 0)) return;
            active.Reorder(routeNode, target);
            routeNode = target;
            SaveRouteDraft();
        }

        private void AddRouteInteger(VisualElement parent, string label, int value, Action<int> set)
        {
            var field = new IntegerField(label) { value = value, isDelayed = true };
            field.RegisterValueChangedCallback(e => EditRouteValue(() => set(e.newValue)));
            parent.Add(field);
        }

        private void AddOffsetFields(VisualElement parent, JObject owner, string key, string? label = null)
        {
            foreach (var axis in new[] { "x", "y" })
                AddRouteNumber(parent, $"{label ?? key} {axis}", owner, key, axis);
        }

        private void AddRouteNumber(VisualElement parent, string label, JObject owner, string key, string? axis = null,
            bool refresh = false)
        {
            var value = axis == null ? owner[key] : (owner[key] as JObject)?[axis];
            var field = new DoubleField(label) { value = PrtsRoutePreview.Number(value), isDelayed = true };
            field.RegisterValueChangedCallback(e => EditRouteValue(() =>
            {
                if (double.IsNaN(e.newValue) || double.IsInfinity(e.newValue) || Math.Abs(e.newValue) > float.MaxValue)
                    throw new FormatException("请输入有限的单精度数值。");
                if (axis == null) owner[key] = e.newValue;
                else
                {
                    if (owner[key] is not JObject) owner[key] = new JObject { ["x"] = 0, ["y"] = 0 };
                    owner[key]![axis] = e.newValue;
                }
            }, refresh));
            parent.Add(field);
        }

        private void EditRouteValue(Action edit, bool refresh = true)
        {
            try
            {
                RequireRouteDraft();
                edit();
                if (refresh) SaveRouteDraft();
                else
                {
                    PersistRouteValue(routeDraft!);
                    RouteVisualRevision++;
                    PersistInspectorDrafts();
                    SceneView.RepaintAll();
                }
            }
            catch (Exception error)
            {
                if (status != null) status.text = error.Message;
                RefreshRouteEditing();
            }
        }
    }
}
