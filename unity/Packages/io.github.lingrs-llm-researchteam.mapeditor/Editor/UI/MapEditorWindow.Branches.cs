#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        [SerializeField] private List<string> expandedBranchRecords = new();
        [SerializeField] private List<BranchHistoryStep> branchHistory = new();
        private BranchesPanel? branchesPanel;
        private string lastBranchRevision = "";
        private LevelDocument? lastBranchDocument;

        private void BuildBranchesPanel(VisualElement parent)
        {
            branchesPanel = new BranchesPanel(this);
            parent.Add(branchesPanel);
        }

        private void RefreshBranches()
        {
            if (workbenchTab == 7 && session != null) branchesPanel?.Refresh();
        }

        private void NoteBranchRevision()
        {
            lastBranchRevision = session!.Revision;
            lastBranchDocument = session.Document;
        }

        private void RecordBranchHistory(BranchHistoryStep step, string before)
        {
            if (before == session!.Revision) return;
            step.Bind(before, session.Revision);
            branchHistory.Add(step);
            RemapMusicSelection(true, key => step.Remap(key, true));
            if (branchHistory.Count > 2 * DocumentSession.HistoryLimit) branchHistory.RemoveAt(0);
        }

        private static bool CanKeepBranchBindings(LevelDocument before, LevelDocument after) =>
            new[] { "branches", "extraRoutes", "enemyDbRefs", "enemies", "predefines", "hardPredefines" }
                .All(section => JToken.DeepEquals(before.GetSection(section), after.GetSection(section)));

        private void RestoreBranchHistory()
        {
            if (lastBranchDocument == null || lastBranchRevision == session!.Revision) return;
            var step = branchHistory.LastOrDefault(item => item.Connects(lastBranchRevision, session.Revision));
            if (step == null && !JToken.DeepEquals(lastBranchDocument.GetSection("branches"), session.Document.GetSection("branches")))
                ClearMusicSelection(true);
            if (step == null && !CanKeepBranchBindings(lastBranchDocument, session.Document)) return;
            var forward = step?.IsForward(lastBranchRevision) ?? true;
            var document = ReadBranchDocument(session.Document);
            inspectorStore = BranchInspectorDrafts.Rebase(InspectorStore, lastBranchDocument.ToJson(), session.Json,
                key => step?.Remap(key, forward) ?? (step == null ? key : null), null,
                step?.Token(forward), step?.Token(!forward),
                step == null ? null : (key, initial, raw) => step.RebaseFields(key, initial, raw, document));
            inspectorJson = inspectorStore.ToJson();
            if (step != null)
            {
                RemapBranchView(key => step.Remap(key, forward));
                RemapMusicSelection(true, key => step.Remap(key, forward));
            }
        }

        private void RemapBranchView(Func<string, string?> remap)
        {
            expandedBranchRecords = expandedBranchRecords.Select(remap).OfType<string>().ToList();
            branchesPanel?.RemapMusicReveal(remap);
        }

        private static JObject ReadBranchDocument(LevelDocument document) => document.CopyRoot();
    }
}
