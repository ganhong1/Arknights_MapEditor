#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        [SerializeField] private MapWorkspaceWindow? workspaceOwner;
        [SerializeField] private string workspaceTitle = "";
        [SerializeField] private bool workspaceEditLocked;
        [SerializeField] private string finishedLevelOrigin = "";
        [NonSerialized] private VisualElement? workspaceInspector;

        internal LevelDocument WorkspaceDocument => session!.Document;
        internal string WorkspacePath => session!.SourcePath;
        private bool DocumentDirty => session!.IsDirty || (finishedLevelOrigin.Length > 0 && session.SourcePath.Length == 0);
        internal bool WorkspaceDirty => DocumentDirty || draft != session!.Json || ProjectDirty || InspectorDirty;
        internal string WorkspaceTitle => WorkspacePath.Length > 0 ? Path.GetFileNameWithoutExtension(WorkspacePath) : workspaceTitle;
        internal bool WorkspaceEditLocked => workspaceEditLocked;
        internal void SetWorkspaceOwner(MapWorkspaceWindow owner) => workspaceOwner = owner;
        internal bool IsStandalone => workspaceOwner == null;

        internal MapEditorWindow CopyToWorkspace(MapWorkspaceWindow owner)
        {
            var snapshot = CaptureWindowRecovery();
            var copy = CreateInstance<MapEditorWindow>();
            try
            {
                copy.LoadWorkspaceDocument(owner, "恢复地图", snapshot.DocumentJson, snapshot.SourcePath, snapshot.SourceHash);
                copy.draft = copy.session!.RestoreRecovery(snapshot);
                var project = ParseProjectRecovery(snapshot.ProjectJson);
                if (project != null) copy.ApplyProjectRecovery(project);
                copy.ApplyInspectorRecovery(InspectorDraftStore.Parse(snapshot.InspectorJson));
                copy.editorDrawerOpen = editorDrawerOpen;
                copy.moduleNavigationOpen = moduleNavigationOpen;
                copy.resourceDrawerOpen = resourceDrawerOpen;
                copy.editorPanelWidth = editorPanelWidth;
                copy.libraryPanelWidth = libraryPanelWidth;
                copy.workbenchTab = workbenchTab;
                copy.routePreviewSettings = routePreviewSettings;
                copy.sourceExpanded = sourceExpanded;
                return copy;
            }
            catch { DestroyImmediate(copy); throw; }
        }
        internal void SetWorkspaceEditLock(bool locked)
        {
            workspaceEditLocked = locked;
            workspaceOwner?.DocumentChanged(this);
        }

        private void RequireWorkspaceEditable()
        {
            if (workspaceEditLocked) throw new InvalidOperationException("此地图已启用编辑锁。");
        }

        internal void LoadWorkspaceDocument(MapWorkspaceWindow owner, string title, string json, string path, string hash)
        {
            workspaceOwner = owner;
            workspaceTitle = title;
            session!.UseLocalHistory();
            session.Load(json, path, hash);
            draft = session.Json;
        }

        internal void SetFinishedLevelOrigin(string origin) => finishedLevelOrigin = origin;

        internal void CreateFromFinishedLevel(string title, string json, string origin)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出 Play Mode 后新建地图。");
            if (workspaceOwner != null)
            {
                workspaceOwner.AddFinishedLevel(title, json, origin);
                return;
            }
            var copy = CreateInstance<MapEditorWindow>();
            try
            {
                copy.session!.Load(json, "", "");
                copy.draft = copy.session.Json;
                copy.SetFinishedLevelOrigin(origin);
                copy.titleContent = new GUIContent(title);
                copy.Show();
                copy.OpenPreview();
            }
            catch
            {
                DestroyImmediate(copy);
                throw;
            }
        }

        internal VisualElement WorkspaceInspector()
        {
            if (workspaceInspector == null)
            {
                workspaceInspector = new VisualElement();
                workspaceInspector.style.flexGrow = 1;
                CreateGUI();
            }
            return workspaceInspector;
        }

        internal bool ConfirmWorkspaceClose() => CanReplace();

        private void StepEditorHistory(bool forward)
        {
            ApplyDraft();
            if (workspaceOwner == null)
            {
                if (forward) Undo.PerformRedo();
                else Undo.PerformUndo();
                return;
            }
            session!.StepHistory(forward);
            draft = session.Json;
            Refresh();
        }

        internal void PaintWorkspaceCells(IReadOnlyList<Vector2Int> cells)
        {
            RequireAppliedDraft();
            if (devicePlacementEnabled && workbenchTab == 5 && !erase)
            {
                placeDeviceCells?.Invoke(cells);
                return;
            }
            PlaceManagedTiles(cells);
        }

        internal void PickWorkspaceCell(Vector2Int cell)
        {
            brushJson = session!.Document.GetCell(cell.y, cell.x).ToString(Formatting.Indented);
            SetEraseMode(false);
        }
    }
}
