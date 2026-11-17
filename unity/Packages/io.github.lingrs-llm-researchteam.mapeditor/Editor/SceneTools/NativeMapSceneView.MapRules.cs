#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class NativeMapSceneView
    {
        private int ruleControl;
        private readonly HashSet<Vector3Int> ruleStroke = new();
        private Vector3Int? ruleHover, ruleStart, rulePrevious;
        private LevelDocument? ruleStartDocument;
        private string ruleField = "";
        private bool ruleErase, ruleRectangle;
        private int ruleMask;
        private bool CanEditMapRules => editor is { WorkspaceEditLocked: false } && owner?.OwnsRouteInput(editor, group) == true;

        private void ResetMapRuleGesture()
        {
            if (ruleControl != 0 && GUIUtility.hotControl == ruleControl) GUIUtility.hotControl = 0;
            ruleControl = 0; ruleStroke.Clear(); ruleHover = ruleStart = rulePrevious = null; ruleStartDocument = null;
        }
        private void HandleMapRuleInput(Event input, EventType? capturedType = null)
        {
            var type = capturedType ?? input.type;
            if (type == EventType.MouseDown) owner?.ActivateNativeGroup(group);
            if (!CanEditMapRules || type == EventType.Ignore || ruleControl != 0 &&
                (focusedWindow != this || input.alt || GUIUtility.hotControl != ruleControl)) { ResetMapRuleGesture(); return; }
            if (type == EventType.KeyDown && input.keyCode == KeyCode.Escape)
            { if (ruleControl == 0) editor!.ExitMapRuleScene(); ResetMapRuleGesture(); input.Use(); Repaint(); return; }
            if (input.alt || focusedWindow != this && type != EventType.MouseDown) return;
            var control = GUIUtility.GetControlID(GetInstanceID() ^ 0x4d52, FocusType.Passive);
            if (type == EventType.Layout) HandleUtility.AddDefaultControl(control);
            try
            {
                if (type is EventType.MouseMove or EventType.MouseDown or EventType.MouseDrag)
                { ruleHover = PickMapRule(input, editor!.MapRuleSceneField); Repaint(); }
                if (type == EventType.MouseLeaveWindow && ruleControl == 0) { ruleHover = null; Repaint(); }
                if (type == EventType.MouseDown && input.button == 0 && GUIUtility.hotControl == 0 && ruleHover.HasValue)
                {
                    editor!.RequireMapRuleGesture(editor.MapRuleSceneField);
                    ruleField = editor.MapRuleSceneField; ruleErase = editor.MapRuleErase; ruleMask = editor.MapRuleMask;
                    ruleRectangle = editor.MapRuleRectangle && ruleField != "blockEdges";
                    ruleStartDocument = renderedDocument; ruleStart = ruleHover;
                    ruleControl = GUIUtility.hotControl = control; AddRulePoint(ruleHover.Value); input.Use();
                }
                else if (ruleControl != 0 && GUIUtility.hotControl == ruleControl)
                {
                    if (type == EventType.MouseDrag)
                    { if (ruleHover.HasValue) AddRulePoint(ruleHover.Value); input.Use(); }
                    else if (type == EventType.MouseUp && input.button == 0)
                    {
                        var cells = ruleStroke.ToArray(); var doc = ruleStartDocument; var field = ruleField;
                        var erase = ruleErase; var mask = ruleMask;
                        ResetMapRuleGesture(); input.Use();
                        if (doc != null) editor!.ApplyMapRuleGesture(field, cells, erase, mask, doc);
                        Repaint();
                    }
                }
            }
            catch (Exception error)
            { ResetMapRuleGesture(); owner?.ReportNativeError("scene.map-rule", error); Repaint(); }
        }
        private void AddRulePoint(Vector3Int point)
        {
            if (ruleRectangle && ruleStart is Vector3Int start)
            {
                ruleStroke.Clear();
                for (var row = Mathf.Min(start.y, point.y); row <= Mathf.Max(start.y, point.y); row++)
                    for (var col = Mathf.Min(start.x, point.x); col <= Mathf.Max(start.x, point.x); col++)
                        ruleStroke.Add(new Vector3Int(col, row, 0));
            }
            else if (ruleField != "blockEdges" && rulePrevious is Vector3Int previous)
            {
                var count = Mathf.Max(1, Mathf.Abs(point.x - previous.x), Mathf.Abs(point.y - previous.y));
                for (var i = 0; i <= count; i++)
                { var p = Vector3.Lerp(previous, point, i / (float)count); ruleStroke.Add(new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), 0)); }
            }
            else if (ruleField == "blockEdges" && rulePrevious is Vector3Int old && old.z == point.z &&
                ((point.z % 2 == 0 && old.y == point.y) || (point.z % 2 == 1 && old.x == point.x)))
            {
                var count = Mathf.Max(1, Mathf.Abs(point.x - old.x), Mathf.Abs(point.y - old.y));
                for (var i = 0; i <= count; i++)
                { var p = Vector3.Lerp(old, point, i / (float)count); ruleStroke.Add(new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), point.z)); }
            }
            else ruleStroke.Add(point);
            rulePrevious = point;
        }
        private Vector3Int? PickMapRule(Event input, string field)
        {
            if (field != "blockEdges") return TryPick(input, out var cell) ? new Vector3Int(cell.x, cell.y, 0) : (Vector3Int?)null;
            // The skeleton has gaps between tiles; use projected boundary segments rather than colliders.
            var ray = HandleUtility.GUIPointToWorldRay(input.mousePosition);
            var candidates = new HashSet<Vector2Int>();
            if (TryPick(input, out var picked)) candidates.Add(picked);
            foreach (var height in new[] { 0f, SkeletonStyle.BlockTop, SkeletonStyle.BoxTop, SkeletonStyle.RecessTop, SkeletonStyle.TrenchTop })
                if (new Plane(Vector3.up, new Vector3(0, height, 0)).Raycast(ray, out var distance))
                {
                    var point = ray.GetPoint(distance); var center = new Vector2Int(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.z));
                    for (var y = -1; y <= 1; y++) for (var x = -1; x <= 1; x++) candidates.Add(center + new Vector2Int(x, y));
                }
            var best = 9f; Vector3Int? result = null;
            foreach (var cell in candidates.OrderBy(cell => cell.y).ThenBy(cell => cell.x))
            {
                if (cell.x < 0 || cell.y < 0 || cell.x >= renderedDocument!.Width || cell.y >= renderedDocument.Height) continue;
                for (var direction = 0; direction < 4; direction++)
                {
                    var edge = new Vector3Int(cell.x, cell.y, direction); EdgePoints(edge, out var a, out var b);
                    if (camera.WorldToViewportPoint(a).z <= 0 || camera.WorldToViewportPoint(b).z <= 0) continue;
                    var distance = HandleUtility.DistancePointToLineSegment(input.mousePosition, HandleUtility.WorldToGUIPoint(a), HandleUtility.WorldToGUIPoint(b));
                    if (distance >= best) continue;
                    best = distance; result = edge;
                }
            }
            return result;
        }
        private void EdgePoints(Vector3Int cell, out Vector3 a, out Vector3 b)
        {
            var height = SkeletonStyle.SurfaceHeight(renderedDocument!.GetCell(cell.y, cell.x)) + .035f;
            var x = (float)cell.x; var z = (float)cell.y;
            switch (cell.z)
            {
                case 0: a = new Vector3(x, height, z + 1); b = new Vector3(x + 1, height, z + 1); break;
                case 1: a = new Vector3(x + 1, height, z); b = new Vector3(x + 1, height, z + 1); break;
                case 2: a = new Vector3(x, height, z); b = new Vector3(x + 1, height, z); break;
                default: a = new Vector3(x, height, z); b = new Vector3(x, height, z + 1); break;
            }
        }
        private void DrawMapRuleOverlay()
        {
            if (editor == null || renderedDocument == null) return;
            foreach (var field in new[] { "blockEdges", "tilesDisallowToLocate" })
            {
            if (!editor.MapRuleOverlayVisible(field)) continue;
            if (renderedDocument.GetMapRules(field) is JArray rules)
                foreach (var rule in rules.OfType<JObject>())
                {
                    var pos = field == "blockEdges" ? rule["pos"] as JObject : rule;
                    var row = MapEditorWindow.RuleInt(pos?["row"]); var col = MapEditorWindow.RuleInt(pos?["col"]);
                    var direction = field == "blockEdges" ? MapEditorWindow.RuleDirection(rule["direction"]) : 0;
                    if (!row.HasValue || !col.HasValue || !direction.HasValue || direction < 0 || direction > 3 ||
                        row < 0 || col < 0 || row >= renderedDocument.Height || col >= renderedDocument.Width) continue;
                    var mask = field == "blockEdges" ? MapEditorWindow.RuleMask(rule["blockMask"]) : 3;
                    var color = mask switch { 0 => Color.gray, 1 => new Color(1f,.5f,.1f), 2 => Color.cyan, 3 => new Color(.8f,.4f,1f), _ => Color.red };
                    DrawRule(new Vector3Int(col.Value, row.Value, direction.Value), field, color, mask == 0);
                }
            }
            if (!editor.MapRuleSceneActive) return;
            foreach (var point in ruleStroke) DrawRule(point, editor.MapRuleSceneField, ruleErase ? Color.red : Color.cyan);
            if (ruleHover.HasValue && CanEditMapRules) DrawRule(ruleHover.Value, editor.MapRuleSceneField, Color.yellow);
        }
        private void DrawRule(Vector3Int point, string field, Color color, bool dashed = false)
        {
            using (new Handles.DrawingScope(color))
            {
                if (field == "blockEdges") { EdgePoints(point, out var a, out var b); if (dashed) Handles.DrawDottedLine(a, b, 5f); else Handles.DrawAAPolyLine(4f, a, b); }
                else
                {
                    var height = SkeletonStyle.SurfaceHeight(renderedDocument!.GetCell(point.y, point.x)) + .04f;
                    var x = point.x; var y = point.y;
                    Handles.DrawSolidRectangleWithOutline(new[] { new Vector3(x+.08f,height,y+.08f),new Vector3(x+.92f,height,y+.08f),new Vector3(x+.92f,height,y+.92f),new Vector3(x+.08f,height,y+.92f) }, new Color(color.r,color.g,color.b,.16f),color);
                }
            }
        }
    }
}
