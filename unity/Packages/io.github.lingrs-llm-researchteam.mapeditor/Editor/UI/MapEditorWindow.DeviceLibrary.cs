#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        private Func<string, JObject, bool>? prepareDevicePreset;

        private void BuildDeviceLibrary(VisualElement parent, VisualElement workbench)
        {
            var catalog = DeviceSampleCatalog.LoadBundled();
            var search = new TextField("搜索") { name = "device-library-search", tooltip = "搜索装置名称、拼音或 ID" };
            search.AddToClassList("catalog-search");
            search.labelElement.style.minWidth = 36;
            parent.Add(search);
            var onlySamples = new Toggle("仅显示有原版配置") { name = "device-library-filter" };
            parent.Add(onlySamples);
            var status = new Label { name = "device-library-status" };
            status.AddToClassList("terrain-detail-text");
            parent.Add(status);
            var overlay = new DeviceDetailOverlay(catalog, (role, record) => prepareDevicePreset?.Invoke(role, record) ?? false);
            workbench.Add(overlay);
            var entries = new List<DeviceSampleCatalog.Entry>();
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/Catalog/terrain-info.png");
            var list = new ListView(entries, 58, () => new DeviceLibraryRow(icon, (entry, button) => overlay.Open(entry, button)),
                (element, index) => ((DeviceLibraryRow)element).Bind(entries[index]))
            { name = "device-library", selectionType = SelectionType.None };
            list.AddToClassList("catalog-list");
            parent.Add(list);
            IReadOnlyList<DeviceSampleCatalog.Entry> results = catalog.Entries;
            void Filter(string? error = null)
            {
                entries.Clear();
                entries.AddRange(results.Where(item => !onlySamples.value || item.HasSamples));
                status.text = error ?? $"{entries.Count} 项装置";
                list.Rebuild();
            }
            onlySamples.RegisterValueChangedCallback(_ => Filter());
            _ = new CatalogSearchInput<DeviceSampleCatalog.Entry>(search, catalog.CreateSearch, catalog.Entries,
                () => status.text = "搜索中…", (matches, error) => { results = matches; Filter(error); });
            Filter();
        }

        private sealed class DeviceLibraryRow : VisualElement
        {
            private readonly Button select;
            private readonly Button info;
            private readonly Label title = new Label();
            private readonly Label subtitle = new Label();
            private DeviceSampleCatalog.Entry? entry;

            internal DeviceLibraryRow(Texture2D icon, Action<DeviceSampleCatalog.Entry, Button> open)
            {
                AddToClassList("terrain-entry-row");
                AddToClassList("catalog-entry");
                select = new Button(() => { if (entry != null) open(entry, select); });
                select.AddToClassList("terrain-select");
                title.AddToClassList("catalog-name");
                subtitle.AddToClassList("catalog-id");
                select.Add(title); select.Add(subtitle); Add(select);
                info = new Button(() => { if (entry != null) open(entry, info); });
                info.AddToClassList("terrain-info-button");
                if (icon != null) info.Add(new Image { image = icon, pickingMode = PickingMode.Ignore });
                else info.text = "ⓘ";
                Add(info);
            }

            internal void Bind(DeviceSampleCatalog.Entry item)
            {
                entry = item;
                title.text = item.Name;
                subtitle.text = item.Id;
                select.tooltip = item.HasSamples
                    ? $"场上预设 {item.Configurations("tokenInsts")} 组 · 卡牌配置 {item.Configurations("tokenCards")} 组"
                    : "暂无原版场上或卡牌配置";
                info.tooltip = "查看 " + item.Name + " 的配置与出处";
                info.name = "device-info-" + item.Id;
            }
        }
    }
}
