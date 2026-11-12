#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Newtonsoft.Json.Linq;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class PresetRecordForm : VisualElement
    {
        private JObject record = new();
        private string role = "";
        private readonly Action<JObject> changed;
        private static readonly string[] Directions = { "UP", "RIGHT", "DOWN", "LEFT" };

        internal PresetRecordForm(Action<JObject> changed)
        {
            this.changed = changed;
            name = "preset-record-form";
        }

        internal void Bind(JObject? value, string collection)
        {
            record = value ?? new JObject();
            role = collection;
            Clear();
            if (value == null)
            {
                Add(new HelpBox("原生 JSON 修正后可继续使用字段编辑。", HelpBoxMessageType.Warning));
                return;
            }
            var inst = record["inst"] as JObject;
            var id = (string?)inst?["characterKey"] ?? "";
            var character = role.StartsWith("character", StringComparison.Ordinal);
            Text(character ? "干员 ID" : "装置 ID", id,
                v => Update(item => Instance(item)["characterKey"] = v, true));
            if (character && id.Length != 0) BuildOperatorFields(id, inst!);
            if (role == "tokenCards")
                Integer("初始数量", (int?)record["initialCnt"] ?? 0, 0, int.MaxValue,
                    v => Update(item => item["initialCnt"] = v));
            if (role is "characterInsts" or "tokenInsts") BuildPosition();
            var hidden = new Toggle("隐藏") { value = record["hidden"]?.Type == JTokenType.Boolean && (bool)record["hidden"]! };
            hidden.RegisterValueChangedCallback(e => Update(item => item["hidden"] = e.newValue));
            Add(hidden);
            Text("别名", (string?)record["alias"] ?? "",
                v => Update(item => item["alias"] = v.Length == 0 ? JValue.CreateNull() : new JValue(v)));
        }

        private void BuildPosition()
        {
            var cell = record["position"] as JObject;
            Integer("行 row", (int?)cell?["row"] ?? -1, 0, 255, v => SetPosition("row", v));
            Integer("列 col", (int?)cell?["col"] ?? -1, 0, 255, v => SetPosition("col", v));
            var direction = record["direction"]?.ToString();
            var index = int.TryParse(direction, out var numeric) ? numeric : Array.IndexOf(Directions, direction);
            Choice("场上朝向", Directions, index, v => Update(item => item["direction"] = Directions[v]));
        }

        private void SetPosition(string key, int value) => Update(item =>
        {
            if (item["position"] is not JObject cell) item["position"] = cell = new JObject();
            cell[key] = value;
        });

        private void BuildOperatorFields(string id, JObject inst)
        {
            var catalog = OperatorPresetCatalog.LoadBundled();
            var entry = catalog.Find(id);
            if (entry == null)
            {
                Add(new HelpBox("该干员在 2.7.81 目录中没有定义，可在原生 JSON 中保留或编辑。", HelpBoxMessageType.Info));
                return;
            }
            var forms = ((JArray)catalog.Details(entry)["forms"]!).OfType<JObject>().ToArray();
            var template = (string?)record["tmplId"];
            var selected = Array.FindIndex(forms, item => string.IsNullOrEmpty(template)
                ? entry.Id == id ? (bool?)item["default"] == true : (string?)item["id"] == id
                : (string?)item["id"] == template);
            if (forms.Length > 1 || selected < 0)
                Choice("形态", forms.Select(item => OperatorLabels.Profession((string?)item["profession"]) + " · " + item["id"]),
                    selected, v => Update(item => item["tmplId"] = (string?)forms[v]["id"], true));
            if (selected < 0) return;
            var form = forms[selected];
            var caps = (JArray)form["phaseCaps"]!;
            var phase = SquadPresetImport.ReadPhase(inst["phase"]);
            Choice("精英阶段", Enumerable.Range(0, caps.Count).Select(i => "精英 " + i),
                phase, v => Update(item => Instance(item)["phase"] = v, true));
            Integer("等级", (int?)inst["level"] ?? 1, 1, phase >= 0 && phase < caps.Count ? (int)caps[phase]! : int.MaxValue,
                v => Update(item => Instance(item)["level"] = v));
            Integer("潜能", ((int?)inst["potentialRank"] ?? 0) + 1, 1, (int)form["potentialMax"]! + 1,
                v => Update(item => Instance(item)["potentialRank"] = v - 1));
            Integer("战斗信赖阶段", (int?)inst["favorPoint"] ?? 0, 0, catalog.MaximumFavorPhase,
                v => Update(item => Instance(item)["favorPoint"] = v));
            BuildSkillFields(form);
            BuildModuleFields(form, catalog);
            var skins = ((JArray)form["skins"]!).OfType<JObject>().ToArray();
            var skinId = (string?)record["skinId"];
            var skin = FindOptional(skins, "id", skinId);
            Choice("皮肤", new[] { "默认" }.Concat(skins.Select(item => $"{item["name"]} · {item["id"]}")), skin,
                v => Update(item => item["skinId"] = v == 0 ? JValue.CreateNull() : skins[v - 1]["id"]!.DeepClone()));
        }

        private void BuildSkillFields(JObject form)
        {
            var skills = ((JArray)form["skills"]!).OfType<JObject>().ToArray();
            if (skills.Length == 0) return;
            var skill = (int?)record["skillIndex"] ?? -1;
            Choice("出战技能", skills.Select((item, i) => $"技能 {i + 1} · {item["name"]}"), skill,
                v => Update(item => item["skillIndex"] = v, true));
            if (skill < 0 || skill >= skills.Length) return;
            var effective = record["mainSkillLvl"];
            var selected = ReadLevel(effective);
            var cap = Math.Min(10, (int)skills[skill]["levelCount"]!);
            Choice("技能等级", Enumerable.Range(1, cap).Select(level => OperatorLabels.Mastery(level) ?? $"{level}级"),
                selected - 1, v => Update(item => item["mainSkillLvl"] = v + 1),
                CurrentLevelLabel(effective)).name = "preset-skill-level";
        }

        private void BuildModuleFields(JObject form, OperatorPresetCatalog catalog)
        {
            if (record["uniEquipIds"] is JArray multiple && multiple.Count > 1)
            {
                Add(new Label("多个模组 · 在原生 JSON 中编辑"));
                return;
            }
            var modules = ((JArray)form["modules"]!).OfType<JObject>().ToArray();
            var equipped = (record["uniEquipIds"] as JArray)?.OfType<JObject>().FirstOrDefault();
            var id = (string?)equipped?["key"];
            var selected = FindOptional(modules, "id", id);
            var selection = Choice("模组", new[] { "无" }.Concat(modules.Select(item =>
                    $"{catalog.ModuleLabel((string)item["id"]!)} · {item["name"]}")), selected,
                v => Update(item => item["uniEquipIds"] = v == 0 ? JValue.CreateNull() :
                    new JArray(new JObject { ["key"] = modules[v - 1]["id"]!.DeepClone(), ["level"] = 1 }), true), id);
            selection.name = "preset-module";
            selection.tooltip = id;
            if (selected > 0)
            {
                var level = equipped?["level"];
                var cap = (int)modules[selected - 1]["maxLevel"]!;
                Choice("模组等级", Enumerable.Range(1, cap).Select(value => $"{value}级"),
                    ReadLevel(level) - 1, v => Update(item => item["uniEquipIds"]![0]!["level"] = v + 1),
                    CurrentLevelLabel(level)).name = "preset-module-level";
            }
        }

        private static int ReadLevel(JToken? value) =>
            value?.Type == JTokenType.Integer && int.TryParse(value.ToString(), out var level) && level > 0 ? level : 0;

        private static string? CurrentLevelLabel(JToken? value) =>
            value?.Type == JTokenType.Integer ? $"当前值 · {value}" : null;

        private static JObject Instance(JObject item)
        {
            if (item["inst"] is not JObject inst) item["inst"] = inst = new JObject();
            return inst;
        }

        private static int FindOptional(JObject[] entries, string key, string? value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            var index = Array.FindIndex(entries, item => (string?)item[key] == value);
            return index < 0 ? -1 : index + 1;
        }

        private void Text(string label, string value, Action<string> edit)
        {
            var field = new TextField(label) { value = value, isDelayed = true };
            field.RegisterValueChangedCallback(e => edit(e.newValue));
            Add(field);
        }

        private void Integer(string label, int value, int min, int max, Action<int> edit)
        {
            var field = new IntegerField(label) { value = value, isDelayed = true };
            field.tooltip = max == int.MaxValue ? $"至少 {min}" : $"范围 {min}–{max}";
            field.RegisterValueChangedCallback(e =>
            {
                if (e.newValue < min || e.newValue > max) field.SetValueWithoutNotify(e.previousValue);
                else edit(e.newValue);
            });
            Add(field);
        }

        private PopupField<string> Choice(string label, IEnumerable<string> labels, int selected, Action<int> edit,
            string? currentLabel = null)
        {
            var choices = labels.ToList();
            var placeholder = selected < 0 || selected >= choices.Count;
            if (placeholder) choices.Insert(0, currentLabel ?? "请选择");
            var field = new PopupField<string>(label, choices, placeholder ? 0 : selected);
            field.RegisterValueChangedCallback(e =>
            {
                var index = choices.IndexOf(e.newValue) - (placeholder ? 1 : 0);
                if (index >= 0) edit(index);
            });
            Add(field);
            return field;
        }

        private void Update(Action<JObject> apply, bool rebuild = false)
        {
            var next = (JObject)record.DeepClone();
            apply(next);
            record = next;
            changed(next);
            if (rebuild) Bind(next, role);
        }
    }

    internal static class OperatorLabels
    {
        internal static string? Mastery(int level) => level switch
        {
            8 => "专一", 9 => "专二", 10 => "专三", _ => null
        };

        internal static string Profession(string? value) => value switch
        {
            "PIONEER" => "先锋", "WARRIOR" => "近卫", "SNIPER" => "狙击", "CASTER" => "术师",
            "TANK" => "重装", "MEDIC" => "医疗", "SUPPORT" => "辅助", "SPECIAL" => "特种",
            _ => value ?? ""
        };
    }
}
