#nullable enable
using System;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Storage;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow : EditorWindow
    {
        [SerializeField] private DocumentSession? session;
        [SerializeField] private string draft = "";
        [SerializeField] private bool draftInitialized;
        [SerializeField] private bool sourceExpanded;
        [SerializeField] private MapPreviewStage? ownedPreview;
        [SerializeField] private string recoveryId = "";
        [NonSerialized] private AutoRecovery? recovery;
        [NonSerialized] private string recoveryFingerprint = "";
        [NonSerialized] private double nextRecoveryTime;
        private TextField? jsonField;
        private Label? status;
        [NonSerialized] private IEditorFileDialogs? dialogs;
        public IEditorFileDialogs Dialogs { get => dialogs ??= new EditorFileDialogs(); set => dialogs = value ?? throw new ArgumentNullException(nameof(value)); }

        [MenuItem("Tools/Arknights/Map Editor")]
        public static void Open() => MapWorkspaceWindow.OpenWorkspace();

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndo;
            EditorApplication.update += UpdateRecovery;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            saveChangesMessage = "关卡有未保存修改。保存后关闭，或明确丢弃。";
            if (session == null)
            {
                session = CreateInstance<DocumentSession>();
                session.hideFlags = HideFlags.HideAndDontSave;
                session.Load(LevelDocument.Create(10, 6).ToJson(), "", "");
            }
            if (!draftInitialized) { draft = session.Json; draftInitialized = true; }
            if (ownedPreview != null) BindPreview(ownedPreview);
            if (!Guid.TryParseExact(recoveryId, "N", out var id))
            {
                id = Guid.NewGuid();
                recoveryId = id.ToString("N");
            }
            recovery = new AutoRecovery(RecoveryDirectory, id);
            // The serialized window has authoritative live text after reload. Loading
            // only acquires our cache digest; it never overwrites the live draft.
            if (System.IO.File.Exists(recovery.CachePath))
            {
                try { recovery.Load(); }
                catch (Exception error) { Debug.LogWarning("Map editor recovery cache: " + error.Message); }
            }
        }

        public void CreateGUI()
        {
            var root = workspaceInspector ?? rootVisualElement;
            root.Clear();
            refreshFieldSummaries.Clear();
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/UI/Workbench.uss");
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            root.AddToClassList("workbench");
            if (workspaceOwner?.UsesNativeScenes != true)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.AddToClassList("workbench-toolbar");
                if (workspaceOwner == null)
                {
                    AddButton(row, "新建", NewDocument);
                    AddButton(row, "打开 JSON", OpenDocument);
                }
                BuildDocumentTools(row);
                if (workspaceOwner == null)
                {
                    AddButton(row, "地图示意", OpenPreview);
                    var hand = new Button { name = "standalone-preset-hand", text = PresetHandButtonText };
                    hand.clicked += () =>
                    {
                        SetPresetHandVisible(!PresetHandVisible);
                        hand.text = PresetHandButtonText;
                    };
                    row.Add(hand);
                }
                root.Add(row);
            }
            BuildWorkbench(root);
            status = new Label(); status.AddToClassList("workbench-status");
            root.Add(status);
            Refresh();
        }

        internal void BuildDocumentTools(VisualElement row)
        {
            AddButton(row, "保存", () => SaveDocument(false));
            AddButton(row, "另存", () => SaveDocument(true));
            AddButton(row, "撤销", () => StepEditorHistory(false));
            AddButton(row, "重做", () => StepEditorHistory(true));
            AddButton(row, "保存恢复副本", SaveRecovery);
            AddButton(row, "恢复草稿", RestoreRecovery);
        }

        private void AddButton(VisualElement parent, string text, Action action)
        {
            parent.Add(new Button(() =>
            {
                try { action(); }
                catch (Exception error) { if (status != null) status.text = error.Message; }
            }) { text = text });
        }

        private bool CanReplace() => CanReplaceProject() && (!InspectorDirty || Dialogs.Discard("未应用局部草稿", "丢弃局部编辑框中的修改？")) && (session == null || (!DocumentDirty && draft == session.Json) ||
            Dialogs.Discard("未保存关卡", "丢弃当前文档修改和 JSON 草稿？"));

        private void ApplyDraft()
        {
            RequireWorkspaceEditable();
            session!.Edit(LevelDocument.Parse(draft), "编辑关卡 JSON");
            draft = session.Json;
            lastRenderedJson = session.Json;
            if (ownedPreview != null) ownedPreview.UpdateDocument(session.Document, null);
            workspaceOwner?.DocumentChanged(this);
        }

        private void OnUndo()
        {
            if (workspaceOwner != null) return;
            // An external Unity undo must not silently replace an un-applied text draft.
            if (!hasUnsavedChanges || draft == lastRenderedJson) draft = session!.Json;
            Refresh();
            if (ownedPreview != null) ownedPreview.UpdateDocument(session!.Document, null);
        }

        [NonSerialized] private string lastRenderedJson = "";

        private void NewDocument()
        {
            if (!CanReplace()) return;
            BeginRecoveryGeneration();
            CloseOwnedPreview();
            session!.Load(LevelDocument.Create(10, 6).ToJson(), "", "");
            ResetProjectState();
            ClearInspectorDrafts();
            draft = session.Json;
            titleContent = new GUIContent("LevelData Editor");
            workbenchTab = 0;
            CreateGUI();
            if (!EditorApplication.isPlayingOrWillChangePlaymode) OpenPreview();
        }

        private void OpenPreview()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出 Play Mode 后编辑地图。");
            ApplyDraft();
            CloseOwnedPreview();
            var stage = CreateInstance<MapPreviewStage>();
            stage.Configure(session!.Document);
            BindPreview(stage);
            ownedPreview = stage;
            // Dedicated multi-document SceneViews deliberately ignore stages.
            // GetWindow<SceneView> can return those subclasses; select an exact
            // native SceneView so standalone documents always have a visible host.
            SceneView? target = null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<SceneView>())
                if (candidate.GetType() == typeof(SceneView)) { target = candidate; break; }
            if (target == null) target = CreateWindow<SceneView>("地图场景");
            target.Show();
            StageUtility.GoToStage(stage, false);
            stage.SetView(false, target);
            target.Focus();
            Refresh();
        }

        private void OpenDocument()
        {
            var path = Dialogs.Open("打开 LevelData", "", "json");
            if (path.Length == 0) return;
            if (FocusOtherWriter(path) || !CanReplace()) return;
            var snapshot = DocumentFileStore.Read(path);
            try { LevelDocument.Parse(snapshot.Text); }
            catch (LevelDocumentException error) { InvalidDocumentWindow.Open(snapshot.Text, error.Message); return; }
            BeginRecoveryGeneration();
            CloseOwnedPreview();
            session!.Load(snapshot.Text, path, snapshot.Sha256);
            ResetProjectState();
            ClearInspectorDrafts();
            draft = session.Json;
            CreateGUI();
        }

        private static string RecoveryDirectory => System.IO.Path.GetFullPath(
            System.IO.Path.Combine(Application.dataPath, "../Library/ArknightsMapEditor/Recovery"));

        private RecoverySnapshot CaptureWindowRecovery()
        {
            var snapshot = session!.CaptureRecovery(draft);
            return new RecoverySnapshot(snapshot.DocumentJson, snapshot.DraftJson, snapshot.SavedJson,
                snapshot.SourcePath, snapshot.SourceHash, CaptureProjectRecovery(), CaptureInspectorRecovery());
        }

        private void SaveRecovery()
        {
            System.IO.Directory.CreateDirectory(RecoveryDirectory);
            var path = System.IO.Path.Combine(RecoveryDirectory, Guid.NewGuid().ToString("N") + ".json");
            RecoveryStore.Save(path, CaptureWindowRecovery(), null);
            if (status != null) status.text = "恢复副本已保存：" + path;
        }

        private void RestoreRecovery()
        {
            RequireWorkspaceEditable();
            if (!CanReplace()) return;
            var path = Dialogs.Open("恢复草稿（不会覆盖源文件）", RecoveryDirectory, "json");
            if (path.Length == 0) return;
            var snapshot = RecoveryStore.Read(path, out _);
            var projectState = ParseProjectRecovery(snapshot.ProjectJson);
            var inspectorState = snapshot.InspectorJson.Length == 0 ? new InspectorDraftStore() : InspectorDraftStore.Parse(snapshot.InspectorJson);
            ValidateInspectorRecovery(inspectorState);
            if (snapshot.SourcePath.Length > 0 && FocusOtherWriter(snapshot.SourcePath)) return;
            // Validate without replacing live state or its recovery journal.
            var validator = CreateInstance<DocumentSession>();
            try { validator.RestoreRecovery(snapshot); }
            finally { DestroyImmediate(validator); }
            BeginRecoveryGeneration();
            CloseOwnedPreview();
            draft = session!.RestoreRecovery(snapshot);
            if (projectState != null) ApplyProjectRecovery(projectState); else ResetProjectState();
            ApplyInspectorRecovery(inspectorState);
            CreateGUI();
        }

        private bool SaveDocument(bool saveAs)
        {
            if (InspectorDirty) throw new InvalidOperationException("请先应用或明确丢弃局部编辑草稿，再保存关卡。");
            ApplyDraft();
            var path = session!.SourcePath;
            var errors = session.Document.ValidateReferences();
            foreach (var diagnostic in errors)
                if (diagnostic.Severity == "error") throw new FormatException(diagnostic.Path + ": " + diagnostic.Message);
            if (saveAs || path.Length == 0)
                path = Dialogs.Save("保存 LevelData（另存须使用新文件名）", "", "level.json", "json");
            if (path.Length == 0) return false;
            if (FocusOtherWriter(path)) throw new InvalidOperationException("该文档已由另一个编辑窗口打开，请使用已聚焦的窗口。");
            session.Save(path);
            Refresh();
            return true;
        }

        private bool FocusOtherWriter(string path) => TryFocusOpenDocument(path, this);

        internal static bool TryFocusOpenDocument(string path, MapEditorWindow? current = null)
        {
            var full = System.IO.Path.GetFullPath(path);
            var comparison = Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            foreach (var other in Resources.FindObjectsOfTypeAll<MapEditorWindow>())
            {
                if (other == current || other.session == null || other.session.SourcePath.Length == 0) continue;
                if (!string.Equals(full, System.IO.Path.GetFullPath(other.session.SourcePath), comparison)) continue;
                if (other.workspaceOwner != null) other.workspaceOwner.FocusDocument(other);
                else other.Focus();
                return true;
            }
            return false;
        }

        private void BeginRecoveryGeneration()
        {
            FlushRecovery();
            var id = Guid.NewGuid(); recoveryId = id.ToString("N");
            recovery = new AutoRecovery(RecoveryDirectory, id); recoveryFingerprint = "";
        }

        private void Refresh()
        {
            if (session == null) return;
            hasUnsavedChanges = WorkspaceDirty;
            jsonField?.SetValueWithoutNotify(draft);
            lastRenderedJson = session.Json;
            routePreview?.Refresh(session.Document);
            refreshPresets?.Invoke();
            refreshDocumentView?.Invoke();
            if (status != null) status.text = $"{session.Document.Width} × {session.Document.Height} | " +
                (draft != session.Json ? "有未应用完整草稿" : InspectorDirty ? "有未应用局部草稿" : DocumentDirty || ProjectDirty ? "未保存" : "文档已载入") + " | LevelData 编辑器；地图示意随已应用修改更新";
            workspaceOwner?.DocumentChanged(this);
        }

        public override void SaveChanges()
        {
            try
            {
                if (!SaveDocument(false)) return;
                if (ProjectDirty && !SaveProjectChanges()) return;
                if (!WorkspaceDirty) base.SaveChanges();
            }
            catch (Exception error)
            {
                hasUnsavedChanges = true;
                if (status != null) status.text = error.Message;
            }
        }

        private void CloseOwnedPreview()
        {
            if (ownedPreview != null) ownedPreview.Unbind();
            if (ownedPreview != null && StageUtility.GetCurrentStage() == ownedPreview)
                StageUtility.GoBackToPreviousStage();
            ownedPreview = null;
        }

        private void UpdateRecovery()
        {
            if (EditorApplication.timeSinceStartup < nextRecoveryTime) return;
            nextRecoveryTime = EditorApplication.timeSinceStartup + 2;
            FlushRecovery();
        }

        private void FlushRecovery()
        {
            if (session == null || recovery == null || !WorkspaceDirty) return;
            var fingerprint = session.Json + "\u0000" + draft + "\u0000" + ProjectRecoveryFingerprint + "\u0000" + CaptureInspectorRecovery();
            if (fingerprint == recoveryFingerprint) return;
            try
            {
                recovery.Save(CaptureWindowRecovery());
                recoveryFingerprint = fingerprint;
            }
            catch (Exception error)
            {
                // Throttle identical failures until the user changes the document.
                recoveryFingerprint = fingerprint;
                if (status != null) status.text = "自动恢复副本保存失败：" + error.Message;
                Debug.LogWarning("Map editor recovery: " + error.Message);
            }
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) { FlushRecovery(); CloseOwnedPreview(); }
        }

        private void OnDisable()
        {
            routePreview?.Stop();
            FlushRecovery();
            Undo.undoRedoPerformed -= OnUndo;
            EditorApplication.update -= UpdateRecovery;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }
        private void OnDestroy()
        {
            CloseOwnedPreview();
            if (session != null) { Undo.ClearUndo(session); DestroyImmediate(session); }
        }
    }
}
