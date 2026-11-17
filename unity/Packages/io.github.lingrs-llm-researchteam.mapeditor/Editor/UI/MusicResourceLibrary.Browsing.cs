#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed partial class MusicResourceLibrary
    {
        private const string All = "全部";
        private static readonly string[] UsageGroups =
        {
            "环境底声", "天气与全图机制", "地块音效", "装置音效", "关卡演出", "玩法与操作反馈"
        };
        private readonly Dictionary<(bool Sound, bool Native, string Scope), BrowseState> browseStates = new();
        private readonly VisualElement usageToolbar = new();
        private readonly VisualElement filters = new();
        private ResourceSubcategoryList subcategories = null!;
        private AudioLibraryCatalog? original;
        private Button usageMode = null!;
        private Button nativeMode = null!;
        private UnityEditor.UIElements.ToolbarSearchField query = null!;
        private readonly Label searchLabel = new();
        private bool sound;
        private bool native;
        private float? restoreScroll;

        private (bool Sound, bool Native, string Scope) View => (sound, native, target == null ? "" : FieldGroup);
        private BrowseState CurrentView => browseStates.TryGetValue(View, out var state) ? state : new BrowseState();
        private string FieldGroup => target?.Environment == true ? AudioLibraryCatalog.EnvironmentGroup :
            target?.Opening == true ? AudioLibraryCatalog.OpeningGroup : AudioLibraryCatalog.TriggerGroup;

        private sealed class BrowseState
        {
            internal string Group { get; }
            internal string Signal { get; }
            internal float Scroll { get; }

            internal BrowseState(string group = All, string signal = All, float scroll = 0)
            {
                Group = group;
                Signal = signal;
                Scroll = scroll;
            }
        }

        private void BuildViewControls()
        {
            var tree = new VisualElement();
            tree.AddToClassList("resource-tree-frame");
            Add(tree);
            var modes = Row(tree);
            modes.AddToClassList("resource-mode-frame");
            usageMode = Command("按用途", "audio-library-usage", () => ChangeView(sound, false));
            nativeMode = Command("按原版事件", "audio-library-native", () => ChangeView(sound, true));
            modes.Add(usageMode);
            modes.Add(nativeMode);
            usageToolbar.AddToClassList("workbench-tabs");
            usageToolbar.AddToClassList("resource-filter-frame");
            tree.Add(usageToolbar);
            filters.AddToClassList("resource-branch-frame");
            tree.Add(filters);
            subcategories = new ResourceSubcategoryList(this, "audio-library-signal", "细分类");
            subcategories.Changed += () => ChangeFilter(CurrentView.Group, subcategories.Value);
        }

        internal void ShowKind(bool showSounds)
        {
            if (sound == showSounds) return;
            Unbind();
            ChangeView(showSounds, native);
        }

        private void ChangeView(bool showSounds, bool showNative)
        {
            if (sound == showSounds && native == showNative) return;
            if (target != null && target.Environment != showSounds) return;
            var state = CurrentView;
            var offset = list.Q<ScrollView>()?.scrollOffset.y ?? 0;
            browseStates[View] = new BrowseState(state.Group, state.Signal, offset);
            if (sound != showSounds)
            {
                selected = null;
                CloseDetails();
            }
            sound = showSounds;
            native = showNative;
            restoreScroll = CurrentView.Scroll;
            RefreshEntries();
        }

        private void RefreshViewControls()
        {
            searchLabel.text = sound ? "搜索音效" : "搜索音乐";
            usageMode.EnableInClassList("selected-tab", !native);
            nativeMode.EnableInClassList("selected-tab", native);
            usageToolbar.style.display = !native && !sound ? DisplayStyle.Flex : DisplayStyle.None;
            filters.Clear();
            filters.style.display = native || sound ? DisplayStyle.Flex : DisplayStyle.None;
            if (!native && !sound) return;

            var rows = ScopedRows().ToArray();
            var state = CurrentView;
            AddFilter(native ? "模块" : "分类", "audio-library-group", native ? rows.SelectMany(Groups)
                    .Distinct().OrderBy(item => item == "battle" ? 0 : item == "ui" ? 1 : 2)
                : UsageGroups.Where(group => rows.Any(row => Groups(row).Contains(group))), state.Group,
                group => ChangeFilter(group, All));
            if (state.Group == All) return;
            var signals = rows.Where(row => Groups(row).Contains(state.Group)).SelectMany(row => Signals(row, state.Group))
                .Distinct().OrderBy(item => item, StringComparer.Ordinal);
            subcategories.ShowChoices(native ? "信号" : "细分类", new[] { All }.Concat(signals), state.Signal);
            filters.Add(subcategories);
        }

        private void AddFilter(string label, string control, IEnumerable<string> values, string selectedValue,
            Action<string> changed)
        {
            var choices = new[] { All }.Concat(values.Distinct()).ToList();
            var index = choices.IndexOf(selectedValue);
            var field = new DropdownField(label, choices, Math.Max(0, index)) { name = control };
            field.AddToClassList("resource-filter-frame");
            field.RegisterValueChangedCallback(value =>
            {
                changed(value.newValue);
                filters.Q<DropdownField>(control)?.Focus();
            });
            filters.Add(field);
        }

        private void ChangeFilter(string group, string signal)
        {
            browseStates[View] = new BrowseState(group, signal);
            restoreScroll = 0;
            RefreshEntries();
        }

        private IEnumerable<string> Groups(MusicEventCatalog.Entry row) =>
            native ? new[] { row.Address.Module } : row.Usage.Select(path => path.Split('/')[0]);

        private IEnumerable<string> Signals(MusicEventCatalog.Entry row, string group) =>
            native ? new[] { row.Address.Signal } :
                row.Usage.Where(path => path.StartsWith(group + "/", StringComparison.Ordinal))
                    .Select(path => path.Substring(group.Length + 1));

        private IReadOnlyList<MusicEventCatalog.Entry> AvailableRows()
        {
            if (sound)
                return (native ? original?.Effects : original?.EffectClips) ?? Array.Empty<MusicEventCatalog.Entry>();
            if (native) return original?.Music ?? Array.Empty<MusicEventCatalog.Entry>();
            return catalog?.Choices(opening) ?? Array.Empty<MusicEventCatalog.Entry>();
        }

        private IEnumerable<MusicEventCatalog.Entry> ScopedRows()
        {
            var available = AvailableRows();
            return target == null ? available :
                available.Where(row => row.Address.Group == FieldGroup && row.Address.SubSignal.Length > 0);
        }

        private IReadOnlyList<MusicEventCatalog.Entry> FilteredRows()
        {
            var items = ScopedRows();
            if (native || sound)
            {
                var state = CurrentView;
                if (state.Group != All && items.Any(row => Groups(row).Contains(state.Group)))
                {
                    items = items.Where(row => Groups(row).Contains(state.Group));
                    if (state.Signal != All && items.Any(row => Signals(row, state.Group).Contains(state.Signal)))
                        items = items.Where(row => Signals(row, state.Group).Contains(state.Signal));
                }
            }
            var ordered = items.OrderBy(row => row.Label, StringComparer.Ordinal)
                .ThenBy(row => row.EventName, StringComparer.Ordinal);
            return sound && !native
                ? ordered.GroupBy(row => row.ClipAsset, StringComparer.OrdinalIgnoreCase).Select(groupRows => groupRows.First()).ToArray()
                : ordered.ToArray();
        }

        private void LocateSelection()
        {
            if (selected == null) return;
            query.SetValueWithoutNotify("");
            if (!native && !sound && !AvailableRows().Any(row => row.EventName == selected.EventName))
                ChangeView(false, true);
            if (native) ChangeFilter(selected.Address.Module, selected.Address.Signal);
            else if (sound && selected.Usage.Count > 0)
            {
                var path = selected.Usage[0].Split('/');
                ChangeFilter(path[0], path[1]);
            }
            else RefreshEntries();
            var at = SelectedIndex();
            if (at >= 0) list.ScrollToItem(at);
        }

        private int SelectedIndex()
        {
            if (selected == null) return -1;
            var exact = displayed.FindIndex(row => row.DisplayId == selected.DisplayId);
            if (exact >= 0) return exact;
            if (!selected.IsSound) return -1;
            var byEvent = displayed.FindIndex(row => row.EventName == selected.EventName &&
                (row.ClipAsset == null || selected.ClipAsset == null));
            if (byEvent >= 0) return byEvent;
            return displayed.FindIndex(row => row.ClipAsset != null && string.Equals(row.ClipAsset,
                selected.ClipAsset, StringComparison.OrdinalIgnoreCase));
        }

        private MusicEventCatalog.Entry? FindTarget(MusicEventPicker field, string key) =>
            field.Environment ? original?.Find(AudioLibraryCatalog.EnvironmentGroup, key) :
                catalog?.Find(field.Opening, key) ?? original?.Find(FieldGroup, key);

    }
}
