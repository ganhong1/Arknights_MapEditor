#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class OperatorPresetDetail : VisualElement
    {
        private readonly OperatorPresetCatalog catalog;
        private readonly Func<string, JObject, bool> choose;
        private readonly Label title = new();
        private readonly ScrollView body = new();
        private readonly Button close;
        private Button? opener;

        internal OperatorPresetDetail(OperatorPresetCatalog catalog, Func<string, JObject, bool> choose)
        {
            this.catalog = catalog;
            this.choose = choose;
            name = "operator-detail-overlay";
            AddToClassList("terrain-detail-overlay");
            style.display = DisplayStyle.None;
            var panel = new VisualElement();
            panel.AddToClassList("terrain-detail-panel");
            var header = new VisualElement();
            header.AddToClassList("terrain-detail-header");
            title.AddToClassList("workbench-heading");
            header.Add(title);
            close = new Button(Close) { text = "关闭" };
            header.Add(close);
            panel.Add(header);
            body.AddToClassList("terrain-detail-body");
            panel.Add(body);
            Add(panel);
            RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Escape) return;
                e.StopPropagation();
                Close();
            }, TrickleDown.TrickleDown);
        }

        internal void Open(OperatorPresetCatalog.Entry entry, Button trigger)
        {
            opener = trigger;
            title.text = entry.Name;
            body.Clear();
            body.scrollOffset = Vector2.zero;
            try
            {
                var details = catalog.Details(entry);
                BuildProfile(entry, details);
                var tabs = new VisualElement();
                tabs.AddToClassList("workbench-tabs");
                body.Add(tabs);
                var rolePage = new VisualElement();
                body.Add(rolePage);
                var instance = new Button { text = $"场上样例 · {entry.Instances}", name = "operator-role-instance" };
                var cards = new Button { text = $"卡牌样例 · {entry.Cards}", name = "operator-role-card" };
                tabs.Add(instance);
                tabs.Add(cards);
                void Select(bool isInstance)
                {
                    instance.EnableInClassList("selected-tab", isInstance);
                    cards.EnableInClassList("selected-tab", !isInstance);
                    rolePage.Clear();
                    var role = isInstance ? "characterInsts" : "characterCards";
                    BuildRole(rolePage, entry, role, (JArray)details[role]!);
                }
                instance.clicked += () => Select(true);
                cards.clicked += () => Select(false);
                Select(entry.Instances > 0 || entry.Cards == 0);
            }
            catch (Exception error) { body.Add(new HelpBox(error.Message, HelpBoxMessageType.Error)); }
            style.display = DisplayStyle.Flex;
            close.Focus();
        }

        private void BuildProfile(OperatorPresetCatalog.Entry entry, JObject details)
        {
            var summary = new VisualElement();
            summary.AddToClassList("terrain-detail-header");
            if (PrtsObjectView.TryGetOperatorAvatar(entry.Id, out var texture, out var rect))
            {
                var image = new Image { image = texture, uv = rect, scaleMode = ScaleMode.ScaleToFit };
                image.AddToClassList("operator-detail-avatar");
                summary.Add(image);
            }
            var profile = new VisualElement();
            profile.style.flexGrow = 1;
            profile.style.flexShrink = 1;
            AddProfileText(profile, $"{entry.Id} · {entry.Rarity}★");
            var description = (string?)details["description"];
            if (!string.IsNullOrWhiteSpace(description)) AddProfileText(profile, description!);
            summary.Add(profile);
            body.Add(summary);

            var options = new Foldout { text = "干员详情", value = true, name = "operator-profile" };
            body.Add(options);
            var forms = ((JArray)details["forms"]!).OfType<JObject>().ToArray();
            var values = forms.Select(form => FormName(form) + " · " + form["id"]).ToList();
            var selected = Math.Max(0, Array.FindIndex(forms, form => (bool?)form["default"] == true));
            var info = new VisualElement();
            if (forms.Length > 1)
            {
                var selector = new PopupField<string>("查看形态", values, selected) { name = "operator-profile-form" };
                selector.RegisterValueChangedCallback(_ => FillProfile(info, forms[selector.index]));
                options.Add(selector);
            }
            options.Add(info);
            FillProfile(info, forms[selected]);
        }

        private static void FillProfile(VisualElement parent, JObject form)
        {
            parent.Clear();
            var position = (string?)form["position"] switch
            {
                "MELEE" => "近战位", "RANGED" => "远程位", "ALL" => "近战／远程位", _ => ""
            };
            var tags = ((JArray?)form["tags"])?.Values<string>().ToArray() ?? Array.Empty<string>();
            AddProfileText(parent, string.Join(" · ", new[] { FormName(form), position }.Concat(tags).Where(text => !string.IsNullOrEmpty(text))));
            var caps = ((JArray)form["phaseCaps"]!).Select((cap, index) => $"精英 {index} · {cap} 级");
            AddProfileText(parent, "等级上限　" + string.Join("　", caps));
            var skills = ((JArray)form["skills"]!).OfType<JObject>().ToArray();
            var skillInfo = new Foldout { text = $"技能 · {skills.Length}", value = false, name = "operator-profile-skills" };
            for (var i = 0; i < skills.Length; i++)
            {
                var skill = skills[i];
                var unlock = skill["unlock"] as JObject;
                var unlockText = unlock == null ? "" : $" · 精英 {SquadPresetImport.ReadPhase(unlock["phase"])} {unlock["level"]} 级解锁";
                AddProfileText(skillInfo, $"{i + 1}. {skill["name"]}{unlockText} · 最高 {skill["levelCount"]} 级");
            }
            parent.Add(skillInfo);
            var modules = ((JArray)form["modules"]!).OfType<JObject>().ToArray();
            var moduleInfo = new Foldout { text = $"模组 · {modules.Length}", value = false };
            foreach (var module in modules)
                AddProfileText(moduleInfo, $"{module["name"]} · {module["id"]} · 最高 {module["maxLevel"]} 级");
            parent.Add(moduleInfo);
            var skins = ((JArray)form["skins"]!).OfType<JObject>().ToArray();
            var skinInfo = new Foldout { text = $"皮肤 · {skins.Length}", value = false };
            foreach (var skin in skins)
                AddProfileText(skinInfo, (string?)skin["name"] ?? (string)skin["id"]!);
            parent.Add(skinInfo);
        }

        private static string FormName(JObject form)
        {
            var profession = OperatorLabels.Profession((string?)form["profession"]);
            var branch = (string?)form["subProfession"];
            return string.IsNullOrEmpty(branch) ? profession : profession + " · " + branch;
        }

        private static void AddProfileText(VisualElement parent, string value)
        {
            var label = new Label(value);
            label.AddToClassList("terrain-detail-text");
            parent.Add(label);
        }

        private void BuildRole(VisualElement parent, OperatorPresetCatalog.Entry entry, string role, JArray samples)
        {
            parent.Add(new Button(() => Choose(role, CreateBlank(entry, catalog)))
            { text = "按干员定义新建", name = "operator-blank-preset" });
            if (samples.Count == 0)
            {
                parent.Add(new Label("暂无该用法的原版样例"));
                return;
            }
            var names = samples.Select((sample, i) =>
            {
                var record = (JObject)sample["record"]!;
                return $"样例 {i + 1} · 等级 {record["inst"]?["level"]} · 技能等级 {record["mainSkillLvl"]} · {sample["origins"]!.Count()} 份记录";
            }).ToList();
            var selection = new PopupField<string>("原版配置", names, 0) { name = "operator-sample" };
            parent.Add(selection);
            parent.Add(new Button(() => Choose(role, (JObject)samples[selection.index]!["record"]!.DeepClone()))
            { text = "使用此样例", name = "operator-use-sample" });
            var parameters = new VisualElement { name = "operator-sample-parameters" };
            parameters.AddToClassList("operator-sample-parameters");
            parent.Add(parameters);
            var rawFoldout = new Foldout { text = "查看原版 JSON", value = false };
            var raw = new TextField { multiline = true, isReadOnly = true, name = "operator-sample-json" };
            rawFoldout.Add(raw);
            parent.Add(rawFoldout);
            var origin = new Foldout { text = "查看出处", value = false, name = "operator-sample-origins" };
            parent.Add(origin);

            void ShowOrigins()
            {
                origin.Clear();
                if (!origin.value) return;
                try
                {
                    var labels = ((JArray)samples[selection.index]!["origins"]!).OfType<JObject>()
                        .SelectMany(catalog.SourceLabels).Distinct().ToList();
                    var list = new ListView(labels, 45, () =>
                    {
                        var item = new Label();
                        item.style.whiteSpace = WhiteSpace.Normal;
                        return item;
                    }, (element, index) =>
                    {
                        ((Label)element).text = labels[index];
                        element.tooltip = labels[index];
                    }) { selectionType = SelectionType.None };
                    list.style.height = 170;
                    origin.Add(list);
                }
                catch (Exception error) { origin.Add(new HelpBox(error.Message, HelpBoxMessageType.Error)); }
            }
            void Show()
            {
                var sample = (JObject)samples[selection.index]!["record"]!;
                ShowParameters(parameters, sample, entry);
                raw.SetValueWithoutNotify(sample.ToString(Formatting.Indented));
                ShowOrigins();
            }
            selection.RegisterValueChangedCallback(_ => Show());
            origin.RegisterValueChangedCallback(e => { if (e.target == origin) ShowOrigins(); });
            Show();
        }

        private void ShowParameters(VisualElement panel, JObject sample, OperatorPresetCatalog.Entry entry)
        {
            panel.Clear();
            var heading = new Label("配置参数");
            heading.AddToClassList("operator-sample-heading");
            panel.Add(heading);
            var inst = sample["inst"] as JObject;
            var form = FindSampleForm(sample, entry);
            if (inst != null)
                foreach (var property in inst.Properties())
                    AddParameter(panel, "inst." + property.Name, property.Name, property.Value, form);
            var ordered = sample.Properties().Where(property => inst == null || property.Name != "inst")
                .OrderBy(property => property.Name switch
                {
                    "skillIndex" => 0, "mainSkillLvl" => 1, "uniEquipIds" => 2, "skinId" => 3, _ => 4
                });
            foreach (var property in ordered)
                AddParameter(panel, property.Name, property.Name, property.Value, form);
        }

        private JObject? FindSampleForm(JObject record, OperatorPresetCatalog.Entry entry)
        {
            var id = (string?)record["inst"]?["characterKey"];
            var template = (string?)record["tmplId"];
            var formId = !string.IsNullOrEmpty(template) ? template : entry.Id == id ? null : id;
            return ((JArray)catalog.Details(entry)["forms"]!).OfType<JObject>()
                .FirstOrDefault(form => formId == null ? (bool?)form["default"] == true : (string?)form["id"] == formId);
        }

        private static void AddParameter(VisualElement parent, string path, string key, JToken value, JObject? form)
        {
            var title = path switch
            {
                "inst.characterKey" => "干员 ID",
                "inst.phase" => "精英阶段",
                "inst.level" => "等级",
                "inst.potentialRank" => "潜能",
                "inst.favorPoint" => "信赖参数",
                "inst.favorBattlePhase" => "战斗信赖阶段",
                "inst.playerInstId" => "编队实例 ID",
                "tmplId" => "形态模板",
                "skillIndex" => "出战技能",
                "mainSkillLvl" => "技能等级",
                "uniEquipIds" => "模组",
                "skinId" => "皮肤",
                "hidden" => "隐藏",
                "alias" => "别名",
                "showSpIllust" => "显示特殊立绘",
                "masterInfos" => "专精配置",
                "overrideSkillBlackboard" => "技能黑板覆盖",
                "overrideTalents" => "天赋覆盖",
                _ => key
            };
            var elementName = "operator-sample-param-" + path;
            if (value is JContainer container && container.HasValues)
            {
                var group = new Foldout { text = $"{title} · {container.Count}", name = elementName, tooltip = path,
                    value = path == "uniEquipIds" || path.StartsWith("uniEquipIds.", StringComparison.Ordinal) };
                if (container is JObject obj)
                    foreach (var property in obj.Properties())
                        AddParameter(group, path + "." + property.Name, property.Name, property.Value, form);
                if (container is JArray array)
                    for (var i = 0; i < array.Count; i++)
                        AddParameter(group, $"{path}.{i}", $"[{i + 1}]", array[i]!, form);
                parent.Add(group);
                return;
            }
            var raw = value.ToString(Formatting.None);
            var row = new VisualElement { name = elementName, tooltip = path + " = " + raw };
            row.AddToClassList("operator-sample-param");
            var label = new Label(title);
            label.AddToClassList("operator-sample-key");
            row.Add(label);
            var formatted = new Label(ParameterValue(path, value, raw, form));
            formatted.AddToClassList("operator-sample-value");
            row.Add(formatted);
            parent.Add(row);
        }

        private static string ParameterValue(string path, JToken value, string raw, JObject? form)
        {
            if (path == "inst.phase")
            {
                var phase = SquadPresetImport.ReadPhase(value);
                if (phase >= 0) return $"精英 {phase}";
            }
            if (path == "inst.potentialRank" && value.Type == JTokenType.Integer)
                return ((int)value + 1).ToString();
            if (path == "skillIndex" && value.Type == JTokenType.Integer)
            {
                var index = (int)value;
                if (index < 0) return "无技能";
                var skills = form?["skills"] as JArray;
                var name = skills != null && index < skills.Count ? (string?)skills[index]?["name"] : null;
                return name == null ? $"技能 {index + 1}" : $"技能 {index + 1} · {name}";
            }
            if (value.Type != JTokenType.String || string.IsNullOrEmpty((string?)value)) return raw;
            var id = (string)value!;
            var definitions = path == "skinId" ? form?["skins"] :
                path.StartsWith("uniEquipIds.", StringComparison.Ordinal) && path.EndsWith(".key", StringComparison.Ordinal)
                    ? form?["modules"] : null;
            var display = (definitions as JArray)?.OfType<JObject>()
                .FirstOrDefault(item => (string?)item["id"] == id)?["name"]?.Value<string>();
            return string.IsNullOrEmpty(display) ? id : $"{display} · {id}";
        }

        internal static JObject CreateBlank(OperatorPresetCatalog.Entry entry, OperatorPresetCatalog catalog)
        {
            var form = catalog.Form(entry, null);
            return new JObject
            {
                ["inst"] = new JObject { ["characterKey"] = entry.Id, ["phase"] = 0, ["level"] = 1,
                    ["potentialRank"] = 0, ["favorPoint"] = 0 },
                ["tmplId"] = entry.Forms.Count > 1 ? form["id"]!.DeepClone() : JValue.CreateNull(),
                ["skillIndex"] = form["skills"]!.Any() ? 0 : -1, ["mainSkillLvl"] = 1,
                ["hidden"] = false, ["alias"] = JValue.CreateNull(),
                ["uniEquipIds"] = JValue.CreateNull(), ["skinId"] = JValue.CreateNull()
            };
        }

        internal static string Describe(JObject record, OperatorPresetCatalog catalog)
        {
            var inst = (JObject)record["inst"]!;
            var id = (string?)inst["characterKey"];
            var entry = catalog.Find(id);
            var name = entry?.Name ?? id ?? "未知干员";
            var skillIndex = (int?)record["skillIndex"] ?? -1;
            var skill = skillIndex >= 0 ? $"技能 {skillIndex + 1}" : "无技能";
            var template = (string?)record["tmplId"];
            if (entry != null)
            {
                var forms = ((JArray)catalog.Details(entry)["forms"]!).OfType<JObject>();
                var formId = template ?? (entry.Id == id ? null : id);
                var form = forms.FirstOrDefault(item => formId == null ? (bool?)item["default"] == true : (string?)item["id"] == formId);
                if (form?["skills"] is JArray skills && skillIndex >= 0 && skillIndex < skills.Count)
                    skill = (string?)skills[skillIndex]!["name"] ?? skill;
            }
            var modules = (record["uniEquipIds"] as JArray)?.OfType<JObject>()
                .Select(module => $"{module["key"]} · 等级 {module["level"]}").ToArray();
            return $"{name}{(string.IsNullOrEmpty(template) ? "" : " · " + template)}\n" +
                $"精英 {SquadPresetImport.ReadPhase(inst["phase"])} · 等级 {inst["level"]} · 潜能 {((int?)inst["potentialRank"] ?? 0) + 1} · 战斗信赖 {inst["favorPoint"]}\n" +
                $"{skill} · 技能等级 {record["mainSkillLvl"]} · 模组 {(modules == null || modules.Length == 0 ? "无" : string.Join("、", modules))}";
        }

        private void Choose(string role, JObject record)
        {
            try { if (choose(role, record)) Close(); }
            catch (Exception error) { body.Add(new HelpBox(error.Message, HelpBoxMessageType.Error)); }
        }

        private void Close()
        {
            style.display = DisplayStyle.None;
            opener?.Focus();
            opener = null;
        }
    }
}
