#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Arknights.MapEditor.Catalog;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class EnemyLibraryResults : VisualElement
    {
        private const int CardWidth = 68;
        private readonly ListView list;
        private readonly ListView gallery;
        private readonly Button tableButton;
        private readonly Button galleryButton;
        private readonly PopupField<string> pageSize;
        private readonly Button previousPage;
        private readonly Button nextPage;
        private readonly Label pageStatus;
        private readonly List<EnemyCatalog.Entry> pageEntries = new();
        private readonly List<int> gridRows = new();
        private IReadOnlyList<EnemyCatalog.Entry> ordered = Array.Empty<EnemyCatalog.Entry>();
        private int page;
        private int cardsPerRow = 1;
        private bool iconMode;

        internal EnemyLibraryResults(EnemyPortraitCatalog portraits, Texture2D? infoIcon,
            Action<EnemyCatalog.Entry> choose, Action<EnemyCatalog.Entry, Button> open, Func<string> selected)
        {
            AddToClassList("enemy-library-results");
            var toolbar = new VisualElement();
            toolbar.AddToClassList("enemy-library-toolbar");
            tableButton = new Button(() => SwitchView(false)) { text = "列表", name = "enemy-view-table" };
            galleryButton = new Button(() => SwitchView(true)) { text = "头像", name = "enemy-view-avatars" };
            toolbar.Add(tableButton);
            toolbar.Add(galleryButton);
            Add(toolbar);

            list = new ListView(pageEntries, 50,
                () => new EnemyLibraryRow(portraits, infoIcon, choose, open),
                (element, index) => ((EnemyLibraryRow)element).Bind(pageEntries[index], selected()))
                { name = "enemy-library", selectionType = SelectionType.None };
            list.AddToClassList("catalog-list");
            Add(list);

            gallery = new ListView(gridRows, 76, () => new VisualElement(),
                (element, row) => BindGallery(element, row, portraits, infoIcon, selected, choose, open))
                { name = "enemy-avatar-grid", selectionType = SelectionType.None };
            gallery.AddToClassList("catalog-list");
            gallery.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                var next = Mathf.Max(1, Mathf.FloorToInt((evt.newRect.width - 18) / CardWidth));
                if (cardsPerRow == next) return;
                cardsPerRow = next;
                UpdateGridRows();
            });
            Add(gallery);

            var pagination = new VisualElement();
            pagination.AddToClassList("enemy-library-toolbar");
            pagination.Add(new Label("每页"));
            pageSize = new PopupField<string>(new List<string> { "50", "100", "200", "500" }, 0) { name = "enemy-page-size" };
            pageSize.RegisterValueChangedCallback(_ => ShowPage(0));
            pagination.Add(pageSize);
            previousPage = new Button(() => ShowPage(page - 1)) { text = "上一页", name = "enemy-page-prev" };
            nextPage = new Button(() => ShowPage(page + 1)) { text = "下一页", name = "enemy-page-next" };
            pageStatus = new Label { name = "enemy-page-status" };
            pagination.Add(previousPage);
            pagination.Add(pageStatus);
            pagination.Add(nextPage);
            Add(pagination);
            SwitchView(false);
        }

        internal void SetEntries(IReadOnlyList<EnemyCatalog.Entry> next, bool query)
        {
            ordered = query ? next : next.OrderBy(entry => entry.Filters.Order ?? int.MaxValue).ToArray();
            ShowPage(0);
        }

        internal void RefreshSelection()
        {
            list.RefreshItems();
            if (iconMode) gallery.RefreshItems();
        }

        private void SwitchView(bool icons)
        {
            iconMode = icons;
            list.style.display = icons ? DisplayStyle.None : DisplayStyle.Flex;
            gallery.style.display = icons ? DisplayStyle.Flex : DisplayStyle.None;
            tableButton.EnableInClassList("selected-tab", !icons);
            galleryButton.EnableInClassList("selected-tab", icons);
            if (icons) UpdateGridRows();
        }

        private void ShowPage(int index)
        {
            var size = int.Parse(pageSize.value, CultureInfo.InvariantCulture);
            var pageCount = (ordered.Count + size - 1) / size;
            page = Math.Min(Math.Max(0, index), Math.Max(0, pageCount - 1));
            previousPage.SetEnabled(page > 0);
            nextPage.SetEnabled(page + 1 < pageCount);
            pageStatus.text = pageCount == 0 ? "0 / 0" : $"{page + 1} / {pageCount}";
            pageEntries.Clear();
            pageEntries.AddRange(ordered.Skip(page * size).Take(size));
            list.RefreshItems();
            if (pageEntries.Count > 0) list.ScrollToItem(0);
            if (iconMode)
            {
                UpdateGridRows();
                if (gridRows.Count > 0) gallery.ScrollToItem(0);
            }
        }

        private void UpdateGridRows()
        {
            gridRows.Clear();
            for (var start = 0; start < pageEntries.Count; start += cardsPerRow) gridRows.Add(start);
            gallery.Rebuild();
        }

        private void BindGallery(VisualElement row, int index, EnemyPortraitCatalog portraits, Texture2D? infoIcon, Func<string> selected,
            Action<EnemyCatalog.Entry> choose, Action<EnemyCatalog.Entry, Button> open)
        {
            row.AddToClassList("enemy-avatar-row");
            while (row.childCount < cardsPerRow) row.Add(new EnemyAvatarCard(portraits, infoIcon, choose, open));
            for (var column = 0; column < row.childCount; column++)
            {
                var card = (EnemyAvatarCard)row[column];
                var item = gridRows[index] + column;
                var show = column < cardsPerRow && item < pageEntries.Count;
                card.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (show) card.Bind(pageEntries[item], selected());
            }
        }

        private sealed class EnemyAvatarCard : VisualElement
        {
            private readonly EnemyPortraitCatalog portraits;
            private readonly Image image = LibraryPortrait.Create("enemy-avatar-image", "enemy-grid-portrait");
            private readonly Button select;
            private EnemyCatalog.Entry? item;

            internal EnemyAvatarCard(EnemyPortraitCatalog portraits, Texture2D? infoIcon, Action<EnemyCatalog.Entry> choose,
                Action<EnemyCatalog.Entry, Button> open)
            {
                this.portraits = portraits;
                AddToClassList("enemy-avatar-card");
                select = new Button(() => { if (item != null) choose(item); });
                select.AddToClassList("enemy-avatar-select");
                select.Add(image);
                Add(select);
                var info = new Button { tooltip = "查看敌人详情", name = "enemy-grid-info" };
                info.AddToClassList("enemy-avatar-info");
                info.clicked += () => { if (item != null) open(item, info); };
                if (infoIcon != null) info.Add(new Image { image = infoIcon, pickingMode = PickingMode.Ignore });
                else info.text = "ⓘ";
                Add(info);
            }

            internal void Bind(EnemyCatalog.Entry entry, string selected)
            {
                item = entry;
                tooltip = entry.Name + " · " + entry.Id;
                name = "enemy-avatar-" + entry.Id;
                select.EnableInClassList("selected-tab", selected == entry.Id);
                BindPortrait(image, portraits, entry.Id);
            }
        }

        private static void BindPortrait(Image image, EnemyPortraitCatalog portraits, string id)
        {
            var picture = portraits.Find(id);
            LibraryPortrait.Bind(image, picture == null ? null : portraits.Texture(picture),
                picture?.Uv ?? default, picture?.LightBackground == true);
        }

        private sealed class EnemyLibraryRow : VisualElement
        {
            private readonly Button select;
            private readonly Button info;
            private readonly Image portrait = LibraryPortrait.Create("operator-library-avatar", "enemy-library-avatar");
            private readonly Label title = new();
            private readonly Label subtitle = new();
            private readonly EnemyPortraitCatalog portraits;
            private EnemyCatalog.Entry? entry;

            internal EnemyLibraryRow(EnemyPortraitCatalog portraits,
                Texture2D? icon, Action<EnemyCatalog.Entry> choose, Action<EnemyCatalog.Entry, Button> open)
            {
                this.portraits = portraits;
                AddToClassList("enemy-library-row");
                select = new Button(() => { if (entry != null) choose(entry); });
                select.AddToClassList("operator-library-row");
                var text = new VisualElement { pickingMode = PickingMode.Ignore };
                text.AddToClassList("library-row-labels");
                title.AddToClassList("catalog-name");
                subtitle.AddToClassList("catalog-id");
                text.Add(title);
                text.Add(subtitle);
                select.Add(portrait);
                select.Add(text);
                Add(select);
                info = new Button { tooltip = "查看敌人详情" };
                info.clicked += () => { if (entry != null) open(entry, info); };
                info.AddToClassList("terrain-info-button");
                if (icon != null) info.Add(new Image { image = icon, pickingMode = PickingMode.Ignore });
                else info.text = "ⓘ";
                Add(info);
            }

            internal void Bind(EnemyCatalog.Entry item, string selected)
            {
                entry = item;
                title.text = item.Name;
                subtitle.text = item.Filters.Number.Length > 0 ? item.Filters.Number + " · " + item.Id : item.Id;
                select.tooltip = item.Id;
                select.name = "enemy-select-" + item.Id;
                info.name = "enemy-info-" + item.Id;
                select.EnableInClassList("selected-tab", selected == item.Id);
                BindPortrait(portrait, portraits, item.Id);
            }
        }
    }
}
