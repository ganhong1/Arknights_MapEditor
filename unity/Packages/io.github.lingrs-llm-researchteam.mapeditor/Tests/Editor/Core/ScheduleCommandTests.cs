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
            Assert.That(changed.GetSection("waves")[0]["fragments"][0]["actions"][1]["routeIndex"].Value<int>(), Is.EqualTo(2));
            Assert.That(changed.GetSection("branches")["branch"]["phases"][0]["actions"][0]["routeIndex"].Value<int>(), Is.EqualTo(2));
            Assert.That(((JArray)original.GetSection("routes")).Count, Is.EqualTo(3));
        }

        [Test]
        public void ExtraRouteDeletionReindexesBranchAndExplicitExtraLibrary()
        {
            var changed = Sample().DeleteRoute("extraRoutes", 0);
            Assert.That(changed.GetSection("waves")[0]["fragments"][0]["actions"][1]["routeIndex"].Value<int>(), Is.EqualTo(1));
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

        [Test]
        public void EnemyDeletionRejectsMainAndBranchSpawnReferences()
        {
            Assert.Throws<LevelDocumentException>(() => Sample().DeleteEnemy("enemy_a"));
            Assert.Throws<LevelDocumentException>(() => Sample().DeleteEnemy("enemy_b"));
            Assert.That(((JArray)Sample().DeleteEnemy("unused").GetSection("enemyDbRefs")).Count, Is.EqualTo(2));
            Assert.Throws<LevelDocumentException>(() => Sample().WithSection("future", "{\"key\":\"unused\"}").DeleteEnemy("unused"));
        }

        [Test]
        public void RouteMoveRetainsTargetIdentityInActions()
        {
            var changed = Sample().MoveRoute("routes", 2, 0);
            Assert.That(changed.GetSection("routes")[0]["tag"].Value<int>(), Is.EqualTo(2));
            Assert.That(changed.GetSection("waves")[0]["fragments"][0]["actions"][0]["routeIndex"].Value<int>(), Is.EqualTo(0));
            Assert.That(changed.GetSection("waves")[0]["fragments"][0]["actions"][1]["routeIndex"].Value<int>(), Is.EqualTo(2));
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
