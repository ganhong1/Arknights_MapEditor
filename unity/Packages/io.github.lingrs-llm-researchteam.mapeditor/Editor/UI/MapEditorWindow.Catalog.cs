#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        [SerializeField] private bool terrainPlacementEnabled;
        [SerializeField] private string selectedTerrainGroup = "";
        internal bool TerrainPlacementEnabled => terrainPlacementEnabled;
        private void BuildCatalogPanel(VisualElement parent, VisualElement workbench)
        {
            var catalog = EditorCatalog.LoadBundled();
            var samples = TerrainSampleCatalog.LoadBundled();
            var terrain = catalog.Entries.Where(entry => entry.Category == "terrain").ToArray();
            var groups = terrain.GroupBy(GroupKey).ToDictionary(group => group.Key, group => group.ToArray());
            var search = new TextField { name = "terrain-search", label = "搜索", tooltip = "搜索家族、中文名、ID 或描述" };
            search.AddToClassList("catalog-search");
            search.labelElement.style.minWidth = 36;
            parent.Add(search);
            var searchHint = new Label();
            searchHint.AddToClassList("terrain-detail-text");
            parent.Add(searchHint);
            var available = new Toggle("仅显示可直接绘制") { name = "terrain-filter" };
            available.AddToClassList("catalog-filter");
            parent.Add(available);

            var entries = new List<CatalogEntry>();
            ListView list = null!;
            var selection = new Label { name = "terrain-selection" };
            selection.AddToClassList("tiledata-preview-message");
            var detail = new TerrainDetailOverlay(samples, ApplyTerrain);
            workbench.Add(detail);
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/Catalog/terrain-info.png");
            list = new ListView(entries, 48,
                () => new TerrainCatalogRow(icon, SelectTerrain, (entry, button) => detail.Open(entry, groups[GroupKey(entry)], button)),
                (element, index) => ((TerrainCatalogRow)element).Bind(entries[index], groups[GroupKey(entries[index])].Length,
                    samples.HasRecommendation(entries[index].Id), GroupKey(entries[index]) == selectedTerrainGroup))
            {
                name = "terrain-library",
                selectionType = SelectionType.None
            };
            list.AddToClassList("catalog-list");
            search.RegisterValueChangedCallback(_ => Filter());
            available.RegisterValueChangedCallback(_ => Filter());
            parent.Add(list);
            parent.Add(selection);
            Filter();

            void Filter()
            {
                entries.Clear();
                entries.AddRange(catalog.Search(search.value, "terrain", out var error)
                    .Where(entry => !available.value || samples.HasRecommendation(entry.Id))
                    .GroupBy(GroupKey)
                    .Select(group => group.FirstOrDefault(entry => samples.HasRecommendation(entry.Id)) ?? group.First()));
                searchHint.text = error ?? "";
                searchHint.style.display = error == null ? DisplayStyle.None : DisplayStyle.Flex;
                list.Rebuild();
            }

            void SelectTerrain(CatalogEntry entry)
            {
                var tile = samples.Default(entry.Id);
                if (tile == null)
                {
                    selection.text = $"{entry.DisplayName} · 暂无可直接绘制的配置";
                    return;
                }

                ApplyTerrain(entry, tile);
            }

            void ApplyTerrain(CatalogEntry entry, JObject tile)
            {
                brushJson = tile.ToString();
                terrainPlacementEnabled = true;
                selectedTerrainGroup = GroupKey(entry);
                devicePlacementEnabled = false;
                selectWorkbenchTab?.Invoke(6);
                SetEraseMode(false);
                selection.text = $"当前地形 · {entry.DisplayName}";
                list.RefreshItems();
            }
        }

        private static string GroupKey(CatalogEntry entry) => entry.Family ?? entry.Id;

        private sealed class TerrainCatalogRow : VisualElement
        {
            private readonly Button select;
            private readonly Button info;
            private readonly Label title = new Label();
            private readonly Label subtitle = new Label();
            private CatalogEntry? entry;

            internal TerrainCatalogRow(Texture2D icon, Action<CatalogEntry> choose, Action<CatalogEntry, Button> showDetails)
            {
                AddToClassList("catalog-entry");
                AddToClassList("terrain-entry-row");
                select = new Button(() =>
                {
                    if (entry != null) choose(entry);
                });
                select.AddToClassList("terrain-select");
                title.AddToClassList("catalog-name");
                subtitle.AddToClassList("catalog-id");
                select.Add(title);
                select.Add(subtitle);
                Add(select);

                info = new Button();
                info.AddToClassList("terrain-info-button");
                info.clicked += () =>
                {
                    if (entry != null) showDetails(entry, info);
                };
                if (icon != null)
                {
                    var image = new Image { image = icon, pickingMode = PickingMode.Ignore };
                    info.Add(image);
                }
                else info.text = "ⓘ";
                Add(info);
            }

            internal void Bind(CatalogEntry item, int variantCount, bool canDraw, bool selected)
            {
                select.EnableInClassList("terrain-selected", selected);
                select.name = "terrain-select-" + item.Id;
                entry = item;
                title.text = item.DisplayName;
                subtitle.text = variantCount > 1 ? $"{variantCount} 个变种" : item.Name.Length > 0 ? item.Id : "";
                subtitle.style.display = subtitle.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                select.tooltip = canDraw ? $"选用 {item.Id} 的默认推荐" : "暂无可直接绘制的配置";
                info.name = "terrain-info-" + item.Id;
                info.tooltip = $"查看 {title.text} 的详细信息";
            }
        }

        private sealed class TerrainDetailOverlay : VisualElement
        {
            private readonly Label title = new Label();
            private readonly ScrollView body = new ScrollView();
            private readonly Button close;
            private readonly TerrainSampleCatalog samples;
            private readonly Action<CatalogEntry, JObject> choose;
            private Button? opener;
            private bool sourceCards;
            private bool totalSourceCards;

            internal TerrainDetailOverlay(TerrainSampleCatalog samples, Action<CatalogEntry, JObject> choose)
            {
                this.samples = samples;
                this.choose = choose;
                name = "terrain-detail-overlay";
                AddToClassList("terrain-detail-overlay");
                style.display = DisplayStyle.None;
                var panel = new VisualElement();
                panel.AddToClassList("terrain-detail-panel");
                var header = new VisualElement();
                header.AddToClassList("terrain-detail-header");
                title.AddToClassList("workbench-heading");
                header.Add(title);
                close = new Button(Close) { text = "关闭", name = "terrain-detail-close" };
                header.Add(close);
                panel.Add(header);
                body.AddToClassList("terrain-detail-body");
                panel.Add(body);
                Add(panel);
                RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode != KeyCode.Escape) return;
                    evt.StopPropagation();
                    Close();
                }, TrickleDown.TrickleDown);
            }

            internal void Open(CatalogEntry entry, IReadOnlyList<CatalogEntry> variants, Button trigger)
            {
                opener = trigger;
                style.display = DisplayStyle.Flex;
                ShowDetails(entry, variants);
                close.Focus();
            }

            private void Close()
            {
                style.display = DisplayStyle.None;
                if (opener?.panel != null) opener.Focus();
                opener = null;
            }

            private void ShowDetails(CatalogEntry entry, IReadOnlyList<CatalogEntry> variants)
            {
                title.text = entry.DisplayName;
                body.Clear();
                body.scrollOffset = Vector2.zero;
                AddText("基本信息", true);
                if (entry.Name.Length > 0) AddText($"名称 · {entry.Name}");
                AddText($"地块 ID · {entry.Id}");
                if (entry.VisualOnly) AddText("类型 · 骨架示意项");
                AddText(string.IsNullOrEmpty(entry.Description) ? "说明未收录。" : entry.Description);
                AddVariants(entry, variants);
                BuildSamples(entry);
            }

            private void BuildSamples(CatalogEntry entry)
            {
                var originals = samples.Samples(entry.Id);
                var recommended = samples.Recommended(entry.Id);
                var template = TerrainBlackboardCatalog.Template(entry.Id);
                if (template != null)
                {
                    var generic = new Foldout { text = "查看通用配置 · 预制体默认字段", value = false };
                    var genericJson = new TextField
                    {
                        name = "terrain-generic-tiledata", isReadOnly = true, multiline = true,
                        value = template.ToString(Formatting.Indented)
                    };
                    AddJsonViewport(generic, genericJson);
                    body.Add(generic);
                }
                AddText("默认推荐", true);
                if (originals.Count == 0)
                {
                    AddText("暂无原版配置可作为推荐。");
                    AddText("全部既有已知配置 · 0");
                    AddText("总出处 · 0 个关卡");
                    return;
                }

                var recommendations = new VisualElement();
                recommendations.AddToClassList("tiledata-preview-toolbar");
                body.Add(recommendations);
                var browse = new Foldout
                {
                    name = "terrain-known-configurations",
                    text = $"全部既有已知配置 · {originals.Count}",
                    value = false
                };
                body.Add(browse);
                var preview = new VisualElement();
                body.Add(preview);
                var allPaths = originals.SelectMany(sample => sample.SourcePaths)
                    .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
                var totalSources = new TerrainSourceBrowser(allPaths, totalSourceCards, show => totalSourceCards = show,
                    path => new TerrainSourceMap(path, entry.Id, null, samples.Revision))
                {
                    name = "terrain-total-source-browser",
                    text = $"总出处 · {allPaths.Length} 个关卡",
                    tooltip = "当前地块变体的全部既有已知配置所涉及的关卡，按关卡去重。"
                };
                body.Add(totalSources);
                var initial = originals[recommended.Length == 0 ? 0 : recommended[0]];
                var updatePreview = BuildSamplePreview(preview, entry, initial);
                var recommendationButtons = new List<Button>();
                var markSelectedCard = BuildSampleCards(browse, originals, Show);
                for (var i = 0; i < recommended.Length; i++)
                {
                    var rank = i;
                    var button = new Button(() =>
                    {
                        var sample = originals[recommended[rank]];
                        markSelectedCard(sample.Number);
                        Show(sample);
                    })
                    {
                        name = "terrain-recommendation-" + rank,
                        text = rank == 0 ? "推荐 1 · 默认" : $"推荐 {rank + 1}"
                    };
                    recommendationButtons.Add(button);
                    recommendations.Add(button);
                }

                if (recommended.Length == 0)
                    recommendations.Add(new Label("暂无字段完整的推荐配置。"));
                markSelectedCard(initial.Number);
                Show(initial);
                void Show(TerrainSampleCatalog.Sample sample)
                {
                    for (var i = 0; i < recommendationButtons.Count; i++)
                        recommendationButtons[i].EnableInClassList("selected-tab", sample.Number == recommended[i] + 1);
                    updatePreview(sample);
                }
            }

            private static Action<int> BuildSampleCards(Foldout browse,
                IReadOnlyList<TerrainSampleCatalog.Sample> originals, Action<TerrainSampleCatalog.Sample> show)
            {
                const int pageSize = 8;
                var query = new TextField("搜索配置或关卡") { name = "terrain-sample-search" };
                browse.Add(query);
                var hint = new Label { name = "terrain-sample-search-status" };
                hint.AddToClassList("terrain-source-line");
                browse.Add(hint);

                var viewport = new ScrollView { verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible };
                viewport.AddToClassList("terrain-sample-cards-viewport");
                var cards = new VisualElement { name = "terrain-original-samples" };
                cards.AddToClassList("tiledata-preview-cards");
                viewport.Add(cards);
                browse.Add(viewport);

                var toolbar = new VisualElement();
                toolbar.AddToClassList("tiledata-preview-toolbar");
                var previous = new Button { text = "上一页", name = "terrain-sample-prev" };
                var pageLabel = new Label { name = "terrain-sample-page" };
                var next = new Button { text = "下一页", name = "terrain-sample-next" };
                toolbar.Add(previous);
                toolbar.Add(pageLabel);
                toolbar.Add(next);
                browse.Add(toolbar);

                IReadOnlyList<TerrainSampleCatalog.Sample> results = originals;
                var shownCards = new Dictionary<int, Button>();
                var selected = -1;
                var page = 0;
                var searching = false;
                previous.clicked += () => ChangePage(-1);
                next.clicked += () => ChangePage(1);
                _ = new CatalogSearchInput<TerrainSampleCatalog.Sample>(query, TerrainSearchRuntime.CreateSampleWorker,
                    originals, () =>
                    {
                        searching = true;
                        hint.text = "搜索中…";
                    }, (matches, error) =>
                    {
                        searching = false;
                        results = matches;
                        page = 0;
                        viewport.scrollOffset = Vector2.zero;
                        hint.text = error ?? (matches.Count == 0 ? "未找到匹配配置" : "");
                        RenderPage();
                    });
                RenderPage();
                return MarkSelected;

                void MarkSelected(int number)
                {
                    if (shownCards.TryGetValue(selected, out var oldCard))
                        oldCard.EnableInClassList("selected-tab", false);
                    selected = number;
                    if (shownCards.TryGetValue(selected, out var card))
                        card.EnableInClassList("selected-tab", true);
                }

                void ChangePage(int delta)
                {
                    if (searching) return;
                    page += delta;
                    viewport.scrollOffset = Vector2.zero;
                    RenderPage();
                }

                void RenderPage()
                {
                    var pages = (results.Count + pageSize - 1) / pageSize;
                    cards.Clear();
                    shownCards.Clear();
                    foreach (var sample in results.Skip(page * pageSize).Take(pageSize))
                    {
                        var card = new Button(() =>
                        {
                            if (searching) return;
                            MarkSelected(sample.Number);
                            show(sample);
                        })
                        {
                            name = $"terrain-sample-card-{sample.Number}",
                            tooltip = sample.ExamplePath
                        };
                        card.AddToClassList("terrain-sample-card");
                        card.Add(new Label($"配置 #{sample.Number} · {sample.SourceCount} 个关卡")
                            { pickingMode = PickingMode.Ignore });
                        var example = new Label(System.IO.Path.GetFileNameWithoutExtension(sample.ExamplePath))
                            { pickingMode = PickingMode.Ignore };
                        example.AddToClassList("catalog-id");
                        card.Add(example);
                        card.EnableInClassList("selected-tab", sample.Number == selected);
                        cards.Add(card);
                        shownCards.Add(sample.Number, card);
                    }
                    pageLabel.text = $"{(pages == 0 ? 0 : page + 1)} / {pages} 页 · {results.Count} 套";
                    previous.SetEnabled(page > 0);
                    next.SetEnabled(page + 1 < pages);
                }
            }

            private Action<TerrainSampleCatalog.Sample> BuildSamplePreview(VisualElement preview, CatalogEntry entry,
                TerrainSampleCatalog.Sample initial)
            {
                var selected = initial;
                var source = new Label { name = "terrain-current-source-count" };
                source.AddToClassList("terrain-source-line");
                preview.Add(source);
                var example = new Label { name = "terrain-current-source-example" };
                example.AddToClassList("terrain-source-line");
                preview.Add(example);
                var json = new TextField
                {
                    name = "terrain-detail-tiledata",
                    isReadOnly = true,
                    multiline = true
                };
                AddJsonViewport(preview, json).name = "terrain-current-config-json-viewport";
                preview.Add(new Button(() =>
                {
                    choose(entry, selected.CopyTile());
                    Close();
                }) { text = "使用这套配置", name = "terrain-use-sample" });
                var sources = new TerrainSourceBrowser(initial.SourcePaths, sourceCards, show => sourceCards = show,
                    path => new TerrainSourceMap(path, entry.Id, selected.Number, samples.Revision));
                preview.Add(sources);
                RefreshText();
                return sample =>
                {
                    if (ReferenceEquals(sample, selected)) return;
                    selected = sample;
                    RefreshText();
                    sources.SetSources(sample.SourcePaths);
                };

                void RefreshText()
                {
                    source.text = $"当前配置 #{selected.Number} 的出处 · {selected.SourceCount} 个关卡";
                    example.text = $"{selected.ExamplePath} · tiles[{selected.ExampleIndex}]";
                    example.tooltip = example.text;
                    json.SetValueWithoutNotify(selected.CopyTile().ToString(Formatting.Indented));
                }
            }

            private static ScrollView AddJsonViewport(VisualElement parent, TextField field)
            {
                var viewport = new ScrollView { verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible };
                viewport.AddToClassList("terrain-detail-json-viewport");
                field.AddToClassList("terrain-detail-json");
                viewport.Add(field);
                parent.Add(viewport);
                return viewport;
            }

            private void AddVariants(CatalogEntry entry, IReadOnlyList<CatalogEntry> variants)
            {
                AddText("变种", true);
                if (variants.Count < 2) { AddText("暂无已归组变种。"); return; }
                var options = new VisualElement();
                options.AddToClassList("tiledata-preview-toolbar");
                foreach (var variant in variants)
                {
                    var button = new Button(() =>
                    {
                        ShowDetails(variant, variants);
                        body.Q<Button>("terrain-variant-" + variant.Id)?.Focus();
                    })
                    {
                        name = "terrain-variant-" + variant.Id,
                        tooltip = variant.Id
                    };
                    button.Add(new Label(variant.DisplayName) { pickingMode = PickingMode.Ignore });
                    if (variant.Name.Length > 0)
                    {
                        var id = new Label(variant.Id) { pickingMode = PickingMode.Ignore };
                        id.AddToClassList("catalog-id");
                        button.Add(id);
                    }
                    button.EnableInClassList("selected-tab", variant.Id == entry.Id);
                    options.Add(button);
                }
                body.Add(options);
            }

            private void AddText(string value, bool heading = false)
            {
                var label = new Label(value);
                label.AddToClassList(heading ? "terrain-detail-section" : "terrain-detail-text");
                body.Add(label);
            }
        }
    }
}
