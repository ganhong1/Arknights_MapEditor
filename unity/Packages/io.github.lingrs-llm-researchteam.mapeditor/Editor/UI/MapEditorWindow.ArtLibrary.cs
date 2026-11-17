#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        [SerializeField] private string selectedArtKey = "";

        internal const string OfflineArtPackFolder = "Arknights.MapEditor.OfflineArtPackFolder";
        internal const string OfflineArtPackFolder2791 = "Arknights.MapEditor.OfflineArtPackFolder.2.7.91";
        internal static string ArtPackFolder => ResolveArtPackFolder(EditorPrefs.GetString(OfflineArtPackFolder, ""),
            Environment.GetEnvironmentVariable("ARK_OFFLINE_ART_PACK"));

        internal static string ResolveArtPackFolder(string saved, string? environment) =>
            string.IsNullOrWhiteSpace(saved) ? environment ?? "" : saved;

        internal static string CategoryName(OfflineArtCategory category) => category switch
        {
            OfflineArtCategory.Scene => "完整场景",
            OfflineArtCategory.Surface => "材质与贴图",
            OfflineArtCategory.Model => "背景与模型",
            OfflineArtCategory.Effect => "场景特效",
            OfflineArtCategory.Lighting => "光照与环境",
            OfflineArtCategory.Camera => "镜头与画面",
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };

        private Action BuildArtLibrary(VisualElement parent) => BuildArtLibrary(parent, () => ArtPackFolder,
            folder => EditorPrefs.SetString(OfflineArtPackFolder, folder),
            () => ResolveArtPackFolder(EditorPrefs.GetString(OfflineArtPackFolder2791, ""),
                Environment.GetEnvironmentVariable("ARK_OFFLINE_ART_PACK_2791")),
            folder => EditorPrefs.SetString(OfflineArtPackFolder2791, folder));

        internal Action BuildArtLibrary(VisualElement parent, Func<string> readFolder, Action<string> saveFolder,
            Func<string>? readUpdateFolder = null, Action<string>? saveUpdateFolder = null)
        {
            var controls = new VisualElement();
            controls.AddToClassList("workbench-tabs");
            var reconnect = new Button { text = "选择美术包目录", name = "offline-art-connect" };
            var gallery = new Button { text = "打开分层展示地图", name = "offline-art-gallery" };
            controls.Add(reconnect);
            controls.Add(gallery);
            parent.Add(controls);

            var state = new Label { name = "offline-art-status" };
            var category = new PopupField<string>("分类",
                new[] { "全部分类" }.Concat(Enum.GetValues(typeof(OfflineArtCategory))
                    .Cast<OfflineArtCategory>().Select(CategoryName)).ToList(), 0)
                { name = "offline-art-category" };
            var sourceType = new ResourceSubcategoryList(parent, "offline-art-type", "类型");
            sourceType.style.display = DisplayStyle.None;
            var query = new TextField("搜索") { name = "offline-art-query" };
            var total = new Label { name = "offline-art-count" };
            var empty = new HelpBox("尚未连接美术包，请先选择包含 catalog.json 和 Bundles 的目录。", HelpBoxMessageType.Info)
                { name = "offline-art-empty" };
            var list = new ListView { name = "offline-art-items", fixedItemHeight = 48, selectionType = SelectionType.None };
            list.style.flexGrow = 1;
            var matches = Array.Empty<OfflineArtEntry>();
            OfflineArtCatalog? baselinePack = null;
            OfflineArtCatalog? updatePack = null;
            var entries = new Dictionary<string, (OfflineArtEntry Entry, OfflineArtCatalog Pack)>(StringComparer.Ordinal);
            parent.Add(state);
            var filters = new VisualElement();
            filters.AddToClassList("resource-tree-frame");
            filters.AddToClassList("resource-branch-frame");
            category.AddToClassList("resource-filter-frame");
            filters.Add(category);
            filters.Add(sourceType);
            parent.Add(filters);
            parent.Add(query);
            parent.Add(total);
            parent.Add(empty);
            parent.Add(list);
            category.SetEnabled(false);
            sourceType.SetEnabled(false);
            query.SetEnabled(false);

            list.makeItem = () =>
            {
                var row = new Button { name = "offline-art-item" };
                row.style.alignItems = Align.FlexStart;
                row.style.justifyContent = Justify.Center;
                row.Add(new Label { name = "art-name" });
                row.Add(new Label { name = "art-key" });
                row.clicked += () =>
                {
                    if (!(row.userData is OfflineArtEntry entry) || !entries.TryGetValue(entry.Key, out var source)) return;
                    selectedArtKey = entry.Key;
                    list.RefreshItems();
                    OfflineArtPreviewWindow.ShowEntry(source.Pack, entry);
                };
                return row;
            };
            list.bindItem = (element, index) =>
            {
                var entry = matches[index];
                element.userData = entry;
                element.EnableInClassList("selected-tab", selectedArtKey == entry.Key);
                element.tooltip = entry.Key;
                element.Q<Label>("art-name").text = $"{entry.SourceType} · {Path.GetFileName(entry.UnityPath)}";
                element.Q<Label>("art-key").text = entry.Key;
                element.Q<Label>("art-key").style.opacity = 0.72f;
            };

            var searchInput = new CatalogSearchInput<OfflineArtEntry>(query,
                () => CatalogSearchRuntime.CreateWorker<OfflineArtEntry>(entry => new JObject
                {
                    ["name"] = Path.GetFileNameWithoutExtension(entry.UnityPath), ["targetFile"] = entry.Key,
                    ["category"] = entry.SourceType, ["description"] = entry.UnityPath + " " + entry.SearchTerms
                }),
                Array.Empty<OfflineArtEntry>(), () => total.text = "正在搜索…", (items, message) =>
                {
                    matches = items.ToArray();
                    list.itemsSource = matches;
                    list.RefreshItems();
                    total.text = message ?? $"{matches.Length:N0} 项";
                    empty.style.display = matches.Length == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                    empty.text = baselinePack == null && updatePack == null ? "尚未连接美术包，请先选择包含 catalog.json 和 Bundles 的目录。" :
                        entries.Count == 0 ? "美术包目录没有资源条目。" :
                        "当前分类、类型和搜索条件没有匹配资源。可清空搜索或选择全部分类。";
                });

            IReadOnlyList<OfflineArtEntry> categoryItems = Array.Empty<OfflineArtEntry>();
            void FilterTypes() => searchInput.SetItems(sourceType.Value == "全部" ? categoryItems :
                categoryItems.Where(entry => entry.SourceType == sourceType.Value).ToArray());
            void FilterCategory()
            {
                categoryItems = entries.Values.Select(source => source.Entry)
                    .Where(entry => category.index == 0 || (int)entry.Category == category.index - 1)
                    .OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
                var types = categoryItems.Select(entry => entry.SourceType).Distinct().ToList();
                sourceType.style.display = types.Count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
                types.Insert(0, "全部");
                sourceType.ShowChoices("类型", types, "全部");
                FilterTypes();
            }
            category.RegisterValueChangedCallback(_ => FilterCategory());
            sourceType.Changed += FilterTypes;

            void Connect(string location)
            {
                if (string.IsNullOrWhiteSpace(location)) return;
                try
                {
                    var next = OfflineArtCatalog.Open(location);
                    if (next.GameBaseline == OfflineArtCatalog.Baseline)
                    {
                        baselinePack = next;
                        saveFolder(next.Root);
                    }
                    else
                    {
                        updatePack = next;
                        saveUpdateFolder?.Invoke(next.Root);
                    }
                }
                catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException ||
                    error is ArgumentException || error is Newtonsoft.Json.JsonException)
                {
                    state.text = "无法连接美术包 · 日志 " + PluginLog.Failure("art-pack.connect", error);
                    state.tooltip = error.Message;
                    state.style.whiteSpace = WhiteSpace.Normal;
                }
            }

            void RefreshSources()
            {
                entries.Clear();
                var sources = new[] { baselinePack, updatePack }.OfType<OfflineArtCatalog>().ToArray();
                foreach (var source in sources)
                foreach (var entry in source.Entries)
                    entries[entry.Key] = (entry, source);
                var connected = sources.Length > 0;
                gallery.SetEnabled(baselinePack != null);
                category.SetEnabled(connected);
                sourceType.SetEnabled(connected);
                query.SetEnabled(connected);
                if (!state.text.StartsWith("无法连接", StringComparison.Ordinal))
                {
                    state.text = connected ? $"美术资源 · {entries.Count:N0} 项" : "选择包含 catalog.json 和 Bundles 的美术包目录。";
                    state.tooltip = string.Join("\n", sources.Select(source => source.Root));
                }
                state.style.whiteSpace = WhiteSpace.Normal;
                FilterCategory();
            }

            reconnect.clicked += () =>
            {
                var folder = EditorUtility.OpenFolderPanel("选择离线美术包", readFolder(), "");
                if (folder.Length == 0) return;
                state.text = "";
                Connect(folder);
                RefreshSources();
            };
            gallery.clicked += () =>
            {
                if (baselinePack != null) OfflineArtPreviewWindow.ShowGallery(baselinePack, workspaceOwner);
            };
            return () =>
            {
                if (baselinePack != null && (updatePack != null || readUpdateFolder == null)) return;
                state.text = "";
                if (baselinePack == null) Connect(readFolder());
                if (updatePack == null && readUpdateFolder != null) Connect(readUpdateFolder());
                RefreshSources();
            };
        }
    }
}
