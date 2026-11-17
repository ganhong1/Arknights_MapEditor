#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        internal static void BuildEnemyPanelSafely(VisualElement panel, string operation, Action build)
        {
            try { build(); }
            catch (Exception error)
            {
                PluginLog.Failure(operation, error);
                panel.Clear();
                panel.Add(new HelpBox("敌人数据暂不可用，请查看插件日志。", HelpBoxMessageType.Warning));
            }
        }

        private void BuildEnemyLibrary(VisualElement parent, VisualElement workbench)
        {
            var catalog = EnemyCatalog.LoadBundled();
            var portraits = EnemyPortraitCatalog.LoadOptional();
            var searchRow = new VisualElement();
            searchRow.AddToClassList("operator-library-search-row");
            var search = new TextField("搜索")
            {
                name = "enemy-library-search",
                tooltip = "搜索名称、内部 ID、图鉴编号、描述、能力或术语解释"
            };
            search.AddToClassList("catalog-search");
            search.labelElement.style.minWidth = 36;
            var tagsButton = new Button { name = "enemy-tag-filter-toggle" };
            searchRow.Add(search);
            searchRow.Add(tagsButton);
            parent.Add(searchRow);

            var tags = new EnemyTagFilter();
            var samples = new Toggle("仅显示有原版配置") { name = "enemy-sample-filter" };
            var count = new Label { name = "enemy-library-count" };
            count.AddToClassList("catalog-id");
            var detail = new EnemyDetail(catalog, portraits, record => prepareEnemyReference?.Invoke(record) ?? false,
                record => prepareInlineEnemy?.Invoke(record) ?? false);
            workbench.Add(detail);
            var infoIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(PrtsObjectView.CatalogRoot + "terrain-info.png");
            var view = new EnemyLibraryResults(portraits, infoIcon, Select,
                (entry, button) => detail.Open(entry, button, entry.Filters.Profiles.FirstOrDefault(tags.Matches)),
                () => selectedEnemyId);
            IReadOnlyList<EnemyCatalog.Entry> matches = catalog.Entries;
            string? searchError = null;
            bool searching = false;

            void Refresh()
            {
                var filtered = matches.Where(entry =>
                    (!samples.value || entry.ReferenceSamples + entry.InlineSamples > 0) &&
                    tags.Matches(entry)).ToArray();
                view.SetEntries(filtered, !string.IsNullOrWhiteSpace(search.value));
                count.text = searching ? "搜索中…" : searchError ?? $"{filtered.Length} 项敌人";
            }

            void Select(EnemyCatalog.Entry entry)
            {
                try { prepareEnemyReference?.Invoke(BlankEnemyReference(entry.Id, entry.Levels.Count > 0 ? entry.Levels[0] : 0)); }
                catch (Exception error) { if (status != null) status.text = error.Message; }
            }

            parent.Add(new EnemyTagFilterPanel(tags, tagsButton, Refresh));
            parent.Add(samples);
            parent.Add(count);
            parent.Add(view);
            refreshEnemySelection = view.RefreshSelection;
            _ = new CatalogSearchInput<EnemyCatalog.Entry>(search, catalog.CreateSearch, catalog.Entries,
                () => { searching = true; count.text = "搜索中…"; },
                (entries, error) => { matches = entries; searchError = error; searching = false; Refresh(); });
            samples.RegisterValueChangedCallback(_ => Refresh());
            Refresh();
        }
    }
}
