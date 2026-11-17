#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapWorkspaceWindow
    {
        [SerializeField] private bool nativeScenes;
        [SerializeField] private NativeMapSceneView?[] nativeViews = new NativeMapSceneView?[2];
        private VisualElement? nativeTabs;
        private VisualElement? nativeInspector;
        private VisualElement? nativeDocumentTools;
        private Button? nativeErase;
        private Button? nativeEditLock;
        private Button? nativeCameraLock;
        private Button? nativeHand;
        private MapEditorWindow? nativeDisplayedEditor;
        private readonly List<(MapEditorWindow Editor, Button Tab)> nativeTabButtons = new List<(MapEditorWindow, Button)>();
        internal bool UsesNativeScenes => nativeScenes;
        internal System.Func<NativeMapSceneView> CreateNativeView { get; set; } = NativeMapSceneDock.Create;

        private static Rect NativeInspectorBounds()
        {
            var main = EditorGUIUtility.GetMainWindowPosition();
            var width = Mathf.Clamp(main.width * .25f, 340, 500);
            return new Rect(main.xMax - width - 8, main.y + 70, width, Mathf.Max(400, main.height - 90));
        }

        private void CreateNativeGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.AddToClassList("map-workspace");
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/UI/Workbench.uss");
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            if (left.Tabs.Count == 0 && right.Tabs.Count == 0) AddBlank(0, false);

            var header = new VisualElement { name = "workspace-toolbox" };
            header.AddToClassList("workspace-toolbox");
            root.Add(header);
            var toolbar = ToolRow(header);
            toolbar.Add(Button("新建地图", () => AddBlank(focusedGroup)));
            toolbar.Add(Button("打开 JSON", OpenFile, operation: "document.open"));
            toolbar.Add(Button(split ? "单场景" : "双场景", ToggleSplit));
            if (split) toolbar.Add(Button("另一场景显示此图", ShowInOtherGroup));
            toolbar.Add(Button("显示场景", () => { BindNativeGroup(focusedGroup); nativeViews[focusedGroup]?.Focus(); }));

            var tabs = new ScrollView(ScrollViewMode.Horizontal) { name = "workspace-document-tabs" };
            tabs.AddToClassList("workspace-tabs");
            tabs.AddToClassList("workspace-document-strip");
            tabs.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            tabs.contentContainer.style.flexDirection = FlexDirection.Row;
            nativeTabs = tabs;
            nativeTabButtons.Clear();
            var controls = ToolRow(header);
            nativeErase = Button("置空", ToggleNativeErase, "开启后直接左键点击或拖动清空");
            nativeErase.name = "native-erase";
            nativeEditLock = Button("编辑锁", () => { if (FocusedEditor is { } doc) doc.SetWorkspaceEditLock(!doc.WorkspaceEditLocked); });
            nativeCameraLock = Button("相机锁", ToggleNativeCameraLock);
            controls.Add(nativeErase);
            controls.Add(nativeEditLock);
            controls.Add(nativeCameraLock);
            nativeHand = Button("显示手牌区", () => { if (FocusedEditor is { } doc) doc.SetPresetHandVisible(!doc.PresetHandVisible); });
            nativeHand.name = "native-preset-hand";
            controls.Add(nativeHand);
            nativeDocumentTools = ToolRow(header);

            root.Add(tabs);
            focusLabel = new Label { name = "workspace-active-document" };
            focusLabel.AddToClassList("workspace-tool-context");
            root.Add(focusLabel);
            nativeInspector = new VisualElement { name = "workspace-inspector" };
            nativeInspector.AddToClassList("workspace-split");
            nativeDisplayedEditor = null;
            root.Add(nativeInspector);
            status = new Label { name = "workspace-status" };
            status.AddToClassList("workspace-status");
            root.Add(status);

            BindNativeGroup(0);
            if (split) BindNativeGroup(1);
            else CloseNativeGroup(1);
            ShowNativeTab();
            RefreshFocus();
        }

        private static VisualElement ToolRow(VisualElement header)
        {
            var row = new VisualElement();
            row.AddToClassList("workspace-row");
            row.AddToClassList("workspace-tool-row");
            header.Add(row);
            return row;
        }

        private void ToggleNativeErase()
        {
            if (FocusedEditor is not { WorkspaceEditLocked: false } doc) return;
            doc.SetEraseMode(!doc.EraseMode);
            if (!doc.EraseMode) return;
            var index = focusedGroup;
            BindNativeGroup(index);
            nativeViews[index]?.Focus();
        }

        private void RefreshNativeTitles()
        {
            if (!nativeScenes || nativeTabs == null) return;
            var documents = Documents.ToArray();
            if (!documents.SequenceEqual(nativeTabButtons.Select(tab => tab.Editor)))
            {
                nativeTabs.Clear();
                nativeTabButtons.Clear();
                foreach (var editor in documents)
                {
                    var tab = Button("", () =>
                    {
                        SelectDocument(editor);
                        BindNativeGroup(focusedGroup);
                        ShowNativeTab();
                    });
                    nativeTabButtons.Add((editor, tab));
                    nativeTabs.Add(DocumentTab(tab, Button("×", () => CloseNativeDocument(editor), "关闭 " + editor.WorkspaceTitle)));
                }
            }
            foreach (var item in nativeTabButtons)
            {
                item.Tab.text = item.Editor.WorkspaceTitle + (item.Editor.WorkspaceDirty ? " ●" : "");
                item.Tab.parent.EnableInClassList("workspace-selected", item.Editor == FocusedEditor);
                item.Tab.tooltip = item.Editor.WorkspaceTitle;
                item.Tab.parent.Q<Button>(className: "workspace-document-close").tooltip = "关闭 " + item.Editor.WorkspaceTitle;
            }
            var selected = Group(focusedGroup).Selected;
            SetToolState(nativeErase, "置空", selected?.Editor.EraseMode == true, selected != null && !selected.Editor.WorkspaceEditLocked);
            SetToolState(nativeEditLock, "编辑锁", selected?.Editor.WorkspaceEditLocked == true, selected != null);
            SetToolState(nativeCameraLock, "相机锁", selected?.Camera.CameraLocked == true, selected != null);
            if (nativeHand != null)
            {
                nativeHand.SetEnabled(selected != null);
                nativeHand.text = selected?.Editor.PresetHandButtonText ?? "显示手牌区";
                nativeHand.EnableInClassList("workspace-selected", selected?.Editor.PresetHandVisible == true);
            }
            RefreshNativeInspector();
        }

        private static void SetToolState(Button? tool, string label, bool active, bool enabled)
        {
            if (tool == null) return;
            tool.SetEnabled(enabled);
            tool.EnableInClassList("workspace-selected", active);
            tool.text = active ? label + " ✓" : label;
        }

        private void RefreshNativeInspector()
        {
            if (!nativeScenes || nativeInspector == null || nativeDocumentTools == null) return;
            var editor = FocusedEditor;
            nativeInspector.SetEnabled(editor == null || !editor.WorkspaceEditLocked);
            nativeDocumentTools.SetEnabled(editor != null && !editor.WorkspaceEditLocked);
            if (editor == nativeDisplayedEditor) return;
            nativeInspector.Clear();
            nativeDocumentTools.Clear();
            if (editor != null)
            {
                editor.BuildDocumentTools(nativeDocumentTools);
                nativeInspector.Add(editor.WorkspaceInspector());
            }
            nativeDisplayedEditor = editor;
        }

        private void BindNativeGroup(int index, bool dockFloating = false)
        {
            var selected = Group(index).Selected;
            if (selected == null) { CloseNativeGroup(index); return; }
            var view = nativeViews[index];
            if (view == null || !view.IsDockable || dockFloating && !NativeMapSceneDock.IsInMainWindow(view))
            {
                view?.RememberPose();
                var replacement = CreateNativeView();
                try
                {
                    replacement.Attach(this, index);
                    replacement.Bind(selected.Editor, selected.Camera);
                }
                catch { replacement.CloseFromWorkspace(); throw; }
                nativeViews[index] = replacement;
                view?.CloseFromWorkspace();
                return;
            }
            view.Bind(selected.Editor, selected.Camera);
        }

        private void ShowNativeTab()
        {
            var view = nativeViews[focusedGroup];
            if (view != null && view.docked) view.ShowTab();
        }

        private void CloseNativeGroup(int index)
        {
            var view = nativeViews[index];
            nativeViews[index] = null;
            if (view != null) view.CloseFromWorkspace();
        }

        private void CloseNativeDocument(MapEditorWindow editor)
        {
            if (!editor.ConfirmWorkspaceClose()) return;
            for (var i = 0; i < 2; i++)
            {
                var group = Group(i);
                var selected = group.Selected;
                group.Tabs.RemoveAll(tab => tab.Editor == editor);
                group.Active = selected != null && selected.Editor != editor ? group.Tabs.IndexOf(selected)
                    : Mathf.Clamp(group.Active, 0, group.Tabs.Count - 1);
                if (i == 0 || split) BindNativeGroup(i);
            }
            if (FocusedEditor == editor || FocusedEditor == null)
                focusedGroup = Group(0).Selected != null || !split ? 0 : 1;
            DestroyImmediate(editor);
            RefreshFocus();
        }

        private void ToggleNativeCameraLock()
        {
            var tab = Group(focusedGroup).Selected;
            if (tab == null) return;
            nativeViews[focusedGroup]?.RememberPose();
            tab.Camera.CameraLocked = !tab.Camera.CameraLocked;
            RefreshTitles();
        }

        internal void ActivateNativeGroup(int index) => FocusGroup(index);
        internal void ReportNativeError(string operation, Exception error) =>
            Report(PluginLog.FailureMessage(operation, error));

        internal bool OwnsRouteInput(MapEditorWindow document, int index) =>
            focusedGroup == index && Group(index).Selected?.Editor == document;

        internal void FocusRouteScene(MapEditorWindow document, Vector2Int? cell = null)
        {
            if (!nativeScenes || FocusedEditor != document)
                throw new System.InvalidOperationException("请在当前地图的原生场景中编辑路线。");
            BindNativeGroup(focusedGroup);
            var view = nativeViews[focusedGroup];
            if (view == null) throw new System.InvalidOperationException("无法打开当前地图场景。");
            var state = Group(focusedGroup).Selected?.Camera;
            if (cell is Vector2Int point && state?.CameraLocked != true)
                view.LookAt(new Vector3(point.x + .5f, .6f, point.y + .5f), view.rotation, view.size, view.orthographic);
            ShowNativeTab();
            view.Focus();
        }

        internal void NativeViewClosed(int index, NativeMapSceneView view)
        {
            if (nativeViews[index] == view) nativeViews[index] = null;
        }
    }
}
