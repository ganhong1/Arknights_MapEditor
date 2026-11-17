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
        private const int MusicResourceCategory = 5;
        private const int SoundResourceCategory = 6;
        private MusicResourceLibrary? musicResourceLibrary;

        private void OpenMusicResourceLibrary(MusicEventPicker target)
        {
            if (session == null || musicResourceLibrary == null) return;
            var ownerSession = session;
            var revision = session.Revision;
            var module = workbenchTab;
            var panel = target.panel;
            var category = target.Environment ? SoundResourceCategory : MusicResourceCategory;
            openResourceCategory?.Invoke(category);
            musicResourceLibrary.Bind(target, () => ReferenceEquals(session, ownerSession) &&
                session?.Revision == revision && workbenchTab == module && resourceDrawerOpen &&
                resourceCategory == category && target.parent != null &&
                (workspaceOwner == null || workspaceOwner.FocusedEditor == this) &&
                (panel == null || ReferenceEquals(target.panel, panel)));
        }

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
                (7, "分支", BuildBranchesPanel),
                (8, "敌人定义", panel => BuildEnemyPanelSafely(panel, "editor.enemy-definitions", () => BuildEnemiesPanel(panel))),
                (9, "符文与增益", p => BuildFieldPlaceholder(p, "符文与增益", "三个字段分别保留，不合并数据。", "runes", "optionalRunes", "globalBuffs")),
                (3, "美术", BuildProjectPanel),
                (4, "高级", BuildSectionPanel),
                (11, "背景音乐", BuildMusicPanel),
                (12, "环境音效 · environmentSe", p => BuildGlobalField(p, "environmentSe", "环境音效")),
            };
            var tabsById = new Dictionary<int, (Button Button, VisualElement Page)>();
            void Select(int id)
            {
                if (id != 1 && RouteSceneActive) ExitRouteScene();
                if (id != 6 && MapRuleSceneField.Length > 0) ExitMapRuleScene();
                if (id != workbenchTab) musicResourceLibrary?.Deactivate();
                workbenchTab = id;
                if (id == 6) openMapDataPanel?.Invoke();
                if (id == 5) openPresetPanel?.Invoke();
                if (id == 2 && session != null) wavesPanel?.Refresh(true);
                if (id == 7 && session != null) branchesPanel?.Refresh(true);
                if (id == 11) RefreshMusic();
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
                if (!open) musicResourceLibrary?.Deactivate();
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
            tabs.AddToClassList("resource-category-tabs");
            var categoryStrip = new ScrollView(ScrollViewMode.Horizontal) { name = "resource-category-strip" };
            categoryStrip.AddToClassList("resource-category-frame");
            categoryStrip.style.flexGrow = 0;
            categoryStrip.style.flexShrink = 0;
            categoryStrip.Add(tabs);
            drawer.Add(categoryStrip);
            drawer.RegisterCallback<GeometryChangedEvent>(change =>
            {
                var width = change.newRect.width;
                if (width <= 0) return;
                var compact = width < 260 || change.newRect.height < 260;
                categoryStrip.style.height = compact ? new StyleLength(52) : new StyleLength(StyleKeyword.Auto);
                tabs.style.flexWrap = compact ? Wrap.NoWrap : Wrap.Wrap;
                tabs.style.width = compact ? StyleKeyword.Auto : new StyleLength(Mathf.Max(0, width - 22));
            });
            var categories = new[] { "地形", "装置", "敌人", "美术", "预设干员", "音乐", "音效" };
            var buttons = new Button[categories.Length];
            var panels = new VisualElement[categories.Length];
            Action? activateArt = null;
            void Select(int index)
            {
                if (index is MusicResourceCategory or SoundResourceCategory)
                    musicResourceLibrary?.ShowKind(index == SoundResourceCategory);
                else musicResourceLibrary?.Deactivate();
                resourceCategory = index;
                if (index == 3) activateArt?.Invoke();
                for (var i = 0; i < panels.Length; i++)
                {
                    if (i != SoundResourceCategory)
                        panels[i].style.display = ReferenceEquals(panels[i], panels[index]) ? DisplayStyle.Flex : DisplayStyle.None;
                    buttons[i].EnableInClassList("selected-tab", i == index);
                }
            }
            for (var i = 0; i < categories.Length; i++)
            {
                var index = i;
                buttons[i] = new Button(() => Select(index)) { name = "resource-category-" + i, text = categories[i] };
                tabs.Add(buttons[i]);
                if (i == SoundResourceCategory)
                {
                    panels[i] = panels[MusicResourceCategory];
                    continue;
                }
                panels[i] = new VisualElement { name = "resource-panel-" + i };
                panels[i].AddToClassList("workbench-resource-panel");
                if (i == 0) BuildCatalogPanel(panels[i], body);
                else if (i == 1) BuildDeviceLibrary(panels[i], body);
                else if (i == 4) BuildOperatorLibrary(panels[i], body);
                else if (i == 2) BuildEnemyPanelSafely(panels[i], "catalog.enemy-library", () => BuildEnemyLibrary(panels[i], body));
                else if (i == MusicResourceCategory)
                {
                    musicResourceLibrary = new MusicResourceLibrary(body);
                    panels[i].Add(musicResourceLibrary);
                }
                else activateArt = BuildArtLibrary(panels[i]);
                drawer.Add(panels[i]);
            }
            var audioPack = new Button { name = "music-event-audio-root", text = "试听包", tooltip = "选择音乐或音效试听包" };
            audioPack.clicked += () =>
            {
                var menu = new GenericDropdownMenu();
                menu.AddItem("音乐试听包", false, () => ChooseAudioPackage(false));
                menu.AddItem("音效试听包", false, () => ChooseAudioPackage(true));
                menu.DropDown(audioPack.worldBound, audioPack, false);
            };
            tabs.Add(audioPack);
            tabs.Add(new Button(() => FinishedLevelBrowser.Open(this))
            {
                name = "resource-finished-levels",
                text = "成品关卡地图"
            });
            Select(Mathf.Clamp(resourceCategory, 0, categories.Length - 1));
            SetOpen(resourceDrawerOpen);
            openResourceCategory = index => { SetOpen(true); Select(index); };
        }

        private void ChooseAudioPackage(bool effects)
        {
            var title = effects ? "音效试听包" : "音乐试听包";
            try
            {
                var root = EditorUtility.OpenFolderPanel("选择" + title, MusicPreview.AudioRoot(effects) ?? "", "");
                if (root.Length == 0) return;
                if (!MusicPreview.SetAudioRoot(root, effects))
                    throw new InvalidOperationException("此目录缺少试听包清单。");
                if (status != null) status.text = "已设置" + title;
            }
            catch (Exception error) { ReportWindowFailure("audio.package", error); }
        }

    }
}
