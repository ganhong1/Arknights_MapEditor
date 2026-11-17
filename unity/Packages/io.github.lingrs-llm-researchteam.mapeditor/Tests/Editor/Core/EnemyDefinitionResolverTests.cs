#nullable enable
using System;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Arknights.MapEditor.Tests.Core
{
    public sealed class EnemyDefinitionResolverTests
    {
        private static JArray Levels() => JArray.Parse(@"[
            {'level':0,'enemyData':{'name':{'m_defined':true,'m_value':'猎狗'},'prefabKey':{'m_defined':true,'m_value':'enemy_dog'},'attributes':{'maxHp':{'m_defined':true,'m_value':820},'atk':{'m_defined':true,'m_value':190}}}},
            {'level':1,'enemyData':{'name':{'m_defined':false,'m_value':null},'attributes':{'maxHp':{'m_defined':true,'m_value':2350}}}}]");

        [Test]
        public void LevelResolutionInheritsSparseFieldsWithoutChangingInputs()
        {
            var levels = Levels(); var original = levels.ToString();
            var result = EnemyDefinitionResolver.ResolveDatabaseLevel("enemy_dog", levels, 1);
            Assert.That(result["name"]!.Value<string>(), Is.EqualTo("猎狗"));
            Assert.That(result["attributes"]!["maxHp"]!.Value<int>(), Is.EqualTo(2350));
            Assert.That(result["attributes"]!["atk"]!.Value<int>(), Is.EqualTo(190));
            Assert.That(EnemyDefinitionResolver.GetKeyInLevel(result), Is.EqualTo("enemy_dog"));
            Assert.That(levels.ToString(), Is.EqualTo(original));
            result["attributes"]!["atk"] = 0;
            Assert.That(levels.ToString(), Is.EqualTo(original));
        }

        [Test]
        public void NegativeLevelUsesFirstEntryAndMissingLevelFails()
        {
            Assert.That(EnemyDefinitionResolver.ResolveDatabaseLevel("enemy_dog", Levels(), -1)["attributes"]!["maxHp"]!.Value<int>(), Is.EqualTo(820));
            Assert.Throws<InvalidOperationException>(() => EnemyDefinitionResolver.ResolveDatabaseLevel("enemy_dog", Levels(), 2));
        }

        [Test]
        public void DefinedZeroOverridesAndUndefinedZeroInherits()
        {
            var reference = JObject.Parse(@"{'useDb':true,'id':'enemy_dog','level':1,'overwrittenData':{'attributes':{'maxHp':{'m_defined':false,'m_value':0},'atk':{'m_defined':true,'m_value':0}}}}");
            var original = reference.ToString();
            var result = EnemyDefinitionResolver.ResolveReference(reference, Levels());
            Assert.That(result["attributes"]!["maxHp"]!.Value<int>(), Is.EqualTo(2350));
            Assert.That(result["attributes"]!["atk"]!.Value<int>(), Is.Zero);
            Assert.That(reference.ToString(), Is.EqualTo(original));
        }

        [Test]
        public void IndependentDefinitionDoesNotReadDatabaseAndUsesCustomIdentity()
        {
            var reference = JObject.Parse(@"{'useDb':false,'id':'dog_custom','level':999,'overwrittenData':{'prefabKey':{'m_defined':true,'m_value':'enemy_dog'},'attributes':{'maxHp':{'m_defined':true,'m_value':25}}}}");
            var result = EnemyDefinitionResolver.ResolveReference(reference, null);
            Assert.That(result["key"]!.Value<string>(), Is.EqualTo("enemy_dog"));
            Assert.That(EnemyDefinitionResolver.GetKeyInLevel(result), Is.EqualTo("dog_custom"));
            Assert.That(result["attributes"]!["maxHp"]!.Value<int>(), Is.EqualTo(25));
            Assert.Throws<InvalidOperationException>(() => EnemyDefinitionResolver.ResolveReference(JObject.Parse("{'useDb':false,'id':'x','overwrittenData':null}"), null));
        }

        [Test]
        public void MaterializedIndependentCopyKeepsResolvedValuesAndChangesOnlyIdentity()
        {
            var resolved = EnemyDefinitionResolver.ResolveDatabaseLevel("enemy_dog", Levels(), 1);
            var original = resolved.ToString();
            var reference = EnemyDefinitionResolver.MaterializeIndependent(resolved, "dog_copy");
            var copy = EnemyDefinitionResolver.ResolveReference(reference, null);
            Assert.That(reference["useDb"]!.Value<bool>(), Is.False);
            Assert.That(JToken.DeepEquals(copy["attributes"], resolved["attributes"]), Is.True);
            Assert.That(copy["name"], Is.EqualTo(resolved["name"]));
            Assert.That(copy["key"], Is.EqualTo(resolved["key"]));
            Assert.That(EnemyDefinitionResolver.GetKeyInLevel(copy), Is.EqualTo("dog_copy"));
            Assert.That(resolved.ToString(), Is.EqualTo(original));
        }

        [Test]
        public void BlackboardMergesByExactKeyAndEmptyDeltaDoesNotClear()
        {
            var levels = Levels();
            levels[0]["enemyData"]!["talentBlackboard"] = JArray.Parse("[{'key':'A','value':1},{'key':'B','value':2}]");
            levels[1]["enemyData"]!["talentBlackboard"] = JArray.Parse("[{'key':'A','value':3},{'key':'a','value':4}]");
            var reference = JObject.Parse("{'useDb':true,'id':'enemy_dog','level':1,'overwrittenData':{'talentBlackboard':[]}}");
            var result = (JArray)EnemyDefinitionResolver.ResolveReference(reference, levels)["talentBlackboard"]!;
            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0]["key"]!.Value<string>(), Is.EqualTo("B"));
            Assert.That(result[1]["value"]!.Value<float>(), Is.EqualTo(3));
            Assert.That(result[2]["key"]!.Value<string>(), Is.EqualTo("a"));
        }

        [Test]
        public void SkillDeltaReplacesScalarFieldsButMergesNonemptyBlackboard()
        {
            var levels = Levels();
            levels[0]["enemyData"]!["skills"] = JArray.Parse("[{'prefabKey':'skill','cooldown':30,'blackboard':[{'key':'a','value':1},{'key':'b','value':2}]}]");
            levels[1]["enemyData"]!["skills"] = JArray.Parse("[{'prefabKey':'skill','cooldown':5,'blackboard':[{'key':'a','value':3}]}]");
            var result = EnemyDefinitionResolver.ResolveDatabaseLevel("enemy_dog", levels, 1);
            Assert.That(result["skills"]![0]!["cooldown"]!.Value<float>(), Is.EqualTo(5));
            Assert.That(((JArray)result["skills"]![0]!["blackboard"]!).Count, Is.EqualTo(2));
            levels[1]["enemyData"]!["skills"]![0]!["blackboard"] = new JArray();
            result = EnemyDefinitionResolver.ResolveDatabaseLevel("enemy_dog", levels, 1);
            Assert.That(((JArray)result["skills"]![0]!["blackboard"]!).Count, Is.Zero);
        }

        [Test]
        public void SpDataNullInheritsAndObjectReplacesWholeRecord()
        {
            var levels = Levels();
            levels[0]["enemyData"]!["spData"] = JObject.Parse("{'spType':1,'maxSp':20,'initSp':5,'increment':1}");
            levels[1]["enemyData"]!["spData"] = JValue.CreateNull();
            Assert.That(EnemyDefinitionResolver.ResolveDatabaseLevel("enemy_dog", levels, 1)["spData"]!["maxSp"]!.Value<int>(), Is.EqualTo(20));
            levels[1]["enemyData"]!["spData"] = JObject.Parse("{'spType':0,'maxSp':30,'initSp':0,'increment':0}");
            Assert.That(EnemyDefinitionResolver.ResolveDatabaseLevel("enemy_dog", levels, 1)["spData"]!["initSp"]!.Value<int>(), Is.Zero);
        }

        [Test]
        public void KeyFallsBackOnlyForNullOrEmptyAlias()
        {
            Assert.That(EnemyDefinitionResolver.GetKeyInLevel(JObject.Parse("{'alias':'','key':'enemy'}")), Is.EqualTo("enemy"));
            Assert.That(EnemyDefinitionResolver.GetKeyInLevel(JObject.Parse("{'alias':' ','key':'enemy'}")), Is.EqualTo(" "));
        }

        [Test]
        public void IndependentDefaultsMatchNativeConstructors()
        {
            var result = EnemyDefinitionResolver.ResolveReference(JObject.Parse("{'useDb':false,'id':'x','overwrittenData':{'spData':{}}}"), null);
            Assert.That(result["attributes"]!["maxHp"]!.Value<int>(), Is.EqualTo(100));
            Assert.That(result["attributes"]!["attackSpeed"]!.Value<float>(), Is.EqualTo(100));
            Assert.That(result["attributes"]!["respawnTime"]!.Value<int>(), Is.EqualTo(60));
            Assert.That(result["attributes"]!["epBreakRecoverSpeed"]!.Value<float>(), Is.EqualTo(1));
            Assert.That(result["spData"]!["increment"]!.Value<float>(), Is.EqualTo(1));
            Assert.That(result["rangeRadius"]!.Value<float>(), Is.EqualTo(-1));
            Assert.That(result["lifePointReduce"]!.Value<int>(), Is.EqualTo(1));
        }

        [Test]
        public void InvalidDefinedTypesAndOutOfRangeValuesAreRejected()
        {
            var levels = Levels();
            levels[0]["enemyData"]!["attributes"]!["atk"]!["m_value"] = 2147483648L;
            Assert.Throws<InvalidOperationException>(() => EnemyDefinitionResolver.ResolveDatabaseLevel("enemy_dog", levels, 0));
            levels = Levels();
            levels[0]["enemyData"]!["attributes"]!["moveSpeed"] = JObject.Parse("{'m_defined':true,'m_value':1e100}");
            Assert.Throws<InvalidOperationException>(() => EnemyDefinitionResolver.ResolveDatabaseLevel("enemy_dog", levels, 0));
            Assert.Throws<InvalidOperationException>(() => EnemyDefinitionResolver.ResolveReference(JObject.Parse("{'useDb':'false','id':'x','overwrittenData':{}}"), null));
        }
    }
}
