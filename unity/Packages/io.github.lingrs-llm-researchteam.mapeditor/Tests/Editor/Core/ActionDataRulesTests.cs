#nullable enable
using System;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Arknights.MapEditor.Tests.Core
{
    public sealed class ActionDataRulesTests
    {
        private static readonly string[] EnemyKeys = { "enemy_a" };
        private static JObject Document() => JObject.Parse(@"{
            'routes':[{'startPosition':{'row':0,'col':0},'endPosition':{'row':0,'col':1}}],
            'extraRoutes':[{'startPosition':{'row':1,'col':0},'endPosition':{'row':1,'col':1}}],
            'predefines':{'tokenInsts':[{'alias':'box','inst':{'characterKey':'trap_001_crate'}}],
                'characterCards':[{'alias':'card','inst':{'characterKey':'char_a'}}]},
            'hardPredefines':null
        }");

        private static JObject Spawn()
        {
            var action = ActionDataRules.NewAction("SPAWN");
            action["key"] = "enemy_a"; action["routeIndex"] = 0;
            return action;
        }

        [TestCase("SPAWN", false, "routes")]
        [TestCase("SPAWN", true, "extraRoutes")]
        [TestCase("PREVIEW_CURSOR", false, "routes")]
        [TestCase("PREVIEW_CURSOR", true, "extraRoutes")]
        [TestCase("EMPTY", false, null)]
        [TestCase("EMPTY", true, "extraRoutes")]
        [TestCase("DISPLAY_ENEMY_INFO", true, null)]
        [TestCase("SHOW_ALL_HIDDEN_CARDS", false, null)]
        public void RouteLibraryComesFromActionAndScheduleContext(string type, bool branch, string? library)
        {
            Assert.That(ActionDataRules.RouteLibrary(Document(), ActionDataRules.NewAction(type), branch), Is.EqualTo(library));
        }

        [TestCase("SPAWN")]
        [TestCase("PREVIEW_CURSOR")]
        [TestCase("ACTIVATE_PREDEFINED")]
        [TestCase("STORY")]
        [TestCase("EMPTY")]
        public void NewActionRetainsSelectedTypeAndNativeDefaults(string type)
        {
            var action = ActionDataRules.NewAction(type);
            Assert.That(action.Value<string>("actionType"), Is.EqualTo(type));
            Assert.That(action.Value<int>("count"), Is.EqualTo(1));
            Assert.That(action.Value<bool>("managedByScheduler"), Is.True);
            Assert.That(action.Property("key"), Is.Null);
            Assert.That(action.Property("routeIndex"), Is.Null);
        }

        [TestCase("managedByScheduler", "null")]
        [TestCase("blockFragment", "1")]
        [TestCase("autoPreviewRoute", "'true'")]
        [TestCase("autoDisplayEnemyInfo", "[]")]
        [TestCase("isUnharmfulAndAlwaysCountAsKilled", "{}")]
        [TestCase("dontBlockWave", "'false'")]
        [TestCase("forceBlockWaveInBranch", "0")]
        [TestCase("hiddenGroup", "3")]
        [TestCase("randomSpawnGroupKey", "false")]
        [TestCase("randomSpawnGroupPackKey", "[]")]
        [TestCase("randomType", "'EVERY_SECOND'")]
        [TestCase("refreshType", "5")]
        [TestCase("weight", "0.5")]
        [TestCase("preDelay", "null")]
        [TestCase("interval", "-1")]
        [TestCase("count", "0")]
        [TestCase("routeIndex", "1")]
        [TestCase("key", "null")]
        [TestCase("actionType", "'E_NUM'")]
        public void ChangedDeclaredFieldsUseSharedValidation(string field, string json)
        {
            var original = Spawn(); var edited = (JObject)original.DeepClone();
            edited[field] = JToken.Parse(json);
            foreach (var branch in new[] { false, true })
                Assert.Throws<FormatException>(() => ActionDataRules.Require(Document(), edited, original, EnemyKeys, branch), field);
        }

        [TestCase("hiddenGroup", "null")]
        [TestCase("randomSpawnGroupKey", "''")]
        [TestCase("randomSpawnGroupPackKey", "null")]
        [TestCase("randomType", "'PER_DAY'")]
        [TestCase("refreshType", "3")]
        [TestCase("weight", "0")]
        [TestCase("weight", "-1")]
        public void DeclaredNonProbabilityValuesKeepTheirOriginalMeaning(string field, string json)
        {
            var original = Spawn(); var edited = (JObject)original.DeepClone(); edited[field] = JToken.Parse(json);
            Assert.DoesNotThrow(() => ActionDataRules.Require(Document(), edited, original, EnemyKeys, true));
        }

        [Test]
        public void UnchangedUnknownAndInvalidLegacyValuesArePreserved()
        {
            var original = JObject.Parse("{'actionType':980,'count':-4,'preDelay':null,'hiddenGroup':[],'randomType':'OLD','future':{'x':1}}");
            var edited = (JObject)original.DeepClone(); edited["future"]!["x"] = 2;
            Assert.DoesNotThrow(() => ActionDataRules.Require(Document(), edited, original, EnemyKeys, false));
            edited["preDelay"] = -2;
            Assert.Throws<FormatException>(() => ActionDataRules.Require(Document(), edited, original, EnemyKeys, false));
            Assert.Throws<FormatException>(() => ActionDataRules.Require(Document(), original, null, EnemyKeys, false));
        }

        [Test]
        public void ChangingTypeRevalidatesNewlyConsumedTargets()
        {
            var original = ActionDataRules.NewAction("STORY"); original["key"] = "story_key";
            var next = (JObject)original.DeepClone(); next["actionType"] = "SPAWN"; next["routeIndex"] = 0;
            Assert.Throws<FormatException>(() => ActionDataRules.Require(Document(), next, original, EnemyKeys, false));
            next["key"] = "enemy_a";
            Assert.DoesNotThrow(() => ActionDataRules.Require(Document(), next, original, EnemyKeys, false));
        }

        [Test]
        public void NewSpawnRequiresExplicitEnemyRouteCountAndTiming()
        {
            var incomplete = ActionDataRules.NewAction("SPAWN");
            Assert.Throws<FormatException>(() => ActionDataRules.Require(Document(), incomplete, null, EnemyKeys, false));
            foreach (var field in new[] { "key", "routeIndex", "count", "preDelay", "interval" })
            {
                var action = Spawn(); action.Remove(field);
                Assert.Throws<FormatException>(() => ActionDataRules.Require(Document(), action, null, EnemyKeys, false), field);
            }
        }

        [Test]
        public void AliasValidationDistinguishesActivationTriggerAndWithdrawal()
        {
            var action = ActionDataRules.NewAction("ACTIVATE_PREDEFINED"); action["key"] = "card";
            Assert.DoesNotThrow(() => ActionDataRules.Require(Document(), action, null, EnemyKeys, false));
            action["actionType"] = "WITHDRAW_PREDEFINED";
            Assert.Throws<FormatException>(() => ActionDataRules.Require(Document(), action, null, EnemyKeys, true));
            action["key"] = "box";
            Assert.DoesNotThrow(() => ActionDataRules.Require(Document(), action, null, EnemyKeys, true));
            action["actionType"] = "TRIGGER_PREDEFINED"; action["key"] = "box:DefaultTrigger|card";
            Assert.DoesNotThrow(() => ActionDataRules.Require(Document(), action, null, EnemyKeys, true));
            action["key"] = "box||card";
            Assert.Throws<FormatException>(() => ActionDataRules.Require(Document(), action, null, EnemyKeys, true));
        }

        [Test]
        public void OperaSharesExistingNativeRouteMatrixEvenInsideBranch()
        {
            var document = Document(); document["operaConfig"] = "rune_05-01";
            var action = ActionDataRules.NewAction("PLAY_OPERA"); action["key"] = "dscout_skill_start";
            Assert.That(ActionDataRules.RouteStatus(document, action, true), Is.EqualTo(ActionRouteStatus.Main));
            Assert.That(ActionDataRules.RouteLibrary(document, action, true), Is.EqualTo("routes"));
            Assert.DoesNotThrow(() => ActionDataRules.Require(document, action, null, EnemyKeys, true));
            action["routeIndex"] = 3;
            Assert.Throws<FormatException>(() => ActionDataRules.Require(document, action, null, EnemyKeys, true));
            document["operaConfig"] = "main_14-09"; action["key"] = "main_14_shake";
            Assert.That(ActionDataRules.RouteStatus(document, action, true), Is.EqualTo(ActionRouteStatus.Unused));
            Assert.DoesNotThrow(() => ActionDataRules.Require(document, action, null, EnemyKeys, true));
            action["key"] = "unknown";
            Assert.That(ActionDataRules.RouteStatus(document, action, true), Is.EqualTo(ActionRouteStatus.Unknown));
            Assert.DoesNotThrow(() => ActionDataRules.Require(document, action, null, EnemyKeys, true));
        }

        [Test]
        public void EmptyIsAConfigurationCandidateAndHasNoExecutableTimeRange()
        {
            var action = ActionDataRules.NewAction("EMPTY"); action["key"] = ""; action["routeIndex"] = -1;
            Assert.DoesNotThrow(() => ActionDataRules.Require(Document(), action, null, EnemyKeys, true));
            Assert.That(WaveSchedule.RelativeTime(new JValue(0), action), Is.Null);
            action["routeIndex"] = 3;
            Assert.Throws<FormatException>(() => ActionDataRules.Require(Document(), action, null, EnemyKeys, true));
            Assert.DoesNotThrow(() => ActionDataRules.Require(Document(), action, null, EnemyKeys, false));
        }
    }
}
