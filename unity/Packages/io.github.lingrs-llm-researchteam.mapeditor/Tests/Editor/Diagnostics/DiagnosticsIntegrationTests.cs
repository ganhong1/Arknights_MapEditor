#nullable enable
using System;
using System.Linq;
using System.Reflection;
using Arknights.MapEditor.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Arknights.MapEditor.Tests
{
    public sealed class DiagnosticsIntegrationTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private sealed class CancelDialogs : IEditorFileDialogs
        {
            public string Open(string title, string directory, string extension) => "";
            public string Save(string title, string directory, string name, string extension) => "";
            public bool Discard(string title, string message) => true;
        }

        [Test]
        public void EditingRecordsDocumentIdentityAndNewRevisionWithoutPrivateContent()
        {
            var editor = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                var before = PluginLog.Recent().Select(row => row.Id).ToArray();
                var next = editor.WorkspaceDocument.WithSection("bgmEvent", "\"private-music-name\"");
                typeof(MapEditorWindow).GetMethod("EditSession", Flags)!.Invoke(editor,
                    new object?[] { next, "private-edit-label", null, null, null, null });
                var entry = PluginLog.Recent().First(row => !before.Contains(row.Id) && row.Operation == "document.edit");
                Assert.That(entry.DocumentId, Is.EqualTo(typeof(MapEditorWindow).GetField("recoveryId", Flags)!.GetValue(editor)));
                Assert.That(entry.Revision, Is.Not.Empty);
                Assert.That(entry.OperationId, Is.Not.Empty);
                Assert.That(entry.Outcome, Is.EqualTo("success"));
                Assert.That(entry.ToJson().ToString(), Does.Not.Contain("private-music-name").And.Not.Contain("private-edit-label"));
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(next.ToJson()));
            }
            finally { UnityEngine.Object.DestroyImmediate(editor); }
        }

        [Test]
        public void CancelledSavePreservesDraftAndRecordsCancellation()
        {
            var editor = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                editor.Dialogs = new CancelDialogs();
                var original = editor.WorkspaceDocument.ToJson();
                var result = typeof(MapEditorWindow).GetMethod("SaveDocument", Flags)!.Invoke(editor, new object[] { true });
                Assert.That(result, Is.False);
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(original));
                Assert.That(editor.WorkspacePath, Is.Empty);
                var entry = PluginLog.Recent().First(row => row.Operation == "document.save-as");
                Assert.That(entry.Outcome, Is.EqualTo("cancelled"));
                Assert.That(entry.Level, Is.EqualTo("info"));
            }
            finally { UnityEngine.Object.DestroyImmediate(editor); }
        }

        [Test]
        public void EditingLockIsAValidationRejectionAndKeepsTheDocument()
        {
            var editor = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                var original = editor.WorkspaceDocument.ToJson();
                editor.SetWorkspaceEditLock(true);
                var failure = Assert.Throws<TargetInvocationException>(() =>
                    typeof(MapEditorWindow).GetMethod("SaveDocument", Flags)!.Invoke(editor, new object[] { false }));
                Assert.That(failure!.InnerException, Is.TypeOf<EditorOperationRejectedException>());
                var entry = PluginLog.Recent().First(row => row.Operation == "document.save" && row.Outcome == "failed");
                Assert.That(entry.Category, Is.EqualTo("validation"));
                Assert.That(entry.Level, Is.EqualTo("info"));
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(original));
            }
            finally { UnityEngine.Object.DestroyImmediate(editor); }
        }

        [Test]
        public void InvalidSaveKeepsRawDraftAndOuterErrorUsesSameDiagnostic()
        {
            var editor = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                var original = editor.WorkspaceDocument.ToJson();
                const string raw = "{private-unfinished-draft";
                typeof(MapEditorWindow).GetField("draft", Flags)!.SetValue(editor, raw);
                var failure = Assert.Throws<TargetInvocationException>(() =>
                    typeof(MapEditorWindow).GetMethod("SaveDocument", Flags)!.Invoke(editor, new object[] { false }));
                var entry = PluginLog.Recent().First(row => row.Operation == "document.save" && row.Outcome == "failed");
                Assert.That(entry.Category, Is.EqualTo("validation"));
                Assert.That(PluginLog.Failure("document.command", failure!.InnerException!), Is.EqualTo(entry.Id));
                Assert.That(editor.WorkspaceDocument.ToJson(), Is.EqualTo(original));
                Assert.That(typeof(MapEditorWindow).GetField("draft", Flags)!.GetValue(editor), Is.EqualTo(raw));
                Assert.That(entry.ToJson().ToString(), Does.Not.Contain("private-unfinished-draft"));
            }
            finally { UnityEngine.Object.DestroyImmediate(editor); }
        }
    }
}
