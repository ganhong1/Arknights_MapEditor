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
            BuildOptionsPanel(parent);
        }

        private void BuildGlobalField(VisualElement parent, string key, string title)
        {
            var panel = new Foldout { text = title + " · " + key, value = true };
            var state = new Label();
            void UpdateState()
            {
                var value = session!.Document.GetSection(key);
                state.text = value == null ? "当前文档未提供此字段" : value.Type == JTokenType.Null ? "当前值：null" : "当前值类型：" + value.Type;
            }
            UpdateState();
            refreshFieldSummaries.Add(UpdateState);
            panel.Add(state);
            var field = new TextField("原始 JSON") { name = "global-field-" + key, multiline = true,
                value = session!.Document.GetSection(key)?.ToString(Formatting.Indented) ?? "null" };
            field.style.minHeight = key == "options" ? 110 : 45;
            var slot = "global:" + key;
            BindLocalTextField(slot, field, () => key);
            panel.Add(field);
            panel.Add(new HelpBox(key == "options" ? "保留完整 options 对象及未知成员；本版提供 JSON 编辑。" :
                "字符串使用 JSON 双引号，例如 \"事件名\"；null 与空字符串保持区别。此处不验证实际音频资源加载。", HelpBoxMessageType.Info));
            AddButton(panel, "读取当前值", () => ReloadLocalDraft(slot, key,
                session!.Document.GetSection(key)?.ToString(Formatting.Indented) ?? "null"));
            AddButton(panel, "应用 " + key, () =>
            {
                RequireLocalDraft(slot, key);
                var next = session!.Document.WithSection(key, field.value);
                var value = next.GetSection(key)!;
                if (key == "options" && value.Type != JTokenType.Object && value.Type != JTokenType.Null)
                    throw new FormatException("options 必须是对象或 null。");
                CommitEdit(next, "编辑 " + key, null);
                AppliedLocalDraft(slot);
            });
            parent.Add(panel);
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
            if (draft != session!.Json) throw new InvalidOperationException("请先应用或撤销完整 JSON 草稿，再操作结构化编辑器。");
        }
        private void CommitEdit(LevelDocument next, string label, IEnumerable<Vector2Int>? dirty)
        {
            RequireWorkspaceEditable();
            RequireAppliedDraft(); session!.Edit(next, label); draft = session.Json;
            Refresh(); ownedPreview?.UpdateDocument(next, dirty);
        }
        private void BindPreview(MapPreviewStage stage)
        {
            stage.Bind(PaintWorkspaceCells, PickWorkspaceCell);
            stage.DrawRoutePreview = DrawRoutePreview;
            stage.SelectHandPreset = SelectHandPreset;
            stage.SetPresetHand(devicePresetSection, showPresetHand, ActiveHandPreset);
        }
        private void BuildSectionPanel(VisualElement parent)
        {
            var panel = new Foldout { text = "路线、敌人、调度与原生配置", value = false };
            var keys = new List<string> { "routes", "extraRoutes", "enemyDbRefs", "waves", "branches", "predefines", "hardPredefines", "options", "runes", "optionalRunes", "globalBuffs", "mapId", "bgmEvent", "environmentSe", "cameraPlugin" };
            var key = new PopupField<string>("字段", keys, 0);
            var text = new TextField("字段 JSON（保留未知内容）") { multiline = true };
            text.value = session!.Document.GetSection(key.value)?.ToString(Formatting.Indented) ?? "null";
            BindLocalTextField("section", text, () => key.value);
            Action load = () => ReloadLocalDraft("section", key.value, session!.Document.GetSection(key.value)?.ToString(Formatting.Indented) ?? "null");
            panel.Add(key); panel.Add(text);
            AddButton(panel, "读取当前字段", load);
            AddButton(panel, "校验并应用字段", () => { RequireLocalDraft("section", key.value); CommitEdit(session!.Document.WithSection(key.value, text.value), "编辑 " + key.value, null); AppliedLocalDraft("section"); });
            var index = new IntegerField("路线索引"); panel.Add(index);
            AddButton(panel, "删除路线并重写引用", () => { if (key.value != "routes" && key.value != "extraRoutes") throw new InvalidOperationException("请选择 routes 或 extraRoutes。"); CommitEdit(session!.Document.DeleteRoute(key.value, index.value), "删除路线", null); load(); });
            var enemy = new TextField("敌人 ID"); panel.Add(enemy);
            AddButton(panel, "删除未引用敌人", () => { CommitEdit(session!.Document.DeleteEnemy(enemy.value), "删除敌人引用", null); load(); });
            panel.Add(new HelpBox("复杂字段可直接编辑原生 JSON。隐藏输入、卡牌与机制触发器需各自原生配置，不能当作普通可摆放装置。", HelpBoxMessageType.Info));
            parent.Add(panel);
        }
    }
}
