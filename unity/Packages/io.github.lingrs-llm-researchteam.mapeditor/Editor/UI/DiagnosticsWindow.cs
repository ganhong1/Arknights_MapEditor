#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    /// <summary>Local diagnostics browser. It never edits a map or its recovery drafts.</summary>
    internal sealed class DiagnosticsWindow : EditorWindow
    {
        [SerializeField] private string selectedId = "";
        [SerializeField] private string query = "";
        [SerializeField] private string selectedModule = "全部";
        [SerializeField] private string selectedLevel = "全部";
        [SerializeField] private bool automatic = true;
        private IReadOnlyList<DiagnosticEvent> events = Array.Empty<DiagnosticEvent>();
        private readonly List<DiagnosticEvent> visible = new();
        private string signature = "";
        private string lastSearchError = "";
        private ListView list = null!;
        private TextField detail = null!;
        private Label status = null!, counts = null!, message = null!, verbose = null!;
        private PopupField<string> modules = null!, levels = null!;
        private CatalogSearchInput<DiagnosticEvent>? search;
        private IVisualElementScheduledItem? poll;

        [MenuItem("Tools/Arknights/Diagnostics/Diagnostics Window")]
        internal static void Open()
        {
            var window = GetWindow<DiagnosticsWindow>("插件诊断");
            window.minSize = new Vector2(580, 420);
            window.Show();
        }

        public void CreateGUI()
        {
            poll?.Pause();
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = root.style.paddingRight = 10;
            root.style.paddingTop = root.style.paddingBottom = 8;
            root.style.unityFontStyleAndWeight = FontStyle.Normal;
            var toolbar = Row(root);
            Button(toolbar, "刷新", "diagnostics-refresh", () => RefreshLive(true));
            Button(toolbar, "打开日志目录", "diagnostics-open", OpenDirectory);
            Button(toolbar, "导出日志", "diagnostics-export", () => Export(false));
            Button(toolbar, "导出诊断包", "diagnostics-bundle", () => Export(true));
            var auto = new Toggle("自动刷新") { value = automatic, name = "diagnostics-auto" };
            auto.RegisterValueChangedCallback(evt => automatic = evt.newValue);
            toolbar.Add(auto);
            status = Note(root, "", "diagnostics-health");
            message = Note(root, "", "diagnostics-message");
            BuildSettings(root);
            var filters = Card(root);
            var input = new TextField("搜索事件") { value = query, name = "diagnostics-search" };
            input.tooltip = "操作、错误码、事件编号、文档编号或异常类型";
            input.RegisterValueChangedCallback(evt => query = evt.newValue);
            filters.Add(input);
            var choices = Row(filters);
            modules = new PopupField<string>("模块", new List<string> { selectedModule }, 0) { name = "diagnostics-module" };
            levels = new PopupField<string>("级别", new List<string> { "全部", "debug", "info", "warning", "error" }, selectedLevel)
                { name = "diagnostics-level" };
            choices.Add(modules); choices.Add(levels);
            modules.RegisterValueChangedCallback(evt => { selectedModule = evt.newValue; Filter(); });
            levels.RegisterValueChangedCallback(evt => { selectedLevel = evt.newValue; Filter(); });
            counts = Note(filters, "", "diagnostics-count");
            var split = new TwoPaneSplitView(0, 280, TwoPaneSplitViewOrientation.Vertical) { name = "diagnostics-split" };
            split.style.flexGrow = 1;
            root.Add(split);
            list = new ListView(visible, 52, MakeRow, BindRow) { name = "diagnostics-events", selectionType = SelectionType.Single };
            list.style.flexGrow = 1;
            split.Add(list);
            var detailArea = Card(split);
            detailArea.style.flexGrow = 1;
            Note(detailArea, "事件详情", "diagnostics-detail-title");
            detail = new TextField { multiline = true, isReadOnly = true, name = "diagnostics-detail" };
            var detailScroll = new ScrollView(); detailScroll.style.flexGrow = 1; detailScroll.Add(detail); detailArea.Add(detailScroll);
            Button(detailArea, "复制事件 JSON", "diagnostics-copy", () => EditorGUIUtility.systemCopyBuffer = detail.value);
            list.onSelectionChange += selection =>
            {
                var item = selection.OfType<DiagnosticEvent>().FirstOrDefault();
                if (item == null) return;
                selectedId = item.Id;
                ShowDetail(item);
            };
            search = new CatalogSearchInput<DiagnosticEvent>(input,
                () => CatalogSearchRuntime.CreateWorker<DiagnosticEvent>(Describe), Array.Empty<DiagnosticEvent>(),
                () => counts.text = "搜索中…", (matches, error) =>
                {
                    ApplyResults(matches);
                    if (error != null)
                    {
                        // Stop automatic re-search on a failing search provider; avoid generating an event loop.
                        automatic = false;
                        root.Q<Toggle>("diagnostics-auto").SetValueWithoutNotify(false);
                        lastSearchError = error + " 自动刷新已暂停。";
                        message.text = lastSearchError;
                    }
                    else if (message.text == lastSearchError)
                    {
                        message.text = "";
                        lastSearchError = "";
                    }
                });
            RefreshLive(true);
            poll = root.schedule.Execute(() => { if (automatic) RefreshLive(false); }).Every(1000);
        }

        private void OnDisable() => poll?.Pause();

        private void BuildSettings(VisualElement root)
        {
            var fold = new Foldout { text = "日志设置", value = false, name = "diagnostics-settings" };
            root.Add(fold);
            var panel = Card(fold);
            var current = PluginLog.Settings;
            var directory = new TextField("日志目录") { value = current.DirectoryPath, name = "diagnostics-directory" };
            var size = new DoubleField("单文件上限 MiB") { value = current.MaximumBytes / 1048576d, name = "diagnostics-size" };
            var files = new IntegerField("保留文件数") { value = current.RetainedFiles, name = "diagnostics-files" };
            var days = new IntegerField("保留天数") { value = current.RetentionDays, name = "diagnostics-days" };
            panel.Add(directory); panel.Add(size); panel.Add(files); panel.Add(days);
            Button(panel, "应用日志设置", "diagnostics-configure", () =>
            {
                if (double.IsNaN(size.value) || double.IsInfinity(size.value) || size.value <= 0 || size.value > 100)
                    throw new ArgumentException("单文件大小需要大于 0 且不超过 100 MiB。");
                PluginLog.Configure(new DiagnosticSettings { DirectoryPath = directory.value,
                    MaximumBytes = checked((long)(size.value * 1048576d)), RetainedFiles = files.value, RetentionDays = days.value });
                message.text = "日志设置已应用。";
                RefreshLive(true);
            });
            var tools = Row(panel);
            Button(tools, "详细日志 15 分钟", "diagnostics-verbose", () => { PluginLog.SetVerbose(TimeSpan.FromMinutes(15)); RefreshHealth(); });
            Button(tools, "结束详细日志", "diagnostics-quiet", () => { PluginLog.SetVerbose(TimeSpan.Zero); RefreshHealth(); });
            verbose = Note(panel, "", "diagnostics-verbose-state");
        }

        private void RefreshLive(bool force)
        {
            try
            {
                RefreshHealth();
                RefreshFrom(PluginLog.Recent(), force);
            }
            catch (Exception error)
            {
                // The diagnostics UI is the final reporting boundary; logging this error would recurse.
                message.text = "读取诊断状态失败：" + error.GetType().Name;
            }
        }

        private void RefreshHealth()
        {
            var health = PluginLog.Health;
            status.text = (health.IsHealthy ? "日志写入正常" : "日志写入异常") +
                $" · 队列 {health.Pending} · 丢弃 {health.Dropped} · 写入失败 {health.WriteFailures}" +
                (health.LastWriteError.Length > 0 ? " · " + health.LastWriteError : "");
            verbose.text = PluginLog.VerboseUntil > DateTimeOffset.UtcNow
                ? "详细日志截至 " + PluginLog.VerboseUntil.ToLocalTime().ToString("HH:mm:ss") : "普通日志";
        }

        internal void RefreshFrom(IReadOnlyList<DiagnosticEvent> records, bool force = false)
        {
            var next = string.Join("|", records.Select(item => item.Id + ":" + item.Count + ":" + item.LastUtc.Ticks));
            if (!force && signature == next) return;
            signature = next;
            events = records.OrderByDescending(item => item.LastUtc).ToArray();
            var available = new[] { "全部" }.Concat(events.Select(Module).Distinct().OrderBy(item => item)).ToList();
            if (!available.Contains(selectedModule)) available.Add(selectedModule);
            modules.choices = available;
            modules.SetValueWithoutNotify(selectedModule);
            Filter();
        }

        private void Filter()
        {
            var matches = events.Where(item => (selectedModule == "全部" || Module(item) == selectedModule) &&
                (selectedLevel == "全部" || item.Level == selectedLevel)).ToArray();
            search?.SetItems(matches);
        }

        private void ApplyResults(IReadOnlyList<DiagnosticEvent> matches)
        {
            visible.Clear(); visible.AddRange(matches);
            list.Rebuild();
            var index = visible.FindIndex(item => item.Id == selectedId);
            list.SetSelectionWithoutNotify(index < 0 ? Array.Empty<int>() : new[] { index });
            ShowDetail(index < 0 ? null : visible[index]);
            counts.text = $"{visible.Count} / {events.Count} 组事件 · 当前结果累计 {visible.Sum(item => (long)item.Count)} 次";
        }

        private static JObject Describe(DiagnosticEvent item) => new()
        {
            ["name"] = item.Operation,
            ["targetFile"] = new JArray(new[] { item.Id, item.Code, item.OperationId }
                .Concat(item.RelatedEventIds).Concat(item.RelatedOperationIds).Distinct()),
            ["description"] = new JArray(item.DocumentId ?? "", item.Category, item.Outcome, string.Join(" ", item.ExceptionTypes)),
            ["category"] = item.Level
        };

        private static string Module(DiagnosticEvent item)
        {
            var separator = item.Operation.IndexOf('.');
            return separator > 0 ? item.Operation.Substring(0, separator) : item.Operation;
        }

        private static VisualElement MakeRow()
        {
            var row = new VisualElement(); row.style.paddingLeft = row.style.paddingRight = 6;
            row.style.paddingTop = 5; row.style.borderBottomWidth = 1; row.style.borderBottomColor = new Color(.22f, .27f, .31f);
            row.Add(new Label { name = "diagnostics-event-title" });
            row.Add(new Label { name = "diagnostics-event-meta" });
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            var item = visible[index];
            row.Q<Label>("diagnostics-event-title").text = item.Operation + " · " + item.Code;
            row.Q<Label>("diagnostics-event-meta").text = item.LastUtc.ToLocalTime().ToString("MM-dd HH:mm:ss") +
                " · " + item.Level + " · " + item.Count + " 次";
            row.tooltip = item.Id;
        }

        private void ShowDetail(DiagnosticEvent? item) => detail.SetValueWithoutNotify(
            item == null ? "选择事件查看详情。" : item.ToJson().ToString(Formatting.Indented));

        private void OpenDirectory()
        {
            Directory.CreateDirectory(PluginLog.DirectoryPath);
            EditorUtility.RevealInFinder(PluginLog.DirectoryPath);
        }

        private void Export(bool bundle)
        {
            var path = EditorUtility.SaveFilePanel(bundle ? "导出诊断包" : "导出插件日志", "", "arkmap-diagnostics", bundle ? "zip" : "jsonl");
            if (path.Length == 0) return;
            PluginLog.Export(path, bundle);
            message.text = bundle ? "诊断包已导出。" : "日志已导出。";
        }

        private void Button(VisualElement parent, string title, string name, Action action) =>
            parent.Add(new Button(() =>
            {
                try { message.text = ""; action(); }
                catch (Exception error) { message.text = title + "失败：" + error.Message; }
            }) { text = title, name = name });

        private static Label Note(VisualElement parent, string text, string name)
        {
            var label = new Label(text) { name = name };
            label.style.whiteSpace = WhiteSpace.Normal; parent.Add(label); return label;
        }

        private static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexWrap = Wrap.Wrap;
            row.style.marginBottom = 5; parent.Add(row); return row;
        }

        private static VisualElement Card(VisualElement parent)
        {
            var panel = new VisualElement();
            panel.style.backgroundColor = new Color(.15f, .18f, .21f);
            panel.style.paddingTop = panel.style.paddingBottom = 7;
            panel.style.paddingLeft = panel.style.paddingRight = 8;
            panel.style.marginTop = panel.style.marginBottom = 4;
            panel.style.borderTopWidth = panel.style.borderBottomWidth = panel.style.borderLeftWidth = panel.style.borderRightWidth = 1;
            panel.style.borderTopColor = panel.style.borderBottomColor = panel.style.borderLeftColor = panel.style.borderRightColor = new Color(.31f, .39f, .44f);
            parent.Add(panel); return panel;
        }
    }
}
