#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        [SerializeField] private string devicePresetSection = "predefines";
        [SerializeField] private string devicePresetRole = "characterInsts";
        [SerializeField] private int devicePresetIndex;
        [SerializeField] private bool presetCardView;
        [SerializeField] private string selectedOperatorId = "";
        [SerializeField] private bool libraryOperatorDraft;
        [SerializeField] private string operatorSelectionInitialJson = "";
        [SerializeField] private bool showPresetHand;
        private Action? refreshPresets;
        private Action<int>? setPresetSelectionIndex;
        private Action? openPresetPanel;
        private Action<string>? selectLastPlacedOperator;
        private Func<string, JObject, bool>? prepareOperatorPreset;
        private Func<string, JObject, bool>? prepareLibraryOperator;
        private Action? refreshOperatorLibrarySelection;
        private Func<string, string, int, bool>? selectHandPreset;
        private Vector2Int? selectedPresetCell;
        internal Vector2Int? SelectedPresetCell => workbenchTab == 5 ? selectedPresetCell : null;
        internal bool OperatorPlacementEnabled => devicePlacementEnabled && workbenchTab == 5 && devicePresetRole == "characterInsts";
        internal bool PresetHandVisible => showPresetHand;
        internal string ActivePresetLayer => devicePresetSection;
        internal (string Role, int Index)? ActiveHandPreset =>
            devicePresetRole is "characterCards" or "tokenCards" ? (devicePresetRole, devicePresetIndex) : null;

        internal bool SelectHandPreset(string layer, string role, int index) =>
            selectHandPreset?.Invoke(layer, role, index) ?? false;

        internal void SetPresetHandVisible(bool visible)
        {
            showPresetHand = visible;
            RefreshPresetHand();
        }

        internal string PresetHandButtonText => showPresetHand ? "隐藏手牌区" : "显示手牌区";

        private void RefreshPresetHand()
        {
            ownedPreview?.SetPresetHand(devicePresetSection, showPresetHand, ActiveHandPreset);
            workspaceOwner?.DocumentChanged(this);
        }

        private static string PresetTarget(string section, string role, int index) =>
            new JArray(section, role, index).ToString(Formatting.None);

        private static bool TryReadPresetTarget(string target, out string section, out string role, out int index)
        {
            section = role = "";
            index = -1;
            try
            {
                var fields = JArray.Parse(target);
                if (fields.Count != 3 || fields[0].Type != JTokenType.String || fields[1].Type != JTokenType.String ||
                    fields[2].Type != JTokenType.Integer || !int.TryParse(fields[2].ToString(), out index) || index < -1) return false;
                section = fields[0].Value<string>()!;
                role = fields[1].Value<string>()!;
                return section is "predefines" or "hardPredefines" &&
                    role is "characterInsts" or "characterCards" or "tokenInsts" or "tokenCards";
            }
            catch (JsonException) { return false; }
        }

        private void RestorePresetBinding()
        {
            devicePresetSection = "predefines";
            devicePresetRole = "characterInsts";
            devicePresetIndex = 0;
            selectedPresetCell = null;
            libraryOperatorDraft = false;
            selectedOperatorId = operatorSelectionInitialJson = "";
            SetDevicePlacement(false);
            if (!InspectorStore.HasChanges("preset") || !InspectorStore.TryReadTarget("preset", out var target) ||
                !TryReadPresetTarget(target, out var section, out var role, out var index)) return;
            devicePresetSection = section;
            devicePresetRole = role;
            devicePresetIndex = index;
        }

        private static JArray? PresetCollection(LevelDocument document, string section, string role) =>
            (document.GetSection(section) as JObject)?[role] as JArray;

        private JArray? PresetRecords(string section, string role) =>
            PresetCollection(session!.Document, section, role);

        private void BuildPredefinedPanel(VisualElement parent)
        {
            var panel = new VisualElement { name = "predefined-editor" };
            var sections = new List<string> { "predefines", "hardPredefines" };
            var roles = new List<string> { "characterInsts", "characterCards", "tokenInsts", "tokenCards" };
            var section = new PopupField<string>("预设层", sections, Math.Max(0, sections.IndexOf(devicePresetSection)),
                PresetSectionName, PresetSectionName) { name = "device-edit-section" };
            panel.Add(section);
            var collection = new PopupField<string>(roles, Math.Max(0, roles.IndexOf(devicePresetRole))) { name = "device-edit-role" };
            collection.style.display = DisplayStyle.None;
            panel.Add(collection);
            var index = new IntegerField { name = "device-edit-index", value = devicePresetIndex };
            setPresetSelectionIndex = row =>
            {
                devicePresetIndex = row;
                index.SetValueWithoutNotify(row);
            };
            index.style.display = DisplayStyle.None;
            panel.Add(index);
            var kind = PresetButtonRow(panel);
            var operators = new Button { text = "干员", name = "preset-kind-operators" };
            var devices = new Button { text = "装置", name = "preset-kind-devices" };
            kind.Add(operators);
            kind.Add(devices);
            var modes = PresetButtonRow(panel);
            var scene = new Button { text = "场上", name = "preset-mode-instance" };
            var cards = new Button { text = "卡牌", name = "preset-mode-card" };
            modes.Add(scene);
            modes.Add(cards);

            const int cardsPerPage = 12;
            var cardPage = Math.Max(0, index.value) / cardsPerPage;
            var viewModes = PresetButtonRow(panel);
            viewModes.AddToClassList("preset-view-modes");
            var listMode = new Button { text = "列表", name = "preset-show-list" };
            var blockMode = new Button { text = "块状", name = "preset-show-cards" };
            viewModes.Add(listMode);
            viewModes.Add(blockMode);

            var rows = new List<PresetPreviewContent>();
            var list = new ListView(rows, 24, () => new Label(), (element, row) =>
            {
                var item = rows[row];
                ((Label)element).text = $"{row + 1}. {item.Name} · {item.Detail}";
            })
            { name = "preset-records", selectionType = SelectionType.Single };
            list.style.height = 106;
            panel.Add(list);
            var blocks = new VisualElement { name = "preset-record-cards" };
            var cardScroll = new ScrollView();
            cardScroll.AddToClassList("preset-card-scroll");
            var grid = new VisualElement();
            grid.AddToClassList("tiledata-preview-cards");
            cardScroll.Add(grid);
            blocks.Add(cardScroll);
            var empty = new Label("暂无预设");
            empty.AddToClassList("tiledata-preview-message");
            blocks.Add(empty);
            var pages = PresetButtonRow(blocks);
            pages.AddToClassList("preset-view-modes");
            var previousPage = new Button { text = "上一页", name = "preset-prev-page" };
            var nextPage = new Button { text = "下一页", name = "preset-next-page" };
            var pageLabel = new Label();
            pages.Add(previousPage);
            pages.Add(pageLabel);
            pages.Add(nextPage);
            panel.Add(blocks);

            var toolbar = PresetButtonRow(panel);
            toolbar.Add(new Button(() => openResourceCategory?.Invoke(IsOperatorRole(collection.value) ? 4 : 1))
            { text = "从资源库添加" });
            var copySquad = new Button { text = "从编队复制", name = "preset-copy-squad" };
            toolbar.Add(copySquad);
            var addEmpty = new Button { text = "新建草稿" };
            toolbar.Add(addEmpty);
            var reload = new Button { text = "重新读取" };
            toolbar.Add(reload);
            var squadContainer = new VisualElement();
            panel.Add(squadContainer);
            var selectedOperator = new Label { name = "preset-selected-operator" };
            selectedOperator.AddToClassList("preset-selected-operator");
            panel.Add(selectedOperator);

            var json = new TextField { name = "device-edit-json", multiline = true, value = "{}" };
            var form = new PresetRecordForm(record =>
            {
                var raw = record.ToString(Formatting.Indented);
                json.SetValueWithoutNotify(raw);
                CaptureLocalDraft("preset", raw);
            });
            panel.Add(form);
            var advanced = new Foldout { text = "原生 JSON", value = false };
            advanced.Add(json);
            panel.Add(advanced);
            string Target() => PresetTarget(section.value, collection.value, index.value);
            void Show(string raw)
            {
                json.SetValueWithoutNotify(raw);
                JObject? value;
                try { value = LevelDocument.Create(1, 1).WithSection("preset", raw).GetSection("preset") as JObject; }
                catch (LevelDocumentException) { value = null; }
                form.Bind(value, collection.value);
            }
            var openingRecord = index.value >= 0 ? PresetRecords(section.value, collection.value)?.ElementAtOrDefault(index.value) : null;
            BindLocalDraft("preset", openingRecord?.ToString(Formatting.Indented) ?? "{}", Target(), Show);
            json.RegisterValueChangedCallback(e => CaptureLocalDraft("preset", e.newValue));
            advanced.RegisterValueChangedCallback(e => { if (e.target == advanced && !e.newValue) Show(json.value); });

            BuildDevicePlacement(panel, json, Target, section, collection);
            var buttons = PresetButtonRow(panel);
            var insert = new Button { text = "插入预设", name = "preset-insert" };
            var save = new Button { text = "保存修改", name = "preset-save" };
            var remove = new Button { text = "删除", name = "preset-delete" };
            var up = new Button { text = "上移", name = "preset-up" };
            var down = new Button { text = "下移", name = "preset-down" };
            var conflict = new HelpBox("预设草稿的绑定已变化。原始 JSON 中保留了草稿，可复制或重新读取。", HelpBoxMessageType.Warning);
            panel.Add(conflict);
            buttons.Add(insert);
            buttons.Add(save);
            buttons.Add(remove);
            buttons.Add(up);
            buttons.Add(down);

            JArray? Records() => PresetRecords(section.value, collection.value);
            bool HasSelection() => index.value >= 0 && index.value < rows.Count;
            void ShowView(bool showCards)
            {
                presetCardView = showCards;
                list.style.display = showCards ? DisplayStyle.None : DisplayStyle.Flex;
                blocks.style.display = showCards ? DisplayStyle.Flex : DisplayStyle.None;
                listMode.EnableInClassList("selected-tab", !showCards);
                blockMode.EnableInClassList("selected-tab", showCards);
                if (!showCards) return;
                if (HasSelection()) cardPage = index.value / cardsPerPage;
                RenderCards();
            }

            void ChangePage(int delta)
            {
                cardPage += delta;
                cardScroll.scrollOffset = Vector2.zero;
                RenderCards();
            }

            void RenderCards()
            {
                var pageCount = Math.Max(1, (rows.Count + cardsPerPage - 1) / cardsPerPage);
                cardPage = Math.Max(0, Math.Min(cardPage, pageCount - 1));
                grid.Clear();
                var end = Math.Min(rows.Count, (cardPage + 1) * cardsPerPage);
                for (var row = cardPage * cardsPerPage; row < end; row++)
                {
                    var target = row;
                    var item = rows[row];
                    var card = new Button(() => SelectRow(target)) { name = $"preset-record-card-{row}" };
                    card.AddToClassList("tiledata-preview-card");
                    card.AddToClassList("preset-record-card");
                    card.EnableInClassList("selected-tab", index.value == row);
                    item.AddTo(card, $"{row + 1}. {item.Name}");
                    grid.Add(card);
                }
                cardScroll.style.display = rows.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
                empty.style.display = rows.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                pages.style.display = pageCount > 1 ? DisplayStyle.Flex : DisplayStyle.None;
                pageLabel.text = $"{cardPage + 1} / {pageCount} 页";
                previousPage.SetEnabled(cardPage > 0);
                nextPage.SetEnabled(cardPage + 1 < pageCount);
            }

            void RefreshRows()
            {
                var records = Records();
                var isOperator = IsOperatorRole(collection.value);
                var isInstance = IsInstanceRole(collection.value);
                rows.Clear();
                if (records != null)
                    foreach (var record in records) rows.Add(new PresetPreviewContent(record, isOperator, isInstance));
                list.Rebuild();
                list.SetSelectionWithoutNotify(HasSelection() ? new[] { index.value } : Array.Empty<int>());
                if (presetCardView) RenderCards();
                operators.EnableInClassList("selected-tab", isOperator);
                devices.EnableInClassList("selected-tab", !isOperator);
                scene.EnableInClassList("selected-tab", isInstance);
                cards.EnableInClassList("selected-tab", !isInstance);
                copySquad.style.display = isOperator ? DisplayStyle.Flex : DisplayStyle.None;
                squadContainer.style.display = isOperator ? DisplayStyle.Flex : DisplayStyle.None;
                selectedOperator.style.display = isOperator && selectedOperatorId.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (selectedOperatorId.Length > 0)
                {
                    var name = OperatorPresetCatalog.LoadBundled().Find(selectedOperatorId)?.Name ?? selectedOperatorId;
                    selectedOperator.text = "已选干员 · " + name + (libraryOperatorDraft ? " · 待创建" : "");
                }
                insert.text = collection.value == "characterCards" ? "加入关卡卡牌" : "插入预设";
                var stale = InspectorStore.HasChanges("preset") && !InspectorStore.IsCurrent("preset", session!.Json, Target());
                conflict.style.display = stale ? DisplayStyle.Flex : DisplayStyle.None;
                insert.SetEnabled(!HasSelection() && !stale);
                save.SetEnabled(HasSelection() && !stale);
                remove.SetEnabled(HasSelection());
                up.SetEnabled(HasSelection() && !isInstance && index.value > 0);
                down.SetEnabled(HasSelection() && !isInstance && index.value + 1 < rows.Count);
                selectedPresetCell = HasSelection() && isInstance ? PresetCell(records![index.value]) : null;
                SceneView.RepaintAll();
            }

            bool ChangeTarget(string layer, string role, int row, JObject? candidate = null, bool moveOperatorDraft = false)
            {
                if (moveOperatorDraft) RequireLocalDraft("preset", Target());
                var items = PresetRecords(layer, role);
                var initial = row >= 0 && items != null && row < items.Count
                    ? items[row].ToString(Formatting.Indented) : "{}";
                var raw = moveOperatorDraft ? json.value : candidate?.ToString(Formatting.Indented) ?? initial;
                var untouchedOperatorDraft = libraryOperatorDraft && json.value == operatorSelectionInitialJson;
                if (!ReplaceLocalDraft("preset", PresetTarget(layer, role, row), initial, raw,
                        confirmDiscard: !moveOperatorDraft && !untouchedOperatorDraft)) return false;
                var layerChanged = layer != section.value;
                var rearm = moveOperatorDraft && devicePlacementEnabled;
                libraryOperatorDraft = moveOperatorDraft;
                if (!moveOperatorDraft) operatorSelectionInitialJson = "";
                var targetPage = Math.Max(0, row) / cardsPerPage;
                if (cardPage != targetPage || layer != section.value || role != collection.value)
                    cardScroll.scrollOffset = Vector2.zero;
                cardPage = targetPage;
                devicePresetSection = layer;
                devicePresetRole = role;
                devicePresetIndex = row;
                section.SetValueWithoutNotify(layer);
                collection.SetValueWithoutNotify(role);
                index.SetValueWithoutNotify(row);
                SetDevicePlacement(rearm);
                Show(raw);
                updateDevicePlacementAvailability?.Invoke();
                RefreshRows();
                if (layerChanged || showPresetHand) RefreshPresetHand();
                return true;
            }

            bool Choose(string role, JObject record, bool fromLibrary)
            {
                RequireWorkspaceEditable();
                var count = PresetRecords(section.value, role)?.Count ?? 0;
                if (!ChangeTarget(section.value, role, count, record)) return false;
                if (fromLibrary)
                {
                    libraryOperatorDraft = true;
                    operatorSelectionInitialJson = json.value;
                    var id = (string?)record["inst"]?["characterKey"] ?? "";
                    selectedOperatorId = OperatorPresetCatalog.LoadBundled().Find(id)?.Id ?? id;
                    refreshOperatorLibrarySelection?.Invoke();
                }
                SetEraseMode(false);
                SetDevicePlacement(role == "tokenInsts" || fromLibrary && role == "characterInsts");
                updateDevicePlacementAvailability?.Invoke();
                selectWorkbenchTab?.Invoke(5);
                return true;
            }

            void SelectRole(bool character, bool instance)
            {
                var role = character ? instance ? "characterInsts" : "characterCards" :
                    instance ? "tokenInsts" : "tokenCards";
                if (role != collection.value) ChangeTarget(section.value, role, 0);
            }

            void Run(Action action)
            {
                try { action(); }
                catch (Exception error) { if (status != null) status.text = error.Message; }
            }

            section.RegisterValueChangedCallback(e => Run(() =>
            {
                section.SetValueWithoutNotify(e.previousValue);
                var row = libraryOperatorDraft ? PresetRecords(e.newValue, collection.value)?.Count ?? 0 : 0;
                ChangeTarget(e.newValue, collection.value, row, moveOperatorDraft: libraryOperatorDraft);
            }));
            collection.RegisterValueChangedCallback(e => Run(() =>
            {
                collection.SetValueWithoutNotify(e.previousValue);
                ChangeTarget(section.value, e.newValue, 0);
            }));
            void SelectRow(int row) => Run(() =>
            {
                if (row >= 0 && row != index.value && !ChangeTarget(section.value, collection.value, row)) RefreshRows();
            });
            list.onSelectionChange += _ => SelectRow(list.selectedIndex);
            listMode.clicked += () => ShowView(false);
            blockMode.clicked += () => ShowView(true);
            previousPage.clicked += () => ChangePage(-1);
            nextPage.clicked += () => ChangePage(1);
            operators.clicked += () => Run(() => SelectRole(true, IsInstanceRole(collection.value)));
            devices.clicked += () => Run(() => SelectRole(false, IsInstanceRole(collection.value)));
            scene.clicked += () => Run(() => SelectRole(IsOperatorRole(collection.value), true));
            cards.clicked += () => Run(() => SelectRole(IsOperatorRole(collection.value), false));
            addEmpty.clicked += () => Run(() => ChangeTarget(section.value, collection.value, rows.Count));
            reload.clicked += () => Run(() => ChangeTarget(section.value, collection.value, Math.Min(index.value, rows.Count)));
            copySquad.clicked += () => Run(() => ShowSquadImport(squadContainer, (role, record) => Choose(role, record, false)));
            prepareDevicePreset = prepareOperatorPreset = (role, record) => Choose(role, record, false);
            prepareLibraryOperator = (role, record) => Choose(role, record, true);
            selectHandPreset = (layer, role, row) =>
            {
                if (layer != section.value || role is not ("characterCards" or "tokenCards") ||
                    row < 0 || row >= (PresetRecords(layer, role)?.Count ?? 0)) return false;
                if ((collection.value != role || index.value != row) && !ChangeTarget(layer, role, row)) return false;
                selectWorkbenchTab?.Invoke(5);
                return true;
            };
            void ReadCurrent()
            {
                if (!InspectorStore.HasChanges("preset") && !devicePlacementEnabled)
                    ChangeTarget(section.value, collection.value, index.value < 0 ? rows.Count : Math.Min(index.value, rows.Count));
            }
            openPresetPanel = () =>
            {
                RefreshRows();
                ReadCurrent();
            };
            refreshPresets = () => { if (workbenchTab == 5) openPresetPanel(); };
            selectLastPlacedOperator = role =>
                ChangeTarget(section.value, role, (PresetRecords(section.value, role)?.Count ?? 0) - 1);
            insert.clicked += () => Run(() => Write(false));
            save.clicked += () => Run(() => Write(true));
            remove.clicked += () => Run(() =>
            {
                RequireAppliedDraft();
                if (InspectorStore.HasChanges("preset")) throw new EditorOperationRejectedException("请先保存或重新读取这条预设的草稿。");
                var next = session!.Document.DeletePredefined(section.value, collection.value, index.value);
                CommitEdit(next, "删除关卡预设", null);
                ChangeTarget(section.value, collection.value, Math.Min(index.value, rows.Count));
            });
            up.clicked += () => Run(() => Move(-1));
            down.clicked += () => Run(() => Move(1));
            RefreshRows();
            ShowView(presetCardView);
            parent.Add(panel);

            void Write(bool replace)
            {
                RequireLocalDraft("preset", Target());
                var next = session!.Document.SavePredefined(section.value, collection.value, index.value, json.value, replace);
                CommitEdit(next, replace ? "编辑关卡预设" : "添加关卡预设", null);
                AppliedLocalDraft("preset");
                libraryOperatorDraft = false;
                operatorSelectionInitialJson = "";
                SetDevicePlacement(false);
                RefreshRows();
            }

            void Move(int offset)
            {
                RequireAppliedDraft();
                if (InspectorStore.HasChanges("preset")) throw new EditorOperationRejectedException("请先保存这条卡牌的草稿。");
                var target = index.value + offset;
                var next = session!.Document.MovePredefined(section.value, collection.value, index.value, target);
                CommitEdit(next, "调整预设卡牌顺序", null);
                ChangeTarget(section.value, collection.value, target);
            }
        }

        private static bool IsInstanceRole(string role) => role is "characterInsts" or "tokenInsts";
        private static bool IsOperatorRole(string role) => role is "characterInsts" or "characterCards";
        private static string PresetSectionName(string section) => section == "hardPredefines" ? "困难预设" : "基础预设";
        private static Vector2Int? PresetCell(JToken token)
        {
            if ((token as JObject)?["position"] is not JObject position ||
                !int.TryParse(position["row"]?.ToString(), out var row) ||
                !int.TryParse(position["col"]?.ToString(), out var col)) return null;
            return new Vector2Int(col, row);
        }

        private static VisualElement PresetButtonRow(VisualElement parent)
        {
            var row = new VisualElement();
            row.AddToClassList("workbench-tabs");
            parent.Add(row);
            return row;
        }
    }
}
