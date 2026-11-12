#nullable enable
using System;
using System.IO;
using System.Reflection;
using Arknights.MapEditor.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Arknights.MapEditor.Tests
{
    public sealed class ProjectRecoveryUiTests
    {
        private string directory = null!;
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        [SetUp] public void SetUp() { directory=Path.Combine(Path.GetTempPath(),"ark-project-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory); }
        [TearDown] public void TearDown() => Directory.Delete(directory,true);

        [Test] public void RecoveryPreflightPreservesInvalidDraftWithoutWritingSource()
        {
            var source=Path.Combine(directory,"source.arkmap");File.WriteAllText(source,"untouched");
            var root=new Newtonsoft.Json.Linq.JObject { ["version"]=1,["projectPath"]=source,["projectHash"]=new string('a',64),["levelPath"]="../unfinished",["artJson"]="{bad",["savedLevel"]="level.json",["savedArt"]="[]" };
            var path=Path.Combine(directory,"sidecar.json");File.WriteAllText(path,root.ToString());
            var state=typeof(MapEditorWindow).GetMethod("ReadProjectRecovery",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{path});
            Assert.That(state,Is.Not.Null);Assert.That(File.ReadAllText(source),Is.EqualTo("untouched"));
        }

        [TestCase("{\"version\":2}")]
        [TestCase("{\"version\":1,\"projectPath\":null}")]
        public void RecoveryPreflightRejectsMalformedEnvelope(string content)
        {
            var path=Path.Combine(directory,"bad.json");File.WriteAllText(path,content);
            var failure=Assert.Throws<TargetInvocationException>(()=>typeof(MapEditorWindow).GetMethod("ReadProjectRecovery",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{path}));
            Assert.That(failure!.InnerException,Is.TypeOf<InvalidDataException>());
        }

        [Test] public void ResetDropsPriorProjectAssociationAndDraftDirtyState()
        {
            var window=ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                typeof(MapEditorWindow).GetField("projectPath",PrivateInstance)!.SetValue(window,"old.arkmap");
                typeof(MapEditorWindow).GetField("projectHash",PrivateInstance)!.SetValue(window,new string('a',64));
                typeof(MapEditorWindow).GetField("artJson",PrivateInstance)!.SetValue(window,"draft");
                Assert.That(typeof(MapEditorWindow).GetProperty("ProjectDirty",PrivateInstance)!.GetValue(window),Is.True);
                typeof(MapEditorWindow).GetMethod("ResetProjectState",PrivateInstance)!.Invoke(window,null);
                Assert.That(typeof(MapEditorWindow).GetField("projectPath",PrivateInstance)!.GetValue(window),Is.EqualTo(""));
                Assert.That(typeof(MapEditorWindow).GetProperty("ProjectDirty",PrivateInstance)!.GetValue(window),Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }
        [Test] public void ProjectBindingRejectsDifferentOrUnsavedDocument()
        {
            var method=typeof(MapEditorWindow).GetMethod("ValidateProjectLevelBinding",BindingFlags.NonPublic|BindingFlags.Static)!;
            var target=Path.Combine(directory,"a.json");
            Assert.Throws<TargetInvocationException>(()=>method.Invoke(null,new object[]{target,""}));
            Assert.Throws<TargetInvocationException>(()=>method.Invoke(null,new object[]{target,Path.Combine(directory,"b.json")}));
            Assert.DoesNotThrow(()=>method.Invoke(null,new object[]{target,target}));
        }

        [Test] public void LegacyRecoveryHasNoProjectState()
        {
            Assert.That(typeof(MapEditorWindow).GetMethod("ParseProjectRecovery",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{""}),Is.Null);
        }
    }
}
