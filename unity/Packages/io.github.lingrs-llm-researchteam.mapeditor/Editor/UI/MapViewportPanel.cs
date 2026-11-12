#nullable enable
using System;
using System.Collections.Generic;
using Arknights.MapEditor.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class MapViewportPanel : IDisposable
    {
        private enum Gesture { None, Paint, Pan, Orbit, Zoom }
        private static readonly Vector2[] AxisAngles =
        {
            new Vector2(0, -90), new Vector2(0, 90), new Vector2(90, 0),
            new Vector2(-90, 0), new Vector2(0, 180), Vector2.zero
        };
        private readonly IMGUIContainer canvas;
        private readonly MapEditorWindow editor;
        private readonly MapViewportState state;
        private readonly Action activate;
        private readonly Action<string> report;
        private readonly List<Vector2Int> stroke = new List<Vector2Int>();
        private readonly HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        private MapWorkspaceViewport? renderer;
        private RenderTexture? texture;
        private LevelDocument? document;
        private Vector2Int? lastCell;
        private int dragButton = -1;
        private int dragControl;
        private Gesture gesture;
        private readonly VisualElement cameraTools;
        private readonly Button projection;
        private readonly Button hand;
        private readonly Button erase;
        private readonly Button editLock;
        private readonly Button cameraLock;
        private readonly Button handPreviewToggle;
        private readonly PresetHandPreview presetHand;
        private readonly ViewportOrientationGizmo orientation = new ViewportOrientationGizmo();
        private bool dirty = true;
        private bool failed;
        internal VisualElement Element { get; }

        internal MapViewportPanel(MapEditorWindow editor, MapViewportState state, Action activate, Action<string> report)
        {
            this.editor = editor;
            this.state = state;
            this.activate = activate;
            this.report = report;
            presetHand = new PresetHandPreview((layer, role, index) =>
            {
                activate();
                return editor.SelectHandPreset(layer, role, index);
            });
            Element = new VisualElement { name = "map-viewport" };
            Element.AddToClassList("workspace-viewport");
            var toolbar = new VisualElement();
            toolbar.AddToClassList("workspace-row");
            toolbar.style.flexWrap = Wrap.Wrap;
            cameraTools = new VisualElement();
            cameraTools.AddToClassList("workspace-row");
            cameraTools.Add(new Button(() => SnapAxis(2)) { text = "俯视" });
            cameraTools.Add(new Button(() => ChangeCamera(() => SetView(60f, 0, MapViewportState.Projection.Perspective))) { text = "立体" });
            cameraTools.Add(new Button(Fit) { text = "适应", tooltip = "聚焦地图（F）" });
            projection = new Button(ToggleProjection) { name = "viewport-projection", tooltip = "切换透视和正交，保留当前方向" };
            hand = new Button(ToggleHand) { name = "viewport-hand", text = "手形", tooltip = "手形平移（Q），左键拖动平移" };
            cameraTools.Add(projection);
            hand.AddToClassList("workspace-hand-tool");
            var handIcon = EditorGUIUtility.FindTexture(EditorGUIUtility.isProSkin ? "d_ViewToolMove" : "ViewToolMove");
            if (handIcon != null)
            {
                hand.text = "";
                hand.Add(new Image { image = handIcon, pickingMode = PickingMode.Ignore });
            }
            toolbar.Add(cameraTools);
            erase = new Button(ToggleErase) { name = "viewport-erase", text = "置空", tooltip = "开启后左键点击或拖动，将地块置为禁入区。再次点击退出。" };
            toolbar.Add(erase);
            editLock = new Button(ToggleEditLock) { name = "viewport-edit-lock", text = "编辑锁", tooltip = "锁定当前文档，包含它在其他视口中的编辑" };
            cameraLock = new Button(ToggleCameraLock) { name = "viewport-camera-lock", text = "相机锁", tooltip = "锁定当前视口的方向、位置、缩放和投影" };
            toolbar.Add(editLock);
            toolbar.Add(cameraLock);
            handPreviewToggle = new Button(() => editor.SetPresetHandVisible(!editor.PresetHandVisible))
                { name = "viewport-preset-hand" };
            toolbar.Add(handPreviewToggle);
            var coordinates = new Toggle("坐标") { value = state.Coordinates };
            coordinates.labelElement.style.minWidth = 0;
            coordinates.RegisterValueChangedCallback(evt => { state.Coordinates = evt.newValue; Refresh(); });
            toolbar.Add(coordinates);
            Element.Add(toolbar);
            canvas = new IMGUIContainer(Draw) { name = "map-viewport-canvas", focusable = true };
            canvas.AddToClassList("workspace-canvas");
            canvas.RegisterCallback<GeometryChangedEvent>(_ => Refresh());
            canvas.RegisterCallback<DetachFromPanelEvent>(_ => CancelInput());
            var preview = new VisualElement();
            preview.AddToClassList("workspace-canvas");
            preview.Add(canvas);
            preview.Add(hand);
            preview.Add(presetHand);
            Element.Add(preview);
            RefreshControls();
            RefreshHandPreview();
        }

        private void ChangeCamera(Action change)
        {
            if (state.CameraLocked) return;
            activate();
            change();
            RefreshControls();
            Refresh();
        }

        private void SetView(float pitch, float yaw, MapViewportState.Projection mode)
        {
            state.Flat = false;
            state.Pitch = pitch;
            state.Yaw = yaw;
            state.ProjectionMode = mode;
        }

        private void Fit() => ChangeCamera(() =>
        {
            state.Offset = Vector2.zero;
            state.VerticalOffset = 0;
            state.Zoom = 1;
        });

        private void SnapAxis(int index) => ChangeCamera(() =>
            SetView(AxisAngles[index].x, AxisAngles[index].y, MapViewportState.Projection.Orthographic));

        private void ToggleProjection() => ChangeCamera(() => state.ProjectionMode = state.Orthographic
            ? MapViewportState.Projection.Perspective : MapViewportState.Projection.Orthographic);

        private void ToggleHand() => ChangeCamera(() =>
        {
            CancelInput();
            state.Hand = !state.Hand;
        });

        private void ToggleEditLock()
        {
            activate();
            editor.SetWorkspaceEditLock(!editor.WorkspaceEditLocked);
            RefreshEditLock();
        }

        private void ToggleErase()
        {
            if (editor.WorkspaceEditLocked) return;
            activate();
            CancelInput();
            editor.SetEraseMode(!editor.EraseMode);
            if (editor.EraseMode) state.Hand = false;
            RefreshControls();
        }

        private void ToggleCameraLock()
        {
            activate();
            CancelInput();
            state.CameraLocked = !state.CameraLocked;
            if (state.CameraLocked)
            {
                var doc = editor.WorkspaceDocument;
                state.LockedSize = new Vector2Int(doc.Width, doc.Height);
                var rect = canvas.contentRect;
                state.LockedAspect = rect.width > 0 && rect.height > 0 ? rect.width / rect.height : 0;
            }
            RefreshControls();
            Refresh();
        }

        internal void RefreshEditLock()
        {
            var locked = editor.WorkspaceEditLocked;
            if (locked && gesture == Gesture.Paint) CancelInput();
            editLock.EnableInClassList("workspace-selected", locked);
            editLock.text = locked ? "编辑锁 ✓" : "编辑锁";
            erase.SetEnabled(!locked);
            erase.EnableInClassList("workspace-selected", editor.EraseMode);
            erase.text = editor.EraseMode ? "置空 ✓" : "置空";
        }

        private void RefreshControls()
        {
            RefreshEditLock();
            cameraTools.SetEnabled(!state.CameraLocked);
            cameraLock.EnableInClassList("workspace-selected", state.CameraLocked);
            cameraLock.text = state.CameraLocked ? "相机锁 ✓" : "相机锁";
            projection.text = state.Orthographic ? "正交" : "透视";
            hand.SetEnabled(!state.CameraLocked);
            hand.EnableInClassList("workspace-selected", state.Hand && !state.CameraLocked);
        }

        internal void Refresh()
        {
            dirty = true;
            failed = false;
            canvas.MarkDirtyRepaint();
        }

        internal void RefreshDocument()
        {
            if (document != editor.WorkspaceDocument) Refresh();
            RefreshHandPreview();
        }

        private void RefreshHandPreview()
        {
            handPreviewToggle.text = editor.PresetHandButtonText;
            handPreviewToggle.EnableInClassList("workspace-selected", editor.PresetHandVisible);
            presetHand.Update(editor.WorkspaceDocument, editor.ActivePresetLayer, editor.PresetHandVisible, editor.ActiveHandPreset);
        }

        private void Draw()
        {
            var rect = canvas.contentRect;
            if (rect.width < 2 || rect.height < 2) return;
            var current = Event.current;
            var control = GUIUtility.GetControlID(FocusType.Passive);
            if (current.type == EventType.Repaint) DrawPreview(rect);
            var gizmo = new Rect(rect.xMax - 132, rect.y + 6, 98, 94);
            if (rect.width >= 150 && rect.height >= 108) orientation.Draw(gizmo, state, SnapAxis, ToggleProjection);
            else gizmo = Rect.zero;
            if (current.type == EventType.Repaint || current.type == EventType.Used) return;
            if (dragButton < 0 && gizmo.Contains(current.mousePosition) && current.isMouse) return;
            if (failed || renderer == null || document == null) return;
            try { HandleInput(current, rect, control); }
            catch (Exception error) { CancelInput(); report(error.Message); }
        }

        private void DrawPreview(Rect rect)
        {
            if (failed) return;
            try
            {
                if (dirty)
                {
                    renderer ??= new MapWorkspaceViewport();
                    document = editor.WorkspaceDocument;
                    texture = renderer.Render(document, state, rect.size);
                    dirty = false;
                }
                if (texture == null || renderer == null) return;
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
                renderer.DrawRoutes(rect);
            }
            catch (Exception error)
            {
                failed = true;
                report("地图预览失败：" + error.Message);
                renderer?.Dispose();
                renderer = null;
                texture = null;
            }
        }

        private void HandleInput(Event current, Rect rect, int control)
        {
            if (current.type == EventType.Ignore || (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape && dragButton >= 0))
            {
                CancelInput();
                if (current.type == EventType.KeyDown) current.Use();
                return;
            }
            if (current.type == EventType.ScrollWheel && rect.Contains(current.mousePosition))
            {
                ChangeCamera(() => Zoom(current.delta.y * .045f));
                current.Use();
                return;
            }
            if (current.type == EventType.KeyDown && canvas.focusController?.focusedElement == canvas && dragButton < 0)
            {
                if (current.keyCode == KeyCode.Q) { ToggleHand(); current.Use(); }
                if (current.keyCode == KeyCode.F) { Fit(); current.Use(); }
                return;
            }
            if (current.type == EventType.MouseDown && rect.Contains(current.mousePosition))
            {
                activate();
                canvas.Focus();
                if (current.button == 0 && current.shift && !current.alt)
                {
                    if (renderer!.TryPick(document!, current.mousePosition, rect, out var picked)) editor.PickWorkspaceCell(picked);
                }
                else
                {
                    gesture = GetGesture(current);
                    if (gesture != Gesture.None)
                    {
                        dragButton = current.button;
                        dragControl = control;
                        GUIUtility.hotControl = dragControl;
                        if (gesture == Gesture.Paint) AddStrokePoint(current.mousePosition, rect);
                    }
                }
                current.Use();
                return;
            }
            if (dragButton < 0 || GUIUtility.hotControl != dragControl) return;
            if (current.type == EventType.MouseDrag)
            {
                Drag(current, rect);
                current.Use();
            }
            if (current.type == EventType.MouseUp && current.button == dragButton)
            {
                var cells = stroke.ToArray();
                CancelInput();
                current.Use();
                if (cells.Length > 0 && !editor.WorkspaceEditLocked) editor.PaintWorkspaceCells(cells);
            }
        }

        private Gesture GetGesture(Event current)
        {
            if (current.alt)
            {
                if (state.CameraLocked) return Gesture.None;
                if (current.button == 0) return Gesture.Orbit;
                if (current.button == 1) return Gesture.Zoom;
            }
            if (current.button == 0)
                return state.Hand && !state.CameraLocked ? Gesture.Pan : editor.WorkspaceEditLocked ? Gesture.None : Gesture.Paint;
            if (state.CameraLocked) return Gesture.None;
            if (current.button == 1) return Gesture.Orbit;
            return current.button == 2 ? Gesture.Pan : Gesture.None;
        }

        private void Zoom(float exponent) => state.Zoom = Mathf.Clamp(state.Zoom * Mathf.Exp(exponent), .05f, 8f);

        private void Drag(Event current, Rect rect)
        {
            if (gesture == Gesture.Paint) { AddStrokePoint(current.mousePosition, rect); return; }
            if (state.CameraLocked) return;
            if (gesture == Gesture.Orbit)
            {
                if (state.Flat)
                {
                    state.Flat = false;
                    state.Pitch = 89f;
                    if (state.ProjectionMode == MapViewportState.Projection.FollowView)
                        state.ProjectionMode = MapViewportState.Projection.Perspective;
                }
                state.Yaw = Mathf.Repeat(state.Yaw + current.delta.x * .45f, 360f);
                state.Pitch = Mathf.Clamp(state.Pitch + current.delta.y * .4f, -89f, 89f);
            }
            else if (gesture == Gesture.Pan)
            {
                var move = renderer!.PanDelta(current.mousePosition, current.delta, rect);
                state.Offset += new Vector2(move.x, move.z);
                state.VerticalOffset += move.y;
            }
            else if (gesture == Gesture.Zoom) Zoom((current.delta.x + current.delta.y) * .012f);
            Refresh();
        }

        private void AddStrokePoint(Vector2 point, Rect rect)
        {
            if (!renderer!.TryPick(document!, point, rect, out var cell)) return;
            var from = lastCell ?? cell;
            var steps = Mathf.Max(1, Mathf.Abs(cell.x - from.x), Mathf.Abs(cell.y - from.y));
            for (var i = 0; i <= steps; i++)
            {
                var pointOnLine = Vector2Int.RoundToInt(Vector2.Lerp(from, cell, i / (float)steps));
                if (visited.Add(pointOnLine)) stroke.Add(pointOnLine);
            }
            lastCell = cell;
        }

        private void CancelInput()
        {
            if (dragControl != 0 && GUIUtility.hotControl == dragControl) GUIUtility.hotControl = 0;
            dragControl = 0;
            dragButton = -1;
            gesture = Gesture.None;
            stroke.Clear();
            visited.Clear();
            lastCell = null;
        }

        public void Dispose()
        {
            CancelInput();
            Element.RemoveFromHierarchy();
            renderer?.Dispose();
            renderer = null;
            texture = null;
        }
    }
}
