#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
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
            inspectorStore = store ?? throw new ArgumentNullException(nameof(store));
            inspectorJson = store.ToJson();
            foreach (var pair in inspectorBindings)
                if (store.TryRead(pair.Key, out var raw)) DisplayLocalDraft(pair.Value, raw);
        }
        private void ClearInspectorDrafts()
        {
            inspectorStore = new InspectorDraftStore(); inspectorJson = inspectorStore.ToJson();
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
                InspectorStore.Update(slot, raw);
                PersistInspectorDrafts();
            }
            catch (Exception error)
            {
                if (InspectorStore.TryRead(slot, out var saved)) DisplayLocalDraft(inspectorBindings[slot], saved);
                if (status != null) status.text = error.Message;
            }
        }
        private void RequireLocalDraft(string slot, string target)
        {
            RequireAppliedDraft();
            InspectorStore.RequireCurrent(slot, session!.Json, target);
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
