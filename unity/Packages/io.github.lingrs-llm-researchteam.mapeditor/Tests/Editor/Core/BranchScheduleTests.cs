#nullable enable
using System;
using System.Linq;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Arknights.MapEditor.Tests.Core
{
    public sealed class BranchScheduleTests
    {
        private static readonly string[] Enemies = { "enemy_a" };
        private static JObject Document() => JObject.Parse(@"{
            'extraRoutes':[{'startPosition':{'row':0,'col':0},'endPosition':{'row':1,'col':1},'motionMode':'WALK'}],
            'routes':[], 'enemyDbRefs':[{'id':'enemy_a','useDb':true,'level':0}],
            'branches':{'line_01':{'future':7,'phases':[{'preDelay':0,'actions':[
                {'actionType':'SPAWN','key':'enemy_a','count':1,'preDelay':0,'interval':0,'routeIndex':0,'future':42},
                {'actionType':'ACTIVATE_PREDEFINED','key':'box#1','preDelay':0}]}]}},
            'predefines':{'tokenInsts':[{'inst':{'characterKey':'trap_001_crate'},'alias':'box#1','hidden':true,'position':{'row':1,'col':1}}],
                'characterCards':[{'inst':{'characterKey':'char_a'},'alias':''}]},
            'hardPredefines':{'characterCards':[{'inst':{'characterKey':'char_b'},'alias':'hard_card'}]},
            'future':{'keep':true}
        }");

        private static JObject WithCaller()
        {
            var doc = Document();
            ((JArray)doc["predefines"]!["tokenInsts"]!).Add(JObject.Parse(@"{
                'inst':{'characterKey':'trap_076_bgarms'},'alias':'bell#1',
                'overrideSkillBlackboard':[{'key':'branch_id','valueStr':'line_01'}, {'key':'action_index','value':0.0}]
            }"));
            return doc;
        }

        [Test]
        public void AddAndCopyPreserveOriginalAndAllExtensionFields()
        {
            var original = Document();
            var created = BranchSchedule.AddBranch(original, "new");
            Assert.That(created["branches"]!["new"]!["phases"]!.Count(), Is.EqualTo(1));
            Assert.That(original["branches"]!["new"], Is.Null);
            var copy = BranchSchedule.AddBranch(original, "copy", "line_01");
            Assert.That(JToken.DeepEquals(copy["branches"]!["copy"], original["branches"]!["line_01"]), Is.True);
            Assert.That(JToken.DeepEquals(copy["future"], original["future"]), Is.True);
            Assert.Throws<FormatException>(() => BranchSchedule.AddBranch(original, "line_01"));
            Assert.Throws<FormatException>(() => BranchSchedule.AddBranch(original, " "));
        }

        [Test]
        public void MissingAndNullBranchesCanBeCreatedButMalformedObjectIsPreserved()
        {
            Assert.That(BranchSchedule.AddBranch(new JObject(), "a")["branches"]!["a"], Is.TypeOf<JObject>());
            Assert.That(BranchSchedule.AddBranch(JObject.Parse("{'branches':null}"), "a")["branches"]!["a"], Is.TypeOf<JObject>());
            Assert.Throws<FormatException>(() => BranchSchedule.AddBranch(JObject.Parse("{'branches':[]}"), "a"));
        }

        [Test]
        public void KnownAwCallerRenamesWithBranchAndPreservesTheIndex()
        {
            var original = WithCaller();
            var reference = BranchSchedule.References(original, "line_01").Single();
            Assert.That(reference.CanRewrite, Is.True);
            Assert.That(reference.ActionIndex, Is.EqualTo(0));
            var renamed = BranchSchedule.RenameBranch(original, "line_01", "line_new");
            Assert.That(renamed["branches"]!["line_01"], Is.Null);
            Assert.That(renamed["predefines"]!["tokenInsts"]![1]!["overrideSkillBlackboard"]![0]!["valueStr"]!.Value<string>(), Is.EqualTo("line_new"));
            Assert.That(original["branches"]!["line_01"], Is.Not.Null);
            Assert.Throws<FormatException>(() => BranchSchedule.DeleteBranch(original, "line_01"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingOrNullCallerIndexKeepsStructuralEditsProtected(bool explicitNull)
        {
            var original = WithCaller();
            var index = (JObject)original["predefines"]!["tokenInsts"]![1]!["overrideSkillBlackboard"]![1]!;
            if (explicitNull) index["value"] = JValue.CreateNull();
            else index.Remove("value");

            Assert.That(BranchSchedule.References(original, "line_01").Single().ActionIndex, Is.EqualTo(-1));
            Assert.Throws<FormatException>(() => BranchSchedule.ChangeNode(original, "line_01", 0, 1, BranchEdit.Delete));
        }

        [Test]
        public void UnknownStringAndExternalFaustBindingProtectIdentity()
        {
            var original = Document();
            original["future"]!["branch"] = "line_01";
            Assert.That(BranchSchedule.References(original, "line_01").Single().CanRewrite, Is.False);
            Assert.Throws<FormatException>(() => BranchSchedule.RenameBranch(original, "line_01", "x"));
            var faust = BranchSchedule.AddBranch(Document(), "faust_ballis");
            ((JArray)faust["enemyDbRefs"]!).Add(JObject.Parse("{'id':'enemy_1508_faust','useDb':true}"));
            Assert.That(BranchSchedule.References(faust, "faust_ballis").Single().Kind, Does.Contain("外部"));
            Assert.Throws<FormatException>(() => BranchSchedule.RenameBranch(faust, "faust_ballis", "x"));
            Assert.Throws<FormatException>(() => BranchSchedule.DeleteBranch(faust, "faust_ballis"));
            Assert.DoesNotThrow(() => BranchSchedule.AddBranch(faust, "copy", "faust_ballis"));
        }

        [Test]
        public void UnreferencedDeleteRenameAndCollisionAreExplicit()
        {
            var original = Document();
            Assert.That(BranchSchedule.DeleteBranch(original, "line_01")["branches"]!.Count(), Is.Zero);
            Assert.That(BranchSchedule.RenameBranch(original, "line_01", "a")["branches"]!["a"], Is.Not.Null);
            Assert.That(JToken.DeepEquals(BranchSchedule.RenameBranch(original, "line_01", "line_01"), original), Is.True);
            Assert.Throws<FormatException>(() => BranchSchedule.DeleteBranch(original, "missing"));
            Assert.Throws<FormatException>(() => BranchSchedule.RenameBranch(BranchSchedule.AddBranch(original, "a"), "line_01", "a"));
        }

        [Test]
        public void PhaseAndActionOperationsKeepExtensionValues()
        {
            var original = Document();
            var copy = BranchSchedule.ChangeNode(original, "line_01", null, 0, BranchEdit.Copy);
            Assert.That(copy["branches"]!["line_01"]!["phases"]!.Count(), Is.EqualTo(2));
            var added = BranchSchedule.ChangeNode(copy, "line_01", null, 1, BranchEdit.Insert);
            Assert.That(added["branches"]!["line_01"]!["phases"]![1]!["actions"]!.Count(), Is.Zero);
            var moved = BranchSchedule.ChangeNode(original, "line_01", 0, 0, BranchEdit.Move, 1);
            Assert.That(BranchSchedule.Read(moved, "line_01", new[] { 0, 1 })["future"]!.Value<int>(), Is.EqualTo(42));
            var deleted = BranchSchedule.ChangeNode(moved, "line_01", 0, 0, BranchEdit.Delete);
            Assert.That(BranchSchedule.Read(deleted, "line_01", new[] { 0 })["actions"]!.Count(), Is.EqualTo(1));
            Assert.That(BranchSchedule.Read(original, "line_01", new[] { 0 })["actions"]!.Count(), Is.EqualTo(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => BranchSchedule.ChangeNode(original, "line_01", 0, -1, BranchEdit.Delete));
        }

        [Test]
        public void IndexedCallerBlocksOnlyOperationsThatChangeItsTarget()
        {
            var doc = WithCaller();
            Assert.Throws<FormatException>(() => BranchSchedule.ChangeNode(doc, "line_01", 0, 0, BranchEdit.Delete));
            Assert.Throws<FormatException>(() => BranchSchedule.ChangeNode(doc, "line_01", 0, 0, BranchEdit.Insert));
            Assert.Throws<FormatException>(() => BranchSchedule.ChangeNode(doc, "line_01", 0, 0, BranchEdit.Move, 1));
            Assert.DoesNotThrow(() => BranchSchedule.ChangeNode(doc, "line_01", 0, 0, BranchEdit.Copy));
            Assert.DoesNotThrow(() => BranchSchedule.ChangeNode(doc, "line_01", 0, 1, BranchEdit.Delete));
            Assert.DoesNotThrow(() => BranchSchedule.ChangeNode(doc, "line_01", 0, 2, BranchEdit.Insert));
        }

        [Test]
        public void IndexedCallerRejectsChangingItsTargetToAnUnrelatedAction()
        {
            var doc = WithCaller();
            var before = doc.ToString();
            var fields = (JObject)BranchSchedule.Read(doc, "line_01", new[] { 0, 0 }).DeepClone();
            fields["actionType"] = "PLAY_BGM";
            var error = Assert.Throws<FormatException>(() =>
                BranchSchedule.ApplyFields(doc, "line_01", new[] { 0, 0 }, fields, Enemies));
            Assert.That(error!.Message, Does.Contain("action_index"));
            Assert.That(doc.ToString(), Is.EqualTo(before));
            fields["actionType"] = "EMPTY";
            Assert.DoesNotThrow(() => BranchSchedule.ApplyFields(doc, "line_01", new[] { 0, 0 }, fields, Enemies));
            fields["actionType"] = "PLAY_BGM";
            Assert.DoesNotThrow(() => BranchSchedule.ApplyFields(Document(), "line_01", new[] { 0, 0 }, fields, Enemies));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingBranchesStillReportsExistingBlackboardCalls(bool explicitNull)
        {
            var doc = WithCaller();
            if (explicitNull) doc["branches"] = JValue.CreateNull();
            else doc.Remove("branches");
            var issues = BranchSchedule.Validate(doc, Enemies);
            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0], Does.Contain("line_01"));
            Assert.That(issues[0], Does.Contain("overrideSkillBlackboard"));
            Assert.That(BranchSchedule.Validate(new JObject(), Enemies), Is.Empty);
        }

        [Test]
        public void ApplyPhaseAndBranchFieldsLeavesChildrenIntact()
        {
            var doc = Document();
            var changed = BranchSchedule.ApplyFields(doc, "line_01", new[] { 0 }, JObject.Parse("{'preDelay':2,'future':9}"), Enemies);
            Assert.That(BranchSchedule.Read(changed, "line_01", new[] { 0 })["preDelay"]!.Value<int>(), Is.EqualTo(2));
            Assert.That(JToken.DeepEquals(BranchSchedule.Read(doc, "line_01", new[] { 0 })["actions"], BranchSchedule.Read(changed, "line_01", new[] { 0 })["actions"]), Is.True);
            Assert.Throws<FormatException>(() => BranchSchedule.ApplyFields(doc, "line_01", new[] { 0 }, JObject.Parse("{'actions':[]}"), Enemies));
            Assert.Throws<FormatException>(() => BranchSchedule.ApplyFields(doc, "line_01", new[] { 0 }, JObject.Parse("{'preDelay':null}"), Enemies));
        }

        [Test]
        public void SpawnUsesExtraRoutesAndRejectsInvalidEdits()
        {
            var doc = Document();
            var action = (JObject)BranchSchedule.Read(doc, "line_01", new[] { 0, 0 }).DeepClone();
            action["count"] = 3;
            Assert.DoesNotThrow(() => BranchSchedule.ApplyFields(doc, "line_01", new[] { 0, 0 }, action, Enemies));
            action["routeIndex"] = 1;
            var error = Assert.Throws<FormatException>(() => BranchSchedule.ApplyFields(doc, "line_01", new[] { 0, 0 }, action, Enemies));
            Assert.That(error!.Message, Does.Contain("额外路线"));
            action["routeIndex"] = 0;
            action["key"] = "missing";
            Assert.Throws<FormatException>(() => BranchSchedule.ApplyFields(doc, "line_01", new[] { 0, 0 }, action, Enemies));
            action["key"] = "enemy_a"; action["count"] = 0;
            Assert.Throws<FormatException>(() => BranchSchedule.ApplyFields(doc, "line_01", new[] { 0, 0 }, action, Enemies));
        }

        [Test]
        public void UnmodifiedLegacyActionRetainsHistoricalValues()
        {
            var doc = Document();
            var original = BranchSchedule.Read(doc, "line_01", new[] { 0, 0 });
            original["key"] = "runtime_enemy";
            original["count"] = -1;
            var fields = (JObject)original.DeepClone(); fields["future"] = 99;
            Assert.DoesNotThrow(() => BranchSchedule.ApplyFields(doc, "line_01", new[] { 0, 0 }, fields, Enemies));
            Assert.That(BranchSchedule.Validate(doc, Enemies), Is.Not.Empty);
        }

        [Test]
        public void AliasCandidatesIncludeFallbackAndSeparateCardWithdrawal()
        {
            var doc = Document();
            var all = BranchSchedule.Aliases(doc, "ACTIVATE_PREDEFINED");
            Assert.That(all.Select(alias => alias.Alias), Is.EquivalentTo(new[] { "box#1", "char_a", "hard_card" }));
            Assert.That(all.Single(alias => alias.Alias == "box#1").Hidden, Is.True);
            Assert.That(BranchSchedule.Aliases(doc, "WITHDRAW_PREDEFINED").Select(alias => alias.Alias), Is.EquivalentTo(new[] { "box#1" }));
            var action = JObject.Parse("{'actionType':'TRIGGER_PREDEFINED','key':'box#1:DefaultTrigger|char_a','preDelay':0,'interval':0}");
            Assert.DoesNotThrow(() => BranchSchedule.ApplyFields(doc, "line_01", new[] { 0, 1 }, action, Enemies));
            action["key"] = "unknown";
            Assert.Throws<FormatException>(() => BranchSchedule.ApplyFields(doc, "line_01", new[] { 0, 1 }, action, Enemies));
        }

        [Test]
        public void ValidationFindsMalformedSiblingWithoutLosingOtherDiagnostics()
        {
            var doc = Document();
            Assert.That(BranchSchedule.Validate(doc, Enemies), Is.Empty);
            doc["branches"]!["bad"] = JObject.Parse("{'phases':null}");
            BranchSchedule.Read(doc, "line_01", new[] { 0, 0 })["routeIndex"] = 8;
            var messages = BranchSchedule.Validate(doc, Enemies);
            Assert.That(messages.Count, Is.EqualTo(2));
            Assert.That(messages.Any(message => message.Contains("bad")), Is.True);
            Assert.That(messages.Any(message => message.Contains("extraRoutes") || message.Contains("额外路线")), Is.True);
        }

        [Test]
        public void PreviewAndEmptyRouteRecordsAreCheckedWithoutBecomingSpawn()
        {
            var doc = Document();
            foreach (var type in new[] { "PREVIEW_CURSOR", "EMPTY" })
            {
                // Type selection preserves the existing count; this case isolates route-library validation.
                var action = JObject.Parse("{'count':1,'preDelay':0,'interval':0,'routeIndex':0}"); action["actionType"] = type;
                Assert.DoesNotThrow(() => BranchSchedule.ApplyFields(doc, "line_01", new[] { 0, 0 }, action, Enemies));
                action["routeIndex"] = 4;
                Assert.Throws<FormatException>(() => BranchSchedule.ApplyFields(doc, "line_01", new[] { 0, 0 }, action, Enemies));
            }
        }
    }
}
