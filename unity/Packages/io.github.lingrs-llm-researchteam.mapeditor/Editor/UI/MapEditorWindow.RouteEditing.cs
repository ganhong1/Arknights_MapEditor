#nullable enable
using System;
using System.Linq;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        private const string SceneRouteSlot = "sceneRoute";
        private static readonly string[] RouteLibraries = { "routes", "extraRoutes" };
        private static readonly string[] RouteMotions = { "WALK", "FLY" };
        private const int NoRouteNode = int.MinValue;
        [SerializeField] private int routeLibraryChoice;
        [SerializeField] private int routeListSelection = -1;
        [NonSerialized] private RouteEditDraft? routeDraft;
        [NonSerialized] private int routeNode = NoRouteNode;
        [NonSerialized] private int pendingCheckpoint = -1;
        [NonSerialized] private Action? refreshRouteEditing;

        internal bool RouteSceneActive { get; private set; }
        internal bool RouteSceneDrawing { get; private set; }
        internal bool RouteScenePickAppearance => pendingCheckpoint >= 0;
        internal bool RoutePanelActive => workbenchTab == 1 && !routePlaybackSelected;
        internal RouteEditDraft? ActiveRouteDraft => routeDraft;
        internal int SelectedRouteNode => routeNode;
        internal int RouteVisualRevision { get; private set; }

        private string RouteLibrary => RouteLibraries[Mathf.Clamp(routeLibraryChoice, 0, RouteLibraries.Length - 1)];
        private JArray? ReadRouteList() => WorkspaceDocument.GetSection(RouteLibrary) as JArray;
        private static string RouteTarget(string library, int index) => library + "/" + index;
        private void RefreshRouteEditing() => refreshRouteEditing?.Invoke();

        private void RestoreRouteEditing()
        {
            routeDraft = null;
            routeListSelection = -1;
            routeLibraryChoice = 0;
            RouteVisualRevision++;
            RouteSceneActive = RouteSceneDrawing = false;
            pendingCheckpoint = -1;
            routeNode = NoRouteNode;
            if (InspectorStore.TryRead(SceneRouteSlot, out var raw) &&
                InspectorStore.TryReadTarget(SceneRouteSlot, out var target) &&
                TryReadRouteTarget(target, out var libraryIndex, out var index))
            {
                routeLibraryChoice = libraryIndex;
                routeListSelection = index;
                routeDraft = new RouteEditDraft(RouteLibraries[libraryIndex], index, ParseRouteObject(raw));
            }
            RefreshRouteEditing();
        }

        private static bool TryReadRouteTarget(string target, out int libraryIndex, out int index)
        {
            var parts = target.Split('/');
            index = -1;
            libraryIndex = parts.Length == 2 ? Array.IndexOf(RouteLibraries, parts[0]) : -1;
            return libraryIndex >= 0 && int.TryParse(parts[1], out index) && index >= -1;
        }

        private static JObject ParseRouteObject(string raw) =>
            LevelDocument.Create(1, 1).WithSection("route", raw).GetSection("route") as JObject ??
            throw new FormatException("路线配置须为 JSON 对象。");

        internal void BeginRouteDraft(string library, int index, string motion)
        {
            RequireWorkspaceEditable();
            RequireAppliedDraft();
            if (routeDraft != null) throw new EditorOperationRejectedException("请先应用或取消当前路线草稿。");
            if (index < -1) throw new ArgumentOutOfRangeException(nameof(index));
            var choice = Array.IndexOf(RouteLibraries, library);
            if (choice < 0) throw new ArgumentException("未知路线库。", nameof(library));
            var original = index < 0 ? RouteEditDraft.Create(motion) :
                (WorkspaceDocument.GetSection(library) as JArray)?.ElementAtOrDefault(index) as JObject ??
                throw new EditorOperationRejectedException("请选择一条有效路线。");
            var initial = index < 0 ? "" : original.ToString(Formatting.None);
            InspectorStore.SetDraft(SceneRouteSlot, session!.Json, RouteTarget(library, index), initial,
                original.ToString(Formatting.None));
            routeLibraryChoice = choice;
            routeListSelection = index;
            routeDraft = new RouteEditDraft(library, index, original);
            NotifyRouteEdit();
        }

        private void SaveRouteDraft()
        {
            var active = RequireRouteDraft();
            PersistRouteValue(active);
            NotifyRouteEdit();
        }

        private void PersistRouteValue(RouteEditDraft active)
        {
            try { InspectorStore.Update(SceneRouteSlot, active.Route.ToString(Formatting.None)); }
            catch
            {
                if (InspectorStore.TryRead(SceneRouteSlot, out var previous)) active.Replace(ParseRouteObject(previous));
                throw;
            }
        }

        private RouteEditDraft RequireRouteDraft()
        {
            RequireWorkspaceEditable();
            RequireAppliedDraft();
            var active = routeDraft ?? throw new EditorOperationRejectedException("请先新建路线或开始编辑。");
            InspectorStore.RequireCurrent(SceneRouteSlot, session!.Json, RouteTarget(active.Library, active.Index));
            return active;
        }

        private bool RouteDraftIsCurrent(RouteEditDraft route) =>
            InspectorStore.IsCurrent(SceneRouteSlot, session!.Json, RouteTarget(route.Library, route.Index));

        private void NotifyRouteEdit()
        {
            RouteVisualRevision++;
            PersistInspectorDrafts();
            RefreshRouteEditing();
            SceneView.RepaintAll();
        }

        internal void EnterRouteScene(bool draw)
        {
            RequireRouteDraft();
            if (workspaceOwner == null) throw new EditorOperationRejectedException("请在多地图工作台中使用场景路线工具。");
            workspaceOwner.FocusRouteScene(this);
            routePlaybackSelected = false;
            RouteSceneDrawing = draw;
            RouteSceneActive = true;
            SceneView.RepaintAll();
            RefreshRouteEditing();
        }

        internal void ExitRouteScene()
        {
            RouteSceneActive = RouteSceneDrawing = false;
            pendingCheckpoint = -1;
            SceneView.RepaintAll();
            RefreshRouteEditing();
        }

        internal void PlaceRouteCell(Vector2Int cell)
        {
            var active = RequireRouteDraft();
            RequireRouteCell(cell);
            if (pendingCheckpoint >= 0)
            {
                active.Insert(pendingCheckpoint, "APPEAR_AT_POS", cell);
                routeNode = pendingCheckpoint;
                pendingCheckpoint = -1;
            }
            else active.Place(cell);
            SaveRouteDraft();
        }

        internal void RemoveLastRoutePoint()
        {
            RequireRouteDraft().RemoveLastPoint();
            SaveRouteDraft();
        }

        internal bool FinishRouteDrawing()
        {
            if (routeDraft?.Complete != true) return false;
            RouteSceneDrawing = false;
            RefreshRouteEditing();
            SceneView.RepaintAll();
            return true;
        }

        internal void SelectRouteNode(int index)
        {
            if (routeNode == index) return;
            routeNode = index;
            RefreshRouteEditing();
            SceneView.RepaintAll();
        }

        internal void MoveRouteNode(int index, Vector2Int cell)
        {
            var active = RequireRouteDraft();
            RequireRouteCell(cell);
            active.Replace(active.MovedRoute(index, cell));
            routeNode = index;
            SaveRouteDraft();
        }

        internal void InsertRouteNode(int before, Vector2Int cell)
        {
            RequireRouteCell(cell);
            RequireRouteDraft().Insert(before, "MOVE", cell);
            routeNode = before;
            SaveRouteDraft();
        }

        private void RequireRouteCell(Vector2Int cell)
        {
            if (cell.x < 0 || cell.y < 0 || cell.x >= WorkspaceDocument.Width || cell.y >= WorkspaceDocument.Height)
                throw new ArgumentOutOfRangeException(nameof(cell), "节点必须落在地图范围内。");
        }

        internal void ApplyRouteDraft()
        {
            var active = RequireRouteDraft();
            var next = active.Apply(WorkspaceDocument);
            var index = active.Index >= 0 ? active.Index : ((JArray)next.GetSection(active.Library)!).Count - 1;
            if (active.Index >= 0 && !JToken.DeepEquals(active.Route, ReadRouteList()?[index]))
            {
                var uses = ReadRouteUses()[index].ToArray();
                if (uses.Length > 0 && !EditorUtility.DisplayDialog("应用共享路线",
                    $"已识别 {uses.Length} 个使用位置。\n" + string.Join("\n", uses.Take(8)), "应用", "取消")) return;
            }
            CommitEdit(next, "应用路线", null);
            CancelRouteDraft();
            routeListSelection = index;
            RefreshRouteEditing();
        }

        internal void CancelRouteDraft()
        {
            InspectorStore.Remove(SceneRouteSlot);
            routeDraft = null;
            routeNode = NoRouteNode;
            ExitRouteScene();
            NotifyRouteEdit();
        }

        internal JObject? DisplayedRoute() => routeDraft?.Route ??
            (routeListSelection >= 0 ? ReadRouteList()?.ElementAtOrDefault(routeListSelection) as JObject : null);

        private ILookup<int, string> ReadRouteUses() => WorkspaceDocument.RouteUsePaths(RouteLibrary);

        private void ChangeRouteOrder(int target)
        {
            RequireWorkspaceEditable();
            if (routeDraft != null) throw new EditorOperationRejectedException("请先应用或取消当前路线草稿。");
            var index = routeListSelection;
            var next = WorkspaceDocument.MoveRoute(RouteLibrary, index, target);
            var routes = ReadRouteList()!;
            var knownUses = ReadRouteUses();
            var uses = Enumerable.Range(Math.Min(index, target), Math.Abs(index - target) + 1)
                .SelectMany(i => knownUses[i]).ToArray();
            if (uses.Length > 0 && !EditorUtility.DisplayDialog("重排路线",
                $"将同步更新 {uses.Length} 个动作的路线索引。\n" + string.Join("\n", uses.Take(8)), "确定", "取消")) return;
            CommitEdit(next, "重排路线", null);
            routeListSelection = Mathf.Clamp(target, 0, routes.Count - 1);
            RefreshRouteEditing();
        }

        private void DeleteSelectedRoute()
        {
            RequireWorkspaceEditable();
            if (routeDraft != null) throw new EditorOperationRejectedException("请先应用或取消当前路线草稿。");
            var next = WorkspaceDocument.DeleteRoute(RouteLibrary, routeListSelection);
            if (!EditorUtility.DisplayDialog("删除路线", "删除所选路线并更新后续路线索引？", "确定", "取消")) return;
            CommitEdit(next, "删除路线", null);
            routeListSelection = -1;
            RefreshRouteEditing();
        }
    }
}
