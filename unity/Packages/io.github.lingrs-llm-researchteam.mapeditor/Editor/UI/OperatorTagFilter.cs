#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class OperatorTagFilter
    {
        internal enum Group { Profession, Branch, Rarity, Position, Trait }

        private readonly Dictionary<Group, HashSet<string>> selected = new();
        internal bool MatchAllTraits { get; set; }
        internal int Count => selected.Values.Sum(values => values.Count);

        internal bool Includes(Group group, string value) =>
            selected.TryGetValue(group, out var values) && values.Contains(value);

        internal void Toggle(Group group, string value)
        {
            if (!selected.TryGetValue(group, out var values))
                selected.Add(group, values = new HashSet<string>(StringComparer.Ordinal));
            if (!values.Remove(value)) values.Add(value);
        }

        internal void Clear()
        {
            selected.Clear();
            MatchAllTraits = false;
        }

        internal OperatorPresetCatalog.FilterForm? Match(OperatorPresetCatalog.Entry entry)
        {
            if (!Allows(Group.Rarity, entry.Rarity.ToString(System.Globalization.CultureInfo.InvariantCulture))) return null;
            return entry.FilterForms.FirstOrDefault(form => Allows(Group.Profession, form.Profession) &&
                Allows(Group.Branch, form.Branch) && AllowsPosition(form.Position) && AllowsTraits(form.Tags));
        }

        private bool Allows(Group group, string value) =>
            !selected.TryGetValue(group, out var values) || values.Count == 0 || values.Contains(value);

        private bool AllowsPosition(string position) => position == "ALL" || Allows(Group.Position, position);

        private bool AllowsTraits(IReadOnlyList<string> traits)
        {
            if (!selected.TryGetValue(Group.Trait, out var values) || values.Count == 0) return true;
            return MatchAllTraits ? values.All(traits.Contains) : traits.Any(values.Contains);
        }
    }

    internal sealed class OperatorTagFilterPanel : VisualElement
    {
        private readonly OperatorTagFilter filter;
        private readonly Button anchor;
        private readonly Button clear;
        private readonly Toggle allTraits;
        private readonly List<(OperatorTagFilter.Group Group, string Value, Button Button)> tags = new();

        internal OperatorTagFilterPanel(OperatorTagFilter filter, IReadOnlyList<OperatorPresetCatalog.Entry> entries,
            Button anchor, Action changed)
        {
            this.filter = filter;
            this.anchor = anchor;
            name = "operator-tag-filter-panel";
            AddToClassList("operator-tag-filter-panel");
            style.display = DisplayStyle.None;
            var header = new VisualElement();
            header.AddToClassList("operator-filter-header");
            header.Add(new Label("标签筛选"));
            clear = new Button(() =>
            {
                filter.Clear();
                allTraits!.SetValueWithoutNotify(false);
                Refresh();
                changed();
            }) { text = "清除", name = "operator-tag-clear" };
            header.Add(clear);
            Add(header);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("operator-tag-filter-scroll");
            Add(scroll);
            var forms = entries.SelectMany(entry => entry.FilterForms).ToArray();
            AddGroup(scroll, "职业", OperatorTagFilter.Group.Profession,
                new[] { "PIONEER", "WARRIOR", "SNIPER", "TANK", "MEDIC", "SUPPORT", "CASTER", "SPECIAL" },
                OperatorLabels.Profession, changed);
            AddGroup(scroll, "稀有度", OperatorTagFilter.Group.Rarity, new[] { "1", "2", "3", "4", "5", "6" },
                value => value + "★", changed);
            AddGroup(scroll, "位置", OperatorTagFilter.Group.Position, new[] { "MELEE", "RANGED" },
                value => value == "MELEE" ? "近战位" : "远程位", changed);

            var traits = new Foldout { text = "词缀", value = true };
            scroll.Add(traits);
            allTraits = new Toggle("同时满足所选词缀") { name = "operator-tag-all-traits" };
            allTraits.RegisterValueChangedCallback(e => { filter.MatchAllTraits = e.newValue; Refresh(); changed(); });
            traits.Add(allTraits);
            AddGroup(traits, null, OperatorTagFilter.Group.Trait, Unique(forms.SelectMany(form => form.Tags)),
                value => value, changed);

            var branches = new Foldout { text = "职业分支", value = false };
            scroll.Add(branches);
            AddGroup(branches, null, OperatorTagFilter.Group.Branch, Unique(forms.Select(form => form.Branch)),
                value => value, changed);
            anchor.clicked += () => SetOpen(style.display == DisplayStyle.None);
            RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Escape) return;
                SetOpen(false);
                anchor.Focus();
                e.StopPropagation();
            });
            Refresh();
        }

        private static IEnumerable<string> Unique(IEnumerable<string> values) =>
            values.Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal);

        private void AddGroup(VisualElement parent, string? title, OperatorTagFilter.Group group,
            IEnumerable<string> values, Func<string, string> label, Action changed)
        {
            if (title != null)
            {
                var heading = new Label(title);
                heading.AddToClassList("operator-filter-heading");
                parent.Add(heading);
            }
            var row = new VisualElement();
            row.AddToClassList("operator-filter-tags");
            parent.Add(row);
            foreach (var value in values)
            {
                var button = new Button(() =>
                {
                    filter.Toggle(group, value);
                    Refresh();
                    changed();
                }) { text = label(value), name = $"operator-tag-{group}-{value}" };
                tags.Add((group, value, button));
                row.Add(button);
            }
        }

        private void SetOpen(bool open)
        {
            style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            Refresh();
        }

        private void Refresh()
        {
            var count = filter.Count;
            anchor.text = count == 0 ? "标签筛选" : $"标签筛选 · {count}";
            anchor.EnableInClassList("selected-tab", count > 0 || style.display == DisplayStyle.Flex);
            clear.SetEnabled(count > 0 || filter.MatchAllTraits);
            foreach (var (group, value, button) in tags)
                button.EnableInClassList("selected-tab", filter.Includes(group, value));
        }
    }
}
