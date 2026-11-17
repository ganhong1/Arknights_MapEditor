#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Arknights.MapEditor.Storage;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor.Tests
{
    public sealed partial class MusicWorkspaceTests
    {
        [UnityTest]
        public IEnumerator EditingBranchMusicKeepsUnrelatedNumberLexemes()
        {
            const string precise = "0.123456789012345678901234567890123";
            const string exponent = "1.234567890123456789012345e+24";
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            try
            {
                var document = MakeDocument()
                    .WithSection("precisionRoot", "{\"value\":" + precise + "}")
                    .WithSection("branches", "{\"demo\":{\"phaseMeta\":" + exponent +
                        ",\"phases\":[{\"preDelay\":0,\"actions\":[{\"actionType\":4,\"key\":\"shield\",\"preDelay\":0," +
                        "\"unrelated\":{\"precise\":" + precise + ",\"exponent\":" + exponent + "}}]}]}}");
                workspace.ShowUtility();
                workspace.CreateGUI();
                workspace.AddDocument(0, "音乐精度", document.ToJson());
                var editor = workspace.FocusedEditor!;
                var inspector = OpenInspector(workspace);
                yield return null;
                var root = inspector.rootVisualElement;

                Module(root, 11, "music-editor");
                var music = SelectMusicAction(root, 0);
                Find<TextField>(music, "music-input-key").value = "boss_sui";
                ClickText(music, "应用动作");
                Assert.That(BranchActions(editor).Single()!.Value<string>("key"), Is.EqualTo("boss_sui"));
                Assert.That(editor.WorkspaceDocument.ToJson(false), Is.EqualTo(
                    document.ToJson(false).Replace("\"actionType\":4,\"key\":\"shield\"", "\"actionType\":4,\"key\":\"boss_sui\"")));
            }
            finally { UnityEngine.Object.DestroyImmediate(workspace); }
        }

        [UnityTest]
        public IEnumerator RedoRefusesToCommitUnappliedDocumentJsonAndKeepsRedoAvailable()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            try
            {
                var original = MakeDocument(MusicAction("history", 0));
                workspace.ShowUtility();
                workspace.CreateGUI();
                workspace.AddDocument(0, "音乐重做", original.ToJson());
                var editor = workspace.FocusedEditor!;
                var inspector = OpenInspector(workspace);
                yield return null;
                var root = inspector.rootVisualElement;

                var music = Module(root, 11, "music-editor");
                Find<TextField>(music, "music-opening-key").value = "shatterpoint_02";
                Click(Find<Button>(music, "music-opening-apply"));
                var revised = editor.WorkspaceDocument.ToJson();
                Assert.That(revised, Is.Not.EqualTo(original.ToJson()));
                ClickText(root, "撤销");
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(original.ToJson()));

                Click(Find<Button>(root, "leveldata-json-mode"));
                var field = Find<TextField>(root, "leveldata-json");
                var raw = original.WithSection("levelId", "\"unapplied-full-json\"").ToJson();
                field.value = raw;
                ClickText(root, "重做");
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(original.ToJson()));
                Assert.That(Find<TextField>(root, "leveldata-json").value, Is.EqualTo(raw));

                Find<TextField>(root, "leveldata-json").value = original.ToJson();
                ClickText(root, "重做");
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(revised));
            }
            finally { UnityEngine.Object.DestroyImmediate(workspace); }
        }

        [UnityTest]
        public IEnumerator EnvironmentDraftFollowsOtherFieldCommitsAndRejectsOwnFieldConflict()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            try
            {
                var original = MakeDocument().WithSection("environmentSe", "\"env-original\"");
                workspace.ShowUtility();
                workspace.CreateGUI();
                workspace.AddDocument(0, "环境音草稿", original.ToJson());
                var editor = workspace.FocusedEditor!;
                var inspector = OpenInspector(workspace);
                yield return null;
                var root = inspector.rootVisualElement;

                Module(root, 12, "global-field-environmentSe");
                Find<TextField>(root, "global-field-environmentSe").value = "\"env-local\"";
                var music = Module(root, 11, "music-editor");
                Find<TextField>(music, "music-opening-key").value = "shatterpoint_02";
                Click(Find<Button>(music, "music-opening-apply"));
                Assert.That(editor.WorkspaceDocument.GetSection("bgmEvent")!.Value<string>(), Is.EqualTo("shatterpoint_02"));

                Module(root, 12, "global-field-environmentSe");
                Assert.That(Find<TextField>(root, "global-field-environmentSe").value, Is.EqualTo("\"env-local\""));
                ClickText(Find<ScrollView>(root, "workbench-page-12"), "应用 environmentSe");
                Assert.That(editor.WorkspaceDocument.GetSection("environmentSe")!.Value<string>(), Is.EqualTo("env-local"));

                Find<TextField>(root, "global-field-environmentSe").value = "\"env-local-conflict\"";
                var other = editor.WorkspaceDocument.WithSection("environmentSe", "\"env-other\"");
                Click(Find<Button>(root, "leveldata-json-mode"));
                Find<TextField>(root, "leveldata-json").value = other.ToJson();
                ClickText(Find<VisualElement>(root, "leveldata-source-panel"), "校验并应用 JSON");
                Assert.That(editor.WorkspaceDocument.GetSection("environmentSe")!.Value<string>(), Is.EqualTo("env-other"));
                var apply = Find<ScrollView>(root, "workbench-page-12").Query<Button>()
                    .Where(button => button.text == "应用 environmentSe").First();
                Assert.That(apply, Is.Not.Null);
                if (apply.enabledInHierarchy) Click(apply);
                Assert.That(editor.WorkspaceDocument.GetSection("environmentSe")!.Value<string>(), Is.EqualTo("env-other"));
                Assert.That(Find<TextField>(root, "global-field-environmentSe").value, Is.EqualTo("\"env-local-conflict\""));
            }
            finally { UnityEngine.Object.DestroyImmediate(workspace); }
        }

        [UnityTest]
        public IEnumerator RestoreDifferentMapClearsMusicSelectionAndRetainsRestoredDraft()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/MusicRestore-" + Guid.NewGuid().ToString("N")));
            var path = Path.Combine(directory, "recovery.json");
            try
            {
                var firstDocument = MakeDocument(MusicAction("first", 0));
                var replacementDocument = MakeDocument(MusicAction("restored", 3));
                workspace.ShowUtility();
                workspace.CreateGUI();
                workspace.AddDocument(0, "待恢复地图", firstDocument.ToJson());
                var first = workspace.FocusedEditor!;
                workspace.AddDocument(0, "恢复来源地图", replacementDocument.ToJson());
                var second = workspace.FocusedEditor!;
                var inspector = OpenInspector(workspace);
                yield return null;
                var root = inspector.rootVisualElement;

                Module(root, 11, "music-editor");
                Find<TextField>(SelectMusicAction(root, 0), "music-input-preDelay").value = "8.5";
                var capture = typeof(MapEditorWindow).GetMethod("CaptureWindowRecovery", PrivateInstance)!;
                var snapshot = (RecoverySnapshot)capture.Invoke(second, null);
                Directory.CreateDirectory(directory);
                RecoveryStore.Save(path, snapshot, null);

                workspace.SelectDocument(first);
                Module(root, 11, "music-editor");
                Find<TextField>(SelectMusicAction(root, 0), "music-input-preDelay").value = "1.5";
                first.Dialogs = new Confirm { OpenPath = path };
                ClickText(root, "恢复草稿");
                Assert.That(first.WorkspaceDocument.ToJson(), Is.EqualTo(replacementDocument.ToJson()));
                var priorSelection = Find<VisualElement>(root, "music-action-edit");
                Assert.That(priorSelection.Q<TextField>("music-input-preDelay"), Is.Null);

                var restored = SelectMusicAction(root, 0);
                Assert.That(Number(restored, "music-input-preDelay"), Is.EqualTo(8.5));
                ClickText(restored, "应用字段");
                Assert.That(WaveActions(first).Single()!.Value<double>("preDelay"), Is.EqualTo(8.5));
                workspace.SelectDocument(second);
                Assert.That(second.WorkspaceDocument.ToJson(), Is.EqualTo(replacementDocument.ToJson()));
                Assert.That(Number(SelectMusicAction(root, 0), "music-input-preDelay"), Is.EqualTo(8.5));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(workspace);
                if (File.Exists(path)) File.Delete(path);
                if (Directory.Exists(directory)) Directory.Delete(directory);
            }
        }

        [UnityTest]
        public IEnumerator RecoveredOpeningDraftBoundToAnotherFieldCannotApply()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            try
            {
                var document = MakeDocument().WithSection("bgmEvent", "\"shield\"");
                workspace.ShowUtility();
                workspace.CreateGUI();
                workspace.AddDocument(0, "错绑开局曲草稿", document.ToJson());
                var editor = workspace.FocusedEditor!;
                var inspector = OpenInspector(workspace);
                yield return null;
                var root = inspector.rootVisualElement;
                var store = new InspectorDraftStore();
                store.SetDraft("global:bgmEvent", document.ToJson(), "environmentSe", "\"shield\"", "\"boss_sui\"");
                typeof(MapEditorWindow).GetMethod("ApplyInspectorRecovery", PrivateInstance)!.Invoke(editor, new object[] { store });
                var music = Module(root, 11, "music-editor");

                Assert.That(Find<Button>(music, "music-opening-apply").enabledInHierarchy, Is.False);
                Assert.That(Find<TextField>(music, "global-field-bgmEvent").value, Is.EqualTo("\"boss_sui\""));
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(document.ToJson()));
            }
            finally { UnityEngine.Object.DestroyImmediate(workspace); }
        }

        [UnityTest]
        public IEnumerator MusicRowUpdatesImmediatelyAndKeepsTheSelectedEditorAttached()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            try
            {
                var original = MakeDocument(MusicAction("row", 1.25));
                workspace.ShowUtility();
                workspace.CreateGUI();
                workspace.AddDocument(0, "音乐实时草稿行", original.ToJson());
                var editor = workspace.FocusedEditor!;
                var inspector = OpenInspector(workspace);
                yield return null;
                var root = inspector.rootVisualElement;

                Module(root, 11, "music-editor");
                var form = SelectMusicAction(root, 0);
                var key = Find<TextField>(form, "music-input-key");
                var delay = Find<TextField>(form, "music-input-preDelay");
                key.value = "boss_sui";
                var row = Find<Button>(root, "music-action-0");
                Assert.That(row.text, Does.Contain("boss_sui"));
                Assert.That(row.text, Does.Contain("●"));
                Assert.That(Find<TextField>(form, "music-input-key"), Is.SameAs(key));
                Assert.That(Find<TextField>(form, "music-input-preDelay"), Is.SameAs(delay));
                delay.value = "2.75";
                row = Find<Button>(root, "music-action-0");
                Assert.That(row.text, Does.Contain("+2.75s"));
                Assert.That(row.text, Does.Contain("●"));
                Assert.That(Find<TextField>(form, "music-input-key"), Is.SameAs(key));
                Assert.That(Find<TextField>(form, "music-input-preDelay"), Is.SameAs(delay));
                Assert.That(key.panel, Is.SameAs(root.panel));
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(original.ToJson()));
            }
            finally { UnityEngine.Object.DestroyImmediate(workspace); }
        }
    }
}
