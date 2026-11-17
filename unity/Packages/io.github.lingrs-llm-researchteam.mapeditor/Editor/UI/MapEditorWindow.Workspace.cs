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
        internal MapWorkspaceWindow? WorkspaceOwner => workspaceOwner;
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
                copy.routePlaybackSelected = routePlaybackSelected;
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
            if (workspaceEditLocked) throw new EditorOperationRejectedException("此地图已启用编辑锁。");
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
                throw new EditorOperationRejectedException("请退出 Play Mode 后新建地图。");
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
                try { CreateGUI(); }
                catch
                {
                    workspaceInspector = null;
                    throw;
                }
            }
            return workspaceInspector;
        }

        internal bool ConfirmWorkspaceClose() => CanReplace();

        internal static bool IsUndoShortcut(KeyCode key, EventModifiers modifiers)
        {
            const EventModifiers relevant = EventModifiers.Control | EventModifiers.Shift |
                EventModifiers.Alt | EventModifiers.Command;
            return key == KeyCode.Z && (modifiers & relevant) == EventModifiers.Control;
        }

        internal static bool IsTextInput(VisualElement? target)
        {
            for (var element = target; element != null; element = element.parent)
                if (element is TextField || element.ClassListContains(TextInputBaseField<string>.ussClassName) ||
                    element.name == TextInputBaseField<string>.textInputUssName) return true;
            return false;
        }

        internal void UndoFromShortcut()
        {
            if (WorkspaceEditLocked) return;
            try { StepEditorHistory(false); }
            catch (Exception error)
            {
                if (status != null) status.text = error.Message;
                if (workspaceOwner != null) workspaceOwner.ReportNativeError("document.undo", error);
                else ReportWindowFailure("document.undo", error);
            }
        }

        internal static void HandleUndoKey(KeyDownEvent input, MapEditorWindow? editor)
        {
            if (editor == null || !IsUndoShortcut(input.keyCode, input.modifiers) ||
                IsTextInput(input.target as VisualElement)) return;
            input.StopImmediatePropagation();
            input.PreventDefault();
            input.imguiEvent?.Use();
            editor.UndoFromShortcut();
        }

        private void StepEditorHistory(bool forward)
            => DiagnoseDocumentOperation(forward ? "document.redo" : "document.undo", () => StepEditorHistoryCore(forward));

        private void StepEditorHistoryCore(bool forward)
        {
            RequireWorkspaceEditable();
            RequireAppliedDraft();
            var previous = session!.Document;
            session!.StepHistory(forward);
            RestoreWaveHistory();
            RestoreBranchHistory();
            var rebased = RebaseModuleDrafts(InspectorStore, previous, session.Document);
            if (rebased != null)
            {
                inspectorStore = rebased;
                inspectorJson = rebased.ToJson();
            }
            ReconcileRecordSelections(previous, session.Document);
            draft = session.Json;
            Refresh();
            ownedPreview?.UpdateDocument(session.Document, null);
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
