#nullable enable
using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor.Tests
{
    public sealed partial class MusicWorkspaceTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private sealed class Confirm : IEditorFileDialogs
        {
            internal string OpenPath = "";
            public string Open(string title, string directory, string extension) => OpenPath;
            public string Save(string title, string directory, string name, string extension) => "";
            public bool Discard(string title, string message) => true;
        }

        private static LevelDocument MakeDocument(params JObject[] waves)
        {
            var fragment = new JObject { ["preDelay"] = 0, ["actions"] = new JArray(waves.Cast<JToken>()) };
            var wave = WaveSchedule.NewNode(1);
            wave["fragments"] = new JArray(fragment);
            return LevelDocument.Create(4, 4)
                .WithSection("levelId", "\"shared-id\"")
                .WithSection("bgmEvent", "\"shield\"")
                .WithSection("waves", new JArray(wave).ToString(Formatting.None))
                .WithSection("branches", "{\"demo\":{\"phases\":[{\"preDelay\":0,\"actions\":[]}]}}");
        }

        private static JObject MusicAction(string marker, double delay) => new()
        {
            ["actionType"] = "PLAY_BGM", ["key"] = "shield", ["preDelay"] = delay, ["testMarker"] = marker
        };

        private static JArray WaveActions(MapEditorWindow editor) =>
            (JArray)editor.WorkspaceDocument.GetSection("waves")![0]!["fragments"]![0]!["actions"]!;

        private static JArray BranchActions(MapEditorWindow editor) =>
            (JArray)editor.WorkspaceDocument.GetSection("branches")!["demo"]!["phases"]![0]!["actions"]!;

        private static T Find<T>(VisualElement parent, string name) where T : VisualElement
        {
            var element = parent.Q<T>(name);
            Assert.That(element, Is.Not.Null, name);
            return element;
        }

        private static void Click(Button button)
        {
            Assert.That(button.enabledInHierarchy, Is.True, button.name + " " + button.text);
            typeof(Clickable).GetMethod("Invoke", PrivateInstance)!.Invoke(button.clickable, new object?[] { null });
        }

        private static void ClickText(VisualElement root, string text)
        {
            var buttons = root.Query<Button>().Where(button => button.text == text).ToList();
            Assert.That(buttons.Count, Is.EqualTo(1), text);
            Click(buttons[0]);
        }

        private static VisualElement Module(VisualElement root, int index, string name)
        {
            Click(Find<Button>(root, "workbench-tab-" + index));
            Assert.That(Find<ScrollView>(root, "workbench-page-" + index).style.display.value, Is.EqualTo(DisplayStyle.Flex));
            return Find<VisualElement>(root, name);
        }

        private static Foldout OpenWaveAction(VisualElement root, string action)
        {
            var panel = Find<VisualElement>(root, "waves-editor");
            Find<Foldout>(panel, "waves-node-0").value = true;
            Find<Foldout>(panel, "waves-node-0-0").value = true;
            var fold = Find<Foldout>(panel, "waves-node-0-0-" + action);
            fold.value = true;
            return fold;
        }

        private static Foldout OpenBranchAction(VisualElement root, int index)
        {
            var panel = Find<VisualElement>(root, "branches-editor");
            Find<Foldout>(panel, "branch-node:" + BranchInspectorDrafts.Key("demo")).value = true;
            Find<Foldout>(panel, "branch-node:" + BranchInspectorDrafts.Key("demo", 0)).value = true;
            var fold = Find<Foldout>(panel, "branch-node:" + BranchInspectorDrafts.Key("demo", 0, index));
            fold.value = true;
            return fold;
        }

        private static VisualElement SelectMusicAction(VisualElement root, int index)
        {
            var list = Find<VisualElement>(root, "music-action-list");
            Click(Find<Button>(list, "music-action-" + index));
            return Find<VisualElement>(root, "music-action-edit");
        }

        private static VisualElement BeginMusicAction(VisualElement root, string target)
        {
            var picker = Find<BaseField<string>>(root, "music-target");
            picker.value = target;
            Click(Find<Button>(root, "music-new-action"));
            return Find<VisualElement>(root, "music-action-edit");
        }

        private static double Number(VisualElement root, string input) =>
            double.Parse(Find<TextField>(root, input).value, CultureInfo.InvariantCulture);

        private static void Redo(MapEditorWindow editor) =>
            typeof(MapEditorWindow).GetMethod("StepEditorHistory", PrivateInstance)!.Invoke(editor, new object[] { true });

        private static MapWorkspaceInspectorWindow OpenInspector(MapWorkspaceWindow workspace)
        {
            typeof(MapWorkspaceWindow).GetMethod("ShowInspector", PrivateInstance)!.Invoke(workspace, null);
            var inspector = (MapWorkspaceInspectorWindow)typeof(MapWorkspaceWindow)
                .GetField("inspectorWindow", PrivateInstance)!.GetValue(workspace);
            inspector.CreateGUI();
            return inspector;
        }

        [UnityTest]
        public IEnumerator MusicDraftFollowsTheExactWaveActionAcrossNavigationReorderAndUndo()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            try
            {
                workspace.ShowUtility();
                workspace.CreateGUI();
                var document = MakeDocument(MusicAction("first", 0), MusicAction("second", 1));
                workspace.AddDocument(0, "音乐动作定位", document.ToJson());
                var editor = workspace.FocusedEditor!;
                editor.Dialogs = new Confirm();
                var inspector = OpenInspector(workspace);
                yield return null;
                var root = inspector.rootVisualElement;

                Module(root, 11, "music-editor");
                var music = SelectMusicAction(root, 0);
                Find<TextField>(music, "music-input-preDelay").value = "3.5";
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(document.ToJson()));

                Click(Find<Button>(music, "music-action-locate"));
                yield return null;
                Assert.That(Find<ScrollView>(root, "workbench-page-2").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                var first = Find<Foldout>(Find<VisualElement>(root, "waves-editor"), "waves-node-0-0-0");
                Assert.That(first.value, Is.True);
                Assert.That(Number(first, "waves-input-preDelay"), Is.EqualTo(3.5));

                ClickText(first, "下移");
                Assert.That(WaveActions(editor)[0]!.Value<string>("testMarker"), Is.EqualTo("second"));
                Assert.That(WaveActions(editor)[1]!.Value<string>("testMarker"), Is.EqualTo("first"));
                Assert.That(WaveActions(editor)[1]!.Value<double>("preDelay"), Is.Zero);
                var moved = OpenWaveAction(root, "1");
                Assert.That(Number(moved, "waves-input-preDelay"), Is.EqualTo(3.5));

                Click(Find<Button>(moved, "waves-action-music"));
                yield return null;
                Assert.That(Find<ScrollView>(root, "workbench-page-11").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                var selected = Find<VisualElement>(root, "music-action-edit");
                Assert.That(Number(selected, "music-input-preDelay"), Is.EqualTo(3.5));
                ClickText(selected, "应用字段");
                Assert.That(WaveActions(editor)[0]!.Value<double>("preDelay"), Is.EqualTo(1));
                Assert.That(WaveActions(editor)[1]!.Value<double>("preDelay"), Is.EqualTo(3.5));
                Assert.That(WaveActions(editor)[1]!.Value<string>("testMarker"), Is.EqualTo("first"));

                Module(root, 2, "waves-editor");
                editor.UndoFromShortcut();
                Assert.That(Number(OpenWaveAction(root, "1"), "waves-input-preDelay"), Is.Zero);
                editor.UndoFromShortcut();
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(document.ToJson()));
                Redo(editor);
                Redo(editor);
                Assert.That(WaveActions(editor)[1]!.Value<double>("preDelay"), Is.EqualTo(3.5));
                Assert.That(JToken.DeepEquals(editor.WorkspaceDocument.GetSection("branches"), document.GetSection("branches")), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(workspace); }
        }

        [UnityTest]
        public IEnumerator MusicCreationTracksOneDocumentPerTabAndUndoOnlyChangesTheFocusedMap()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            try
            {
                workspace.ShowUtility();
                workspace.CreateGUI();
                var original = MakeDocument();
                workspace.AddDocument(0, "音乐联动甲", original.ToJson());
                var first = workspace.FocusedEditor!;
                workspace.AddDocument(0, "音乐联动乙", original.ToJson());
                var second = workspace.FocusedEditor!;
                first.Dialogs = new Confirm();
                second.Dialogs = new Confirm();
                var inspector = OpenInspector(workspace);
                yield return null;
                var root = inspector.rootVisualElement;

                workspace.SelectDocument(first);
                Module(root, 11, "music-editor");
                var waveDraft = BeginMusicAction(root, "波次 [0] · 片段 [0]");
                Find<TextField>(waveDraft, "music-input-key").value = "shield";
                Find<TextField>(waveDraft, "music-input-preDelay").value = "2.25";
                Assert.That(WaveActions(first), Is.Empty);

                workspace.SelectDocument(second);
                Module(root, 11, "music-editor");
                Assert.That(WaveActions(second), Is.Empty);
                var branchDraft = BeginMusicAction(root, "分支 demo · 阶段 [0]");
                Find<TextField>(branchDraft, "music-input-key").value = "boss_sui";
                ClickText(branchDraft, "创建动作");
                Assert.That(BranchActions(second).Single()!.Value<string>("key"), Is.EqualTo("boss_sui"));

                var secondMusic = SelectMusicAction(root, 0);
                Click(Find<Button>(secondMusic, "music-action-locate"));
                var branch = Find<Foldout>(Find<VisualElement>(root, "branches-editor"),
                    "branch-node:" + BranchInspectorDrafts.Key("demo", 0, 0));
                Assert.That(branch.value, Is.True);
                Assert.That(Find<TextField>(branch, "branch-input-key").value, Is.EqualTo("boss_sui"));

                workspace.SelectDocument(first);
                Module(root, 2, "waves-editor");
                var pending = OpenWaveAction(root, "new");
                Assert.That(Find<TextField>(pending, "waves-input-key").value, Is.EqualTo("shield"));
                Assert.That(Number(pending, "waves-input-preDelay"), Is.EqualTo(2.25));
                ClickText(pending, "创建动作");
                Assert.That(WaveActions(first).Single()!.Value<double>("preDelay"), Is.EqualTo(2.25));

                Module(root, 11, "music-editor");
                Find<TextField>(root, "music-opening-key").value = "shatterpoint_02";
                Click(Find<Button>(root, "music-opening-apply"));
                Assert.That(first.WorkspaceDocument.GetSection("bgmEvent")!.Value<string>(), Is.EqualTo("shatterpoint_02"));
                Assert.That(second.WorkspaceDocument.GetSection("bgmEvent")!.Value<string>(), Is.EqualTo("shield"));
                Assert.That(BranchActions(first), Is.Empty);
                Assert.That(WaveActions(second), Is.Empty);

                using (var undo = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Z, modifiers = EventModifiers.Control }))
                {
                    undo.target = root;
                    root.SendEvent(undo);
                    Assert.That(undo.isPropagationStopped, Is.True);
                }
                Assert.That(first.WorkspaceDocument.GetSection("bgmEvent")!.Value<string>(), Is.EqualTo("shield"));
                Assert.That(WaveActions(first), Has.Count.EqualTo(1));
                first.UndoFromShortcut();
                Assert.That(first.WorkspaceDocument.ToJson(), Is.EqualTo(original.ToJson()));
                Assert.That(BranchActions(second), Has.Count.EqualTo(1));
                workspace.SelectDocument(second);
                second.UndoFromShortcut();
                Assert.That(second.WorkspaceDocument.ToJson(), Is.EqualTo(original.ToJson()));
            }
            finally { UnityEngine.Object.DestroyImmediate(workspace); }
        }

        [UnityTest]
        public IEnumerator BranchLookupFollowsRenameAndUndoWithMusicDraftIntact()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            try
            {
                var document = MakeDocument().WithSection("branches",
                    "{\"demo\":{\"phases\":[{\"preDelay\":0,\"actions\":[{\"actionType\":4,\"key\":\"shield\",\"preDelay\":0}]}]}}");
                workspace.ShowUtility();
                workspace.CreateGUI();
                workspace.AddDocument(0, "分支改名测试", document.ToJson());
                var editor = workspace.FocusedEditor!;
                editor.Dialogs = new Confirm();
                var inspector = OpenInspector(workspace);
                yield return null;
                var root = inspector.rootVisualElement;

                Module(root, 11, "music-editor");
                var music = SelectMusicAction(root, 0);
                Find<TextField>(music, "music-input-preDelay").value = "6";
                Click(Find<Button>(music, "music-action-locate"));
                var branches = Find<VisualElement>(root, "branches-editor");
                var owner = Find<Foldout>(branches, "branch-node:" + BranchInspectorDrafts.Key("demo"));
                Assert.That(owner.value, Is.True);
                Find<TextField>(owner, "branches-target-key").value = "renamed";
                ClickText(owner, "重命名");
                Assert.That(editor.WorkspaceDocument.GetSection("branches")!["renamed"], Is.Not.Null);

                Module(root, 11, "music-editor");
                music = SelectMusicAction(root, 0);
                Assert.That(Number(music, "music-input-preDelay"), Is.EqualTo(6));
                Click(Find<Button>(music, "music-action-locate"));
                var renamed = Find<Foldout>(branches, "branch-node:" + BranchInspectorDrafts.Key("renamed", 0, 0));
                Assert.That(renamed.value, Is.True);
                Assert.That(Number(renamed, "branch-input-preDelay"), Is.EqualTo(6));

                editor.UndoFromShortcut();
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(document.ToJson()));
                var restored = OpenBranchAction(root, 0);
                Assert.That(Number(restored, "branch-input-preDelay"), Is.EqualTo(6));
                Click(Find<Button>(restored, "branch-action-music"));
                music = Find<VisualElement>(root, "music-action-edit");
                Assert.That(Number(music, "music-input-preDelay"), Is.EqualTo(6));
            }
            finally { UnityEngine.Object.DestroyImmediate(workspace); }
        }


        [UnityTest]
        public IEnumerator HeidiReplacementIsExplicitAndUnregisteredTriggerStaysAReference()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            try
            {
                var trigger = MusicAction("reference", 0);
                trigger["key"] = "rglk1csghost";
                var document = MakeDocument(trigger).WithSection("bgmEvent", "\"bat_shatterpoint_02\"");
                workspace.ShowUtility();
                workspace.CreateGUI();
                workspace.AddDocument(0, "未匹配音乐事件", document.ToJson());
                var editor = workspace.FocusedEditor!;
                var inspector = OpenInspector(workspace);
                yield return null;
                var root = inspector.rootVisualElement;

                var panel = Module(root, 11, "music-editor");
                Assert.That(Find<TextField>(panel, "music-opening-key").value, Is.EqualTo("bat_shatterpoint_02"));
                Assert.That(panel.Q<Button>("music-event-preview"), Is.Null);
                Click(Find<Button>(panel, "music-open-library"));
                var library = Find<VisualElement>(root, "music-library");
                Assert.That(Find<VisualElement>(root, "resource-panel-5").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(Find<Label>(library, "music-library-context").text, Does.Contain("bgmEvent"));
                Click(Find<Button>(library, "music-library-current-detail"));
                Assert.That(Find<VisualElement>(root, "music-library-detail").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Click(Find<Button>(root, "music-library-detail-close"));
                Click(Find<Button>(library, "music-event-suggested-key"));
                Assert.That(Find<TextField>(panel, "music-opening-key").value, Is.EqualTo("shatterpoint_02"));
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(document.ToJson()));
                Click(Find<Button>(panel, "music-opening-apply"));
                Assert.That(editor.WorkspaceDocument.GetSection("bgmEvent")!.Value<string>(), Is.EqualTo("shatterpoint_02"));

                var action = SelectMusicAction(root, 0);
                Assert.That(Find<Label>(action, "music-event-status").text, Does.Contain("局中事件未登记"));
                Assert.That(action.Q<Button>("music-event-preview"), Is.Null);
                Click(Find<Button>(action, "music-open-library"));
                Click(Find<Button>(library, "music-library-current-detail"));
                Assert.That(Find<Label>(root, "audio-detail-notice").text, Does.Contain("可试听参考曲"));
                Assert.That(Find<AudioDetailPlayer>(root, "music-detail-player"), Is.Not.Null);
                Click(Find<Button>(root, "music-library-detail-close"));
                Assert.That(Find<Label>(library, "music-library-context").text, Does.Contain("局中动作"));
                Assert.That(library.Q<Button>("music-event-suggested-key"), Is.Null);
                Assert.That(library.Q<Button>("music-library-assign"), Is.Null);
                Assert.That(WaveActions(editor).Single()!.Value<string>("key"), Is.EqualTo("rglk1csghost"));
                editor.UndoFromShortcut();
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(document.ToJson()));
            }
            finally { UnityEngine.Object.DestroyImmediate(workspace); }
        }

        [UnityTest]
        public IEnumerator RightMusicCardsHighlightBeforeAssigningAndUnbindOnMapSwitch()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            try
            {
                var document = MakeDocument();
                workspace.ShowUtility();
                workspace.CreateGUI();
                workspace.AddDocument(0, "资源选择 A", document.ToJson());
                var first = workspace.FocusedEditor!;
                workspace.AddDocument(0, "资源选择 B", document.ToJson());
                var second = workspace.FocusedEditor!;
                workspace.SelectDocument(first);
                var inspector = OpenInspector(workspace);
                yield return null;
                var root = inspector.rootVisualElement;
                var music = Module(root, 11, "music-editor");
                Click(Find<Button>(music, "music-open-library"));
                var library = Find<VisualElement>(root, "music-library");
                var list = Find<ListView>(library, "music-library-list");
                var alternate = Enumerable.Range(0, list.itemsSource.Count).Single(i =>
                    ((Arknights.MapEditor.Catalog.MusicEventCatalog.Entry)list.itemsSource[i]!).Key == "shatterpoint_02");
                var row = list.makeItem();
                list.bindItem(row, alternate);
                Click(Find<Button>(row, "music-library-select-shatterpoint_02"));
                list.bindItem(row, alternate);
                Assert.That(Find<Button>(row, "music-library-select-shatterpoint_02").ClassListContains("selected-tab"), Is.True);
                Assert.That(Find<TextField>(music, "music-opening-key").value, Is.EqualTo("shield"));
                Click(Find<Button>(library, "music-library-assign"));
                Assert.That(Find<TextField>(music, "music-opening-key").value, Is.EqualTo("shatterpoint_02"));
                Assert.That(first.WorkspaceDocument.ToJson(), Is.EqualTo(document.ToJson()));

                workspace.SelectDocument(second);
                yield return null;
                music = Module(root, 11, "music-editor");
                Click(Find<Button>(root, "resource-category-5"));
                library = Find<VisualElement>(root, "music-library");
                Assert.That(Find<Label>(library, "music-library-context").style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(Find<VisualElement>(library, "music-library-selection").style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(library.Q<Button>("music-event-preview"), Is.Null);
                Assert.That(library.Q<Button>("music-library-assign"), Is.Null);
                Assert.That(Find<TextField>(music, "music-opening-key").value, Is.EqualTo("shield"));
                Assert.That(second.WorkspaceDocument.ToJson(), Is.EqualTo(document.ToJson()));
            }
            finally { UnityEngine.Object.DestroyImmediate(workspace); }
        }


        [UnityTest]
        public IEnumerator RawBranchDraftRequiresReadingBeforeMusicCanApplyIt()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            try
            {
                var document = MakeDocument().WithSection("branches",
                    "{\"demo\":{\"phases\":[{\"preDelay\":0,\"actions\":[{\"actionType\":4,\"key\":\"shield\",\"preDelay\":0}]}]}}");
                workspace.ShowUtility();
                workspace.CreateGUI();
                workspace.AddDocument(0, "分支原始 JSON", document.ToJson());
                var editor = workspace.FocusedEditor!;
                var inspector = OpenInspector(workspace);
                yield return null;
                var root = inspector.rootVisualElement;

                Module(root, 7, "branches-editor");
                var action = OpenBranchAction(root, 0);
                var raw = Find<TextField>(action, "branch-action-json");
                var expected = JObject.Parse(raw.value);
                expected["preDelay"] = 4.25;
                raw.value = expected.ToString(Formatting.None);
                Module(root, 11, "music-editor");
                var music = SelectMusicAction(root, 0);
                Assert.That(music.Query<Button>().ToList().Any(button =>
                    button.text == "应用动作" && button.enabledInHierarchy), Is.False);
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(document.ToJson()));

                Module(root, 7, "branches-editor");
                action = OpenBranchAction(root, 0);
                Assert.That(JToken.DeepEquals(JObject.Parse(Find<TextField>(action, "branch-action-json").value), expected), Is.True);
                ClickText(action, "读取为待应用字段");
                Module(root, 11, "music-editor");
                music = SelectMusicAction(root, 0);
                Assert.That(Number(music, "music-input-preDelay"), Is.EqualTo(4.25));
                ClickText(music, "应用动作");
                Assert.That(BranchActions(editor).Single()!.Value<double>("preDelay"), Is.EqualTo(4.25));
                Assert.That(BranchActions(editor).Single()!["actionType"]!.Type, Is.EqualTo(JTokenType.Integer));
            }
            finally { UnityEngine.Object.DestroyImmediate(workspace); }
        }
    }
}
