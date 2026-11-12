#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        [SerializeField] private float editorPanelWidth = 380;
        [SerializeField] private float libraryPanelWidth = 280;
        private Action? resizeWorkbenchPanels;

        private VisualElement BuildPanelSplitter(VisualElement body, VisualElement panel, bool left)
        {
            var handle = new VisualElement
                { name = left ? "editor-splitter" : "resource-splitter" };
            handle.AddToClassList("workbench-splitter");
            resizeWorkbenchPanels = () =>
            {
                var width = body.contentRect.width;
                if (float.IsNaN(width) || width <= 0) return;
                // Reserve the center before sizing either optional side panel.
                var available = Mathf.Max(0, width - Mathf.Min(240, width * .4f)
                    - (editorDrawerOpen ? 7 : 0) - (resourceDrawerOpen ? 7 : 0));
                var desired = (editorDrawerOpen ? editorPanelWidth : 0) + (resourceDrawerOpen ? libraryPanelWidth : 0);
                var scale = desired > 0 ? Mathf.Min(1, available / desired) : 1;
                var editor = body.Q("editor-drawer");
                var library = body.Q("resource-drawer");
                if (editor != null) editor.style.width = editorPanelWidth * scale;
                if (library != null) library.style.width = libraryPanelWidth * scale;
            };
            var dragging = false;
            var startX = 0f;
            var startWidth = 0f;
            handle.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                dragging = true; startX = evt.mousePosition.x; startWidth = panel.resolvedStyle.width;
                handle.CaptureMouse(); evt.StopPropagation();
            });
            handle.RegisterCallback<MouseMoveEvent>(evt =>
            {
                if (!dragging || !handle.HasMouseCapture()) return;
                var delta = (evt.mousePosition.x - startX) * (left ? 1 : -1);
                var other = body.Q(left ? "resource-drawer" : "editor-drawer");
                var otherOpen = left ? resourceDrawerOpen : editorDrawerOpen;
                var otherWidth = otherOpen && other != null ? other.resolvedStyle.width + 7 : 0;
                var maximum = Mathf.Max(100, body.contentRect.width - 247 - otherWidth);
                var width = Mathf.Clamp(startWidth + delta, Mathf.Min(160, maximum), maximum);
                if (left) editorPanelWidth = width; else libraryPanelWidth = width;
                resizeWorkbenchPanels?.Invoke(); evt.StopPropagation();
            });
            handle.RegisterCallback<MouseUpEvent>(evt =>
            {
                if (evt.button != 0 || !dragging) return;
                dragging = false; handle.ReleaseMouse(); evt.StopPropagation();
            });
            handle.RegisterCallback<MouseCaptureOutEvent>(_ => dragging = false);
            return handle;
        }
    }
}
