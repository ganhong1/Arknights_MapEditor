#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal static class EnemyDefinitionForm
    {
        private static readonly string[] SpTypes =
            { "NONE", "INCREASE_WITH_TIME", "INCREASE_WHEN_ATTACK", "INCREASE_WHEN_TAKEN_DAMAGE" };

        internal static void BuildInline(VisualElement parent, JObject record, EnemyCatalog.Entry? entry,
            EnemyCatalog catalog, Action<bool> changed)
        {
            var identity = Section(parent, "身份", "enemy-inline-identity", true);
            String(identity, record, "name", "名称", changed, allowNull: true);
            String(identity, record, "description", "描述", changed, allowNull: true, multiline: true);
            String(identity, record, "key", "敌人预制体键", changed, rebuild: true);
            String(identity, record, "alias", "本关别名", changed, allowNull: true);
            var key = new Label { name = "enemy-inline-runtime-key" };
            identity.Add(key);
            void UpdateKey() => key.text = "本关引用键 · " + (EnemyDefinitionResolver.GetKeyInLevel(record) ?? "—");
            UpdateKey();
            identity.Q<TextField>("enemy-field-alias").RegisterValueChangedCallback(_ => UpdateKey());

            var battle = Section(parent, "战斗属性", "enemy-inline-battle");
            var defaults = EnemyDefinitionResolver.CreateDefaultDefinition();
            var attributes = Section(battle, "attributes · 数值属性", "enemy-inline-attributes");
            BuildState(attributes, record, "attributes", () => defaults["attributes"]!.DeepClone(), changed);
            if (record["attributes"] is JObject data)
            {
                foreach (var property in ((JObject)defaults["attributes"]!).Properties())
                    Field(attributes, data, property.Name, EnemyOverrideForm.Label(property.Name),
                        property.Value.Type, changed);
            }
            foreach (var name in new[] { "applyWay", "motion", "levelType" }) EnumValue(battle, record, name, EnemyOverrideForm.Label(name), changed);
            Tags(battle, record, changed);
            foreach (var name in new[] { "notCountInTotal", "lifePointReduce", "rangeRadius", "viewRadius", "numOfExtraDrops" })
                Field(battle, record, name, EnemyOverrideForm.Label(name), defaults[name]!.Type, changed);

            BuildParameters(parent, record, new EnemyParameterSamples(catalog, entry), null, false, changed);
        }

        internal static void BuildReference(VisualElement parent, JObject record, EnemyCatalog catalog,
            Func<JObject, JArray?> levels, Action<bool> changed)
        {
            String(parent, record, "id", "本关 ID", changed, rebuild: true);
            var mode = new PopupField<string>("定义方式", new List<string> { "数据库引用", "独立定义" },
                (bool?)record["useDb"] == false ? 1 : 0) { name = "enemy-mode" };
            parent.Add(mode);
            mode.RegisterValueChangedCallback(_ =>
            {
                try
                {
                    if (mode.index == 1 && !string.IsNullOrEmpty((string?)record["id"]))
                    {
                        var copy = EnemyDefinitionResolver.MaterializeIndependent(
                            EnemyDefinitionResolver.ResolveReference(record, levels(record)), (string)record["id"]!);
                        foreach (var property in copy.Properties()) record[property.Name] = property.Value.DeepClone();
                    }
                    else
                    {
                        record["useDb"] = mode.index == 0;
                        if (mode.index == 1 && record["overwrittenData"] is not JObject)
                            record["overwrittenData"] = new JObject();
                    }
                    changed(true);
                }
                catch (Exception error)
                {
                    mode.SetValueWithoutNotify((bool?)record["useDb"] == false ? "独立定义" : "数据库引用");
                    parent.Add(new Label(error.Message));
                }
            });
            var level = Field(parent, record, "level", "数据库等级", JTokenType.Integer, changed, rebuild: true);
            level.SetEnabled((bool?)record["useDb"] != false);
            var entry = catalog.Find((string?)record["id"]);
            if (entry != null) parent.Add(new Label("已知等级 · " + string.Join(" / ", entry.Levels)));

            var overlay = Section(parent, "覆盖数据 · overwrittenData", "enemy-overrides", true);
            BuildState(overlay, record, "overwrittenData", () => new JObject(), changed);
            JObject? inherited = null;
            if ((bool?)record["useDb"] == true && levels(record) is JArray database)
            {
                try { inherited = EnemyDefinitionResolver.ResolveDatabaseLevel((string)record["id"]!, database, (int?)record["level"] ?? 0); }
                catch (InvalidOperationException) { }
            }
            if (record["overwrittenData"] is JObject patch)
            {
                var scalar = Section(overlay, "身份与战斗属性覆盖", "enemy-scalar-overrides");
                EnemyOverrideForm.Build(scalar, patch, () => changed(true), inherited: inherited);
                var prefab = (bool?)patch["prefabKey"]?["m_defined"] == true ? (string?)patch["prefabKey"]?["m_value"] : null;
                BuildParameters(overlay, patch, new EnemyParameterSamples(catalog, catalog.Find(prefab) ?? entry),
                    inherited, true, changed);
            }
            else overlay.Add(new Label(inherited == null ? "创建覆盖对象后可编辑。" : "当前沿用数据库定义。创建覆盖对象后可修改。"));
        }

        private static void BuildParameters(VisualElement parent, JObject value, EnemyParameterSamples samples,
            JObject? inherited, bool patch, Action<bool> changed)
        {
            var talents = Section(parent, "天赋黑板 · talentBlackboard", "enemy-talents");
            BuildBlackboard(talents, value, "talentBlackboard", inherited?["talentBlackboard"] as JArray,
                samples, null, patch, changed);
            var skills = Section(parent, "技能 · skills", "enemy-skills");
            BuildSkills(skills, value, inherited?["skills"] as JArray, samples, patch, changed);
            var sp = Section(parent, "技力 · spData", "enemy-sp");
            if (patch) StateLabel(sp, "继承值", inherited?["spData"]);
            BuildState(sp, value, "spData", () => inherited?["spData"] is JObject source ? source.DeepClone() :
                new JObject { ["spType"] = "NONE", ["maxSp"] = 0, ["initSp"] = 0, ["increment"] = 1f }, changed,
                patch ? "开始覆盖" : "创建技力配置");
            if (value["spData"] is not JObject data) return;
            EnumValue(sp, data, "spType", "技力回复方式", changed, SpTypes);
            Field(sp, data, "maxSp", "技力上限", JTokenType.Integer, changed);
            Field(sp, data, "initSp", "初始技力", JTokenType.Integer, changed);
            Field(sp, data, "increment", "回复量", JTokenType.Float, changed);
        }

        private static void BuildSkills(VisualElement parent, JObject value, JArray? inherited,
            EnemyParameterSamples samples, bool patch, Action<bool> changed)
        {
            const string name = "skills";
            if (patch)
            {
                StateLabel(parent, "继承技能", inherited);
                if (inherited != null)
                    foreach (var original in inherited.OfType<JObject>())
                    {
                        var key = (string?)original["prefabKey"];
                        if (key == null || (value[name] as JArray)?.OfType<JObject>().Any(skill => (string?)skill["prefabKey"] == key) == true) continue;
                        Button(parent, "覆盖 " + key, () =>
                        {
                            if (value[name] is not JArray) value[name] = new JArray();
                            ((JArray)value[name]!).Add(original.DeepClone());
                            changed(true);
                        });
                    }
            }
            BuildState(parent, value, name, () => new JArray(), changed, "创建技能列表");
            if (value[name] is not JArray list) return;
            Button(parent, "新增技能", () =>
            {
                list.Add(new JObject { ["prefabKey"] = "", ["priority"] = 0, ["cooldown"] = 0f,
                    ["initCooldown"] = 0f, ["spCost"] = 0, ["blackboard"] = null });
                changed(true);
            });
            var skillSamples = new Foldout { text = "原版技能样例", value = false, name = "enemy-skill-samples" };
            skillSamples.RegisterValueChangedCallback(e =>
            {
                if (e.target == skillSamples && e.newValue && skillSamples.childCount == 0)
                    AddSkillSamples(skillSamples, list, samples, changed);
            });
            parent.Add(skillSamples);
            for (var i = 0; i < list.Count; i++)
            {
                var index = i;
                if (list[index] is not JObject skill)
                {
                    StateLabel(parent, $"[{index}] 原值", list[index]);
                    continue;
                }
                var key = (string?)skill["prefabKey"] ?? "";
                string Caption() => $"{index + 1}. {skill["prefabKey"]} · 优先级 {skill["priority"]} · 冷却 {skill["cooldown"]} · 初始 {skill["initCooldown"]} · SP {skill["spCost"]} · 黑板 {(skill["blackboard"] as JArray)?.Count ?? 0}";
                var fold = Section(parent, Caption(), "enemy-skill-" + i, list.Count == 1);
                if (list.OfType<JObject>().Count(item => key.Length > 0 && (string?)item["prefabKey"] == key) > 1)
                    fold.Add(new Label("该技能键重复，请核对列表顺序和覆盖结果。"));
                String(fold, skill, "prefabKey", "技能键", changed, rebuild: true);
                Field(fold, skill, "priority", "优先级", JTokenType.Integer, changed);
                Field(fold, skill, "cooldown", "冷却", JTokenType.Float, changed);
                Field(fold, skill, "initCooldown", "初始冷却", JTokenType.Float, changed);
                Field(fold, skill, "spCost", "技力消耗", JTokenType.Integer, changed);
                var board = Section(fold, "技能黑板 · blackboard", "enemy-skill-blackboard-" + i);
                var inheritedSkill = inherited?.OfType<JObject>().FirstOrDefault(item => (string?)item["prefabKey"] == key);
                if (patch)
                {
                    if (inheritedSkill != null)
                    {
                        var previous = Section(fold, "同键继承技能", "");
                        StateLabel(previous, "", inheritedSkill);
                    }
                    fold.Add(new Label("同键覆盖采用本项的技能参数。非空黑板按键合并，null 或 [] 使用本项黑板。")
                    { style = { whiteSpace = WhiteSpace.Normal } });
                }
                BuildBlackboard(board, skill, "blackboard", inheritedSkill?["blackboard"] as JArray,
                    samples, key, patch, changed);
                ListActions(fold, list, index, patch && inheritedSkill != null ? "移除覆盖" : "删除技能", changed);
                fold.RegisterCallback<ChangeEvent<int>>(_ => fold.text = Caption());
                fold.RegisterCallback<ChangeEvent<float>>(_ => fold.text = Caption());
            }
        }

        private static void AddSkillSamples(VisualElement parent, JArray list, EnemyParameterSamples samples,
            Action<bool> changed)
        {
            var choices = samples.Skills().ToArray();
            if (choices.Length == 0) { parent.Add(new Label("当前敌人暂无技能样例。")); return; }
            var menu = new PopupField<string>("配置样例", choices.Select((item, index) =>
                (index + 1) + ". " + item.Value["prefabKey"] + " · " + item.Source).ToList(), 0);
            parent.Add(menu);
            void Add(bool full)
            {
                var example = choices[menu.index].Value;
                var key = (string?)example["prefabKey"];
                var existing = list.OfType<JObject>().FirstOrDefault(skill => (string?)skill["prefabKey"] == key);
                if (existing != null)
                {
                    parent.Add(new Label("列表已有此技能键，请编辑原条目或使用「复制」。"));
                    return;
                }
                var item = full ? (JObject)example.DeepClone() : new JObject { ["prefabKey"] = key, ["priority"] = 0,
                    ["cooldown"] = 0f, ["initCooldown"] = 0f, ["spCost"] = 0, ["blackboard"] = null };
                list.Add(item);
                changed(true);
            }
            Button(parent, "仅选用技能键", () => Add(false));
            Button(parent, "复制整条配置", () => Add(true));
        }

        private static void BuildBlackboard(VisualElement parent, JObject owner, string field, JArray? inherited,
            EnemyParameterSamples samples, string? skill, bool patch, Action<bool> changed)
        {
            if (patch && inherited != null)
            {
                var inheritedFold = new Foldout { text = "继承条目 · " + inherited.Count, value = false };
                StateLabel(inheritedFold, "", inherited);
                parent.Add(inheritedFold);
            }
            BuildState(parent, owner, field, () => new JArray(), changed, "创建黑板列表");
            if (owner[field] is not JArray list) return;
            var toolbar = Row(parent);
            Button(toolbar, "新增数值", () => Add(JValue.CreateNull()));
            Button(toolbar, "新增字符串", () => Add(new JValue("")));
            void Add(JToken value)
            {
                list.Add(new JObject { ["key"] = "", ["value"] = 0f, ["valueStr"] = value });
                changed(true);
            }
            var observed = new Foldout { text = "原版键与取值", value = false,
                name = field == "talentBlackboard" ? "enemy-talent-samples" : "" };
            observed.RegisterValueChangedCallback(e =>
            {
                if (e.target != observed || !e.newValue || observed.childCount > 0) return;
                var choices = samples.Blackboard(skill).ToArray();
                if (choices.Length == 0) { observed.Add(new Label("当前范围暂无原版取值。")); return; }
                var menu = new PopupField<string>("原版条目", choices.Select((item, index) =>
                    $"{index + 1}. {item.Value["key"]} = {(item.Value["valueStr"]?.Type == JTokenType.String && !string.IsNullOrEmpty((string?)item.Value["valueStr"]) ? item.Value["valueStr"] : item.Value["value"])} · {item.Source}").ToList(), 0);
                observed.Add(menu);
                Button(observed, "复制此条目", () => { list.Add(choices[menu.index].Value.DeepClone()); changed(true); });
            });
            parent.Add(observed);
            var duplicates = list.OfType<JObject>().Where(entry => !string.IsNullOrEmpty((string?)entry["key"]))
                .GroupBy(entry => (string)entry["key"]!, StringComparer.Ordinal)
                .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
            if (duplicates.Length > 0) parent.Add(new Label("重复键 · " + string.Join(" / ", duplicates)));
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] is not JObject item) { StateLabel(parent, $"[{i}] 原值", list[i]); continue; }
                var key = (string?)item["key"];
                var fold = Section(parent, $"[{i}] {(string.IsNullOrEmpty(key) ? "未填写键名" : key)}",
                    parent.name + "-entry-" + i, list.Count == 1);
                String(fold, item, "key", "键名", changed, rebuild: true);
                Field(fold, item, "value", "数值", JTokenType.Float, changed);
                String(fold, item, "valueStr", "字符串", changed, allowNull: true, rebuild: true);
                if (item["valueStr"]?.Type == JTokenType.String && !string.IsNullOrEmpty((string?)item["valueStr"]))
                    fold.Add(new Label("此条提供字符串值。"));
                if (patch && inherited?.OfType<JObject>().Any(entry => (string?)entry["key"] == key) == true)
                    fold.Add(new Label("本条覆盖同名继承键。"));
                ListActions(fold, list, i, "删除条目", changed);
            }
        }

        private static void Tags(VisualElement parent, JObject record, Action<bool> changed)
        {
            var section = Section(parent, "敌人标签 · enemyTags", "enemy-inline-tags");
            BuildState(section, record, "enemyTags", () => new JArray(), changed);
            if (record["enemyTags"] is not JArray tags) return;
            Button(section, "新增标签", () => { tags.Add(""); changed(true); });
            for (var i = 0; i < tags.Count; i++)
            {
                var index = i;
                if (tags[index]?.Type != JTokenType.String) { StateLabel(section, $"[{index}] 原值", tags[index]); continue; }
                var input = new TextField("[" + index + "]") { isDelayed = true, value = (string)tags[index]! };
                input.RegisterValueChangedCallback(e => { tags[index] = e.newValue; changed(false); });
                section.Add(input);
                Button(section, "删除标签 " + index, () => { tags.RemoveAt(index); changed(true); });
            }
        }

        private static void EnumValue(VisualElement parent, JObject owner, string name, string label, Action<bool> changed,
            IReadOnlyList<string>? values = null)
        {
            if (values == null)
            {
                values = name switch
                {
                    "applyWay" => new[] { "NONE", "MELEE", "RANGED", "ALL" },
                    "motion" => new[] { "WALK", "FLY" },
                    "levelType" => new[] { "NORMAL", "ELITE", "BOSS" },
                    _ => Array.Empty<string>()
                };
            }
            var current = owner[name];
            if (current == null || current.Type == JTokenType.Null || !values.Contains(current.ToString()))
            {
                var raw = current?.ToString(Formatting.None) ?? "";
                var input = new TextField(label + " · " + name) { value = raw, isDelayed = true, name = "enemy-field-" + name };
                input.AddToClassList("enemy-definition-field");
                parent.Add(input);
                var error = new Label(); parent.Add(error);
                input.RegisterValueChangedCallback(e =>
                {
                    try
                    {
                        var v = LevelDocument.Create(1, 1).WithSection("enum", e.newValue).GetSection("enum")!;
                        if (v.Type != JTokenType.String && v.Type != JTokenType.Integer && v.Type != JTokenType.Null)
                            throw new FormatException("请输入字符串、整数或 null。");
                        owner[name] = v; error.text = ""; changed(false);
                    }
                    catch (Exception ex) { error.text = ex.Message; }
                });
                if (values.Count == 0) return;
                var menu = new PopupField<string>("已知值", values.ToList(), 0);
                parent.Add(menu);
                Button(parent, "采用所选值", () => { owner[name] = menu.value; changed(true); });
                return;
            }
            var dropdown = new PopupField<string>(label + " · " + name, values.ToList(), values.ToList().IndexOf(current.ToString()))
                { name = "enemy-field-" + name };
            dropdown.AddToClassList("enemy-definition-field");
            dropdown.RegisterValueChangedCallback(e => { owner[name] = e.newValue; changed(false); });
            parent.Add(dropdown);
        }

        private static VisualElement Field(VisualElement parent, JObject owner, string name, string title,
            JTokenType type, Action<bool> changed, bool rebuild = false)
        {
            var label = title + " · " + name;
            var current = owner[name];
            VisualElement field;
            switch (type)
            {
                case JTokenType.Boolean:
                    var toggle = new Toggle(label) { value = current?.Type == JTokenType.Boolean && (bool)current };
                    toggle.RegisterValueChangedCallback(e => { owner[name] = e.newValue; changed(rebuild); });
                    field = toggle; break;
                case JTokenType.Integer:
                    var integer = new IntegerField(label) { isDelayed = true,
                        value = int.TryParse(current?.ToString(), out var parsed) ? parsed : 0 };
                    integer.RegisterValueChangedCallback(e => { owner[name] = e.newValue; changed(rebuild); });
                    field = integer; break;
                default:
                    var number = new FloatField(label) { isDelayed = true, value =
                        (current?.Type is JTokenType.Float or JTokenType.Integer or JTokenType.Raw) &&
                        float.TryParse(current.ToString(Formatting.None), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsedNumber) ? parsedNumber : 0f };
                    number.RegisterValueChangedCallback(e =>
                    {
                        if (float.IsNaN(e.newValue) || float.IsInfinity(e.newValue)) { number.SetValueWithoutNotify(e.previousValue); return; }
                        owner[name] = e.newValue; changed(rebuild);
                    });
                    field = number; break;
            }
            field.name = "enemy-field-" + name;
            field.AddToClassList("enemy-definition-field");
            parent.Add(field);
            if (current == null || current.Type == JTokenType.Null)
            {
                StateLabel(parent, name, current);
                Button(parent, "填入 " + name, () =>
                {
                    owner[name] = type == JTokenType.Boolean ? new JValue(false) :
                        type == JTokenType.Integer ? new JValue(0) : new JValue(0f);
                    changed(true);
                });
            }
            else if (current.Type != type && !(type == JTokenType.Float && current.Type is JTokenType.Integer or JTokenType.Raw))
                StateLabel(parent, "原值", current);
            return field;
        }

        private static void String(VisualElement parent, JObject owner, string name, string label, Action<bool> changed,
            bool allowNull = false, bool multiline = false, bool rebuild = false)
        {
            var field = new TextField(label + " · " + name) { isDelayed = true, multiline = multiline,
                value = owner[name]?.Type == JTokenType.String ? (string)owner[name]! : "", name = "enemy-field-" + name };
            field.AddToClassList("enemy-definition-field");
            field.RegisterValueChangedCallback(e => { owner[name] = e.newValue; changed(rebuild); });
            parent.Add(field);
            if (allowNull)
            {
                var operations = Row(parent);
                if (owner[name]?.Type != JTokenType.String)
                    StateLabel(operations, name, owner[name]);
                Button(operations, "设为 null", () => { owner[name] = JValue.CreateNull(); changed(true); });
                Button(operations, "移除字段", () => { owner.Remove(name); changed(true); });
                if (owner[name]?.Type != JTokenType.String)
                    Button(operations, "填入空字符串", () => { owner[name] = ""; changed(true); });
            }
        }

        private static void BuildState(VisualElement parent, JObject owner, string name, Func<JToken> create,
            Action<bool> changed, string caption = "创建对象")
        {
            var current = owner[name];
            var state = current == null ? "未提供" : current.Type == JTokenType.Null ? "null" :
                current is JArray list ? list.Count + " 条" : current is JObject ? "对象" : current.Type.ToString();
            var tools = Row(parent);
            tools.Add(new Label(state));
            if (current == null || current.Type == JTokenType.Null)
                Button(tools, caption, () => { owner[name] = create(); changed(true); });
            else
                Button(tools, "设为 null", () => { owner[name] = JValue.CreateNull(); changed(true); });
            if (current != null)
                Button(tools, "移除字段", () => { owner.Remove(name); changed(true); });
        }

        private static void ListActions(VisualElement parent, JArray list, int index, string delete, Action<bool> changed)
        {
            var row = Row(parent);
            Button(row, "复制", () => { list.Insert(index + 1, list[index]!.DeepClone()); changed(true); });
            Button(row, delete, () => { list.RemoveAt(index); changed(true); });
            Button(row, "上移", () => Move(index - 1)).SetEnabled(index > 0);
            Button(row, "下移", () => Move(index + 1)).SetEnabled(index + 1 < list.Count);
            void Move(int next)
            {
                var item = list[index]!;
                list.RemoveAt(index);
                list.Insert(next, item);
                changed(true);
            }
        }

        private static Foldout Section(VisualElement parent, string title, string name, bool open = false)
        {
            var foldout = new Foldout { text = title, name = name, value = open };
            foldout.AddToClassList("option-field-card");
            parent.Add(foldout);
            return foldout;
        }

        private static void StateLabel(VisualElement parent, string label, JToken? value)
        {
            var detail = new Label((label.Length == 0 ? "" : label + " · ") + (value?.ToString(Formatting.None) ?? "未提供"));
            detail.AddToClassList("enemy-definition-value");
            parent.Add(detail);
        }

        private static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement();
            row.AddToClassList("enemy-parameter-row");
            parent.Add(row);
            return row;
        }

        private static Button Button(VisualElement parent, string text, Action action)
        {
            var button = new Button(action) { text = text };
            parent.Add(button);
            return button;
        }
    }
}
