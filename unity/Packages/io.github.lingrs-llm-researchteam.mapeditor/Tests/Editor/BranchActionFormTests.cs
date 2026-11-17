#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor.Tests
{
    public sealed class BranchActionFormTests
    {
        private sealed class Host : EditorWindow { }

        private static JObject Route(int row) => JObject.Parse("{\"motionMode\":\"WALK\",\"startPosition\":{\"row\":" + row +
            ",\"col\":0},\"endPosition\":{\"row\":" + row + ",\"col\":1}}");

        private static JObject Document() => new()
        {
            ["routes"] = new JArray(Route(99)),
            ["extraRoutes"] = new JArray(Route(0), Route(1)),
            ["predefines"] = new JObject
            {
                ["tokenInsts"] = new JArray(new JObject
                {
                    ["alias"] = "gun#1", ["hidden"] = true,
                    ["inst"] = new JObject { ["characterKey"] = "trap_007_ballis" }
                }),
                ["characterCards"] = new JArray(new JObject
                {
                    ["alias"] = "", ["inst"] = new JObject { ["characterKey"] = "char_002_amiya" }
                })
            }
        };

        private static void Click(VisualElement root, string text)
        {
            var button = root.Query<Button>().ToList().First(item => item.text == text);
            Assert.That(button.enabledInHierarchy, Is.True);
            typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(button.clickable, new object?[] { null });
        }

        [UnityTest]
        public IEnumerator SpawnChoicesUseExtraRoutesAndPreserveUnknownImportedValues()
        {
            var host = ScriptableObject.CreateInstance<Host>();
            try
            {
                host.ShowUtility(); yield return null;
                var original = JObject.Parse("{\"actionType\":\"SPAWN\",\"key\":\"external_enemy\",\"routeIndex\":9,\"future\":{\"x\":1}}");
                var before = original.ToString();
                JObject? saved = null;
                var shown = -1;
                var preview = false;
                var form = new BranchActionForm(original, Document(), new[]
                { new BranchEnemyChoice { Key = "enemy_a", Label = "本关敌人 A", Path = "/enemyDbRefs/0" } },
                    value => saved = value, _ => { }, (route, play) => { shown = route; preview = play; });
                host.rootVisualElement.Add(form);
                Assert.That(saved, Is.Null);
                var routeSelect = form.Q<PopupField<string>>("branch-input-routeIndex");
                Assert.That(routeSelect.index, Is.Zero);
                Assert.That(routeSelect.choices.Skip(1).All(value => value.StartsWith("extraRoutes[")), Is.True);
                Assert.That(routeSelect.choices.Count, Is.EqualTo(3));
                routeSelect.index = 2;
                Assert.That(saved!.Value<int>("routeIndex"), Is.EqualTo(1));
                Assert.That(saved.Value<string>("key"), Is.EqualTo("external_enemy"));
                Assert.That(JToken.DeepEquals(saved["future"], original["future"]), Is.True);
                Click(form, "演示路线");
                Assert.That(shown, Is.EqualTo(1)); Assert.That(preview, Is.True);
                form.Q<PopupField<string>>("branch-input-key").index = 1;
                Assert.That(saved.Value<string>("key"), Is.EqualTo("enemy_a"));
                Assert.That(original.ToString(), Is.EqualTo(before));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [UnityTest]
        public IEnumerator PresetAliasesLocateRecordsAndWithdrawExcludesCards()
        {
            var host = ScriptableObject.CreateInstance<Host>();
            try
            {
                host.ShowUtility(); yield return null;
                JObject? saved = null;
                var located = "";
                var form = new BranchActionForm(JObject.Parse("{\"actionType\":\"ACTIVATE_PREDEFINED\",\"key\":\"gun#1\"}"),
                    Document(), Array.Empty<BranchEnemyChoice>(), value => saved = value, path => located = path, (_, _) => { });
                host.rootVisualElement.Add(form);
                Assert.That(saved, Is.Null);
                var select = form.Q<PopupField<string>>("branch-input-key");
                Assert.That(select.choices.Count, Is.EqualTo(3));
                var locate = form.Query<Button>().ToList().First(button => button.text.StartsWith("定位 gun#1"));
                Click(form, locate.text);
                Assert.That(located, Is.EqualTo("/predefines/tokenInsts/0"));
                select.index = 2;
                Assert.That(saved!.Value<string>("key"), Is.EqualTo("char_002_amiya"));
                form.Q<PopupField<string>>("branch-input-actionType").value = "WITHDRAW_PREDEFINED";
                select = form.Q<PopupField<string>>("branch-input-key");
                Assert.That(select.index, Is.Zero);
                Assert.That(select.choices.Count, Is.EqualTo(2));
                Assert.That(saved.Value<string>("key"), Is.EqualTo("char_002_amiya"));
                Assert.That(select.choices[1], Does.Contain("gun#1"));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [UnityTest]
        public IEnumerator TriggerCandidateEditsPreserveOtherAliasesAndAbilities()
        {
            var host = ScriptableObject.CreateInstance<Host>();
            try
            {
                host.ShowUtility(); yield return null;
                JObject? saved = null;
                var original = JObject.Parse("{\"actionType\":\"TRIGGER_PREDEFINED\",\"key\":\"gun#1:Fire|runtime_alias\",\"extension\":5}");
                var form = new BranchActionForm(original, Document(), Array.Empty<BranchEnemyChoice>(),
                    value => saved = value, _ => { }, (_, _) => { });
                host.rootVisualElement.Add(form);
                Assert.That(saved, Is.Null);
                Assert.That(form.Q<TextField>("branch-trigger-ability-0").value, Is.EqualTo("Fire"));
                form.Q<TextField>("branch-trigger-ability-0").value = "Reload";
                Assert.That(saved!.Value<string>("key"), Is.EqualTo("gun#1:Reload|runtime_alias"));
                form.Q<TextField>("branch-trigger-alias-1").value = "second";
                Assert.That(saved.Value<string>("key"), Is.EqualTo("gun#1:Reload|second"));
                Click(form, "删除候选");
                Assert.That(saved.Value<string>("key"), Is.EqualTo("second"));
                Click(form, "新增候选");
                Assert.That(saved.Value<string>("key"), Is.EqualTo("second|gun#1"));
                Assert.That(saved.Value<int>("extension"), Is.EqualTo(5));
                Assert.That(original.Value<string>("key"), Is.EqualTo("gun#1:Fire|runtime_alias"));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [UnityTest]
        public IEnumerator InvalidRawDraftStaysVisibleUntilParsedAndNullIsDistinctFromMissing()
        {
            var host = ScriptableObject.CreateInstance<Host>();
            try
            {
                host.ShowUtility(); yield return null;
                JObject? saved = null;
                var rawDraft = "";
                var form = new BranchActionForm(JObject.Parse("{\"actionType\":\"SPAWN\",\"count\":1}"), Document(),
                    Array.Empty<BranchEnemyChoice>(), value => saved = value, _ => { }, (_, _) => { }, text => rawDraft = text);
                host.rootVisualElement.Add(form);
                var raw = form.Q<TextField>("branch-action-json");
                raw.value = "{\"count\":";
                Assert.That(rawDraft, Is.EqualTo(raw.value));
                Assert.That(form.Q("branch-action-fields").enabledInHierarchy, Is.False);
                Click(form, "读取为待应用字段");
                Assert.That(saved, Is.Null);
                Assert.That(raw.value, Is.EqualTo(rawDraft));
                Assert.That(form.Q<Label>("branch-action-error").text, Is.Not.Empty);
                raw.value = "{\"actionType\":\"SPAWN\",\"count\":2,\"unknown\":[1,2]}";
                Click(form, "读取为待应用字段");
                Assert.That(form.Q("branch-action-fields").enabledInHierarchy, Is.True);
                Assert.That(saved!.Value<int>("count"), Is.EqualTo(2));
                Assert.That(saved["unknown"]!.Count(), Is.EqualTo(2));
                Click(form.Q("branch-field-hiddenGroup"), "设为 null");
                Assert.That(saved["hiddenGroup"]!.Type, Is.EqualTo(JTokenType.Null));
                Click(form.Q("branch-field-hiddenGroup"), "移除字段");
                Assert.That(saved.Property("hiddenGroup"), Is.Null);
                form.Q<TextField>("branch-input-count").value = "invalid";
                Assert.That(form.Q<TextField>("branch-input-count").value, Is.EqualTo("invalid"));
                Assert.That(saved.Value<string>("count"), Is.EqualTo("invalid"));
                form.Q<TextField>("branch-input-count").value = "3";
                Assert.That(saved.Value<int>("count"), Is.EqualTo(3));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }
        [UnityTest]
        public IEnumerator SharedMainFormKeepsEnemyAndRouteControlsAliveAndUsesMainRoutes()
        {
            var host = ScriptableObject.CreateInstance<Host>();
            try
            {
                host.ShowUtility(); yield return null;
                JObject? saved = null; string shown = "";
                var form = new ActionDataForm(JObject.Parse("{\"actionType\":\"SPAWN\",\"key\":\"old\",\"count\":1}"),
                    Document(), new[] { new BranchEnemyChoice { Key = "enemy_a", Label = "Enemy A", Path = "/enemyDbRefs/0" } },
                    value => saved = value, _ => { }, (library, _, _) => shown = library, false, prefix: "waves");
                host.rootVisualElement.Add(form);
                var enemy = form.Q<PopupField<string>>("waves-input-key");
                var route = form.Q<PopupField<string>>("waves-input-routeIndex");
                Assert.That(route.choices.Skip(1).All(value => value.StartsWith("routes[")), Is.True);
                enemy.index = 1; route.index = 1;
                Assert.That(form.Q<PopupField<string>>("waves-input-routeIndex"), Is.SameAs(route));
                Assert.That(saved!.Value<string>("key"), Is.EqualTo("enemy_a"));
                Assert.That(saved.Value<int>("routeIndex"), Is.Zero);
                Click(form, "演示路线"); Assert.That(shown, Is.EqualTo("routes"));
                Assert.That(form.Q("waves-field-forceBlockWaveInBranch"), Is.Null);
                Click(form, "全部字段");
                Assert.That(form.Q("waves-field-forceBlockWaveInBranch"), Is.Not.Null);
                Assert.That(form.Query<VisualElement>(className: "wave-field").ToList().Count, Is.EqualTo(WaveSchedule.ActionFields.Length));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [UnityTest]
        public IEnumerator SameGroupPreviewUsesOnlyProvidedSiblingsAndCurrentDraft()
        {
            var host = ScriptableObject.CreateInstance<Host>();
            try
            {
                host.ShowUtility(); yield return null;
                var initial = JObject.Parse("{\"actionType\":\"SPAWN\",\"key\":\"old\",\"randomSpawnGroupKey\":\"a\"}");
                var siblings = new JArray(initial.DeepClone(), JObject.Parse("{\"key\":\"peer\",\"randomSpawnGroupKey\":\"b\",\"weight\":2}"));
                var form = new ActionDataForm(initial, Document(), Array.Empty<BranchEnemyChoice>(), _ => { }, _ => { },
                    (_, _, _) => { }, true, siblingActions: siblings, actionIndex: 0);
                host.rootVisualElement.Add(form);
                var group = form.Query<Foldout>().ToList().Single(item => item.text == "同组动作"); group.value = true;
                form.Q<TextField>("action-input-randomSpawnGroupKey").value = "b";
                Assert.That(group.value, Is.True);
                var labels = group.Query<Label>().ToList().Select(label => label.text).ToArray();
                Assert.That(labels.Any(label => label.Contains("peer")), Is.True);
                Assert.That(labels.Any(label => label.Contains("old") && label.Contains("group · b")), Is.True);
                Assert.That(siblings[0]!.Value<string>("randomSpawnGroupKey"), Is.EqualTo("a"));
                Click(form, "全部字段");
                Assert.That(form.Query<Foldout>().ToList().Single(item => item.text == "同组动作").value, Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }
        [UnityTest]
        public IEnumerator FailedPersistenceKeepsRawPendingAndTriggerCandidatesTransactional()
        {
            var host = ScriptableObject.CreateInstance<Host>();
            try
            {
                host.ShowUtility(); yield return null;
                var fail = true; JObject? saved = null;
                var form = new BranchActionForm(JObject.Parse("{\"actionType\":\"TRIGGER_PREDEFINED\",\"key\":\"first:a|second:b\"}"),
                    Document(), Array.Empty<BranchEnemyChoice>(), value =>
                    { if (fail) throw new InvalidOperationException("locked"); saved = value; }, _ => { }, (_, _) => { },
                    _ => throw new InvalidOperationException("raw persistence failed"));
                host.rootVisualElement.Add(form);
                form.Q<TextField>("branch-trigger-ability-0").value = "failed";
                Assert.That(saved, Is.Null);
                fail = false;
                form.Q<TextField>("branch-trigger-ability-1").value = "accepted";
                Assert.That(saved!.Value<string>("key"), Is.EqualTo("first:a|second:accepted"));
                form.Q<TextField>("branch-action-json").value = "{";
                Assert.That(form.HasPendingRaw, Is.True);
                Assert.That(form.Q("branch-action-fields").enabledSelf, Is.False);
                Assert.That(form.Q<TextField>("branch-action-json").value, Is.EqualTo("{"));
                Assert.That(form.Q<Label>("branch-action-error").text, Does.Contain("raw persistence failed"));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }
    }
}
