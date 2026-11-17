#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Core
{
    public sealed partial class LevelDocument
    {
        public JArray GetEnemyReferences() => root["enemyDbRefs"] is JArray list
            ? (JArray)list.DeepClone() : new JArray();

        public JArray GetInlineEnemies() => root["enemies"] is JArray list ? (JArray)list.DeepClone() : new JArray();

        public static JObject ParseEnemyDefinition(string json) => (JObject)ParseObjectFragment(json).DeepClone();

        public LevelDocument PutInlineEnemy(int index, string json)
        {
            var entry = ParseObjectFragment(json);
            if (entry["key"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace(entry["key"]!.Value<string>()))
                throw Error("Inline enemy needs a prefab key", "$.enemies");
            var id = EnemyDefinitionResolver.GetKeyInLevel(entry)!;
            if (string.IsNullOrWhiteSpace(id)) throw Error("Inline enemy identity is required", "$.enemies");
            var copy = (JObject)root.DeepClone();
            if (copy["enemies"] == null || copy["enemies"]!.Type == JTokenType.Null) copy["enemies"] = new JArray();
            var list = RequiredArray(copy["enemies"], "$.enemies");
            if (index < -1 || index >= list.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (HasEnemyIdentity(copy, id, index))
                throw Error("Enemy identity already exists", "$.enemies");
            ValidateInlineFields(entry, index >= 0 ? list[index] as JObject : null);
            if (index < 0) list.Add(entry);
            else
            {
                var previous = InlineIdentity(list[index]);
                if (previous != null && previous != id)
                {
                    if (HasEnemyIdentity(copy, previous, index))
                        throw Error("Previous enemy identity is shared by another definition", list[index].Path);
                    RenameEnemyUsages(copy, previous, id);
                }
                list[index] = entry;
            }
            return Parse(copy.ToString(Formatting.None));
        }

        private static void ValidateInlineFields(JObject entry, JObject? previous)
        {
            void Check(JObject item, JObject? old, string key, string path, params JTokenType[] types)
            {
                var value = item[key];
                // LevelDocument stores validated fractions as JRaw to retain their exact spelling.
                var numeric = types.Contains(JTokenType.Float) &&
                    (value?.Type is JTokenType.Raw or JTokenType.Float or JTokenType.Integer) &&
                    float.TryParse(value.ToString(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var number) &&
                    !float.IsInfinity(number) && !float.IsNaN(number);
                if (value != null && !(types.Contains(JTokenType.Float) ? numeric : types.Contains(value.Type)) &&
                    !JToken.DeepEquals(value, old?[key]))
                    throw Error("Unexpected field type", path + "." + key);
            }

            const string path = "$.enemies[]";
            foreach (var key in new[] { "name", "description" })
                Check(entry, previous, key, path, JTokenType.String, JTokenType.Null);
            foreach (var key in new[] { "motion", "applyWay", "levelType" })
                Check(entry, previous, key, path, JTokenType.Integer, JTokenType.String);
            foreach (var key in new[] { "lifePointReduce", "numOfExtraDrops" })
                Check(entry, previous, key, path, JTokenType.Integer);
            foreach (var key in new[] { "rangeRadius", "viewRadius" })
                Check(entry, previous, key, path, JTokenType.Integer, JTokenType.Float);
            Check(entry, previous, "notCountInTotal", path, JTokenType.Boolean);
            foreach (var key in new[] { "attributes", "spData" })
                Check(entry, previous, key, path, JTokenType.Object, JTokenType.Null);
            foreach (var key in new[] { "enemyTags", "talentBlackboard", "skills" })
                Check(entry, previous, key, path, JTokenType.Array, JTokenType.Null);
            if (entry["spData"] is JObject sp && !JToken.DeepEquals(sp, previous?["spData"]))
            {
                Check(sp, null, "spType", path + ".spData", JTokenType.Integer, JTokenType.String);
                Check(sp, null, "maxSp", path + ".spData", JTokenType.Integer);
                Check(sp, null, "initSp", path + ".spData", JTokenType.Integer);
                Check(sp, null, "increment", path + ".spData", JTokenType.Integer, JTokenType.Float);
            }

            if (entry["skills"] is not JArray skills || JToken.DeepEquals(skills, previous?["skills"])) return;
            foreach (var token in skills)
            {
                if (token is not JObject skill || skill["prefabKey"]?.Type != JTokenType.String ||
                    string.IsNullOrWhiteSpace(skill["prefabKey"]!.Value<string>()))
                    throw Error("Skill needs a prefab key", path + ".skills[]");
                var oldSkill = (previous?["skills"] as JArray)?.OfType<JObject>().FirstOrDefault(item =>
                    JToken.DeepEquals(item["prefabKey"], skill["prefabKey"]));
                foreach (var key in new[] { "priority", "spCost" })
                    Check(skill, oldSkill, key, path + ".skills[]", JTokenType.Integer);
                foreach (var key in new[] { "cooldown", "initCooldown" })
                    Check(skill, oldSkill, key, path + ".skills[]", JTokenType.Integer, JTokenType.Float);
                Check(skill, oldSkill, "blackboard", path + ".skills[]", JTokenType.Array, JTokenType.Null);
            }
        }

        public LevelDocument DeleteInlineEnemy(int index)
        {
            var copy = (JObject)root.DeepClone();
            var list = RequiredArray(copy["enemies"], "$.enemies");
            CheckIndex(index, list.Count);
            var id = InlineIdentity(list[index]);
            if (id != null)
            {
                if (GetEnemyUsages(id).Count > 0) throw Error("Enemy is used by an action", list[index].Path);
                CheckUnresolvedEnemyReferences(copy, id);
            }
            list.RemoveAt(index);
            return Parse(copy.ToString(Formatting.None));
        }

        private static string? InlineIdentity(JToken token)
        {
            if (!(token is JObject entry)) return null;
            var alias = entry["alias"]?.Type == JTokenType.String ? entry["alias"]!.Value<string>() : null;
            return !string.IsNullOrEmpty(alias) ? alias :
                entry["key"]?.Type == JTokenType.String ? entry["key"]!.Value<string>() : null;
        }

        private static string? ReferenceIdentity(JToken token)
        {
            var id = (token as JObject)?["id"];
            return id?.Type == JTokenType.String ? id.Value<string>() : null;
        }

        private static bool HasEnemyIdentity(JObject document, string id, int excludedInline) =>
            (document["enemyDbRefs"] as JArray)?.Any(item => ReferenceIdentity(item) == id) == true ||
            (document["enemies"] as JArray)?.Where((_, index) => index != excludedInline).Any(item => InlineIdentity(item) == id) == true;

        public IReadOnlyList<string> GetEnemyUsages(string key) => Actions(root)
            .Where(item => IsEnemyTargetAction(item.Item1["actionType"]) &&
                item.Item1["key"]?.Type == JTokenType.String && item.Item1["key"]!.Value<string>() == key)
            .Select(item => item.Item1.Path + ".key").ToArray();

        public LevelDocument PutEnemyReference(int index, string json)
        {
            var entry = ParseObjectFragment(json);
            var id = ReferenceIdentity(entry);
            if (id == null || string.IsNullOrWhiteSpace(id)) throw Error("Enemy id is required", "$.enemyDbRefs");
            if (entry["useDb"]?.Type != JTokenType.Boolean || entry["level"]?.Type != JTokenType.Integer ||
                !int.TryParse(entry["level"]!.ToString(), out _))
                throw Error("Expected useDb boolean and Int32 level", "$.enemyDbRefs");
            var patch = entry["overwrittenData"];
            if (patch != null && patch.Type != JTokenType.Null && !(patch is JObject))
                throw Error("Expected overwrittenData object or null", "$.enemyDbRefs");
            if (!entry["useDb"]!.Value<bool>() && !(patch is JObject))
                throw Error("Independent enemy requires overwrittenData", "$.enemyDbRefs");
            var copy = (JObject)root.DeepClone();
            if (copy["enemyDbRefs"] == null || copy["enemyDbRefs"]!.Type == JTokenType.Null)
                copy["enemyDbRefs"] = new JArray();
            var refs = RequiredArray(copy["enemyDbRefs"], "$.enemyDbRefs");
            if (index < -1 || index >= refs.Count) throw new ArgumentOutOfRangeException(nameof(index));
            for (var i = 0; i < refs.Count; i++)
                if (i != index && ReferenceIdentity(refs[i]) == id)
                {
                    if (index == -1 && JToken.DeepEquals(refs[i], entry)) return this;
                    throw Error("Enemy id already exists", refs[i].Path + ".id");
                }
            var oldId = index >= 0 ? ReferenceIdentity(refs[index]) : null;
            if (oldId != id && copy["enemies"] is JArray inline)
                foreach (var enemy in inline)
                    if (InlineIdentity(enemy) == id) throw Error("Enemy id would shadow an inline definition", enemy.Path);
            if (index >= 0)
            {
                if (!string.IsNullOrEmpty(oldId) && oldId != id)
                {
                    if (refs.Where((_, at) => at != index).Any(other => ReferenceIdentity(other) == oldId))
                        throw Error("Previous enemy identity is shared by another reference", refs[index].Path);
                    RenameEnemyUsages(copy, oldId!, id);
                }
                refs[index] = entry;
            }
            else refs.Add(entry);
            return Parse(copy.ToString(Formatting.None));
        }

        public LevelDocument DeleteEnemyReference(int index)
        {
            var copy = (JObject)root.DeepClone();
            var refs = RequiredArray(copy["enemyDbRefs"], "$.enemyDbRefs");
            CheckIndex(index, refs.Count);
            var id = ReferenceIdentity(refs[index]);
            if (!string.IsNullOrEmpty(id))
            {
                if (GetEnemyUsages(id!).Count > 0) throw Error("Enemy is used by an action", refs[index].Path);
                CheckUnresolvedEnemyReferences(copy, id!);
            }
            refs.RemoveAt(index);
            return Parse(copy.ToString(Formatting.None));
        }

        private static bool IsEnemyTargetAction(JToken? type) =>
            IsAction(type, "SPAWN", 0) || IsAction(type, "DISPLAY_ENEMY_INFO", 5);

        private static void RenameEnemyUsages(JObject document, string oldId, string id)
        {
            CheckUnresolvedEnemyReferences(document, oldId);
            foreach (var item in Actions(document))
            {
                var key = item.Item1["key"];
                if (IsEnemyTargetAction(item.Item1["actionType"]) &&
                    key?.Type == JTokenType.String && key.Value<string>() == oldId)
                    item.Item1["key"] = id;
            }
        }

        private static void CheckUnresolvedEnemyReferences(JObject document, string id)
        {
            var understood = new HashSet<JProperty>();
            foreach (var item in Actions(document))
            {
                var key = item.Item1.Property("key");
                var type = item.Item1["actionType"];
                // Some indexed branch consumers inspect the key of otherwise empty actions.
                if (key != null && KnownAction(type) && !IsBranchEmpty(type, item.Item2))
                    understood.Add(key);
            }
            foreach (var property in document.Descendants().OfType<JProperty>())
            {
                if (property.Name != "key" && property.Name != "valueStr" && property.Name != "enemyKey") continue;
                if (property.Value.Type == JTokenType.String && property.Value.Value<string>() == id &&
                    !understood.Contains(property) &&
                    !(property.Name == "key" && property.Parent?.Parent == document["enemies"]))
                    throw Error("Unresolved enemy reference needs review", property.Path);
            }
        }
    }
}
