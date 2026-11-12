#nullable enable
using System;
using System.Collections.Generic;
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
        private static readonly string[] MapDataFields = { "map", "tiles", "blockEdges", "tags", "effects", "layerRects" };
        private const string DimensionsSlot = "mapData:dimensions";
        [SerializeField] private string mapDataField = "tiles";
        private Action? openMapDataPanel;
        private Action<string>? selectMapDataField;

        private void BuildMapDataPanel(VisualElement parent)
        {
            var root = new VisualElement { name = "mapdata-editor" };
            root.Add(new Label("地图数据 · LevelData.mapData"));
            var tabs = new VisualElement(); tabs.AddToClassList("workbench-tabs"); root.Add(tabs);
            var content = new VisualElement(); root.Add(content);
            var pages = new Dictionary<string, (Button Button, VisualElement Page)>();
            void Select(string field)
            {
                mapDataField = pages.ContainsKey(field) ? field : "tiles";
                foreach (var entry in pages)
                {
                    entry.Value.Button.EnableInClassList("selected-tab", entry.Key == mapDataField);
                    entry.Value.Page.style.display = entry.Key == mapDataField ? DisplayStyle.Flex : DisplayStyle.None;
                }
                if (mapDataField == "tiles") openTileDataPreview?.Invoke();
            }
            foreach (var field in MapDataFields)
            {
                var key = field;
                var button = new Button(() => Select(key)) { name = "mapdata-tab-" + key, text = key == "tiles" ? "tiles · TileData" : key };
                var page = new VisualElement { name = "mapdata-page-" + key };
                if (key == "tiles") BuildTileDataPanel(page);
                else
                {
                    page.Add(new Label("LevelData.mapData." + key));
                    if (key == "map") BuildMapDimensions(page);
                    BuildMapDataJsonField(page, key);
                }
                tabs.Add(button); content.Add(page); pages.Add(key, (button, page));
            }
            var rawButton = new Button(() => Select("root")) { text = "完整 mapData JSON", name = "mapdata-tab-root" };
            var rawPage = new VisualElement { name = "mapdata-page-root" };
            BuildMapDataJsonField(rawPage, "root");
            tabs.Add(rawButton); content.Add(rawPage); pages.Add("root", (rawButton, rawPage));
            selectMapDataField = Select;
            openMapDataPanel = () => Select(mapDataField);
            Select(mapDataField);
            parent.Add(root);
        }

        private void BuildMapDimensions(VisualElement parent)
        {
            var dimensions = new VisualElement(); dimensions.AddToClassList("workbench-tabs");
            var width = new IntegerField("宽 / 列") { name = "mapdata-width", isDelayed = true };
            var height = new IntegerField("高 / 行") { name = "mapdata-height", isDelayed = true };
            width.style.width = 130; height.style.width = 130;
            width.labelElement.style.minWidth = 48; height.labelElement.style.minWidth = 48;
            dimensions.Add(width); dimensions.Add(height); parent.Add(dimensions);
            string Current() => new JObject { ["width"] = session!.Document.Width, ["height"] = session.Document.Height }.ToString(Formatting.None);
            BindLocalDraft(DimensionsSlot, Current(), "map", raw =>
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    width.SetValueWithoutNotify(session!.Document.Width);
                    height.SetValueWithoutNotify(session.Document.Height);
                    return;
                }
                var value = JObject.Parse(raw);
                width.SetValueWithoutNotify(value.Value<int>("width")); height.SetValueWithoutNotify(value.Value<int>("height"));
            });
            void Capture()
            {
                try
                {
                    RequireWorkspaceEditable();
                    CaptureLocalDraft(DimensionsSlot, new JObject { ["width"] = width.value, ["height"] = height.value }.ToString(Formatting.None));
                }
                catch (Exception error)
                {
                    if (InspectorStore.TryRead(DimensionsSlot, out var raw)) DisplayLocalDraft(inspectorBindings[DimensionsSlot], raw);
                    if (status != null) status.text = error.Message;
                }
            }
            width.RegisterValueChangedCallback(_ => Capture()); height.RegisterValueChangedCallback(_ => Capture());
            var actions = new VisualElement(); actions.AddToClassList("workbench-tabs"); parent.Add(actions);
            AddButton(actions, "调整地图尺寸（裁剪预检）", () => ApplyMapDimensions(width.value, height.value));
            AddButton(actions, "读取当前尺寸", () => ReloadLocalDraft(DimensionsSlot, "map", Current()));
            var summary = new Label { name = "mapdata-dimensions-summary" }; parent.Add(summary);
            var warning = new HelpBox("", HelpBoxMessageType.Warning) { name = "mapdata-spatial-warning" }; parent.Add(warning);
            void Update()
            {
                RefreshCleanMapDraft(DimensionsSlot, "map", Current());
                summary.text = $"当前地图 {session!.Document.Width} × {session.Document.Height}；宽高由 map 行列数决定，不另存字段。";
                var warnings = session.Document.GetMapDataSpatialWarnings();
                warning.text = string.Join("\n", warnings);
                warning.style.display = warnings.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            }
            refreshFieldSummaries.Add(Update); Update();
        }

        private void BuildMapDataJsonField(VisualElement parent, string key)
        {
            var slot = "mapData:" + key;
            var target = key == "root" ? "mapData" : "mapData." + key;
            var field = new TextField(key == "root" ? "完整 mapData JSON" : key + " · 原始 JSON")
                { name = "mapdata-json-" + key, multiline = true, value = ReadMapDataField(key) };
            field.style.minHeight = 100;
            var state = new Label { name = "mapdata-state-" + key }; parent.Add(state);
            BindLocalDraft(slot, field.value, target, raw => field.SetValueWithoutNotify(raw));
            field.RegisterValueChangedCallback(e =>
            {
                try { RequireWorkspaceEditable(); CaptureLocalDraft(slot, e.newValue); }
                catch (Exception error)
                {
                    field.SetValueWithoutNotify(e.previousValue);
                    if (status != null) status.text = error.Message;
                }
            });
            parent.Add(field);
            parent.Add(new HelpBox(MapDataFieldHelp(key), HelpBoxMessageType.Info));
            var actions = new VisualElement(); actions.AddToClassList("workbench-tabs"); parent.Add(actions);
            AddButton(actions, "读取当前值", () => ReloadLocalDraft(slot, target, ReadMapDataField(key)));
            AddButton(actions, "应用 " + (key == "root" ? "mapData" : key), () => ApplyMapDataDraft(key, field.value));
            if (key != "root" && key != "map")
            {
                AddButton(actions, "设为空数组", () => { RequireWorkspaceEditable(); field.value = "[]"; });
                AddButton(actions, "设为 null", () => { RequireWorkspaceEditable(); field.value = "null"; });
                AddButton(actions, "移除此字段", () => { RequireWorkspaceEditable(); field.value = ""; });
            }
            void Update()
            {
                RefreshCleanMapDraft(slot, target, ReadMapDataField(key));
                var value = key == "root" ? session!.Document.GetSection("mapData") : session!.Document.GetSection("mapData")?[key];
                state.text = value == null ? "已应用文档：未提供此字段" : value.Type == JTokenType.Null ? "已应用文档：null" :
                    "已应用文档：" + value.Type + (value is JArray array ? " · " + array.Count + " 项" : "");
                if (InspectorStore.HasChanges(slot)) state.text += " · 有未应用草稿";
            }
            refreshFieldSummaries.Add(Update); Update();
        }

        private string ReadMapDataField(string key)
        {
            var mapData = session!.Document.GetSection("mapData");
            return (key == "root" ? mapData : mapData?[key])?.ToString(Formatting.Indented) ?? "";
        }

        private void RefreshCleanMapDraft(string slot, string target, string value)
        {
            if (InspectorStore.HasChanges(slot) || !inspectorBindings.TryGetValue(slot, out var binding)) return;
            if (binding.Document != session!.Json || binding.Initial != value) ReloadLocalDraft(slot, target, value);
        }

        private void RequireMapDataMutation(string currentSlot)
        {
            RequireWorkspaceEditable(); RequireAppliedDraft();
            if (InspectorStore.HasChanges("tileData"))
                throw new InvalidOperationException("TileData 有未应用草稿，请先在 tiles 中应用或明确重置，再修改地图结构。");
            foreach (var slot in inspectorBindings.Keys)
                if (slot.StartsWith("mapData:", StringComparison.Ordinal) && slot != currentSlot && InspectorStore.HasChanges(slot))
                    throw new InvalidOperationException("地图数据中还有另一份未应用草稿，请先处理：" + slot.Substring(8));
        }

        private void ApplyMapDataDraft(string key, string raw)
        {
            var slot = "mapData:" + key;
            RequireMapDataMutation(slot);
            RequireLocalDraft(slot, key == "root" ? "mapData" : "mapData." + key);
            var next = key == "root" ? session!.Document.WithMapData(raw) : string.IsNullOrWhiteSpace(raw) ?
                session!.Document.RemoveMapDataField(key) : session!.Document.WithMapDataField(key, raw);
            CommitEdit(next, "编辑 mapData" + (key == "root" ? "" : "." + key), null);
            AppliedLocalDraft(slot);
        }

        private void ApplyMapDimensions(int width, int height)
        {
            RequireMapDataMutation(DimensionsSlot); RequireLocalDraft(DimensionsSlot, "map");
            var resized = session!.Document.Resize(width, height);
            var next = session.Document.WithMapData(resized.GetSection("mapData")!.ToString(Formatting.None));
            CommitEdit(next, "调整地图尺寸", null);
            AppliedLocalDraft(DimensionsSlot);
        }

        private static string MapDataFieldHelp(string key)
        {
            switch (key)
            {
                case "map": return "二维数组的每格保存 tiles 配置索引。JSON 行序自上向下；场景和 TileData 绑定坐标自下向上。尺寸变化同样执行裁剪预检。";
                case "blockEdges": return "每项按 pos、direction、blockMask 保存。保留未知成员；尚不提供推测性的阻挡边绘制或自动裁剪。";
                case "tags": return "有序字符串列表，保留原始顺序和未知值。";
                case "effects": return "地图级效果记录：key、offset、direction。与 tiles 中的地块 effects 分开；资源存在性及运行表现未在此验证。";
                case "layerRects": return "原生字符串列表。具体格式和空间边界尚未核实，保留原文，不自动转换成矩形。";
                default: return "整体替换 mapData 会检查网格和配置索引，尺寸缩小执行裁剪预检。其他字段、未知成员及 null 保留。TileData 草稿须先处理。";
            }
        }
    }
}
