#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class ResourceSubcategoryList : VisualElement
    {
        private const float RowHeight = 28;
        private const float DefaultHeight = 5 * RowHeight;
        private const float MaximumHeight = 14 * RowHeight;
        private readonly VisualElement owner;
        private readonly Foldout foldout = new() { value = true };
        private readonly ScrollView scroll = new(ScrollViewMode.Vertical);
        private readonly VisualElement handle = new() { name = "resource-subfilter-resize", focusable = true };
        private readonly Dictionary<string, Button> buttons = new(StringComparer.Ordinal);
        private string title;
        private float intendedHeight = DefaultHeight;
        private float startY;
        private float startHeight;
        private bool dragging;

        internal string Value { get; private set; } = "";
        internal event Action? Changed;

        internal ResourceSubcategoryList(VisualElement owner, string name, string title)
        {
            this.owner = owner;
            this.name = name;
            this.title = title;
            AddToClassList("resource-subfilter-frame");
            foldout.AddToClassList("resource-subfilter-foldout");
            scroll.name = "resource-subfilter-scroll";
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            foldout.Add(scroll);
            handle.tooltip = "拖动调整高度，双击恢复默认。聚焦后也可用上下方向键调整。";
            handle.AddToClassList("resource-subfilter-resize");
            var grip = new VisualElement { pickingMode = PickingMode.Ignore };
            grip.AddToClassList("resource-subfilter-grip");
            handle.Add(grip);
            foldout.Add(handle);
            Add(foldout);
            ConfigureDrag();
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                owner.RegisterCallback<GeometryChangedEvent>(ResizeForOwner);
                UpdateHeight();
            });
            RegisterCallback<DetachFromPanelEvent>(_ => owner.UnregisterCallback<GeometryChangedEvent>(ResizeForOwner));
        }

        internal void ShowChoices(string label, IEnumerable<string> choices, string selected)
        {
            title = label;
            var options = choices.ToArray();
            if (!buttons.Keys.SequenceEqual(options, StringComparer.Ordinal))
            {
                buttons.Clear();
                scroll.Clear();
                scroll.scrollOffset = Vector2.zero;
                foreach (var option in options)
                {
                    var button = new Button(() => Select(option)) { text = option, tooltip = option };
                    button.AddToClassList("resource-subfilter-option");
                    buttons.Add(option, button);
                    scroll.Add(button);
                }
            }
            SetSelection(buttons.ContainsKey(selected) ? selected : options.FirstOrDefault() ?? "");
            UpdateHeight();
        }

        private void Select(string value)
        {
            if (value == Value) return;
            SetSelection(value);
            Changed?.Invoke();
            if (buttons.TryGetValue(value, out var button)) button.Focus();
        }

        private void SetSelection(string value)
        {
            if (buttons.TryGetValue(Value, out var last)) last.RemoveFromClassList("selected-tab");
            Value = value;
            if (buttons.TryGetValue(value, out var active)) active.AddToClassList("selected-tab");
            foldout.text = value.Length == 0 ? title : title + " · " + value;
            foldout.tooltip = foldout.text;
        }

        private void ResizeForOwner(GeometryChangedEvent change) => UpdateHeight();

        private float HeightLimit
        {
            get
            {
                var available = owner.contentRect.height;
                var maximum = available > 0 ? Mathf.Min(MaximumHeight, Mathf.Max(RowHeight, available - 180)) : MaximumHeight;
                return Mathf.Min(maximum, Mathf.Max(RowHeight, buttons.Count * RowHeight));
            }
        }

        private void UpdateHeight()
        {
            var limit = HeightLimit;
            scroll.style.height = Mathf.Clamp(intendedHeight, Mathf.Min(2 * RowHeight, limit), limit);
            handle.style.display = buttons.Count * RowHeight > Mathf.Min(DefaultHeight, limit)
                ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void Resize(float height)
        {
            var limit = HeightLimit;
            intendedHeight = Mathf.Clamp(height, Mathf.Min(2 * RowHeight, limit), limit);
            UpdateHeight();
        }

        private void ConfigureDrag()
        {
            handle.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                handle.Focus();
                if (evt.clickCount == 2)
                {
                    intendedHeight = DefaultHeight;
                    UpdateHeight();
                }
                else
                {
                    startY = evt.mousePosition.y;
                    startHeight = scroll.resolvedStyle.height;
                    dragging = true;
                    handle.CaptureMouse();
                }
                evt.StopPropagation();
            });
            handle.RegisterCallback<MouseMoveEvent>(evt =>
            {
                if (!dragging || !handle.HasMouseCapture()) return;
                Resize(startHeight + evt.mousePosition.y - startY);
                evt.StopPropagation();
            });
            handle.RegisterCallback<MouseUpEvent>(evt =>
            {
                if (!dragging || evt.button != 0) return;
                dragging = false;
                handle.ReleaseMouse();
                evt.StopPropagation();
            });
            handle.RegisterCallback<MouseCaptureOutEvent>(_ => dragging = false);
            handle.RegisterCallback<KeyDownEvent>(evt =>
            {
                var direction = evt.keyCode == KeyCode.UpArrow ? -1 : evt.keyCode == KeyCode.DownArrow ? 1 : 0;
                if (direction == 0) return;
                Resize(scroll.resolvedStyle.height + direction * RowHeight);
                evt.StopPropagation();
                evt.PreventDefault();
            });
        }
    }
}
