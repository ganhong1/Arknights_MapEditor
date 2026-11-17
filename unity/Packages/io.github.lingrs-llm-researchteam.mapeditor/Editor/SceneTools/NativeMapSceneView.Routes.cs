#nullable enable
using System;
using System.Linq;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class NativeMapSceneView
    {
        private const float NodePickRadius = 11f;
        private const float SegmentPickRadius = 9f;
        private int routeControl;
        private int? draggedRouteNode;
        private Vector2Int? routeHover;
        private Vector2Int? routeDragCell;
        private PrtsRoutePreview? routeGeometry;
        private PrtsRoutePreview.Path? editPath;
        private LevelDocument? geometryDocument;
        private int geometryRevision = -1;
        private Vector2Int? geometryHover;
        private Vector2Int? geometryDrag;
        private int? geometryNode;
        private bool geometryDrawing;
        private bool geometryForPreview;

        private bool CanUseRouteScene => editor is { WorkspaceEditLocked: false } &&
            owner?.OwnsRouteInput(editor, group) == true;

        private void HandleRouteInput(Event input)
        {
            if (input.type == EventType.MouseDown) owner?.ActivateNativeGroup(group);
            if (!CanUseRouteScene || input.type == EventType.Ignore ||
                routeControl != 0 && (focusedWindow != this || input.alt || GUIUtility.hotControl != routeControl))
            {
                ResetRouteGesture();
                return;
            }
            if (input.type == EventType.KeyDown && input.keyCode == KeyCode.Escape)
            {
                if (routeControl == 0) editor!.ExitRouteScene();
                ResetRouteGesture();
                input.Use();
                return;
            }
            if (input.alt || focusedWindow != this && input.type != EventType.MouseDown) return;
            var control = GUIUtility.GetControlID(GetInstanceID() ^ 0x7254, FocusType.Passive);
            if (input.type == EventType.Layout) HandleUtility.AddDefaultControl(control);
            try
            {
                ProcessRouteInput(input, control);
            }
            catch (Exception error)
            {
                ResetRouteGesture();
                editor!.ExitRouteScene();
                owner?.ReportNativeError("scene.route-edit", error);
            }
        }

        private void ProcessRouteInput(Event input, int control)
        {
            var active = editor!.ActiveRouteDraft;
            if (active == null) return;
            if (input.type == EventType.KeyDown && editor.RouteSceneDrawing &&
                !EditorGUIUtility.editingTextField)
            {
                if (input.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
                {
                    if (editor.FinishRouteDrawing()) input.Use();
                }
                else if (input.keyCode == KeyCode.Backspace)
                {
                    editor.RemoveLastRoutePoint();
                    input.Use();
                }
                return;
            }
            if (input.type is EventType.MouseMove or EventType.MouseDrag or EventType.MouseDown)
            {
                Vector2Int? cell = TryPick(input, out var picked) ? picked : null;
                if (routeHover != cell || input.type == EventType.MouseMove) Repaint();
                routeHover = cell;
            }
            if (input.type == EventType.MouseLeaveWindow && routeControl == 0)
            {
                routeHover = null;
                Repaint();
            }
            if (routeControl != 0 && GUIUtility.hotControl == routeControl)
            {
                if (input.type == EventType.MouseDrag && routeHover is Vector2Int cell)
                {
                    if (routeDragCell != cell) Repaint();
                    routeDragCell = cell;
                    input.Use();
                }
                else if (input.type == EventType.MouseUp && input.button == 0)
                {
                    var node = draggedRouteNode;
                    var moved = routeDragCell.HasValue;
                    ResetRouteGesture();
                    if (node is int index && moved && TryPick(input, out var released) &&
                        active.TryNodeCell(index, out var previous) && previous != released)
                        editor.MoveRouteNode(index, released);
                    input.Use();
                }
                return;
            }
            if (input.type != EventType.MouseDown || input.button != 0 || GUIUtility.hotControl != 0) return;
            if ((editor.RouteSceneDrawing || editor.RouteScenePickAppearance) && routeHover is Vector2Int grid)
            {
                editor.PlaceRouteCell(grid);
                input.Use();
                return;
            }
            if (editor.RouteSceneDrawing || editor.RouteScenePickAppearance) return;
            if (PickRouteNode(active, input.mousePosition) is int pickedNode)
            {
                editor.SelectRouteNode(pickedNode);
                draggedRouteNode = pickedNode;
                routeControl = GUIUtility.hotControl = control;
                input.Use();
                return;
            }
            if (PickRouteInsertion(CurrentEditPath(), input.mousePosition) is { } insertion)
            {
                editor.InsertRouteNode(insertion.Index, insertion.Cell);
                input.Use();
            }
        }

        private int? PickRouteNode(RouteEditDraft active, Vector2 mouse)
        {
            var hit = active.Handles()
                .Select(node => (node.Index, Distance: (HandleUtility.WorldToGUIPoint(node.Point) - mouse).sqrMagnitude))
                .Where(node => node.Distance <= NodePickRadius * NodePickRadius)
                .OrderBy(node => node.Distance)
                .ThenByDescending(node => node.Index == editor!.SelectedRouteNode)
                .Select(node => (int?)node.Index).FirstOrDefault();
            return hit;
        }

        private (int Index, Vector2Int Cell)? PickRouteInsertion(PrtsRoutePreview.Path? path, Vector2 mouse)
        {
            if (path == null) return null;
            PrtsRoutePreview.Leg? nearest = null;
            var ambiguous = false;
            var best = SegmentPickRadius;
            foreach (var leg in path.Legs)
            {
                if (leg.Fallback) continue;
                var from = HandleUtility.WorldToGUIPoint(leg.From);
                var to = HandleUtility.WorldToGUIPoint(leg.To);
                var distance = HandleUtility.DistancePointToLineSegment(mouse, from, to);
                if (distance > SegmentPickRadius) continue;
                if (nearest is PrtsRoutePreview.Leg previous && Mathf.Abs(distance - best) < 1 &&
                    previous.InsertBefore != leg.InsertBefore) ambiguous = true;
                else if (distance < best)
                {
                    best = distance;
                    nearest = leg;
                    ambiguous = false;
                }
            }
            if (ambiguous || nearest is not PrtsRoutePreview.Leg selected) return null;
            var a = HandleUtility.WorldToGUIPoint(selected.From);
            var b = HandleUtility.WorldToGUIPoint(selected.To);
            var screen = b - a;
            if (screen.sqrMagnitude < .001f) return null;
            var world = Vector3.Lerp(selected.From, selected.To,
                Mathf.Clamp01(Vector2.Dot(mouse - a, screen) / screen.sqrMagnitude));
            var cell = new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.z));
            if (cell.x < 0 || cell.y < 0 || cell.x >= renderedDocument!.Width || cell.y >= renderedDocument.Height)
                return null;
            return (selected.InsertBefore, cell);
        }

        private void ResetRouteGesture()
        {
            if (routeControl != 0 && GUIUtility.hotControl == routeControl) GUIUtility.hotControl = 0;
            routeControl = 0;
            routeHover = null;
            routeDragCell = null;
            draggedRouteNode = null;
            Repaint();
        }

        private PrtsRoutePreview.Path? CurrentEditPath()
        {
            var active = editor!.ActiveRouteDraft;
            var drawing = CanUseRouteScene && editor.RouteSceneDrawing;
            var hover = drawing ? routeHover : null;
            var changedDocument = geometryDocument != renderedDocument;
            var editing = editor.RouteSceneActive || active != null;
            if (!changedDocument && geometryRevision == editor.RouteVisualRevision &&
                geometryHover == hover && geometryDrag == routeDragCell &&
                geometryNode == draggedRouteNode && geometryDrawing == drawing && geometryForPreview == editing) return editPath;
            if (changedDocument) routeGeometry = new PrtsRoutePreview(renderedDocument!, false);
            geometryDocument = renderedDocument;
            geometryRevision = editor.RouteVisualRevision;
            geometryHover = hover;
            geometryDrag = routeDragCell;
            geometryNode = draggedRouteNode;
            geometryDrawing = drawing;
            geometryForPreview = editing;
            var route = editor.DisplayedRoute();
            if (active != null && draggedRouteNode is int node && routeDragCell is Vector2Int target)
                route = active.MovedRoute(node, target);
            else if (active != null && hover is Vector2Int candidate && active.TryNodeCell(RouteEditDraft.Start, out _) &&
                (!active.TryNodeCell(RouteEditDraft.End, out var last) || candidate != last))
            {
                var ghost = new RouteEditDraft(active.Library, active.Index, active.Route);
                ghost.Place(candidate);
                route = ghost.Route;
            }
            editPath = route == null ? null : routeGeometry!.BuildPath(route, true);
            return editPath;
        }

        private bool DrawRouteEditing()
        {
            if (!editor!.RoutePanelActive || editor.DisplayedRoute() == null) return false;
            var path = CurrentEditPath();
            if (path != null)
            {
                PrtsRouteOverlay.Draw(path, true, path.Length);
                DrawRouteDirections(path);
            }
            if (editor.ActiveRouteDraft is not RouteEditDraft active) return true;
            DrawRouteHandles(active, path);
            if (CanUseRouteScene && editor.RouteSceneActive) DrawRouteGuide(active, path?.HasFallback == true);
            return true;
        }

        private void DrawRouteHandles(RouteEditDraft active, PrtsRoutePreview.Path? path)
        {
            var input = Event.current;
            var hover = CanUseRouteScene && editor!.RouteSceneActive ? PickRouteNode(active, input.mousePosition) : null;
            foreach (var handle in active.Handles())
            {
                var point = draggedRouteNode == handle.Index && routeDragCell is Vector2Int cell
                    ? new Vector3(cell.x + .5f, handle.Point.y, cell.y + .5f) : handle.Point;
                var selected = editor!.SelectedRouteNode == handle.Index || hover == handle.Index;
                var radius = HandleUtility.GetHandleSize(point) * (selected ? .062f : .041f);
                using (new Handles.DrawingScope(selected ? Color.white : new Color(.46f, .82f, 1f)))
                    Handles.DrawWireDisc(point + Vector3.up * .03f, Vector3.up, radius, 2);
                var text = handle.Index == RouteEditDraft.Start ? "起" :
                    handle.Index == RouteEditDraft.End ? "终" : (handle.Index + 1).ToString();
                Handles.Label(point + Vector3.up * .08f, text, EditorStyles.miniBoldLabel);
            }
            if (!CanUseRouteScene || !editor!.RouteSceneActive || draggedRouteNode.HasValue || hover.HasValue ||
                editor.RouteSceneDrawing || editor.RouteScenePickAppearance ||
                PickRouteInsertion(path, input.mousePosition) is not { } insertion) return;
            var target = PrtsRoutePreview.World(insertion.Cell, null, path?.Air == true);
            using (new Handles.DrawingScope(Color.white))
                Handles.DrawWireDisc(target, Vector3.up, HandleUtility.GetHandleSize(target) * .061f, 2);
            Handles.Label(target + Vector3.up * .08f, "+", EditorStyles.boldLabel);
        }

        private static void DrawRouteDirections(PrtsRoutePreview.Path path)
        {
            using (new Handles.DrawingScope(path.Air ? new Color(1f, .94f, .61f) : new Color(1f, .79f, .81f)))
            {
                foreach (var leg in path.Legs)
                {
                    if (leg.Fallback) continue;
                    var center = Vector3.Lerp(leg.From, leg.To, .58f) + Vector3.up * .032f;
                    var direction = (leg.To - leg.From).normalized;
                    var side = Vector3.Cross(Vector3.up, direction) * .10f;
                    var tail = center - direction * .14f;
                    Handles.DrawLine(center, tail + side);
                    Handles.DrawLine(center, tail - side);
                }
            }
        }

        private void DrawRouteGuide(RouteEditDraft active, bool fallback)
        {
            var prompt = editor!.RouteScenePickAppearance ? "点击地图设置再出现位置" :
                editor.RouteSceneDrawing ? (!active.TryNodeCell(RouteEditDraft.Start, out _) ? "点击地图设置起点" :
                    "点击添加落点 · Enter 完成 · Backspace 撤回一处 · Esc 暂停") :
                "拖动节点或点击路线插入检查点 · Esc 退出场景工具";
            if (routeHover is Vector2Int cell) prompt += $" · ({cell.y}, {cell.x})";
            if (fallback) prompt += " · 虚线段不可达";
            Handles.BeginGUI();
            var label = new GUIContent(prompt);
            var dimensions = EditorStyles.helpBox.CalcSize(label);
            GUI.Label(new Rect(12, 12, dimensions.x + 12, Mathf.Max(21, dimensions.y)), label, EditorStyles.helpBox);
            Handles.EndGUI();
        }
    }
}
