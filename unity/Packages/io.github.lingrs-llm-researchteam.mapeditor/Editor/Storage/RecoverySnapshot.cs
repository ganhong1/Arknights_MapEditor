#nullable enable
using System;

namespace Arknights.MapEditor.Storage
{
    public sealed class RecoverySnapshot
    {
        public string DocumentJson { get; }
        public string DraftJson { get; }
        public string SavedJson { get; }
        public string SourcePath { get; }
        public string SourceHash { get; }
        public string ProjectJson { get; }
        public string InspectorJson { get; }

        public bool HasSameContent(RecoverySnapshot other) =>
            DocumentJson == other.DocumentJson && DraftJson == other.DraftJson && SavedJson == other.SavedJson &&
            SourcePath == other.SourcePath && SourceHash == other.SourceHash && ProjectJson == other.ProjectJson &&
            InspectorJson == other.InspectorJson;

        public RecoverySnapshot(string documentJson, string draftJson, string savedJson, string sourcePath, string sourceHash, string projectJson = "", string inspectorJson = "")
        {
            DocumentJson = documentJson ?? throw new ArgumentNullException(nameof(documentJson));
            DraftJson = draftJson ?? throw new ArgumentNullException(nameof(draftJson));
            SavedJson = savedJson ?? throw new ArgumentNullException(nameof(savedJson));
            SourcePath = sourcePath ?? throw new ArgumentNullException(nameof(sourcePath));
            SourceHash = sourceHash ?? throw new ArgumentNullException(nameof(sourceHash));
            ProjectJson = projectJson ?? throw new ArgumentNullException(nameof(projectJson));
            InspectorJson = inspectorJson ?? throw new ArgumentNullException(nameof(inspectorJson));
        }
    }
}
