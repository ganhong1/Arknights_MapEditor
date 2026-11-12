#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class OptionsSourceOverlay : VisualElement
    {
        private readonly Label title = new Label();
        private readonly Label status = new Label();
        private readonly Label metadata = OptionsSampleBrowser.Text("");
        private readonly TextField search = new TextField("搜索关卡或 LevelData");
        private readonly TextField context = new TextField("原文件 options") { multiline = true, isReadOnly = true };
        private readonly List<(OptionsStageSources.Group Group, OptionsValueCatalog.Source? File)> rows =
            new List<(OptionsStageSources.Group, OptionsValueCatalog.Source?)>();
        private readonly ListView tree;
        private readonly CatalogSearchInput<OptionsStageSources.Group> searchInput;
        private OptionsStageSources? index;
        private OptionsValueCatalog.Sample? sample;
        private IReadOnlyList<OptionsStageSources.Group> visibleGroups = Array.Empty<OptionsStageSources.Group>();
        private readonly HashSet<OptionsStageSources.Group> expanded = new HashSet<OptionsStageSources.Group>();
        private Button? opener;
        private readonly Button close;
        private string indexError = "";

        internal OptionsSourceOverlay()
        {
            name = "options-source-details";
            AddToClassList("terrain-detail-overlay");
            var panel = new VisualElement();
            panel.AddToClassList("terrain-detail-panel");
            var header = new VisualElement();
            header.AddToClassList("terrain-detail-header");
            title.AddToClassList("workbench-heading");
            header.Add(title);
            close = new Button(Close) { text = "关闭", name = "options-source-close" };
            header.Add(close);
            panel.Add(header);
            panel.Add(search);
            panel.Add(status);
            tree = new ListView(rows, 38, MakeRow, BindRow)
            {
                name = "options-source-stage-tree",
                selectionType = SelectionType.None
            };
            tree.style.flexGrow = 1;
            tree.style.minHeight = 60;
            panel.Add(tree);
            var detail = new ScrollView { name = "options-source-file-details" };
            detail.style.maxHeight = 190;
            detail.Add(metadata);
            context.name = "options-source-file-options";
            context.style.flexDirection = FlexDirection.Column;
            detail.Add(context);
            panel.Add(detail);
            Add(panel);
            searchInput = new CatalogSearchInput<OptionsStageSources.Group>(search,
                () => TerrainSearchRuntime.CreateWorker<OptionsStageSources.Group>(group =>
                    TerrainSearchRuntime.GroupedStage(new[] { group.Title }, new[] { group.Name },
                        new[] { group.Id, group.Code }, group.Files.Select(file => file.Path), new[] { group.Difficulty })),
                Array.Empty<OptionsStageSources.Group>(), () => status.text = "搜索中…", ShowGroups);
            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;
                evt.StopPropagation();
                Close();
            }, TrickleDown.TrickleDown);
        }

        internal void Open(string key, OptionsValueCatalog.Sample value, Button trigger)
        {
            opener = trigger;
            sample = value;
            title.text = key + " · " + value.Label + " · 出处";
            indexError = "";
            OptionsStageSources.Group[] groups;
            try
            {
                index = OptionsStageSources.LoadBundled();
                groups = index.GroupFiles(value.Sources);
            }
            catch (Exception error) when (error is FormatException || error is IOException || error is JsonException)
            {
                index = null;
                indexError = error.Message + "\n";
                groups = new[] { new OptionsStageSources.Group(null, value.Sources) };
            }
            expanded.Clear();
            if (groups.Length > 0) expanded.Add(groups[0]);
            metadata.text = "";
            context.style.display = DisplayStyle.None;
            search.SetValueWithoutNotify("");
            searchInput.SetItems(groups);
            tree.ScrollToItem(0);
            close.Focus();
        }

        private void ShowGroups(IReadOnlyList<OptionsStageSources.Group> groups, string? error)
        {
            visibleGroups = groups;
            status.text = indexError + (error ?? groups.Count(group => group.Id.Length > 0) + " 个关卡条目 · " +
                (sample?.Sources.Count ?? 0) + " 个文件出处");
            RebuildRows();
        }

        private void RebuildRows()
        {
            var scroll = tree.Q<ScrollView>();
            var offset = scroll?.scrollOffset ?? Vector2.zero;
            rows.Clear();
            foreach (var group in visibleGroups)
            {
                rows.Add((group, null));
                if (expanded.Contains(group)) rows.AddRange(group.Files.Select(file => (group, (OptionsValueCatalog.Source?)file)));
            }
            tree.Rebuild();
            if (scroll != null) scroll.scrollOffset = offset;
        }

        private Button MakeRow()
        {
            var button = new Button();
            button.AddToClassList("options-source-stage-row");
            button.clicked += () =>
            {
                if (!(button.userData is int row) || row >= rows.Count) return;
                var entry = rows[row];
                if (entry.File != null) ShowFile(entry.File);
                else
                {
                    if (!expanded.Remove(entry.Group)) expanded.Add(entry.Group);
                    RebuildRows();
                }
            };
            return button;
        }

        private void BindRow(VisualElement element, int row)
        {
            var button = (Button)element;
            var entry = rows[row];
            button.userData = row;
            button.EnableInClassList("options-source-file-row", entry.File != null);
            button.text = entry.File == null
                ? (expanded.Contains(entry.Group) ? "▾ " : "▸ ") + entry.Group.Title +
                    (entry.Group.Difficulty.Length > 0 ? " · " + entry.Group.Difficulty : "") + " · " + entry.Group.Files.Count + " 份"
                : entry.File.Path + "\n" + entry.File.Kind + (index?.IsCachePathMatch(entry.File) == true ? " · 按资源路径关联" : "");
            button.tooltip = button.text;
        }

        private void ShowFile(OptionsValueCatalog.Source file)
        {
            metadata.text = OptionsSampleBrowser.SourceDescription(file, sample!.Pointers(file));
            context.SetValueWithoutNotify(file.CopyOptions().ToString(Formatting.Indented));
            context.style.display = DisplayStyle.Flex;
        }

        private void Close()
        {
            RemoveFromHierarchy();
            opener?.Focus();
        }
    }
}
