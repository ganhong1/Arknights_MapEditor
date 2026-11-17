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
    public sealed class BranchesEditorTests
    {
        private const string Body510 = "9ade453a5ba2c8325ec5c1dc03f6f3a4590cf9681861be6949d2ce4135c07f26";
        private const string BodyAw6 = "2f8d8f156bc412bef9c3067608599d104902004daef1b48623781afbf3006802";

        private sealed class Confirm : IEditorFileDialogs
        {
            public string Open(string title, string directory, string extension) => "";
            public string Save(string title, string directory, string name, string extension) => "";
            public bool Discard(string title, string message) => true;
        }

        private static LevelDocument Original(string stage, string body)
        {
            var catalog = FinishedLevelCatalog.Load(FinishedLevelCatalog.BundledPath());
            var source = catalog.Groups.SelectMany(group => group.Variants)
                .First(variant => variant.Stage?.Id == stage && variant.Source.Body == body);
            using var archive = catalog.OpenBodies();
            return LevelDocument.Parse(catalog.ReadBody(archive, source.Source.Body));
        }

        private static void Invoke(Button button)
        {
            Assert.That(button, Is.Not.Null);
            Assert.That(button.enabledInHierarchy, Is.True, button.text);
            typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(button.clickable, new object?[] { null });
        }

        private static void Click(VisualElement root, string text) =>
            Invoke(root.Query<Button>().ToList().First(button => button.text == text));

        private static Foldout Open(VisualElement root, string branch, params int[] address)
        {
            var fold = root.Q<Foldout>("branch-node:" + BranchInspectorDrafts.Key(branch, address));
            Assert.That(fold, Is.Not.Null, branch + " / " + string.Join("/", address));
            fold.value = true;
            return fold;
        }

        private static VisualElement Panel(MapEditorWindow editor)
        {
            editor.CreateGUI();
            Invoke(editor.rootVisualElement.Q<Button>("workbench-tab-7"));
            var panel = editor.rootVisualElement.Q("branches-editor");
            Assert.That(panel, Is.Not.Null);
            return panel;
        }

        private static JArray Actions(MapEditorWindow editor, string branch, int phase = 0) =>
            (JArray)editor.WorkspaceDocument.GetSection("branches")![branch]!["phases"]![phase]!["actions"]!;

        [UnityTest]
        public IEnumerator Original510ShowsSevenPhasesTenAliasActionsAndKeepsDraftOnUndo()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            var editor = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                var original = Original("main_05-10", Body510);
                editor.LoadWorkspaceDocument(workspace, "5-10 分支测试", original.ToJson(), "", "");
                editor.Dialogs = new Confirm();
                editor.ShowUtility();
                yield return null;
                var panel = Panel(editor);
                var branch = Open(panel, "faust_ballis");
                var phases = (JArray)original.GetSection("branches")!["faust_ballis"]!["phases"]!;
                Assert.That(phases.Count, Is.EqualTo(7));
                Assert.That(phases.Sum(phase => ((JArray)phase["actions"]!).Count), Is.EqualTo(10));
                Assert.That(branch.Query<Label>().ToList().Any(label => label.text.Contains("SummonBallis")), Is.True);
                for (var index = 0; index < phases.Count; index++) Open(panel, "faust_ballis", index);
                var first = Open(panel, "faust_ballis", 0, 0);
                var aliases = first.Q<PopupField<string>>("branch-input-key");
                Assert.That(aliases.value, Does.Contain("trap_007_ballis#1"));
                Assert.That(aliases.choices.Any(label => label.Contains("隐藏")), Is.True);
                Assert.That(first.Q<PopupField<string>>("branch-input-routeIndex"), Is.Null);
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(original.ToJson()));

                // The stage selector and its draft remain attached when the phase is moved and undone.
                var phase = Open(panel, "faust_ballis", 0);
                phase.Q<TextField>("branch-phase-preDelay").value = "2.5";
                Click(phase, "下移");
                Assert.That(Open(panel, "faust_ballis", 1).Q<TextField>("branch-phase-preDelay").value, Is.EqualTo("2.5"));
                editor.UndoFromShortcut();
                Assert.That(Open(panel, "faust_ballis", 0).Q<TextField>("branch-phase-preDelay").value, Is.EqualTo("2.5"));
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(original.ToJson()));
                Click(Open(panel, "faust_ballis", 0), "应用阶段参数");
                Assert.That(editor.WorkspaceDocument.GetSection("branches")!["faust_ballis"]!["phases"]![0]!["preDelay"]!.Value<double>(), Is.EqualTo(2.5));
                Assert.That(JToken.DeepEquals(editor.WorkspaceDocument.GetSection("waves"), original.GetSection("waves")), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(workspace);
            }
        }

        [UnityTest]
        public IEnumerator OriginalAw6UsesExtraRoutesAndProtectsTheIndexedCaller()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            var editor = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                var original = Original("act12mini_06", BodyAw6);
                editor.LoadWorkspaceDocument(workspace, "AW-6 分支测试", original.ToJson(), "", "");
                editor.Dialogs = new Confirm();
                editor.ShowUtility();
                yield return null;
                var panel = Panel(editor);
                var branch = Open(panel, "line_01");
                Open(panel, "line_01", 0);
                var action = Open(panel, "line_01", 0, 0);
                Assert.That(action.Q<PopupField<string>>("branch-input-routeIndex").value, Does.StartWith("extraRoutes[0]"));
                Assert.That(action.Q<PopupField<string>>("branch-input-key").value, Does.Contain("enemy_1255_lybgpa_2"));
                Assert.That(action.Query<Button>().ToList().Any(button => button.text == "演示路线"), Is.True);
                Assert.That(branch.Query<Label>().ToList().Any(label => label.text.Contains("action_index · 0")), Is.True);
                var document = JObject.Parse(original.ToJson());
                Assert.That(BranchSchedule.References(document, "line_01").Any(reference => reference.CanRewrite && reference.ActionIndex == 0), Is.True);
                Assert.Throws<FormatException>(() => BranchSchedule.ChangeNode(document, "line_01", 0, 0, BranchEdit.Delete));
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(original.ToJson()));

                branch.Q<TextField>("branches-target-key").value = "line_test";
                Click(branch, "重命名");
                var renamed = JObject.Parse(editor.WorkspaceDocument.ToJson());
                Assert.That(renamed["branches"]!["line_test"], Is.Not.Null);
                Assert.That(renamed["branches"]!["line_01"], Is.Null);
                Assert.That(BranchSchedule.References(renamed, "line_test").Any(reference => reference.CanRewrite), Is.True);
                Assert.That(JToken.DeepEquals(renamed["extraRoutes"], document["extraRoutes"]), Is.True);
                editor.UndoFromShortcut();
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(original.ToJson()));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(workspace);
            }
        }

        [UnityTest]
        public IEnumerator ActionDraftFollowsRenameRedoAndDeleteUndoWithoutEditingTheDocument()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            var editor = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                var initial = LevelDocument.Create(4, 4).WithSection("branches",
                    "{\"demo\":{\"phases\":[{\"preDelay\":0,\"actions\":[{\"actionType\":\"EMPTY\",\"preDelay\":0}]}]}}");
                editor.LoadWorkspaceDocument(workspace, "分支草稿测试", initial.ToJson(), "", "");
                editor.Dialogs = new Confirm();
                editor.ShowUtility();
                yield return null;
                var panel = Panel(editor);
                var branch = Open(panel, "demo");
                Open(panel, "demo", 0);
                Open(panel, "demo", 0, 0).Q<TextField>("branch-input-preDelay").value = "1.5";
                branch.Q<TextField>("branches-target-key").value = "renamed";
                Click(branch, "重命名");
                Assert.That(Open(panel, "renamed", 0, 0).Q<TextField>("branch-input-preDelay").value, Is.EqualTo("1.5"));
                editor.UndoFromShortcut();
                Assert.That(Open(panel, "demo", 0, 0).Q<TextField>("branch-input-preDelay").value, Is.EqualTo("1.5"));
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(initial.ToJson()));
                typeof(MapEditorWindow).GetMethod("StepEditorHistory", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(editor, new object[] { true });
                Assert.That(Open(panel, "renamed", 0, 0).Q<TextField>("branch-input-preDelay").value, Is.EqualTo("1.5"));
                Click(Open(panel, "renamed", 0, 0), "删除");
                Assert.That(Actions(editor, "renamed"), Is.Empty);
                editor.UndoFromShortcut();
                Open(panel, "renamed", 0);
                var restored = Open(panel, "renamed", 0, 0);
                Assert.That(restored.Q<TextField>("branch-input-preDelay").value, Is.EqualTo("1.5"));
                Assert.That(Actions(editor, "renamed")[0]!.Value<double>("preDelay"), Is.Zero);
                Click(restored, "应用动作");
                Assert.That(Actions(editor, "renamed")[0]!.Value<double>("preDelay"), Is.EqualTo(1.5));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(workspace);
            }
        }

        [UnityTest]
        public IEnumerator PendingSpawnUsesExplicitEnemyAndExtraRouteAndCreationCanBeUndone()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            var editor = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                var route = RouteEditDraft.Create("WALK");
                route["startPosition"] = new JObject { ["row"] = 0, ["col"] = 0 };
                route["endPosition"] = new JObject { ["row"] = 3, ["col"] = 3 };
                var initial = LevelDocument.Create(4, 4)
                    .WithSection("extraRoutes", new JArray(route).ToString())
                    .WithSection("enemies", "[{\"alias\":\"local\",\"key\":\"enemy_1007_slime\",\"name\":\"局部敌人\"}]")
                    .WithSection("branches", "{\"demo\":{\"phases\":[{\"preDelay\":0,\"actions\":[]}]}}");
                editor.LoadWorkspaceDocument(workspace, "分支空白测试", initial.ToJson(), "", "");
                editor.Dialogs = new Confirm();
                editor.ShowUtility();
                yield return null;
                var panel = Panel(editor);
                Open(panel, "demo");
                Click(Open(panel, "demo", 0), "新增动作");
                Assert.That(Actions(editor, "demo"), Is.Empty);
                var pending = Open(panel, "demo", 0, int.MaxValue);
                Assert.That(pending.Q<PopupField<string>>("branch-input-key").index, Is.Zero);
                Assert.That(pending.Q<PopupField<string>>("branch-input-routeIndex").index, Is.Zero);
                pending.Q<PopupField<string>>("branch-input-key").index = 1;
                pending.Q<PopupField<string>>("branch-input-routeIndex").index = 1;
                pending.Q<TextField>("branch-input-count").value = "3";
                pending.Q<TextField>("branch-input-interval").value = "0.5";
                Assert.That(Actions(editor, "demo"), Is.Empty);
                Click(pending, "创建动作");
                Assert.That(Actions(editor, "demo").Count, Is.EqualTo(1));
                var action = (JObject)Actions(editor, "demo")[0];
                Assert.That(action.Value<string>("key"), Is.EqualTo("local"));
                Assert.That(action.Value<int>("routeIndex"), Is.Zero);
                Assert.That(action.Value<int>("count"), Is.EqualTo(3));
                Assert.That(action.Value<double>("interval"), Is.EqualTo(.5));
                editor.UndoFromShortcut();
                Assert.That(Actions(editor, "demo"), Is.Empty);
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(initial.ToJson()));
                Assert.That(initial.GetSection("branches")!["demo"]!["phases"]![0]!["actions"]!.Count(), Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(editor);
                UnityEngine.Object.DestroyImmediate(workspace);
            }
        }
    }
}
