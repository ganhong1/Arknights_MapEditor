#nullable enable
using Arknights.MapEditor.Core;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Arknights.MapEditor.Tests
{
    public sealed class EnemyDocumentTests
    {
        private const string Reference = "{\"useDb\":true,\"id\":\"enemy_a\",\"level\":0,\"overwrittenData\":null}";
        private static LevelDocument Map() => LevelDocument.Create(2, 2);
        [Test]
        public void AddingDefinitionPreservesOriginalAndUnknownFields()
        {
            var original = Map().WithSection("extension", "{\"v\":1}");
            var changed = original.PutEnemyReference(-1, Reference);
            Assert.That(original.GetEnemyReferences().Count, Is.Zero);
            Assert.That(changed.GetEnemyReferences().Count, Is.EqualTo(1));
            Assert.That(changed.GetSection("extension")!["v"]!.Value<int>(), Is.EqualTo(1));
            var detached = changed.GetEnemyReferences(); detached.Clear();
            Assert.That(changed.GetEnemyReferences().Count, Is.EqualTo(1));
        }
        [Test]
        public void RenameUpdatesEnemyActionsAcrossBothTreesButNotPredefinedAlias()
        {
            var map = Map().PutEnemyReference(-1, Reference)
                .WithSection("waves", "[{\"fragments\":[{\"actions\":[{\"actionType\":\"SPAWN\",\"key\":\"enemy_a\"},{\"actionType\":\"ACTIVATE_PREDEFINED\",\"key\":\"enemy_a\"}]}]}]")
                .WithSection("branches", "{\"b\":{\"phases\":[{\"actions\":[{\"actionType\":0,\"key\":\"enemy_a\"}]}]}}");
            var changed = map.PutEnemyReference(0, Reference.Replace("enemy_a", "enemy_b"));
            Assert.That(changed.GetEnemyUsages("enemy_b").Count, Is.EqualTo(2));
            Assert.That(map.GetEnemyUsages("enemy_a").Count, Is.EqualTo(2));
            Assert.That(changed.GetSection("waves")![0]!["fragments"]![0]!["actions"]![1]!["key"]!.Value<string>(), Is.EqualTo("enemy_a"));
            Assert.Throws<LevelDocumentException>(() => changed.DeleteEnemyReference(0));
        }
        [Test]
        public void DuplicateIdentityAndShadowingInlineAreRejected()
        {
            var map = Map().PutEnemyReference(-1, Reference);
            Assert.That(map.PutEnemyReference(-1, Reference), Is.SameAs(map));
            Assert.Throws<LevelDocumentException>(() => map.PutEnemyReference(-1, Reference.Replace("\"level\":0", "\"level\":1")));
            var inline = Map().WithSection("enemies", "[{\"alias\":\"enemy_a\",\"key\":\"prefab_a\"}]");
            Assert.Throws<LevelDocumentException>(() => inline.PutEnemyReference(-1, Reference));
        }
        [Test]
        public void UnknownActionReferencePreventsRenameAndDelete()
        {
            var map = Map().PutEnemyReference(-1, Reference).WithSection("waves", "[{\"fragments\":[{\"actions\":[{\"actionType\":999,\"key\":\"enemy_a\"}]}]}]");
            Assert.Throws<LevelDocumentException>(() => map.PutEnemyReference(0, Reference.Replace("enemy_a", "enemy_b")));
            Assert.Throws<LevelDocumentException>(() => map.DeleteEnemyReference(0));
        }

        [TestCase("key")]
        [TestCase("valueStr")]
        [TestCase("enemyKey")]
        public void ResolvedActionDoesNotExcuseDistinctSameValueReferences(string field)
        {
            var map = Map().PutEnemyReference(-1, Reference)
                .WithSection("waves", "[{\"fragments\":[{\"actions\":[{\"actionType\":\"SPAWN\",\"key\":\"enemy_a\"}]}]}]")
                .WithSection("future", "{\"" + field + "\":\"enemy_a\"}");
            var error = Assert.Throws<LevelDocumentException>(() =>
                map.PutEnemyReference(0, Reference.Replace("enemy_a", "enemy_b")));
            Assert.That(error!.Path, Is.EqualTo("future." + field));
        }

        [TestCase("\"EMPTY\"")]
        [TestCase("13")]
        public void BranchEmptyKeyRequiresReviewWhileNonEnemyWaveKeysRemainUntouched(string emptyType)
        {
            var map = Map().PutEnemyReference(-1, Reference)
                .WithSection("waves", "[{\"fragments\":[{\"actions\":[{\"actionType\":" + emptyType + ",\"key\":\"enemy_a\"},{\"actionType\":\"PLAY_BGM\",\"key\":\"enemy_a\"}]}]}]");
            var branches = "{\"b\":{\"phases\":[{\"actions\":[{\"actionType\":" + emptyType + ",\"key\":\"enemy_a\"}]}]}}";
            var branch = map.WithSection("branches", branches);
            var replacement = Reference.Replace("enemy_a", "enemy_b");
            Assert.Throws<LevelDocumentException>(() => branch.PutEnemyReference(0, replacement));
            Assert.Throws<LevelDocumentException>(() => branch.DeleteEnemyReference(0));
            var renamed = map.PutEnemyReference(0, replacement);
            Assert.That(JToken.DeepEquals(renamed.GetSection("waves"), map.GetSection("waves")), Is.True);
            Assert.That(map.DeleteEnemyReference(0).GetEnemyReferences(), Is.Empty);
        }

        [Test]
        public void ReferenceEditsAndInlineCreationTolerateMalformedImportedSiblingEntries()
        {
            var map = Map().WithSection("enemyDbRefs", "[null,29,{\"id\":{\"future\":true}}]")
                .WithSection("enemies", "[{\"alias\":{},\"key\":\"other\"}]");
            var appended = map.PutEnemyReference(-1, Reference);
            Assert.That(appended.GetEnemyReferences()[2]!["id"]!["future"]!.Value<bool>(), Is.True);
            Assert.That(appended.GetEnemyReferences()[3]!["id"]!.Value<string>(), Is.EqualTo("enemy_a"));
            Assert.That(map.PutInlineEnemy(-1, "{\"key\":\"new_prefab\"}").GetInlineEnemies().Count, Is.EqualTo(2));
            var repaired = map.PutEnemyReference(0, Reference).DeleteEnemyReference(1).DeleteEnemyReference(1);
            Assert.That(repaired.GetEnemyReferences().Count, Is.EqualTo(1));
            Assert.That(repaired.GetEnemyReferences()[0]!["id"]!.Value<string>(), Is.EqualTo("enemy_a"));
        }
        [Test]
        public void InlineAliasResolvesSpawnWithoutDatabaseReference()
        {
            var map = Map().WithSection("enemies", "[{\"key\":\"prefab_a\",\"alias\":\"enemy_a\"}]")
                .WithSection("waves", "[{\"fragments\":[{\"actions\":[{\"actionType\":0,\"key\":\"enemy_a\",\"routeIndex\":0}]}]}]");
            Assert.That(map.ValidateReferences().Any(d => d.Message.Contains("SPAWN key unresolved")), Is.False);
        }

        [Test]
        public void InlineDefinitionsSharePrefabAndRenameOnlyTypedReferences()
        {
            const string inline = "{\"key\":\"prefab_a\",\"alias\":\"inline_a\",\"extension\":{\"x\":1},\"skills\":[],\"spData\":null}";
            var map = Map().PutEnemyReference(-1, Reference)
                .PutInlineEnemy(-1, inline).PutInlineEnemy(-1, inline.Replace("inline_a", "inline_b"))
                .WithSection("waves", "[{\"fragments\":[{\"actions\":[{\"actionType\":\"SPAWN\",\"key\":\"inline_a\"},{\"actionType\":\"ACTIVATE_PREDEFINED\",\"key\":\"inline_a\"}]}]}]")
                .WithSection("branches", "{\"b\":{\"phases\":[{\"actions\":[{\"actionType\":5,\"key\":\"inline_a\"}]}]}}");
            Assert.Throws<LevelDocumentException>(() => map.PutInlineEnemy(-1, inline.Replace("inline_a", "enemy_a")));
            Assert.Throws<LevelDocumentException>(() => map.PutInlineEnemy(-1, inline));
            Assert.Throws<LevelDocumentException>(() => map.DeleteInlineEnemy(0));
            var next = map.PutInlineEnemy(0, inline.Replace("inline_a", "inline_c"));
            Assert.That(next.GetEnemyUsages("inline_c").Count, Is.EqualTo(2));
            Assert.That(next.GetSection("waves")![0]!["fragments"]![0]!["actions"]![1]!["key"]!.Value<string>(), Is.EqualTo("inline_a"));
            Assert.That(next.GetInlineEnemies()[0]!["extension"]!["x"]!.Value<int>(), Is.EqualTo(1));
            Assert.That(next.GetInlineEnemies()[0]!["skills"], Is.TypeOf<JArray>());
            Assert.That(next.GetInlineEnemies()[0]!["spData"]!.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(next.DeleteInlineEnemy(1).GetInlineEnemies().Count, Is.EqualTo(1));
        }

        [Test]
        public void InlineFractionsArePreservedAndNonnumericCooldownIsRejected()
        {
            const string inline = "{\"key\":\"prefab_a\",\"skills\":[{\"prefabKey\":\"skill_a\",\"cooldown\":1.2500}],\"spData\":{\"increment\":1.000}}";
            var next = Map().PutInlineEnemy(-1, inline);
            Assert.That(next.ToJson(false), Does.Contain("\"cooldown\":1.2500"));
            Assert.That(next.ToJson(false), Does.Contain("\"increment\":1.000"));
            Assert.Throws<LevelDocumentException>(() => Map().PutInlineEnemy(-1, inline.Replace("1.2500", "\"bad\"")));
        }

        [Test]
        public void IndependentDefinitionRequiresObjectAndCanBeDeletedWhenUnused()
        {
            Assert.Throws<LevelDocumentException>(() => Map().PutEnemyReference(-1, Reference.Replace("true", "false")));
            var map = Map().PutEnemyReference(-1, Reference.Replace("true", "false").Replace("null", "{}"));
            Assert.That(map.DeleteEnemyReference(0).GetEnemyReferences().Count, Is.Zero);
        }
    }
}
