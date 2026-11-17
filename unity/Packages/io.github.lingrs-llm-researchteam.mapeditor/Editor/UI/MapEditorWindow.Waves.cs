#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        [SerializeField] private List<string> expandedWaveRecords = new();
        [SerializeField] private string selectedWaveRecord = "";
        [SerializeField] private bool showAllWaveFields;
        [SerializeField] private List<WaveHistoryStep> waveHistory = new();
        private string lastWaveRevision = "";
        private LevelDocument? lastWaveDocument;
        private WavesPanel? wavesPanel;

        private void BuildSchedulePanel(VisualElement parent)
        {
            wavesPanel = new WavesPanel(this);
            parent.Add(wavesPanel);
        }

        private void RefreshWaves()
        {
            if (workbenchTab == 2 && session != null) wavesPanel?.Refresh();
        }

        private static bool CanKeepWaveBindings(LevelDocument current, LevelDocument next, bool allowAppend)
        {
            if (!JToken.DeepEquals(current.GetSection("waves"), next.GetSection("waves"))) return false;
            foreach (var section in new[] { "routes", "enemyDbRefs", "enemies" })
            {
                var before = current.GetSection(section);
                var after = next.GetSection(section);
                if (JToken.DeepEquals(before, after)) continue;
                // Appending definitions preserves existing choices. Other mutations can change their meaning.
                if (!allowAppend || after is not JArray appended ||
                    (before is JArray existing
                        ? existing.Count > appended.Count || existing.Where((item, index) =>
                            !JToken.DeepEquals(item, appended[index])).Any()
                        : before != null && before.Type != JTokenType.Null)) return false;
                if (section == "enemyDbRefs" && appended.Count > ((before as JArray)?.Count ?? 0) &&
                    current.GetSection("enemies") is JArray { Count: > 0 }) return false;
            }
            return true;
        }

        private void RecordWaveHistory(WaveHistoryStep step, string before)
        {
            if (before == session!.Revision) return;
            step.Bind(before, session.Revision);
            waveHistory.Add(step);
            RemapMusicSelection(false, key => step.Remap(key, true));
            if (waveHistory.Count > 2 * DocumentSession.HistoryLimit) waveHistory.RemoveAt(0);
        }

        private void RestoreWaveHistory()
        {
            if (lastWaveDocument == null || lastWaveRevision == session!.Revision) return;
            var step = waveHistory.LastOrDefault(item => item.Connects(lastWaveRevision, session.Revision));
            if (step == null && !JToken.DeepEquals(lastWaveDocument.GetSection("waves"), session.Document.GetSection("waves")))
                ClearMusicSelection(false);
            if (step == null && (!WaveInspectorDrafts.Saved(InspectorStore).Any() ||
                !CanKeepWaveBindings(lastWaveDocument, session.Document, false))) return;
            var forward = step?.IsForward(lastWaveRevision) ?? true;
            var waves = step == null ? null : session.Document.GetSection("waves");
            var store = WaveInspectorDrafts.Rebase(InspectorStore, lastWaveDocument.ToJson(), session.Json,
                key => step != null ? step.Remap(key, forward) : key, null,
                step?.Token(forward), step?.Token(!forward),
                step == null ? null : (key, initial, raw) => step.RebaseFields(key, initial, raw, waves));
            inspectorJson = store.ToJson();
            inspectorStore = store;
            if (step != null)
            {
                RemapWaveView(key => step.Remap(key, forward));
                RemapMusicSelection(false, key => step.Remap(key, forward));
            }
        }

        private void RemapWaveView(Func<string, string?> remap)
        {
            expandedWaveRecords = expandedWaveRecords.Select(remap).OfType<string>().ToList();
            selectedWaveRecord = remap(selectedWaveRecord) ?? "";
            wavesPanel?.RemapMusicReveal(remap);
            wavesPanel?.ResetPaging();
        }

        private void NoteWaveRevision()
        {
            lastWaveRevision = session!.Revision;
            lastWaveDocument = session.Document;
        }

        private void ShowWaveRoute(int route, bool play, string library = "routes")
        {
            var document = WorkspaceDocument;
            if (!WaveSchedule.HasRoute(document.GetSection(library) as JArray, route))
                throw new FormatException("该路线没有可预览的定义。");
            if (workspaceOwner == null) RequireAppliedDraft();
            if (workspaceOwner != null) workspaceOwner.FocusRouteScene(this);
            else if (ownedPreview == null) OpenPreview();
            var playback = RoutePreview;
            playback.SetVisible(true, document);
            playback.SetSource(RoutePreviewSource.Definitions);
            var index = playback.Entries.ToList().FindIndex(entry => entry.Library == library && entry.RouteIndex == route);
            if (index < 0) throw new FormatException("该路线没有可预览的定义。");
            playback.SetMode(RoutePreviewMode.Single);
            playback.Select(index);
            if (play) { playback.SetAnimate(true); playback.Replay(); }
            else { playback.SetAnimate(false); }
            SceneView.RepaintAll();
        }

        private void LocateWaveField(int[] address)
        {
            var path = "/waves";
            for (var i = 0; i < address.Length; i++)
            {
                if (i > 0) path += "/" + WaveSchedule.ChildField(i);
                path += "/" + address[i];
            }
            LocateDocumentPath(path);
        }

        private void OpenWavesEnemy(string section, int index)
        {
            selectWorkbenchTab?.Invoke(8);
            LocateDocumentPath("/" + section + "/" + index);
        }
    }
}
