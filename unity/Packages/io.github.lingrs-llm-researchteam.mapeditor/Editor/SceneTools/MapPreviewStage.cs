#nullable enable
using System;
using System.Collections.Generic;
using Arknights.MapEditor.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Editor
{
    public sealed class MapPreviewStage : PreviewSceneStage
    {
        [SerializeField] private string documentJson = "";
        [SerializeField] private bool coordinates;
        [NonSerialized] private ChunkedMapView? view;
        [NonSerialized] private PrtsObjectView? objectView;
        [NonSerialized] private PrtsLabelView? labelView;
        [NonSerialized] private LevelDocument? document;
        [NonSerialized] private Action<IReadOnlyList<Vector2Int>>? paint;
        [NonSerialized] private Action<Vector2Int>? pick;
        [NonSerialized] internal Action? UndoMapEdit;
        [NonSerialized] internal Func<bool>? DrawRoutePreview;
        private readonly List<Vector2Int> stroke = new List<Vector2Int>();
        private readonly HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        private Vector2Int? lastCell;
        private readonly List<Vector3[]> routeLines = new List<Vector3[]>();
        private readonly List<Vector3> presetMarkers = new List<Vector3>();
        private readonly Dictionary<Vector2Int, string> presetDescriptions = new Dictionary<Vector2Int, string>();
        private Vector2Int? hoverCell;
        private readonly Dictionary<SceneView, PresetHandPreview> hands = new();
        private string handLayer = "predefines";
        private bool handVisible;
        private (string Role, int Index)? selectedHand;
        [NonSerialized] internal Func<string, string, int, bool>? SelectHandPreset;
        public Mesh OwnedMesh => view != null ? view.MeshAt(0, 0) : throw new InvalidOperationException("Stage is not open.");
        public void Configure(LevelDocument value) { document = value; documentJson = value.ToJson(false); BuildMarkers(); }
        public void Bind(Action<IReadOnlyList<Vector2Int>> paintAction, Action<Vector2Int> pickAction) { paint = paintAction; pick = pickAction; }
        public void Unbind() { paint = null; pick = null; UndoMapEdit = null; DrawRoutePreview = null; SelectHandPreset = null; stroke.Clear(); visited.Clear(); lastCell = null; }
        public void SetCoordinates(bool value) { coordinates = value; SceneView.RepaintAll(); }
        internal void SetPresetHand(string layer, bool visible, (string Role, int Index)? selection)
        {
            handLayer = layer;
            handVisible = visible;
            selectedHand = selection;
            SceneView.RepaintAll();
        }
        public void SetView(bool flat, SceneView? target = null)
        {
            target ??= SceneView.lastActiveSceneView;
            if (target == null || document == null || StageUtility.GetCurrentStage() != this) return;
            var size = Mathf.Max(document.Width, document.Height) * .72f;
            if (!flat && target.camera != null)
            {
                // Timeline uses 48 CSS px per tile and a 900 px perspective.
                // Preserve that focal-length ratio when fitting the SceneView.
                const float cellPitch = 48f + 2f;
                var distance = Mathf.Max(900f / cellPitch, Mathf.Max(document.Width, document.Height) * 1.4f);
                var pixelsPerTile = Mathf.Min(cellPitch * EditorGUIUtility.pixelsPerPoint,
                    target.camera.pixelWidth / (document.Width + 2f),
                    target.camera.pixelHeight / (document.Height + 3.5f));
                var halfAngle = Mathf.Atan(target.camera.pixelHeight / (2f * distance * Mathf.Max(1f, pixelsPerTile)));
                var fov = Mathf.Clamp(2f * halfAngle * Mathf.Rad2Deg, 4f, 90f);
                target.cameraSettings.fieldOfView = fov;
                size = distance * Mathf.Sin(fov * .5f * Mathf.Deg2Rad);
            }
            target.LookAt(new Vector3(document.Width / 2f, 0, document.Height / 2f),
                Quaternion.Euler(flat ? 90f : 60f, 0, 0), size, flat, true);
            target.showGrid = false;
            target.wantsMouseMove = true;
            target.Repaint();
        }
        public void UpdateDocument(LevelDocument value, IEnumerable<Vector2Int>? dirty)
        {
            Configure(value); view?.Update(value, dirty); objectView?.Update(value); SceneView.RepaintAll();
        }
        protected override GUIContent CreateHeaderContent() => new GUIContent("LevelData 地图编辑 · 左键绘制 / Shift拾取 / Alt导航");
        protected override bool OnOpenStage()
        {
            if (!base.OnOpenStage()) return false;
            try { InitializeView(); return true; }
            catch { ReleaseView(); base.OnCloseStage(); throw; }
        }
        private void InitializeView()
        {
            document = LevelDocument.Parse(documentJson);
            BuildMarkers();
            view = new ChunkedMapView(scene); view.Update(document, null);
            objectView = new PrtsObjectView(scene); objectView.Update(document);
            labelView = new PrtsLabelView(scene);
            SceneView.beforeSceneGui -= BeforeSceneGui;
            SceneView.beforeSceneGui += BeforeSceneGui;
            SceneView.duringSceneGui -= DuringSceneGui;
            SceneView.duringSceneGui += DuringSceneGui;
        }
        protected override void OnEnable() { base.OnEnable(); if (scene.IsValid() && documentJson.Length > 0) InitializeView(); }
        protected override void OnDisable() { try { ReleaseView(); } finally { base.OnDisable(); } }
        protected override void OnCloseStage() { ReleaseView(); base.OnCloseStage(); }
        private void ReleaseView()
        {
            SceneView.beforeSceneGui -= BeforeSceneGui;
            SceneView.duringSceneGui -= DuringSceneGui;
            view?.Dispose(); view = null; stroke.Clear(); visited.Clear(); lastCell = null;
            objectView?.Dispose(); objectView = null;
            labelView?.Dispose(); labelView = null;
            foreach (var hand in hands.Values) hand.RemoveFromHierarchy();
            hands.Clear();
            if (GUIUtility.hotControl == controlId) GUIUtility.hotControl = 0;
        }
        private int controlId;

        private void BeforeSceneGui(SceneView sceneView)
        {
            if (StageUtility.GetCurrentStage() == this && EditorWindow.focusedWindow == sceneView && !EditorGUIUtility.editingTextField)
                HandleUndoInput(Event.current);
        }

        internal void HandleUndoInput(Event input)
        {
            if (UndoMapEdit == null || input.type != EventType.KeyDown ||
                !MapEditorWindow.IsUndoShortcut(input.keyCode, input.modifiers)) return;
            input.Use();
            stroke.Clear(); visited.Clear(); lastCell = null;
            if (controlId != 0 && GUIUtility.hotControl == controlId) GUIUtility.hotControl = 0;
            UndoMapEdit();
        }

        private void DuringSceneGui(SceneView sceneView)
        {
            if (StageUtility.GetCurrentStage() != this || document == null) return;
            if (handVisible && !hands.ContainsKey(sceneView))
            {
                var hand = new PresetHandPreview((layer, role, index) => SelectHandPreset?.Invoke(layer, role, index) ?? false);
                hands.Add(sceneView, hand);
                sceneView.rootVisualElement.Add(hand);
            }
            if (hands.TryGetValue(sceneView, out var visibleHand)) visibleHand.Update(document, handLayer, handVisible, selectedHand);
            var e = Event.current;
            if (e.type == EventType.MouseMove)
            {
                hoverCell = ChunkedMapView.TryPick(document, HandleUtility.GUIPointToWorldRay(e.mousePosition), out var hovered)
                    ? hovered : (Vector2Int?)null;
                sceneView.Repaint();
            }
            if (e.type == EventType.Repaint)
            {
                if (DrawRoutePreview?.Invoke() != true)
                    using (new Handles.DrawingScope(Color.cyan))
                        foreach (var line in routeLines) if (line.Length > 1) Handles.DrawAAPolyLine(2f, line);
                Handles.color = Color.yellow;
                if (objectView != null) foreach (var position in objectView.UnresolvedMarkers)
                    Handles.DrawWireCube(position, new Vector3(.5f, .5f, .5f));
            }
            controlId = GUIUtility.GetControlID(GetInstanceID(), FocusType.Passive);
            if (e.type == EventType.Layout && !e.alt && paint != null) HandleUtility.AddDefaultControl(controlId);
            if (e.type == EventType.Repaint && sceneView.camera != null)
                labelView?.Update(document, coordinates, sceneView.orthographic);
            if (e.type == EventType.Repaint && hoverCell.HasValue)
            {
                var hoveredCell = hoverCell.Value;
                if (hoveredCell.x >= 0 && hoveredCell.y >= 0 && hoveredCell.x < document.Width && hoveredCell.y < document.Height)
                {
                    var tileId = document.GetCell(hoveredCell.y, hoveredCell.x)["tileKey"]?.ToString() ?? "";
                    var text = "(" + hoveredCell.x + ", " + hoveredCell.y + ")  " + PrtsObjectView.DisplayName(tileId) + " · " + tileId;
                    if (presetDescriptions.TryGetValue(hoveredCell, out var description)) text += "\n" + description;
                    Handles.BeginGUI();
                    try { GUI.Box(new Rect(12, 38, Mathf.Min(540, sceneView.position.width - 24), 58), text, EditorStyles.helpBox); }
                    finally { Handles.EndGUI(); }
                }
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            { stroke.Clear(); visited.Clear(); lastCell = null; if (GUIUtility.hotControl == controlId) GUIUtility.hotControl = 0; e.Use(); return; }
            if (e.type == EventType.Ignore)
            { stroke.Clear(); visited.Clear(); lastCell = null; if (GUIUtility.hotControl == controlId) GUIUtility.hotControl = 0; return; }
            if (e.type == EventType.MouseUp && e.button == 0 && stroke.Count > 0 && GUIUtility.hotControl == controlId)
            {
                var cells = stroke.ToArray(); stroke.Clear(); visited.Clear(); lastCell = null;
                GUIUtility.hotControl = 0; e.Use();
                try { paint?.Invoke(cells); } catch (Exception error) { PluginLog.Failure("scene.paint", error); }
                return;
            }
            if (e.alt || e.button != 0) return;
            if (e.type != EventType.MouseDown && e.type != EventType.MouseDrag) return;
            if (e.type == EventType.MouseDrag && (stroke.Count == 0 || GUIUtility.hotControl != controlId)) return;
            if (!ChunkedMapView.TryPick(document, HandleUtility.GUIPointToWorldRay(e.mousePosition), out var cell)) return;
            if (e.shift && e.type == EventType.MouseDown) { pick?.Invoke(cell); e.Use(); return; }
            if (paint == null) return;
            GUIUtility.hotControl = controlId;
            if (lastCell.HasValue)
            {
                var from = lastCell.Value; var steps = Mathf.Max(Mathf.Abs(cell.x - from.x), Mathf.Abs(cell.y - from.y));
                for (var i = 0; i <= steps; i++)
                { var p = steps == 0 ? cell : new Vector2Int(Mathf.RoundToInt(Mathf.Lerp(from.x, cell.x, i / (float)steps)), Mathf.RoundToInt(Mathf.Lerp(from.y, cell.y, i / (float)steps))); if (visited.Add(p)) stroke.Add(p); }
            }
            else if (visited.Add(cell)) stroke.Add(cell);
            lastCell = cell; e.Use();
        }

        private void BuildMarkers()
        {
            routeLines.Clear(); presetMarkers.Clear(); presetDescriptions.Clear(); hoverCell = null;
            if (document == null) return;
            ReadRouteLines(document, routeLines);
            foreach (var scope in new[] { "predefines", "hardPredefines" })
            {
                if (!(document.GetSection(scope) is JObject data)) continue;
                foreach (var kind in new[] { "characterInsts", "tokenInsts" })
                    if (data[kind] is JArray entries) foreach (var entry in entries) if (entry is JObject item)
                    {
                        AddPoint(presetMarkers, item["position"]);
                        if (!(item["position"] is JObject position) || !int.TryParse(position["col"]?.ToString(), out var col) ||
                            !int.TryParse(position["row"]?.ToString(), out var row)) continue;
                        var id = (item["inst"] as JObject)?["characterKey"]?.ToString() ?? "未知ID";
                        var key = new Vector2Int(col, row);
                        var detail = PrtsObjectView.DisplayName(id) + " · " + id +
                            "（" + PrtsObjectView.VisualSource(id) + "）";
                        presetDescriptions[key] = presetDescriptions.TryGetValue(key, out var previous) ? previous + "; " + detail : detail;
                    }
            }
        }

        internal static void ReadRouteLines(LevelDocument document, List<Vector3[]> lines)
        {
            lines.Clear();
            foreach (var key in new[] { "routes", "extraRoutes" })
            {
                if (!(document.GetSection(key) is JArray routes)) continue;
                foreach (var route in routes)
                {
                    if (!(route is JObject item)) continue;
                    var points = new List<Vector3>(); AddPoint(points, item["startPosition"]);
                    if (item["checkpoints"] is JArray checkpoints) foreach (var point in checkpoints) if (point is JObject checkpoint) AddPoint(points, checkpoint["position"]);
                    AddPoint(points, item["endPosition"]); lines.Add(points.ToArray());
                }
            }
        }
        private static void AddPoint(List<Vector3> points, JToken? position)
        {
            if (!(position is JObject point)) return;
            if (!float.TryParse(point["row"]?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var row) ||
                !float.TryParse(point["col"]?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var col) ||
                float.IsNaN(row) || float.IsNaN(col) || float.IsInfinity(row) || float.IsInfinity(col)) return;
            points.Add(new Vector3(col + .5f, .6f, row + .5f));
        }
    }
}
