#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arknights.MapEditor.Editor.Tests
{
    public sealed class MapRuleNativeInputTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static object Get(object obj, string name) => obj.GetType().GetField(name, Flags)!.GetValue(obj)!;
        private static object? Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Flags)!.Invoke(obj,args);
        private static void Property(object obj, string name, object value) => obj.GetType().GetProperty(name, Flags)!.SetValue(obj,value);
        private static void Tool(MapEditorWindow editor, string field, bool rectangle = false)
        {
            typeof(MapEditorWindow).GetField("workbenchTab", Flags)!.SetValue(editor,6);
            Property(editor,"MapRuleSceneField",field); Property(editor,"MapRuleRectangle",rectangle);
        }
        private static IEnumerator Project(NativeMapSceneView view, Vector3 world, Action<Vector2> result)
        {
            var received = false;
            void Read(SceneView current)
            {
                if (current != view || Event.current.type != EventType.Repaint) return;
                result(HandleUtility.WorldToGUIPoint(world)); received = true;
            }
            SceneView.duringSceneGui += Read;
            try
            {
                for (var i=0;i<20 && !received;i++) { view.Repaint(); yield return null; }
                Assert.That(received,Is.True,"Fixture SceneView must repaint before projecting input.");
            }
            finally { SceneView.duringSceneGui -= Read; }
        }
        private static string lastInputDiagnostic = "";
        private static string InputDiagnostic(NativeMapSceneView view, Event requested)
        {
            var editor=(MapEditorWindow)Get(view,"editor");
            return $"requested={requested.type}/{requested.rawType}; mouse={requested.mousePosition}; view={view.position}; current={Event.current?.type}/{Event.current?.rawType}; focus={EditorWindow.focusedWindow?.GetInstanceID()}/{view.GetInstanceID()}; canEdit={typeof(NativeMapSceneView).GetProperty("CanEditMapRules",Flags)!.GetValue(view)}; group={Get(view,"group")}; field={editor.MapRuleSceneField}; active={editor.MapRuleSceneActive}; locked={editor.WorkspaceEditLocked}; hot={GUIUtility.hotControl}; ruleControl={Get(view,"ruleControl")}; hover={Get(view,"ruleHover")}";
        }
        private static IEnumerator SceneInput(NativeMapSceneView view, Event input)
        {
            Assert.That(EditorWindow.focusedWindow, Is.SameAs(view), "Input fixture must own scene focus.");
            var dispatched=false;
            Exception? failure=null;
            void Dispatch(SceneView current)
            {
                if(current!=view || Event.current.type!=EventType.Repaint || dispatched) return;
                dispatched=true;
                try
                {
                    // Event.type is native-filtered in a repaint. Supply the input-adapter's captured type
                    // while using real SceneView camera, GUI control ownership and the production state machine.
                    lastInputDiagnostic = "BEFORE " + InputDiagnostic(view,input);
                    Call(view,"HandleMapRuleInput",input,(EventType?)input.rawType);
                    lastInputDiagnostic += " AFTER " + InputDiagnostic(view,input);
                    Debug.Log("[MapRuleNativeInputTests] " + lastInputDiagnostic);
                }
                catch(Exception error) { failure=error; }
            }
            SceneView.duringSceneGui+=Dispatch;
            try
            {
                for(var i=0;i<20 && !dispatched;i++) { view.Repaint(); yield return null; }
                Assert.That(dispatched,Is.True,"Scene input requires a live IMGUI frame.");
                if(failure!=null) throw failure;
            }
            finally { SceneView.duringSceneGui-=Dispatch; }
        }
        private static IEnumerator Mouse(NativeMapSceneView view, EventType type, Vector2 point) =>
            SceneInput(view,new Event {type=type,button=0,mousePosition=point});
        private static int Count(MapEditorWindow editor, string field) => (editor.WorkspaceDocument.GetMapRules(field) as JArray)?.Count ?? 0;

        [UnityTest]
        public IEnumerator NativeImguiContextDispatchPaintsCellsRectangleAndEdgesCancelsEscapeAndIsolatesScenes()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            try
            {
                workspace.CreateNativeView = () =>
                {
                    var scene = ScriptableObject.CreateInstance<NativeMapSceneView>();
                    scene.ShowUtility();
                    scene.minSize=new Vector2(600,500); scene.maxSize=new Vector2(800,700);
                    scene.position=new Rect(80,80,600,500); return scene;
                };
                typeof(MapWorkspaceWindow).GetField("nativeScenes",Flags)!.SetValue(workspace,true);
                workspace.CreateGUI(); Call(workspace,"ToggleSplit");
                Call(workspace,"AddDocument",1,"fixture right",LevelDocument.Create(5,5).ToJson(),"","",true);
                var views=(NativeMapSceneView[])Get(workspace,"nativeViews"); var left=views[0]; var right=views[1];
                left.position=new Rect(80,80,600,500); right.position=new Rect(140,120,600,500);
                yield return null; yield return null;
                Assert.That(left.position.width,Is.GreaterThan(500),"Fixture SceneView width must survive native window creation.");
                Assert.That(left.camera.pixelRect.height,Is.GreaterThan(200),"Fixture camera must have a usable viewport below its toolbar.");
                var a=(MapEditorWindow)Get(left,"editor"); var b=(MapEditorWindow)Get(right,"editor");
                ((HashSet<string>)Get(a,"visibleMapRules")).UnionWith(new[] { "blockEdges", "tilesDisallowToLocate" });
                ((HashSet<string>)Get(b,"visibleMapRules")).Add("tilesDisallowToLocate");
                Assert.That(a,Is.Not.SameAs(b)); Assert.That(left.camera.scene.handle,Is.Not.EqualTo(right.camera.scene.handle));
                left.LookAt(new Vector3(2.5f,0,2.5f),Quaternion.Euler(90,0,0),4,true,true);
                right.LookAt(new Vector3(2.5f,0,2.5f),Quaternion.Euler(90,0,0),4,true,true);
                left.Focus(); workspace.ActivateNativeGroup(0); Tool(a,"tilesDisallowToLocate"); yield return null;
                Vector2 first=default,last=default,edge=default;
                yield return Project(left,new Vector3(.5f,0,.5f),point=>first=point);
                Assert.That(a.MapRuleSceneActive, Is.True); Assert.That(workspace.OwnsRouteInput(a,0), Is.True);
                yield return Mouse(left,EventType.MouseDown,first);
                Assert.That(Get(left,"ruleHover"), Is.Not.Null, "MouseDown must reach the scene tool and hit a grid cell. " + lastInputDiagnostic);
                Assert.That((int)Get(left,"ruleControl"), Is.Not.Zero, "MouseDown must acquire its rule gesture control.");
                yield return Mouse(left,EventType.MouseUp,first);
                Assert.That(Count(a,"tilesDisallowToLocate"),Is.EqualTo(1)); Assert.That(Count(b,"tilesDisallowToLocate"),Is.Zero);
                Tool(a,"tilesDisallowToLocate",true);
                yield return Project(left,new Vector3(2.5f,0,2.5f),point=>last=point);
                yield return Mouse(left,EventType.MouseDown,first); yield return Mouse(left,EventType.MouseDrag,last);
                yield return SceneInput(left,new Event {type=EventType.KeyDown,keyCode=KeyCode.Escape}); yield return Mouse(left,EventType.MouseUp,last);
                Assert.That(Count(a,"tilesDisallowToLocate"),Is.EqualTo(1));
                yield return Mouse(left,EventType.MouseDown,first); yield return Mouse(left,EventType.MouseDrag,last); yield return Mouse(left,EventType.MouseUp,last);
                Assert.That(Count(a,"tilesDisallowToLocate"),Is.EqualTo(9));
                Tool(a,"blockEdges"); yield return Project(left,new Vector3(1.5f,.035f,2),point=>edge=point);
                yield return Mouse(left,EventType.MouseDown,edge); yield return Mouse(left,EventType.MouseUp,edge);
                Assert.That(Count(a,"blockEdges"),Is.EqualTo(1));
                var leftCamera=(MapViewportState)Get(left,"viewState"); left.RememberPose(); leftCamera.CameraLocked=true;
                Tool(a,"tilesDisallowToLocate"); yield return Project(left,new Vector3(3.5f,0,3.5f),point=>last=point);
                yield return Mouse(left,EventType.MouseDown,last); yield return Mouse(left,EventType.MouseUp,last);
                Assert.That(Count(a,"tilesDisallowToLocate"),Is.EqualTo(10));
                a.SetWorkspaceEditLock(true); yield return Project(left,new Vector3(4.5f,0,3.5f),point=>last=point);
                yield return Mouse(left,EventType.MouseDown,last); yield return Mouse(left,EventType.MouseUp,last);
                Assert.That(Count(a,"tilesDisallowToLocate"),Is.EqualTo(10)); a.SetWorkspaceEditLock(false);
                // Begin a gesture, then bind another document: releasing must not edit either document.
                yield return Mouse(left,EventType.MouseDown,last); var before=a.WorkspaceDocument.ToJson(); var other=b.WorkspaceDocument.ToJson();
                left.Bind(b,new MapViewportState()); yield return Mouse(left,EventType.MouseUp,last);
                Assert.That(a.WorkspaceDocument.ToJson(),Is.EqualTo(before)); Assert.That(b.WorkspaceDocument.ToJson(),Is.EqualTo(other));
                right.Focus(); workspace.ActivateNativeGroup(1); Tool(b,"tilesDisallowToLocate"); yield return null;
                yield return Project(right,new Vector3(.5f,0,.5f),point=>first=point);
                yield return Mouse(right,EventType.MouseDown,first); yield return Mouse(right,EventType.MouseUp,first);
                Assert.That(Count(b,"tilesDisallowToLocate"),Is.EqualTo(1)); Assert.That(Count(a,"tilesDisallowToLocate"),Is.EqualTo(10));
            }
            finally { UnityEngine.Object.DestroyImmediate(workspace); }
        }
    }
}
