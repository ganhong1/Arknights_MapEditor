#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Editor
{
    internal static class SquadPresetImport
    {
        internal sealed class Slot
        {
            internal readonly string Label;
            internal readonly string CharacterId;
            private readonly JObject? character;
            private readonly JObject selection;

            internal Slot(string label, JObject? character, JObject selection)
            {
                Label = label;
                CharacterId = Text(character, "charId") ?? "未知干员";
                this.character = character;
                this.selection = selection;
            }

            internal JObject ToPreset(OperatorPresetCatalog catalog)
            {
                if (character == null) throw new FormatException("编队槽位缺少对应的干员记录。");
                return ConvertCharacter(character, selection, catalog);
            }
        }

        internal static IReadOnlyList<Slot> Read(string json)
        {
            var data = LevelDocument.Create(1, 1).WithSection("snapshot", json).GetSection("snapshot") as JObject
                ?? throw new FormatException("编队快照应为 JSON 对象。");
            if (Text(data, "clientVersion") is string client && client != "2.7.81")
                throw new FormatException("编队快照的客户端版本与 2.7.81 干员库不匹配。");
            var slots = data["troop"] is JObject troop ? ReadTroop(troop) :
                data["squads"] is JObject && data["chars"] is JObject ? ReadTroop(data) : ReadPrepared(data);
            if (slots.Count == 0) throw new FormatException("编队快照里没有可选槽位。");
            return slots;
        }

        private static List<Slot> ReadTroop(JObject troop)
        {
            if (troop["chars"] is not JObject characters || troop["squads"] is not JObject squads)
                throw new FormatException("编队快照需要同时提供 troop.chars 和 troop.squads。");
            var result = new List<Slot>();
            foreach (var squad in squads.Properties())
            {
                if (squad.Value is not JObject data || data["slots"] is not JArray slots) continue;
                for (var index = 0; index < slots.Count; index++)
                {
                    if (slots[index] is not JObject slot || Int(slot, "charInstId") is not int id || id <= 0) continue;
                    var character = characters[id.ToString(System.Globalization.CultureInfo.InvariantCulture)] as JObject;
                    if (Int(character, "instId") is int ownedId && ownedId != id) character = null;
                    result.Add(new Slot(SlotLabel(Text(data, "name") ?? squad.Name, index), character, slot));
                }
            }
            return result;
        }

        private static List<Slot> ReadPrepared(JObject data)
        {
            if (data["roster"] is not JArray roster || data["patches"] is not JArray patches)
                throw new FormatException("请选择包含账号干员和槽位的快照，或 BattleLab 编队快照。");
            var byId = patches.OfType<JObject>().Where(item => Text(item, "charId") != null)
                .ToDictionary(item => Text(item, "charId")!, StringComparer.Ordinal);
            var selections = data["squadSelections"] as JArray;
            if (selections != null && selections.Count != roster.Count)
                throw new FormatException("编队成员和槽位选择数量不一致。");
            var result = new List<Slot>();
            for (var index = 0; index < roster.Count; index++)
            {
                var id = (string?)roster[index] ?? "";
                byId.TryGetValue(id, out var character);
                var selection = selections?[index] as JObject;
                if (selections != null && Text(selection, "charId") != id)
                    throw new FormatException("槽位选择与编队成员 ID 不匹配。");
                result.Add(new Slot(SlotLabel(Text(data, "squadId") ?? "试跑编队", index), character, selection ?? new JObject()));
            }
            return result;
        }

        private static string SlotLabel(string squad, int index) => squad + " · 槽位 " + (index + 1);

        private static JObject ConvertCharacter(JObject character, JObject selection, OperatorPresetCatalog catalog)
        {
            var id = Text(character, "charId");
            var entry = catalog.Find(id) ?? throw new FormatException("2.7.81 干员目录中没有该角色。");
            var template = Text(selection, "currentTmpl") ?? Text(character, "currentTmpl");
            var form = catalog.Form(entry, template ?? (entry.Id == id ? null : id));
            var formId = (string)form["id"]!;
            var ownedForm = (character["tmpl"] as JObject)?[formId] as JObject;
            var slotForm = (selection["tmpl"] as JObject)?[formId] as JObject;
            if (!(bool)form["default"]! && ownedForm == null)
                throw new FormatException("快照缺少所选形态的技能、皮肤或模组记录。");
            var level = NeedInt(character, "level");
            var phase = ReadPhase(character["evolvePhase"]);
            var caps = (JArray)form["phaseCaps"]!;
            if (phase < 0 || phase >= caps.Count || level < 1 || level > (int)caps[phase]!)
                throw new FormatException("编队干员的精英阶段或等级超出该形态的范围。");
            var potential = NeedInt(character, "potentialRank");
            if (potential < 0 || potential > (int)form["potentialMax"]!)
                throw new FormatException("潜能超出该形态的范围。");
            var baseSkillLevel = NeedInt(character, "mainSkillLvl");
            var skillIndex = Int(slotForm, "skillIndex") ?? Int(selection, "skillIndex") ??
                Int(ownedForm, "defaultSkillIndex") ?? NeedInt(character, "defaultSkillIndex");
            var skillLevel = SkillLevel(form, ownedForm ?? character, skillIndex, baseSkillLevel);
            var equip = ResolveModule(form, ownedForm ?? character, slotForm ?? selection);
            var favor = catalog.FavorPhase(NeedInt(character, "favorPoint"));
            return new JObject
            {
                ["inst"] = new JObject
                {
                    ["characterKey"] = entry.Id, ["level"] = level, ["phase"] = phase,
                    ["potentialRank"] = potential, ["favorPoint"] = favor
                },
                ["skillIndex"] = skillIndex, ["mainSkillLvl"] = skillLevel,
                ["skinId"] = Text(ownedForm, "skinId") ?? Text(ownedForm, "skin") ?? Text(character, "skin"),
                ["tmplId"] = entry.Forms.Count > 1 ? formId : template,
                ["uniEquipIds"] = equip, ["hidden"] = false, ["alias"] = JValue.CreateNull()
            };
        }

        private static int SkillLevel(JObject form, JObject owned, int index, int baseLevel)
        {
            var skills = (JArray)form["skills"]!;
            if (index == -1 && skills.Count == 0) return baseLevel;
            if (index < 0 || index >= skills.Count) throw new FormatException("技能索引不属于所选形态。");
            if (owned["skills"] is not JArray progress || index >= progress.Count || progress[index] is not JObject skill)
                throw new FormatException("快照缺少所选技能的专精记录。");
            if (Text(skill, "skillId") is string id && id != (string?)skills[index]!["id"])
                throw new FormatException("所选技能与形态的技能顺序不匹配。");
            if (Int(skill, "unlock") == 0 || GetBoolean(skill, "unlock") == false)
                throw new FormatException("所选技能尚未解锁。");
            var mastery = NeedInt(skill, "specializeLevel");
            if (mastery < 0 || mastery > 3 || (mastery > 0 && baseLevel != 7) ||
                baseLevel < 1 || baseLevel + mastery > (int)skills[index]!["levelCount"]!)
                throw new FormatException("技能等级或专精超出该技能的范围。");
            return baseLevel + mastery;
        }

        private static JToken ResolveModule(JObject form, JObject owned, JObject selection)
        {
            var equipped = selection.TryGetValue("currentEquip", out var value) ? value : owned["currentEquip"];
            if (equipped?.Type is not (null or JTokenType.Null or JTokenType.String))
                throw new FormatException("编队所选模组应为 ID 或 null。");
            var id = equipped?.Type == JTokenType.String ? (string?)equipped : null;
            if (string.IsNullOrEmpty(id)) return JValue.CreateNull();
            var definition = ((JArray)form["modules"]!).OfType<JObject>().FirstOrDefault(item => Text(item, "id") == id)
                ?? throw new FormatException("所选模组不属于该形态。");
            var state = (owned["equip"] as JObject)?[id!] as JObject
                ?? throw new FormatException("快照缺少所选模组的等级记录。");
            var level = NeedInt(state, "level");
            if (Int(state, "locked") is int locked && locked != 0 || GetBoolean(state, "locked") == true ||
                level < 1 || level > (int)definition["maxLevel"]!)
                throw new FormatException("所选模组尚未解锁或等级超出范围。");
            return new JArray(new JObject { ["key"] = id, ["level"] = level });
        }

        internal static int ReadPhase(JToken? value) => value?.Type == JTokenType.Integer ? (int)value :
            value?.Type == JTokenType.String ? OperatorPresetCatalog.ParseTier((string?)value) : -1;
        private static int NeedInt(JObject? data, string name) =>
            Int(data, name) ?? throw new FormatException("快照缺少整数 " + name + "。");
        private static int? Int(JObject? data, string name) =>
            data?[name]?.Type == JTokenType.Integer ? (int?)data[name] : null;
        private static bool? GetBoolean(JObject data, string name) =>
            data[name]?.Type == JTokenType.Boolean ? (bool?)data[name] : null;
        private static string? Text(JObject? data, string name) =>
            data?[name]?.Type == JTokenType.String && !string.IsNullOrEmpty((string?)data[name]) ? (string?)data[name] : null;
    }
}
