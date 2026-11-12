#nullable enable
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Editor
{
    internal static class PrtsRouteOverlay
    {
        private static readonly Color Ground = new Color32(217, 38, 38, 220);
        private static readonly Color Air = new Color32(214, 194, 92, 230);
        private static readonly Color Shadow = new Color32(25, 16, 16, 235);
        private static readonly Color GroundLeading = new Color32(250, 209, 216, 242);
        private static readonly Color AirLeading = new Color32(255, 255, 204, 242);
        private static readonly Color Missing = new Color32(220, 153, 95, 238);
        private static readonly Vector3[] Line = new Vector3[2];
        private static GUIStyle? markerStyle;

        internal static void Draw(PrtsRoutePreview.Path path, bool focused, float distance)
        {
            var tint = path.Air ? Air : Ground;
            if (!focused) tint.a *= .60f;
            var width = focused ? 3f : 2f;
            var head = distance < path.Length;
            using (new Handles.DrawingScope(Shadow))
                foreach (var leg in path.Legs)
                    if (!leg.Fallback) DrawPortion(leg, 0, distance, width + 2);
            using (new Handles.DrawingScope(tint))
                foreach (var leg in path.Legs)
                    if (!leg.Fallback) DrawPortion(leg, 0, distance, width);
            if (path.HasFallback)
                using (new Handles.DrawingScope(Missing))
                    foreach (var leg in path.Legs)
                        if (leg.Fallback && distance > leg.Offset)
                            Handles.DrawDottedLine(leg.From, Vector3.Lerp(leg.From, leg.To,
                                Mathf.Clamp01((distance - leg.Offset) / leg.Length)), 5);
            if (head)
                using (new Handles.DrawingScope(path.Air ? AirLeading : GroundLeading))
                    foreach (var leg in path.Legs)
                        if (!leg.Fallback)
                        {
                            DrawPortion(leg, Mathf.Max(0, distance - 1.20f), distance, width);
                            DrawPortion(leg, Mathf.Max(0, distance - 3.04f), distance - 1.84f, width);
                        }
            using (new Handles.DrawingScope(tint))
            {
                for (var i = 0; i < path.Markers.Count; ++i)
                {
                    var marker = path.Markers[i];
                    if (marker.Offset > distance + .0001f) continue;
                    if (!focused && marker.Kind is not (RouteMarkerKind.Start or RouteMarkerKind.End)) continue;
                    DrawMarker(marker, focused, i, path);
                }
            }
        }

        private static void DrawPortion(PrtsRoutePreview.Leg leg, float from, float to, float width)
        {
            if (to <= leg.Offset || from >= leg.Offset + leg.Length || from >= to) return;
            Line[0] = Vector3.Lerp(leg.From, leg.To, Mathf.Clamp01((from - leg.Offset) / leg.Length));
            Line[1] = Vector3.Lerp(leg.From, leg.To, Mathf.Clamp01((to - leg.Offset) / leg.Length));
            Handles.DrawAAPolyLine(width, Line);
        }

        private static void DrawMarker(PrtsRoutePreview.Marker marker, bool focused, int index, PrtsRoutePreview.Path path)
        {
            var point = marker.Point + Vector3.up * .018f;
            var radius = marker.Kind switch
            {
                RouteMarkerKind.Wait => .30f,
                RouteMarkerKind.Checkpoint => .12f,
                _ => .18f
            };
            if (marker.Kind is RouteMarkerKind.Wait or RouteMarkerKind.Appear or RouteMarkerKind.Disappear)
                using (new Handles.DrawingScope(new Color(.18f, .17f, .19f, .94f)))
                    Handles.DrawSolidDisc(point, Vector3.up, radius);
            Handles.DrawWireDisc(point, Vector3.up, radius, focused ? 2.5f : 1.5f);
            if (marker.Kind == RouteMarkerKind.Start) Handles.DrawSolidDisc(point, Vector3.up, .08f);
            if (marker.Kind == RouteMarkerKind.End) Handles.DrawWireDisc(point, Vector3.up, .09f, 1.5f);
            if (!focused || marker.Label.Length == 0) return;
            markerStyle ??= new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(.96f, .93f, .93f) }
            };
            // Several consecutive checkpoints can share the same visible position.
            // Offset their captions so a hidden wait and a disappear marker both remain readable.
            var labelRow = 0;
            for (var previous = 0; previous < index; ++previous)
            {
                var other = path.Markers[previous];
                if (other.Label.Length != 0 && (other.Point - marker.Point).sqrMagnitude < .001f) ++labelRow;
            }
            var label = new GUIContent(marker.Label);
            var offset = HandleUtility.WorldToGUIPoint(point) + new Vector2(0, -labelRow * 15f);
            var size = markerStyle.CalcSize(label);
            Handles.BeginGUI();
            GUI.Label(new Rect(offset - size * .5f, size), label, markerStyle);
            Handles.EndGUI();
        }
    }
}
