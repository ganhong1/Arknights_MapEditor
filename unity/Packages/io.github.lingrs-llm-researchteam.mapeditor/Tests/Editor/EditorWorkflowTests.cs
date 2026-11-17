using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using Arknights.MapEditor.Storage;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Arknights.MapEditor.Tests
{
    public sealed class EditorWorkflowTests
    {
        private sealed class Dialogs : IEditorFileDialogs
        {
            public string OpenPath="", SavePath=""; public bool DiscardResult; public int SaveCalls;
            public string Open(string title,string directory,string extension)=>OpenPath;
            public string Save(string title,string directory,string name,string extension){SaveCalls++;return SavePath;}
            public bool Discard(string title,string message)=>DiscardResult;
        }
        private static object Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);
        private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
        private static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        private static string TempDirectory()
        {
            var path=Path.Combine(Application.dataPath,"../Library/MapEditorTestFiles/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;
        }
        [UnityTest]
        public IEnumerator TileDataFieldsPreserveUnknownPropertiesAndInvalidPlacementDraft()
        {
            var window=ScriptableObject.CreateInstance<MapEditorWindow>();
            try {
                window.Show(); yield return null;
                Set(window,"brushJson","{\"tileKey\":\"tile_road\",\"heightType\":\"LOWLAND\",\"future\":{\"keep\":42}}");
                window.CreateGUI();
                var session = Field<DocumentSession>(window, "session");
                var preview = window.rootVisualElement.Q<VisualElement>("tiledata-preview");
                Call(preview, "AddEntry", Call(window, "SelectedTerrain"));
                var field=preview.Q<UnityEditor.UIElements.PopupField<string>>("tiledata-field-heightType");
                field.value="HIGHLAND";
                var raw = preview.Q<TextField>("tiledata-preview-json");
                var tile=Newtonsoft.Json.Linq.JObject.Parse(raw.value);
                Assert.That((string)tile["heightType"],Is.EqualTo("HIGHLAND"));
                Assert.That((int)tile["future"]["keep"],Is.EqualTo(42));
                Call(preview, "Apply");
                var tiles = (Newtonsoft.Json.Linq.JArray)session.Document.GetSection("mapData")["tiles"];
                Assert.That((string)tiles[1]["heightType"], Is.EqualTo("HIGHLAND"));
                Assert.That((int)tiles[1]["future"]["keep"], Is.EqualTo(42));
                Assert.That((string)Newtonsoft.Json.Linq.JObject.Parse(Field<string>(window, "brushJson"))["heightType"], Is.EqualTo("LOWLAND"));

                var validTile = raw.value;
                raw.value = "invalid TileData draft";
                Assert.That(field.enabledSelf,Is.False);
                window.CreateGUI();
                raw = window.rootVisualElement.Q<TextField>("tiledata-preview-json");
                Assert.That(raw.value, Is.EqualTo("invalid TileData draft"));
                Assert.That(window.rootVisualElement.Q<UnityEditor.UIElements.PopupField<string>>("tiledata-field-heightType").enabledSelf, Is.False);
                raw.value = validTile;

                Set(window,"brushJson","invalid raw draft"); window.CreateGUI();
                Assert.That(Field<string>(window,"brushJson"),Is.EqualTo("invalid raw draft"));
                var before = session.Json;
                var promptCalls = 0;
                window.TileReplacementPrompt = _ => { promptCalls++; return 0; };
                var invalidPlacement = Assert.Throws<TargetInvocationException>(() => Call(window, "PlaceManagedTiles", new[] { new Vector2Int(0, 0) }));
                Assert.That(invalidPlacement.InnerException, Is.TypeOf<LevelDocumentException>());
                Assert.That(session.Json, Is.EqualTo(before));
                Assert.That(Field<string>(window,"brushJson"),Is.EqualTo("invalid raw draft"));
                Assert.That(promptCalls, Is.Zero);
                var library = window.rootVisualElement.Q<ListView>("terrain-library");
                Assert.That(library.itemsSource.Count, Is.EqualTo(97));
                window.rootVisualElement.Q<Toggle>("terrain-filter").value = true;
                Assert.That(library.itemsSource.Count, Is.EqualTo(92));
                window.DiscardChanges();
            } finally {Object.DestroyImmediate(window);}
        }

        [UnityTest]
        public IEnumerator TileDataEditingKeepsIndependentEntriesAndWritesDocument()
        {
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.Show();
                yield return null;
                Set(window, "workbenchTab", 6);
                window.CreateGUI();
                var session = Field<DocumentSession>(window, "session");
                var original = session.Json;
                var preview = window.rootVisualElement.Q<VisualElement>("tiledata-preview");
                var list = preview.Q<ListView>("tiledata-preview-list");
                var automatic = preview.Q<Toggle>("tiledata-preview-automatic");
                Assert.That(automatic.value, Is.True);
                Assert.That(list.itemsSource.Count, Is.EqualTo(1));

                var raw = preview.Q<TextField>("tiledata-preview-json");
                var tile = Newtonsoft.Json.Linq.JObject.Parse(raw.value);
                raw.value = tile.ToString(Newtonsoft.Json.Formatting.None);
                var draft = raw.value;
                Call(preview, "ShowView", true);
                Assert.That(list.style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(preview.Q<Button>("tiledata-preview-card-0").ClassListContains("selected-tab"), Is.True);
                Call(preview, "ShowView", false);
                Assert.That(raw.value, Is.EqualTo(draft));
                Assert.That(list.selectedIndex, Is.Zero);

                var fieldNames = new[] { "tileKey", "heightType", "buildableType", "passableMask", "playerSideMask",
                    "advancedBuildableMask", "blackboard", "effects" };
                var details = preview.Q("tiledata-row-tileKey").parent;
                var fieldRows = new List<string>();
                foreach (var child in details.Children())
                    if (child.name != null && child.name.StartsWith("tiledata-row-", StringComparison.Ordinal))
                        fieldRows.Add(child.name.Substring("tiledata-row-".Length));
                Assert.That(fieldRows, Is.EqualTo(fieldNames));
                Assert.That(preview.Q<TextField>("tiledata-field-tileKey").value, Is.EqualTo("tile_forbidden"));
                Assert.That(preview.Q<TextField>("tiledata-field-blackboard").value, Is.EqualTo("null"));
                Assert.That(preview.Q<TextField>("tiledata-field-advancedBuildableMask").isReadOnly, Is.True);
                var coordinates = preview.Q<Label>("tiledata-preview-coordinates");
                Assert.That(coordinates.text, Does.Contain("(0, 0)"));
                Assert.That(coordinates.ClassListContains("tiledata-preview-important"), Is.True);
                raw.value = tile.ToString(Newtonsoft.Json.Formatting.Indented);
                Call(preview, "AddEntry", tile);
                Assert.That(list.itemsSource.Count, Is.EqualTo(2));
                automatic.value = false;
                Call(preview, "Bind", null, 1, false);
                var previewGrid = Field<Newtonsoft.Json.Linq.JArray>(preview, "grid");
                Assert.That((int)previewGrid[previewGrid.Count - 1][0], Is.EqualTo(1));
                Assert.That((int)previewGrid[previewGrid.Count - 1][1], Is.EqualTo(0));
                Assert.That(session.Json, Is.Not.EqualTo(original));
                Assert.That(window.hasUnsavedChanges, Is.True);
                window.DiscardChanges();
            }
            finally { Object.DestroyImmediate(window); }
        }

        [UnityTest]
        public IEnumerator DocumentModesPreserveDraftAcrossToggleAndRebuild()
        {
            var window=ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.Show();yield return null;window.CreateGUI();
                var source=window.rootVisualElement.Q("leveldata-source-panel");
                Assert.That(source.style.display.value,Is.EqualTo(DisplayStyle.None));
                System.Action<string> click = name => typeof(Clickable).GetMethod("Invoke",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(window.rootVisualElement.Q<Button>(name).clickable,new object[]{null});
                window.rootVisualElement.Q<TextField>("leveldata-json").value="unapplied source";
                click("leveldata-json-mode");window.CreateGUI();
                Assert.That(window.rootVisualElement.Q("leveldata-source-panel").style.display.value,Is.EqualTo(DisplayStyle.Flex));
                Assert.That(window.rootVisualElement.Q<TextField>("leveldata-json").value,Is.EqualTo("unapplied source"));
                click("leveldata-tree-mode");window.CreateGUI();
                Assert.That(window.rootVisualElement.Q<TextField>("leveldata-json").value,Is.EqualTo("unapplied source"));
                Assert.That(window.hasUnsavedChanges,Is.True);
            }
            finally {Object.DestroyImmediate(window);}
        }

        [UnityTest]
        public IEnumerator PaintStrokeUndoSaveReopenPreservesUnknownFields()
        {
            var directory=TempDirectory();var window=ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.Show();yield return null;window.CreateGUI();
                var dialogs=new Dialogs {SavePath=Path.Combine(directory,"level.json"),DiscardResult=true};window.Dialogs=dialogs;
                var promptCalls = 0;
                window.TileReplacementPrompt = count => { Assert.That(count, Is.EqualTo(3)); promptCalls++; return 0; };
                var session=Field<DocumentSession>(window,"session");
                var doc=LevelDocument.Create(7,3).WithSection("future","{\"keep\":[null,1.00000000000000001]}");
                session.Load(doc.ToJson(),"","");Set(window,"draft",session.Json);Call(window,"OpenPreview");
                var stage=Field<MapPreviewStage>(window,"ownedPreview");
                var paint=Field<Action<IReadOnlyList<Vector2Int>>>(stage,"paint");
                paint(new[]{new Vector2Int(0,0),new Vector2Int(1,0),new Vector2Int(2,0)});
                Assert.That(promptCalls, Is.EqualTo(1));
                Assert.That(session.Document.GetCell(0,0)["tileKey"].ToString(),Is.EqualTo("tile_road"));
                Call(window,"StepEditorHistoryCore",false);Assert.That(session.Document.GetCell(0,2)["tileKey"].ToString(),Is.EqualTo("tile_forbidden"));
                Call(window,"StepEditorHistoryCore",true);window.SaveChanges();Assert.That(window.hasUnsavedChanges,Is.False);
                var exported=LevelDocument.Parse(File.ReadAllText(dialogs.SavePath));
                Assert.That(exported.GetSection("future").ToString(),Is.EqualTo(doc.GetSection("future").ToString()));
                dialogs.OpenPath=dialogs.SavePath;Call(window,"OpenDocument");
                Assert.That(session.Document.ToJson(false),Is.EqualTo(exported.ToJson(false)));
            }
            finally {Object.DestroyImmediate(window);Directory.Delete(directory,true);}
        }
        [UnityTest]
        public IEnumerator SaveCancelAndSwitchCancelRetainDocument()
        {
            var window=ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.Show();yield return null;window.CreateGUI();window.Dialogs=new Dialogs();
                var field=window.rootVisualElement.Q<TextField>("leveldata-json");field.value=LevelDocument.Create(4,3).WithLevelId("unsaved").ToJson();
                var unapplied = field.value;
                window.SaveChanges();Assert.That(window.hasUnsavedChanges,Is.True);
                Call(window,"NewDocument");Assert.That(Field<DocumentSession>(window,"session").Document.LevelId,Is.Empty);
                Assert.That(field.value, Is.EqualTo(unapplied));
            }
            finally {Object.DestroyImmediate(window);}
        }
        [UnityTest]
        public IEnumerator AutomaticRecoveryRetainsInvalidDraftAndIndependentArtDraft()
        {
            var window=ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.Show();yield return null;window.CreateGUI();
                window.rootVisualElement.Q<TextField>("leveldata-json").value="unfinished level JSON";
                Set(window,"artJson","unfinished art JSON");Call(window,"FlushRecovery");
                var cache=Field<AutoRecovery>(window,"recovery");
                var snapshot=RecoveryStore.Read(cache.CachePath,out _);
                Assert.That(snapshot.DraftJson,Is.EqualTo("unfinished level JSON"));
                Assert.That(snapshot.ProjectJson,Does.Contain("unfinished art JSON"));
                var sidecar=Path.Combine(Path.GetDirectoryName(cache.CachePath),cache.SessionId.ToString("N")+".artdraft.json");
                Assert.That(File.Exists(sidecar),Is.False);
                var dialogs=new Dialogs {OpenPath=cache.CachePath,DiscardResult=true};window.Dialogs=dialogs;
                Set(window,"draft","changed");Set(window,"artJson","changed art");Call(window,"RestoreRecovery");
                Assert.That(Field<string>(window,"draft"),Is.EqualTo("unfinished level JSON"));
                Assert.That(Field<string>(window,"artJson"),Is.EqualTo("unfinished art JSON"));
            }
            finally {Object.DestroyImmediate(window);}
        }
        [Test]
        public void RecoveryRetriesSameDraftAndTracksSourceCheckpointAfterSave()
        {
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            var target = Path.Combine(Path.GetTempPath(), "ark-map-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(target);
            try
            {
                var cache = new AutoRecovery(target, System.Guid.NewGuid());
                Set(window, "recovery", cache);
                Set(window, "loadRecoveryDigest", false);
                var current = Field<DocumentSession>(window, "session");
                var changed = current.Document.WithLevelId("pending");
                current.Edit(changed, "test");
                Set(window, "draft", current.Json);
                Set(window, "artJson", "unfinished art");
                Directory.Delete(target);
                File.WriteAllText(target, "directory unavailable");
                Call(window, "FlushRecovery");
                Assert.That(cache.ExpectedHash, Is.Null);
                Assert.That(Field<double>(window, "retryRecoveryAt"), Is.GreaterThan(0));
                File.Delete(target);
                Set(window, "retryRecoveryAt", 0.0);
                Set(window, "nextRecoveryTime", 0.0);
                Call(window, "UpdateRecovery");
                Assert.That(cache.Load().DraftJson, Is.EqualTo(current.Json));
                var savedPath = Path.Combine(target, "saved.json");
                current.Save(savedPath);
                Call(window, "FlushRecovery");
                var checkpoint = cache.Load();
                Assert.That(checkpoint.SourcePath, Is.EqualTo(savedPath));
                Assert.That(checkpoint.SourceHash, Is.EqualTo(DocumentFileStore.Read(savedPath).Sha256));
                Assert.That(checkpoint.SavedJson, Is.EqualTo(current.Json));

                File.WriteAllText(cache.CachePath, "external contents");
                Set(window, "draft", "new draft");
                Call(window, "FlushRecovery");
                var replacement = Field<AutoRecovery>(window, "recovery");
                Assert.That(replacement.SessionId, Is.Not.EqualTo(cache.SessionId));
                Assert.That(File.ReadAllText(cache.CachePath), Is.EqualTo("external contents"));
                Assert.That(replacement.Load().DraftJson, Is.EqualTo("new draft"));
            }
            finally
            {
                Object.DestroyImmediate(window);
                if (Directory.Exists(target)) Directory.Delete(target, true);
                else if (File.Exists(target)) File.Delete(target);
            }
        }

        [UnityTest]
        public IEnumerator CancelLevelSaveNeverStartsProjectSave()
        {
            var window=ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.Show();yield return null;window.CreateGUI();
                var dialogs=new Dialogs();window.Dialogs=dialogs;
                window.rootVisualElement.Q<TextField>("leveldata-json").value=LevelDocument.Create(4,3).WithLevelId("pending").ToJson();
                Call(window,"ApplyDraft");
                Set(window,"artJson","[{\"unfinished\":true}]");
                window.SaveChanges();
                Assert.That(dialogs.SaveCalls,Is.EqualTo(1));
                Assert.That(Field<string>(window,"projectPath"),Is.Empty);
                Assert.That(Field<string>(window,"artJson"),Does.Contain("unfinished"));
                Assert.That(window.hasUnsavedChanges,Is.True);
            }
            finally {Object.DestroyImmediate(window);}
        }

        [UnityTest]
        public IEnumerator CorruptUnifiedProjectRecoveryKeepsCurrentSessionAndDrafts()
        {
            var directory=TempDirectory();var window=ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.Show();yield return null;window.CreateGUI();
                var session=Field<DocumentSession>(window,"session");var before=session.Json;
                Set(window,"draft","existing invalid draft");Set(window,"artJson","existing art");
                var path=Path.Combine(directory,"bad.recovery.json");
                var incoming=LevelDocument.Create(2,2).WithLevelId("incoming").ToJson();
                RecoveryStore.Save(path,new RecoverySnapshot(incoming,incoming,incoming,"","","{broken",""),null);
                window.Dialogs=new Dialogs{OpenPath=path,DiscardResult=true};
                Assert.Throws<TargetInvocationException>(()=>Call(window,"RestoreRecovery"));
                Assert.That(session.Json,Is.EqualTo(before));
                Assert.That(Field<string>(window,"draft"),Is.EqualTo("existing invalid draft"));
                Assert.That(Field<string>(window,"artJson"),Is.EqualTo("existing art"));
            }
            finally {Object.DestroyImmediate(window);Directory.Delete(directory,true);}
        }

        [UnityTest]
        public IEnumerator LegacyRecoveryRestoresLevelAndClearsUnrelatedProjectAndInspectorState()
        {
            var directory = TempDirectory();
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.Show(); yield return null; window.CreateGUI();
                var session = Field<DocumentSession>(window, "session");
                var store = (InspectorDraftStore)typeof(MapEditorWindow).GetProperty("InspectorStore", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
                store.Begin("old-slot", session.Json, "routes", "[]");
                store.Update("old-slot", "unfinished local draft");
                Set(window, "projectPath", Path.Combine(directory, "old.arkmap"));
                Set(window, "projectHash", new string('a', 64));
                Set(window, "artJson", "unfinished art");
                var source = Path.Combine(directory, "level.json");
                var incoming = LevelDocument.Create(3, 2).WithLevelId("legacy").ToJson();
                var original = DocumentFileStore.Save(source, incoming, null);
                var envelope = new Newtonsoft.Json.Linq.JObject
                {
                    ["version"] = 1, ["DocumentJson"] = incoming, ["DraftJson"] = "unfinished legacy JSON",
                    ["SavedJson"] = incoming, ["SourcePath"] = source, ["SourceHash"] = original.Sha256
                };
                var path = Path.Combine(directory, "legacy.recovery.json");
                File.WriteAllText(path, envelope.ToString());
                window.Dialogs = new Dialogs { OpenPath = path, DiscardResult = true };
                Call(window, "RestoreRecovery");
                Assert.That(session.Document.LevelId, Is.EqualTo("legacy"));
                Assert.That(Field<string>(window, "draft"), Is.EqualTo("unfinished legacy JSON"));
                Assert.That(Field<string>(window, "projectPath"), Is.Empty);
                Assert.That(Field<string>(window, "projectHash"), Is.Empty);
                Assert.That(Field<string>(window, "artJson"), Is.EqualTo("[]"));
                var restored = (InspectorDraftStore)typeof(MapEditorWindow).GetProperty("InspectorStore", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
                Assert.That(restored.TryRead("old-slot", out _), Is.False);
                Assert.That(restored.IsDirty, Is.False);
                Assert.That(DocumentFileStore.Read(source).Sha256, Is.EqualTo(original.Sha256));
                Assert.That(window.hasUnsavedChanges, Is.True);
            }
            finally { Object.DestroyImmediate(window); Directory.Delete(directory, true); }
        }

        [UnityTest]
        public IEnumerator OpeningProjectClearsOldInspectorDraftsOnlyAfterSuccess()
        {
            var directory=TempDirectory();var window=ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                window.Show();yield return null;window.CreateGUI();
                var store=(InspectorDraftStore)typeof(MapEditorWindow).GetProperty("InspectorStore",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(window);
                store.Begin("test",Field<DocumentSession>(window,"session").Json,"routes","[]");store.Update("test","unfinished");
                var level=Path.Combine(directory,"level.json");File.WriteAllText(level,LevelDocument.Create(2,2).ToJson());
                var project=Path.Combine(directory,"project.arkmap");ArkMapProjectStore.Save(project,new ArkMapProject("level.json",Array.Empty<ArtMapping>()),null);
                window.Dialogs=new Dialogs{OpenPath=project,DiscardResult=true};Call(window,"OpenProjectDocument");
                var updated=(InspectorDraftStore)typeof(MapEditorWindow).GetProperty("InspectorStore",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(window);
                Assert.That(updated.IsDirty,Is.False);Assert.That(updated.TryRead("test",out _),Is.False);
                Assert.That(Field<DocumentSession>(window,"session").Document.Width,Is.EqualTo(2));
                Assert.DoesNotThrow(()=>Call(window,"RequireLocalDraft","section","routes"));
            }
            finally {Object.DestroyImmediate(window);Directory.Delete(directory,true);}
        }

        [UnityTest]
        public IEnumerator ExistingWriterIsFocusedWithoutReplacingSecondDocument()
        {
            var directory=TempDirectory();var first=ScriptableObject.CreateInstance<MapEditorWindow>();var second=ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                first.Show();second.Show();yield return null;
                var path=Path.Combine(directory,"level.json");File.WriteAllText(path,LevelDocument.Create(2,3).WithLevelId("owned").ToJson());
                first.Dialogs=new Dialogs {OpenPath=path};second.Dialogs=new Dialogs {OpenPath=path};
                Call(first,"OpenDocument");var before=Field<DocumentSession>(second,"session").Json;
                Call(second,"OpenDocument");Assert.That(Field<DocumentSession>(second,"session").Json,Is.EqualTo(before));
            }
            finally {Object.DestroyImmediate(first);Object.DestroyImmediate(second);Directory.Delete(directory,true);}
        }
        [UnityTest]
        public IEnumerator ClosingWindowDoesNotSwitchForeignStage()
        {
            var window=ScriptableObject.CreateInstance<MapEditorWindow>();MapPreviewStage foreign=null;
            try
            {
                window.Show();yield return null;Call(window,"OpenPreview");
                foreign=ScriptableObject.CreateInstance<MapPreviewStage>();foreign.Configure(LevelDocument.Create(2,2));StageUtility.GoToStage(foreign,false);
                Object.DestroyImmediate(window);yield return null;
                Assert.That(StageUtility.GetCurrentStage(),Is.SameAs(foreign));Assert.That(foreign.scene.IsValid(),Is.True);
                StageUtility.GoBackToPreviousStage();yield return null;
                Assert.That(StageUtility.GetCurrentStage(),Is.Not.Null);
            }
            finally {if(window!=null)Object.DestroyImmediate(window);if(foreign!=null&&StageUtility.GetCurrentStage()==foreign)StageUtility.GoBackToPreviousStage();StageUtility.GoToMainStage();}
        }
        [Test]
        public void InvalidDocumentCanOnlyBeSavedAsNewRawFile()
        {
            var directory=TempDirectory();var window=InvalidDocumentWindow.Open("{invalid", "diagnostic");
            try
            {
                var path=Path.Combine(directory,"invalid.json");window.SaveOriginal(path);Assert.That(File.ReadAllText(path),Is.EqualTo("{invalid"));
                Assert.Throws<DocumentFileConflictException>(()=>window.SaveOriginal(path));
            }
            finally {Object.DestroyImmediate(window);Directory.Delete(directory,true);}
        }
    }
}
