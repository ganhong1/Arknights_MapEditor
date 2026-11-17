#nullable enable
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Editor
{
    internal sealed class OfflineArtSceneImageView
    {
        private float zoom = 1f;
        private Vector2 pan;
        private int dragControl;

        internal void Reset()
        {
            EndDrag();
            zoom = 1f;
            pan = Vector2.zero;
        }

        internal void Draw(Texture2D image)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("−", EditorStyles.toolbarButton)) Step(1f / 1.3f, Vector2.zero);
                if (GUILayout.Button("适应窗口", EditorStyles.toolbarButton)) Reset();
                if (GUILayout.Button("+", EditorStyles.toolbarButton)) Step(1.3f, Vector2.zero);
                GUILayout.Label($"{zoom * 100f:0}%", EditorStyles.miniLabel, GUILayout.Width(48));
                GUILayout.FlexibleSpace();
                GUILayout.Label("滚轮缩放 · 拖动平移", EditorStyles.miniLabel);
            }

            var area = GUILayoutUtility.GetRect(220, 140, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            var fit = Mathf.Min(area.width / image.width, area.height / image.height);
            var control = GUIUtility.GetControlID(FocusType.Passive, area);
            var input = Event.current;
            switch (input.GetTypeForControl(control))
            {
                case EventType.ScrollWheel when area.Contains(input.mousePosition):
                    Step(Mathf.Pow(1.18f, -input.delta.y), input.mousePosition - area.center);
                    input.Use();
                    break;
                case EventType.MouseDown when area.Contains(input.mousePosition) && zoom > 1f && (input.button == 0 || input.button == 2):
                    dragControl = GUIUtility.hotControl = control;
                    input.Use();
                    break;
                case EventType.MouseDrag when dragControl == control && GUIUtility.hotControl == control:
                    pan += input.delta;
                    input.Use();
                    break;
                case EventType.MouseUp when dragControl == control && GUIUtility.hotControl == control:
                    EndDrag();
                    input.Use();
                    break;
            }

            if (area.width <= 1f || area.height <= 1f) return;
            var size = new Vector2(image.width, image.height) * (fit * zoom);
            pan = ClampPan(pan, size, area.size);
            EditorGUIUtility.AddCursorRect(area, zoom > 1f ? MouseCursor.Pan : MouseCursor.Arrow);
            if (input.type != EventType.Repaint) return;
            EditorGUI.DrawRect(area, new Color(.08f, .095f, .11f));
            GUI.BeginGroup(area);
            try
            {
                var rect = new Rect((area.size - size) * .5f + pan, size);
                GUI.DrawTexture(rect, image, ScaleMode.StretchToFill, false);
            }
            finally { GUI.EndGroup(); }
        }

        internal void EndDrag()
        {
            if (dragControl != 0 && GUIUtility.hotControl == dragControl) GUIUtility.hotControl = 0;
            dragControl = 0;
        }

        private void Step(float multiplier, Vector2 anchor)
        {
            var next = Mathf.Clamp(zoom * multiplier, 1f, 8f);
            pan = AnchoredPan(pan, anchor, zoom, next);
            zoom = next;
            if (zoom == 1f) pan = Vector2.zero;
        }

        internal static Vector2 AnchoredPan(Vector2 oldPan, Vector2 anchor, float oldZoom, float newZoom) =>
            anchor - (anchor - oldPan) * (newZoom / oldZoom);

        internal static Vector2 ClampPan(Vector2 offset, Vector2 imageSize, Vector2 viewport)
        {
            var limit = Vector2.Max(Vector2.zero, (imageSize - viewport) * .5f);
            return new Vector2(Mathf.Clamp(offset.x, -limit.x, limit.x), Mathf.Clamp(offset.y, -limit.y, limit.y));
        }
    }
}
