#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class EnemyTagFilter
    {
        internal static readonly string[] Grades = { "SS", "S+", "S", "A+", "A", "B+", "B", "C", "D", "E" };
        internal static readonly (string Key, string Title, string[] Options)[] Basic =
        {
            ("rank", "地位", new[] { "普通", "精英", "领袖" }),
            ("race", "种类", new[] { "感染生物", "无人机", "萨卡兹", "宿主", "海怪", "法术造物",
                "源石造物", "化物", "机械", "野生动物", "坍缩体", "其他" }),
            ("attack", "攻击方式", new[] { "近战", "远程", "不攻击" }),
            ("damage", "伤害类型", new[] { "物理", "法术", "治疗", "无" })
        };
        internal static readonly (string Key, string Title, string[] Options)[] Ratings =
        {
            ("hp", "生命值", Grades), ("atk", "攻击力", Grades), ("def", "防御力", Grades),
            ("move", "移动速度", Grades), ("interval", "攻击速度", Grades), ("res", "法术抗性", Grades)
        };
        internal static readonly (string Key, string Title, string[] Options)[] More =
        {
            ("motion", "行动方式", new[] { "地面", "飞行" }),
            ("element", "元素抗性", Grades), ("damageRes", "损伤抵抗", Grades)
        };

        private readonly Dictionary<string, HashSet<string>> selected = new(StringComparer.Ordinal);
        internal int Count => selected.Values.Sum(options => options.Count);
        internal bool Includes(string group, string option) =>
            selected.TryGetValue(group, out var values) && values.Contains(option);

        internal void Toggle(string group, string option)
        {
            if (!selected.TryGetValue(group, out var values))
                selected.Add(group, values = new HashSet<string>(StringComparer.Ordinal));
            if (!values.Add(option)) values.Remove(option);
            if (values.Count == 0) selected.Remove(group);
        }

        internal void Clear() => selected.Clear();

        internal bool Matches(EnemyFilterCatalog.Profile profile) =>
            selected.All(group => profile.Values(group.Key).Any(group.Value.Contains));
        internal bool Matches(EnemyCatalog.Entry entry) =>
            selected.Count == 0 || entry.Filters.Profiles.Any(Matches);

    }

    internal sealed class EnemyTagFilterPanel : VisualElement
    {
        private readonly EnemyTagFilter filter;
        private readonly Button anchor;
        private readonly Action changed;
        private readonly List<(string Group, string Option, Button Button)> tags = new();

        internal EnemyTagFilterPanel(EnemyTagFilter filter, Button anchor, Action changed)
        {
            this.filter = filter;
            this.anchor = anchor;
            this.changed = changed;
            name = "enemy-tag-filter-panel";
            AddToClassList("operator-tag-filter-panel");
            style.display = DisplayStyle.None;
            var header = new VisualElement();
            header.AddToClassList("operator-filter-header");
            header.Add(new Label("标签筛选"));
            header.Add(new Button(() => Update(filter.Clear)) { text = "清除全部", name = "enemy-tag-clear-all" });
            Add(header);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("enemy-tag-filter-scroll");
            Add(scroll);
            AddSection(scroll, "筛选", "basic", EnemyTagFilter.Basic, true);
            AddSection(scroll, "六维筛选", "ratings", EnemyTagFilter.Ratings, false);
            AddSection(scroll, "更多筛选", "more", EnemyTagFilter.More, false);
            anchor.clicked += () => SetOpen(style.display == DisplayStyle.None);
            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;
                SetOpen(false);
                anchor.Focus();
                evt.StopPropagation();
            });
            Refresh();
        }

        private void AddSection(VisualElement parent, string title, string key,
            IEnumerable<(string Key, string Title, string[] Options)> groups, bool open)
        {
            var section = new Foldout { text = title, name = "enemy-tag-section-" + key, value = open };
            parent.Add(section);
            foreach (var (group, label, options) in groups)
            {
                var heading = new Label(label);
                heading.AddToClassList("operator-filter-heading");
                section.Add(heading);
                var row = new VisualElement();
                row.AddToClassList("operator-filter-tags");
                foreach (var option in options)
                {
                    var button = new Button(() => Update(() => filter.Toggle(group, option)))
                        { text = option, name = $"enemy-tag-{group}-{option}" };
                    tags.Add((group, option, button));
                    row.Add(button);
                }
                section.Add(row);
            }
        }

        private void SetOpen(bool open)
        {
            style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            Refresh();
        }

        private void Update(Action action)
        {
            action();
            Refresh();
            changed();
        }

        private void Refresh()
        {
            anchor.text = filter.Count == 0 ? "标签筛选" : $"标签筛选 · {filter.Count}";
            anchor.EnableInClassList("selected-tab", filter.Count > 0 || style.display == DisplayStyle.Flex);
            foreach (var (group, option, button) in tags)
                button.EnableInClassList("selected-tab", filter.Includes(group, option));
        }
    }
}
