#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Storage;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapWorkspaceWindow : EditorWindow
    {
        [Serializable]
        private sealed class MapTab
        {
            [SerializeField] internal MapEditorWindow Editor = null!;
            [SerializeField] internal MapViewportState Camera = new MapViewportState();
        }

        [Serializable]
        private sealed class MapGroup
        {
            [SerializeField] internal List<MapTab> Tabs = new List<MapTab>();
            [SerializeField] internal int Active;
            internal MapTab? Selected => Active >= 0 && Active < Tabs.Count ? Tabs[Active] : null;
        }

        [SerializeField] private MapGroup left = new MapGroup();
        [SerializeField] private MapGroup right = new MapGroup();
        [SerializeField] private bool split;
        [SerializeField] private int focusedGroup;
        [SerializeField] private int nextUntitled = 1;

        private readonly VisualElement?[] groupHosts = new VisualElement?[2];
        private readonly MapViewportPanel?[] viewports = new MapViewportPanel?[2];
        private readonly List<(MapEditorWindow Editor, Button Button)> tabButtons = new List<(MapEditorWindow, Button)>();
        [SerializeField] private MapWorkspaceInspectorWindow? inspectorWindow;
        private Label? focusLabel;
        private Label? status;

        [MenuItem("Tools/Arknights/多地图工作台")]
        public static void OpenWorkspace()
        {
            var open = Resources.FindObjectsOfTypeAll<MapWorkspaceWindow>();
            var native = open.FirstOrDefault(window => window.nativeScenes);
            if (native != null) { native.CreateGUI(); native.Focus(); return; }
            var source = open.FirstOrDefault();
            var standalone = Resources.FindObjectsOfTypeAll<MapEditorWindow>().Where(editor => editor.IsStandalone).ToArray();
            var workspace = CreateInstance<MapWorkspaceWindow>();
            workspace.nativeScenes = true;
            workspace.titleContent = new GUIContent("关卡工作台");
            workspace.position = source?.inspectorWindow != null ? source.inspectorWindow.position : NativeInspectorBounds();
            if (source != null) workspace.AdoptWorkspace(source);
            else
            {
                foreach (var editor in standalone)
                    workspace.AddTab(0, new MapTab { Editor = editor.CopyToWorkspace(workspace) }, false);
                foreach (var editor in standalone) { editor.DiscardChanges(); editor.Close(); }
                workspace.CreateGUI();
            }
            workspace.Show();
            workspace.Focus();
        }

        private void AdoptWorkspace(MapWorkspaceWindow source)
        {
            var previousLeft = left;
            var previousRight = right;
            var previousEditors = Documents.ToArray();
            var movedEditors = source.Documents.ToArray();
            left = source.left;
            right = source.right;
            split = source.split;
            focusedGroup = source.focusedGroup;
            nextUntitled = source.nextUntitled;
            foreach (var editor in movedEditors) editor.SetWorkspaceOwner(this);
            try { CreateGUI(); }
            catch
            {
                foreach (var editor in movedEditors) editor.SetWorkspaceOwner(source);
                left = previousLeft;
                right = previousRight;
                Close();
                source.CreateGUI();
                throw;
            }
            source.left = new MapGroup();
            source.right = new MapGroup();
            source.hasUnsavedChanges = false;
            source.Close();
            foreach (var editor in previousEditors) DestroyImmediate(editor);
            Focus();
        }

        private MapGroup Group(int index) => index == 0 ? left : right;
        internal IEnumerable<MapEditorWindow> Documents => left.Tabs.Concat(right.Tabs).Select(tab => tab.Editor).Where(editor => editor != null).Distinct();
        internal MapEditorWindow? FocusedEditor => Group(focusedGroup).Selected?.Editor;

        private void OnEnable()
        {
            saveChangesMessage = "工作台有未保存的地图。保存后关闭，或明确丢弃。";
            rootVisualElement.RegisterCallback<KeyDownEvent>(HandleWorkspaceUndo, TrickleDown.TrickleDown);
        }

        private void HandleWorkspaceUndo(KeyDownEvent input) => MapEditorWindow.HandleUndoKey(input, FocusedEditor);

        public void CreateGUI()
        {
            if (nativeScenes) { CreateNativeGUI(); return; }
            ReleaseViewports();
            var root = rootVisualElement;
            root.Clear();
            tabButtons.Clear();
            root.AddToClassList("map-workspace");
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/UI/Workbench.uss");
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            if (left.Tabs.Count == 0 && right.Tabs.Count == 0) AddBlank(0, false);

            var toolbar = new VisualElement();
            toolbar.AddToClassList("workspace-row");
            toolbar.Add(Button("新建地图", () => AddBlank(focusedGroup)));
            toolbar.Add(Button("打开地图 JSON", OpenFile, operation: "document.open"));
            toolbar.Add(Button("打开地图文件所在位置", () => FocusedEditor?.OpenMapFileLocation()));
            toolbar.Add(Button(split ? "合并为单组" : "左右分屏", ToggleSplit));
            if (split) toolbar.Add(Button("在另一组显示此图", ShowInOtherGroup));
            toolbar.Add(Button("编辑工作台", ShowInspector));
            focusLabel = new Label { name = "workspace-active-document" };
            toolbar.Add(focusLabel);
            root.Add(toolbar);

            var maps = split
                ? (VisualElement)new TwoPaneSplitView(0, Mathf.Max(230, position.width * .5f), TwoPaneSplitViewOrientation.Horizontal)
                : new VisualElement();
            maps.AddToClassList("workspace-map-area");
            maps.AddToClassList("workspace-split");
            for (var i = 0; i < (split ? 2 : 1); i++)
            {
                var group = new VisualElement { name = i == 0 ? "workspace-left" : "workspace-right" };
                group.AddToClassList("workspace-group");
                groupHosts[i] = group;
                maps.Add(group);
                BuildGroup(i);
            }
            if (!split) groupHosts[1] = null;
            root.Add(maps);
            status = new Label { name = "workspace-status" };
            status.AddToClassList("workspace-status");
            root.Add(status);
            RefreshFocus();
        }

        private void BuildGroup(int index)
        {
            if (nativeScenes) { BindNativeGroup(index); return; }
            var host = groupHosts[index];
            if (host == null) return;
            viewports[index]?.Dispose();
            viewports[index] = null;
            tabButtons.RemoveAll(item => host.Contains(item.Button));
            host.Clear();
            var tabs = new ScrollView(ScrollViewMode.Horizontal);
            tabs.AddToClassList("workspace-tabs");
            tabs.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            tabs.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            tabs.contentContainer.style.flexDirection = FlexDirection.Row;
            var group = Group(index);
            foreach (var tab in group.Tabs)
            {
                var select = Button("", () => Select(index, tab));
                var pair = DocumentTab(select, Button("×", () => CloseTab(index, tab), "关闭 " + tab.Editor.WorkspaceTitle));
                pair.EnableInClassList("workspace-selected", tab == group.Selected);
                tabButtons.Add((tab.Editor, select));
                tabs.Add(pair);
            }
            tabs.Add(Button("+", () => AddBlank(index), "在此组新建地图"));
            host.Add(tabs);
            var selected = group.Selected;
            if (selected == null) return;
            var viewport = new MapViewportPanel(selected.Editor, selected.Camera, () => FocusGroup(index), Report);
            viewports[index] = viewport;
            host.Add(viewport.Element);
        }

        private static VisualElement DocumentTab(Button title, Button close)
        {
            var tab = new VisualElement();
            tab.AddToClassList("workspace-document-tab");
            title.AddToClassList("workspace-document-title");
            close.AddToClassList("workspace-document-close");
            tab.Add(title);
            tab.Add(close);
            return tab;
        }

        private Button Button(string text, Action action, string tooltip = "", string operation = "workspace.command") => new Button(() =>
        {
            try { action(); }
            catch (Exception error) { Report(PluginLog.FailureMessage(operation, error)); }
        }) { text = text, tooltip = tooltip };

        private void Report(string message)
        {
            if (status != null) status.text = message;
        }

        private void RefreshFocus()
        {
            for (var i = 0; i < groupHosts.Length; i++)
                groupHosts[i]?.EnableInClassList("workspace-focused", i == focusedGroup);
            RefreshTitles();
            RefreshNativeInspector();
        }

        private void RefreshTitles()
        {
            foreach (var tab in tabButtons)
                tab.Button.text = tab.Editor.WorkspaceTitle + (tab.Editor.WorkspaceDirty ? " ●" : "");
            var editor = FocusedEditor;
            if (focusLabel != null)
                focusLabel.text = editor == null ? "当前无地图" : $"{(focusedGroup == 0 ? "左组" : "右组")} · {editor.WorkspaceTitle} · {editor.WorkspaceDocument.Width}×{editor.WorkspaceDocument.Height}";
            hasUnsavedChanges = Documents.Any(document => document.WorkspaceDirty);
            inspectorWindow?.RefreshDocuments();
            RefreshNativeTitles();
        }

        private void ShowInspector()
        {
            if (inspectorWindow == null)
            {
                inspectorWindow = CreateInstance<MapWorkspaceInspectorWindow>();
                inspectorWindow.Attach(this);
                inspectorWindow.Show();
                var main = EditorGUIUtility.GetMainWindowPosition();
                if (main.width >= 450 && main.height >= 430)
                {
                    var width = Mathf.Clamp(main.xMax - position.xMax - 12, 380, 520);
                    var x = Mathf.Min(position.xMax + 6, main.xMax - width - 6);
                    var y = Mathf.Clamp(position.y + 24, main.y + 40, main.yMax - 375);
                    inspectorWindow.position = new Rect(x, y, width, main.yMax - y - 12);
                }
            }
            inspectorWindow.Focus();
        }

        internal void DocumentChanged(MapEditorWindow editor)
        {
            RefreshTitles();
            if (nativeScenes)
            {
                for (var i = 0; i < nativeViews.Length; i++)
                    if (Group(i).Selected?.Editor == editor && nativeViews[i] != null) nativeViews[i]!.RefreshDocument();
                return;
            }
            for (var i = 0; i < viewports.Length; i++)
                if (Group(i).Selected?.Editor == editor)
                {
                    viewports[i]?.RefreshEditLock();
                    viewports[i]?.RefreshDocument();
                }
        }

        private void FocusGroup(int index)
        {
            if (focusedGroup == index) return;
            focusedGroup = index;
            RefreshFocus();
        }

        private void Select(int index, MapTab tab)
        {
            var group = Group(index);
            if (group.Selected != tab)
            {
                group.Active = group.Tabs.IndexOf(tab);
                BuildGroup(index);
            }
            focusedGroup = index;
            RefreshFocus();
        }

        internal void FocusDocument(MapEditorWindow editor)
        {
            SelectDocument(editor);
            Focus();
        }

        internal void SelectDocument(MapEditorWindow editor)
        {
            if (FocusedEditor == editor) return;
            if (split && Group(1 - focusedGroup).Selected?.Editor == editor)
            {
                FocusGroup(1 - focusedGroup);
                return;
            }
            for (var offset = 0; offset < (split ? 2 : 1); offset++)
            {
                var i = (focusedGroup + offset) % 2;
                var tab = Group(i).Tabs.Find(candidate => candidate.Editor == editor);
                if (tab == null) continue;
                Select(i, tab);
                return;
            }
        }

        private void AddBlank(int index, bool refresh = true) =>
            AddDocument(index, "未命名地图 " + nextUntitled++, LevelDocument.Create(10, 6).ToJson(), "", "", refresh);

        internal void ShowOfflineArtGalleryMap()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出 Play Mode 后打开分层展示地图。");
            var existing = Documents.FirstOrDefault(editor => editor.WorkspaceTitle == Catalog.OfflineArtGallery.Title);
            if (existing != null)
            {
                SelectDocument(existing);
                return;
            }
            AddDocument(focusedGroup, Catalog.OfflineArtGallery.Title, Catalog.OfflineArtGallery.CreateMap().ToJson());
        }

        internal void AddFinishedLevel(string title, string json, string origin, bool refresh = true)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出 Play Mode 后新建地图。");
            AddDocumentCore(focusedGroup, title, json, "", "", refresh, origin);
        }

        internal void AddDocument(int index, string title, string json, string path = "", string hash = "", bool refresh = true) =>
            AddDocumentCore(index, title, json, path, hash, refresh, "");

        private void AddDocumentCore(int index, string title, string json, string path, string hash, bool refresh, string finishedOrigin)
        {
            var editor = CreateInstance<MapEditorWindow>();
            editor.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                editor.LoadWorkspaceDocument(this, title, json, path, hash);
                editor.SetFinishedLevelOrigin(finishedOrigin);
            }
            catch { DestroyImmediate(editor); throw; }
            try { AddTab(index, new MapTab { Editor = editor }, refresh); }
            catch
            {
                if (!Documents.Contains(editor)) DestroyImmediate(editor);
                throw;
            }
        }

        private void AddTab(int index, MapTab tab, bool refresh = true)
        {
            var group = Group(index);
            var previousActive = group.Active;
            var previousFocus = focusedGroup;
            group.Tabs.Add(tab);
            group.Active = group.Tabs.Count - 1;
            focusedGroup = index;
            if (!refresh) return;
            if (nativeScenes)
            {
                try { BindNativeGroup(index, true); }
                catch
                {
                    group.Tabs.Remove(tab);
                    group.Active = previousActive;
                    focusedGroup = previousFocus;
                    throw;
                }
                ShowNativeTab();
            }
            else BuildGroup(index);
            RefreshFocus();
        }

        private void OpenFile()
        {
            var path = EditorUtility.OpenFilePanel("打开 LevelData", "", "json");
            if (path.Length == 0) return;
            if (MapEditorWindow.TryFocusOpenDocument(path)) return;
            var file = DocumentFileStore.Read(path);
            AddDocument(focusedGroup, Path.GetFileNameWithoutExtension(path), file.Text, path, file.Sha256);
        }

        private void ToggleSplit()
        {
            if (split)
            {
                foreach (var tab in right.Tabs)
                    if (!left.Tabs.Any(existing => existing.Editor == tab.Editor)) left.Tabs.Add(tab);
                if (focusedGroup == 1 && right.Selected != null)
                    left.Active = left.Tabs.FindIndex(tab => tab.Editor == right.Selected.Editor);
                right.Tabs.Clear();
                right.Active = 0;
                focusedGroup = 0;
            }
            else if (left.Selected != null)
            {
                right.Tabs.Add(new MapTab { Editor = left.Selected.Editor });
                right.Active = 0;
            }
            split = !split;
            CreateGUI();
        }

        private void ShowInOtherGroup()
        {
            if (FocusedEditor is not { } editor) return;
            var index = 1 - focusedGroup;
            var existing = Group(index).Tabs.Find(tab => tab.Editor == editor);
            if (existing != null) Select(index, existing);
            else AddTab(index, new MapTab { Editor = editor });
        }

        private void CloseTab(int index, MapTab tab)
        {
            var lastView = left.Tabs.Concat(right.Tabs).Count(candidate => candidate.Editor == tab.Editor) == 1;
            if (lastView && !tab.Editor.ConfirmWorkspaceClose()) return;
            var group = Group(index);
            var previous = group.Selected;
            var removed = group.Tabs.IndexOf(tab);
            group.Tabs.RemoveAt(removed);
            group.Active = previous == tab ? Mathf.Min(removed, group.Tabs.Count - 1) : group.Tabs.IndexOf(previous!);
            BuildGroup(index);
            if (lastView) DestroyImmediate(tab.Editor);
            RefreshFocus();
        }

        public override void SaveChanges()
        {
            foreach (var editor in Documents.ToArray())
            {
                if (!editor.WorkspaceDirty) continue;
                FocusDocument(editor);
                editor.SaveChanges();
                if (editor.WorkspaceDirty) return;
            }
            RefreshTitles();
            base.SaveChanges();
        }

        private void ReleaseViewports()
        {
            for (var i = 0; i < viewports.Length; i++)
            {
                viewports[i]?.Dispose();
                viewports[i] = null;
            }
        }

        private void OnDisable()
        {
            rootVisualElement.UnregisterCallback<KeyDownEvent>(HandleWorkspaceUndo, TrickleDown.TrickleDown);
            ReleaseViewports();
        }

        private void OnDestroy()
        {
            ReleaseViewports();
            CloseNativeGroup(0);
            CloseNativeGroup(1);
            if (inspectorWindow != null) inspectorWindow.Close();
            foreach (var editor in Documents.ToArray()) DestroyImmediate(editor);
        }
    }
}
