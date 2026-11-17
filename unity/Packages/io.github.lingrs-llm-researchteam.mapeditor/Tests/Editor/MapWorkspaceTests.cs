using System.Collections;
using System.Linq;
using System.Reflection;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Tests
{
    public sealed class MapWorkspaceTests
    {
        private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static object Field(object owner, string name) => owner.GetType().GetField(name, Instance).GetValue(owner);
        private static object Property(object owner, string name) => owner.GetType().GetProperty(name, Instance).GetValue(owner);
        private static void Call(object owner, string name, params object[] args)
        {
            if (name == "PaintWorkspaceCells") owner.GetType().GetProperty("TileReplacementPrompt", Instance)
                .SetValue(owner, new System.Func<int, int>(_ => 2));
            owner.GetType().GetMethod(name, Instance).Invoke(owner, args);
        }
        private static object Selected(object group) => Property(group, "Selected");

        [UnityTest]
        public IEnumerator SplitTabsKeepIndependentViewAndDocumentState()
        {
            var currentStage = StageUtility.GetCurrentStage();
            var window = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            try
            {
                window.ShowUtility();
                window.position = new Rect(40, 70, 900, 680);
                window.CreateGUI();
                Call(window, "ShowInspector");
                var inspector = (MapWorkspaceInspectorWindow)Field(window, "inspectorWindow");
                inspector.CreateGUI();
                var side = inspector.rootVisualElement;
                Call(window, "ToggleSplit");
                var left = Field(window, "left");
                var right = Field(window, "right");
                var blank = Selected(left);
                var sameDocument = Selected(right);
                Assert.That(Field(sameDocument, "Editor"), Is.SameAs(Field(blank, "Editor")));
                Assert.That(Field(sameDocument, "Camera"), Is.Not.SameAs(Field(blank, "Camera")));

                Call(window, "AddDocument", 1, "参考地图", LevelDocument.Create(12, 7).ToJson(), "", "", true);
                var reference = Selected(right);
                var referenceEditor = Field(reference, "Editor");
                var blankEditor = Field(blank, "Editor");
                yield return null;
                yield return null;
                var root = window.rootVisualElement;
                Assert.That(root.Q("workspace-inspector"), Is.Null);
                Assert.That(side.Q("workspace-document-tabs").Query<Button>().ToList().Count, Is.EqualTo(2));
                var pairedTab = root.Q("workspace-left").Q(className: "workspace-document-tab");
                Assert.That(pairedTab.Q<Button>(className: "workspace-document-title").text, Does.Contain("未命名地图"));
                Assert.That(pairedTab.Q<Button>(className: "workspace-document-close").text, Is.EqualTo("×"));
                Assert.That(pairedTab.ClassListContains("workspace-selected"), Is.True);
                var leftCanvas = root.Q("workspace-left").Q("map-viewport-canvas");
                var rightCanvas = root.Q("workspace-right").Q("map-viewport-canvas");
                Assert.That(leftCanvas.layout.height, Is.GreaterThan(200f));
                Assert.That(rightCanvas.layout.height, Is.EqualTo(leftCanvas.layout.height).Within(2f));
                Assert.That(leftCanvas.worldBound.xMax, Is.LessThanOrEqualTo(rightCanvas.worldBound.xMin));
                var viewports = (System.Array)Field(window, "viewports");
                var rightViewport = viewports.GetValue(1);

                Call(window, "FocusGroup", 0);
                Assert.That(root.Q<Label>("workspace-active-document").text, Does.Contain("左组"));
                Call(blankEditor, "PaintWorkspaceCells", new[] { Vector2Int.zero } as object);
                Assert.That(Tile(blankEditor), Is.EqualTo("tile_road"));
                Assert.That(Tile(referenceEditor), Is.EqualTo("tile_forbidden"));
                var draft = side.Q("workspace-inspector").Q<TextField>("leveldata-json");
                draft.value = "unapplied left draft";
                Assert.That(SendUndo(side), Is.True);
                Assert.That(draft.value, Is.EqualTo("unapplied left draft"));
                Assert.That(Tile(blankEditor), Is.EqualTo("tile_road"));
                var referenceTab = side.Q("workspace-document-tabs").Query<Button>().ToList()[1];
                using (var submit = NavigationSubmitEvent.GetPooled())
                {
                    submit.target = referenceTab;
                    referenceTab.SendEvent(submit);
                }
                Assert.That(root.Q<Label>("workspace-active-document").text, Does.Contain("参考地图"));
                Assert.That(side.Q("workspace-inspector").Q<TextField>("leveldata-json").value, Is.Not.EqualTo(draft.value));
                Call(window, "FocusGroup", 0);
                Assert.That(side.Q("workspace-inspector").Q<TextField>("leveldata-json").value, Is.EqualTo("unapplied left draft"));
                draft.value = ((LevelDocument)Property(blankEditor, "WorkspaceDocument")).ToJson();
                var textInput = draft.Q("unity-text-input");
                textInput.Focus();
                SendUndo(textInput);
                Assert.That(Tile(blankEditor), Is.EqualTo("tile_road"));
                Call(blankEditor, "SetWorkspaceEditLock", true);
                Assert.That(SendUndo(side), Is.True);
                Assert.That(Tile(blankEditor), Is.EqualTo("tile_road"));
                Call(blankEditor, "SetWorkspaceEditLock", false);
                Assert.That(SendUndo(side, EventModifiers.Control | EventModifiers.Shift), Is.False);
                Assert.That(Tile(blankEditor), Is.EqualTo("tile_road"));
                Call(referenceEditor, "PaintWorkspaceCells", new[] { Vector2Int.zero } as object);
                Assert.That(SendUndo(side), Is.True);
                Assert.That(Tile(blankEditor), Is.EqualTo("tile_forbidden"));
                Assert.That(Tile(referenceEditor), Is.EqualTo("tile_road"));

                Call(window, "AddDocument", 0, "左组第二张", LevelDocument.Create(5, 5).ToJson(), "", "", true);
                var second = Selected(left);
                Call(window, "Select", 0, blank);
                Assert.That(Selected(right), Is.SameAs(reference));
                Assert.That(viewports.GetValue(1), Is.SameAs(rightViewport));
                Call(window, "CloseTab", 0, second);
                Assert.That(Selected(left), Is.SameAs(blank));
                Assert.That(StageUtility.GetCurrentStage(), Is.SameAs(currentStage));
            }
            finally { Object.DestroyImmediate(window); }
            Assert.That(StageUtility.GetCurrentStage(), Is.SameAs(currentStage));
        }

        private static string Tile(object editor) => ((LevelDocument)Property(editor, "WorkspaceDocument"))
            .GetCell(0, 0)["tileKey"].ToString();

        private static bool SendUndo(VisualElement target, EventModifiers modifiers = EventModifiers.Control)
        {
            using var input = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Z, modifiers = modifiers });
            input.target = target;
            target.SendEvent(input);
            return input.isPropagationStopped;
        }

        [Test]
        public void NativeWorkspaceBindsEachCameraToItsOwnPreviewScene()
        {
            var hasDock = Resources.FindObjectsOfTypeAll<EditorWindow>().Any(view =>
                (view.GetType().FullName == "UnityEditor.GameView" ||
                 view.GetType() == typeof(SceneView) ||
                 view is NativeMapSceneView) && NativeMapSceneDock.IsInMainWindow(view));
            if (!hasDock) Assert.Ignore("此双场景集成测试需要 Unity 主窗口中的 Game 或 Scene 标签。");

            var stage = StageUtility.GetCurrentStage();
            var window = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            try
            {
                window.GetType().GetField("nativeScenes", Instance).SetValue(window, true);
                window.CreateGUI();
                Call(window, "ToggleSplit");
                Call(window, "AddDocument", 1, "另一张", LevelDocument.Create(12, 7).ToJson(), "", "", true);
                var views = (NativeMapSceneView[])Field(window, "nativeViews");
                Assert.That(views[0].camera.scene.IsValid(), Is.True);
                Assert.That(views[1].camera.scene.IsValid(), Is.True);
                Assert.That(views[0].camera.scene.handle, Is.Not.EqualTo(views[1].camera.scene.handle));
                Assert.That(window.rootVisualElement.Q("map-viewport"), Is.Null);
                Assert.That(window.rootVisualElement.Q("workspace-inspector"), Is.Not.Null);
                var toolbox = window.rootVisualElement.Q("workspace-toolbox");
                Assert.That(toolbox.Q("workspace-document-tabs"), Is.Null);
                Assert.That(window.rootVisualElement.Q("workspace-document-tabs").parent, Is.SameAs(toolbox.parent));
                Assert.That(window.rootVisualElement.Q("workspace-active-document").parent, Is.SameAs(toolbox.parent));
                Assert.That(toolbox.Query<Button>().ToList().Exists(button => button.text == "保存"), Is.True);
                Call(window, "ToggleNativeErase");
                Assert.That(toolbox.Q<Button>("native-erase").text, Is.EqualTo("置空 ✓"));
                Assert.That(StageUtility.GetCurrentStage(), Is.SameAs(stage));
                var right = views[1].camera.scene;
                Call(window, "CloseNativeGroup", 1);
                Assert.That(right.IsValid(), Is.False);
                Assert.That(views[0].camera.scene.IsValid(), Is.True);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void EditLockFollowsDocumentAndCameraLockFollowsViewport()
        {
            var window = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            try
            {
                window.CreateGUI();
                Call(window, "ToggleSplit");
                var views = (System.Array)Field(window, "viewports");
                var left = views.GetValue(0);
                var right = views.GetValue(1);
                var editor = Field(left, "editor");
                var leftState = Field(left, "state");
                var rightState = Field(right, "state");
                Call(left, "ToggleEditLock");
                Assert.That(Field(right, "editor"), Is.SameAs(editor));
                Assert.That(Property(editor, "WorkspaceEditLocked"), Is.True);
                Assert.That(Assert.Throws<TargetInvocationException>(() =>
                    Call(editor, "PaintWorkspaceCells", new[] { Vector2Int.zero } as object)).InnerException,
                    Is.InstanceOf<System.InvalidOperationException>());
                Call(left, "SnapAxis", 0);
                Assert.That(Field(leftState, "Yaw"), Is.EqualTo(-90f));
                Assert.That(Property(leftState, "Orthographic"), Is.True);
                Call(left, "ToggleProjection");
                Assert.That(Property(leftState, "Orthographic"), Is.False);
                Assert.That(Field(leftState, "Yaw"), Is.EqualTo(-90f));

                Call(left, "ToggleCameraLock");
                Call(left, "SnapAxis", 2);
                Call(left, "ToggleProjection");
                Assert.That(Field(leftState, "Yaw"), Is.EqualTo(-90f));
                Assert.That(Property(leftState, "Orthographic"), Is.False);
                Assert.That(Field(rightState, "CameraLocked"), Is.False);
                Call(right, "SnapAxis", 2);
                Assert.That(Field(rightState, "Pitch"), Is.EqualTo(90f));
                Assert.That(Field(leftState, "Pitch"), Is.EqualTo(0f));
                Call(right, "ToggleEditLock");
                Call(editor, "PaintWorkspaceCells", new[] { Vector2Int.zero } as object);
                Assert.That(Tile(editor), Is.EqualTo("tile_road"));
                Assert.That(Field(leftState, "CameraLocked"), Is.True);
                Call(left, "ToggleErase");
                Assert.That(Property(editor, "EraseMode"), Is.True);
                Call(editor, "PaintWorkspaceCells", new[] { Vector2Int.zero } as object);
                Assert.That(Tile(editor), Is.EqualTo("tile_forbidden"));
                Call(editor, "StepEditorHistory", false);
                Assert.That(Tile(editor), Is.EqualTo("tile_road"));
            }
            finally { Object.DestroyImmediate(window); }
        }
    }
}
