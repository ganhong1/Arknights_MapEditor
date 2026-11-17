#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Catalog
{
    internal sealed class AudioLibraryCatalog
    {
        internal const string OpeningGroup = "battle.ON_GAME_READY";
        internal const string TriggerGroup = "battle.ON_CUSTOM_TRIGGER";
        internal const string EnvironmentGroup = "battle.ON_ENV_SHOW";
        private const string TableSha256 = "45268b51d57b691fa88bbfb6e9d0c52e09f8f060787a44b59afe94a871954a2f";
        private static AudioLibraryCatalog? bundled;
        private readonly Dictionary<string, MusicEventCatalog.Entry> events;
        private readonly ILookup<string, MusicEventCatalog.Entry> effectsByClip;

        internal IReadOnlyList<MusicEventCatalog.Entry> Music { get; }
        internal IReadOnlyList<MusicEventCatalog.Entry> Effects { get; }
        internal IReadOnlyList<MusicEventCatalog.Entry> EffectClips { get; }

        private AudioLibraryCatalog(JObject root)
        {
            if ((int?)root["version"] != 1 || (string?)root["gameVersion"] != MusicEventCatalog.GameVersion ||
                (string?)root["resourceVersion"] != "26-10-22-14-02-11-51447808" ||
                (string?)root["tableSha256"] != TableSha256)
                throw new FormatException("原版音频目录版本不匹配。");
            Music = Read(root, "music", false);
            Effects = Read(root, "effects", true);
            events = Music.Concat(Effects).ToDictionary(entry => entry.EventName, StringComparer.Ordinal);
            EffectClips = Effects.SelectMany(entry => entry.Effects.Select(effect => entry.ForEffect(effect))).ToArray();
            effectsByClip = EffectClips.ToLookup(entry => entry.ClipAsset!, StringComparer.OrdinalIgnoreCase);
        }

        private static MusicEventCatalog.Entry[] Read(JObject root, string key, bool effects)
        {
            if (root[key] is not JArray rows) throw new FormatException("音频目录缺少 " + key);
            return rows.Select(row => new MusicEventCatalog.Entry(
                row as JObject ?? throw new FormatException("音频目录包含无效条目。"), effects)).ToArray();
        }

        internal static AudioLibraryCatalog LoadBundled()
        {
            if (bundled != null) return bundled;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Editor.PrtsObjectView.CatalogRoot + "audio-browser-2781.json");
            if (asset == null) throw new InvalidOperationException("缺少原版音频目录。");
            if (asset.bytes.Length > 2 * 1024 * 1024) throw new FormatException("原版音频目录过大。");
            return bundled = new AudioLibraryCatalog(JObject.Parse(asset.text));
        }

        internal MusicEventCatalog.Entry? Find(string group, string key)
        {
            if (key.Length == 0) return null;
            return events.TryGetValue(group + "." + key, out var result) ? result : null;
        }

        internal IEnumerable<MusicEventCatalog.Entry> EventsForClip(string asset) => effectsByClip[asset];

        internal CatalogSearch<MusicEventCatalog.Entry> CreateSearch() =>
            CatalogSearchRuntime.CreateWorker<MusicEventCatalog.Entry>(DescribeForSearch);

        private JObject DescribeForSearch(MusicEventCatalog.Entry entry)
        {
            var metadata = events.TryGetValue(entry.EventName, out var original) ? original : entry;
            var related = entry.ClipAsset is string asset
                ? EventsForClip(asset)
                : new[] { entry };
            var effects = entry.IsSound && !entry.IsEffectClip ? entry.Effects : Array.Empty<MusicEventCatalog.Effect>();
            var tracks = new[] { entry.Intro?.Asset, entry.Loop?.Asset }.Concat(effects.Select(effect => effect.Clip.Asset));
            var category = entry.IsSound ? "音效" :
                entry.Address.Group == OpeningGroup ? "开局曲" :
                entry.Address.Group == TriggerGroup ? "局中换曲" : "音乐";
            return new JObject
            {
                ["name"] = SearchTerms(new[] { entry.Label, entry.IsSound ? null : metadata.Label }
                    .Concat(effects.Select(effect => effect.Label))),
                ["targetFile"] = SearchTerms(new[]
                    {
                        entry.Key, entry.EventName, entry.BankName, entry.ReferenceEvent, entry.SuggestedKey
                    }.Concat(related.Select(item => item.EventName))
                    .Concat(tracks).Concat(tracks.Select(System.IO.Path.GetFileName))
                    .Concat(metadata.Terms.Split(' ').Where(term => term.StartsWith("music_", StringComparison.Ordinal)))),
                ["category"] = SearchTerms(new[] { category, entry.Address.Module, entry.Address.Signal }
                    .Concat(related.SelectMany(item => item.Usage).SelectMany(usage => usage.Split('/')))),
                ["description"] = metadata.Terms
            };
        }

        private static JArray SearchTerms(IEnumerable<string?> values) =>
            new(values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    internal readonly struct AudioEventAddress
    {
        internal string Module { get; }
        internal string Signal { get; }
        internal string SubSignal { get; }
        internal string Group => Module + "." + Signal;

        internal AudioEventAddress(string name)
        {
            var parts = name.Split(new[] { '.' }, 3);
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0)
                throw new FormatException("音频事件名缺少模块或信号：" + name);
            Module = parts[0];
            Signal = parts[1];
            SubSignal = parts.Length > 2 ? parts[2] : "";
        }
    }
}
