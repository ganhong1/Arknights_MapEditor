#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class FinishedLevelBrowser : EditorWindow
    {
        private static readonly Vector2 PreviewSize = new Vector2(512, 300);
        [SerializeField] private MapWorkspaceWindow? workspace;
        [SerializeField] private MapEditorWindow? opener;
        private FinishedLevelCatalog? catalog;
        private MapWorkspaceViewport? viewport;
        private RenderTexture? frame;
        private FinishedLevelCatalog.Variant? loaded;
        private string? loadedJson;
        private int request;
        private int indexRequest;
        private readonly List<FinishedLevelCatalog.StageGroup> groups = new List<FinishedLevelCatalog.StageGroup>();
        private readonly List<FinishedLevelCatalog.Variant> variants = new List<FinishedLevelCatalog.Variant>();
        private ListView groupList = null!;
        private ListView variantList = null!;
        private Label result = null!;
        private Label details = null!;
        private Label message = null!;
        private Button create = null!;
        private IMGUIContainer canvas = null!;

        internal static FinishedLevelBrowser Open(MapEditorWindow owner)
        {
            var window = GetWindow<FinishedLevelBrowser>(true, "从成品关卡新建地图", true);
            window.minSize = new Vector2(680, 580);
            window.workspace = owner.WorkspaceOwner;
            window.opener = window.workspace == null ? owner : null;
            window.Focus();
            return window;
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.AddToClassList("workbench");
            var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/" + FinishedLevelCatalog.PackageName + "/Editor/UI/Workbench.uss");
            if (stylesheet != null && !root.styleSheets.Contains(stylesheet)) root.styleSheets.Add(stylesheet);
            var heading = new Label("从成品关卡新建地图");
            heading.AddToClassList("workbench-heading");
            root.Add(heading);
            var split = new TwoPaneSplitView(0, 245, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;
            root.Add(split);

            var left = new VisualElement();
            var query = new TextField("搜索关卡") { name = "finished-level-search",
                tooltip = "按关卡名称、拼音、首字母、编号或文件路径搜索" };
            left.Add(query);
            var componentToggle = new Toggle("查看玩法资源");
            left.Add(componentToggle);
            result = new Label();
            left.Add(result);
            groupList = new ListView(groups, 27, () => new Label(), (element, index) =>
            {
                ((Label)element).text = groups[index].Title;
                element.tooltip = groups[index].Title;
            }) { name = "finished-level-groups" };
            groupList.style.flexGrow = 1;
            left.Add(groupList);
            split.Add(left);

            var right = new VisualElement();
            right.style.paddingLeft = 10;
            split.Add(right);
            right.Add(new Label("模式与关卡配置"));
            variantList = new ListView(variants, 26, () => new Label(), (element, index) =>
            {
                var variant = variants[index];
                var matching = variants.Where(item => item.Label == variant.Label).ToList();
                var label = matching.Count > 1
                    ? variant.Label + " · 配置 " + (matching.IndexOf(variant) + 1)
                    : variant.Label;
                ((Label)element).text = label;
                element.tooltip = label;
            }) { name = "finished-level-variants" };
            variantList.style.minHeight = 95;
            variantList.style.maxHeight = 154;
            variantList.style.flexGrow = 1;
            right.Add(variantList);
            details = new Label { name = "finished-level-details" };
            details.style.whiteSpace = WhiteSpace.Normal;
            right.Add(details);
            canvas = new IMGUIContainer(DrawPreview) { name = "finished-level-preview" };
            canvas.style.minHeight = 180;
            canvas.style.flexGrow = 2;
            right.Add(canvas);
            message = new Label();
            message.style.whiteSpace = WhiteSpace.Normal;
            right.Add(message);
            var actions = new VisualElement();
            actions.AddToClassList("workbench-tabs");
            create = new Button(CreateMap) { name = "finished-level-create", text = "新建地图" };
            actions.Add(create);
            actions.Add(new Button(Close) { text = "取消" });
            right.Add(actions);

            var search = new CatalogSearchInput<FinishedLevelCatalog.StageGroup>(query,
                TerrainSearchRuntime.CreateFinishedLevelWorker,
                Array.Empty<FinishedLevelCatalog.StageGroup>(), () => result.text = "搜索中…",
                (matches, error) => ShowGroups(matches, error));
            void RefreshGroups() => search.SetItems(catalog?.Groups.Where(group => group.IsComponent == componentToggle.value).ToArray()
                ?? Array.Empty<FinishedLevelCatalog.StageGroup>());
            componentToggle.RegisterValueChangedCallback(_ => RefreshGroups());
            groupList.onSelectionChange += items => SelectGroup(items.OfType<FinishedLevelCatalog.StageGroup>().FirstOrDefault());
            variantList.onSelectionChange += items =>
            {
                if (items.OfType<FinishedLevelCatalog.Variant>().FirstOrDefault() is { } item) LoadVariant(item);
            };
            ClearPreview("选择关卡和配置以预览骨架。");
            LoadIndex(RefreshGroups);
        }

        private async void LoadIndex(Action refresh)
        {
            var generation = ++indexRequest;
            result.text = "正在读取关卡索引…";
            try
            {
                var path = FinishedLevelCatalog.BundledPath();
                var next = await Task.Run(() => FinishedLevelCatalog.Load(path));
                if (this == null || generation != indexRequest) return;
                catalog = next;
                refresh();
            }
            catch (Exception error)
            {
                if (this != null && generation == indexRequest) result.text = "关卡索引读取失败 · " + PluginLog.FailureMessage("catalog.level-index", error);
            }
        }

        private void ShowGroups(IReadOnlyList<FinishedLevelCatalog.StageGroup> matches, string? error)
        {
            var selected = groupList.selectedItem as FinishedLevelCatalog.StageGroup;
            groups.Clear();
            groups.AddRange(matches);
            groupList.SetSelectionWithoutNotify(Array.Empty<int>());
            groupList.RefreshItems();
            var index = selected == null ? -1 : groups.IndexOf(selected);
            if (index >= 0) groupList.SetSelectionWithoutNotify(new[] { index });
            else SelectGroup(null);
            result.text = error ?? (groups.Count == 0 ? "未找到匹配关卡" : $"{groups.Count:N0} 项");
        }

        private void SelectGroup(FinishedLevelCatalog.StageGroup? group)
        {
            ClearPreview("请选择要预览的模式和配置。");
            variants.Clear();
            if (group != null) variants.AddRange(group.GameVariants());
            variantList.SetSelectionWithoutNotify(Array.Empty<int>());
            variantList.RefreshItems();
            if (variants.Count > 0) variantList.SetSelection(0);
        }

        private async void LoadVariant(FinishedLevelCatalog.Variant variant)
        {
            if (catalog == null) return;
            ClearPreview("正在读取 LevelData…");
            var generation = request;
            var library = catalog;
            details.text = variant.Label;
            details.tooltip = variant.Stage?.Id ?? GameStageLabel.LevelId(variant.Source.Path);
            try
            {
                var data = await Task.Run(() =>
                {
                    var document = library.ReadVariant(variant);
                    return (json: document.ToJson(), document);
                });
                if (this == null || generation != request) return;
                viewport ??= new MapWorkspaceViewport();
                frame = viewport.Render(data.document, new MapViewportState(), PreviewSize);
                loaded = variant;
                loadedJson = data.json;
                canvas.MarkDirtyRepaint();
                message.text = $"{data.document.Width} × {data.document.Height} · 骨架预览";
                create.SetEnabled(true);
            }
            catch (Exception error)
            {
                if (this != null && generation == request) ClearPreview("无法预览 · " + PluginLog.FailureMessage("catalog.level-preview", error), false);
            }
        }

        private void ClearPreview(string status, bool clearDetails = true)
        {
            ++request;
            frame = null;
            loaded = null;
            loadedJson = null;
            if (clearDetails) details.text = "";
            message.text = status;
            create.SetEnabled(false);
            canvas.MarkDirtyRepaint();
        }

        private void DrawPreview()
        {
            if (frame == null || viewport == null || Event.current.type != EventType.Repaint) return;
            var bounds = canvas.contentRect;
            var factor = Mathf.Min(bounds.width / frame.width, bounds.height / frame.height);
            var size = new Vector2(frame.width * factor, frame.height * factor);
            if (size.x <= 0 || size.y <= 0) return;
            var rect = new Rect((bounds.size - size) * .5f, size);
            GUI.DrawTexture(rect, frame);
            Handles.BeginGUI();
            try { viewport.DrawRoutes(rect); }
            finally { Handles.EndGUI(); }
        }

        private void CreateMap()
        {
            if (loaded == null || loadedJson == null) return;
            if (workspace == null && opener == null)
            {
                message.text = "原编辑窗口已关闭。请从当前工作台重新打开成品关卡地图。";
                return;
            }
            try
            {
                if (workspace != null)
                    workspace.AddFinishedLevel(loaded.NewDocumentTitle, loadedJson, loaded.Origin);
                else
                    opener!.CreateFromFinishedLevel(loaded.NewDocumentTitle, loadedJson, loaded.Origin);
                Close();
            }
            catch (Exception error) { message.text = "新建失败 · " + PluginLog.FailureMessage("catalog.level-create", error); }
        }

        private void OnDisable()
        {
            ++request;
            ++indexRequest;
            viewport?.Dispose();
            viewport = null;
            frame = null;
        }
    }
}
