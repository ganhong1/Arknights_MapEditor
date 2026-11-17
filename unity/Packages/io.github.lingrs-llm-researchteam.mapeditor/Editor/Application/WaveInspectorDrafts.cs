#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Arknights.MapEditor.Editor
{
    internal static class WaveInspectorDrafts
    {
        private const string Prefix = "waves-tree:";
        private const string Detached = Prefix + "detached:";

        internal static string Slot(string key) => Prefix + key;

        internal static bool IsCurrent(InspectorDraftStore store, string slot, string document, string key)
        {
            try { store.RequireCurrent(slot, document, key); return true; }
            catch (InvalidOperationException) { return false; }
        }

        internal static IEnumerable<string> Saved(InspectorDraftStore store) => store.DirtySlots(Prefix);
        internal static string? Target(InspectorDraftStore store, string slot) =>
            !slot.StartsWith(Detached, StringComparison.Ordinal) && store.TryReadTarget(slot, out var target) &&
            Regex.IsMatch(target, @"^\d+(?:/\d+){0,2}(?:/new)?$") && slot == Slot(target)
                ? target : null;

        internal static InspectorDraftStore Rebase(InspectorDraftStore previous, string before, string after,
            Func<string, string?> remap, string? applied, string? detach = null, string? restore = null,
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
                        !previous.TryReadTarget(slot, out var original)) continue;
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

        internal static string? Remap(string key, int[] parent, Func<int, int?> map)
        {
            var parts = key.Split('/');
            if (parts.Length <= parent.Length) return key;
            for (var i = 0; i < parent.Length; i++)
                if (parts[i] != parent[i].ToString(CultureInfo.InvariantCulture)) return key;
            if (!int.TryParse(parts[parent.Length], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                return key;
            var updated = map(index);
            if (updated == null) return null;
            parts[parent.Length] = updated.Value.ToString(CultureInfo.InvariantCulture);
            return string.Join("/", parts);
        }
    }
}
