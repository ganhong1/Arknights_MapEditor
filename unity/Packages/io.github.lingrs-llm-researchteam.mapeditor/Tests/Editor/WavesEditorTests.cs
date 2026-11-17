#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor.Tests
{
    public sealed class WavesEditorTests
    {
        private const string Body510 = "9ade453a5ba2c8325ec5c1dc03f6f3a4590cf9681861be6949d2ce4135c07f26";

        private sealed class Confirm : IEditorFileDialogs
        {
            internal bool Accept = true;
            public string Open(string title, string directory, string extension) => "";
            public string Save(string title, string directory, string name, string extension) => "";
            public bool Discard(string title, string message) => Accept;
        }

        private static LevelDocument Original510()
        {
            var catalog = FinishedLevelCatalog.Load(FinishedLevelCatalog.BundledPath());
            var variant = catalog.Groups.SelectMany(group => group.Variants)
                .First(item => item.Stage?.Id == "main_05-10" && item.Source.Body == Body510);
            using var archive = catalog.OpenBodies();
            return LevelDocument.Parse(catalog.ReadBody(archive, variant.Source.Body));
        }

        private static void Click(VisualElement parent, string text)
        {
            var button = parent.Query<Button>().ToList().First(item => item.text == text);
            Assert.That(button.enabledInHierarchy, Is.True, text);
            Invoke(button);
        }

        private static void Invoke(Button button)
        {
            typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(button.clickable, new object?[] { null });
        }

        private static Foldout Open(VisualElement root, string key)
        {
            var fold = root.Q<Foldout>("waves-node-" + key);
            Assert.That(fold, Is.Not.Null, key);
            fold.value = true;
            return fold;
        }

        private static JArray Actions(MapEditorWindow window) =>
            (JArray)window.WorkspaceDocument.GetSection("waves")![0]!["fragments"]![0]!["actions"]!;

        private static LevelDocument WithActions(params JObject[] records)
        {
            var fragment = new JObject { ["preDelay"] = 1.25, ["actions"] = new JArray(records.Cast<JToken>()) };
            var wave = WaveSchedule.NewNode(1);
            wave["fragments"] = new JArray(fragment);
            var route = RouteEditDraft.Create("WALK");
            route["startPosition"] = new JObject { ["row"] = 0, ["col"] = 0 };
            route["endPosition"] = new JObject { ["row"] = 2, ["col"] = 2 };
            return LevelDocument.Create(4, 4).WithSection("waves", new JArray(wave).ToString())
                .WithSection("routes", new JArray(route).ToString())
                .WithSection("enemies", "[{\"alias\":\"local\",\"key\":\"enemy_1007_slime\",\"name\":\"局部敌人\"}]");
        }

        private static JObject Spawn(int count = 1)
        {
            var action = WaveSchedule.NewNode(3);
            action["key"] = "local";
            action["routeIndex"] = 0;
            action["count"] = count;
            return action;
        }

        private static void Redo(MapEditorWindow editor) =>
            typeof(MapEditorWindow).GetMethod("StepEditorHistory", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(editor, new object[] { true });

        private static void ReadRawAction(VisualElement root, Action<JObject> change)
        {
            var fold = root.Query<Foldout>().ToList().Single(item => item.text == "原始动作 JSON");
            fold.value = true;
            var input = fold.Q<TextField>("waves-action-json");
            var action = JObject.Parse(input.value);
            change(action);
            input.value = action.ToString();
            Click(fold, "读取为待应用字段");
        }

        [TestCase("count", "null")]
        [TestCase("count", "-1")]
        [TestCase("count", "2147483648")]
        [TestCase("preDelay", "-0.1")]
        [TestCase("interval", "1e50")]
        [TestCase("blockFragment", "\"true\"")]
        [TestCase("managedByScheduler", "null")]
        [TestCase("hiddenGroup", "[]")]
        public void ExistingSpawnRejectsChangedInvalidFields(string field, string json)
        {
            var original = Spawn();
            var edited = (JObject)original.DeepClone();
            edited[field] = JToken.Parse(json);
            Assert.Throws<FormatException>(() => WaveSchedule.RequireSpawnTargets(edited, new[] { "local" },
                (JArray)WithActions().GetSection("routes")!, original));
        }

        [Test]
        public void ExistingHistoricalValuesSurviveUnrelatedEditButChangingTypeChecksTargets()
        {
            var previous = JObject.Parse("{\"actionType\":\"SPAWN\",\"key\":\"from_mode\",\"routeIndex\":999,\"count\":null}");
            var edited = (JObject)previous.DeepClone();
            edited["autoPreviewRoute"] = true;
            WaveSchedule.RequireSpawnTargets(edited, Array.Empty<string>(), null, previous);
            previous["actionType"] = "DISPLAY_ENEMY_INFO";
            Assert.Throws<FormatException>(() => WaveSchedule.RequireSpawnTargets(edited, Array.Empty<string>(), null, previous));
            var wave = JObject.Parse("{\"maxTimeWaitingForNextWave\":-1,\"preDelay\":0}");
            var fields = (JObject)wave.DeepClone();
            fields["preDelay"] = JValue.CreateNull();
            Assert.Throws<FormatException>(() => WaveSchedule.RequireWaveFields(fields, wave, 1));
            fields.Remove("preDelay");
            Assert.Throws<FormatException>(() => WaveSchedule.RequireWaveFields(fields, wave, 1));
        }

        [Test]
        public void RawActionsCannotRemoveRequiredFieldsOrIntroduceUnusableReferences()
        {
            var document = WithActions();
            var routes = (JArray)document.GetSection("routes")!;
            var preview = JObject.Parse("{\"actionType\":1,\"routeIndex\":0,\"preDelay\":0,\"interval\":0}");
            var edited = (JObject)preview.DeepClone();
            edited["routeIndex"] = 999;
            Assert.Throws<FormatException>(() => WaveSchedule.RequireActionFields(edited, preview, routes));
            edited["routeIndex"] = 0;
            edited["actionType"] = "E_NUM";
            Assert.Throws<FormatException>(() => WaveSchedule.RequireActionFields(edited, preview, routes));
            edited.Remove("actionType");
            Assert.Throws<FormatException>(() => WaveSchedule.RequireActionFields(edited, preview, routes));
            edited["actionType"] = "SHOW_ALL_HIDDEN_CARDS";
            WaveSchedule.RequireActionFields(edited, preview, routes);
            edited.Remove("interval");
            Assert.Throws<FormatException>(() => WaveSchedule.RequireActionFields(edited, preview, routes));
            var original = Spawn();
            var spawn = (JObject)original.DeepClone();
            spawn.Remove("preDelay");
            Assert.Throws<FormatException>(() => WaveSchedule.RequireSpawnTargets(spawn, new[] { "local" }, routes, original));
            Assert.That(document.WithSection("waves", "null").ValidateWaveReferences(new[] { "local" }),
                Has.Some.Matches<LevelDiagnostic>(d => d.Path == "$.waves" && d.Severity == "error"));
        }

        [UnityTest]
        public IEnumerator CheckReferencesSurvivesMalformedBranchAndTimelineIncludesHiddenCards()
        {
            var badRoute = Spawn();
            badRoute["routeIndex"] = 999;
            var card = JObject.Parse("{\"actionType\":12,\"preDelay\":0.75,\"blockFragment\":true}");
            var sentinel = JObject.Parse("{\"actionType\":14,\"preDelay\":0}");
            var document = WithActions(badRoute, card, sentinel).WithSection("branches", "{\"broken\":null}");
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            var editor = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                editor.LoadWorkspaceDocument(workspace, "校验", document.ToJson(), "", "");
                editor.ShowUtility();
                yield return null;
                editor.CreateGUI();
                var root = editor.rootVisualElement;
                Invoke(root.Q<Button>("workbench-tab-2"));
                var panel = root.Q("waves-editor");
                Click(panel, "检查引用");
                var diagnostics = panel.Q<Label>("waves-message").text;
                Assert.That(diagnostics, Does.Contain("actions[0].routeIndex"));
                Assert.That(diagnostics, Does.Not.Contain("broken"));
                Assert.That(document.ValidateReferences(), Has.Some.Matches<LevelDiagnostic>(d => d.Path.Contains("broken")));
                Open(panel, "0");
                var fragment = Open(panel, "0-0");
                var timing = fragment.Query<Foldout>().ToList().Single(fold => fold.text.StartsWith("片段配置时间"));
                timing.value = true;
                Assert.That(timing.Query<Label>().ToList().Select(row => row.text),
                    Has.Some.Matches<string>(row => row.Contains("SHOW_ALL_HIDDEN_CARDS") && row.Contains("2 s")));
                Assert.That(timing.Query<Label>().ToList().Select(row => row.text), Has.None.Contains("E_NUM"));
                Assert.That(timing.Query<VisualElement>(className: "wave-time-marker").ToList().Count, Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(workspace);
            }
        }

        [UnityTest]
        public IEnumerator SelectorsSkipUnresolvedReferencesAndRawValidationPreservesDraft()
        {
            var document = WithActions(Spawn())
                .WithSection("enemyDbRefs", "[{\"id\":\"local\",\"useDb\":true,\"level\":0},{\"id\":\"enemy_1007_slime\",\"useDb\":true,\"level\":999}]");
            var routes = (JArray)document.GetSection("routes")!;
            routes.Add(new JObject());
            var invalidRoute = (JObject)routes[0].DeepClone();
            invalidRoute["motionMode"] = 2;
            routes.Add(invalidRoute);
            document = document.WithSection("routes", routes.ToString());
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            var editor = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                editor.LoadWorkspaceDocument(workspace, "选择器", document.ToJson(), "", "");
                editor.ShowUtility();
                yield return null;
                editor.CreateGUI();
                var root = editor.rootVisualElement;
                Invoke(root.Q<Button>("workbench-tab-2"));
                var panel = root.Q("waves-editor");
                Open(panel, "0");
                Open(panel, "0-0");
                var action = Open(panel, "0-0-0");
                var enemy = action.Q<PopupField<string>>("waves-input-key");
                Assert.That(enemy.choices.Count, Is.EqualTo(2));
                Assert.That(enemy.value, Does.Contain("enemies[0]"));
                Assert.That(action.Q<PopupField<string>>("waves-input-routeIndex").choices.Count, Is.EqualTo(2));
                Click(action, "全部字段");
                action = Open(panel, "0-0-0");
                Assert.That(action.Q("waves-field-randomSpawnGroupKey"), Is.Not.Null);
                Click(action, "适用字段");
                action = Open(panel, "0-0-0");
                ReadRawAction(action, raw => raw["count"] = -1);
                action = Open(panel, "0-0-0");
                Click(action, "应用字段");
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(document.ToJson()));
                Assert.That(panel.Q<Label>("waves-message").text, Does.Contain("count"));
                Assert.That(action.Q<TextField>("waves-input-count").value, Is.EqualTo("-1"));
                action.Q<TextField>("waves-input-count").value = "3";
                Click(action, "应用字段");
                Assert.That(Actions(editor)[0].Value<int>("count"), Is.EqualTo(3));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(workspace);
            }
        }

        [UnityTest]
        public IEnumerator ReorderDeleteUndoRedoKeepDraftAttachedAndKeepLaterEdits()
        {
            var document = WithActions(Spawn(1), Spawn(2));
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            var editor = ScriptableObject.CreateInstance<MapEditorWindow>();
            editor.Dialogs = new Confirm();
            try
            {
                editor.LoadWorkspaceDocument(workspace, "历史", document.ToJson(), "", "");
                editor.ShowUtility();
                yield return null;
                editor.CreateGUI();
                var root = editor.rootVisualElement;
                Invoke(root.Q<Button>("workbench-tab-2"));
                var panel = root.Q("waves-editor");
                Open(panel, "0");
                Open(panel, "0-0");
                Open(panel, "0-0-1").Q<TextField>("waves-input-count").value = "7";
                Click(Open(panel, "0-0-0"), "下移");
                Assert.That(Open(panel, "0-0-0").Q<TextField>("waves-input-count").value, Is.EqualTo("7"));
                Open(panel, "0-0-0").Q<TextField>("waves-input-count").value = "8";
                editor.UndoFromShortcut();
                Assert.That(Actions(editor)[0].Value<int>("count"), Is.EqualTo(1));
                Assert.That(Open(panel, "0-0-1").Q<TextField>("waves-input-count").value, Is.EqualTo("8"));
                Redo(editor);
                Assert.That(Actions(editor)[0].Value<int>("count"), Is.EqualTo(2));
                Assert.That(Open(panel, "0-0-0").Q<TextField>("waves-input-count").value, Is.EqualTo("8"));
                Click(Open(panel, "0-0-0"), "删除");
                Assert.That(Actions(editor).Count, Is.EqualTo(1));
                Assert.That(panel.Q("waves-recovered-drafts").childCount, Is.EqualTo(1));
                editor.UndoFromShortcut();
                Assert.That(Actions(editor).Count, Is.EqualTo(2));
                var restored = Open(panel, "0-0-0");
                Assert.That(restored.Q<TextField>("waves-input-count").value, Is.EqualTo("8"));
                Assert.That(panel.Q("waves-recovered-drafts").childCount, Is.Zero);
                Click(restored, "应用字段");
                Assert.That(Actions(editor)[0].Value<int>("count"), Is.EqualTo(8));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(workspace);
            }
        }

        [UnityTest]
        public IEnumerator UndoAppliedFieldPreservesOnlyLaterDraftChanges()
        {
            var document = WithActions(Spawn());
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            var editor = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                editor.LoadWorkspaceDocument(workspace, "应用记录", document.ToJson(), "", "");
                editor.ShowUtility();
                yield return null;
                editor.CreateGUI();
                var root = editor.rootVisualElement;
                Invoke(root.Q<Button>("workbench-tab-2"));
                var panel = root.Q("waves-editor");
                Open(panel, "0");
                Open(panel, "0-0");
                var action = Open(panel, "0-0-0");
                action.Q<TextField>("waves-input-count").value = "2";
                Click(action, "应用字段");
                Open(panel, "0-0-0").Q<TextField>("waves-input-interval").value = "0.25";
                editor.UndoFromShortcut();
                action = Open(panel, "0-0-0");
                Assert.That(action.Q<TextField>("waves-input-count").value, Is.EqualTo("1"));
                Assert.That(action.Q<TextField>("waves-input-interval").value, Is.EqualTo("0.25"));
                Redo(editor);
                action = Open(panel, "0-0-0");
                Assert.That(action.Q<TextField>("waves-input-count").value, Is.EqualTo("2"));
                Assert.That(action.Q<TextField>("waves-input-interval").value, Is.EqualTo("0.25"));
                editor.UndoFromShortcut();
                Click(Open(panel, "0-0-0"), "应用字段");
                Assert.That(Actions(editor)[0].Value<int>("count"), Is.EqualTo(1));
                Assert.That(Actions(editor)[0]["interval"]!.ToString(), Is.EqualTo("0.25"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(workspace);
            }
        }

        [UnityTest]
        public IEnumerator ReorderingIdenticalActionsStillRecordsDraftBindingHistory()
        {
            var document = WithActions(Spawn(), Spawn());
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            var editor = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                editor.LoadWorkspaceDocument(workspace, "相同动作", document.ToJson(), "", "");
                editor.ShowUtility();
                yield return null;
                editor.CreateGUI();
                var root = editor.rootVisualElement;
                Invoke(root.Q<Button>("workbench-tab-2"));
                var panel = root.Q("waves-editor");
                Open(panel, "0");
                Open(panel, "0-0");
                Open(panel, "0-0-0").Q<TextField>("waves-input-count").value = "4";
                Click(Open(panel, "0-0-0"), "下移");
                Assert.That(Open(panel, "0-0-1").Q<TextField>("waves-input-count").value, Is.EqualTo("4"));
                editor.UndoFromShortcut();
                Assert.That(Open(panel, "0-0-0").Q<TextField>("waves-input-count").value, Is.EqualTo("4"));
                Redo(editor);
                Assert.That(Open(panel, "0-0-1").Q<TextField>("waves-input-count").value, Is.EqualTo("4"));
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(document.ToJson()));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(workspace);
            }
        }

        [TestCase((int)WaveOrder.Append, "0/3/new", null, "0/3/new")]
        [TestCase((int)WaveOrder.Copy, "0/3/0", "0/4/0", "0/3/0")]
        [TestCase((int)WaveOrder.Move, "0/3/0", "0/2/0", "0/3/0")]
        [TestCase((int)WaveOrder.Delete, "0/3/0", "0/2/0", "0/3/0")]
        public void HistoryMapsOnlyIndicesUnderAffectedParent(int operation, string path, string? intermediate, string original)
        {
            var order = (WaveOrder)operation;
            var step = new WaveHistoryStep(order, new[] { 0 }, order == WaveOrder.Append ? 3 : 2, 3);
            Assert.That(step.Remap("1/3/0", true), Is.EqualTo("1/3/0"));
            if (order == WaveOrder.Append)
                Assert.That(step.Remap(path, false), Is.Null);
            else
            {
                Assert.That(step.Remap(path, true), Is.EqualTo(intermediate));
                Assert.That(step.Remap(intermediate!, false), Is.EqualTo(original));
            }
        }

        [UnityTest]
        public IEnumerator BlankMapAddsNativeWavesAndRequiresExplicitEnemyAndRoute()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            var editor = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                var routeDefinition = RouteEditDraft.Create("WALK");
                routeDefinition["startPosition"] = new JObject { ["row"] = 0, ["col"] = 0 };
                routeDefinition["endPosition"] = new JObject { ["row"] = 5, ["col"] = 9 };
                var initial = LevelDocument.Create(10, 6).WithSection("enemyDbRefs",
                    "[{\"id\":\"enemy_1007_slime\",\"level\":0,\"useDb\":true}]")
                    .WithSection("routes", new JArray(routeDefinition).ToString());
                editor.LoadWorkspaceDocument(workspace, "测试", initial.ToJson(), "", "");
                editor.ShowUtility();
                yield return null;
                editor.CreateGUI();
                var root = editor.rootVisualElement;
                Invoke(root.Q<Button>("workbench-tab-2"));
                var panel = root.Q("waves-editor");
                Click(panel, "新增波次");
                Assert.That(editor.WorkspaceDocument.GetSection("waves")![0]!["maxTimeWaitingForNextWave"]!.Value<float>(), Is.EqualTo(-1));
                var fragment = Open(panel, "0-0");
                Click(fragment, "新增动作");
                Assert.That(Actions(editor), Is.Empty);
                var pending = panel.Q<Foldout>("waves-node-0-0-new");
                var enemy = pending.Q<PopupField<string>>("waves-input-key");
                var route = pending.Q<PopupField<string>>("waves-input-routeIndex");
                Assert.That(enemy.index, Is.Zero);
                Assert.That(route.index, Is.Zero);
                Assert.That(enemy.choices.Count, Is.EqualTo(2));
                Assert.That(route.choices.Count, Is.GreaterThan(1));
                Assert.That(pending.Query<Button>().ToList().Single(button => button.text == "创建动作").enabledInHierarchy, Is.False);

                enemy.index = 1;
                route.index = 1;
                pending.Q<TextField>("waves-input-count").value = "3";
                pending.Q<TextField>("waves-input-interval").value = "0.5";
                Assert.That(Actions(editor), Is.Empty);
                Click(pending, "创建动作");
                var action = (JObject)Actions(editor).Single();
                Assert.That(action.Value<string>("key"), Is.EqualTo("enemy_1007_slime"));
                Assert.That(action.Value<int>("routeIndex"), Is.Zero);
                Assert.That(action.Value<int>("count"), Is.EqualTo(3));
                Assert.That(WaveSchedule.RelativeTime(new JValue(2), action)!.Value.Last, Is.EqualTo(3));
                editor.UndoFromShortcut();
                Assert.That(Actions(editor), Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(workspace);
            }
        }

        [UnityTest]
        public IEnumerator Original510ShowsNativeHierarchyAndMovesDraftWithItsRecord()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            var editor = ScriptableObject.CreateInstance<MapEditorWindow>();
            MapEditorWindow? restored = null;
            try
            {
                var document = Original510();
                var initialActions = (JArray)document.GetSection("waves")![0]!["fragments"]![0]!["actions"]!;
                editor.LoadWorkspaceDocument(workspace, "5-10", document.ToJson(), "", "");
                editor.Dialogs = new Confirm();
                editor.ShowUtility();
                yield return null;
                editor.CreateGUI();
                var root = editor.rootVisualElement;
                Invoke(root.Q<Button>("workbench-tab-2"));
                var panel = root.Q("waves-editor");
                Open(panel, "0");
                var fragment = Open(panel, "0-0");
                Assert.That(panel.Query<Foldout>(className: "wave-record").ToList()
                    .Count(row => row.name.Count(c => c == '-') == 3), Is.EqualTo(10));
                var info = Open(fragment, "0-0-0");
                Assert.That(info.Q<PopupField<string>>("waves-input-key"), Is.Not.Null);
                Assert.That(info.Q("waves-field-routeIndex"), Is.Null);
                Assert.That(info.Query<Button>().ToList().Any(button => button.text == "演示路线"), Is.False);
                var spawn = Open(fragment, "0-0-1");
                Assert.That(spawn.Q<PopupField<string>>("waves-input-routeIndex").value, Does.StartWith("routes[1]"));
                Assert.That(spawn.Q<PopupField<string>>("waves-input-key").value, Does.Contain("enemy_1507_mephi"));
                spawn.Q<TextField>("waves-input-count").value = "2";
                Click(spawn, "上移");
                Assert.That(JToken.DeepEquals(Actions(editor)[0], initialActions[1]), Is.True);
                Assert.That(panel.Q<Foldout>("waves-node-0-0-0").Q<TextField>("waves-input-count").value, Is.EqualTo("2"));

                restored = editor.CopyToWorkspace(workspace);
                restored.ShowUtility();
                yield return null;
                restored.CreateGUI();
                Invoke(restored.rootVisualElement.Q<Button>("workbench-tab-2"));
                var copy = restored.rootVisualElement.Q("waves-editor");
                Open(copy, "0");
                Open(copy, "0-0");
                var moved = Open(copy, "0-0-0");
                Assert.That(moved.Q<TextField>("waves-input-count").value, Is.EqualTo("2"));
                Click(moved, "应用字段");
                Assert.That(Actions(restored)[0]!.Value<int>("count"), Is.EqualTo(2));
                Assert.That(Actions(editor)[0]!.Value<int>("count"), Is.EqualTo(1));
                Assert.That(JToken.DeepEquals(Actions(restored)[1], initialActions[0]), Is.True);
                Assert.That(JToken.DeepEquals(document.GetSection("branches"), restored.WorkspaceDocument.GetSection("branches")), Is.True);
                Assert.That(JToken.DeepEquals(document.GetSection("routes"), restored.WorkspaceDocument.GetSection("routes")), Is.True);
            }
            finally
            {
                if (restored != null) UnityEngine.Object.DestroyImmediate(restored);
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(workspace);
            }
        }

        [Test]
        public void DetachedDraftCannotFollowADeletedNodeOrReplaceAnotherRecord()
        {
            var original = new InspectorDraftStore();
            original.SetDraft(WaveInspectorDrafts.Slot("0/1/0"), "old", "0/1/0", "{}", "{\"count\":3}");
            original.SetDraft(WaveInspectorDrafts.Slot("0/2/new"), "old", "0/2/new", "", "{}");
            original.SetDraft("action", "old", "legacy", "", "legacy draft");
            string? Remap(string key) => WaveInspectorDrafts.Remap(key, new[] { 0 }, index =>
                index == 1 ? null : index > 1 ? index - 1 : index);
            var rebased = WaveInspectorDrafts.Rebase(original, "old", "new", Remap, null);
            Assert.That(rebased.HasChanges(WaveInspectorDrafts.Slot("0/1/0")), Is.False);
            var detached = WaveInspectorDrafts.Saved(rebased).Single(slot => slot.Contains("detached"));
            Assert.That(rebased.TryRead(detached, out var raw), Is.True);
            Assert.That(raw, Is.EqualTo("{\"count\":3}"));
            var pending = WaveInspectorDrafts.Slot("0/1/new");
            rebased.RequireCurrent(pending, "new", "0/1/new");
            Assert.That(rebased.TryRead("action", out var legacy), Is.True);
            Assert.That(legacy, Is.EqualTo("legacy draft"));
            Assert.Throws<InvalidOperationException>(() => rebased.RequireCurrent(pending, "old", "0/1/new"));
        }

        [UnityTest]
        public IEnumerator OriginalEmptyWaveArrayIsNotPopulatedUntilExplicitAdd()
        {
            const string body = "8da04f0712a3a5514af4499d1ebda667a31f8402a72817a445b595c54622c363";
            var catalog = FinishedLevelCatalog.Load(FinishedLevelCatalog.BundledPath());
            using var archive = catalog.OpenBodies();
            var document = LevelDocument.Parse(catalog.ReadBody(archive, body));
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            var editor = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                editor.LoadWorkspaceDocument(workspace, "原版空波次", document.ToJson(), "", "");
                editor.ShowUtility();
                yield return null;
                editor.CreateGUI();
                var root = editor.rootVisualElement;
                Invoke(root.Q<Button>("workbench-tab-2"));
                var panel = root.Q("waves-editor");
                Assert.That(panel.Q<Foldout>("waves-node-0"), Is.Null);
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(document.ToJson()));
                Click(panel, "新增波次");
                Assert.That(Actions(editor), Is.Empty);
                editor.UndoFromShortcut();
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(document.ToJson()));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(workspace);
            }
        }

        [UnityTest]
        public IEnumerator NonSpawnCreationAndPreviewEditingUseSharedFormAndUndo()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            var editor = ScriptableObject.CreateInstance<MapEditorWindow>();
            editor.Dialogs = new Confirm();
            try
            {
                var document = WithActions().WithSection("enemies", "[]");
                editor.LoadWorkspaceDocument(workspace, "非出怪动作", document.ToJson(), "", "");
                editor.ShowUtility(); yield return null; editor.CreateGUI();
                Invoke(editor.rootVisualElement.Q<Button>("workbench-tab-2"));
                var panel = editor.rootVisualElement.Q("waves-editor");
                Open(panel, "0");
                Click(Open(panel, "0-0"), "新增动作");
                var pending = panel.Q<Foldout>("waves-node-0-0-new");
                pending.Q<PopupField<string>>("waves-input-actionType").value = "SHOW_ALL_HIDDEN_CARDS";
                Assert.That(Actions(editor), Is.Empty);
                Click(pending, "创建动作");
                Assert.That(Actions(editor)[0].Value<string>("actionType"), Is.EqualTo("SHOW_ALL_HIDDEN_CARDS"));
                editor.UndoFromShortcut();
                Assert.That(Actions(editor), Is.Empty);
                Redo(editor);
                var action = Open(panel, "0-0-0");
                action.Q<PopupField<string>>("waves-input-actionType").value = "PREVIEW_CURSOR";
                action.Q<PopupField<string>>("waves-input-routeIndex").index = 1;
                Click(action, "应用字段");
                Assert.That(Actions(editor)[0].Value<string>("actionType"), Is.EqualTo("PREVIEW_CURSOR"));
                Assert.That(Actions(editor)[0].Value<int>("routeIndex"), Is.Zero);
                editor.UndoFromShortcut();
                Assert.That(Actions(editor)[0].Value<string>("actionType"), Is.EqualTo("SHOW_ALL_HIDDEN_CARDS"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(workspace);
            }
        }

        [UnityTest]
        public IEnumerator WaveSearchRetainsAncestorsAndLocatesTheMatchingAction()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            var editor = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                var music = WaveSchedule.NewNode(3);
                music["actionType"] = "PLAY_BGM";
                music["key"] = "wave_music_probe";
                var document = WithActions(Spawn(), music);
                editor.LoadWorkspaceDocument(workspace, "波次搜索测试", document.ToJson(), "", "");
                editor.ShowUtility();
                yield return null;
                editor.CreateGUI();
                Invoke(editor.rootVisualElement.Q<Button>("workbench-tab-2"));
                var panel = editor.rootVisualElement.Q("waves-editor");
                Open(panel, "0");
                Open(panel, "0-0");
                Assert.That(panel.Q<Foldout>("waves-node-0-0-0"), Is.Not.Null);

                var filter = panel.Q<TextField>("waves-search");
                filter.value = "wave_music_probe";
                var deadline = Time.realtimeSinceStartup + 10;
                while (panel.Q<Foldout>("waves-node-0-0-0") != null && Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That(panel.Q<Foldout>("waves-node-0")?.value, Is.True);
                Assert.That(panel.Q<Foldout>("waves-node-0-0")?.value, Is.True);
                Assert.That(panel.Q<Foldout>("waves-node-0-0-0"), Is.Null);
                var match = Open(panel, "0-0-1");
                Click(match, "字段树");
                var tree = editor.rootVisualElement.Q("leveldata-tree");
                Assert.That(tree.Q<Foldout>("leveldata-node:/waves/0/fragments/0/actions/1")?.value, Is.True);
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(document.ToJson()));

                filter.value = "";
                deadline = Time.realtimeSinceStartup + 10;
                while (panel.Q<Foldout>("waves-node-0-0-0") == null && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(panel.Q<Foldout>("waves-node-0-0-0"), Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(workspace);
            }
        }

        [UnityTest]
        public IEnumerator WaveSettingsRejectInvalidTimeAndApplyOnlyTheEditedFields()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            var editor = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                var document = WithActions(Spawn());
                editor.LoadWorkspaceDocument(workspace, "波次参数测试", document.ToJson(), "", "");
                editor.ShowUtility();
                yield return null;
                editor.CreateGUI();
                Invoke(editor.rootVisualElement.Q<Button>("workbench-tab-2"));
                var panel = editor.rootVisualElement.Q("waves-editor");
                var wave = Open(panel, "0");
                var delay = wave.Q<TextField>("waves-input-postDelay");
                var initial = delay.value;
                delay.value = "-2";
                Assert.That(delay.value, Is.EqualTo(initial));
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(document.ToJson()));

                delay.value = "2.5";
                wave.Q<TextField>("waves-input-advancedWaveTag").value = "audit-wave";
                Click(wave, "应用字段");

                var updated = editor.WorkspaceDocument.GetSection("waves")![0]!;
                Assert.That(updated.Value<float>("postDelay"), Is.EqualTo(2.5f));
                Assert.That(updated.Value<string>("advancedWaveTag"), Is.EqualTo("audit-wave"));
                Assert.That(JToken.DeepEquals(updated["fragments"], document.GetSection("waves")![0]!["fragments"]), Is.True);
                editor.UndoFromShortcut();
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(document.ToJson()));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(workspace);
            }
        }

        [Test]
        public void FieldChangesPreserveChildNodesAndNewActionStaysUnbound()
        {
            JToken? invalid = new JValue(123);
            Assert.Throws<FormatException>(() => WaveSchedule.GetOrCreateChildren(ref invalid, Array.Empty<int>()));
            var wave = JObject.Parse("{\"preDelay\":1,\"fragments\":[null,{\"actions\":[{\"actionType\":451,\"future\":[1]}]}],\"advancedWaveTag\":null}");
            var fragments = wave["fragments"]!.DeepClone();
            var fields = WaveSchedule.Fields(wave, 1);
            fields["preDelay"] = 2;
            WaveSchedule.ApplyFields(wave, fields, 1);
            Assert.That(JToken.DeepEquals(wave["fragments"], fragments), Is.True);
            Assert.That(wave["advancedWaveTag"]!.Type, Is.EqualTo(JTokenType.Null));
            var pending = WaveSchedule.NewNode(3);
            Assert.That(pending.Property("routeIndex"), Is.Null);
            Assert.Throws<FormatException>(() => WaveSchedule.RequireSpawnTargets(pending, Array.Empty<string>(), new JArray()));
        }
    }
}
