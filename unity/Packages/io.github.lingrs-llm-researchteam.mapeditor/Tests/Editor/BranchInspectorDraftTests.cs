#nullable enable
using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Arknights.MapEditor.Editor.Tests
{
    public sealed class BranchInspectorDraftTests
    {
        private static InspectorDraftStore Draft(string document, string key, string raw = "{\"preDelay\":7}")
        {
            var result = new InspectorDraftStore();
            result.SetDraft(BranchInspectorDrafts.Slot(key), document, key, "{\"preDelay\":0}", raw);
            return result;
        }

        private static void AssertDraft(InspectorDraftStore store, string document, string key, string raw)
        {
            var slot = BranchInspectorDrafts.Slot(key);
            Assert.That(BranchInspectorDrafts.Target(store, slot), Is.EqualTo(key));
            Assert.That(BranchInspectorDrafts.IsCurrent(store, slot, document, key), Is.True);
            Assert.That(store.TryRead(slot, out var actual), Is.True);
            Assert.That(actual, Is.EqualTo(raw));
        }

        [Test]
        public void AddressesPreserveBranchPunctuationAndUseBoundedSlots()
        {
            var branch = "名/称~[0]:" + new string('x', 500);
            var key = BranchInspectorDrafts.Key(branch, 2, 3);
            Assert.That(BranchInspectorDrafts.ReadTarget(key, out var name, out var address), Is.True);
            Assert.That(name, Is.EqualTo(branch));
            Assert.That(address, Is.EqualTo(new[] { 2, 3 }));
            Assert.That(BranchInspectorDrafts.Slot(key).Length, Is.LessThan(256));
            AssertDraft(Draft("before", key), "before", key, "{\"preDelay\":7}");
            Assert.That(BranchInspectorDrafts.ReadTarget("[\"x\",-1]", out _, out _), Is.False);
            Assert.That(BranchInspectorDrafts.ReadTarget("[\"x\",2147483648]", out _, out _), Is.False);
            Assert.That(BranchInspectorDrafts.ReadTarget("[\"x\",0,1,2]", out _, out _), Is.False);
            Assert.That(BranchInspectorDrafts.ReadTarget("[", out _, out _), Is.False);
            var dateKey = BranchInspectorDrafts.Key("2026-11-14T00:00:00Z", 0);
            Assert.That(BranchInspectorDrafts.ReadTarget(dateKey, out var dateName, out _), Is.True);
            Assert.That(dateName, Is.EqualTo("2026-11-14T00:00:00Z"));
        }

        [Test]
        public void RenameRebindsDescendantsAndLeavesOtherModulesUntouched()
        {
            var key = BranchInspectorDrafts.Key("original", 0, 1);
            var store = Draft("before", key);
            store.SetDraft("enemy", "before", "enemy", "old", "edited");
            var step = new BranchHistoryStep(BranchHistoryKind.Rename, "original", newBranch: "renamed");
            step.Bind("revision1", "revision2");
            Assert.That(step.Connects("revision2", "revision1"), Is.True);
            Assert.That(step.IsForward("revision1"), Is.True);
            var next = BranchInspectorDrafts.Rebase(store, "before", "after", address => step.Remap(address, true));
            AssertDraft(next, "after", BranchInspectorDrafts.Key("renamed", 0, 1), "{\"preDelay\":7}");
            Assert.That(next.TryRead("enemy", out var untouched), Is.True);
            Assert.That(untouched, Is.EqualTo("edited"));
            AssertDraft(store, "before", key, "{\"preDelay\":7}");
            var undo = BranchInspectorDrafts.Rebase(next, "after", "before", address => step.Remap(address, false));
            AssertDraft(undo, "before", key, "{\"preDelay\":7}");
        }

        [TestCase(-1)]
        [TestCase(0)]
        public void NonAdjacentMoveHasExactInverse(int phase)
        {
            var step = new BranchHistoryStep(BranchHistoryKind.Move, "branch", phase, 0, 3);
            for (var index = 0; index < 5; index++)
            {
                var key = phase < 0 ? BranchInspectorDrafts.Key("branch", index, 2) :
                    BranchInspectorDrafts.Key("branch", phase, index);
                var forward = step.Remap(key, true)!;
                Assert.That(step.Remap(forward, false), Is.EqualTo(key));
            }
            var other = BranchInspectorDrafts.Key("other", 0, 0);
            Assert.That(step.Remap(other, true), Is.EqualTo(other));
        }

        [Test]
        public void DeleteUndoAndRedoRestoreOnlyTheCorrespondingDetachedDraft()
        {
            var key = BranchInspectorDrafts.Key("branch", 1, 0);
            var store = Draft("before", key);
            var step = new BranchHistoryStep(BranchHistoryKind.Delete, "branch", index: 1);
            var removed = BranchInspectorDrafts.Rebase(store, "before", "after", address => step.Remap(address, true),
                detach: step.Token(true), restore: step.Token(false));
            Assert.That(BranchInspectorDrafts.Saved(removed).Single(), Does.Contain("detached:"));
            var restored = BranchInspectorDrafts.Rebase(removed, "after", "before", address => step.Remap(address, false),
                detach: step.Token(false), restore: step.Token(true));
            AssertDraft(restored, "before", key, "{\"preDelay\":7}");
            var redo = BranchInspectorDrafts.Rebase(restored, "before", "after", address => step.Remap(address, true),
                detach: step.Token(true), restore: step.Token(false));
            Assert.That(BranchInspectorDrafts.Saved(redo).Count(), Is.EqualTo(1));
            Assert.That(BranchInspectorDrafts.Target(redo, BranchInspectorDrafts.Saved(redo).Single()), Is.Null);
        }

        [Test]
        public void CopyAndInsertShiftFollowingAddressesWithoutDuplicatingDrafts()
        {
            var key = BranchInspectorDrafts.Key("branch", 0, 1);
            foreach (var kind in new[] { BranchHistoryKind.Insert, BranchHistoryKind.Copy })
            {
                var step = new BranchHistoryStep(kind, "branch", 0, 1, 1);
                var changed = step.Remap(key, true)!;
                Assert.That(changed, Is.EqualTo(BranchInspectorDrafts.Key("branch", 0, 2)));
                Assert.That(step.Remap(changed, false), Is.EqualTo(key));
                Assert.That(step.Remap(key, false), Is.Null);
            }
        }

        [Test]
        public void AppliedAndStaleDraftsAreHandledSeparately()
        {
            var key = BranchInspectorDrafts.Key("branch", 0);
            var store = Draft("before", key);
            var staleKey = BranchInspectorDrafts.Key("branch", 1);
            store.SetDraft(BranchInspectorDrafts.Slot(staleKey), "old-revision", staleKey, "0", "1");
            var next = BranchInspectorDrafts.Rebase(store, "before", "after", address => address, applied: key);
            Assert.That(next.HasChanges(BranchInspectorDrafts.Slot(key)), Is.False);
            AssertDraft(next, "old-revision", staleKey, "1");
        }

        [Test]
        public void UndoFieldChangePreservesOnlyLaterLocalEdits()
        {
            var key = BranchInspectorDrafts.Key("branch", 0, 0);
            var document = JObject.Parse("{branches:{branch:{phases:[{preDelay:0,actions:[{preDelay:0,count:1,routeIndex:2}]}]}}}");
            var step = new BranchHistoryStep(BranchHistoryKind.Fields, "branch", 0, 0);
            var result = step.RebaseFields(key, "{\"preDelay\":5,\"count\":1,\"routeIndex\":2}",
                "{\"preDelay\":5,\"count\":9,\"routeIndex\":2}", document);
            Assert.That(result.HasValue, Is.True);
            var merged = JObject.Parse(result!.Value.Raw);
            Assert.That((int)merged["preDelay"]!, Is.Zero);
            Assert.That((int)merged["count"]!, Is.EqualTo(9));
            Assert.That((int)merged["routeIndex"]!, Is.EqualTo(2));
            Assert.That(JToken.DeepEquals(JObject.Parse(result.Value.Initial),
                document["branches"]!["branch"]!["phases"]![0]!["actions"]![0]), Is.True);
        }

        [Test]
        public void InvalidRawJsonIsKeptWhenBaselineUnchangedAndDetachedWhenItChanges()
        {
            var key = BranchInspectorDrafts.Key("branch", 0);
            var document = JObject.Parse("{branches:{branch:{phases:[{preDelay:0,actions:[]}]}}}");
            var step = new BranchHistoryStep(BranchHistoryKind.Fields, "branch", 0);
            var baseline = "{\"preDelay\":0}";
            Assert.That(step.RebaseFields(key, baseline, "{", document)!.Value.Raw, Is.EqualTo("{"));
            Assert.That(step.RebaseFields(key, "{\"preDelay\":5}", "{", document), Is.Null);
        }

        [Test]
        public void CreateUndoAndWholeBranchDeleteDetachAllDescendants()
        {
            var key = BranchInspectorDrafts.Key("branch", 1, 2);
            Assert.That(new BranchHistoryStep(BranchHistoryKind.Create, "branch").Remap(key, false), Is.Null);
            Assert.That(new BranchHistoryStep(BranchHistoryKind.DeleteBranch, "branch").Remap(key, true), Is.Null);
            Assert.That(BranchInspectorDrafts.ReadRecord(new JObject(), key), Is.Null);
        }

        [Test]
        public void MovingTwoDraftsDoesNotOverwriteEitherSlot()
        {
            var first = BranchInspectorDrafts.Key("branch", 0, 0);
            var second = BranchInspectorDrafts.Key("branch", 1, 0);
            var store = Draft("before", first, "{\"preDelay\":7}");
            store.SetDraft(BranchInspectorDrafts.Slot(second), "before", second,
                "{\"preDelay\":0}", "{\"preDelay\":9}");
            var step = new BranchHistoryStep(BranchHistoryKind.Move, "branch", index: 0, target: 1);
            var moved = BranchInspectorDrafts.Rebase(store, "before", "after", address => step.Remap(address, true));
            AssertDraft(moved, "after", second, "{\"preDelay\":7}");
            AssertDraft(moved, "after", first, "{\"preDelay\":9}");
            Assert.That(BranchInspectorDrafts.Saved(moved).Count(), Is.EqualTo(2));
        }

        [Test]
        public void UndoInsertionRetainsNewNodeDraftForRedo()
        {
            var key = BranchInspectorDrafts.Key("branch", 0, 1);
            var store = Draft("after", key);
            var step = new BranchHistoryStep(BranchHistoryKind.Insert, "branch", 0, 1);
            var undone = BranchInspectorDrafts.Rebase(store, "after", "before", address => step.Remap(address, false),
                detach: step.Token(false), restore: step.Token(true));
            Assert.That(BranchInspectorDrafts.Target(undone, BranchInspectorDrafts.Saved(undone).Single()), Is.Null);
            var redone = BranchInspectorDrafts.Rebase(undone, "before", "after", address => step.Remap(address, true),
                detach: step.Token(true), restore: step.Token(false));
            AssertDraft(redone, "after", key, "{\"preDelay\":7}");
        }

        [Test]
        public void PendingActionFollowsItsPhaseAndBranchButNotActionIndices()
        {
            var pending = BranchInspectorDrafts.Key("branch", 0, int.MaxValue);
            foreach (var operation in new[] { BranchHistoryKind.Insert, BranchHistoryKind.Copy,
                BranchHistoryKind.Move, BranchHistoryKind.Delete })
            {
                var actionStep = new BranchHistoryStep(operation, "branch", 0, 0, 1);
                Assert.That(actionStep.Remap(pending, true), Is.EqualTo(pending));
                Assert.That(actionStep.Remap(pending, false), Is.EqualTo(pending));
            }
            var phaseStep = new BranchHistoryStep(BranchHistoryKind.Move, "branch", index: 0, target: 2);
            var moved = phaseStep.Remap(pending, true)!;
            Assert.That(moved, Is.EqualTo(BranchInspectorDrafts.Key("branch", 2, int.MaxValue)));
            Assert.That(phaseStep.Remap(moved, false), Is.EqualTo(pending));
            var rename = new BranchHistoryStep(BranchHistoryKind.Rename, "branch", newBranch: "new/name");
            Assert.That(rename.Remap(pending, true), Is.EqualTo(BranchInspectorDrafts.Key("new/name", 0, int.MaxValue)));
        }

        [Test]
        public void PendingActionPreservesRawUntilItsParentPhaseIsDeleted()
        {
            var pending = BranchInspectorDrafts.Key("branch", 0, int.MaxValue);
            var document = JObject.Parse("{branches:{branch:{phases:[{preDelay:0,actions:[]}]}}}");
            var step = new BranchHistoryStep(BranchHistoryKind.Delete, "branch", index: 0);
            Assert.That(BranchInspectorDrafts.ReadRecord(document, pending), Is.Null);
            Assert.That(step.RebaseFields(pending, "", "{", document)!.Value.Raw, Is.EqualTo("{"));
            var store = new InspectorDraftStore();
            store.SetDraft(BranchInspectorDrafts.Slot(pending), "before", pending, "", "{");
            var deleted = BranchInspectorDrafts.Rebase(store, "before", "after", address => step.Remap(address, true),
                detach: step.Token(true), restore: step.Token(false));
            var restored = BranchInspectorDrafts.Rebase(deleted, "after", "before", address => step.Remap(address, false),
                detach: step.Token(false), restore: step.Token(true));
            AssertDraft(restored, "before", pending, "{");
            document["branches"]!["branch"]!["phases"] = new JArray();
            Assert.That(step.RebaseFields(pending, "", "{", document), Is.Null);
        }
    }
}
