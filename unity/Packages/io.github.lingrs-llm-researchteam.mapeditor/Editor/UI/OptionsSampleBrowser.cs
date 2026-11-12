#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    /// <summary>A persistent per-field browser. Browsing never writes the draft.</summary>
    internal sealed class OptionsSampleBrowser : VisualElement
    {
        private readonly string key;
        private readonly bool blackboardEntry;
        private readonly Action<JToken> choose;
        private readonly string chooseLabel;
        private readonly Label summary = Text("");
        private readonly Label feedback = Text("");
        private readonly Label current = Text("");
        private readonly VisualElement recommendations = Row();
        private readonly Foldout all = new Foldout { text = "全部已知值与出处", value = false };
        private readonly VisualElement content = new VisualElement();
        private readonly TextField find = new TextField("搜索取值");
        private readonly List<OptionsValueCatalog.Sample> results = new List<OptionsValueCatalog.Sample>();
        private readonly ListView values;
        private readonly VisualElement sampleDetails = new VisualElement { name = "options-selected-sample" };
        private readonly TextField valueJson = Json("选中原版值");
        private readonly Label selectedSummary = Text("");
        private readonly Foldout sourceFoldout = new Foldout { name = "options-sample-sources", text = "所选值的出处", value = false };
        private readonly TextField sourceFind = new TextField("搜索出处");
        private readonly List<OptionsValueCatalog.Source> sourceRows = new List<OptionsValueCatalog.Source>();
        private readonly ListView sources;
        private readonly List<Button> choices = new List<Button>();
        private readonly Button chooseSelected;
        private readonly Label sourceSummary = Text("");
        private readonly TextField context = Json("出处 options 上下文（只读）");
        private OptionsValueCatalog? catalog;
        private OptionsValueCatalog.Group? group;
        private OptionsValueCatalog.Sample? selected;
        private OptionsValueCatalog.Source? selectedSource;
        private CatalogSearch<OptionsValueCatalog.Sample>? valueSearch;
        private CatalogSearch<OptionsValueCatalog.Source>? sourceSearch;
        private IReadOnlyList<OptionsValueCatalog.Source> allSources = Array.Empty<OptionsValueCatalog.Source>();
        private bool initialized;
        private bool selecting;
        private bool showingMissing;
        private bool canChoose = true;

        internal OptionsSampleBrowser(string key, Action<JToken> choose, string chooseLabel = "选用此值", bool blackboardEntry = false)
        {
            this.key = key; this.choose = choose; this.chooseLabel = chooseLabel; this.blackboardEntry = blackboardEntry;
            name = "options-observed-" + key;
            style.marginTop = 5; style.marginBottom = 7;
            Add(current); Add(summary); Add(recommendations); Add(feedback); Add(all);
            all.Add(content); content.Add(find);
            values = new ListView(results, 44, MakeSampleRow, (element, index) =>
            {
                var sample = results[index];
                element.Q<Label>().text = "#" + (sample.Index + 1) + " · " + sample.Label + "\n" + sample.Count + " 份去重正文 · " + sample.Sources.Count + " 个出处";
                element.Q<Button>("options-sample-info").userData = sample;
            }) { name = name + "-values", selectionType = SelectionType.Single };
            values.style.height = 176; content.Add(values);
            values.onSelectionChange += items => { if (!selecting && items.FirstOrDefault() is OptionsValueCatalog.Sample sample) Select(sample); };
            content.Add(sampleDetails);
            sampleDetails.Add(selectedSummary);
            sampleDetails.Add(JsonScroll(valueJson));
            chooseSelected = OptionsValueEditor.Action(sampleDetails, chooseLabel, ApplySelected, name + "-choose");
            sampleDetails.style.display = DisplayStyle.None;
            sourceFoldout.style.display = DisplayStyle.None;
            content.Add(sourceFoldout); sourceFoldout.Add(sourceFind);
            sources = new ListView(sourceRows, 60, () => Text(""), (element, index) =>
            {
                var source = sourceRows[index]; var title = TerrainSearchRuntime.StageName(source.Path);
                ((Label)element).text = (string.IsNullOrEmpty(title) ? "" : title + "\n") + source.Path + "\n" + source.Kind;
            }) { name = name + "-sources", selectionType = SelectionType.Single };
            sources.style.height = 168;
            sources.onSelectionChange += items => { if (!selecting && items.FirstOrDefault() is OptionsValueCatalog.Source source) ShowSource(source); };
            sourceFoldout.Add(sources); sourceFoldout.Add(sourceSummary); sourceFoldout.Add(JsonScroll(context));
            find.RegisterValueChangedCallback(_ => RefreshValues());
            sourceFind.RegisterValueChangedCallback(_ => RefreshSources());
            all.RegisterValueChangedCallback(e => { if (e.target == all && e.newValue) { EnsureLoaded(); RefreshValues(); } });
            EnsureLoaded();
        }

        private VisualElement MakeSampleRow()
        {
            var row = new VisualElement();
            row.AddToClassList("options-sample-row");
            row.Add(Text(""));
            var info = new Button { name = "options-sample-info", tooltip = "查看此值的关卡出处" };
            info.AddToClassList("terrain-info-button");
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/Catalog/terrain-info.png");
            if (icon != null) info.Add(new Image { image = icon, pickingMode = PickingMode.Ignore });
            else info.text = "ⓘ";
            info.clicked += () =>
            {
                if (!(info.userData is OptionsValueCatalog.Sample sample)) return;
                var host = parent;
                while (host != null && !host.ClassListContains("workbench")) host = host.parent;
                if (host == null) return;
                host.Q<OptionsSourceOverlay>()?.RemoveFromHierarchy();
                var overlay = new OptionsSourceOverlay();
                host.Add(overlay);
                overlay.Open(key, sample, info);
            };
            row.Add(info);
            return row;
        }

        internal void DisplayCurrent(JToken? value, string? operationNotice = null)
        {
            current.text = "当前值：" + (value == null ? "未提供" : OptionsValueCatalog.Compact(value)) +
                (string.IsNullOrEmpty(operationNotice) ? "" : "\n" + operationNotice);
            if (group != null && value != null && !group.Samples.Any(sample => sample.Matches(value)))
                current.text += " · 自定义值";
        }

        internal void SetEditingEnabled(bool enabled)
        {
            canChoose = enabled;
            foreach (var button in choices) button.SetEnabled(enabled);
            chooseSelected.SetEnabled(enabled && selected != null);
        }

        private void EnsureLoaded()
        {
            if (initialized) return;
            try
            {
                catalog = OptionsValueCatalog.LoadBundled();
                group = blackboardEntry ? catalog.Blackboard(key) : catalog.Field(key);
                if (group == null) { summary.text = "原版关卡中未收录此键的取值。"; initialized = true; return; }
                summary.text = group.Samples.Count + " 种已知值" +
                    (!blackboardEntry ? " · " + group.MissingSources.Count + " 个出处未提供字段" : "");
                foreach (var sample in group.Recommended)
                {
                    var candidate = sample;
                    var buttonText = (chooseLabel == "选用此值" ? "" : chooseLabel + " ") + sample.Label + " · " + sample.PrimaryCount + "份";
                    var button = OptionsValueEditor.Action(recommendations, buttonText,
                        () => { Select(candidate); ApplySelected(); }, name + "-recommended-" + sample.Index);
                    button.style.whiteSpace = WhiteSpace.Normal;
                    button.tooltip = "选用到当前草稿。\n" + sample.Json + "\nArknightsGameData：" + sample.PrimaryCount + " 份去重正文；共 " + sample.Sources.Count + " 个出处。";
                    choices.Add(button);
                }
                if (group.MissingSources.Count > 0)
                {
                    var missing = new Button(ShowMissing)
                    {
                        text = "查看未提供此字段的出处（" + group.MissingSources.Count + "）",
                        name = name + "-missing"
                    };
                    missing.style.whiteSpace = WhiteSpace.Normal;
                    content.Insert(content.IndexOf(sampleDetails), missing);
                }
                initialized = true;
            }
            catch (Exception error) { feedback.text = "取值目录读取失败：" + error.Message; }
        }

        private void RefreshValues()
        {
            if (group == null) return;
            try
            {
                IEnumerable<OptionsValueCatalog.Sample> filtered = group.Samples;
                if (!string.IsNullOrWhiteSpace(find.value))
                {
                    valueSearch ??= TerrainSearchRuntime.CreateWorker<OptionsValueCatalog.Sample>(sample => new JObject
                    { ["name"] = key, ["targetFile"] = sample.Label, ["description"] = sample.Json, ["category"] = new JArray(sample.Sources.Select(source => source.Path)) });
                    filtered = valueSearch.Filter(group.Samples, find.value, out var error); feedback.text = error ?? "";
                }
                if (!results.SequenceEqual(filtered))
                {
                    selecting = true;
                    results.Clear(); results.AddRange(filtered); values.Rebuild();
                    selecting = false;
                }
                all.text = "全部已知值与出处 · " + results.Count + "/" + group.Samples.Count;
                if (!showingMissing && (selected == null || !results.Contains(selected)))
                    Select(results.FirstOrDefault());
                else
                    SyncValueSelection();
            }
            catch (Exception error) { selecting = false; feedback.text = "筛选不可用：" + error.Message; }
        }

        private void Select(OptionsValueCatalog.Sample? sample)
        {
            if (showingMissing || selected != sample) selectedSource = null;
            showingMissing = false;
            selected = sample;
            SyncValueSelection();
            sampleDetails.style.display = sourceFoldout.style.display = sample == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (sample == null) { chooseSelected.SetEnabled(false); return; }
            valueJson.SetValueWithoutNotify(sample.Json);
            chooseSelected.SetEnabled(canChoose);
            selectedSummary.text = "配置 #" + (sample.Index + 1) + " · " + sample.Count + " 份去重正文 · " + sample.Sources.Count + " 个出处";
            allSources = sample.Sources;
            sourceFoldout.text = "所选值的出处 · " + allSources.Count;
            RefreshSources();
        }

        private void ShowMissing()
        {
            if (group == null) return;
            if (!showingMissing) selectedSource = null;
            showingMissing = true;
            selected = null; SyncValueSelection(); chooseSelected.SetEnabled(false);
            sampleDetails.style.display = DisplayStyle.None;
            sourceFoldout.style.display = DisplayStyle.Flex;
            allSources = group.MissingSources; sourceFoldout.value = true;
            sourceFoldout.text = "未提供此字段的全部出处 · " + allSources.Count;
            RefreshSources();
        }

        private void SyncValueSelection()
        {
            var wasSelecting = selecting;
            selecting = true;
            try
            {
                var index = selected == null ? -1 : results.IndexOf(selected);
                values.SetSelectionWithoutNotify(index < 0 ? Array.Empty<int>() : new[] { index });
            }
            finally { selecting = wasSelecting; }
        }

        private void ApplySelected()
        {
            if (selected == null || !canChoose) return;
            try { choose(selected.CopyValue()); feedback.text = ""; }
            catch (Exception error) { feedback.text = "未选用：" + error.Message; }
        }

        private void RefreshSources()
        {
            try
            {
                IEnumerable<OptionsValueCatalog.Source> filtered = allSources;
                if (!string.IsNullOrWhiteSpace(sourceFind.value))
                {
                    sourceSearch ??= TerrainSearchRuntime.CreateWorker<OptionsValueCatalog.Source>(source =>
                    {
                        var entry = TerrainSearchRuntime.Stage(source.Path);
                        entry["description"] = source.Kind + " " + source.Revision;
                        return entry;
                    });
                    filtered = sourceSearch.Filter(allSources, sourceFind.value, out var error); feedback.text = error ?? "";
                }
                selecting = true; sourceRows.Clear(); sourceRows.AddRange(filtered); sources.Rebuild();
                var index = selectedSource == null ? -1 : sourceRows.IndexOf(selectedSource);
                if (index < 0 && sourceRows.Count > 0) index = 0;
                sources.SetSelectionWithoutNotify(index < 0 ? Array.Empty<int>() : new[] { index }); selecting = false;
                if (index >= 0) ShowSource(sourceRows[index]);
                else { sourceSummary.text = "无匹配出处"; context.SetValueWithoutNotify(""); }
            }
            catch (Exception error) { selecting = false; feedback.text = "出处读取失败：" + error.Message; }
        }

        private void ShowSource(OptionsValueCatalog.Source source)
        {
            if (catalog == null) return;
            selectedSource = source;
            sourceSummary.text = SourceDescription(source, selected == null ? "/options/" + key + "（未提供）" : selected.Pointers(source));
            context.SetValueWithoutNotify(source.CopyOptions().ToString(Formatting.Indented));
        }

        internal static string SourceDescription(OptionsValueCatalog.Source source, string pointer) =>
            source.Path + "\n" + pointer + "\n来源：" + source.Kind + "\n版本：" + (source.Revision ?? "版本未绑定") +
                "\nSHA-256：" + source.Sha256 + "\nlevelId：" + source.LevelId + " · mapId：" + source.MapId + "\noptions 状态：" + source.State;

        internal static Label Text(string value) { var label = new Label(value); label.style.whiteSpace = WhiteSpace.Normal; return label; }
        private static VisualElement Row() { var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexWrap = Wrap.Wrap; return row; }
        private static TextField Json(string label)
        {
            var field = new TextField(label) { multiline = true, isReadOnly = true };
            field.style.flexDirection = FlexDirection.Column;
            field.labelElement.style.minWidth = 0;
            field.labelElement.style.width = StyleKeyword.Auto;
            field.labelElement.style.whiteSpace = WhiteSpace.Normal;
            return field;
        }

        private static ScrollView JsonScroll(TextField field)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = 180;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.Add(field);
            return scroll;
        }
    }
}
