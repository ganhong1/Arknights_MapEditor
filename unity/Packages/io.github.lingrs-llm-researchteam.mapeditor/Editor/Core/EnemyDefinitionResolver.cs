#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Core
{
    /// <summary>PC 2.7.81 enemy definition composition; this does not apply battle buffs or run skills.</summary>
    public static class EnemyDefinitionResolver
    {
        private static readonly string[] ScalarFields =
        {
            "name", "description", "prefabKey", "applyWay", "motion", "enemyTags", "lifePointReduce",
            "levelType", "rangeRadius", "numOfExtraDrops", "viewRadius", "notCountInTotal"
        };
        private static readonly string[] IntegerAttributes =
        {
            "maxHp", "atk", "def", "cost", "blockCnt", "respawnTime", "maxDeployCount", "massLevel", "baseForceLevel", "tauntLevel"
        };
        private static readonly string[] FloatAttributes =
        {
            "magicResistance", "moveSpeed", "attackSpeed", "baseAttackTime", "hpRecoveryPerSec", "spRecoveryPerSec",
            "epDamageResistance", "epResistance", "damageHitratePhysical", "damageHitrateMagical", "epBreakRecoverSpeed"
        };
        private static readonly string[] BooleanAttributes =
        {
            "stunImmune", "silenceImmune", "sleepImmune", "frozenImmune", "levitateImmune", "disarmedCombatImmune",
            "fearedImmune", "palsyImmune", "attractImmune", "teleportImmune", "groundBoundImmune"
        };

        public static JObject ResolveDatabaseLevel(string enemyId, JArray levels, int level)
        {
            if (string.IsNullOrEmpty(enemyId)) throw new ArgumentException("Enemy id required", nameof(enemyId));
            if (levels == null) throw new ArgumentNullException(nameof(levels));
            var entries = levels.Select(token => token as JObject ?? throw Invalid("Expected enemy level object")).ToArray();
            if (entries.Length == 0 || level >= 0 && !entries.Any(item => Integer(item["level"], "level") == level))
                throw Invalid("Enemy database level not found: " + enemyId + " / " + level);
            var result = CreateDefaultDefinition();
            result["alias"] = enemyId;
            if (level < 0) ApplyDelta(result, Object(entries[0]["enemyData"], "enemyData"));
            else
                foreach (var entry in entries)
                {
                    if (Integer(entry["level"], "level") > level) break;
                    ApplyDelta(result, Object(entry["enemyData"], "enemyData"));
                }
            return result;
        }

        public static JObject ResolveReference(JObject reference, JArray? databaseLevels)
        {
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            var useDb = Boolean(reference["useDb"], "useDb");
            var id = NullableString(reference["id"], "id");
            JObject result;
            if (useDb)
            {
                if (databaseLevels == null) throw Invalid("Enemy database source is unavailable");
                result = ResolveDatabaseLevel(id ?? "", databaseLevels, Integer(reference["level"], "level"));
                if (!IsNull(reference["overwrittenData"])) ApplyDelta(result, Object(reference["overwrittenData"], "overwrittenData"));
            }
            else
            {
                if (IsNull(reference["overwrittenData"])) throw Invalid("Independent enemy requires overwrittenData");
                result = CreateDefaultDefinition();
                ApplyDelta(result, Object(reference["overwrittenData"], "overwrittenData"));
                if (!string.IsNullOrEmpty(id) && !string.Equals(id, NullableString(result["key"], "key"), StringComparison.Ordinal))
                    result["alias"] = id;
            }
            return result;
        }

        public static string? GetKeyInLevel(JObject definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            var alias = NullableString(definition["alias"], "alias");
            return string.IsNullOrEmpty(alias) ? NullableString(definition["key"], "key") : alias;
        }

        public static JObject MaterializeIndependent(JObject resolved, string newId)
        {
            if (resolved == null) throw new ArgumentNullException(nameof(resolved));
            if (string.IsNullOrWhiteSpace(newId)) throw new ArgumentException("Independent enemy id required", nameof(newId));
            if (string.IsNullOrEmpty(NullableString(resolved["key"], "key"))) throw Invalid("Resolved enemy needs a prefab key");
            var delta = new JObject();
            foreach (var field in ScalarFields)
            {
                var source = field == "prefabKey" ? "key" : field;
                if (resolved.Property(source) == null) throw Invalid("Resolved enemy field missing: " + source);
                delta[field] = Defined(resolved[source]);
            }
            var attributes = Object(resolved["attributes"], "attributes");
            var patch = new JObject();
            foreach (var field in IntegerAttributes.Concat(FloatAttributes).Concat(BooleanAttributes))
            {
                if (attributes.Property(field) == null) throw Invalid("Resolved enemy attribute missing: " + field);
                patch[field] = Defined(attributes[field]);
            }
            delta["attributes"] = patch;
            foreach (var field in new[] { "talentBlackboard", "skills", "spData" }) delta[field] = Copy(resolved[field]);
            return new JObject { ["useDb"] = false, ["id"] = newId, ["level"] = 0, ["overwrittenData"] = delta };
        }

        private static void ApplyDelta(JObject result, JObject delta)
        {
            foreach (var field in ScalarFields)
            {
                if (!TryDefined(delta[field], field, out var value)) continue;
                ValidateScalar(field, value);
                result[field == "prefabKey" ? "key" : field] = field == "rangeRadius" || field == "viewRadius"
                    ? new JValue(Single(value, field)) : Copy(value);
            }
            if (!IsNull(delta["attributes"]))
            {
                var patch = Object(delta["attributes"], "attributes");
                var attributes = (JObject)result["attributes"]!;
                foreach (var field in IntegerAttributes.Concat(FloatAttributes).Concat(BooleanAttributes))
                {
                    if (!TryDefined(patch[field], field, out var value)) continue;
                    if (IntegerAttributes.Contains(field)) attributes[field] = Integer(value, field);
                    else if (BooleanAttributes.Contains(field)) attributes[field] = Boolean(value, field);
                    else attributes[field] = Single(value, field);
                }
            }
            if (!IsNull(delta["talentBlackboard"]))
            {
                var patch = Array(delta["talentBlackboard"], "talentBlackboard");
                if (patch.Count > 0) result["talentBlackboard"] = MergeBlackboard(result["talentBlackboard"], patch);
            }
            if (!IsNull(delta["skills"])) result["skills"] = MergeSkills(result["skills"], Array(delta["skills"], "skills"));
            if (!IsNull(delta["spData"])) result["spData"] = NormalizeSp(Object(delta["spData"], "spData"));
        }

        private static JArray MergeBlackboard(JToken? original, JArray delta)
        {
            var result = IsNull(original) ? new JArray() : (JArray)Array(original, "blackboard").DeepClone();
            foreach (var token in delta)
            {
                var pair = Object(token, "blackboard entry");
                var key = NullableString(pair["key"], "blackboard key");
                foreach (var old in result.OfType<JObject>().Where(old => NullableString(old["key"], "blackboard key") == key).ToArray()) old.Remove();
                var text = NullableString(pair["valueStr"], "valueStr");
                result.Add(new JObject
                {
                    ["key"] = key,
                    ["value"] = string.IsNullOrEmpty(text) ? Single(pair["value"] ?? new JValue(0), "blackboard value") : 0f,
                    ["valueStr"] = string.IsNullOrEmpty(text) ? null : text
                });
            }
            return result;
        }

        private static JToken MergeSkills(JToken? original, JArray delta)
        {
            var result = IsNull(original) ? new JArray() : (JArray)Array(original, "skills").DeepClone();
            foreach (var token in delta)
            {
                var patch = NormalizeSkill(Object(token, "skill"));
                var key = NullableString(patch["prefabKey"], "skill prefabKey");
                var existing = result.OfType<JObject>().FirstOrDefault(item => NullableString(item["prefabKey"], "skill prefabKey") == key);
                if (existing != null && patch["blackboard"] is JArray board && board.Count > 0)
                    patch["blackboard"] = MergeBlackboard(existing["blackboard"], board);
                foreach (var old in result.OfType<JObject>().Where(item => NullableString(item["prefabKey"], "skill prefabKey") == key).ToArray()) old.Remove();
                result.Add(patch);
            }
            return result.Count == 0 ? JValue.CreateNull() : (JToken)result;
        }

        private static JObject NormalizeSkill(JObject skill) => new JObject
        {
            ["prefabKey"] = NullableString(skill["prefabKey"], "prefabKey"),
            ["priority"] = Integer(skill["priority"] ?? new JValue(0), "priority"),
            ["cooldown"] = Single(skill["cooldown"] ?? new JValue(0), "cooldown"),
            ["initCooldown"] = Single(skill["initCooldown"] ?? new JValue(0), "initCooldown"),
            ["spCost"] = Integer(skill["spCost"] ?? new JValue(0), "spCost"),
            ["blackboard"] = IsNull(skill["blackboard"]) ? JValue.CreateNull() : Array(skill["blackboard"], "blackboard").DeepClone()
        };

        private static JObject NormalizeSp(JObject sp) => new JObject
        {
            ["spType"] = Copy(sp["spType"] ?? new JValue(0)),
            ["maxSp"] = Integer(sp["maxSp"] ?? new JValue(0), "maxSp"),
            ["initSp"] = Integer(sp["initSp"] ?? new JValue(0), "initSp"),
            ["increment"] = Single(sp["increment"] ?? new JValue(1), "increment")
        };

        private static bool TryDefined(JToken? token, string field, out JToken? value)
        {
            value = null;
            if (IsNull(token)) return false;
            var wrapper = Object(token, field);
            if (!Boolean(wrapper["m_defined"] ?? new JValue(false), field + ".m_defined")) return false;
            value = wrapper["m_value"];
            return true;
        }

        private static void ValidateScalar(string field, JToken? value)
        {
            if (field == "name" || field == "description" || field == "prefabKey") NullableString(value, field);
            else if (field == "lifePointReduce" || field == "numOfExtraDrops") Integer(value, field);
            else if (field == "rangeRadius" || field == "viewRadius") Single(value, field);
            else if (field == "notCountInTotal") Boolean(value, field);
            else if (field == "enemyTags")
            {
                if (!IsNull(value)) foreach (var tag in Array(value, field)) NullableString(tag, "enemy tag");
            }
            else if (value?.Type != JTokenType.Integer && value?.Type != JTokenType.String) throw Invalid("Expected enum value: " + field);
        }

        public static JObject CreateDefaultDefinition()
        {
            // Native constructors: EnemyData 0x182417A60, AttributesData 0x18231B650.
            var attributes = new JObject();
            foreach (var field in IntegerAttributes) attributes[field] = 0;
            foreach (var field in FloatAttributes) attributes[field] = 0f;
            foreach (var field in BooleanAttributes) attributes[field] = false;
            attributes["maxHp"] = 100;
            attributes["cost"] = 1;
            attributes["blockCnt"] = 1;
            attributes["moveSpeed"] = 1f;
            attributes["attackSpeed"] = 100f;
            attributes["baseAttackTime"] = 1f;
            attributes["respawnTime"] = 60;
            attributes["spRecoveryPerSec"] = 1f;
            attributes["maxDeployCount"] = 1;
            attributes["damageHitratePhysical"] = 1f;
            attributes["damageHitrateMagical"] = 1f;
            attributes["epBreakRecoverSpeed"] = 1f;
            return new JObject
            {
                ["name"] = null, ["description"] = null, ["key"] = null, ["attributes"] = attributes,
                ["applyWay"] = 1, ["motion"] = 0, ["enemyTags"] = null, ["notCountInTotal"] = false,
                ["alias"] = null, ["lifePointReduce"] = 1, ["rangeRadius"] = -1f, ["numOfExtraDrops"] = 0,
                ["viewRadius"] = -1f, ["levelType"] = 0, ["talentBlackboard"] = null, ["skills"] = null, ["spData"] = null
            };
        }

        private static JObject Defined(JToken? value) => new JObject { ["m_defined"] = true, ["m_value"] = Copy(value) };
        private static JToken Copy(JToken? value) => value?.DeepClone() ?? JValue.CreateNull();
        private static bool IsNull(JToken? value) => value == null || value.Type == JTokenType.Null;
        private static JObject Object(JToken? value, string name) => value as JObject ?? throw Invalid("Expected object: " + name);
        private static JArray Array(JToken? value, string name) => value as JArray ?? throw Invalid("Expected array: " + name);
        private static string? NullableString(JToken? value, string name) => IsNull(value) ? null : value!.Type == JTokenType.String ? value.Value<string>() : throw Invalid("Expected string: " + name);
        private static int Integer(JToken? value, string name)
        {
            if (value?.Type != JTokenType.Integer || !int.TryParse(value.ToString(), out var result)) throw Invalid("Expected Int32: " + name);
            return result;
        }
        private static bool Boolean(JToken? value, string name) => value?.Type == JTokenType.Boolean ? value.Value<bool>() : throw Invalid("Expected Boolean: " + name);
        private static float Single(JToken? value, string name)
        {
            if (!(value?.Type is JTokenType.Float or JTokenType.Integer or JTokenType.Raw))
                throw Invalid("Expected Single: " + name);
            if (!float.TryParse(value.ToString(Newtonsoft.Json.Formatting.None), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var result) || float.IsNaN(result) || float.IsInfinity(result))
                throw Invalid("Expected finite Single: " + name);
            return result;
        }
        private static InvalidOperationException Invalid(string message) => new InvalidOperationException(message);
    }
}
