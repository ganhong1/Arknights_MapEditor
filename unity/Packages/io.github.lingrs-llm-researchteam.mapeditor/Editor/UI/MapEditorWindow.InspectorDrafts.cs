#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        [SerializeField] private string inspectorJson = "";
        [NonSerialized] private InspectorDraftStore? inspectorStore;
        [NonSerialized] private readonly Dictionary<string, LocalDraftBinding> inspectorBindings = new Dictionary<string, LocalDraftBinding>();
        private sealed class LocalDraftBinding
        {
            internal string Document = "", Target = "", Initial = "";
            internal Action<string> Display = _ => { };
        }
        private InspectorDraftStore InspectorStore => inspectorStore ??= inspectorJson.Length == 0 ? new InspectorDraftStore() : ParseInspectorRecovery(inspectorJson);
        private bool InspectorDirty => InspectorStore.IsDirty;
        private string CaptureInspectorRecovery() => inspectorJson = InspectorStore.ToJson();
        private static InspectorDraftStore ParseInspectorRecovery(string json)
        {
            var store = InspectorDraftStore.Parse(json); ValidateInspectorRecovery(store); return store;
        }
        private static void ValidateInspectorRecovery(InspectorDraftStore store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (store.TryRead("routePoints", out var raw)) ValidateRoutePointsDraft(raw);
            if (store.TryReadInitial("routePoints", out var initial)) ValidateRoutePointsDraft(initial);
            if (store.TryRead(SceneRouteSlot, out var route))
            {
                ParseRouteObject(route);
                if (!store.TryReadTarget(SceneRouteSlot, out var target) || !TryReadRouteTarget(target, out _, out _))
                    throw new FormatException("路线草稿索引无效。");
            }
        }
        private static void ValidateRoutePointsDraft(string raw)
        {
            var token = LevelDocument.Create(1, 1).WithSection("points", raw).GetSection("points");
            if (!(token is JObject points) || points.Count != 5 || points["checkpoints"]?.Type != JTokenType.String)
                throw new FormatException("Invalid route coordinate draft.");
            foreach (var name in new[] { "startRow", "startCol", "endRow", "endCol" })
            {
                var value = points[name];
                if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float && value.Type != JTokenType.Raw) ||
                    !float.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || float.IsNaN(number) || float.IsInfinity(number))
                    throw new FormatException("Invalid route coordinate draft field: " + name);
            }
        }
        private void ApplyInspectorRecovery(InspectorDraftStore store)
        {
            ValidateInspectorRecovery(store);
            ExitMapRuleScene();
            ResetScheduleBindings();
            inspectorStore = store;
            inspectorJson = store.ToJson();
            RestorePresetBinding();
            enemySelectionSlot = store.DirtySlots("enemy/").FirstOrDefault() ?? "";
            enemyReferenceIndex = -1;
            inlineEnemySelected = false;
            selectedTileData = 0;
            foreach (var pair in inspectorBindings)
                if (store.TryRead(pair.Key, out var raw)) DisplayLocalDraft(pair.Value, raw);
            refreshEnemyDrafts?.Invoke();
            RestoreRouteEditing();
            if (workbenchTab == 2 && session != null) wavesPanel?.Refresh(true);
            if (workbenchTab == 7 && session != null) branchesPanel?.Refresh(true);
        }
        private void ResetScheduleBindings()
        {
            waveHistory.Clear();
            branchHistory.Clear();
            lastBranchDocument = null;
            lastWaveDocument = null;
            expandedWaveRecords.Clear();
            expandedBranchRecords.Clear();
            selectedWaveRecord = "";
            ClearMusicSelection(false);
            ClearMusicSelection(true);
        }

        private void ClearInspectorDrafts()
        {
            ExitMapRuleScene();
            ResetScheduleBindings();
            inspectorStore = new InspectorDraftStore(); inspectorJson = inspectorStore.ToJson();
            RestoreRouteEditing();
            RestorePresetBinding();
            selectedTileData = 0;
            enemySelectionSlot = "";
            enemyReferenceIndex = -1;
            inlineEnemySelected = false;
            refreshEnemyDrafts?.Invoke();
            libraryOperatorDraft = false;
            selectedOperatorId = operatorSelectionInitialJson = "";
            SetDevicePlacement(false);
            foreach (var binding in inspectorBindings.Values)
            {
                // Stale displayed content must not silently become a draft for a new document.
                binding.Document = ""; binding.Initial = "";
                binding.Display("");
            }
        }
        private void PersistInspectorDrafts()
        {
            inspectorJson = InspectorStore.ToJson();
            hasUnsavedChanges = WorkspaceDirty;
            RefreshDocumentNotice();
            workspaceOwner?.DocumentChanged(this);
        }
        private void BindLocalTextField(string slot, TextField field, Func<string> target)
        {
            BindLocalDraft(slot, field.value, target(), raw => field.SetValueWithoutNotify(raw));
            field.RegisterValueChangedCallback(e => CaptureLocalDraft(slot, e.newValue));
        }
        private void BindLocalDraft(string slot, string initial, string target, Action<string> display)
        {
            var binding = new LocalDraftBinding { Document = session!.Json, Target = target, Initial = initial, Display = display };
            inspectorBindings[slot] = binding;
            InspectorStore.Begin(slot, binding.Document, target, initial);
            if (InspectorStore.TryRead(slot, out var raw)) DisplayLocalDraft(binding, raw);
            PersistInspectorDrafts();
        }
        private void CaptureLocalDraft(string slot, string raw)
        {
            try
            {
                var binding = inspectorBindings[slot];
                InspectorStore.Begin(slot, binding.Document, binding.Target, binding.Initial);
                if (!InspectorStore.TryReadTarget(slot, out var boundTarget) || boundTarget != binding.Target)
                    throw new EditorOperationRejectedException("草稿属于另一个字段或条目，请先选择其原目标。");
                InspectorStore.Update(slot, raw);
                PersistInspectorDrafts();
            }
            catch (Exception error)
            {
                if (inspectorBindings.TryGetValue(slot, out var binding) && InspectorStore.TryRead(slot, out var saved)) DisplayLocalDraft(binding, saved);
                ReportWindowFailure("document.inspector-draft", error);
            }
        }
        private static InspectorDraftStore? RebaseModuleDrafts(InspectorDraftStore store, LevelDocument before, LevelDocument after)
        {
            var slots = store.DirtySlots("").Concat(new[] { SceneRouteSlot, "tileData", "preset" }
                .Where(slot => store.TryReadTarget(slot, out _))).Distinct(StringComparer.Ordinal).ToArray();
            if (slots.Length == 0 || ReferenceEquals(before, after)) return null;
            var beforeJson = before.ToJson();
            var afterJson = after.ToJson();
            if (beforeJson == afterJson) return null;
            InspectorDraftStore? result = null;
            bool Same(string key) => JToken.DeepEquals(before.GetSection(key), after.GetSection(key));
            bool SameMapField(string key) => JToken.DeepEquals(before.GetSection("mapData")?[key], after.GetSection("mapData")?[key]);
            bool SameSize() => before.Width == after.Width && before.Height == after.Height;
            bool SameScope(string slot, string target)
            {
                if (slot == SceneRouteSlot)
                    return TryReadRouteTarget(target, out var library, out _) && Same(RouteLibraries[library]) && SameSize();
                if (slot == "tileData" || slot == "mapData:root") return Same("mapData");
                if (slot == "global:levelId") return target == "levelId" && Same(target);
                if (slot == DimensionsSlot || slot == "mapData:map")
                    return SameMapField("map") && SameMapField("tiles");
                if (slot == "tilesDisallowToLocate") return Same(slot) && SameSize();
                if (slot.StartsWith("mapData:", StringComparison.Ordinal))
                    return SameMapField(slot.Substring("mapData:".Length)) && SameSize();
                if (slot == "preset")
                    return TryReadPresetTarget(target, out var section, out var role, out _) &&
                        JToken.DeepEquals(PresetCollection(before, section, role), PresetCollection(after, section, role)) &&
                        (!IsInstanceRole(role) || SameSize());
                return slot == "section" && Same(target);
            }
            foreach (var slot in slots)
            {
                if (!store.TryReadTarget(slot, out var target) ||
                    !store.IsCurrent(slot, beforeJson, target) || !store.TryReadInitial(slot, out var initial) ||
                    !store.TryRead(slot, out var raw)) continue;
                var nextTarget = RebindEnemyDraft(slot, target, before, after);
                if (nextTarget == null && !SameScope(slot, target)) continue;
                result ??= InspectorDraftStore.Parse(store.ToJson());
                result.SetDraft(slot, afterJson, nextTarget ?? target, initial, raw, store.HasPendingRaw(slot));
            }
            return result;
        }

        private static string? RebindEnemyDraft(string slot, string target, LevelDocument before, LevelDocument after)
        {
            var inline = slot.StartsWith("enemy/inline/", StringComparison.Ordinal);
            if ((!inline && !slot.StartsWith("enemy/reference/", StringComparison.Ordinal)) ||
                !int.TryParse(target, out var index) || index < 0) return null;
            var field = inline ? "enemies" : "enemyDbRefs";
            var oldItems = before.GetSection(field) as JArray;
            var newItems = after.GetSection(field) as JArray;
            if (oldItems == null || newItems == null || index >= oldItems.Count || EnemySlot(oldItems, inline, index) != slot)
                return null;
            var moved = FindRecordAfterChange(oldItems, newItems, index);
            return moved >= 0 && EnemySlot(newItems, inline, moved) == slot
                ? moved.ToString(CultureInfo.InvariantCulture) : null;
        }

        private static int FindRecordAfterChange(JArray? before, JArray? after, int index)
        {
            if (JToken.DeepEquals(before, after)) return index;
            if (before == null || after == null || index < 0 || index >= before.Count) return -1;
            var value = before[index];
            if (before.Count(item => JToken.DeepEquals(item, value)) != 1) return -1;
            var matches = Enumerable.Range(0, after.Count).Where(i => JToken.DeepEquals(after[i], value)).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : -1;
        }

        private void ReconcileRecordSelections(LevelDocument previous, LevelDocument current)
        {
            if (routeDraft == null)
                routeListSelection = FindRecordAfterChange(previous.GetSection(RouteLibrary) as JArray,
                    current.GetSection(RouteLibrary) as JArray, routeListSelection);
            var oldPresets = PresetCollection(previous, devicePresetSection, devicePresetRole);
            var newPresets = PresetCollection(current, devicePresetSection, devicePresetRole);
            if (!JToken.DeepEquals(oldPresets, newPresets))
            {
                SetDevicePlacement(false);
                if (!InspectorStore.HasChanges("preset"))
                    setPresetSelectionIndex?.Invoke(FindRecordAfterChange(oldPresets, newPresets, devicePresetIndex));
            }
            if (!JToken.DeepEquals(previous.GetSection("mapData")?["tiles"], current.GetSection("mapData")?["tiles"]) &&
                !InspectorStore.HasChanges("tileData"))
                selectedTileData = FindRecordAfterChange(previous.GetSection("mapData")?["tiles"] as JArray,
                    current.GetSection("mapData")?["tiles"] as JArray, selectedTileData);
        }
        private void RequireLocalDraft(string slot, string target)
        {
            RequireAppliedDraft();
            InspectorStore.RequireCurrent(slot, session!.Json, target);
        }
        // Root fields can safely keep their draft when only another section changed.
        // Positional editors use their own structural remapping and dependency checks.
        private void RefreshRootDraftBaseline(string slot, string target, string current, string? legacyInitial = null)
        {
            if (!inspectorBindings.TryGetValue(slot, out var binding)) return;
            var store = InspectorStore;
            var document = session!.Json;
            if (!store.HasChanges(slot))
            {
                if (binding.Document != document || binding.Initial != current ||
                    store.TryReadInitial(slot, out var cleanInitial) &&
                    (cleanInitial != current || !store.IsCurrent(slot, document, target)))
                    ReloadLocalDraft(slot, target, current);
                return;
            }

            if (!store.TryReadTarget(slot, out var boundTarget) || boundTarget != target ||
                !store.TryReadInitial(slot, out var initial) || !store.TryRead(slot, out var raw)) return;

            var matchesRevision = store.IsCurrent(slot, document, target);
            // Older environmentSe drafts used "null" for an absent field. Only
            // migrate that representation while its original revision is known.
            if (initial != current && !(matchesRevision && legacyInitial != null && initial == legacyInitial)) return;
            if (!matchesRevision || initial != current || binding.Document != document || binding.Initial != current)
                ReplaceLocalDraft(slot, target, current, raw, confirmDiscard: false);
        }

        private bool ReloadLocalDraft(string slot, string target, string raw)
            => ReplaceLocalDraft(slot, target, raw, raw);

        private bool ReplaceLocalDraft(string slot, string target, string initial, string raw, bool confirmDiscard = true)
        {
            if (confirmDiscard && InspectorStore.HasChanges(slot) &&
                !Dialogs.Discard("未应用字段草稿", "重新读取会丢弃该字段草稿，是否继续？")) return false;
            var replacement = InspectorDraftStore.Parse(InspectorStore.ToJson());
            replacement.Remove(slot);
            replacement.Begin(slot, session!.Json, target, initial);
            replacement.Update(slot, raw);
            var binding = inspectorBindings[slot];
            binding.Document = session!.Json; binding.Target = target; binding.Initial = initial;
            inspectorStore = replacement; DisplayLocalDraft(binding, raw);
            PersistInspectorDrafts(); return true;
        }
        private void DisplayLocalDraft(LocalDraftBinding binding, string raw)
        {
            try { binding.Display(raw); }
            catch (Exception error)
            {
                if (status != null) status.text = "草稿已保留，但无法显示为结构化字段：" + error.Message;
            }
        }
        private void AppliedLocalDraft(string slot)
        {
            if (InspectorStore.TryRead(slot, out var raw) && inspectorBindings.TryGetValue(slot, out var binding))
            {
                binding.Document = session!.Json; binding.Initial = raw; binding.Display(raw);
            }
            InspectorStore.Remove(slot); PersistInspectorDrafts();
        }
    }
}
