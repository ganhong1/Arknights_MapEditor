#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class EnemyDetail : VisualElement
    {
        private static readonly Regex NativeTextTags = new(@"<(?:[@$][A-Za-z0-9_.-]+|/)>", RegexOptions.CultureInvariant);
        private readonly EnemyCatalog catalog;
        private readonly EnemyPortraitCatalog portraits;
        private readonly Func<JObject, bool> choose;
        private readonly Func<JObject, bool>? chooseInline;
        private readonly Label heading = new();
        private readonly ScrollView body = new();
        private readonly VisualElement portrait = new();
        private Button? opener;
        internal EnemyDetail(EnemyCatalog catalog, EnemyPortraitCatalog portraits, Func<JObject, bool> choose,
            Func<JObject, bool>? chooseInline = null)
        {
            this.catalog = catalog; this.portraits = portraits; this.choose = choose;
            this.chooseInline = chooseInline;
            name = "enemy-detail-overlay";
            AddToClassList("terrain-detail-overlay"); style.display = DisplayStyle.None;
            var panel = new VisualElement(); panel.AddToClassList("terrain-detail-panel"); Add(panel);
            var header = new VisualElement(); header.AddToClassList("terrain-detail-header"); panel.Add(header);
            header.Add(heading); header.Add(new Button(Close) { text = "关闭", name = "enemy-detail-close" });
            body.AddToClassList("terrain-detail-body"); panel.Add(body);
            RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape) { Close(); e.StopPropagation(); } });
        }
        private void Close() { style.display = DisplayStyle.None; opener?.Focus(); }
        internal void Open(EnemyCatalog.Entry entry, Button button, EnemyFilterCatalog.Profile? profile = null)
        {
            opener = button; body.Clear(); body.scrollOffset = Vector2.zero; heading.text = entry.Name + " · " + entry.Id;
            style.display = DisplayStyle.Flex; BringToFront();
            var details = catalog.Details(entry);
            body.Add(portrait);
            ShowPortrait(portrait, entry.Id);
            if (entry.Filters.Number.Length > 0) body.Add(new Label("图鉴编号 · " + entry.Filters.Number));
            profile ??= entry.Filters.Profiles.FirstOrDefault();
            body.Add(AttributeSection("基本属性", profile, EnemyTagFilter.Basic.Concat(EnemyTagFilter.More.Take(1))));
            body.Add(AttributeSection(entry.HasDatabase ? "属性评级 · Lv.0" : "属性评级", profile,
                EnemyTagFilter.Ratings.Concat(EnemyTagFilter.More.Skip(1))));
            if (!string.IsNullOrWhiteSpace(entry.Description))
                body.Add(BodyText(entry.Description, "enemy-detail-description"));
            if (entry.Filters.Abilities.Length > 0)
            {
                body.Add(new Label("能力") { name = "enemy-detail-ability-heading" });
                body.Add(BodyText(entry.Filters.Abilities, "enemy-detail-ability"));
            }
            var levels = new VisualElement(); body.Add(new Label("数据库等级")); body.Add(levels);
            foreach (var level in entry.Levels)
            {
                var value = level;
                levels.Add(new Button(() => Use(new JObject { ["useDb"] = true, ["id"] = entry.Id, ["level"] = value, ["overwrittenData"] = null })) { text = "引用 Lv." + value });
                if (chooseInline != null && details["databaseLevels"] is JArray databaseLevels)
                    levels.Add(new Button(() => UseInlineLevel(entry, databaseLevels, value))
                        { text = "复制 Lv." + value + " 为内联", name = "enemy-copy-db-inline-" + value });
            }
            var references = details["references"] as JArray ?? new JArray();
            var database = details["databaseLevels"] as JArray;
            var preview = new VisualElement();
            var all = new Foldout { text = "全部既有已知配置 · " + references.Count, value = false };
            var grid = new VisualElement(); grid.AddToClassList("tiledata-preview-cards"); all.Add(grid);
            var recommendations = new VisualElement(); body.Add(new Label("默认推荐")); body.Add(recommendations);
            void Select(int index)
            {
                if (index < 0 || index >= references.Count) return;
                preview.Clear(); var sample = (JObject)references[index]!; var record = (JObject)sample["record"]!;
                ShowPortrait(portrait, entry.Id, ReferencePrefab(record, database));
                preview.Add(new Label("配置 #" + (index + 1)));
                preview.Add(new TextField { multiline = true, isReadOnly = true, value = record.ToString(Formatting.Indented) });
                preview.Add(new Button(() => Use(record)) { text = "选用此配置" });
                preview.Add(Sources("当前配置出处", sample["origins"] as JArray ?? new JArray()));
            }
            for (var i = 0; i < references.Count; i++)
            {
                var index = i; var record = references[i]?["record"];
                var card = new Button(() => Select(index)) { name = "enemy-config-" + index, text = $"#{index + 1}\n等级 {record?["level"]}\n{((bool?)record?["useDb"] == false ? "独立定义" : "数据库引用")}" };
                card.AddToClassList("tiledata-preview-card"); grid.Add(card);
            }
            foreach (var item in details["recommendations"] as JArray ?? new JArray())
            { var index = (int)item; recommendations.Add(new Button(() => Select(index)) { text = "配置 #" + (index + 1) }); }
            body.Add(all); body.Add(preview);
            var inline = details["inline"] as JArray ?? new JArray();
            var inlineFold = new Foldout { text = "原版内联定义 · " + inline.Count, value = false }; body.Add(inlineFold);
            foreach (var token in inline.OfType<JObject>())
            {
                var sample = token;
                var fold = new Foldout { text = "内联 EnemyData", value = false }; inlineFold.Add(fold);
                var inlinePortrait = new VisualElement();
                fold.Add(inlinePortrait);
                fold.RegisterValueChangedCallback(evt =>
                {
                    if (evt.target == fold && evt.newValue)
                        ShowPortrait(inlinePortrait, entry.Id, (string?)sample["record"]?["key"]);
                });
                fold.Add(new TextField { multiline = true, isReadOnly = true, value = sample["record"]?.ToString(Formatting.Indented) ?? "null" });
                if (chooseInline != null && sample["record"] is JObject record)
                    fold.Add(new Button(() => UseInline(record)) { text = "复制为内联草稿" });
                fold.Add(Sources("出处", sample["origins"] as JArray ?? new JArray()));
            }
            var origins = new JArray(references.Concat(inline).OfType<JObject>().SelectMany(sample => sample["origins"] as JArray ?? new JArray()).Select(value => value.DeepClone()));
            body.Add(Sources("总出处", origins));
            if (references.Count > 0) Select(0);
        }

        private static Label BodyText(string text, string name)
        {
            var label = new Label(NativeTextTags.Replace(text, "")) { name = name, enableRichText = false };
            label.AddToClassList("terrain-detail-text");
            return label;
        }

        private static VisualElement AttributeSection(string title, EnemyFilterCatalog.Profile? profile,
            IEnumerable<(string Key, string Title, string[] Options)> fields)
        {
            var section = new VisualElement();
            section.AddToClassList("enemy-detail-attributes");
            var heading = new Label(title);
            heading.AddToClassList("catalog-name");
            section.Add(heading);
            var rows = new VisualElement();
            rows.AddToClassList("enemy-detail-attribute-rows");
            section.Add(rows);
            foreach (var (key, label, _) in fields)
            {
                var row = new VisualElement();
                row.AddToClassList("enemy-detail-attribute");
                var keyLabel = new Label(label);
                keyLabel.AddToClassList("enemy-detail-attribute-name");
                row.Add(keyLabel);
                var values = profile?.Values(key);
                var value = new Label(values?.Count > 0 ? string.Join("／", values) : "—") { name = "enemy-detail-" + key };
                value.AddToClassList("enemy-detail-attribute-value");
                row.Add(value);
                rows.Add(row);
            }
            return section;
        }

        private void ShowPortrait(VisualElement target, string id, string? prefabKey = null)
        {
            target.Clear();
            var picture = portraits.Find(id, prefabKey);
            var texture = picture == null ? null : portraits.Texture(picture);
            var source = texture == null ? "该配置暂无已核验头像" : picture!.SourceLabel;
            var asset = picture == null ? prefabKey : picture.Asset + "\n" + picture.Bundle + "\n2.7.81 · " + EnemyCatalog.Revision;
            target.Add(LibraryPortrait.Detail(texture, picture?.Uv ?? default, source, asset, picture?.LightBackground == true));
        }

        private static string? ReferencePrefab(JObject record, JArray? database)
        {
            // Read the same composed prefab key used by the editor's enemy definition preview.
            try { return (string?)EnemyDefinitionResolver.ResolveReference(record, database)["key"]; }
            catch (InvalidOperationException)
            {
                return (bool?)record["overwrittenData"]?["prefabKey"]?["m_defined"] == true
                    ? (string?)record["overwrittenData"]?["prefabKey"]?["m_value"] : null;
            }
        }
        private VisualElement Sources(string title, JArray origins)
        {
            var labels = origins.OfType<JObject>().SelectMany(catalog.SourceLabels).Distinct().ToArray();
            var browser = new TerrainSourceBrowser(labels, false, _ => { }, wrapRows: true);
            browser.text = title + " · " + labels.Length;
            return browser;
        }
        private void Use(JObject record) { if (choose((JObject)record.DeepClone())) Close(); }
        private void UseInline(JObject record) { if (chooseInline?.Invoke((JObject)record.DeepClone()) == true) Close(); }
        private void UseInlineLevel(EnemyCatalog.Entry entry, JArray databaseLevels, int level)
        {
            try { UseInline(EnemyDefinitionResolver.ResolveDatabaseLevel(entry.Id, databaseLevels, level)); }
            catch (InvalidOperationException error) { body.Add(new HelpBox(error.Message, HelpBoxMessageType.Error)); }
        }
    }
}
