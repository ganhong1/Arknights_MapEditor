using System;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Arknights.MapEditor.Tests.Core
{
    public sealed class ScheduleCommandTests
    {
        private static LevelDocument Sample() => LevelDocument.Create(4, 3)
            .WithSection("routes", "[{\"tag\":0},{\"tag\":1},{\"tag\":2}]")
            .WithSection("extraRoutes", "[{\"tag\":0},{\"tag\":1},{\"tag\":2}]")
            .WithSection("waves", "[{\"fragments\":[{\"actions\":[{\"actionType\":\"SPAWN\",\"routeIndex\":2,\"key\":\"enemy_a\"},{\"actionType\":1,\"routeIndex\":2,\"useExtraRoute\":true}]}]}]")
            .WithSection("branches", "{\"branch\":{\"phases\":[{\"actions\":[{\"actionType\":0,\"routeIndex\":2,\"key\":\"enemy_b\"}]}]}}")
            .WithSection("enemyDbRefs", "[{\"id\":\"enemy_a\"},{\"id\":\"enemy_b\"},{\"id\":\"unused\",\"future\":42}]");

        [Test]
        public void DeleteUnusedMainRouteReindexesOnlyMainActions()
        {
            var original = Sample();
            var changed = original.DeleteRoute("routes", 1);
            Assert.That(changed.GetSection("waves")[0]["fragments"][0]["actions"][0]["routeIndex"].Value<int>(), Is.EqualTo(1));
            Assert.That(changed.GetSection("waves")[0]["fragments"][0]["actions"][1]["routeIndex"].Value<int>(), Is.EqualTo(1));
            Assert.That(changed.GetSection("branches")["branch"]["phases"][0]["actions"][0]["routeIndex"].Value<int>(), Is.EqualTo(2));
            Assert.That(((JArray)original.GetSection("routes")).Count, Is.EqualTo(3));
        }

        [Test]
        public void ExtraRouteDeletionReindexesOnlyBranchAndPreservesHistoricalFlag()
        {
            var changed = Sample().DeleteRoute("extraRoutes", 0);
            Assert.That(changed.GetSection("waves")[0]["fragments"][0]["actions"][1]["routeIndex"].Value<int>(), Is.EqualTo(2));
            Assert.That(changed.GetSection("waves")[0]["fragments"][0]["actions"][1]["useExtraRoute"].Value<bool>(), Is.True);
            Assert.That(changed.GetSection("branches")["branch"]["phases"][0]["actions"][0]["routeIndex"].Value<int>(), Is.EqualTo(1));
            Assert.Throws<LevelDocumentException>(() => Sample().DeleteRoute("routes", 2));
            Assert.Throws<LevelDocumentException>(() => Sample().DeleteRoute("extraRoutes", 2));
        }

        [Test]
        public void UnknownRouteReferencesAndMalformedSchedulesBlockDestructiveEdits()
        {
            Assert.Throws<LevelDocumentException>(() => Sample().WithSection("future", "{\"routeIndex\":2}").DeleteRoute("routes", 1));
            Assert.Throws<LevelDocumentException>(() => Sample().WithSection("waves", "[{}]").DeleteRoute("routes", 1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OperaRetainsMainRouteIdentityInBothSchedulers(bool branch)
        {
            var address = branch ? ScheduleAddress.Branch("branch", 0) : ScheduleAddress.Main(0, 0);
            var document = Sample().WithSection("operaConfig", "\"rune_05-01\"")
                .InsertAction(address, 0, "{\"actionType\":7,\"key\":\"dscout_skill_start\",\"routeIndex\":1}");
            var changed = document.MoveRoute("routes", 1, 0);
            Assert.That(changed.RouteUsePaths("routes")[0], Has.Some.Contains("演出"));
            Assert.That(changed.GetSection("routes")[0]["tag"].Value<int>(), Is.EqualTo(1));
            Assert.That(document.DeleteRoute("routes", 0).RouteUsePaths("routes")[0], Has.Some.Contains("演出"));
            Assert.Throws<LevelDocumentException>(() => document.DeleteRoute("routes", 1));
            Assert.DoesNotThrow(() => document.MoveRoute("extraRoutes", 1, 0));
        }

        [Test]
        public void UnknownOperaGuardsOnlyAffectedRoutesAndKnownNonRouteCommandDoesNotGuard()
        {
            var document = Sample().WithSection("operaConfig", "\"main_14-09\"");
            var address = ScheduleAddress.Main(0, 0);
            var unknown = document.InsertAction(address, 0, "{\"actionType\":\"PLAY_OPERA\",\"key\":\"future\",\"routeIndex\":1}");
            Assert.Throws<LevelDocumentException>(() => unknown.MoveRoute("routes", 1, 2));
            Assert.DoesNotThrow(() => unknown.MoveRoute("extraRoutes", 1, 2));
            Assert.DoesNotThrow(() => unknown.MoveRoute("routes", 1, 1));
            var known = document.InsertAction(address, 0, "{\"actionType\":7,\"key\":\"main_14_shake\",\"routeIndex\":1}");
            Assert.DoesNotThrow(() => known.DeleteRoute("routes", 1));
        }

        [Test]
        public void OperaWithoutExplicitIndexUsesNativeZeroWhileLegacyFlagIsGuarded()
        {
            var document = Sample().WithSection("operaConfig", "\"rune_05-01\"");
            var address = ScheduleAddress.Main(0, 0);
            var action = "{\"actionType\":7,\"key\":\"dscout_skill_start\"}";
            Assert.That(document.InsertAction(address, 0, action).MoveRoute("routes", 0, 1)
                .RouteUsePaths("routes")[1], Has.Some.Contains("演出"));
            var legacy = document.InsertAction(address, 0, action.Insert(action.Length - 1, ",\"useExtraRoute\":true"));
            Assert.Throws<LevelDocumentException>(() => legacy.MoveRoute("extraRoutes", 0, 1));
        }

        [Test]
        public void BranchEmptyRetainsExtraRouteIdentity()
        {
            var document = Sample().InsertAction(ScheduleAddress.Branch("branch", 0), 0, "{\"actionType\":13,\"routeIndex\":1}");
            Assert.That(document.MoveRoute("extraRoutes", 1, 0).RouteUsePaths("extraRoutes")[0], Has.Some.Contains("分支路线"));
            Assert.Throws<LevelDocumentException>(() => document.DeleteRoute("extraRoutes", 1));
            var moved = document.DeleteRoute("extraRoutes", 0);
            Assert.That(moved.GetSection("extraRoutes")[0]["tag"].Value<int>(), Is.EqualTo(1));
            Assert.That(moved.RouteUsePaths("extraRoutes")[0], Has.Some.Contains("分支路线"));
            Assert.DoesNotThrow(() => document.DeleteRoute("routes", 1));
        }

        [Test]
        public void EnemyDeletionRejectsMainAndBranchSpawnReferences()
        {
            Assert.Throws<LevelDocumentException>(() => Sample().DeleteEnemy("enemy_a"));
            Assert.Throws<LevelDocumentException>(() => Sample().DeleteEnemy("enemy_b"));
            Assert.That(((JArray)Sample().DeleteEnemy("unused").GetSection("enemyDbRefs")).Count, Is.EqualTo(2));
            Assert.Throws<LevelDocumentException>(() => Sample().WithSection("future", "{\"key\":\"unused\"}").DeleteEnemy("unused"));
            Assert.Throws<LevelDocumentException>(() => Sample().WithSection("future", "{\"valueStr\":\"unused\"}").DeleteEnemy("unused"));
            var music = Sample().InsertAction(ScheduleAddress.Main(0, 0), 0,
                "{\"actionType\":\"PLAY_BGM\",\"key\":\"unused\"}");
            Assert.That(((JArray)music.DeleteEnemy("unused").GetSection("enemyDbRefs")).Count, Is.EqualTo(2));
        }

        [Test]
        public void RouteMoveRetainsTargetIdentityInActions()
        {
            var changed = Sample().MoveRoute("routes", 2, 0);
            Assert.That(changed.GetSection("routes")[0]["tag"].Value<int>(), Is.EqualTo(2));
            Assert.That(changed.GetSection("waves")[0]["fragments"][0]["actions"][0]["routeIndex"].Value<int>(), Is.EqualTo(0));
            Assert.That(changed.GetSection("waves")[0]["fragments"][0]["actions"][1]["routeIndex"].Value<int>(), Is.EqualTo(0));
        }

        [Test]
        public void ActionCommandsPreserveExtensionFieldsAndSeparateBranchAddress()
        {
            var address = ScheduleAddress.Branch("branch", 0);
            var changed = Sample().InsertAction(address, 0, "{\"actionType\":\"FUTURE\",\"future\":{\"value\":17}}")
                .MoveAction(address, 0, 1);
            Assert.That(changed.GetSection("branches")["branch"]["phases"][0]["actions"][1]["future"]["value"].Value<int>(), Is.EqualTo(17));
            var deleted = changed.DeleteAction(address, 1);
            Assert.That(deleted.GetSection("branches").ToString(), Is.EqualTo(Sample().GetSection("branches").ToString()));
            Assert.Throws<LevelDocumentException>(() => Sample().InsertAction(address, 0, "[]"));
        }

        [Test]
        public void PredefinedCollectionsRemainDistinctAndTriggerReferencesBlockDeletion()
        {
            var initial = Sample().WithSection("predefines", "{\"tokenInsts\":[],\"tokenCards\":[],\"future\":8}");
            var changed = initial.InsertPredefined("predefines", "tokenCards", 0, "{\"futureCard\":42}");
            Assert.That(((JArray)changed.GetSection("predefines")["tokenInsts"]).Count, Is.EqualTo(0));
            Assert.That(changed.DeletePredefined("predefines", "tokenCards", 0).GetSection("predefines").ToString(), Is.EqualTo(initial.GetSection("predefines").ToString()));
            var trigger = changed.InsertAction(ScheduleAddress.Main(0, 0), 0, "{\"actionType\":\"TRIGGER_PREDEFINED\",\"key\":\"unknown\"}");
            Assert.Throws<LevelDocumentException>(() => trigger.DeletePredefined("predefines", "tokenCards", 0));
        }

        [TestCase("\"TRIGGER_PREDEFINED\"")]
        [TestCase("8")]
        public void CompoundTriggersGuardPresetAliasAndImplicitIdentity(string action)
        {
            const string card = "{\"inst\":{\"characterKey\":\"trap_card\"},\"alias\":\"card\"}";
            const string box = "{\"inst\":{\"characterKey\":\"trap_box\"},\"position\":{\"row\":0,\"col\":0},\"direction\":\"UP\"}";
            var map = Sample().WithSection("predefines", "{\"tokenCards\":[],\"tokenInsts\":[]}")
                .SavePredefined("predefines", "tokenCards", 0, card, false)
                .SavePredefined("predefines", "tokenInsts", 0, box, false)
                .InsertAction(ScheduleAddress.Branch("branch", 0), 0,
                    "{\"actionType\":" + action + ",\"key\":\"trap_box:DefaultTrigger|card\"}");
            Assert.Throws<InvalidOperationException>(() =>
                map.SavePredefined("predefines", "tokenCards", 0, card.Replace("\"card\"", "\"other\""), true));
            Assert.Throws<InvalidOperationException>(() =>
                map.SavePredefined("predefines", "tokenInsts", 0, box.Replace("trap_box", "trap_other"), true));
            Assert.Throws<LevelDocumentException>(() => map.DeletePredefined("predefines", "tokenInsts", 0));
            Assert.Throws<LevelDocumentException>(() => map.DeletePredefined("predefines", "tokenCards", 0));
            Assert.That(map.SavePredefined("predefines", "tokenInsts", 0, box.Replace("UP", "LEFT"), true)
                .GetSection("predefines")!["tokenInsts"]![0]!["direction"]!.Value<string>(), Is.EqualTo("LEFT"));
        }

        [Test]
        public void UnrelatedKnownTriggersAndInstanceOnlyWithdrawalDoNotBlockCardDeletion()
        {
            const string card = "{\"inst\":{\"characterKey\":\"trap_card\"},\"alias\":\"card\"}";
            var map = Sample().WithSection("hardPredefines", "{\"tokenCards\":[]}")
                .SavePredefined("hardPredefines", "tokenCards", 0, card, false)
                .InsertAction(ScheduleAddress.Main(0, 0), 0,
                    "{\"actionType\":\"TRIGGER_PREDEFINED\",\"key\":\"other:DefaultTrigger|unrelated\"}")
                .InsertAction(ScheduleAddress.Branch("branch", 0), 0, "{\"actionType\":\"WITHDRAW_PREDEFINED\",\"key\":\"card\"}");
            Assert.That((JArray)map.DeletePredefined("hardPredefines", "tokenCards", 0)
                .GetSection("hardPredefines")!["tokenCards"]!, Is.Empty);
            var unknown = map.InsertAction(ScheduleAddress.Main(0, 0), 0, "{\"actionType\":999,\"key\":\"other\"}");
            Assert.Throws<LevelDocumentException>(() => unknown.DeletePredefined("hardPredefines", "tokenCards", 0));
        }

        [Test]
        public void ReferenceDiagnosticsReportKnownFailuresAndUnknownSemanticsSeparately()
        {
            var changed = Sample().InsertAction(ScheduleAddress.Main(0, 0), 0, "{\"actionType\":\"SPAWN\",\"routeIndex\":99,\"key\":\"missing_enemy\"}")
                .InsertAction(ScheduleAddress.Main(0, 0), 1, "{\"actionType\":\"FUTURE\",\"routeIndex\":17}");
            var diagnostics = changed.ValidateReferences();
            Assert.That(diagnostics, Has.Some.Matches<LevelDiagnostic>(d => d.Severity == "error" && d.Path.Contains("routeIndex")));
            Assert.That(diagnostics, Has.Some.Matches<LevelDiagnostic>(d => d.Severity == "warning" && d.Message.Contains("missing_enemy")));
            Assert.That(diagnostics, Has.Some.Matches<LevelDiagnostic>(d => d.Severity == "warning" && d.Message.Contains("Unknown")));
        }
    }
}
