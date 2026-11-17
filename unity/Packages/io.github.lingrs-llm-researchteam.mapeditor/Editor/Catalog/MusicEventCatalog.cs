#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Catalog
{
    internal sealed class MusicEventCatalog
    {
        internal const string GameVersion = "2.7.81";
        private const string AudioTableSha256 = "5e96594c42e740c4acbe6c6da928878d206dd34e14ea81083d25dcdb5adccfc8";
        private static MusicEventCatalog? bundled;
        private readonly Dictionary<(bool Opening, string Key), Entry> entries;
        private readonly Entry[] opening;
        private readonly Entry[] trigger;

        internal sealed class Clip
        {
            internal string Asset { get; }
            internal string? File { get; }
            internal string? Sha256 { get; }
            internal int SampleRate { get; }
            internal int Frames { get; }
            internal int Channels { get; }
            internal string? Source { get; }

            internal Clip(JObject row)
            {
                Asset = (string?)row["asset"] ?? throw new FormatException("音乐索引缺少音轨路径。");
                File = (string?)row["file"];
                Sha256 = (string?)row["sha256"];
                SampleRate = (int?)row["sampleRate"] ?? 0;
                Frames = (int?)row["frames"] ?? 0;
                Channels = (int?)row["channels"] ?? 0;
                Source = (string?)row["source"];
                if (File != null && (Sha256?.Length != 64 || SampleRate <= 0 || Frames <= 0 || Channels is < 1 or > 8))
                    throw new FormatException("音乐索引包含无效的音轨信息。");
            }
        }

        internal sealed class Effect
        {
            internal Clip Clip { get; }
            internal string Label { get; }
            internal float Weight { get; }
            internal bool Is2D { get; }

            internal Effect(JObject row)
            {
                Clip = new Clip(row["clip"] as JObject ?? throw new FormatException("音效缺少音轨。"));
                Label = (string?)row["label"] ?? Clip.Asset;
                Weight = (float?)row["weight"] ?? 1;
                Is2D = (bool?)row["is2D"] ?? false;
            }
        }

        internal sealed class Entry
        {
            internal bool Opening { get; }
            internal bool IsSound { get; }
            internal AudioEventAddress Address { get; }
            internal IReadOnlyList<Effect> Effects { get; } = Array.Empty<Effect>();
            internal IReadOnlyList<string> Usage { get; } = Array.Empty<string>();
            internal string Terms { get; } = "";
            internal string? Mixer { get; }
            internal bool EffectLoop { get; }
            internal bool IsEffectClip { get; }
            internal string? ClipAsset => IsEffectClip ? (Intro ?? Loop)?.Asset : null;
            internal string DisplayId => IsEffectClip ? EventName + "|" + ClipAsset : EventName;
            internal string Key { get; }
            internal string Label { get; }
            internal string EventName { get; }
            internal string? BankName { get; }
            internal string? ReferenceEvent { get; }
            internal string? SuggestedKey { get; }
            internal float Crossfade { get; }
            internal float Delay { get; }
            internal Clip? Intro { get; }
            internal Clip? Loop { get; }
            internal bool IsRegistered => BankName != null;
            internal bool HasPreview => Intro?.File != null || Loop?.File != null;
            internal bool HasFullPreview => HasPreview && (Intro == null || Intro.File != null) &&
                (Loop == null || Loop.File != null) && (IsEffectClip || Effects.All(effect => effect.Clip.File != null));
            internal string BindingStatus => IsRegistered ? "已登记" : Opening ? "开局事件未登记" : "局中事件未登记";
            internal string PreviewStatus => HasFullPreview ? "有试听音轨" : HasPreview ? "有部分试听音轨" : "缺试听音轨";

            internal Entry(JObject row)
            {
                Opening = (bool?)row["opening"] ?? throw new FormatException("音乐索引缺少事件场合。");
                Key = (string?)row["key"] ?? throw new FormatException("音乐索引缺少事件键。");
                Label = (string?)row["label"] ?? Key;
                EventName = MusicEventCatalog.EventName(Opening, Key);
                Address = new AudioEventAddress(EventName);
                BankName = (string?)row["bank"];
                ReferenceEvent = (string?)row["reference"];
                SuggestedKey = (string?)row["suggestedKey"];
                Crossfade = (float?)row["crossfade"] ?? 0;
                Delay = (float?)row["delay"] ?? 0;
                Intro = row["intro"] is JObject intro ? new Clip(intro) : null;
                Loop = row["loop"] is JObject loop ? new Clip(loop) : null;
                if (Key.Length == 0 || (string?)row["event"] != EventName ||
                    (BankName == null && ReferenceEvent == null))
                    throw new FormatException("音乐索引包含无效的事件。");
            }

            internal Entry(JObject row, bool effects)
            {
                EventName = (string?)row["event"] ?? throw new FormatException("音频目录缺少事件名。");
                BankName = (string?)row["bank"] ?? throw new FormatException("音频目录缺少播放记录。");
                Address = new AudioEventAddress(EventName);
                Opening = Address.Group == AudioLibraryCatalog.OpeningGroup;
                Key = Address.SubSignal.Length == 0 ? Address.Signal : Address.SubSignal;
                Label = (string?)row["label"] ?? Key;
                Terms = (string?)row["terms"] ?? "";
                IsSound = effects;
                Crossfade = (float?)row["crossfade"] ?? 0;
                Delay = (float?)row["delay"] ?? 0;
                Mixer = (string?)row["mixer"];
                EffectLoop = (bool?)row["effectLoop"] ?? false;
                Usage = row["usage"]?.Values<string>().Where(item => item != null).Cast<string>().ToArray() ??
                    Array.Empty<string>();
                Effects = row["effects"]?.OfType<JObject>().Select(item => new Effect(item)).ToArray() ??
                    Array.Empty<Effect>();
                if (IsSound)
                {
                    if (Effects.Count == 0 || Usage.Count == 0) throw new FormatException("音效缺少音轨或用途。");
                    var clip = Effects.FirstOrDefault(item => item.Clip.File != null)?.Clip ?? Effects[0].Clip;
                    Intro = EffectLoop ? null : clip;
                    Loop = EffectLoop ? clip : null;
                }
                else
                {
                    Intro = row["intro"] is JObject intro ? new Clip(intro) : null;
                    Loop = row["loop"] is JObject loop ? new Clip(loop) : null;
                }
            }

            private Entry(Entry parent, Effect effect)
            {
                EventName = parent.EventName;
                BankName = parent.BankName;
                Address = parent.Address;
                Key = System.IO.Path.GetFileName(effect.Clip.Asset);
                Label = effect.Label;
                IsSound = true;
                IsEffectClip = true;
                Effects = parent.Effects;
                Usage = parent.Usage;
                EffectLoop = parent.EffectLoop;
                Mixer = parent.Mixer;
                Intro = EffectLoop ? null : effect.Clip;
                Loop = EffectLoop ? effect.Clip : null;
            }

            internal Entry ForEffect(Effect effect) => new(this, effect);
        }

        private MusicEventCatalog(JObject root)
        {
            if ((int?)root["version"] != 1 || (string?)root["gameVersion"] != GameVersion ||
                (string?)root["audioTableSha256"] != AudioTableSha256 || root["entries"] is not JArray rows)
                throw new FormatException("音乐事件索引版本不匹配。");
            entries = new Dictionary<(bool, string), Entry>();
            foreach (var row in rows)
            {
                var entry = new Entry(row as JObject ?? throw new FormatException("音乐事件索引条目无效。"));
                if (entries.ContainsKey((entry.Opening, entry.Key)))
                    throw new FormatException("音乐事件索引存在重复条目。");
                entries.Add((entry.Opening, entry.Key), entry);
            }
            opening = Registered(true);
            trigger = Registered(false);
        }

        internal Entry? Find(bool isOpening, string key) => entries.TryGetValue((isOpening, key), out var result) ? result : null;
        internal IReadOnlyList<Entry> Choices(bool isOpening) => isOpening ? opening : trigger;

        internal static string EventName(bool opening, string key) =>
            (opening ? "battle.ON_GAME_READY." : "battle.ON_CUSTOM_TRIGGER.") + key;

        private Entry[] Registered(bool isOpening) => entries.Values.Where(entry => entry.IsRegistered && entry.Opening == isOpening)
            .OrderBy(entry => entry.Label, StringComparer.Ordinal).ThenBy(entry => entry.Key, StringComparer.Ordinal).ToArray();

        internal static MusicEventCatalog Parse(string json)
        {
            if (json.Length > 2 * 1024 * 1024) throw new FormatException("音乐事件索引过大。");
            using var input = new StringReader(json);
            using var reader = new JsonTextReader(input) { DateParseHandling = DateParseHandling.None, MaxDepth = 12 };
            return new MusicEventCatalog(JObject.Load(reader));
        }

        internal static MusicEventCatalog LoadBundled()
        {
            if (bundled != null) return bundled;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Editor.PrtsObjectView.CatalogRoot + "music-events.json");
            if (asset == null) throw new InvalidOperationException("缺少音乐事件索引。");
            return bundled = Parse(asset.text);
        }
    }
}
