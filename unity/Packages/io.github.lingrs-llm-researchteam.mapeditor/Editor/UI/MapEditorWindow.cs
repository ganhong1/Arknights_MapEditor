#nullable enable
using System;
using System.IO;
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
        [NonSerialized] private RecoverySnapshot? lastRecovery;
        [NonSerialized] private string? recoveryError;
        [NonSerialized] private string? failedRecoverySession;
        [NonSerialized] private double retryRecoveryAt;
        [NonSerialized] private bool loadRecoveryDigest = true;
        [NonSerialized] private double nextRecoveryTime;
        private TextField? jsonField;
        private Label? status;
        [NonSerialized] private IEditorFileDialogs? dialogs;
        public IEditorFileDialogs Dialogs { get => dialogs ??= new EditorFileDialogs(); set => dialogs = value ?? throw new ArgumentNullException(nameof(value)); }

        [MenuItem("Tools/Arknights/Map Editor")]
        public static void Open() => MapWorkspaceWindow.OpenWorkspace();

        private void OnEnable()
        {
            rootVisualElement.RegisterCallback<KeyDownEvent>(HandleStandaloneUndo, TrickleDown.TrickleDown);
            EditorApplication.update += UpdateRecovery;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            saveChangesMessage = "关卡有未保存修改。保存后关闭，或明确丢弃。";
            if (session == null)
            {
                session = CreateInstance<DocumentSession>();
                session.hideFlags = HideFlags.HideAndDontSave;
                session.Load(LevelDocument.Create(10, 6).ToJson(), "", "");
            }
            session.UseLocalHistory();
            if (!draftInitialized) { draft = session.Json; draftInitialized = true; }
            if (ownedPreview != null) BindPreview(ownedPreview);
            if (!Guid.TryParseExact(recoveryId, "N", out _))
                recoveryId = Guid.NewGuid().ToString("N");
            TryOpenRecovery();
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
                    AddButton(row, "打开 JSON", OpenDocument, "document.open");
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
            AddButton(row, "保存", () => SaveDocument(false), "document.save");
            AddButton(row, "另存", () => SaveDocument(true), "document.save-as");
            AddButton(row, "撤销", () => StepEditorHistory(false));
            AddButton(row, "重做", () => StepEditorHistory(true));
            AddButton(row, "打开地图文件所在位置", OpenMapFileLocation);
            AddButton(row, "保存恢复副本", SaveRecovery, "recovery.manual-save");
            AddButton(row, "恢复草稿", RestoreRecovery, "recovery.restore");
        }

        private void AddButton(VisualElement parent, string text, Action action, string operation = "document.command")
        {
            parent.Add(new Button(() =>
            {
                try { action(); }
                catch (Exception error) { ReportWindowFailure(operation, error); }
            }) { text = text });
        }

        private bool CanReplace() => CanReplaceProject() && (!InspectorDirty || Dialogs.Discard("未应用局部草稿", "丢弃局部编辑框中的修改？")) && (session == null || (!DocumentDirty && draft == session.Json) ||
            Dialogs.Discard("未保存关卡", "丢弃当前文档修改和 JSON 草稿？"));

        private void ApplyDraft()
        {
            RequireWorkspaceEditable();
            var previous = session!.Document;
            EditSession(LevelDocument.Parse(draft), "编辑关卡 JSON");
            ReconcileRecordSelections(previous, session.Document);
            if (ownedPreview != null) ownedPreview.UpdateDocument(session.Document, null);
            workspaceOwner?.DocumentChanged(this);
        }

        private void HandleStandaloneUndo(KeyDownEvent input)
        {
            if (workspaceOwner == null) HandleUndoKey(input, this);
        }

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
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new EditorOperationRejectedException("请退出 Play Mode 后编辑地图。");
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
            InvalidateSceneStageLayout();
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
            => DiagnoseDocumentOperation("recovery.manual-save", SaveRecoveryCore);

        private void SaveRecoveryCore()
        {
            System.IO.Directory.CreateDirectory(RecoveryDirectory);
            var path = System.IO.Path.Combine(RecoveryDirectory, Guid.NewGuid().ToString("N") + ".json");
            RecoveryStore.Save(path, CaptureWindowRecovery(), null);
            if (status != null) status.text = "恢复副本已保存：" + path;
        }

        private void RestoreRecovery()
            => DiagnoseDocumentOperation("recovery.restore", RestoreRecoveryCore, value => !value);

        private bool RestoreRecoveryCore()
        {
            RequireWorkspaceEditable();
            if (!CanReplace()) return false;
            var path = Dialogs.Open("恢复草稿（不会覆盖源文件）", RecoveryDirectory, "json");
            if (path.Length == 0) return false;
            var snapshot = RecoveryStore.Read(path, out _);
            var projectState = ParseProjectRecovery(snapshot.ProjectJson);
            var inspectorState = snapshot.InspectorJson.Length == 0 ? new InspectorDraftStore() : InspectorDraftStore.Parse(snapshot.InspectorJson);
            ValidateInspectorRecovery(inspectorState);
            if (snapshot.SourcePath.Length > 0 && FocusOtherWriter(snapshot.SourcePath)) return false;
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
            return true;
        }

        private bool SaveDocument(bool saveAs)
            => DiagnoseDocumentOperation(saveAs ? "document.save-as" : "document.save", () => SaveDocumentCore(saveAs), value => !value);

        private bool SaveDocumentCore(bool saveAs)
        {
            RequireWorkspaceEditable();
            if (InspectorDirty) throw new EditorOperationRejectedException("请先应用或明确丢弃局部编辑草稿，再保存关卡。");
            RequireAppliedDraft();
            var path = session!.SourcePath;
            var errors = session.Document.ValidateReferences();
            foreach (var diagnostic in errors)
                if (diagnostic.Severity == "error") throw new FormatException(diagnostic.Path + ": " + diagnostic.Message);
            if (saveAs || path.Length == 0)
                path = Dialogs.Save("保存 LevelData（另存须使用新文件名）", "", "level.json", "json");
            if (path.Length == 0) return false;
            if (FocusOtherWriter(path)) throw new EditorOperationRejectedException("该文档已由另一个编辑窗口打开，请使用已聚焦的窗口。");
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
            ResetRecoveryGeneration();
            recoveryError = null;
            failedRecoverySession = null;
            retryRecoveryAt = 0;
            TryOpenRecovery();
        }

        private void ResetRecoveryGeneration()
        {
            recovery = null;
            recoveryId = Guid.NewGuid().ToString("N");
            lastRecovery = null;
            loadRecoveryDigest = true;
        }

        private bool TryOpenRecovery(string? directory = null)
        {
            if (recovery != null && !loadRecoveryDigest) return true;
            try
            {
                recovery ??= new AutoRecovery(directory ?? RecoveryDirectory, Guid.ParseExact(recoveryId, "N"));
                // Acquire the cache digest without applying its content to the live document.
                if (File.Exists(recovery.CachePath))
                {
                    try { recovery.Load(); }
                    catch (Exception error) when (error is InvalidDataException || error is System.Text.DecoderFallbackException)
                    {
                        ReportRecoveryFailure("recovery.invalid-journal", error);
                        ResetRecoveryGeneration();
                        recovery = new AutoRecovery(directory ?? RecoveryDirectory, Guid.ParseExact(recoveryId, "N"));
                    }
                }
                loadRecoveryDigest = false;
                if (recoveryError != null && !WorkspaceDirty) MarkRecoveryHealthy();
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                error is InvalidDataException || error is ArgumentException || error is NotSupportedException)
            {
                ReportRecoveryFailure("recovery.open", error);
                return false;
            }
        }

        private void ReportRecoveryFailure(string operation, Exception error)
        {
            retryRecoveryAt = EditorApplication.timeSinceStartup + 10;
            var message = "自动恢复暂不可用：" + error.Message;
            if (recoveryError == null || !recoveryError.StartsWith(message + " · 日志 ", StringComparison.Ordinal))
            {
                failedRecoverySession = recoveryId;
                recoveryError = message + " · 日志 " + PluginLog.Failure(operation, error, recoveryId);
            }
            if (status != null) status.text = recoveryError;
        }

        private void ReportWindowFailure(string operation, Exception error)
        {
            var message = PluginLog.FailureMessage(operation, error, recoveryId);
            if (status != null) status.text = message;
        }

        private void MarkRecoveryHealthy()
        {
            PluginLog.Recovered("recovery", failedRecoverySession ?? recoveryId);
            failedRecoverySession = null;
            recoveryError = null;
            retryRecoveryAt = 0;
        }

        private void Refresh()
        {
            if (session == null) return;
            NoteWaveRevision();
            NoteBranchRevision();
            hasUnsavedChanges = WorkspaceDirty;
            jsonField?.SetValueWithoutNotify(draft);
            routePreview?.Refresh(session.Document);
            RefreshRouteEditing();
            refreshPresets?.Invoke();
            refreshDocumentView?.Invoke();
            RefreshWaves();
            RefreshBranches();
            RefreshMusic();
            if (status != null) status.text = $"{session.Document.Width} × {session.Document.Height} | " +
                (draft != session.Json ? "有未应用完整草稿" : InspectorDirty ? "有未应用局部草稿" : DocumentDirty || ProjectDirty ? "未保存" : "文档已载入") + " | LevelData 编辑器；地图示意随已应用修改更新" + (recoveryError == null ? "" : " | " + recoveryError);
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
                ReportWindowFailure("document.save-on-close", error);
            }
        }

        private void CloseOwnedPreview()
        {
            if (ownedPreview != null) ownedPreview.Unbind();
            if (ownedPreview != null && StageUtility.GetCurrentStage() == ownedPreview)
            {
                StageUtility.GoBackToPreviousStage();
                InvalidateSceneStageLayout();
            }
            ownedPreview = null;
        }

        private static void InvalidateSceneStageLayout()
        {
            // Unity 2021.3's scene breadcrumb changes its GUILayout tree when the stage changes.
            // Undo can send ValidateCommand before the next frame, so invalidate the cached layout now.
            foreach (SceneView view in SceneView.sceneViews)
            {
                if (view == null || view.GetType() != typeof(SceneView)) continue;
                var root = view.rootVisualElement;
                while (root.parent != null) root = root.parent;
                // The stage toolbar is a sibling of the public SceneView content root.
                root.Query<IMGUIContainer>().Where(container =>
                    container.name?.StartsWith("prefab-toolbar", StringComparison.Ordinal) == true)
                    .ForEach(container => container.MarkDirtyLayout());
            }
        }

        private void UpdateRecovery()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now < nextRecoveryTime || now < retryRecoveryAt) return;
            nextRecoveryTime = now + 2;
            FlushRecovery();
        }

        private void FlushRecovery()
        {
            try
            {
                if (session == null || !WorkspaceDirty || !TryOpenRecovery()) return;
                var snapshot = CaptureWindowRecovery();
                if (recoveryError == null && lastRecovery?.HasSameContent(snapshot) == true) return;
                try { recovery!.Save(snapshot); }
                catch (DocumentFileConflictException error)
                {
                    ReportRecoveryFailure("recovery.journal-conflict", error);
                    var directory = Path.GetDirectoryName(recovery!.CachePath);
                    ResetRecoveryGeneration();
                    if (!TryOpenRecovery(directory)) return;
                    recovery!.Save(snapshot);
                }
                lastRecovery = snapshot;
                if (recoveryError != null)
                {
                    MarkRecoveryHealthy();
                    if (status != null) status.text = "自动恢复已恢复写入。";
                }
            }
            catch (Exception error)
            {
                ReportRecoveryFailure("recovery.write", error);
            }
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) { FlushRecovery(); CloseOwnedPreview(); }
        }

        private void OnDisable()
        {
            try
            {
                rootVisualElement.UnregisterCallback<KeyDownEvent>(HandleStandaloneUndo, TrickleDown.TrickleDown);
                routePreview?.Stop();
                FlushRecovery();
            }
            finally
            {
                EditorApplication.update -= UpdateRecovery;
                EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            }
        }
        private void OnDestroy()
        {
            CloseOwnedPreview();
            if (session != null) { Undo.ClearUndo(session); DestroyImmediate(session); }
        }
    }
}
