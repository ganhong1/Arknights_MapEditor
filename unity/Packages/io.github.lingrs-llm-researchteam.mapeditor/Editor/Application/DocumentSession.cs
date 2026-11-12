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
        private const int HistoryLimit = 24;
        [NonSerialized] private string? cachedJson;
        [NonSerialized] private LevelDocument? cachedDocument;
        public string Json => json;
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
            PushHistory(forward ? undo : redo, json);
            var index = source.Count - 1;
            json = source[index];
            source.RemoveAt(index);
        }

        private static void PushHistory(List<string> entries, string value)
        {
            if (entries.Count == HistoryLimit) entries.RemoveAt(0);
            entries.Add(value);
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
            json = valid;
            if (saved == null) saved = CreateInstance<DocumentSaveState>();
            saved.hideFlags = HideFlags.HideAndDontSave;
            saved.Json = valid;
            saved.Path = path;
            saved.Hash = hash;
            cachedDocument = document; cachedJson = valid;
        }

        public void Edit(LevelDocument next, string label)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            var text = next.ToJson();
            if (text == json) return;
            if (localHistory)
            {
                PushHistory(undo, json);
                redo.Clear();
            }
            else
            {
                Undo.IncrementCurrentGroup();
                Undo.RegisterCompleteObjectUndo(this, label);
            }
            json = text;
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
