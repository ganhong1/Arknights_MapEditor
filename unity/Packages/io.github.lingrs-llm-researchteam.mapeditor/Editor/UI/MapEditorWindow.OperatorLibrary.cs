#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Storage;
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
        private void BuildOperatorLibrary(VisualElement parent, VisualElement workbench)
        {
            var catalog = OperatorPresetCatalog.LoadBundled();
            var searchRow = new VisualElement { name = "operator-library-search-row" };
            searchRow.AddToClassList("operator-library-search-row");
            parent.Add(searchRow);
            var search = new TextField("搜索") { name = "operator-library-search", tooltip = "搜索干员名称或 ID" };
            search.AddToClassList("catalog-search");
            search.labelElement.style.minWidth = 36;
            searchRow.Add(search);
            var tags = new OperatorTagFilter();
            var tagsButton = new Button { name = "operator-tag-filter-toggle", tooltip = "按干员标签筛选" };
            searchRow.Add(tagsButton);
            var filter = new Toggle("仅显示有原版样例") { name = "operator-library-filter" };
            parent.Add(filter);
            var count = new Label { name = "operator-library-count" };
            count.AddToClassList("catalog-id");
            parent.Add(count);

            var detail = new OperatorPresetDetail(catalog, (role, record) => prepareLibraryOperator?.Invoke(role, record) ?? false);
            workbench.Add(detail);
            var infoIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(PrtsObjectView.CatalogRoot + "terrain-info.png");
            var visible = new List<OperatorPresetCatalog.Entry>();
            var list = new ListView(visible, 48,
                () => new OperatorLibraryRow(infoIcon, entry =>
                {
                    if (libraryOperatorDraft && entry.Id == selectedOperatorId && devicePresetRole == "characterInsts")
                    {
                        SetEraseMode(false);
                        SetDevicePlacement(true);
                        selectWorkbenchTab?.Invoke(5);
                        return;
                    }
                    try { prepareLibraryOperator?.Invoke("characterInsts", OperatorPresetDetail.CreateBlank(entry, catalog)); }
                    catch (Exception error) { if (status != null) status.text = error.Message; }
                }, (entry, button) => detail.Open(entry, button)),
                (element, row) => ((OperatorLibraryRow)element).Bind(visible[row],
                    tags.Count == 0 ? null : tags.Match(visible[row]), selectedOperatorId == visible[row].Id))
            { name = "operator-library", selectionType = SelectionType.None };
            list.AddToClassList("catalog-list");
            parent.Add(list);
            refreshOperatorLibrarySelection = list.RefreshItems;

            var results = catalog.Entries;
            void Refresh(string? error = null)
            {
                visible.Clear();
                visible.AddRange(results.Where(entry =>
                    (!filter.value || entry.Instances + entry.Cards > 0) && tags.Match(entry) != null));
                count.text = error ?? $"{visible.Count} 名干员";
                list.Rebuild();
            }
            parent.Insert(parent.IndexOf(searchRow) + 1,
                new OperatorTagFilterPanel(tags, catalog.Entries, tagsButton, () => Refresh()));
            filter.RegisterValueChangedCallback(_ => Refresh());
            _ = new CatalogSearchInput<OperatorPresetCatalog.Entry>(search, catalog.CreateSearch, catalog.Entries,
                () => count.text = "搜索中…", (matches, error) => { results = matches; Refresh(error); });
            Refresh();
        }

        private static void ShowSquadImport(VisualElement parent, Func<string, JObject, bool> choose)
        {
            var path = EditorUtility.OpenFilePanel("选择编队快照", "", "json");
            if (path.Length == 0) return;
            BuildSquadImport(parent, DocumentFileStore.Read(path).Text, choose);
        }

        internal static void BuildSquadImport(VisualElement parent, string json, Func<string, JObject, bool> choose)
        {
            var slots = SquadPresetImport.Read(json);
            var catalog = OperatorPresetCatalog.LoadBundled();
            var labels = slots.Select(slot => $"{slot.Label} · {catalog.Find(slot.CharacterId)?.Name ?? slot.CharacterId}").ToList();
            parent.Clear();
            var slotField = new PopupField<string>("编队槽位", labels, 0) { name = "preset-squad-slot" };
            parent.Add(slotField);
            var preview = new Label { name = "preset-squad-preview" };
            preview.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(preview);
            var row = PresetButtonRow(parent);
            JObject? converted = null;
            void Use(string role)
            {
                if (converted == null) return;
                try { if (choose(role, (JObject)converted.DeepClone())) parent.Clear(); }
                catch (Exception error) { preview.text = error.Message; }
            }
            var place = new Button(() => Use("characterInsts")) { text = "复制到场上草稿", name = "preset-squad-instance" };
            var card = new Button(() => Use("characterCards")) { text = "复制到卡牌草稿", name = "preset-squad-card" };
            row.Add(place);
            row.Add(card);
            row.Add(new Button(() => parent.Clear()) { text = "关闭" });
            void Select()
            {
                converted = null;
                try
                {
                    converted = slots[slotField.index].ToPreset(catalog);
                    preview.text = OperatorPresetDetail.Describe(converted, catalog);
                }
                catch (Exception error) { preview.text = error.Message; }
                place.SetEnabled(converted != null);
                card.SetEnabled(converted != null);
            }
            slotField.RegisterValueChangedCallback(_ => Select());
            Select();
        }

        private sealed class OperatorLibraryRow : VisualElement
        {
            private readonly Button info;
            private readonly Button summary;
            private readonly Image image;
            private readonly Label title = new();
            private readonly Label subtitle = new();
            private OperatorPresetCatalog.Entry? entry;

            internal OperatorLibraryRow(Texture2D? infoIcon, Action<OperatorPresetCatalog.Entry> select,
                Action<OperatorPresetCatalog.Entry, Button> open)
            {
                AddToClassList("terrain-entry-row");
                summary = new Button();
                summary.AddToClassList("operator-library-row");
                image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                image.AddToClassList("operator-library-avatar");
                summary.Add(image);
                var labels = new VisualElement { pickingMode = PickingMode.Ignore };
                title.AddToClassList("catalog-name");
                subtitle.AddToClassList("catalog-id");
                labels.Add(title);
                labels.Add(subtitle);
                summary.Add(labels);
                Add(summary);
                info = new Button();
                info.AddToClassList("terrain-info-button");
                if (infoIcon != null) info.Add(new Image { image = infoIcon, pickingMode = PickingMode.Ignore });
                else info.text = "ⓘ";
                Add(info);
                summary.clicked += () => { if (entry != null) select(entry); };
                info.clicked += () => { if (entry != null) open(entry, info); };
            }

            internal void Bind(OperatorPresetCatalog.Entry item, OperatorPresetCatalog.FilterForm? matchedForm, bool selected)
            {
                entry = item;
                summary.name = "operator-select-" + item.Id;
                summary.tooltip = "选中 " + item.Name + " 并编辑预设";
                summary.EnableInClassList("selected-tab", selected);
                info.name = "operator-info-" + item.Id;
                info.tooltip = "查看 " + item.Name + " 的详情";
                title.text = item.Name;
                var profession = OperatorLabels.Profession(matchedForm?.Profession ?? item.Profession);
                subtitle.text = $"{profession} · {item.Rarity}★ · 场上 {item.Instances} / 卡牌 {item.Cards}";
                tooltip = item.Id;
                var hasIcon = PrtsObjectView.TryGetOperatorAvatar(item.Id, out var texture, out var uv);
                image.image = texture;
                if (hasIcon) image.uv = uv;
                image.style.visibility = hasIcon ? Visibility.Visible : Visibility.Hidden;
            }
        }
    }
}
