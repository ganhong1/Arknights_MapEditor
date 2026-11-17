#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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
        [SerializeField] private string brushJson = "{\"tileKey\":\"tile_road\",\"heightType\":\"LOWLAND\",\"buildableType\":\"MELEE\",\"passableMask\":\"ALL\",\"playerSideMask\":\"ALL\",\"blackboard\":null,\"effects\":null}";
        [SerializeField] private bool erase;
        internal bool EraseMode => erase;

        internal void SetEraseMode(bool enabled)
        {
            SetDevicePlacement(false);
            if (erase == enabled) return;
            erase = enabled;
            workspaceOwner?.DocumentChanged(this);
        }
        private void BuildGlobalSettings(VisualElement parent)
        {
            const string key = "levelId";
            const string slot = "global:levelId";
            var panel = new VisualElement { name = "level-id-settings" };
            panel.AddToClassList("options-property");
            string ReadLevelId()
            {
                var value = session!.Document.GetSection(key);
                return value?.Type == JTokenType.String ? value.Value<string>()! : "";
            }
            var field = new TextField("关卡资源 ID · levelId")
            {
                name = "global-field-levelId", value = ReadLevelId()
            };
            BindLocalTextField(slot, field, () => key);
            panel.Add(field);
            var apply = new Button { name = "apply-level-id", text = "应用 levelId" };
            panel.Add(apply);
            var reload = new Button(() => ReloadLocalDraft(slot, key, ReadLevelId()))
                { text = "读取当前值" };
            panel.Add(reload);
            parent.Add(panel);

            void UpdateAccess()
            {
                var hasChanges = InspectorStore.HasChanges(slot);
                var stale = false;
                if (hasChanges)
                {
                    try { InspectorStore.RequireCurrent(slot, session!.Json, key); }
                    catch (InvalidOperationException) { stale = true; }
                }
                apply.SetEnabled(!WorkspaceEditLocked && hasChanges && !stale);
                reload.style.display = stale ? DisplayStyle.Flex : DisplayStyle.None;
            }
            field.RegisterValueChangedCallback(_ => UpdateAccess());
            reload.clicked += UpdateAccess;
            apply.clicked += () =>
            {
                try
                {
                    if (!InspectorStore.HasChanges(slot)) return;
                    RequireLocalDraft(slot, key);
                    CommitEdit(session!.Document.WithLevelId(field.value), "编辑 levelId", null);
                    AppliedLocalDraft(slot);
                    Refresh();
                }
                catch (Exception error) { ReportWindowFailure("document.level-id.edit", error); }
            };
            var synchronizedDocument = "";
            refreshFieldSummaries.Add(() =>
            {
                if (synchronizedDocument != session!.Json)
                {
                    if (!InspectorStore.HasChanges(slot)) RefreshRootDraftBaseline(slot, key, ReadLevelId());
                    synchronizedDocument = session.Json;
                }
                field.SetEnabled(!WorkspaceEditLocked);
                UpdateAccess();
            });
            BuildOptionsPanel(parent);
        }

        private void BuildGlobalField(VisualElement parent, string key, string title)
        {
            var panel = new Foldout { text = title + " · " + key, value = true };
            var state = new Label();
            var slot = "global:" + key;
            var synchronizedDocument = "";
            TextField field = null!;
            MusicEventPicker? audioPicker = null;
            string ReadCurrent() => session!.Document.GetSection(key)?.ToString(Formatting.Indented) ?? "";
            void UpdateState()
            {
                var value = session!.Document.GetSection(key);
                if (synchronizedDocument != session.Json)
                {
                    RefreshRootDraftBaseline(slot, key, ReadCurrent(), value == null ? "null" : null);
                    synchronizedDocument = session.Json;
                }
                state.text = value == null ? "当前文档未提供此字段" : value.Type == JTokenType.Null ? "当前值：null" : "当前值类型：" + value.Type;
                if (InspectorStore.HasChanges(slot))
                    state.text += InspectorStore.IsCurrent(slot, session.Json, key) ? " · 有未应用草稿" : " · 草稿与当前字段冲突";
                audioPicker?.SetKey(ReadEnvironmentKey(field.value));
            }
            panel.Add(state);
            field = new TextField("原始 JSON") { name = "global-field-" + key, multiline = true,
                value = ReadCurrent() };
            field.style.minHeight = key == "options" ? 110 : 45;
            BindLocalTextField(slot, field, () => key);
            UpdateState();
            refreshFieldSummaries.Add(UpdateState);
            field.RegisterValueChangedCallback(_ => UpdateState());
            if (key == "environmentSe")
            {
                audioPicker = new MusicEventPicker(false, ReadEnvironmentKey(field.value), value =>
                {
                    RequireWorkspaceEditable();
                    RequireAppliedDraft();
                    RequireLocalDraft(slot, key);
                    field.value = new JValue(value).ToString(Formatting.None);
                }, openLibrary: OpenMusicResourceLibrary, environment: true);
                panel.Add(audioPicker);
            }
            panel.Add(field);
            panel.Add(new HelpBox(key == "options" ? "保留完整 options 对象及未知成员；本版提供 JSON 编辑。" :
                "字符串使用 JSON 双引号，例如 \"事件名\"；null 与空字符串保持区别，清空 JSON 输入可移除此字段。", HelpBoxMessageType.Info));
            AddButton(panel, "读取当前值", () => { if (ReloadLocalDraft(slot, key, ReadCurrent())) UpdateState(); });
            AddButton(panel, "应用 " + key, () =>
            {
                RequireLocalDraft(slot, key);
                var document = session!.Document;
                var next = field.value.Length == 0 ? document.RemoveSection(key) : document.WithSection(key, field.value);
                var value = next.GetSection(key);
                if (key == "options" && value != null && value.Type != JTokenType.Object && value.Type != JTokenType.Null)
                    throw new FormatException("options 必须是对象或 null。");
                if (key == "environmentSe" && value != null && value.Type is not (JTokenType.String or JTokenType.Null))
                    throw new FormatException("environmentSe 必须是字符串或 null。");
                CommitEdit(next, "编辑 " + key, null);
                AppliedLocalDraft(slot);
                UpdateState();
            });
            parent.Add(panel);
        }

        private static string ReadEnvironmentKey(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            try
            {
                var value = JToken.Parse(raw);
                return value.Type == JTokenType.String ? (string?)value ?? "" : "";
            }
            catch (Newtonsoft.Json.JsonException) { return ""; }
        }

        private JObject Brush() => erase ? LevelDocument.Create(1, 1).GetCell(0, 0) : SelectedTerrain();

        private JObject SelectedTerrain()
        {
            // Fragment enters the same strict parser as an imported document.
            var checkedDoc = LevelDocument.Create(1, 1).WithSection("brush", brushJson);
            return checkedDoc.GetSection("brush") as JObject ?? throw new FormatException("地块必须是 JSON 对象。");
        }
        private void RequireAppliedDraft()
        {
            if (draft != session!.Json) throw new EditorOperationRejectedException("请先应用或撤销完整 JSON 草稿，再操作结构化编辑器。");
        }
        private void CommitEdit(LevelDocument next, string label, IEnumerable<Vector2Int>? dirty) =>
            CommitDocumentEdit(next, label, dirty);

        private void CommitDocumentEdit(LevelDocument next, string label, IEnumerable<Vector2Int>? dirty,
            WaveHistoryStep? waveStep = null, string? appliedWave = null,
            BranchHistoryStep? branchStep = null, string? appliedBranch = null)
        {
            RequireWorkspaceEditable();
            RequireAppliedDraft();
            EditSession(next, label, waveStep, appliedWave, branchStep, appliedBranch);
            Refresh(); ownedPreview?.UpdateDocument(next, dirty);
        }

        private void EditSession(LevelDocument next, string label, WaveHistoryStep? waveStep = null, string? appliedWave = null,
            BranchHistoryStep? branchStep = null, string? appliedBranch = null)
            => DiagnoseDocumentOperation(branchStep != null ? "document.branches.edit" : waveStep != null ? "document.waves.edit" : "document.edit",
                () => EditSessionCore(next, label, waveStep, appliedWave, branchStep, appliedBranch));

        private void EditSessionCore(LevelDocument next, string label, WaveHistoryStep? waveStep, string? appliedWave,
            BranchHistoryStep? branchStep, string? appliedBranch)
        {
            var before = session!.Revision;
            var previous = session.Document;
            var store = InspectorStore;
            if (waveStep == null && WaveInspectorDrafts.Saved(store).Any() && CanKeepWaveBindings(session.Document, next, true))
                waveStep = new WaveHistoryStep(WaveOrder.Fields, Array.Empty<int>(), -1);
            string? preparedJson = null;
            InspectorDraftStore? prepared = RebaseModuleDrafts(store, previous, next);
            preparedJson = prepared?.ToJson();
            if (waveStep != null && WaveInspectorDrafts.Saved(store).Any())
            {
                prepared = WaveInspectorDrafts.Rebase(prepared ?? store, session.Json, next.ToJson(), key => waveStep.Remap(key, true),
                    appliedWave, waveStep.Token(true));
                preparedJson = prepared.ToJson();
            }
            if (branchStep == null && BranchInspectorDrafts.Saved(store).Any() && CanKeepBranchBindings(session.Document, next))
                branchStep = new BranchHistoryStep(BranchHistoryKind.Fields, "");
            if (branchStep != null && BranchInspectorDrafts.Saved(store).Any())
            {
                var document = ReadBranchDocument(next);
                prepared = BranchInspectorDrafts.Rebase(prepared ?? store, session.Json, next.ToJson(),
                    key => branchStep.Remap(key, true), appliedBranch, branchStep.Token(true), branchStep.Token(false),
                    (key, initial, raw) => branchStep.RebaseFields(key, initial, raw, document));
                preparedJson = prepared.ToJson();
            }
            session.Edit(next, label, (waveStep?.MovesIndices == true || branchStep != null) && prepared != null);
            if (waveStep == null && !JToken.DeepEquals(previous.GetSection("waves"), next.GetSection("waves")))
                ClearMusicSelection(false);
            if (branchStep == null && !JToken.DeepEquals(previous.GetSection("branches"), next.GetSection("branches")))
                ClearMusicSelection(true);
            draft = session.Json;
            if (prepared != null)
            {
                inspectorStore = prepared;
                inspectorJson = preparedJson!;
            }
            if (waveStep != null) RecordWaveHistory(waveStep, before);
            if (branchStep != null) RecordBranchHistory(branchStep, before);
            NoteWaveRevision();
            NoteBranchRevision();
        }
        private void BindPreview(MapPreviewStage stage)
        {
            stage.Bind(PaintWorkspaceCells, PickWorkspaceCell);
            stage.UndoMapEdit = UndoFromShortcut;
            stage.DrawRoutePreview = DrawRoutePreview;
            stage.SelectHandPreset = SelectHandPreset;
            stage.SetPresetHand(devicePresetSection, showPresetHand, ActiveHandPreset);
        }
        private void BuildSectionPanel(VisualElement parent)
        {
            var panel = new Foldout { text = "路线、敌人、调度与原生配置", value = false };
            var keys = new List<string> { "routes", "extraRoutes", "enemyDbRefs", "waves", "branches", "predefines", "hardPredefines", "options", "runes", "optionalRunes", "globalBuffs", "mapId", "bgmEvent", "environmentSe", "cameraPlugin" };
            var key = new PopupField<string>("字段", keys, 0);
            if (InspectorStore.TryReadTarget("section", out var target) && keys.Contains(target))
                key.SetValueWithoutNotify(target);
            var text = new TextField("字段 JSON（保留未知内容）") { multiline = true };
            text.value = session!.Document.GetSection(key.value)?.ToString(Formatting.Indented) ?? "null";
            BindLocalTextField("section", text, () => key.value);
            string Read(string name) => session!.Document.GetSection(name)?.ToString(Formatting.Indented) ?? "null";
            Action load = () => ReloadLocalDraft("section", key.value, Read(key.value));
            key.RegisterValueChangedCallback(evt =>
            {
                key.SetValueWithoutNotify(evt.previousValue);
                if (ReloadLocalDraft("section", evt.newValue, Read(evt.newValue))) key.SetValueWithoutNotify(evt.newValue);
            });
            refreshFieldSummaries.Add(() =>
            {
                if (InspectorStore.HasChanges("section") || !inspectorBindings.TryGetValue("section", out var binding)) return;
                if (binding.Document != session!.Json || binding.Initial != Read(key.value))
                    ReloadLocalDraft("section", key.value, Read(key.value));
            });
            panel.Add(key); panel.Add(text);
            AddButton(panel, "读取当前字段", load);
            AddButton(panel, "校验并应用字段", () =>
            {
                RequireLocalDraft("section", key.value);
                var previous = session!.Document;
                CommitEdit(previous.WithSection(key.value, text.value), "编辑 " + key.value, null);
                AppliedLocalDraft("section");
                ReconcileRecordSelections(previous, session.Document);
                Refresh();
            });
            var index = new IntegerField("路线索引"); panel.Add(index);
            AddButton(panel, "删除路线并重写引用", () =>
            {
                if (key.value != "routes" && key.value != "extraRoutes") throw new EditorOperationRejectedException("请选择 routes 或 extraRoutes。");
                var previous = session!.Document;
                CommitEdit(previous.DeleteRoute(key.value, index.value), "删除路线", null);
                ReconcileRecordSelections(previous, session.Document);
                Refresh();
            });
            var enemy = new TextField("敌人 ID"); panel.Add(enemy);
            AddButton(panel, "删除未引用敌人", () => CommitEdit(session!.Document.DeleteEnemy(enemy.value), "删除敌人引用", null));
            panel.Add(new HelpBox("复杂字段可直接编辑原生 JSON。隐藏输入、卡牌与机制触发器需各自原生配置，不能当作普通可摆放装置。", HelpBoxMessageType.Info));
            parent.Add(panel);
        }
    }
}
