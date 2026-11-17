#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using UnityEditor.UIElements;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed partial class MusicResourceLibrary : VisualElement
    {
        private readonly Label context = Text("", "music-library-context");
        private readonly Label resultsStatus = Text("", "audio-library-results-status");
        private readonly VisualElement selection = new() { name = "music-library-selection" };
        private readonly VisualElement detail = new() { name = "music-library-detail" };
        private readonly ScrollView detailBody = new(ScrollViewMode.Vertical)
        {
            horizontalScrollerVisibility = ScrollerVisibility.Hidden
        };
        private readonly Label detailTitle = Text("");
        private readonly Button closeDetail;
        private readonly Button openingButton;
        private readonly Button triggerButton;
        private readonly ListView list;
        private readonly List<MusicEventCatalog.Entry> displayed = new();
        private readonly CatalogSearchInput<MusicEventCatalog.Entry>? search;
        private readonly MusicEventCatalog? catalog;
        private readonly Texture2D? infoIcon;
        private MusicEventPicker? target;
        private Func<bool>? targetIsCurrent;
        private MusicEventCatalog.Entry? selected;
        private Button? detailOpener;
        private bool opening = true;

        internal MusicResourceLibrary(VisualElement overlayParent)
        {
            name = "music-library";
            AddToClassList("workbench-resource-panel");
            infoIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(PrtsObjectView.CatalogRoot + "terrain-info.png");
            openingButton = Command("开局曲", "music-library-show-opening", () => Browse(true));
            triggerButton = Command("局中换曲", "music-library-show-trigger", () => Browse(false));
            BuildViewControls();
            usageToolbar.Add(openingButton);
            usageToolbar.Add(triggerButton);
            var searchRow = new VisualElement();
            searchRow.AddToClassList("resource-audio-search");
            searchRow.Add(searchLabel);
            query = new ToolbarSearchField
            {
                name = "music-event-search",
                tooltip = "输入名称、原版事件、音轨 ID 或分类"
            };
            searchRow.Add(query);
            Add(searchRow);
            context.style.display = DisplayStyle.None;
            selection.style.display = DisplayStyle.None;
            Add(context);
            Add(selection);
            Add(resultsStatus);
            list = new ListView(displayed, 49,
                () => new ResourceRow(infoIcon, Select, OpenDetails),
                (element, index) => ((ResourceRow)element).Bind(displayed[index], index == SelectedIndex(), native))
            {
                name = "music-library-list",
                selectionType = SelectionType.None
            };
            list.AddToClassList("catalog-list");
            Add(list);

            detail.AddToClassList("terrain-detail-overlay");
            detail.style.display = DisplayStyle.None;
            var detailPanel = new VisualElement();
            detailPanel.AddToClassList("terrain-detail-panel");
            var titleBar = Row(detailPanel);
            titleBar.AddToClassList("terrain-detail-header");
            detailTitle.AddToClassList("workbench-heading");
            titleBar.Add(detailTitle);
            closeDetail = Command("关闭", "music-library-detail-close", () => CloseDetails(true));
            titleBar.Add(closeDetail);
            detailBody.AddToClassList("terrain-detail-body");
            detailPanel.Add(detailBody);
            detail.Add(detailPanel);
            overlayParent.Add(detail);
            detail.RegisterCallback<KeyDownEvent>(change =>
            {
                if (change.keyCode != KeyCode.Escape) return;
                change.StopPropagation();
                CloseDetails(true);
            }, TrickleDown.TrickleDown);

            RegisterCallback<DetachFromPanelEvent>(_ => Deactivate());
            try { catalog = MusicEventCatalog.LoadBundled(); original = AudioLibraryCatalog.LoadBundled(); }
            catch (Exception error) { resultsStatus.text = PluginLog.FailureMessage("audio.catalog", error); return; }
            search = new CatalogSearchInput<MusicEventCatalog.Entry>(query, query, original.CreateSearch,
                catalog.Choices(true), ShowSearchPending, ReplaceRows);
            Browse(true);
        }

        internal void Bind(MusicEventPicker field, Func<bool> isCurrent)
        {
            Deactivate();
            target = field;
            targetIsCurrent = isCurrent;
            field.KeyChanged += RefreshBoundKey;
            field.RegisterCallback<DetachFromPanelEvent>(TargetDetached);
            opening = field.Opening;
            sound = field.Environment;
            if (native)
            {
                var address = new AudioEventAddress(FieldGroup);
                browseStates[View] = new BrowseState(address.Module, address.Signal);
            }
            else if (sound) browseStates[View] = new BrowseState("环境底声");
            restoreScroll = null;
            selected = FindTarget(field, field.Key);
            RefreshContext();
            RefreshEntries();
        }

        internal void Unbind()
        {
            if (target == null) return;
            target.KeyChanged -= RefreshBoundKey;
            target.UnregisterCallback<DetachFromPanelEvent>(TargetDetached);
            target = null;
            targetIsCurrent = null;
            RefreshContext();
            RefreshEntries();
        }

        internal void Deactivate()
        {
            Unbind();
            CloseDetails();
        }

        private void TargetDetached(DetachFromPanelEvent change)
        {
            if (!ReferenceEquals(change.target, target)) return;
            Deactivate();
        }

        private void RefreshBoundKey()
        {
            var entry = target == null ? null : FindTarget(target, target.Key);
            selected = entry;
            DrawSelection();
            list.RefreshItems();
        }

        private void Browse(bool useOpening)
        {
            Deactivate();
            opening = useOpening;
            sound = false;
            native = false;
            restoreScroll = 0;
            selected = null;
            RefreshContext();
            RefreshEntries();
        }

        private void RefreshContext()
        {
            context.style.display = target == null ? DisplayStyle.None : DisplayStyle.Flex;
            context.text = target == null ? "" :
                target.Environment ? "选用目标 · 环境音效 environmentSe" :
                target.Opening ? "选曲目标 · 开局曲 bgmEvent" : "选曲目标 · 局中动作 key";
            openingButton.EnableInClassList("selected-tab", opening);
            triggerButton.EnableInClassList("selected-tab", !opening);
            openingButton.SetEnabled(target == null);
            triggerButton.SetEnabled(target == null);
        }

        private void RefreshEntries()
        {
            if (catalog == null) return;
            RefreshViewControls();
            search?.SetItems(FilteredRows());
            DrawSelection();
        }

        private void ShowSearchPending()
        {
            resultsStatus.text = "正在搜索…";
            if (displayed.Count == 0) return;
            displayed.Clear();
            list.Rebuild();
        }

        private void ReplaceRows(IReadOnlyList<MusicEventCatalog.Entry> rows, string? error)
        {
            displayed.Clear();
            displayed.AddRange(rows);
            resultsStatus.text = error ?? $"{displayed.Count} 项 · {MusicEventCatalog.GameVersion}";
            list.Rebuild();
            var offset = restoreScroll;
            restoreScroll = null;
            list.schedule.Execute(() =>
            {
                if (offset is float scroll)
                {
                    if (list.Q<ScrollView>() is { } viewport) viewport.scrollOffset = new Vector2(0, scroll);
                }
                else
                {
                    var index = SelectedIndex();
                    if (index >= 0) list.ScrollToItem(index);
                }
            });
            DrawSelection();
        }

        private void Select(MusicEventCatalog.Entry entry)
        {
            selected = entry;
            DrawSelection();
            list.RefreshItems();
        }

        private void DrawSelection()
        {
            selection.Clear();
            selection.style.display = target == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (target == null) return;
            if (selected is not { } entry)
            {
                if (target.Key.Length > 0)
                {
                    selection.Add(Text(FieldGroup + "." + target.Key));
                    selection.Add(Text(target.Environment ? "环境音事件未登记" : opening ? "开局事件未登记" : "局中事件未登记", "music-library-binding-status"));
                }
                selection.Add(Command("结束选曲", "music-library-unbind", Unbind));
                return;
            }
            var toolbar = Row(selection);
            if (SelectedIndex() < 0 && (entry.IsRegistered || native || sound))
                toolbar.Add(Command("定位到此项", "audio-library-locate", LocateSelection));
            if (!entry.IsRegistered)
            {
                var info = InfoButton(infoIcon, "music-library-current-detail", entry.Label);
                info.clicked += () => OpenDetails(entry, info);
                toolbar.Add(info);
            }
            if (entry.IsRegistered && entry.Address.Group == FieldGroup && entry.Address.SubSignal.Length > 0)
                toolbar.Add(Command(target.Environment ? "选用此音效" : "选用此曲",
                    "music-library-assign", () => Assign(entry.Address.SubSignal)));
            if (entry.SuggestedKey is string replacement)
                toolbar.Add(Command("改用 " + replacement, "music-event-suggested-key", () => Assign(replacement)));
            toolbar.Add(Command("结束选曲", "music-library-unbind", Unbind));
        }

        private void Assign(string key)
        {
            if (target is not { } field || targetIsCurrent?.Invoke() != true || !field.enabledInHierarchy)
            {
                Unbind();
                throw new InvalidOperationException("选曲目标已经变化，请从当前编辑项重新打开资源库。");
            }
            if (FindTarget(field, key)?.IsRegistered != true)
                throw new InvalidOperationException("该事件在当前场合未登记。");
            field.Choose(key);
        }

        private void CloseDetails(bool restoreFocus = false)
        {
            MusicPreview.Stop(detail);
            detail.style.display = DisplayStyle.None;
            detailBody.Clear();
            if (restoreFocus) detailOpener?.Focus();
            detailOpener = null;
        }

        private Button Command(string title, string control, Action action, Label? errorStatus = null) => new(() =>
        {
            try { action(); }
            catch (Exception error) { (errorStatus ?? resultsStatus).text = PluginLog.FailureMessage("audio.library", error); }
        }) { text = title, name = control };

        private static VisualElement Row(VisualElement parent)
        {
            var result = new VisualElement();
            result.AddToClassList("workbench-tabs");
            parent.Add(result);
            return result;
        }

        private static Label Text(string text, string? name = null)
        {
            var label = new Label(text);
            if (name != null) label.name = name;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static Button InfoButton(Texture2D? texture, string name, string title)
        {
            var info = new Button { name = name, tooltip = "查看 " + title + " 的详情" };
            info.AddToClassList("terrain-info-button");
            if (texture == null) info.text = "ⓘ";
            else info.Add(new Image { image = texture, pickingMode = PickingMode.Ignore });
            return info;
        }

        private sealed class ResourceRow : VisualElement
        {
            private readonly Button select;
            private readonly Button info;
            private readonly Label title = Text("");
            private readonly Label subtitle = Text("");
            private MusicEventCatalog.Entry? entry;

            internal ResourceRow(Texture2D? icon, Action<MusicEventCatalog.Entry> choose,
                Action<MusicEventCatalog.Entry, Button> details)
            {
                AddToClassList("terrain-entry-row");
                select = new Button(() => { if (entry != null) choose(entry); });
                select.AddToClassList("terrain-select");
                title.AddToClassList("catalog-name");
                subtitle.AddToClassList("catalog-id");
                title.pickingMode = PickingMode.Ignore;
                subtitle.pickingMode = PickingMode.Ignore;
                var labels = new VisualElement { pickingMode = PickingMode.Ignore };
                labels.style.minWidth = 0;
                labels.style.flexGrow = 1;
                labels.Add(title);
                labels.Add(subtitle);
                select.Add(labels);
                Add(select);
                info = InfoButton(icon, "", "");
                info.clicked += () => { if (entry != null) details(entry, info); };
                Add(info);
            }

            internal void Bind(MusicEventCatalog.Entry value, bool selected, bool nativeView)
            {
                entry = value;
                select.name = "music-library-select-" + value.Key;
                select.tooltip = value.EventName;
                select.EnableInClassList("selected-tab", selected);
                title.text = value.Label;
                subtitle.text = nativeView ? value.EventName :
                    value.Label == value.Key ? value.PreviewStatus : value.Key;
                info.name = "music-library-info-" + value.Key;
                info.tooltip = "查看 " + value.Label + " 的详情";
            }
        }
    }
}
