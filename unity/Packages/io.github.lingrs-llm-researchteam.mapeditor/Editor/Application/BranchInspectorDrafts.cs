#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Arknights.MapEditor.Editor
{
    internal static class BranchInspectorDrafts
    {
        private const string Prefix = "branches-tree:";
        private const string Detached = Prefix + "detached:";

        internal static string Key(string branch, params int[] address)
        {
            if (string.IsNullOrEmpty(branch) || address.Length > 2 || address.Any(index => index < 0))
                throw new ArgumentException("Invalid branch draft address.");
            var result = new JArray(branch);
            foreach (var index in address) result.Add(index);
            return result.ToString(Formatting.None);
        }

        internal static string Slot(string key)
        {
            using var sha = SHA256.Create();
            return Prefix + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "").ToLowerInvariant();
        }

        internal static bool ReadTarget(string key, out string branch, out int[] address)
        {
            branch = ""; address = Array.Empty<int>();
            try
            {
                using var input = new System.IO.StringReader(key);
                using var reader = new JsonTextReader(input) { DateParseHandling = DateParseHandling.None };
                if (JToken.Load(reader) is not JArray parts || parts.Count < 1 || parts.Count > 3 ||
                    parts[0].Type != JTokenType.String || string.IsNullOrEmpty(parts[0].Value<string>())) return false;
                var indices = parts.Skip(1).Select(WaveSchedule.Integer).ToArray();
                if (indices.Any(index => index == null || index < 0)) return false;
                branch = parts[0].Value<string>()!;
                address = indices.Select(index => index!.Value).ToArray();
                return Key(branch, address) == key;
            }
            catch (JsonException) { return false; }
        }

        internal static IEnumerable<string> Saved(InspectorDraftStore store) => store.DirtySlots(Prefix);
        internal static string? Target(InspectorDraftStore store, string slot) =>
            !slot.StartsWith(Detached, StringComparison.Ordinal) && store.TryReadTarget(slot, out var key) &&
            ReadTarget(key, out _, out _) && slot == Slot(key) ? key : null;

        internal static bool IsCurrent(InspectorDraftStore store, string slot, string document, string key)
        {
            try { store.RequireCurrent(slot, document, key); return true; }
            catch (InvalidOperationException) { return false; }
        }

        internal static JObject? ReadRecord(JObject document, string key)
        {
            if (!ReadTarget(key, out var branch, out var address)) return null;
            JToken? node = (document["branches"] as JObject)?[branch];
            for (var depth = 0; depth < address.Length; depth++)
            {
                var rows = (node as JObject)?[depth == 0 ? "phases" : "actions"] as JArray;
                if (rows == null || address[depth] >= rows.Count) return null;
                node = rows[address[depth]];
            }
            return node as JObject;
        }

        internal static JObject Fields(JObject record, int depth)
        {
            var result = (JObject)record.DeepClone();
            if (depth < 2) result.Remove(depth == 0 ? "phases" : "actions");
            return result;
        }

        internal static InspectorDraftStore Rebase(InspectorDraftStore previous, string before, string after,
            Func<string, string?> remap, string? applied = null, string? detach = null, string? restore = null,
            Func<string, string, string, (string Initial, string Raw)?>? rebaseFields = null)
        {
            var next = InspectorDraftStore.Parse(previous.ToJson());
            var active = new List<(string Key, string Initial, string Raw, bool RawPending)>();
            foreach (var slot in Saved(previous))
            {
                var target = Target(previous, slot);
                if (target != null && !IsCurrent(previous, slot, before, target)) continue;
                if (target != null && target == applied) { next.Remove(slot); continue; }
                string? key;
                if (target == null)
                {
                    if (restore == null || !slot.StartsWith(Detached + restore, StringComparison.Ordinal) ||
                        !previous.TryReadTarget(slot, out var original) || !ReadTarget(original, out _, out _)) continue;
                    key = original;
                }
                else key = remap(target);
                if (key == null)
                {
                    next.RenameSlot(slot, Detached + detach + Guid.NewGuid().ToString("N"));
                    continue;
                }
                previous.TryReadInitial(slot, out var initial);
                previous.TryRead(slot, out var raw);
                if (target != null && rebaseFields != null)
                {
                    var updated = rebaseFields(key, initial, raw);
                    if (updated == null)
                    {
                        next.RenameSlot(slot, Detached + detach + Guid.NewGuid().ToString("N"));
                        continue;
                    }
                    (initial, raw) = updated.Value;
                }
                next.Remove(slot);
                if (initial != raw) active.Add((key, initial, raw, previous.HasPendingRaw(slot)));
            }
            foreach (var entry in active)
            {
                var slot = Slot(entry.Key);
                if (next.HasChanges(slot)) next.RenameSlot(slot, Detached + Guid.NewGuid().ToString("N"));
                next.SetDraft(slot, after, entry.Key, entry.Initial, entry.Raw, entry.RawPending);
            }
            return next;
        }
    }

    internal enum BranchHistoryKind { Fields, Create, Rename, DeleteBranch, Insert, Copy, Move, Delete }

    [Serializable]
    internal sealed class BranchHistoryStep
    {
        [SerializeField] private string before = "", after = "", token = Guid.NewGuid().ToString("N");
        [SerializeField] private string branch, newBranch;
        [SerializeField] private int phase, index, target;
        [SerializeField] private BranchHistoryKind kind;

        internal BranchHistoryStep(BranchHistoryKind kind, string branch, int phase = -1, int index = -1,
            int target = -1, string newBranch = "")
        {
            this.kind = kind; this.branch = branch; this.phase = phase; this.index = index;
            this.target = target; this.newBranch = newBranch;
        }

        internal void Bind(string from, string to) { before = from; after = to; }
        internal bool Connects(string from, string to) =>
            before == from && after == to || after == from && before == to;
        internal bool IsForward(string from) => before == from;
        internal string Token(bool forward) => token + (forward ? ":forward:" : ":back:");

        internal string? Remap(string key, bool forward)
        {
            if (!BranchInspectorDrafts.ReadTarget(key, out var owner, out var address)) return null;
            if (kind == BranchHistoryKind.Rename)
                return owner == (forward ? branch : newBranch)
                    ? BranchInspectorDrafts.Key(forward ? newBranch : branch, address) : key;
            if (owner != branch) return key;
            if (kind == BranchHistoryKind.Create) return forward ? key : null;
            if (kind == BranchHistoryKind.DeleteBranch) return forward ? null : key;
            if (kind == BranchHistoryKind.Fields) return key;
            var depth = phase < 0 ? 0 : 1;
            if (address.Length <= depth || depth == 1 && address[0] != phase) return key;
            // A pending action belongs to the phase, not to its actions array.
            if (depth == 1 && address[1] == int.MaxValue) return key;
            var mapped = RemapIndex(address[depth], forward);
            if (mapped == null) return null;
            address[depth] = mapped.Value;
            return BranchInspectorDrafts.Key(owner, address);
        }

        private int? RemapIndex(int position, bool forward)
        {
            switch (kind)
            {
                case BranchHistoryKind.Insert:
                case BranchHistoryKind.Copy:
                    var inserted = kind == BranchHistoryKind.Copy ? target : index;
                    return forward ? (position >= inserted ? position + 1 : position) :
                        position == inserted ? null : position > inserted ? position - 1 : position;
                case BranchHistoryKind.Delete:
                    return forward ? (position == index ? null : position > index ? position - 1 : position) :
                        position >= index ? position + 1 : position;
                case BranchHistoryKind.Move:
                    var from = forward ? index : target;
                    var to = forward ? target : index;
                    if (position == from) return to;
                    if (from < to && position > from && position <= to) return position - 1;
                    if (to < from && position >= to && position < from) return position + 1;
                    return position;
                default: return position;
            }
        }

        internal (string Initial, string Raw)? RebaseFields(string key, string initial, string raw, JObject document)
        {
            if (!BranchInspectorDrafts.ReadTarget(key, out var owner, out var address)) return null;
            if (address.Length == 2 && address[1] == int.MaxValue)
                return BranchInspectorDrafts.ReadRecord(document, BranchInspectorDrafts.Key(owner, address[0])) != null
                    ? (initial, raw) : null;
            if (BranchInspectorDrafts.ReadRecord(document, key) is not JObject record) return null;
            var baseline = BranchInspectorDrafts.Fields(record, address.Length);
            if (baseline.ToString(Formatting.None) == initial) return (initial, raw);
            try
            {
                var source = LevelDocument.ParseEnemyDefinition(initial);
                var value = LevelDocument.ParseEnemyDefinition(raw);
                var merged = (JObject)baseline.DeepClone();
                foreach (var field in source.Properties().Select(item => item.Name)
                    .Union(value.Properties().Select(item => item.Name), StringComparer.Ordinal))
                {
                    if (JToken.DeepEquals(source[field], value[field])) continue;
                    if (value[field] is JToken edited) merged[field] = edited.DeepClone();
                    else merged.Remove(field);
                }
                return (baseline.ToString(Formatting.None), merged.ToString(Formatting.None));
            }
            catch (FormatException) { return null; }
        }
    }
}
