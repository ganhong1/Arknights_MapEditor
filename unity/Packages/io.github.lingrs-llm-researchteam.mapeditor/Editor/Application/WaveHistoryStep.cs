#nullable enable
using System;
using System.Linq;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Arknights.MapEditor.Editor
{
    internal enum WaveOrder { Fields, Append, Copy, Move, Delete }

    [Serializable]
    internal sealed class WaveHistoryStep
    {
        [SerializeField] private string before = "";
        [SerializeField] private string after = "";
        [SerializeField] private string token = Guid.NewGuid().ToString("N");
        [SerializeField] private int[] parent;
        [SerializeField] private int index;
        [SerializeField] private int target;
        [SerializeField] private WaveOrder order;

        internal WaveHistoryStep(WaveOrder order, int[] parent, int index, int target = -1)
        {
            this.order = order;
            this.parent = parent;
            this.index = index;
            this.target = target;
        }

        internal bool Connects(string from, string to) =>
            (before == from && after == to) || (after == from && before == to);
        internal bool IsForward(string from) => before == from;
        internal string Token(bool forward) => token + (forward ? ":forward:" : ":back:");
        internal bool MovesIndices => order == WaveOrder.Move;
        internal void Bind(string from, string to) { before = from; after = to; }

        internal string? Remap(string key, bool forward) =>
            WaveInspectorDrafts.Remap(key, parent, index => RemapIndex(index, forward));

        internal (string Initial, string Raw)? RebaseFields(string key, string initial, string raw, JToken? waves)
        {
            if (order != WaveOrder.Fields || index < 0) return (initial, raw);
            var address = parent.Append(index).ToArray();
            if (key != WaveSchedule.Key(address)) return (initial, raw);
            if (WaveSchedule.Read(waves, address) is not JObject record) return null;
            try
            {
                var source = LevelDocument.ParseEnemyDefinition(initial);
                var value = LevelDocument.ParseEnemyDefinition(raw);
                var baseline = WaveSchedule.Fields(record, address.Length);
                var moved = (JObject)baseline.DeepClone();
                foreach (var field in source.Properties().Select(item => item.Name)
                    .Union(value.Properties().Select(item => item.Name), StringComparer.Ordinal))
                {
                    if (JToken.DeepEquals(source[field], value[field])) continue;
                    if (value[field] is JToken edited) moved[field] = edited.DeepClone();
                    else moved.Remove(field);
                }
                return (baseline.ToString(Formatting.None), moved.ToString(Formatting.None));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private int? RemapIndex(int position, bool forward)
        {
            switch (order)
            {
                case WaveOrder.Append: return !forward && position == index ? null : position;
                case WaveOrder.Copy: return forward ? (position >= target ? position + 1 : position) :
                    position == target ? null : position > target ? position - 1 : position;
                case WaveOrder.Delete: return forward ? (position == index ? null : position > index ? position - 1 : position) :
                    position >= index ? position + 1 : position;
                case WaveOrder.Move: return position == index ? target : position == target ? index : position;
                default: return position;
            }
        }
    }
}
