#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Editor
{
    /// <summary>Local inspector drafts retain their original document and target binding.</summary>
    public sealed class InspectorDraftStore
    {
        public const int MaximumBytes = 8 * 1024 * 1024;
        public const int MaximumSlots = 128;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        private sealed class Entry
        {
            internal readonly string Hash, Target, Initial, Raw;
            internal readonly bool RawPending;
            internal Entry(string hash, string target, string initial, string raw, bool rawPending = false)
            { Hash = hash; Target = target; Initial = initial; Raw = raw; RawPending = rawPending; }
        }

        public bool IsDirty => entries.Values.Any(entry => entry.Initial != entry.Raw);
        public bool HasChanges(string slot) => entries.TryGetValue(slot, out var entry) && entry.Initial != entry.Raw;
        internal bool HasPendingRaw(string slot) => entries.TryGetValue(slot, out var entry) && entry.RawPending;

        public bool TryReadTarget(string slot, out string target)
        {
            if (entries.TryGetValue(slot, out var entry)) { target = entry.Target; return true; }
            target = ""; return false;
        }
        public bool TryReadInitial(string slot, out string raw)
        {
            CheckKey(slot, 256);
            if (entries.TryGetValue(slot, out var entry)) { raw = entry.Initial; return true; }
            raw = ""; return false;
        }

        public void Begin(string slot, string documentJson, string target, string initialRaw)
        {
            CheckKey(slot, 256); CheckKey(target, 4096); CheckText(initialRaw);
            if (entries.ContainsKey(slot)) return;
            Replace(slot, new Entry(Digest(documentJson), target, initialRaw, initialRaw));
        }

        public void Capture(string slot, string documentJson, string target, string raw)
        {
            RequireCurrent(slot, documentJson, target);
            Update(slot, raw);
        }

        public void Update(string slot, string raw)
        {
            CheckText(raw);
            var previous = Required(slot);
            Replace(slot, new Entry(previous.Hash, previous.Target, previous.Initial, raw, previous.RawPending));
        }

        internal void SetDraft(string slot, string documentJson, string target, string initial, string raw, bool rawPending = false)
        {
            CheckKey(slot, 256); CheckKey(target, 4096); CheckText(initial); CheckText(raw);
            Replace(slot, new Entry(Digest(documentJson), target, initial, raw, rawPending));
        }

        internal void RenameSlot(string oldSlot, string newSlot)
        {
            CheckKey(newSlot, 256);
            var previous = Required(oldSlot);
            var candidate = new Dictionary<string, Entry>(entries, StringComparer.Ordinal);
            candidate.Remove(oldSlot);
            candidate.Add(newSlot, previous);
            Serialize(candidate);
            entries = candidate;
        }

        internal IEnumerable<string> DirtySlots(string prefix) => entries
            .Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal) && entry.Value.Initial != entry.Value.Raw)
            .Select(entry => entry.Key).ToArray();

        public bool TryRead(string slot, out string raw)
        {
            CheckKey(slot, 256);
            if (entries.TryGetValue(slot, out var entry)) { raw = entry.Raw; return true; }
            raw = ""; return false;
        }

        internal bool IsCurrent(string slot, string documentJson, string target) =>
            entries.TryGetValue(slot, out var entry) && Matches(entry, documentJson, target);

        private static bool Matches(Entry entry, string documentJson, string target) =>
            string.Equals(entry.Target, target, StringComparison.Ordinal) && entry.Hash == Digest(documentJson);

        public void RequireCurrent(string slot, string documentJson, string target)
        {
            var entry = Required(slot);
            if (!Matches(entry, documentJson, target))
                throw new InvalidOperationException("Local draft belongs to another document revision or target. Preserve it before explicitly reloading this field.");
        }

        public void Remove(string slot) { CheckKey(slot, 256); entries.Remove(slot); }
        public void Clear() => entries.Clear();
        public string ToJson() => Serialize(entries);

        public static InspectorDraftStore Parse(string json)
        {
            CheckText(json);
            // Reuse the existing bounded strict JSON codec, including duplicate-key,
            // Unicode and extension rejection. The draft itself is only a string.
            var token = LevelDocument.Create(1, 1).WithSection("draftStore", json).GetSection("draftStore");
            if (!(token is JObject root) || root.Count != 2 || root["version"]?.Type != JTokenType.Integer || root["version"]!.ToString() != "1" || !(root["slots"] is JArray slots))
                throw new FormatException("Invalid inspector draft envelope or unsupported version.");
            if (slots.Count > MaximumSlots) throw new FormatException("Too many inspector draft slots.");
            var store = new InspectorDraftStore();
            foreach (var value in slots)
            {
                if (!(value is JObject item) || (item.Count != 5 &&
                    (item.Count != 6 || item["rawPending"]?.Type != JTokenType.Boolean)))
                    throw new FormatException("Invalid inspector draft entry.");
                string Field(string key) => item[key]?.Type == JTokenType.String ? item[key]!.Value<string>()! : throw new FormatException("Missing draft string: " + key);
                var slot = Field("slot"); var hash = Field("documentHash"); var target = Field("target");
                var initial = Field("initialRaw"); var raw = Field("raw");
                CheckKey(slot, 256); CheckKey(target, 4096); CheckText(initial); CheckText(raw);
                if (hash.Length != 64 || hash.Any(c => !(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')))
                    throw new FormatException("Invalid document digest.");
                if (store.entries.ContainsKey(slot)) throw new FormatException("Duplicate inspector draft slot.");
                store.entries.Add(slot, new Entry(hash, target, initial, raw, item.Value<bool?>("rawPending") ?? false));
            }
            store.ToJson();
            return store;
        }

        private Entry Required(string slot)
        {
            CheckKey(slot, 256);
            return entries.TryGetValue(slot, out var entry) ? entry : throw new InvalidOperationException("Begin the local draft before updating or applying it.");
        }

        private void Replace(string slot, Entry entry)
        {
            var candidate = new Dictionary<string, Entry>(entries, StringComparer.Ordinal) { [slot] = entry };
            if (candidate.Count > MaximumSlots) throw new FormatException("Too many inspector draft slots.");
            Serialize(candidate); // Validate aggregate budget before changing live state.
            entries = candidate;
        }

        private static string Serialize(Dictionary<string, Entry> source)
        {
            var slots = new JArray();
            foreach (var pair in source.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var slot = new JObject { ["slot"] = pair.Key, ["documentHash"] = pair.Value.Hash,
                    ["target"] = pair.Value.Target, ["initialRaw"] = pair.Value.Initial, ["raw"] = pair.Value.Raw };
                if (pair.Value.RawPending) slot["rawPending"] = true;
                slots.Add(slot);
            }
            var json = new JObject { ["version"] = 1, ["slots"] = slots }.ToString(Formatting.None);
            CheckText(json); return json;
        }

        private static void CheckKey(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maximum) throw new FormatException("Invalid inspector draft key or target.");
            CheckText(value);
        }

        private static void CheckText(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value.Length > MaximumBytes || Utf8.GetByteCount(value) > MaximumBytes) throw new FormatException("Inspector drafts exceed the 8 MiB limit.");
        }

        private static string Digest(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value.Length > LevelDocument.MaximumUtf8Bytes || Utf8.GetByteCount(value) > LevelDocument.MaximumUtf8Bytes) throw new FormatException("Document exceeds the size limit.");
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Utf8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
    }
}
