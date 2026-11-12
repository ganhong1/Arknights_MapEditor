#nullable enable
using System;
using System.Collections.Generic;
using Arknights.MapEditor.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Arknights.MapEditor.Editor
{
    public sealed class NativeMapSceneView : SceneView
    {
        [SerializeField] private MapWorkspaceWindow? owner;
        [SerializeField] private int group;
        [SerializeField] private bool dockable;
        private Scene preview;
        private ChunkedMapView? terrain;
        private PrtsObjectView? objects;
        private PrtsLabelView? labels;
        private PresetHandPreview? hand;
        private MapEditorWindow? editor;
        private LevelDocument? renderedDocument;
        private MapViewportState? viewState;
        private bool closing;
        private Vector3[] missingIconLines = Array.Empty<Vector3>();
        private readonly List<Vector2Int> stroke = new List<Vector2Int>();
        private readonly HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        private Vector2Int? previousCell;
        private int paintControl;

        protected override bool SupportsStageHandling() => false;
        internal bool IsDockable => dockable;

        internal void Attach(MapWorkspaceWindow workspace, int groupIndex)
        {
            owner = workspace;
            group = groupIndex;
            dockable = true;
        }

        public override void OnEnable()
        {
            base.OnEnable();
            duringSceneGui += DrawMap;
            beforeSceneGui += HandleMapInput;
        }

        internal void Bind(MapEditorWindow documentEditor, MapViewportState state)
        {
            if (preview.IsValid() == false)
            {
                preview = EditorSceneManager.NewPreviewScene();
                customScene = preview;
                terrain = new ChunkedMapView(preview);
                objects = new PrtsObjectView(preview);
                labels = new PrtsLabelView(preview);
                in2DMode = false;
                showGrid = false;
                sceneLighting = false;
            }
            if (editor != documentEditor || viewState != state)
            {
                CancelStroke();
                RememberPose();
                editor = documentEditor;
                viewState = state;
                if (state.NativeSize > 0) RestorePose(state);
                else
                {
                    var doc = documentEditor.WorkspaceDocument;
                    LookAt(new Vector3(doc.Width * .5f + state.Offset.x, state.VerticalOffset, doc.Height * .5f + state.Offset.y),
                        state.Rotation, Mathf.Max(doc.Width, doc.Height) * .63f * state.Zoom, state.Orthographic, true);
                    RememberPose();
                }
            }
            RefreshDocument();
        }

        internal void RefreshDocument()
        {
            if (editor == null) return;
            var doc = editor.WorkspaceDocument;
            if (doc != renderedDocument)
            {
                terrain!.Update(doc, null);
                objects!.Update(doc);
                missingIconLines = BuildMissingIconLines(objects.MissingIconMarkers);
                renderedDocument = doc;
            }
            if (hand == null && editor.PresetHandVisible)
            {
                hand = new PresetHandPreview((layer, role, index) =>
                {
                    owner?.ActivateNativeGroup(group);
                    return editor?.SelectHandPreset(layer, role, index) ?? false;
                });
                rootVisualElement.Add(hand);
            }
            hand?.Update(doc, editor.ActivePresetLayer, editor.PresetHandVisible, editor.ActiveHandPreset);
            titleContent = new GUIContent((group == 0 ? "场景 A · " : "场景 B · ") + editor.WorkspaceTitle +
                (editor.WorkspaceDirty ? " *" : ""));
            Repaint();
        }

        internal void RememberPose()
        {
            if (viewState == null || viewState.CameraLocked && viewState.NativeSize > 0) return;
            viewState.NativePivot = pivot;
            viewState.NativeRotation = rotation;
            viewState.NativeSize = size;
            viewState.NativeOrtho = orthographic;
        }

        private void RestorePose(MapViewportState state) =>
            LookAt(state.NativePivot, state.NativeRotation, state.NativeSize, state.NativeOrtho, true);

        private void DrawMap(SceneView view)
        {
            if (view != this || editor == null || renderedDocument == null || camera.scene != preview) return;
            var input = Event.current;
            if (viewState is { CameraLocked: true, NativeSize: > 0 } locked &&
                (pivot != locked.NativePivot || rotation != locked.NativeRotation ||
                 !Mathf.Approximately(size, locked.NativeSize) || orthographic != locked.NativeOrtho))
                RestorePose(locked);
            if (input.type == EventType.Repaint)
            {
                if (focusedWindow == this) owner?.ActivateNativeGroup(group);
                terrain!.SetBackdropVisible(camera.transform.position.y >= -1.1f);
                labels?.Update(renderedDocument, viewState?.Coordinates == true, orthographic);
                editor.DrawRoutePreview();
                if (missingIconLines.Length > 0)
                    using (new Handles.DrawingScope(Color.yellow)) Handles.DrawLines(missingIconLines);
                if (editor.SelectedPresetCell is Vector2Int cell)
                    using (new Handles.DrawingScope(new Color(.3f, .85f, 1f)))
                        Handles.DrawWireCube(new Vector3(cell.x + .5f, .55f, cell.y + .5f), new Vector3(.9f, .04f, .9f));
            }
        }

        private static Vector3[] BuildMissingIconLines(IReadOnlyList<Vector3> markers)
        {
            const float radius = .26f;
            var lines = new Vector3[markers.Count * 8];
            for (var i = 0; i < markers.Count; i++)
            {
                var center = markers[i];
                var a = center + new Vector3(-radius, 0, -radius);
                var b = center + new Vector3(-radius, 0, radius);
                var c = center + new Vector3(radius, 0, radius);
                var d = center + new Vector3(radius, 0, -radius);
                var first = i * 8;
                lines[first] = a; lines[first + 1] = b;
                lines[first + 2] = b; lines[first + 3] = c;
                lines[first + 4] = c; lines[first + 5] = d;
                lines[first + 6] = d; lines[first + 7] = a;
            }
            return lines;
        }

        private void HandleMapInput(SceneView view)
        {
            if (view != this || editor == null || renderedDocument == null || camera.scene != preview) return;
            HandlePaint(Event.current);
        }

        private void HandlePaint(Event input)
        {
            var control = GUIUtility.GetControlID(GetInstanceID(), FocusType.Passive);
            var drawing = input.shift || editor!.EraseMode || editor.TerrainPlacementEnabled || editor.OperatorPlacementEnabled;
            if (input.type == EventType.Layout && drawing && !input.alt && !editor.WorkspaceEditLocked)
                HandleUtility.AddDefaultControl(control);
            if (editor!.WorkspaceEditLocked || input.type == EventType.Ignore ||
                input.type == EventType.KeyDown && input.keyCode == KeyCode.Escape)
            {
                if (paintControl != 0 && input.type == EventType.KeyDown) input.Use();
                CancelStroke();
                return;
            }
            if (input.type == EventType.MouseDown)
            {
                owner?.ActivateNativeGroup(group);
                if (input.button != 0 || !drawing || input.alt || GUIUtility.hotControl != 0 ||
                    !TryPick(input, out var cell)) return;
                paintControl = control;
                GUIUtility.hotControl = control;
                AddPoint(cell);
                input.Use();
                return;
            }
            if (paintControl == 0 || GUIUtility.hotControl != paintControl) return;
            if (input.type == EventType.MouseDrag)
            {
                if (TryPick(input, out var cell)) AddPoint(cell);
                input.Use();
            }
            if (input.type == EventType.MouseUp && input.button == 0)
            {
                var cells = stroke.ToArray();
                CancelStroke();
                input.Use();
                try { editor.PaintWorkspaceCells(cells); }
                catch (Exception error) { owner?.ReportNativeError(error.Message); }
            }
        }

        private bool TryPick(Event input, out Vector2Int cell) =>
            ChunkedMapView.TryPick(renderedDocument!, HandleUtility.GUIPointToWorldRay(input.mousePosition), out cell);

        private void AddPoint(Vector2Int cell)
        {
            var previous = previousCell ?? cell;
            var steps = Mathf.Max(1, Mathf.Abs(cell.x - previous.x), Mathf.Abs(cell.y - previous.y));
            for (var i = 0; i <= steps; i++)
            {
                var point = Vector2Int.RoundToInt(Vector2.Lerp(previous, cell, i / (float)steps));
                if (visited.Add(point)) stroke.Add(point);
            }
            previousCell = cell;
        }

        private void CancelStroke()
        {
            if (paintControl != 0 && GUIUtility.hotControl == paintControl) GUIUtility.hotControl = 0;
            paintControl = 0;
            stroke.Clear();
            visited.Clear();
            previousCell = null;
        }

        internal void CloseFromWorkspace()
        {
            RememberPose();
            closing = true;
            Close();
        }

        public override void OnDisable()
        {
            duringSceneGui -= DrawMap;
            beforeSceneGui -= HandleMapInput;
            CancelStroke();
            RememberPose();
            try
            {
                if (preview.IsValid() && camera != null) customScene = default;
                labels?.Dispose();
                hand?.RemoveFromHierarchy(); hand = null;
                objects?.Dispose();
                terrain?.Dispose();
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                preview = default;
                renderedDocument = null;
            }
            finally { base.OnDisable(); }
        }

        private void OnDestroy()
        {
            if (!closing) owner?.NativeViewClosed(group, this);
        }
    }
}
