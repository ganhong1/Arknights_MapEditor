#nullable enable
using System;
using System.Collections;
using System.Globalization;
using Arknights.MapEditor.Core;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class RoutePreviewPanel : VisualElement
    {
        private static readonly string[] ModeNames = { "单一路线", "多路线（时间）", "多路线（次序）" };
        private static readonly string[] SourceNames = { "出怪顺序", "路线定义", "分支出怪" };
        private static readonly string[] SpeedNames = { "标准", "慢速", "很慢", "恒速" };
        private readonly RoutePreviewPlayback playback;
        private readonly Func<LevelDocument> document;
        private readonly Button visibility, previous, next, play, replay;
        private readonly Button[] modes, sources;
        private readonly VisualElement content, range;
        private readonly FloatField before, after;
        private readonly PopupField<string> speed;
        private readonly Toggle animate, hidden;
        private readonly Slider progress;
        private readonly Label current, summary, warning;
        private readonly ListView list;

        internal RoutePreviewPanel(RoutePreviewPlayback playback, Func<LevelDocument> document)
        {
            this.playback = playback;
            this.document = document;
            name = "route-preview";
            AddToClassList("route-preview");
            var header = Row(this);
            visibility = AddButton(header, "route-preview-toggle", "", () => playback.SetVisible(!playback.Visible, this.document()));

            content = new VisualElement();
            Add(content);
            var scope = MapEditorWindow.AddRouteCard(content, "演示范围");
            sources = AddChoices(Row(scope), "route-preview-source", SourceNames,
                index => playback.SetSource((RoutePreviewSource)index));
            modes = AddChoices(Row(scope), "route-preview-mode", ModeNames,
                index => playback.SetMode((RoutePreviewMode)index));

            range = Row(scope);
            before = new FloatField { name = "route-preview-before", isDelayed = true };
            after = new FloatField { name = "route-preview-after", isDelayed = true };
            range.Add(before);
            range.Add(after);
            before.RegisterValueChangedCallback(e => playback.SetBounds(e.newValue, after.value));
            after.RegisterValueChangedCallback(e => playback.SetBounds(before.value, e.newValue));

            var playbackCard = MapEditorWindow.AddRouteCard(content, "播放控制");
            var navigation = Row(playbackCard);
            navigation.focusable = true;
            navigation.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode is not (KeyCode.LeftArrow or KeyCode.RightArrow)) return;
                playback.Select(playback.Selected + (e.keyCode == KeyCode.LeftArrow ? -1 : 1));
                e.StopPropagation();
            });
            navigation.RegisterCallback<WheelEvent>(e =>
            {
                if (e.delta.y == 0 || playback.Entries.Count < 2) return;
                playback.Select(playback.Selected + (e.delta.y > 0 ? 1 : -1));
                e.StopPropagation();
            });
            previous = AddButton(navigation, "route-preview-previous", "◀ 上一条", () => playback.Select(playback.Selected - 1));
            next = AddButton(navigation, "route-preview-next", "下一条 ▶", () => playback.Select(playback.Selected + 1));
            current = new Label { name = "route-preview-current" };
            current.AddToClassList("route-preview-current");
            navigation.Add(current);

            var controls = Row(playbackCard);
            play = AddButton(controls, "route-preview-play", "", playback.TogglePlay);
            replay = AddButton(controls, "route-preview-replay", "重播", playback.Replay);
            speed = new PopupField<string>(new System.Collections.Generic.List<string>(SpeedNames), 0) { name = "route-preview-speed" };
            speed.RegisterValueChangedCallback(e => playback.SetSpeed((RoutePreviewSpeed)Array.IndexOf(SpeedNames, e.newValue)));
            controls.Add(speed);
            animate = new Toggle("路线动画") { name = "route-preview-animate" };
            animate.RegisterValueChangedCallback(e => playback.SetAnimate(e.newValue));
            controls.Add(animate);
            hidden = new Toggle("隐藏组") { name = "route-preview-hidden" };
            hidden.RegisterValueChangedCallback(e => playback.SetIncludeHidden(e.newValue, this.document()));
            controls.Add(hidden);

            progress = new Slider(0, 1) { name = "route-preview-progress", tooltip = "拖动查看路线动画进度" };
            progress.RegisterValueChangedCallback(e => playback.Scrub(e.newValue));
            playbackCard.Add(progress);

            var entries = MapEditorWindow.AddRouteCard(content, "演示记录");
            summary = new Label { name = "route-preview-summary" };
            summary.AddToClassList("route-preview-summary");
            entries.Add(summary);
            warning = new Label("虚线为检查点连线，尚未解析该段的可通行路径。");
            warning.AddToClassList("route-preview-warning");
            entries.Add(warning);

            list = new ListView
            {
                name = "route-preview-entries", fixedItemHeight = 42, selectionType = SelectionType.Single,
                makeItem = MakeItem,
                bindItem = BindItem
            };
            list.AddToClassList("route-preview-entries");
            list.AddToClassList("route-list");
            list.onSelectionChange += _ => { if (list.selectedIndex >= 0) playback.Select(list.selectedIndex); };
            entries.Add(list);

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                playback.Changed += Refresh;
                playback.FrameChanged += RefreshProgress;
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                playback.Changed -= Refresh;
                playback.FrameChanged -= RefreshProgress;
            });
            Refresh();
        }

        private static VisualElement MakeItem()
        {
            var item = new VisualElement();
            item.AddToClassList("route-preview-entry");
            item.Add(new Label { name = "entry-main" });
            var detail = new Label { name = "entry-detail" };
            detail.AddToClassList("route-preview-entry-detail");
            item.Add(detail);
            return item;
        }

        private void BindItem(VisualElement element, int index)
        {
            var entry = playback.Entries[index];
            var route = $"{entry.Library}[{entry.RouteIndex}]";
            var main = element.Q<Label>("entry-main");
            var detail = element.Q<Label>("entry-detail");
            if (playback.Source == RoutePreviewSource.Definitions)
            {
                main.text = route;
                detail.text = "";
            }
            else
            {
                main.text = $"#{IndexRange(entry)}   {PrtsObjectView.DisplayName(entry.Enemy)}   ×{entry.Count}";
                detail.text = $"{Time(entry.Time)}   {route}" + (entry.Group.Length == 0 ? "" : $"   {entry.Group}");
            }
            element.tooltip = main.text + "  " + detail.text + "  " + entry.Enemy;
        }

        private void Refresh()
        {
            visibility.text = playback.Visible ? "隐藏路线" : "显示路线";
            visibility.EnableInClassList("selected-tab", playback.Visible);
            content.style.display = playback.Visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!playback.Visible) return;
            Highlight(modes, (int)playback.Mode);
            Highlight(sources, (int)playback.Source);
            modes[(int)RoutePreviewMode.Time].SetEnabled(playback.Source != RoutePreviewSource.Definitions);
            range.style.display = playback.Mode == RoutePreviewMode.Single ? DisplayStyle.None : DisplayStyle.Flex;
            var unit = playback.Mode == RoutePreviewMode.Time ? "秒" : "条";
            before.label = "向前 · " + unit;
            after.label = "向后 · " + unit;
            before.SetValueWithoutNotify(playback.Before);
            after.SetValueWithoutNotify(playback.After);
            animate.SetValueWithoutNotify(playback.Animate);
            hidden.SetValueWithoutNotify(playback.IncludeHidden);
            hidden.style.display = playback.Source == RoutePreviewSource.Definitions ? DisplayStyle.None : DisplayStyle.Flex;
            speed.SetValueWithoutNotify(SpeedNames[(int)playback.Speed]);

            var count = playback.Entries.Count;
            var selected = playback.Selected;
            previous.SetEnabled(selected > 0);
            next.SetEnabled(selected >= 0 && selected < count - 1);
            play.SetEnabled(playback.Animate && selected >= 0);
            replay.SetEnabled(playback.Animate && selected >= 0);
            progress.SetEnabled(playback.Animate && selected >= 0);
            play.text = playback.Playing ? "暂停" : "播放";
            current.text = selected >= 0 ? $"{selected + 1} / {count}" : "0 / 0";
            if (selected >= 0)
            {
                var entry = playback.Entries[selected];
                var focused = playback.Source == RoutePreviewSource.Definitions
                    ? $"{entry.Library}[{entry.RouteIndex}]"
                    : $"#{IndexRange(entry)}   {Time(entry.Time)}   {PrtsObjectView.DisplayName(entry.Enemy)}";
                summary.text = $"{focused}\n显示 {playback.VisibleCount} 条记录 · {playback.UniquePathCount} 条不同路线";
            }
            else summary.text = playback.Source == RoutePreviewSource.Branches ? "此地图没有分支出怪路线" : "此地图没有可演示的路线";
            warning.style.display = playback.HasFallback ? DisplayStyle.Flex : DisplayStyle.None;
            if (!ReferenceEquals(list.itemsSource, playback.Entries))
            {
                list.itemsSource = (IList)playback.Entries;
                list.Rebuild();
            }
            if (list.selectedIndex != selected)
            {
                list.SetSelectionWithoutNotify(selected >= 0 ? new[] { selected } : Array.Empty<int>());
                if (selected >= 0)
                    list.schedule.Execute(() =>
                    {
                        if (list.selectedIndex == selected && list.panel != null) list.ScrollToItem(selected);
                    });
            }
            RefreshProgress();
        }

        private void RefreshProgress() => progress.SetValueWithoutNotify(playback.Progress);

        private static string IndexRange(PrtsRoutePreview.Entry entry) =>
            entry.Count == 1 ? entry.First.ToString(CultureInfo.InvariantCulture) : $"{entry.First}–{entry.First + entry.Count - 1}";

        private static string Time(double time) => time.ToString("0.##", CultureInfo.InvariantCulture) + "s";

        private static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement();
            row.AddToClassList("route-preview-row");
            parent.Add(row);
            return row;
        }

        private static Button AddButton(VisualElement parent, string name, string text, Action action)
        {
            var button = new Button(action) { name = name, text = text };
            parent.Add(button);
            return button;
        }

        private static Button[] AddChoices(VisualElement parent, string name, string[] labels, Action<int> select)
        {
            var buttons = new Button[labels.Length];
            for (var i = 0; i < buttons.Length; ++i)
            {
                var index = i;
                buttons[i] = AddButton(parent, name + "-" + i, labels[i], () => select(index));
            }
            return buttons;
        }

        private static void Highlight(Button[] buttons, int selected)
        {
            for (var i = 0; i < buttons.Length; ++i) buttons[i].EnableInClassList("selected-tab", i == selected);
        }
    }
}
