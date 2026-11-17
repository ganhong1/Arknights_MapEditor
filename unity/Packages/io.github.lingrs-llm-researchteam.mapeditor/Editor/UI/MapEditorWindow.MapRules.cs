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
        internal string MapRuleSceneField { get; private set; } = "";
        internal bool MapRuleSceneActive => workbenchTab == 6 && MapRuleSceneField.Length > 0;
        private readonly HashSet<string> visibleMapRules = new();
        internal bool MapRuleOverlayVisible(string field) => visibleMapRules.Contains(field);
        internal bool MapRuleErase { get; private set; }
        internal bool MapRuleRectangle { get; private set; }
        internal int MapRuleMask { get; private set; } = 3;
        internal static string MapRuleSlot(string field) => field == "blockEdges" ? "mapData:blockEdges" : field;
        private static string MapRuleTarget(string field) => field == "blockEdges" ? "mapData.blockEdges" : field;

        private void BuildMapRuleSceneTools(VisualElement parent, string field)
        {
            var box = new VisualElement(); box.AddToClassList("wave-settings"); parent.Add(box);
            box.Add(new Label("场景编辑"));
            var visible = new Toggle("显示地图规则") { value = MapRuleOverlayVisible(field), name = "maprule-overlay-" + field }; box.Add(visible);
            visible.RegisterValueChangedCallback(evt => { if (evt.newValue) visibleMapRules.Add(field); else visibleMapRules.Remove(field); SceneView.RepaintAll(); });
            if (field == "blockEdges") box.Add(new Label("灰虚线：无阻挡；橙：地面；蓝：飞行；紫：两者；红：未知掩码。"));
            var erase = new Toggle("移除规则"); box.Add(erase);
            var rectangle = new Toggle("框选格子");
            if (field != "blockEdges") box.Add(rectangle);
            var mask = new PopupField<string>("阻挡类型", new List<string> { "不阻挡 · 0", "地面 · 1", "飞行 · 2", "地面与飞行 · 3" }, 3);
            if (field == "blockEdges") box.Add(mask);
            void Options() { if (MapRuleSceneField != field) return; MapRuleErase = erase.value; MapRuleRectangle = rectangle.value; MapRuleMask = mask.index; SceneView.RepaintAll(); }
            erase.RegisterValueChangedCallback(_ => Options()); rectangle.RegisterValueChangedCallback(_ => Options()); mask.RegisterValueChangedCallback(_ => Options());
            AddButton(box, "在地图中编辑", () =>
            {
                RequireMapRuleGesture(field);
                if (workspaceOwner == null) throw new EditorOperationRejectedException("请在多地图工作台中编辑。");
                ExitRouteScene(); workspaceOwner.FocusRouteScene(this);
                MapRuleSceneField = field; visible.value = true; Options(); SceneView.RepaintAll();
            });
            AddButton(box, "结束场景编辑", ExitMapRuleScene);
            box.Add(new Label(field == "blockEdges" ? "左键点选或拖过格边；同边记录同步更新阻挡类型，移除会删除同边全部记录。Alt 导航，Esc 取消本次拖动。" : "左键点选或拖过格子；框选按起止格形成矩形，Esc 取消本次拖动。"));
        }

        internal void ExitMapRuleScene() { MapRuleSceneField = ""; SceneView.RepaintAll(); }
        internal void RequireMapRuleGesture(string field)
        {
            if (field != "blockEdges" && field != "tilesDisallowToLocate") throw new ArgumentException("未知地图规则。", nameof(field));
            var slot = MapRuleSlot(field); RequireMapDataMutation(slot);
            if (InspectorStore.HasChanges(slot)) throw new EditorOperationRejectedException("此规则有未应用草稿，请先应用或重新读取，再在场景中编辑。");
        }

        internal void ApplyMapRuleGesture(string field, IReadOnlyList<Vector3Int> entries, bool erase, int mask, LevelDocument expected)
        {
            RequireMapRuleGesture(field);
            if (!ReferenceEquals(expected, WorkspaceDocument)) throw new EditorOperationRejectedException("地图已变化，本次拖动已取消。");
            var original = WorkspaceDocument.GetMapRules(field);
            if (original != null && original.Type != JTokenType.Null && original is not JArray)
                throw new EditorOperationRejectedException("规则不是数组，请先在表单中处理原始配置。");
            if (erase && original is not JArray) return;
            var rules = EditMapRuleRows(field, original as JArray, entries, erase, mask);
            if (JToken.DeepEquals(original, rules)) return;
            var next = WorkspaceDocument.SetMapRules(field, rules.ToString(Formatting.None));
            CommitEdit(next, erase ? "移除地图规则" : "绘制地图规则", null);
            if (inspectorBindings.ContainsKey(MapRuleSlot(field)))
                ReplaceLocalDraft(MapRuleSlot(field), MapRuleTarget(field), rules.ToString(Formatting.Indented), rules.ToString(Formatting.Indented), false);
            SceneView.RepaintAll();
        }

        internal static int? RuleDirection(JToken? value) => RuleEnum(value, new[] { "UP", "RIGHT", "DOWN", "LEFT" });
        internal static int? RuleMask(JToken? value) => RuleEnum(value, new[] { "NONE", "WALK_ONLY", "FLY_ONLY", "ALL" });
        private static int? RuleEnum(JToken? value, string[] names)
        {
            var number = RuleInt(value);
            if (number is >= 0 and <= 3) return number;
            var index = value?.Type == JTokenType.String ? Array.IndexOf(names, value.Value<string>()) : -1;
            return index >= 0 ? index : (int?)null;
        }
        internal static int? RuleInt(JToken? value) => value?.Type == JTokenType.Integer && long.TryParse(value.ToString(), out var number) && number >= int.MinValue && number <= int.MaxValue ? (int)number : (int?)null;
        internal static JArray EditMapRuleRows(string field, JArray? original, IReadOnlyList<Vector3Int> entries, bool erase, int mask)
        {
            bool edges = field == "blockEdges";
            var keys = new HashSet<Vector3Int>();
            if (erase)
            {
                foreach (var entry in entries) keys.Add(MapRuleKey(edges, entry));
                return original == null ? new JArray() : new JArray(original.Where(rule =>
                    rule is not JObject obj || ReadMapRuleKey(edges, obj) is not Vector3Int key || !keys.Contains(key)));
            }

            var rows = original == null ? new JArray() : (JArray)original.DeepClone();
            var byKey = rows.OfType<JObject>().ToLookup(rule => ReadMapRuleKey(edges, rule));
            var maskName = edges ? new[] { "NONE", "WALK_ONLY", "FLY_ONLY", "ALL" }[mask] : "";
            foreach (var entry in entries)
            {
                var key = MapRuleKey(edges, entry);
                if (!keys.Add(key)) continue;
                if (byKey.Contains(key))
                {
                    // Updating a shared edge keeps all imported copies and extension members.
                    if (edges)
                        foreach (var rule in byKey[key])
                            rule["blockMask"] = rule["blockMask"]?.Type == JTokenType.String ? new JValue(maskName) : new JValue(mask);
                    continue;
                }
                var position = new JObject { ["row"] = entry.y, ["col"] = entry.x };
                rows.Add(edges ? new JObject { ["pos"] = position, ["direction"] = entry.z, ["blockMask"] = mask } : position);
            }
            return rows;
        }

        internal static bool RuleMatches(string field, JObject rule, Vector3Int cell)
        {
            bool edges = field == "blockEdges";
            return ReadMapRuleKey(edges, rule) == MapRuleKey(edges, cell);
        }

        private static Vector3Int MapRuleKey(bool edges, Vector3Int cell) =>
            edges ? EdgeKey(cell) : new Vector3Int(cell.x, cell.y, 0);

        private static Vector3Int? ReadMapRuleKey(bool edges, JObject rule)
        {
            var position = edges ? rule["pos"] as JObject : rule;
            var row = RuleInt(position?["row"]);
            var col = RuleInt(position?["col"]);
            var direction = edges ? RuleDirection(rule["direction"]) : 0;
            return row.HasValue && col.HasValue && direction.HasValue
                ? MapRuleKey(edges, new Vector3Int(col.Value, row.Value, direction.Value)) : null;
        }
        internal static Vector3Int EdgeKey(Vector3Int cell) => cell.z switch
        { 2 => new Vector3Int(cell.x, cell.y - 1, 0), 3 => new Vector3Int(cell.x - 1, cell.y, 1), _ => cell };
    }
}
