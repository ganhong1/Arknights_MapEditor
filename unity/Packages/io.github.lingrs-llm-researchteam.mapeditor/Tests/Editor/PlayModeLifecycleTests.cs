using System.Collections;
using System.Reflection;
using Arknights.MapEditor.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Tests
{
    public sealed class PlayModeLifecycleTests
    {
        private bool oldEnabled;
        private EnterPlayModeOptions oldOptions;
        private MapEditorWindow window;
        [UnityTest]
        public IEnumerator EnteringPlayWithoutDomainReloadClosesOnlyPreviewAndRetainsDraft()
        {
            oldEnabled=EditorSettings.enterPlayModeOptionsEnabled;oldOptions=EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled=true;
            EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload|EnterPlayModeOptions.DisableSceneReload;
            window=ScriptableObject.CreateInstance<MapEditorWindow>();window.Show();yield return null;window.CreateGUI();
            typeof(MapEditorWindow).GetMethod("OpenPreview",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,null);
            var stage=StageUtility.GetCurrentStage() as MapPreviewStage;var mesh=stage.OwnedMesh;
            window.rootVisualElement.Q<TextField>("leveldata-json").value="unfinished playmode draft";
            yield return new EnterPlayMode();
            Assert.That(mesh==null,Is.True);Assert.That(StageUtility.GetCurrentStage(),Is.Not.SameAs(stage));
            yield return new ExitPlayMode();
            Assert.That(window,Is.Not.Null);window.CreateGUI();
            Assert.That(window.rootVisualElement.Q<TextField>("leveldata-json").value,Is.EqualTo("unfinished playmode draft"));
        }
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if(EditorApplication.isPlaying)yield return new ExitPlayMode();
            EditorSettings.enterPlayModeOptionsEnabled=oldEnabled;EditorSettings.enterPlayModeOptions=oldOptions;
            if(window!=null)Object.DestroyImmediate(window);
        }
    }
}
