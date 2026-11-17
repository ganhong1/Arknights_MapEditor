#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class TerrainSourceBrowser : Foldout
    {
        private const int PageSize = 24;
        private string[] paths;
        private readonly CatalogSearchInput<string> search;
        private string? selectedPath;
        private readonly List<string> matches = new List<string>();
        private readonly ListView list;
        private readonly VisualElement cards = new VisualElement { name = "terrain-source-cards" };
        private readonly ScrollView cardScroll = new ScrollView();
        private readonly Label status = new Label { name = "terrain-source-status" };
        private readonly Label pageStatus = new Label();
        private readonly VisualElement pagination = new VisualElement();
        private readonly Func<string, VisualElement>? buildPreview;
        private readonly VisualElement sourcePreview = new VisualElement { name = "terrain-source-preview-slot" };
        private readonly Button previous;
        private readonly Button next;
        private readonly Button listButton;
        private readonly Button cardsButton;
        private readonly Action<bool> rememberCards;
        private bool cardView;
        private bool resultsPending;
        private int page;

        internal TerrainSourceBrowser(IEnumerable<string> sourcePaths, bool showCards, Action<bool> remember,
            Func<string, VisualElement>? buildPreview = null, bool wrapRows = false)
        {
            name = "terrain-source-browser";
            text = "查看当前配置的关卡出处";
            this.buildPreview = buildPreview;
            value = false;
            paths = sourcePaths.ToArray();
            rememberCards = remember;
            var query = new TextField("搜索关卡") { name = "terrain-source-search" };
            query.tooltip = "按关卡代号、名称、拼音或关卡 ID 搜索";
            Add(query);
            var toolbar = Toolbar();
            listButton = new Button(() => SetView(false)) { text = "列表", name = "terrain-source-show-list" };
            cardsButton = new Button(() => SetView(true)) { text = "块状", name = "terrain-source-show-cards" };
            toolbar.Add(listButton);
            toolbar.Add(cardsButton);
            Add(toolbar);
            status.AddToClassList("terrain-source-line");
            Add(status);
            list = new ListView(matches, 24, () =>
            {
                var row = new Label();
                if (wrapRows) row.AddToClassList("terrain-source-wrapped-row");
                return row;
            }, (element, index) =>
            {
                var path = matches[index];
                var title = TerrainSearchRuntime.StageCaption(path);
                ((Label)element).text = title;
                element.tooltip = title;
            }) { name = "terrain-source-list", selectionType = buildPreview == null ? SelectionType.None : SelectionType.Single };
            if (wrapRows) list.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            list.onSelectionChange += selected =>
            {
                if (selected.FirstOrDefault() is string path) ShowPreview(path);
            };
            list.AddToClassList("terrain-source-results");
            Add(list);
            cards.AddToClassList("tiledata-preview-cards");
            cardScroll.AddToClassList("terrain-source-results");
            cardScroll.Add(cards);
            Add(cardScroll);
            pagination.AddToClassList("tiledata-preview-toolbar");
            previous = new Button(() => ChangePage(-1)) { text = "上一页", name = "terrain-source-prev" };
            next = new Button(() => ChangePage(1)) { text = "下一页", name = "terrain-source-next" };
            pagination.Add(previous);
            pagination.Add(pageStatus);
            pagination.Add(next);
            Add(pagination);
            sourcePreview.AddToClassList("terrain-source-preview-slot");
            sourcePreview.style.display = DisplayStyle.None;
            Add(sourcePreview);
            search = new CatalogSearchInput<string>(query, TerrainSearchRuntime.CreateSourceWorker, paths,
                ShowSearchPending, ShowResults);
            cardView = showCards;
            ShowResults(paths, null);
            SetView(showCards);
        }

        internal void SetSources(IEnumerable<string> sourcePaths)
        {
            paths = sourcePaths.ToArray();
            if (selectedPath != null)
            {
                if (Array.IndexOf(paths, selectedPath) >= 0) ShowPreview(selectedPath, true);
                else
                {
                    selectedPath = null;
                    sourcePreview.Clear();
                    sourcePreview.Add(new Label("选择关卡以预览当前配置的分布。"));
                }
            }
            search.SetItems(paths);
        }

        private static VisualElement Toolbar()
        {
            var toolbar = new VisualElement();
            toolbar.AddToClassList("tiledata-preview-toolbar");
            return toolbar;
        }

        private void SetView(bool showCards)
        {
            cardView = showCards;
            rememberCards(showCards);
            list.style.display = showCards ? DisplayStyle.None : DisplayStyle.Flex;
            cardScroll.style.display = showCards ? DisplayStyle.Flex : DisplayStyle.None;
            listButton.EnableInClassList("selected-tab", !showCards);
            cardsButton.EnableInClassList("selected-tab", showCards);
            if (showCards) RenderCards();
            else pagination.style.display = DisplayStyle.None;
        }

        private void ShowSearchPending()
        {
            status.text = "搜索中…";
            resultsPending = true;
        }

        private void ShowResults(IEnumerable<string> results, string? error)
        {
            matches.Clear();
            matches.AddRange(results);
            status.text = error ?? (matches.Count == 0 ? "未找到匹配关卡" : $"{matches.Count} / {paths.Length} 个关卡");
            var selected = selectedPath == null ? -1 : matches.IndexOf(selectedPath);
            list.SetSelectionWithoutNotify(selected >= 0 ? new[] { selected } : Array.Empty<int>());
            list.RefreshItems();
            resultsPending = false;
            if (cardView) RenderCards();
        }

        private void ShowPreview(string path, bool refresh = false)
        {
            if (buildPreview == null || (!refresh && (resultsPending || path == selectedPath))) return;
            selectedPath = path;
            sourcePreview.style.display = DisplayStyle.Flex;
            sourcePreview.Clear();
            try { sourcePreview.Add(buildPreview(path)); }
            catch (Exception error) when (error is FormatException || error is Newtonsoft.Json.JsonException)
            {
                sourcePreview.Add(new HelpBox(error.Message, HelpBoxMessageType.Warning));
            }
        }

        private void ChangePage(int delta)
        {
            if (resultsPending) return;
            page += delta;
            cardScroll.scrollOffset = Vector2.zero;
            RenderCards();
        }

        private void RenderCards()
        {
            var pageCount = (matches.Count + PageSize - 1) / PageSize;
            page = Mathf.Clamp(page, 0, Math.Max(0, pageCount - 1));
            cards.Clear();
            foreach (var path in matches.Skip(page * PageSize).Take(PageSize))
            {
                var title = TerrainSearchRuntime.StageCaption(path);
                var card = new Button(() => ShowPreview(path)) { tooltip = title };
                card.SetEnabled(buildPreview != null);
                card.AddToClassList("terrain-source-card");
                card.Add(new Label(title));
                cards.Add(card);
            }
            pagination.style.display = DisplayStyle.Flex;
            pageStatus.text = pageCount == 0 ? "0 / 0 页" : $"{page + 1} / {pageCount} 页";
            previous.SetEnabled(page > 0);
            next.SetEnabled(page + 1 < pageCount);
        }
    }
}
