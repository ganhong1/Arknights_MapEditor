#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class DeviceDetailOverlay : VisualElement
    {
        private readonly DeviceSampleCatalog catalog;
        private readonly Func<string, JObject, bool> choose;
        private readonly Label title = new Label();
        private readonly ScrollView body = new ScrollView();
        private readonly Button close;
        private Button? opener;

        internal DeviceDetailOverlay(DeviceSampleCatalog catalog, Func<string, JObject, bool> choose)
        {
            this.catalog = catalog;
            this.choose = choose;
            name = "device-detail-overlay";
            AddToClassList("terrain-detail-overlay");
            style.display = DisplayStyle.None;
            var panel = new VisualElement();
            panel.AddToClassList("terrain-detail-panel");
            var header = Toolbar();
            header.AddToClassList("terrain-detail-header");
            title.AddToClassList("workbench-heading");
            header.Add(title);
            close = new Button(Close) { text = "关闭", name = "device-detail-close" };
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

        internal void Open(DeviceSampleCatalog.Entry entry, Button trigger)
        {
            opener = trigger;
            title.text = entry.Name;
            body.Clear();
            body.scrollOffset = Vector2.zero;
            Text(body, entry.Id, "terrain-detail-text");
            PrtsObjectView.TryGetIcon(entry.Id, out var texture, out var uv);
            body.Add(LibraryPortrait.Detail(texture, uv, PrtsObjectView.VisualSource(entry.Id),
                PrtsObjectView.IconSourceAsset(entry.Id)));
            if (entry.Description.Length > 0) Text(body, entry.Description, "terrain-detail-text");
            if (entry.Prefab.Length > 0) Text(body, entry.Prefab, "terrain-detail-text");
            if (entry.ModeConfigurations is { Count: > 0 } mode)
            {
                var configurations = new Foldout { text = "卫戍协议原版玩法配置", name = "device-mode-configurations", value = false };
                foreach (var record in mode.OfType<JObject>())
                {
                    var item = new Foldout { text = record.Value<string>("label"), value = false };
                    Text(item, record.Value<string>("source") ?? "", "terrain-detail-text");
                    AddJson(item, new TextField { isReadOnly = true, multiline = true, value = record["data"]!.ToString() });
                    configurations.Add(item);
                }
                body.Add(configurations);
            }
            if (entry.Definition is JObject definition)
            {
                var source = new Foldout { text = "原版装置定义", name = "device-static-definition", value = false };
                AddJson(source, new TextField { isReadOnly = true, multiline = true, value = definition.ToString() });
                body.Add(source);
            }
            try
            {
                var details = catalog.Load(entry.Id);
                var tabs = Toolbar();
                body.Add(tabs);
                var pages = new List<VisualElement>();
                var buttons = new List<Button>();
                var offsets = new Vector2[2];
                var selected = -1;
                void Select(int index)
                {
                    if (selected >= 0) offsets[selected] = body.scrollOffset;
                    selected = index;
                    for (var i = 0; i < pages.Count; i++)
                    {
                        pages[i].style.display = i == index ? DisplayStyle.Flex : DisplayStyle.None;
                        buttons[i].EnableInClassList("selected-tab", i == index);
                    }
                    body.scrollOffset = offsets[index];
                }
                for (var i = 0; i < DeviceSampleCatalog.Roles.Length; i++)
                {
                    var index = i;
                    var role = DeviceSampleCatalog.Roles[i];
                    var button = new Button(() => Select(index))
                    { text = i == 0 ? "场上预设" : "卡牌配置", name = "device-role-tab-" + role };
                    buttons.Add(button);
                    tabs.Add(button);
                    var page = new VisualElement { name = "device-role-" + role };
                    pages.Add(page);
                    body.Add(page);
                    BuildRole(page, details, role);
                }
                Select(details.Samples("tokenInsts").Count > 0 ? 0 : 1);
            }
            catch (Exception error) when (error is FormatException || error is IOException || error is JsonException)
            {
                body.Add(new HelpBox("装置配置无法读取：" + error.Message, HelpBoxMessageType.Error));
            }
            style.display = DisplayStyle.Flex;
            close.Focus();
        }

        private void BuildRole(VisualElement page, DeviceSampleCatalog.Details details, string role)
        {
            var samples = details.Samples(role);
            Text(page, "默认推荐", "terrain-detail-section");
            if (samples.Count == 0)
            {
                Text(page, "暂无这一使用形式的原版配置。", "terrain-detail-text");
                Text(page, "全部既有已知配置 · 0", "terrain-detail-section");
                Text(page, "当前配置出处 · 0 个关卡", "terrain-detail-text");
                Text(page, "总出处 · 0 个关卡", "terrain-detail-text");
                return;
            }
            if (role == "tokenInsts") Text(page, "坐标和朝向在地图放置时绑定；关卡出处保留原版摆放值。", "terrain-detail-text");
            var recommendations = Toolbar();
            page.Add(recommendations);
            var known = new Foldout { text = $"全部既有已知配置 · {samples.Count} 份配置",
                                      name = "device-known-configurations", value = false };
            page.Add(known);
            var selected = samples[details.Recommended(role).FirstOrDefault()];
            var updatePreview = BuildPreview(page, role, selected);
            var marks = new List<(Button Button, int Number)>();
            Action<int>? markCard = null;
            void Show(DeviceSampleCatalog.Sample sample)
            {
                foreach (var item in marks) item.Button.EnableInClassList("selected-tab", item.Number == sample.Number);
                markCard?.Invoke(sample.Number);
                updatePreview(sample);
            }
            foreach (var index in details.Recommended(role))
            {
                var sample = samples[index];
                var button = new Button(() => Show(sample))
                { text = marks.Count == 0 ? "推荐 1 · 默认" : $"推荐 {marks.Count + 1}", name = "device-recommendation-" + marks.Count,
                  tooltip = "原版样例推荐；保留原始参数，实际使用条件由具体机制决定。" };
                marks.Add((button, sample.Number));
                recommendations.Add(button);
            }
            markCard = BuildCards(known, samples, Show);
            var allPaths = samples.SelectMany(x => x.SourcePaths).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var total = new TerrainSourceBrowser(allPaths, false, _ => { }, path => SourcePreview(path,
                samples.SelectMany(x => x.Originals).Where(x => x.SourcePaths.Contains(path))))
            { name = "device-total-source-browser", text = $"总出处 · {allPaths.Length} 个关卡" };
            page.Add(total);
            Show(selected);
        }

        private Action<DeviceSampleCatalog.Sample> BuildPreview(VisualElement page, string role, DeviceSampleCatalog.Sample initial)
        {
            var sample = initial;
            var caption = Text(page, "", "tiledata-preview-important");
            var count = Text(page, "", "terrain-source-line");
            var json = new TextField { name = "device-current-json", isReadOnly = true, multiline = true };
            AddJson(page, json);
            var message = Text(page, "", "terrain-detail-text");
            var use = new Button(() =>
            {
                try { if (choose(role, sample.CopyRecord())) Close(); }
                catch (Exception error) when (error is InvalidOperationException || error is FormatException)
                { message.text = error.Message; }
            }) { text = "使用这套配置", name = "device-use-configuration" };
            page.Add(use);
            var sources = new TerrainSourceBrowser(sample.SourcePaths, false, _ => { }, path => SourcePreview(path, sample.Originals.Where(x => x.SourcePaths.Contains(path))))
            { name = "device-current-source-browser", text = "查看当前配置的关卡出处" };
            page.Add(sources);
            return next =>
            {
                sample = next;
                caption.text = $"配置 #{sample.Number} · {sample.SourcePaths.Count} 个关卡";
                RefreshOriginal();
            };

            void RefreshOriginal()
            {
                json.SetValueWithoutNotify(sample.CopyRecord().ToString(Formatting.Indented));
                count.text = $"当前配置出处 · {sample.SourcePaths.Count} 个关卡";
                sources.SetSources(sample.SourcePaths);
                message.text = "";
            }
        }

        private static Action<int> BuildCards(Foldout parent, IReadOnlyList<DeviceSampleCatalog.Sample> samples,
            Action<DeviceSampleCatalog.Sample> show)
        {
            const int pageSize = 8;
            var query = new TextField("搜索配置或关卡") { name = "device-config-search" };
            parent.Add(query);
            var status = Text(parent, "", "terrain-source-line");
            var viewport = new ScrollView();
            viewport.AddToClassList("terrain-sample-cards-viewport");
            var cards = new VisualElement { name = "device-config-cards" };
            cards.AddToClassList("tiledata-preview-cards");
            viewport.Add(cards);
            parent.Add(viewport);
            var toolbar = Toolbar();
            var previous = new Button { text = "上一页" };
            var caption = new Label();
            var next = new Button { text = "下一页" };
            toolbar.Add(previous); toolbar.Add(caption); toolbar.Add(next);
            parent.Add(toolbar);
            IReadOnlyList<DeviceSampleCatalog.Sample> results = samples;
            var page = 0;
            var selected = -1;
            var pending = false;
            var buttons = new List<(Button Button, int Number)>();
            void Mark(int number)
            {
                selected = number;
                foreach (var item in buttons) item.Button.EnableInClassList("selected-tab", item.Number == number);
            }
            void Render()
            {
                var pages = (results.Count + pageSize - 1) / pageSize;
                page = Mathf.Clamp(page, 0, Math.Max(0, pages - 1));
                cards.Clear(); buttons.Clear();
                foreach (var sample in results.Skip(page * pageSize).Take(pageSize))
                {
                    var button = new Button(() => { if (!pending) { Mark(sample.Number); show(sample); } })
                    { name = "device-sample-card-" + sample.Number, tooltip = sample.SourcePaths[0] };
                    button.AddToClassList("terrain-sample-card");
                    button.Add(new Label($"配置 #{sample.Number} · {sample.SourcePaths.Count} 个关卡") { pickingMode = PickingMode.Ignore });
                    button.EnableInClassList("selected-tab", sample.Number == selected);
                    cards.Add(button); buttons.Add((button, sample.Number));
                }
                caption.text = $"{(pages == 0 ? 0 : page + 1)} / {pages} 页 · {results.Count} 份配置";
                previous.SetEnabled(!pending && page > 0); next.SetEnabled(!pending && page + 1 < pages);
            }
            previous.clicked += () => { if (!pending) { page--; Render(); } };
            next.clicked += () => { if (!pending) { page++; Render(); } };
            _ = new CatalogSearchInput<DeviceSampleCatalog.Sample>(query,
                () => CatalogSearchRuntime.CreateWorker<DeviceSampleCatalog.Sample>(item => new JObject
                { ["name"] = "配置 " + item.Number, ["targetFile"] = new JArray(item.SourcePaths),
                  ["description"] = item.CopyRecord().ToString(Formatting.None) }), samples,
                () => { pending = true; status.text = "搜索中…"; },
                (matches, error) => { pending = false; results = matches; page = 0; status.text = error ?? ""; Render(); });
            Render();
            return Mark;
        }

        private static VisualElement SourcePreview(string path, IEnumerable<DeviceSampleCatalog.Original> originals)
        {
            var panel = new ScrollView();
            Text(panel, TerrainSearchRuntime.StageCaption(path), "tiledata-preview-important");
            foreach (var original in originals)
            {
                Text(panel, string.Join("、", original.Pointers(path)), "tiledata-preview-important");
                AddJson(panel, new TextField { isReadOnly = true, multiline = true,
                    value = original.CopyRecord().ToString(Formatting.Indented) });
            }
            return panel;
        }

        private static void AddJson(VisualElement parent, TextField field)
        {
            var viewport = new ScrollView();
            viewport.AddToClassList("terrain-detail-json-viewport");
            field.AddToClassList("terrain-detail-json");
            viewport.Add(field);
            parent.Add(viewport);
        }

        private static Label Text(VisualElement parent, string value, string style)
        {
            var label = new Label(value);
            label.AddToClassList(style);
            parent.Add(label);
            return label;
        }

        private static VisualElement Toolbar()
        {
            var row = new VisualElement();
            row.AddToClassList("tiledata-preview-toolbar");
            return row;
        }

        private void Close()
        {
            style.display = DisplayStyle.None;
            if (opener?.panel != null) opener.Focus();
            opener = null;
        }
    }
}
