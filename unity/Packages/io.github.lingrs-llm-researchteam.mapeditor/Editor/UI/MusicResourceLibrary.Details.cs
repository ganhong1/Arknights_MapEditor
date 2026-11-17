#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine.UIElements;
using Entry = Arknights.MapEditor.Catalog.MusicEventCatalog.Entry;
using Clip = Arknights.MapEditor.Catalog.MusicEventCatalog.Clip;
using Effect = Arknights.MapEditor.Catalog.MusicEventCatalog.Effect;
using AudioCatalog = Arknights.MapEditor.Catalog.AudioLibraryCatalog;

namespace Arknights.MapEditor.Editor
{
    internal sealed partial class MusicResourceLibrary
    {
        private void OpenDetails(Entry entry, Button opener)
        {
            MusicPreview.Stop(detail);
            detailOpener = opener;
            detailTitle.text = DetailTitle(entry);
            detailBody.Clear();
            detailBody.scrollOffset = UnityEngine.Vector2.zero;
            if (entry.IsEffectClip && entry.Label != entry.Key)
                detailBody.Add(Text(entry.Key));

            var notice = Text("", "audio-detail-notice");
            AddDetailPlayer(entry, notice);
            var clips = DetailTracks(entry).ToArray();
            var related = RelatedEvents(entry).ToArray();
            DrawDetailUsage(entry, related);
            DrawDetailTracks(entry, clips);
            AddTechnicalDetails(entry, clips, notice);
            detail.style.display = DisplayStyle.Flex;
            closeDetail.Focus();
        }

        private void AddDetailPlayer(Entry entry, Label notice)
        {
            if (entry.HasPreview)
            {
                Func<Entry?>? previous = !entry.IsSound && !entry.Opening && target?.PreviousMusic != null
                    ? () => targetIsCurrent?.Invoke() == true ? target?.PreviousMusic?.Invoke() : null
                    : null;
                detailBody.Add(new AudioDetailPlayer(entry, detail, previous));
            }
            notice.text = !entry.IsRegistered && entry.HasPreview ? entry.BindingStatus + " · 可试听参考曲" :
                !entry.IsRegistered ? entry.BindingStatus + " · " + entry.PreviewStatus :
                !entry.HasFullPreview ? entry.PreviewStatus : "";
            notice.AddToClassList("audio-detail-notice");
            detailBody.Add(notice);
        }

        private static string DetailTitle(Entry entry)
        {
            if (!entry.IsSound || entry.IsEffectClip || entry.Effects.Count <= 1) return entry.Label;
            var names = entry.Effects.Select(effect => Regex.Replace(effect.Label, @"\s+[0-9]+$", ""))
                .Distinct(StringComparer.Ordinal).ToArray();
            return names.Length == 1 ? names[0] : entry.Key;
        }

        private IEnumerable<Entry> RelatedEvents(Entry entry) =>
            entry.ClipAsset is string asset && original != null
                ? original.EventsForClip(asset).Where(row => row.EventName != entry.EventName)
                    .GroupBy(row => row.EventName, StringComparer.Ordinal).Select(group => group.First())
                : Array.Empty<Entry>();

        private void DrawDetailUsage(Entry entry, IReadOnlyList<Entry> related)
        {
            var panel = DetailSection("用途与调用");
            var purpose = entry.IsSound
                ? string.Join(" · ", new[] { entry }.Concat(related).SelectMany(row => row.Usage)
                    .Distinct(StringComparer.Ordinal).Select(path => path.Replace("/", " › ")))
                : MusicPurpose(entry);
            DetailField(panel, "用途", purpose);
            if (entry.IsSound) DetailField(panel, "触发方式", SoundTrigger(entry), copy: false);
            var field = entry.Address.Group switch
            {
                AudioCatalog.OpeningGroup when !entry.IsSound => "bgmEvent",
                AudioCatalog.TriggerGroup when !entry.IsSound => "波次或分支 · PLAY_BGM.key",
                AudioCatalog.EnvironmentGroup when entry.IsSound => "environmentSe",
                _ => null
            };
            if (field != null) DetailField(panel, "关卡字段", field);
            DetailField(panel, "原版事件", entry.EventName, copy: true);
            if (entry.BankName is string bank && bank != entry.EventName)
                DetailField(panel, "别名指向", bank, copy: true);
            if (entry.ReferenceEvent is string reference)
                DetailField(panel, "参考事件", reference, copy: true);

            if (related.Count == 0) return;
            var events = new Foldout { text = $"相关事件 · {related.Count}", value = false };
            events.name = "audio-detail-related-events";
            foreach (var item in related)
                DetailField(events, SoundTrigger(item), item.EventName, copy: true);
            panel.Add(events);
        }

        private static string MusicPurpose(Entry entry) => entry.Address.Signal switch
        {
            "ON_GAME_READY" when entry.Address.Module == "battle" => "开局曲",
            "ON_CUSTOM_TRIGGER" when entry.Address.Module == "battle" => "局中换曲",
            "ON_GAME_OVER" when entry.Address.Module == "battle" => "战斗结束音乐",
            "ON_BATTLE_FINISH" when entry.Address.Module == "ui" => "结算音乐",
            _ => entry.Address.Module == "sys" ? "系统音乐" : entry.Address.Signal
        };

        private static string SoundTrigger(Entry entry) => entry.Address.Signal switch
        {
            "ON_ENV_SHOW" => "环境出现",
            "ON_ENVSYS_TRIGGER_TILE" => "环境系统触发地块",
            "ON_TILE_TRIGGER" => "地块触发",
            "ON_UNIT_BORN" => "单位出现",
            "ON_UNIT_DEAD" => "单位退场",
            "ON_OPERA_TRIGGER" => "关卡演出触发",
            "ON_ABILITY_START" => "能力开始",
            "ON_ABILITY_END" => "能力结束",
            "ON_SKILL_START" => "技能开始",
            "ON_SKILL_FINISH" => "技能结束",
            _ => entry.Address.Signal
        };

        private void DrawDetailTracks(Entry entry, IReadOnlyList<(string Name, Clip Clip, Effect? Effect)> tracks)
        {
            if (tracks.Count == 0) return;
            var panel = DetailSection("音轨");
            if (entry.IsSound)
            {
                var spaces = tracks.Select(track => track.Effect!.Is2D).Distinct().ToArray();
                var playback = entry.EffectLoop ? "循环" : "单次";
                if (spaces.Length == 1) playback += spaces[0] ? " · 2D" : " · 空间音效";
                DetailField(panel, "播放方式", playback);
                foreach (var track in tracks)
                {
                    var duration = AudioDetailPlayer.ClipTime(track.Clip, precise: true);
                    if (spaces.Length > 1) duration += track.Effect!.Is2D ? " · 2D" : " · 空间音效";
                    DetailField(panel, track.Name, duration);
                }
            }
            else
            {
                foreach (var track in tracks) DetailField(panel, track.Name, AudioDetailPlayer.ClipTime(track.Clip));
            }
        }

        private void AddTechnicalDetails(Entry entry, IReadOnlyList<(string Name, Clip Clip, Effect? Effect)> tracks, Label notice)
        {
            var technical = new Foldout { text = "技术详情", value = false, name = "audio-detail-technical" };
            technical.AddToClassList("audio-detail-section");
            detailBody.Add(technical);
            var loaded = false;
            technical.RegisterValueChangedCallback(change =>
            {
                if (!change.newValue || loaded) return;
                loaded = true;
                if (entry.IsSound)
                    DetailField(technical, "混音组", entry.Mixer ?? "默认混音通道");
                else
                {
                    DetailField(technical, "播放延时", $"{entry.Delay:0.###} 秒");
                    DetailField(technical, "淡入交叠", $"{entry.Crossfade:0.###} 秒");
                }
                var files = MusicPreview.LocalPreviewFiles(entry.IsSound, tracks.Select(track => track.Clip));
                foreach (var track in tracks) DrawTrackSource(technical, track, files, notice, entry.Effects.Count > 1);
            });
        }

        private static IEnumerable<(string Name, Clip Clip, Effect? Effect)> DetailTracks(Entry entry)
        {
            if (entry.IsSound)
            {
                foreach (var effect in entry.Effects)
                    if (!entry.IsEffectClip || string.Equals(effect.Clip.Asset, entry.ClipAsset, StringComparison.OrdinalIgnoreCase))
                        yield return (effect.Label, effect.Clip, effect);
            }
            else
            {
                if (entry.Intro is { } intro) yield return ("前奏", intro, null);
                if (entry.Loop is { } loop) yield return ("循环段", loop, null);
            }
        }

        private void DrawTrackSource(VisualElement panel, (string Name, Clip Clip, Effect? Effect) track,
            IReadOnlyDictionary<string, string> files, Label notice, bool showWeight)
        {
            var block = new VisualElement();
            block.AddToClassList("audio-detail-track");
            block.Add(Text(track.Name));
            DetailField(block, "原始资源", track.Clip.Asset, copy: true);
            if (track.Clip.SampleRate > 0 && track.Clip.Channels > 0)
            {
                var rate = (track.Clip.SampleRate / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
                var channels = track.Clip.Channels switch { 1 => "单声道", 2 => "双声道", _ => $"{track.Clip.Channels} 声道" };
                DetailField(block, "音轨规格", rate + " kHz · " + channels);
            }
            if (showWeight && track.Effect is { } effect) DetailField(block, "原版权重", $"{effect.Weight:0.###}");
            if (track.Clip.Source is string source)
            {
                var origin = source.StartsWith("https://torappu.prts.wiki/", StringComparison.OrdinalIgnoreCase) ? "TOR 音频归档" :
                    source.EndsWith("Android AssetBundle", StringComparison.Ordinal) ? "Android 客户端资源包" : source;
                DetailField(block, "音轨来源", origin);
            }
            DetailField(block, "本地试听文件", files.TryGetValue(track.Clip.Asset, out var path)
                ? PathButton(path, notice)
                : Text("未找到本地试听文件"));
            panel.Add(block);
        }

        private Button PathButton(string path, Label notice)
        {
            var button = Command(path, "audio-detail-local-path", () =>
            {
                if (!File.Exists(path)) throw new IOException("试听文件已移动或删除。");
                EditorUtility.RevealInFinder(path);
            }, notice);
            button.tooltip = "打开所在文件夹\n" + path;
            button.AddToClassList("audio-detail-path");
            return button;
        }

        private VisualElement DetailSection(string title)
        {
            var section = new VisualElement();
            section.AddToClassList("audio-detail-section");
            var heading = Text(title);
            heading.AddToClassList("audio-detail-section-title");
            section.Add(heading);
            detailBody.Add(section);
            return section;
        }

        private void DetailField(VisualElement panel, string label, string value, bool copy = false)
        {
            var text = Text(value);
            var row = DetailField(panel, label, text);
            if (copy)
            {
                var button = Command("复制", "audio-detail-copy", () => EditorGUIUtility.systemCopyBuffer = value);
                button.tooltip = "复制" + label;
                button.AddToClassList("audio-detail-copy");
                row.Add(button);
            }
        }

        private static VisualElement DetailField(VisualElement panel, string label, VisualElement content)
        {
            var row = new VisualElement();
            row.AddToClassList("audio-detail-field");
            var key = Text(label);
            key.AddToClassList("audio-detail-key");
            content.AddToClassList("audio-detail-value");
            row.Add(key);
            row.Add(content);
            panel.Add(row);
            return row;
        }
    }
}
