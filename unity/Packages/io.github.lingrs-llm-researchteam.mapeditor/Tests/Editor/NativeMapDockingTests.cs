using System.Linq;
using System.Reflection;
using Arknights.MapEditor.Editor;
using Arknights.MapEditor.Storage;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Tests
{
    public sealed class NativeMapDockingTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static object Field(object owner, string name) => owner.GetType().GetField(name, Instance).GetValue(owner);
        private static object Call(object owner, string name, params object[] args) =>
            owner.GetType().GetMethod(name, Instance).Invoke(owner, args);
        private static object Parent(EditorWindow window) => typeof(EditorWindow).GetField("m_Parent", Instance).GetValue(window);
        private static NativeMapSceneView View(MapWorkspaceWindow workspace) => ((NativeMapSceneView[])Field(workspace, "nativeViews"))[0];

        private static bool IsMainWindow(EditorWindow window)
        {
            var parent = Parent(window);
            var container = parent?.GetType().GetProperty("window", Instance)?.GetValue(parent);
            return container?.GetType().GetProperty("showMode", Instance)?.GetValue(container)?.ToString() == "MainWindow";
        }

        private static EditorWindow MainGame()
        {
            var game = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(window =>
                window.GetType().FullName == "UnityEditor.GameView" && IsMainWindow(window));
            if (game == null) Assert.Ignore("此停靠集成测试需要 Unity 主窗口中的 Game 标签。");
            return game;
        }

        private static MapWorkspaceWindow Workspace()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            workspace.GetType().GetField("nativeScenes", Instance).SetValue(workspace, true);
            return workspace;
        }

        [Test]
        public void NewSceneUsesMainGameInsteadOfFloatingScene()
        {
            var game = MainGame();
            var floatingScene = ScriptableObject.CreateInstance<SceneView>();
            var workspace = Workspace();
            try
            {
                floatingScene.Show();
                Assert.That(floatingScene.docked, Is.False);
                workspace.CreateGUI();
                Assert.That(View(workspace).docked, Is.True);
                Assert.That(Parent(View(workspace)), Is.SameAs(Parent(game)));
                Assert.That(floatingScene.docked, Is.False);
            }
            finally
            {
                // The workspace itself is never shown; its OnDestroy closes its scene tabs.
                Object.DestroyImmediate(workspace);
                floatingScene.Close();
                game.ShowTab();
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void NewMapAndClosedSceneReopenInMainTabsWithoutChangingPreviousDraft(int floatingTabCount)
        {
            var game = MainGame();
            var workspace = Workspace();
            SceneView companion = null;
            try
            {
                workspace.CreateGUI();
                var old = workspace.GetType().GetProperty("FocusedEditor", Instance).GetValue(workspace);
                old.GetType().GetField("draft", Instance).SetValue(old, "an unapplied map draft");
                var snapshot = (RecoverySnapshot)Call(old, "CaptureWindowRecovery");
                var view = View(workspace);
                if (floatingTabCount > 0)
                {
                    var state = Field(view, "viewState");
                    Call(workspace, "CloseNativeGroup", 0);
                    view = ScriptableObject.CreateInstance<NativeMapSceneView>();
                    view.Show();
                    ((NativeMapSceneView[])Field(workspace, "nativeViews"))[0] = view;
                    Call(view, "Attach", workspace, 0);
                    Call(view, "Bind", old, state);
                    Assert.That(view.docked, Is.False);
                    if (floatingTabCount == 2)
                    {
                        companion = ScriptableObject.CreateInstance<SceneView>();
                        Parent(view).GetType().GetMethod("AddTab", Instance, null,
                            new[] { typeof(EditorWindow), typeof(bool) }, null)
                            .Invoke(Parent(view), new object[] { companion, true });
                        Assert.That(view.docked, Is.True, "Unity also calls a floating multi-tab host docked.");
                    }
                }

                Call(workspace, "AddBlank", 0, true);
                var current = View(workspace);
                Assert.That(current.docked, Is.True);
                Assert.That(Parent(current), Is.SameAs(Parent(game)));
                Assert.That(Parent(current).GetType().GetProperty("actualView", Instance).GetValue(Parent(current)), Is.SameAs(current));
                if (floatingTabCount == 0) Assert.That(current, Is.SameAs(view));
                else Assert.That(view == null, Is.True);

                var after = (RecoverySnapshot)Call(old, "CaptureWindowRecovery");
                Assert.That(after.DocumentJson, Is.EqualTo(snapshot.DocumentJson));
                Assert.That(after.DraftJson, Is.EqualTo(snapshot.DraftJson));
                Assert.That(after.InspectorJson, Is.EqualTo(snapshot.InspectorJson));

                Call(workspace, "CloseNativeGroup", 0);
                Call(workspace, "AddBlank", 0, true);
                Assert.That(View(workspace).docked, Is.True);
                Assert.That(Parent(View(workspace)), Is.SameAs(Parent(game)));
                Assert.That(((RecoverySnapshot)Call(old, "CaptureWindowRecovery")).DraftJson, Is.EqualTo(snapshot.DraftJson));
            }
            finally
            {
                Object.DestroyImmediate(workspace);
                if (companion != null) companion.Close();
                game.ShowTab();
            }
        }

        [Test]
        public void DockingFailureLeavesOriginalSelectionSceneAndDraftTogether()
        {
            var game = MainGame();
            var workspace = Workspace();
            try
            {
                workspace.CreateGUI();
                var old = workspace.GetType().GetProperty("FocusedEditor", Instance).GetValue(workspace);
                old.GetType().GetField("draft", Instance).SetValue(old, "retained draft");
                var state = Field(View(workspace), "viewState");
                Call(workspace, "CloseNativeGroup", 0);
                var floating = ScriptableObject.CreateInstance<NativeMapSceneView>();
                floating.Show();
                ((NativeMapSceneView[])Field(workspace, "nativeViews"))[0] = floating;
                Call(floating, "Attach", workspace, 0);
                Call(floating, "Bind", old, state);
                var editorCount = Resources.FindObjectsOfTypeAll<MapEditorWindow>().Length;
                workspace.GetType().GetProperty("CreateNativeView", Instance).SetValue(workspace,
                    new System.Func<NativeMapSceneView>(() => throw new System.InvalidOperationException("No main docking anchor")));

                Assert.That(Assert.Throws<TargetInvocationException>(() => Call(workspace, "AddBlank", 0, true))
                    .InnerException, Is.TypeOf<System.InvalidOperationException>());
                Assert.That(workspace.GetType().GetProperty("FocusedEditor", Instance).GetValue(workspace), Is.SameAs(old));
                Assert.That(View(workspace), Is.SameAs(floating));
                Assert.That(Field(floating, "editor"), Is.SameAs(old));
                Assert.That(((System.Collections.IList)Field(Field(workspace, "left"), "Tabs")).Count, Is.EqualTo(1));
                Assert.That(Resources.FindObjectsOfTypeAll<MapEditorWindow>().Length, Is.EqualTo(editorCount));
                Assert.That(((RecoverySnapshot)Call(old, "CaptureWindowRecovery")).DraftJson, Is.EqualTo("retained draft"));
            }
            finally
            {
                Object.DestroyImmediate(workspace);
                game.ShowTab();
            }
        }
    }
}
