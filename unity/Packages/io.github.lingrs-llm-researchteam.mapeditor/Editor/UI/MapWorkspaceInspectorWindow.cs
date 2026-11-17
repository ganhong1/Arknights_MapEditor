#nullable enable
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed class MapWorkspaceInspectorWindow : EditorWindow
    {
        [SerializeField] private MapWorkspaceWindow? workspace;
        private readonly List<(MapEditorWindow Editor, Button Button)> tabs = new List<(MapEditorWindow, Button)>();
        private ScrollView? tabBar;
        private VisualElement? inspectorHost;
        private MapEditorWindow? displayedEditor;

        private void OnEnable() =>
            rootVisualElement.RegisterCallback<KeyDownEvent>(HandleWorkspaceUndo, TrickleDown.TrickleDown);

        private void OnDisable() =>
            rootVisualElement.UnregisterCallback<KeyDownEvent>(HandleWorkspaceUndo, TrickleDown.TrickleDown);

        private void HandleWorkspaceUndo(KeyDownEvent input) => MapEditorWindow.HandleUndoKey(input, workspace?.FocusedEditor);

        internal void Attach(MapWorkspaceWindow owner)
        {
            workspace = owner;
            titleContent = new GUIContent("关卡工作台");
            minSize = new Vector2(330, 360);
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.AddToClassList("map-workspace");
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/UI/Workbench.uss");
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);

            tabs.Clear();
            displayedEditor = null;
            tabBar = new ScrollView(ScrollViewMode.Horizontal) { name = "workspace-document-tabs" };
            tabBar.AddToClassList("workspace-tabs");
            tabBar.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            tabBar.contentContainer.style.flexDirection = FlexDirection.Row;
            root.Add(tabBar);
            inspectorHost = new VisualElement { name = "workspace-inspector" };
            inspectorHost.AddToClassList("workspace-split");
            root.Add(inspectorHost);
            RefreshDocuments();
        }

        internal void RefreshDocuments()
        {
            if (workspace == null || tabBar == null || inspectorHost == null) return;
            var documents = workspace.Documents.ToArray();
            if (!documents.SequenceEqual(tabs.Select(tab => tab.Editor)))
            {
                tabBar.Clear();
                tabs.Clear();
                foreach (var editor in documents)
                {
                    var button = new Button(() => workspace.SelectDocument(editor));
                    tabs.Add((editor, button));
                    tabBar.Add(button);
                }
            }
            var selected = workspace.FocusedEditor;
            foreach (var tab in tabs)
            {
                tab.Button.text = tab.Editor.WorkspaceTitle + (tab.Editor.WorkspaceDirty ? " ●" : "");
                tab.Button.EnableInClassList("workspace-selected", tab.Editor == selected);
            }
            inspectorHost.SetEnabled(selected == null || !selected.WorkspaceEditLocked);
            if (selected == displayedEditor) return;
            displayedEditor = selected;
            inspectorHost.Clear();
            if (selected != null) inspectorHost.Add(selected.WorkspaceInspector());
        }
    }
}
