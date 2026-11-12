#nullable enable
using System;
using System.Collections.Generic;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        [SerializeField] private int workbenchTab;
        [SerializeField] private bool editorDrawerOpen;
        [SerializeField] private bool moduleNavigationOpen = true;
        private Action<bool>? setEditorDrawer;
        [SerializeField] private bool resourceDrawerOpen;
        [SerializeField] private int resourceCategory;
        private Action<int>? selectWorkbenchTab;
        private Action<int>? openResourceCategory;

        [MenuItem("Tools/Arknights/Open Workbench Demo")]
        public static void OpenWorkbenchDemo()
        {
            MapWorkspaceWindow.OpenWorkspace();
        }

        private void BuildWorkbench(VisualElement parent)
        {
            var header = new VisualElement();
            header.AddToClassList("workbench-heading-row");
            var heading = new Label("关卡编辑器");
            heading.AddToClassList("workbench-heading");
            header.Add(heading);
            parent.Add(header);

            var body = new VisualElement { name = "workbench-body" };
            body.AddToClassList("workbench-body");
            var editor = BuildWorkbenchTabs();
            editor.name = "editor-drawer";
            editor.AddToClassList("workbench-editor-drawer");
            body.Add(editor);
            var leftSplitter = BuildPanelSplitter(body, editor, true);
            body.Add(leftSplitter);
            body.Add(BuildDocumentView());
            var toggle = new Button { name = "editor-drawer-toggle" };
            setEditorDrawer = open =>
            {
                editorDrawerOpen = open;
                editor.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                leftSplitter.style.display = editor.style.display;
                resizeWorkbenchPanels?.Invoke();
                toggle.text = open ? "◂ 收起编辑" : "编辑 ▸";
                toggle.EnableInClassList("selected-tab", open);
            };
            toggle.clicked += () => setEditorDrawer(!editorDrawerOpen);
            header.Insert(0, toggle);
            setEditorDrawer(editorDrawerOpen);
            BuildResourceDrawer(body, header);
            body.RegisterCallback<GeometryChangedEvent>(_ => resizeWorkbenchPanels?.Invoke());
            parent.Add(body);
        }

        private VisualElement BuildWorkbenchTabs()
        {
            var detail = new VisualElement();
            detail.AddToClassList("workbench-content");
            var navigation = new Foldout { name = "workbench-module-drawer", text = "编辑模块", value = moduleNavigationOpen };
            navigation.AddToClassList("workbench-module-drawer");
            navigation.RegisterValueChangedCallback(e =>
            {
                if (e.target == navigation) moduleNavigationOpen = e.newValue;
            });
            detail.Add(navigation);
            var navigationScroll = new ScrollView(ScrollViewMode.Vertical) { name = "workbench-module-navigation" };
            navigationScroll.AddToClassList("workbench-module-navigation");
            navigation.Add(navigationScroll);
            var tabs = new VisualElement(); tabs.AddToClassList("workbench-tabs"); navigationScroll.Add(tabs);
            tabs.style.flexDirection = FlexDirection.Column;
            var mapTabs = new VisualElement(); mapTabs.AddToClassList("workbench-tabs");
            var battleTabs = new VisualElement(); battleTabs.AddToClassList("workbench-tabs");
            var audioTabs = new VisualElement(); audioTabs.AddToClassList("workbench-tabs");
            var otherTabs = new VisualElement(); otherTabs.AddToClassList("workbench-tabs");
            tabs.Add(new Label("地图与配置")); tabs.Add(mapTabs);
            tabs.Add(new Label("战斗逻辑")); tabs.Add(battleTabs);
            tabs.Add(new Label("音乐音效")); tabs.Add(audioTabs);
            tabs.Add(otherTabs);
            var pages = new VisualElement(); pages.AddToClassList("workbench-pages"); detail.Add(pages);
            // Existing serialized tab IDs remain valid across domain reloads.
            var definitions = new (int Id, string Title, Action<VisualElement> Build)[]
            {
                (10, "全局设置", BuildGlobalSettings),
                (6, "地图数据 · mapData", BuildMapDataPanel),
                (1, "路线", BuildRoutesPanel),
                (5, "关卡预设", BuildPredefinedPanel),
                (2, "波次", BuildSchedulePanel),
                (7, "分支", p => BuildFieldPlaceholder(p, "分支", "管理分支动作序列。", "branches")),
                (8, "敌人定义", p => BuildFieldPlaceholder(p, "敌人定义", "管理本关敌人引用与覆盖参数；出怪时机由波次／分支负责。", "enemyDbRefs")),
                (9, "符文与增益", p => BuildFieldPlaceholder(p, "符文与增益", "三个字段分别保留，不合并数据。", "runes", "optionalRunes", "globalBuffs")),
                (3, "美术", BuildProjectPanel),
                (4, "高级", BuildSectionPanel),
                (11, "背景音乐 · bgmEvent", p => BuildGlobalField(p, "bgmEvent", "背景音乐事件")),
                (12, "环境音效 · environmentSe", p => BuildGlobalField(p, "environmentSe", "环境音效")),
            };
            var tabsById = new Dictionary<int, (Button Button, VisualElement Page)>();
            void Select(int id)
            {
                workbenchTab = id;
                if (id == 6) openMapDataPanel?.Invoke();
                if (id == 5) openPresetPanel?.Invoke();
                foreach (var tab in tabsById)
                {
                    var selected = tab.Key == id;
                    tab.Value.Page.style.display = selected ? DisplayStyle.Flex : DisplayStyle.None;
                    tab.Value.Button.EnableInClassList("selected-tab", selected);
                    if (selected) navigation.text = "编辑模块 · " + tab.Value.Button.text;
                }
            }

            foreach (var definition in definitions)
            {
                var button = new Button(() => Select(definition.Id))
                {
                    name = definition.Id == 10 ? "global-settings" : "workbench-tab-" + definition.Id,
                    text = definition.Title
                };
                var page = new ScrollView { name = "workbench-page-" + definition.Id };
                page.AddToClassList("workbench-detail");
                definition.Build(page);
                tabsById.Add(definition.Id, (button, page));
                var tabGroup = definition.Id == 10 || definition.Id == 6 || definition.Id == 1 || definition.Id == 5 ? mapTabs :
                    definition.Id == 2 || definition.Id == 7 || definition.Id == 8 || definition.Id == 9 ? battleTabs :
                    definition.Id == 11 || definition.Id == 12 ? audioTabs : otherTabs;
                tabGroup.Add(button);
                pages.Add(page);
            }
            selectWorkbenchTab = id => { setEditorDrawer?.Invoke(true); Select(id); };
            Select(tabsById.ContainsKey(workbenchTab) ? workbenchTab : 6);
            return detail;
        }

        private void BuildResourceDrawer(VisualElement body, VisualElement header)
        {
            var drawer = new VisualElement { name = "resource-drawer" };
            drawer.AddToClassList("workbench-library");
            var rightSplitter = BuildPanelSplitter(body, drawer, false);
            body.Add(rightSplitter);
            body.Add(drawer);
            var toggle = new Button { name = "resource-drawer-toggle", tooltip = "展开或收起右侧资源库" };
            void SetOpen(bool open)
            {
                resourceDrawerOpen = open;
                drawer.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                rightSplitter.style.display = drawer.style.display;
                resizeWorkbenchPanels?.Invoke();
                toggle.text = open ? "收起资源库 ▸" : "◂ 资源库";
                toggle.EnableInClassList("selected-tab", open);
            }
            toggle.clicked += () => SetOpen(!resourceDrawerOpen);
            header.Add(toggle);

            var tabs = new VisualElement();
            tabs.AddToClassList("workbench-tabs");
            drawer.Add(tabs);
            var categories = new[] { "地形", "装置", "敌人", "美术", "预设干员" };
            var buttons = new Button[categories.Length];
            var panels = new VisualElement[categories.Length];
            void Select(int index)
            {
                resourceCategory = index;
                for (var i = 0; i < panels.Length; i++)
                {
                    panels[i].style.display = i == index ? DisplayStyle.Flex : DisplayStyle.None;
                    buttons[i].EnableInClassList("selected-tab", i == index);
                }
            }
            for (var i = 0; i < categories.Length; i++)
            {
                var index = i;
                buttons[i] = new Button(() => Select(index)) { name = "resource-category-" + i, text = categories[i] };
                panels[i] = new VisualElement { name = "resource-panel-" + i };
                panels[i].AddToClassList("workbench-resource-panel");
                if (i == 0) BuildCatalogPanel(panels[i], body);
                else if (i == 1) BuildDeviceLibrary(panels[i], body);
                else if (i == 4) BuildOperatorLibrary(panels[i], body);
                else panels[i].Add(new HelpBox(categories[i] + "资源尚未接入。", HelpBoxMessageType.Info));
                tabs.Add(buttons[i]);
                drawer.Add(panels[i]);
            }
            tabs.Add(new Button(() => FinishedLevelBrowser.Open(this))
            {
                name = "resource-finished-levels",
                text = "成品关卡地图"
            });
            Select(Mathf.Clamp(resourceCategory, 0, categories.Length - 1));
            SetOpen(resourceDrawerOpen);
            openResourceCategory = index => { SetOpen(true); Select(index); };
        }

    }
}
