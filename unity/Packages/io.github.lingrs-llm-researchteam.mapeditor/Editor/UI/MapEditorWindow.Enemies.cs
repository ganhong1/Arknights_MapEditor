#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        [SerializeField] private int enemyReferenceIndex = -1;
        [SerializeField] private bool inlineEnemySelected;
        [SerializeField] private string enemySelectionSlot = "";
        [SerializeField] private string selectedEnemyId = "";
        private Func<JObject, bool>? prepareEnemyReference;
        private Func<JObject, bool>? prepareInlineEnemy;
        private Action? refreshEnemySelection;
        private Action? refreshEnemyDrafts;

        private static JObject BlankEnemyReference(string id, int level) => new JObject
        { ["useDb"] = true, ["id"] = id, ["level"] = level, ["overwrittenData"] = null };

        private static JObject BlankInlineEnemy() => EnemyDefinitionResolver.CreateDefaultDefinition();

        private void BuildEnemiesPanel(VisualElement parent)
        {
            var catalog = EnemyCatalog.LoadBundled();
            var portraits = EnemyPortraitCatalog.LoadOptional();
            var panel = new VisualElement { name = "enemy-editor" };
            parent.Add(panel);
            var overview = new VisualElement { name = "enemy-defined-records" };
            overview.AddToClassList("option-field-card");
            overview.Add(new Label("本关已定义敌人") { name = "enemy-defined-title" });
            var columns = new VisualElement();
            columns.AddToClassList("enemy-definition-columns");
            overview.Add(columns);
            var refsView = EnemyColumn(columns, "引用定义 · enemyDbRefs", "enemy-reference-cards");
            var inlineView = EnemyColumn(columns, "内联定义 · enemies", "enemy-inline-cards");
            var pending = new Foldout { text = "待处理草稿", value = false, name = "enemy-detached-drafts" };
            overview.Add(pending);
            panel.Add(overview);

            var selectTools = PresetButtonRow(panel);
            var title = new Label { name = "enemy-editor-title" };
            title.AddToClassList("catalog-name");
            panel.Add(title);
            var message = new Label { name = "enemy-editor-message" };
            message.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(message);
            var actions = PresetButtonRow(panel);
            var form = new VisualElement { name = "enemy-definition-form" };
            panel.Add(form);
            var preview = new Foldout { text = "合成定义预览", name = "enemy-resolved-preview", value = false };
            var resolvedJson = new TextField { multiline = true, isReadOnly = true, name = "enemy-resolved-json" };
            preview.Add(resolvedJson);
            panel.Add(preview);
            var usages = new Foldout { text = "引用位置", value = false };
            panel.Add(usages);
            var rawFold = new Foldout { text = "完整引用 JSON", value = false };
            var raw = new TextField { multiline = true, name = "enemy-reference-json" };
            raw.style.minHeight = 92;
            rawFold.Add(raw);
            panel.Add(rawFold);

            var documentSession = session ?? throw new InvalidOperationException("地图会话尚未初始化。");
            var document = documentSession.Document;
            var refs = document.GetEnemyReferences();
            var inline = document.GetInlineEnemies();
            var choices = new System.Collections.Generic.Dictionary<string, (bool Inline, int Index)>(StringComparer.Ordinal);
            var revision = "";
            var slot = "";
            var detached = false;
            var current = -1;
            var isInline = false;
            var baseline = "";
            var allowDraw = false;
            var sectionStates = new System.Collections.Generic.Dictionary<string, bool>(StringComparer.Ordinal);
            var apply = EnemyButton(actions, "应用到本关", "enemy-apply");
            var delete = EnemyButton(actions, "删除", "enemy-delete");
            var rebase = EnemyButton(actions, "以当前文档为基准保留新增草稿", "enemy-rebase-new");
            var copyInline = EnemyButton(actions, "复制为内联定义", "enemy-copy-inline");
            var copyRef = EnemyButton(actions, "复制为独立变体", "enemy-copy-independent");
            apply.clicked += () => Guard(Apply);
            delete.clicked += () => Guard(Delete);
            rebase.clicked += () => Guard(RebaseNew);
            copyInline.clicked += () => Guard(CopyInline);
            copyRef.clicked += () => Guard(CopyReference);
            EnemyButton(actions, "重新读取", "enemy-reload", Reload);
            EnemyButton(selectTools, "新建引用", "enemy-new-reference", () => SelectRecord(false, -1));
            EnemyButton(selectTools, "新建内联", "enemy-new-inline", () => SelectRecord(true, -1));
            EnemyButton(selectTools, "从敌人资源库选择", "enemy-open-library", () => openResourceCategory?.Invoke(2));
            EnemyButton(selectTools, "在字段树中查看", "locate-field-enemyDbRefs",
                () => LocateDocumentField(isInline ? "enemies" : "enemyDbRefs"));

            prepareEnemyReference = record => UseDraft(false, record);
            prepareInlineEnemy = record => UseDraft(true, record);
            raw.RegisterValueChangedCallback(change => Guard(() => Capture(change.newValue, true)));
            refreshEnemyDrafts = () => { revision = ""; SyncDocument(); };
            refreshFieldSummaries.Add(SyncDocument);
            SyncDocument();

            static JObject Blank(bool inlineRecord) => inlineRecord ? BlankInlineEnemy() : BlankEnemyReference("", 0);
            static string Format(JToken value) => value.ToString(Formatting.Indented);
            string ActiveKey(JObject record) => isInline ? InlineKey(record) : Text(record, "id");
            JArray Records(bool inlineRecord) => inlineRecord ? inline : refs;
            JArray? Levels(JObject record) => catalog.Find(Text(record, "id")) is { } entry
                ? catalog.Details(entry)["databaseLevels"] as JArray : null;
            JObject Resolve(JObject record) => isInline ? record : EnemyDefinitionResolver.ResolveReference(record, Levels(record));

            void SyncDocument()
            {
                if (revision == documentSession.Json) return;
                revision = documentSession.Json;
                document = documentSession.Document;
                refs = document.GetEnemyReferences();
                inline = document.GetInlineEnemies();
                RenderCards();
                if (InspectorStore.HasChanges("enemy"))
                {
                    InspectorStore.TryReadTarget("enemy", out var originalTarget);
                    InspectorStore.TryReadInitial("enemy", out var originalRecord);
                    var validIndex = int.TryParse(originalTarget, NumberStyles.Integer, CultureInfo.InvariantCulture, out var legacyIndex) &&
                        legacyIndex >= 0 && legacyIndex < refs.Count && Format(refs[legacyIndex]) == originalRecord;
                    var legacySlot = originalTarget == "-1" ? EnemySlot(refs, false, -1) :
                        validIndex ? EnemySlot(refs, false, legacyIndex) : "enemy/reference/recovered";
                    if (InspectorStore.HasChanges(legacySlot)) legacySlot = "enemy/reference/recovered";
                    // Keep the original revision and baseline when migrating a recovered draft.
                    InspectorStore.RenameSlot("enemy", legacySlot);
                    enemySelectionSlot = legacySlot;
                    PersistInspectorDrafts();
                    RenderCards();
                }
                else if (string.IsNullOrEmpty(enemySelectionSlot))
                    enemySelectionSlot = inlineEnemySelected ? EnemySlot(inline, true, -1) :
                        EnemySlot(refs, false, enemyReferenceIndex < refs.Count ? enemyReferenceIndex : -1);
                if (choices.TryGetValue(enemySelectionSlot, out var selected)) SelectRecord(selected.Inline, selected.Index);
                else if (InspectorStore.HasChanges(enemySelectionSlot)) ShowDetached(enemySelectionSlot);
                else SelectRecord(inlineEnemySelected, -1);
            }

            void SelectRecord(bool inlineRecord, int index)
            {
                var list = Records(inlineRecord);
                var target = EnemySlot(list, inlineRecord, index);
                var record = index < 0 ? Blank(inlineRecord) : list[index];
                if (slot != target) sectionStates.Clear();
                isInline = inlineRecord;
                current = index;
                detached = false;
                slot = enemySelectionSlot = target;
                enemyReferenceIndex = inlineRecord ? -1 : index;
                inlineEnemySelected = inlineRecord;
                baseline = Format(record);
                title.text = index < 0 ? (isInline ? "新增内联定义" : "新增引用定义") :
                    (isInline ? "内联定义 · enemies" : "引用定义 · enemyDbRefs") + "[" + index + "]";
                rawFold.text = isInline ? "完整内联 JSON" : "完整引用 JSON";
                selectedEnemyId = record is JObject entry ? Text(entry, isInline ? "key" : "id") : "";
                refreshEnemySelection?.Invoke();
                raw.SetEnabled(true);
                rebase.style.display = index < 0 && InspectorStore.HasChanges(slot) && !DraftIsCurrent() ? DisplayStyle.Flex : DisplayStyle.None;
                apply.SetEnabled(true);
                delete.SetEnabled(index >= 0);
                copyInline.SetEnabled(true);
                copyRef.SetEnabled(!isInline);
                allowDraw = true;
                Show(InspectorStore.HasChanges(slot) && InspectorStore.TryRead(slot, out var saved) ? saved : baseline);
                UpdateCardStates();
            }

            void ShowDetached(string id)
            {
                if (!InspectorStore.TryRead(id, out var saved)) return;
                slot = enemySelectionSlot = id;
                isInline = id.StartsWith("enemy/inline/", StringComparison.Ordinal);
                detached = true;
                current = -1;
                allowDraw = false;
                raw.SetValueWithoutNotify(saved);
                rawFold.value = true;
                title.text = "待处理草稿";
                message.text = "找不到草稿原先绑定的记录。可复制完整 JSON，或重新读取以清除此草稿。";
                form.Clear(); usages.Clear();
                preview.style.display = DisplayStyle.None;
                raw.SetEnabled(false);
                apply.SetEnabled(false); delete.SetEnabled(false); copyInline.SetEnabled(false); copyRef.SetEnabled(false);
                rebase.style.display = DisplayStyle.None;
                UpdateCardStates();
            }

            bool UseDraft(bool inlineRecord, JObject value)
            {
                try
                {
                    RequireWorkspaceEditable();
                    var target = EnemySlot(Records(inlineRecord), inlineRecord, -1);
                    var candidate = Format(value);
                    if (InspectorStore.HasChanges(target) && InspectorStore.TryRead(target, out var old) && old != candidate &&
                        !Dialogs.Discard("替换新增敌人草稿", "确定用所选配置替换此新增草稿？")) return false;
                    InspectorStore.SetDraft(target, documentSession.Json, "-1", Format(Blank(inlineRecord)), candidate);
                    SelectRecord(inlineRecord, -1);
                    PersistEnemyDrafts();
                    selectWorkbenchTab?.Invoke(8);
                    return true;
                }
                catch (Exception error) { message.text = error.Message; return false; }
            }

            void PersistEnemyDrafts()
            {
                PersistInspectorDrafts();
                UpdateCardStates();
                RefreshDetachedDrafts();
                RefreshDocumentNotice();
            }

            void Capture(string text, bool rebuild)
            {
                RequireWorkspaceEditable();
                if (detached || !allowDraw) return;
                if (text == baseline && DraftIsCurrent()) InspectorStore.Remove(slot);
                else if (InspectorStore.HasChanges(slot)) InspectorStore.Update(slot, text);
                else InspectorStore.SetDraft(slot, documentSession.Json, current.ToString(CultureInfo.InvariantCulture), baseline, text);
                raw.SetValueWithoutNotify(text);
                rebase.style.display = current < 0 && InspectorStore.HasChanges(slot) && !DraftIsCurrent()
                    ? DisplayStyle.Flex : DisplayStyle.None;
                PersistEnemyDrafts();
                if (rebuild) Show(text);
                else ShowPreview();
            }

            bool DraftIsCurrent()
            {
                if (!InspectorStore.HasChanges(slot)) return true;
                if (current >= 0)
                    return InspectorStore.TryReadInitial(slot, out var initial) && initial == baseline &&
                        InspectorStore.IsCurrent(slot, documentSession.Json, current.ToString(CultureInfo.InvariantCulture));
                try { InspectorStore.RequireCurrent(slot, documentSession.Json, "-1"); return true; }
                catch (InvalidOperationException) { return false; }
            }

            void Show(string text)
            {
                raw.SetValueWithoutNotify(text);
                form.Clear();
                message.text = "";
                try
                {
                    var record = ParseEnemyDraft(text);
                    var changed = new Action<bool>(rebuild => Guard(() => Capture(Format(record), rebuild)));
                    if (isInline) EnemyDefinitionForm.BuildInline(form, record, catalog.Find(Text(record, "key")), catalog, changed);
                    else EnemyDefinitionForm.BuildReference(form, record, catalog, Levels, changed);
                    foreach (var section in form.Query<Foldout>().ToList().Where(item => !string.IsNullOrEmpty(item.name)))
                    {
                        section.RegisterValueChangedCallback(change =>
                        {
                            if (change.target != section) return;
                            sectionStates[section.name] = change.newValue;
                        });
                        if (sectionStates.TryGetValue(section.name, out var open)) section.value = open;
                    }
                }
                catch (Exception error) { message.text = error.Message; }
                ShowPreview(false);
            }

            void ShowPreview(bool clearError = true)
            {
                if (clearError) message.text = "";
                preview.style.display = isInline ? DisplayStyle.None : DisplayStyle.Flex;
                resolvedJson.SetValueWithoutNotify("");
                usages.Clear();
                usages.text = "引用位置";
                try
                {
                    var record = ParseEnemyDraft(raw.value);
                    var value = Resolve(record);
                    resolvedJson.SetValueWithoutNotify(Format(value));
                    var key = current >= 0 && Records(isInline)[current] is JObject existing
                        ? ActiveKey(existing) : EnemyDefinitionResolver.GetKeyInLevel(value) ?? "";
                    var paths = key.Length == 0 ? Array.Empty<string>() : document.GetEnemyUsages(key);
                    usages.text = "引用位置 · " + paths.Count;
                    var usagePreview = new VisualElement();
                    foreach (var path in paths)
                        EnemyButton(usages, path, "", () =>
                        {
                            LocateEnemyUsage(path);
                            usagePreview.Clear();
                            var action = JObject.Parse(documentSession.Json).SelectToken(path)?.Parent?.Parent;
                            usagePreview.Add(new TextField { multiline = true, isReadOnly = true, value = action?.ToString(Formatting.Indented) ?? "null" });
                        });
                    usages.Add(usagePreview);
                }
                catch (Exception error) { if (message.text.Length == 0) message.text = error.Message; }
            }

            void Apply()
            {
                RequireWorkspaceEditable();
                RequireAppliedDraft();
                if (detached || !DraftIsCurrent()) throw new EditorOperationRejectedException("草稿基准已变化。请核对当前记录后重新读取或保留草稿。");
                var record = ParseEnemyDraft(raw.value);
                if (!isInline) Resolve(record);
                var key = ActiveKey(record);
                var previous = current >= 0 && Records(isInline)[current] is JObject previousRecord ? ActiveKey(previousRecord) : "";
                var next = isInline ? document.PutInlineEnemy(current, raw.value) : document.PutEnemyReference(current, raw.value);
                if (previous.Length > 0 && previous != key)
                {
                    var paths = document.GetEnemyUsages(previous);
                    if (paths.Count > 0 && !Dialogs.Discard("更新敌人引用",
                        $"将 {previous} 改为 {key}，并更新 {paths.Count} 个位置？\n" + string.Join("\n", paths.Take(12)))) return;
                }
                var targetList = isInline ? next.GetInlineEnemies() : next.GetEnemyReferences();
                var nextIndex = current >= 0 ? current : targetList.ToList().FindIndex(item => item is JObject obj && ActiveKey(obj) == key);
                var oldSlot = slot;
                enemySelectionSlot = EnemySlot(targetList, isInline, nextIndex);
                CommitEdit(next, "应用敌人定义", null);
                InspectorStore.Remove(oldSlot);
                revision = "";
                SyncDocument();
                PersistEnemyDrafts();
            }

            void Delete()
            {
                RequireWorkspaceEditable();
                RequireAppliedDraft();
                if (detached || current < 0) return;
                var next = isInline ? document.DeleteInlineEnemy(current) : document.DeleteEnemyReference(current);
                if (!Dialogs.Discard("删除敌人定义", "确定删除所选定义及其未应用草稿？")) return;
                var oldSlot = slot;
                enemySelectionSlot = EnemySlot(Records(isInline), isInline, -1);
                CommitEdit(next, "删除敌人定义", null);
                InspectorStore.Remove(oldSlot);
                PersistEnemyDrafts();
            }

            void Reload()
            {
                RequireWorkspaceEditable();
                if (InspectorStore.HasChanges(slot) && !Dialogs.Discard("重新读取敌人定义", "确定用已应用的定义替换当前草稿？")) return;
                InspectorStore.Remove(slot);
                SelectRecord(isInline, detached ? -1 : current);
                PersistEnemyDrafts();
            }

            void RebaseNew()
            {
                RequireWorkspaceEditable();
                if (detached || current >= 0 || !InspectorStore.TryRead(slot, out var saved)) return;
                InspectorStore.SetDraft(slot, documentSession.Json, "-1", baseline, saved);
                rebase.style.display = DisplayStyle.None;
                PersistEnemyDrafts();
            }

            string NextIdentity(string source)
            {
                var stem = source.Length == 0 ? "enemy_custom" : source + "_custom";
                var id = stem;
                for (var suffix = 1; refs.OfType<JObject>().Any(item => Text(item, "id") == id) ||
                    inline.OfType<JObject>().Any(item => InlineKey(item) == id); suffix++) id = stem + "_" + suffix;
                return id;
            }

            void CopyInline()
            {
                var record = (JObject)Resolve(ParseEnemyDraft(raw.value)).DeepClone();
                if (Text(record, "key").Length == 0) throw new EditorOperationRejectedException("请先选择有效的敌人预制体。");
                record["alias"] = NextIdentity(InlineKey(record));
                UseDraft(true, record);
            }

            void CopyReference()
            {
                var record = Resolve(ParseEnemyDraft(raw.value));
                UseDraft(false, EnemyDefinitionResolver.MaterializeIndependent(record, NextIdentity(InlineKey(record))));
            }

            void RenderCards()
            {
                choices.Clear();
                AddColumn(false, refsView);
                AddColumn(true, inlineView);
                RefreshDetachedDrafts();
            }

            void AddColumn(bool inlineRecord, ScrollView view)
            {
                view.Clear();
                var list = Records(inlineRecord);
                if (list.Count == 0) view.Add(new Label("暂无定义"));
                for (var index = 0; index < list.Count; index++)
                {
                    var itemIndex = index;
                    var id = EnemySlot(list, inlineRecord, index);
                    choices.Add(id, (inlineRecord, index));
                    var record = list[index] as JObject;
                    var key = record == null ? "" : inlineRecord ? InlineKey(record) : Text(record, "id");
                    var patch = record?["overwrittenData"] as JObject;
                    var prefab = inlineRecord ? Text(record, "key") : Text(patch?["prefabKey"] as JObject, "m_value");
                    var entry = catalog.Find(key) ?? catalog.Find(prefab);
                    var caption = record == null ? "无法识别的记录" : inlineRecord ? Text(record, "name") : Text(patch?["name"] as JObject, "m_value");
                    if (caption.Length == 0) caption = entry?.Name ?? (key.Length == 0 ? "未命名敌人" : key);
                    var mode = inlineRecord ? "" : record?["useDb"]?.Type == JTokenType.Boolean && (bool?)record["useDb"] == false
                        ? " · 独立定义" : " · Lv." + (record?["level"]?.ToString() ?? "0");
                    var card = new Button(() => SelectRecord(inlineRecord, itemIndex))
                    { name = (inlineRecord ? "enemy-inline-" : "enemy-reference-") + index, tooltip = key };
                    card.AddToClassList("enemy-defined-card");
                    card.userData = id;
                    var picture = portraits.Find(entry?.Id ?? prefab, prefab.Length == 0 ? null : prefab) ??
                        portraits.Find(prefab) ?? portraits.Find(entry?.Id ?? "");
                    var image = LibraryPortrait.Create("operator-library-avatar", "enemy-defined-portrait");
                    LibraryPortrait.Bind(image, picture == null ? null : portraits.Texture(picture), picture?.Uv ?? default, picture?.LightBackground == true);
                    card.Add(image);
                    var labels = new VisualElement { pickingMode = PickingMode.Ignore };
                    labels.AddToClassList("library-row-labels");
                    var name = new Label(caption) { name = "enemy-defined-name" };
                    name.AddToClassList("catalog-name");
                    var identity = new Label(key + mode) { name = "enemy-defined-key" };
                    identity.AddToClassList("catalog-id");
                    labels.Add(name);
                    labels.Add(identity);
                    card.Add(labels);
                    card.Add(new Label("●") { name = "enemy-defined-dirty", pickingMode = PickingMode.Ignore });
                    view.Add(card);
                }
                choices[EnemySlot(list, inlineRecord, -1)] = (inlineRecord, -1);
            }

            void UpdateCardStates()
            {
                foreach (var card in refsView.Query<Button>(className: "enemy-defined-card").ToList()
                    .Concat(inlineView.Query<Button>(className: "enemy-defined-card").ToList()))
                {
                    var id = (string)card.userData;
                    card.EnableInClassList("selected-tab", id == slot);
                    card.Q("enemy-defined-dirty").style.visibility = InspectorStore.HasChanges(id) ? Visibility.Visible : Visibility.Hidden;
                }
                selectTools.Q<Button>("enemy-new-reference").EnableInClassList("selected-tab", !detached && current < 0 && !isInline);
                selectTools.Q<Button>("enemy-new-inline").EnableInClassList("selected-tab", !detached && current < 0 && isInline);
                foreach (var inlineRecord in new[] { false, true })
                {
                    var button = selectTools.Q<Button>(inlineRecord ? "enemy-new-inline" : "enemy-new-reference");
                    button.text = (inlineRecord ? "新建内联" : "新建引用") +
                        (InspectorStore.HasChanges(EnemySlot(Records(inlineRecord), inlineRecord, -1)) ? " ●" : "");
                }
            }

            void RefreshDetachedDrafts()
            {
                pending.Clear();
                var orphaned = InspectorStore.DirtySlots("enemy/").Where(id => !choices.ContainsKey(id)).ToArray();
                pending.style.display = orphaned.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                foreach (var id in orphaned)
                {
                    InspectorStore.TryReadInitial(id, out var source);
                    string key;
                    try { var entry = ParseEnemyDraft(source); key = id.StartsWith("enemy/inline/", StringComparison.Ordinal) ? InlineKey(entry) : Text(entry, "id"); }
                    catch (Exception error) when (error is JsonException || error is LevelDocumentException) { key = ""; }
                    EnemyButton(pending, key.Length == 0 ? "查看未绑定草稿" : key, "", () => ShowDetached(id));
                }
            }

            Button EnemyButton(VisualElement container, string text, string name, Action? action = null)
            {
                var button = new Button { text = text, name = name };
                if (action != null) button.clicked += () => Guard(action);
                container.Add(button);
                return button;
            }

            void Guard(Action action)
            {
                try { action(); }
                catch (Exception error) { message.text = error.Message; }
            }
        }

        private static ScrollView EnemyColumn(VisualElement parent, string title, string name)
        {
            var column = new VisualElement();
            column.AddToClassList("enemy-definition-column");
            var heading = new Label(title);
            heading.style.whiteSpace = WhiteSpace.Normal;
            column.Add(heading);
            var scroll = new ScrollView { name = name };
            scroll.AddToClassList("enemy-definition-cards");
            column.Add(scroll);
            parent.Add(column);
            return scroll;
        }

        private static string EnemySlot(JArray records, bool inline, int index)
        {
            var prefix = inline ? "enemy/inline/" : "enemy/reference/";
            if (index < 0) return prefix + "new";
            var record = records[index] as JObject;
            var key = inline ? InlineKey(record) : Text(record, "id");
            string Identity(JToken item) => inline ? InlineKey(item as JObject) : Text(item as JObject, "id");
            var ordinal = records.Take(index).Count(item => Identity(item) == key);
            using var hash = SHA256.Create();
            return prefix + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "") + "/" + ordinal;
        }

        private static JObject ParseEnemyDraft(string raw) => LevelDocument.ParseEnemyDefinition(raw);

        private static string InlineKey(JObject? record)
        {
            var alias = Text(record, "alias");
            return alias.Length == 0 ? Text(record, "key") : alias;
        }

        private static string Text(JObject? record, string key) =>
            record?[key]?.Type == JTokenType.String ? (string)record[key]! : "";

        private void LocateEnemyUsage(string path)
        {
            var root = path.Split('.')[0].Split('[')[0];
            LocateDocumentField(root);
            if (status != null) status.text = "敌人引用位置：" + path;
        }
    }
}
