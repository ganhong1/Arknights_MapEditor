#nullable enable
using System;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Editor
{
    internal sealed class ViewportOrientationGizmo
    {
        private static readonly Vector3[] Directions =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
        };
        private static readonly string[] Labels = { "X", "", "Y", "", "Z", "" };
        private static readonly string[] Names = { "右视", "左视", "顶视", "底视", "后视", "前视" };
        private static readonly Color[] Colors =
        {
            new Color(.89f, .36f, .32f), Color.gray, new Color(.62f, .84f, .31f), Color.gray,
            new Color(.33f, .56f, .9f), Color.gray
        };
        private readonly int[] drawOrder = new int[Directions.Length];
        private readonly Vector3[] axes = new Vector3[Directions.Length];
        private readonly Vector3[] triangle = new Vector3[3];
        private readonly Vector3[] face = new Vector3[4];
        private GUIStyle? label;
        private GUIStyle? projection;
        private const float Scale = 32;

        internal void Draw(Rect rect, MapViewportState state, Action<int> snap, Action toggleProjection)
        {
            var current = Event.current;
            if (current.type == EventType.ContextClick && rect.Contains(current.mousePosition))
            {
                if (!state.CameraLocked)
                {
                    var menu = new GenericMenu();
                    for (var i = 0; i < Names.Length; i++)
                    {
                        var index = i;
                        menu.AddItem(new GUIContent(Names[i]), false, () => snap(index));
                    }
                    menu.ShowAsContext();
                }
                current.Use();
                return;
            }
            label ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
            projection ??= new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter };
            var rotation = Quaternion.Inverse(state.Rotation);
            var pivot = rect.center - Vector2.up * 6;
            for (var i = 0; i < axes.Length; i++)
            {
                axes[i] = rotation * Directions[i];
                drawOrder[i] = i;
            }
            Array.Sort(drawOrder, (a, b) => axes[b].z.CompareTo(axes[a].z));
            if (current.type == EventType.Repaint) DrawShape(pivot, rotation, state.CameraLocked);
            using (new EditorGUI.DisabledScope(state.CameraLocked))
            {
                // Input priority follows the visible foreground of the gizmo.
                for (var i = drawOrder.Length - 1; i >= 0; i--)
                {
                    var index = drawOrder[i];
                    if (IsEdgeOn(axes[index])) continue;
                    if (GUI.Button(AxisHit(pivot, axes[index]), new GUIContent("", Names[index]), GUIStyle.none))
                        snap(index);
                }
                if (GUI.Button(new Rect(pivot.x - 9, pivot.y - 9, 18, 18), new GUIContent("", "切换投影"), GUIStyle.none))
                    toggleProjection();
                if (GUI.Button(new Rect(rect.x + 17, rect.yMax - 17, 64, 17),
                    state.Orthographic ? "Ortho" : "Persp", projection)) toggleProjection();
            }
        }

        private void DrawShape(Vector2 pivot, Quaternion rotation, bool locked)
        {
            var fade = locked ? .55f : 1;
            foreach (var index in drawOrder)
                if (axes[index].z >= 0) DrawCone(pivot, index, fade);
            DrawCube(pivot, rotation, fade);
            foreach (var index in drawOrder)
                if (axes[index].z < 0) DrawCone(pivot, index, fade);
            foreach (var index in drawOrder)
            {
                if (Labels[index].Length == 0 || IsEdgeOn(axes[index])) continue;
                var point = Project(pivot, axes[index] * 1.27f);
                var previous = GUI.contentColor;
                GUI.contentColor = Colors[index] * new Color(1, 1, 1, fade);
                try { GUI.Label(new Rect(point.x - 8, point.y - 7, 16, 14), Labels[index], label!); }
                finally { GUI.contentColor = previous; }
            }
        }

        private void DrawCone(Vector2 pivot, int index, float fade)
        {
            var axis = axes[index];
            if (IsEdgeOn(axis)) return;
            var normal = new Vector3(axis.y, -axis.x, 0).normalized * .17f;
            var color = Colors[index];
            if (AxisHit(pivot, axis).Contains(Event.current.mousePosition) && fade == 1)
                color = Color.Lerp(color, Color.white, .33f);
            color.a = fade;
            var tip = Project(pivot, axis);
            var baseCenter = Project(pivot, axis * .31f);
            triangle[0] = tip;
            triangle[1] = Project(pivot, axis * .31f + normal);
            triangle[2] = baseCenter;
            using (new Handles.DrawingScope(color)) Handles.DrawAAConvexPolygon(triangle);
            triangle[1] = baseCenter;
            triangle[2] = Project(pivot, axis * .31f - normal);
            using (new Handles.DrawingScope(new Color(color.r * .69f, color.g * .69f, color.b * .69f, fade)))
                Handles.DrawAAConvexPolygon(triangle);
        }

        private void DrawCube(Vector2 pivot, Quaternion rotation, float fade)
        {
            foreach (var normal in Directions)
            {
                var facing = rotation * normal;
                if (facing.z >= -.001f) continue;
                var side = Vector3.Cross(normal, Mathf.Abs(normal.y) > .5f ? Vector3.right : Vector3.up);
                var up = Vector3.Cross(normal, side);
                face[0] = Project(pivot, rotation * ((normal - side - up) * .23f));
                face[1] = Project(pivot, rotation * ((normal + side - up) * .23f));
                face[2] = Project(pivot, rotation * ((normal + side + up) * .23f));
                face[3] = Project(pivot, rotation * ((normal - side + up) * .23f));
                var tint = .58f + .29f * Mathf.Abs(facing.z);
                using (new Handles.DrawingScope(new Color(tint, tint, tint, fade))) Handles.DrawAAConvexPolygon(face);
            }
        }

        private static bool IsEdgeOn(Vector3 axis) => axis.x * axis.x + axis.y * axis.y < .16f;
        private static Vector3 Project(Vector2 pivot, Vector3 point) =>
            new Vector3(pivot.x + point.x * Scale, pivot.y - point.y * Scale, 0);

        private static Rect AxisHit(Vector2 pivot, Vector3 axis)
        {
            var point = Project(pivot, axis * .86f);
            return new Rect(point.x - 9, point.y - 8, 18, 16);
        }
    }
}
