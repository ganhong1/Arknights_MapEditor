#nullable enable
using System;
using System.Collections.Generic;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Storage;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Editor
{
    // Separate object: undoing a document edit must never undo a successful disk save.
    public sealed class DocumentSaveState : ScriptableObject
    {
        public string Json = "";
        public string Path = "";
        public string Hash = "";
    }

    public sealed class DocumentSession : ScriptableObject
    {
        [SerializeField] private string json = "";
        [SerializeField] private DocumentSaveState? saved;
        [SerializeField] private bool localHistory;
        [SerializeField] private List<string> undo = new List<string>();
        [SerializeField] private List<string> redo = new List<string>();
        [SerializeField] private List<string> undoRevisions = new List<string>();
        [SerializeField] private List<string> redoRevisions = new List<string>();
        [SerializeField] private string revision = "";
        internal const int HistoryLimit = 24;
        [NonSerialized] private string? cachedJson;
        [NonSerialized] private LevelDocument? cachedDocument;
        public string Json => json;
        internal string Revision => revision;
        public LevelDocument Document
        {
            get
            {
                // Undo/redo restores serialized JSON directly. Comparing the revision
                // here also handles that path and a fresh cache after domain reload.
                if (cachedDocument == null || !string.Equals(cachedJson, json, StringComparison.Ordinal))
                {
                    var parsed = LevelDocument.Parse(json);
                    cachedDocument = parsed; cachedJson = json;
                }
                return cachedDocument;
            }
        }
        public bool IsDirty => saved == null || json != saved.Json;
        public string SourcePath => saved == null ? "" : saved.Path;
        internal void UseLocalHistory() => localHistory = true;

        internal void StepHistory(bool forward)
        {
            var source = forward ? redo : undo;
            if (source.Count == 0) return;
            var revisions = forward ? redoRevisions : undoRevisions;
            AlignRevisions(source, revisions);
            PushHistory(forward ? undo : redo, forward ? undoRevisions : redoRevisions);
            var index = source.Count - 1;
            json = source[index];
            revision = revisions[index];
            source.RemoveAt(index);
            revisions.RemoveAt(index);
        }

        private static void AlignRevisions(List<string> entries, List<string> revisions)
        {
            while (revisions.Count < entries.Count) revisions.Insert(0, "");
        }

        private void PushHistory(List<string> entries, List<string> revisions)
        {
            AlignRevisions(entries, revisions);
            if (entries.Count == HistoryLimit)
            {
                entries.RemoveAt(0);
                revisions.RemoveAt(0);
            }
            entries.Add(json);
            revisions.Add(revision);
        }

        public RecoverySnapshot CaptureRecovery(string draft) => new RecoverySnapshot(
            json, draft, saved == null ? json : saved.Json, SourcePath, saved == null ? "" : saved.Hash);

        public string RestoreRecovery(RecoverySnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var document = LevelDocument.Parse(snapshot.DocumentJson).ToJson();
            var checkpoint = LevelDocument.Parse(snapshot.SavedJson).ToJson();
            if ((snapshot.SourcePath.Length == 0) != (snapshot.SourceHash.Length == 0))
                throw new FormatException("Recovery source path and digest must be supplied together.");
            if (snapshot.SourceHash.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(snapshot.SourceHash, "\\A[0-9a-fA-F]{64}\\z"))
                throw new FormatException("Invalid recovery source digest.");
            // Validate the complete envelope before changing live state. Draft may be invalid JSON.
            Load(document, snapshot.SourcePath, snapshot.SourceHash);
            saved!.Json = checkpoint;
            return snapshot.DraftJson;
        }

        public void Load(string text, string path, string hash)
        {
            var document = LevelDocument.Parse(text);
            var valid = document.ToJson();
            Undo.ClearUndo(this);
            undo.Clear();
            redo.Clear();
            undoRevisions.Clear();
            redoRevisions.Clear();
            revision = "";
            json = valid;
            if (saved == null) saved = CreateInstance<DocumentSaveState>();
            saved.hideFlags = HideFlags.HideAndDontSave;
            saved.Json = valid;
            saved.Path = path;
            saved.Hash = hash;
            cachedDocument = document; cachedJson = valid;
        }

        public void Edit(LevelDocument next, string label) => Edit(next, label, false);

        internal void Edit(LevelDocument next, string label, bool recordUnchanged)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            var text = next.ToJson();
            if (text == json && !recordUnchanged) return;
            if (localHistory)
            {
                PushHistory(undo, undoRevisions);
                redo.Clear();
                redoRevisions.Clear();
            }
            else
            {
                Undo.IncrementCurrentGroup();
                Undo.RegisterCompleteObjectUndo(this, label);
            }
            json = text;
            revision = Guid.NewGuid().ToString("N");
            cachedDocument = next; cachedJson = text;
        }

        public void Save(string path)
        {
            var existingPath = SourcePath;
            var expectedHash = string.Equals(path, existingPath, StringComparison.OrdinalIgnoreCase)
                && saved != null && saved.Hash.Length > 0 ? saved.Hash : null;
            var snapshot = DocumentFileStore.Save(path, json, expectedHash);
            if (saved == null) saved = CreateInstance<DocumentSaveState>();
            saved.hideFlags = HideFlags.HideAndDontSave;
            saved.Json = json;
            saved.Path = path;
            saved.Hash = snapshot.Sha256;
        }

        private void OnDestroy()
        {
            if (saved != null) DestroyImmediate(saved);
        }
    }
}
