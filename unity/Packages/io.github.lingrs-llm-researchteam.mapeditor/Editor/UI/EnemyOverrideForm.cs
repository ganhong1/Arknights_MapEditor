#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal static class EnemyOverrideForm
    {
        private static readonly string[] Fields = { "name", "description", "prefabKey", "attributes", "applyWay", "motion", "enemyTags", "lifePointReduce", "levelType", "rangeRadius", "numOfExtraDrops", "viewRadius", "notCountInTotal", "talentBlackboard", "skills", "spData" };
        private static readonly string[] Attributes = { "maxHp", "atk", "def", "magicResistance", "cost", "blockCnt", "moveSpeed", "attackSpeed", "baseAttackTime", "respawnTime", "hpRecoveryPerSec", "spRecoveryPerSec", "maxDeployCount", "massLevel", "baseForceLevel", "tauntLevel", "epDamageResistance", "epResistance", "damageHitratePhysical", "damageHitrateMagical", "epBreakRecoverSpeed", "stunImmune", "silenceImmune", "sleepImmune", "frozenImmune", "levitateImmune", "disarmedCombatImmune", "fearedImmune", "palsyImmune", "attractImmune", "teleportImmune", "groundBoundImmune" };

        internal static void Build(VisualElement parent, JObject patch, Action changed, bool attributes = false, JObject? inherited = null)
        {
            var fields = attributes ? Attributes : Fields.Where(field => !IsParameter(field)).ToArray();
            var add = new PopupField<string>("添加字段", fields.ToList(), 0)
            {
                formatListItemCallback = key => Label(key) + " · " + key,
                formatSelectedValueCallback = key => Label(key) + " · " + key
            };
            parent.Add(add);
            parent.Add(new Button(() =>
            {
                var name = add.value;
                if (patch.Property(name) == null)
                {
                    if (name == "attributes") patch[name] = new JObject();
                    else if (name == "skills" || name == "talentBlackboard") patch[name] = new JArray();
                    else if (name == "spData") patch[name] = new JObject();
                    else patch[name] = new JObject { ["m_defined"] = false, ["m_value"] = Default(name) };
                    changed();
                }
            }) { text = "添加所选字段" });
            foreach (var property in patch.Properties().ToArray())
            {
                if (!attributes && IsParameter(property.Name)) continue;
                var row = new Foldout { text = Label(property.Name) + " · " + property.Name, value = true };
                row.AddToClassList("option-field-card"); parent.Add(row);
                if (property.Name == "attributes" && property.Value is JObject child)
                    Build(row, child, changed, true, inherited?["attributes"] as JObject);
                else if (property.Value is JObject wrapper && wrapper.Property("m_defined") != null)
                {
                    var source = inherited?[property.Name == "prefabKey" ? "key" : property.Name];
                    if (source != null) row.Add(new Label("继承值 · " + source.ToString(Formatting.None))
                        { style = { whiteSpace = WhiteSpace.Normal } });
                    var defined = new Toggle("显式覆盖 · m_defined") { value = (bool?)wrapper["m_defined"] ?? false };
                    row.Add(defined);
                    var value = new TextField("m_value · JSON") { value = wrapper["m_value"]?.ToString(Formatting.None) ?? "null", isDelayed = true };
                    value.SetEnabled(defined.value); row.Add(value);
                    var error = new Label(); row.Add(error);
                    defined.RegisterValueChangedCallback(e => { wrapper["m_defined"] = e.newValue; changed(); });
                    BindValue(value, error, token => wrapper["m_value"] = token, changed);
                }
                else
                {
                    var value = new TextField("JSON") { multiline = true, isDelayed = true, value = property.Value.ToString(Formatting.Indented) };
                    var error = new Label(); row.Add(value); row.Add(error);
                    BindValue(value, error, token => property.Value = token, changed);
                }
                row.Add(new Button(() => { property.Remove(); changed(); }) { text = "移除此字段" });
            }
        }

        private static void BindValue(TextField field, Label error, Action<JToken> setValue, Action changed)
        {
            field.RegisterValueChangedCallback(evt =>
            {
                JToken parsed;
                try
                {
                    parsed = LevelDocument.ParseValueFragment(evt.newValue);
                }
                catch (LevelDocumentException exception)
                {
                    error.text = exception.Message;
                    return;
                }
                error.text = "";
                setValue(parsed);
                changed();
            });
        }

        private static bool IsParameter(string name) => name is "skills" or "talentBlackboard" or "spData";

        internal static string Label(string name) => name switch
        {
            "name" => "名称", "description" => "描述", "prefabKey" => "预制体键", "attributes" => "数值属性",
            "applyWay" => "攻击方式", "motion" => "行动方式", "enemyTags" => "敌人标签", "levelType" => "地位",
            "notCountInTotal" => "不计入敌人总数", "lifePointReduce" => "扣除生命", "rangeRadius" => "攻击范围",
            "viewRadius" => "视野", "numOfExtraDrops" => "额外掉落", "maxHp" => "生命值", "atk" => "攻击力",
            "def" => "防御力", "magicResistance" => "法术抗性", "moveSpeed" => "移动速度",
            "attackSpeed" => "攻击速度", "baseAttackTime" => "基础攻击间隔", "epDamageResistance" => "元素抗性",
            "epResistance" => "损伤抵抗", "cost" => "费用", "blockCnt" => "阻挡数", "massLevel" => "重量等级",
            "baseForceLevel" => "基础力度", "tauntLevel" => "嘲讽等级", _ => name
        };

        private static JToken Default(string name)
        {
            if (name.EndsWith("Immune", StringComparison.Ordinal) || name == "notCountInTotal") return new JValue(false);
            if (name == "name" || name == "description" || name == "prefabKey") return JValue.CreateNull();
            return new JValue(0);
        }
    }
}
