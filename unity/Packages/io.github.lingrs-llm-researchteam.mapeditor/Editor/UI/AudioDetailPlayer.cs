#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Arknights.MapEditor.Catalog;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Entry = Arknights.MapEditor.Catalog.MusicEventCatalog.Entry;
using Phase = Arknights.MapEditor.Editor.MusicPreview.PlaybackPhase;

namespace Arknights.MapEditor.Editor
{
    internal sealed class AudioDetailPlayer : VisualElement
    {
        private readonly object owner;
        private readonly Func<Entry?>? previous;
        private readonly Label status = new() { name = "music-detail-preview-status" };
        private readonly Label elapsed = new("0:00");
        private readonly Label duration = new("—");
        private readonly Slider progress = new(0, 1) { name = "music-detail-seek", tooltip = "播放进度" };
        private readonly Slider volume = new("音量", 0, 100) { name = "music-detail-volume", value = 100 };
        private readonly Button play;
        private readonly Button stop;
        private Option option;
        private float total;
        private bool scrubbing;

        internal AudioDetailPlayer(Entry entry, object owner, Func<Entry?>? previous)
        {
            this.owner = owner;
            this.previous = previous;
            name = "music-detail-player";
            AddToClassList("resource-audio-player");
            var options = Options(entry, previous != null);
            option = options[0];
            var toolbar = Row();
            play = new Button(TogglePlayback) { name = "music-detail-play" };
            play.AddToClassList("audio-player-primary");
            stop = new Button(() => MusicPreview.Stop(owner)) { text = "停止", name = "music-detail-stop" };
            toolbar.Add(play);
            toolbar.Add(stop);
            if (options.Count > 1)
            {
                var segment = new PopupField<Option>(options, 0, item => item.Label, item => item.Label)
                    { name = "music-detail-segment", tooltip = "试听片段" };
                segment.AddToClassList("audio-player-segment");
                segment.RegisterValueChangedCallback(change => ChooseOption(change.newValue));
                toolbar.Add(segment);
            }
            Add(toolbar);

            var timeline = Row();
            elapsed.AddToClassList("audio-player-time");
            duration.AddToClassList("audio-player-time");
            timeline.Add(elapsed);
            timeline.Add(progress);
            timeline.Add(duration);
            Add(timeline);

            volume.showInputField = false;
            volume.tooltip = "调整本次试听音量";
            volume.RegisterValueChangedCallback(change => MusicPreview.SetVolume(owner, change.newValue / 100));
            Add(volume);
            status.AddToClassList("audio-player-status");
            Add(status);
            ConfigureSeek();
            RegisterCallback<DetachFromPanelEvent>(_ => MusicPreview.Stop(owner));
            schedule.Execute(Refresh).Every(100);
            Refresh();
        }

        private void ChooseOption(Option next)
        {
            var phase = MusicPreview.ReadPlayback(owner).Phase;
            MusicPreview.Stop(owner);
            scrubbing = false;
            total = 0;
            option = next;
            status.text = "";
            if (phase is Phase.Loading or Phase.Playing) Run(Start);
            Refresh();
        }

        private void TogglePlayback()
        {
            Run(() =>
            {
                var phase = MusicPreview.ReadPlayback(owner).Phase;
                if (phase == Phase.Idle) Start();
                else if (phase != Phase.Loading) MusicPreview.TogglePause(owner);
            });
            Refresh();
        }

        private void Start()
        {
            var lead = option.Transition ? previous?.Invoke() : null;
            if (option.Transition && lead?.HasPreview != true)
                throw new InvalidOperationException("当前动作没有可试听的前一首曲目。");
            MusicPreview.Start(owner, option.Entry, option.LoopOnly, lead,
                value => status.text = !option.Entry.IsRegistered && value == "正在试听" ? "正在试听参考曲" : value,
                singlePass: true, volume: volume.value / 100);
        }

        private void ConfigureSeek()
        {
            progress.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0 && progress.enabledInHierarchy) scrubbing = true;
            }, TrickleDown.TrickleDown);
            progress.RegisterCallback<PointerUpEvent>(_ => ScheduleSeek(), TrickleDown.TrickleDown);
            progress.RegisterCallback<PointerCaptureOutEvent>(_ => ScheduleSeek(), TrickleDown.TrickleDown);
            progress.RegisterValueChangedCallback(change =>
            {
                elapsed.text = TimeLabel(change.newValue);
                if (!scrubbing) Run(() => MusicPreview.SeekTo(owner, change.newValue));
            });
        }

        private void ScheduleSeek()
        {
            if (!scrubbing) return;
            schedule.Execute(() =>
            {
                if (!scrubbing) return;
                scrubbing = false;
                Run(() => MusicPreview.SeekTo(owner, progress.value));
            });
        }

        private void Refresh()
        {
            Run(() =>
            {
                var (phase, position, length) = MusicPreview.ReadPlayback(owner);
                var active = phase is Phase.Playing or Phase.Paused;
                play.text = phase == Phase.Playing ? "暂停" : phase == Phase.Paused ? "继续播放" : "播放";
                play.SetEnabled(phase != Phase.Loading && (!active || MusicPreview.CanPause));
                stop.SetEnabled(phase != Phase.Idle);
                if (length > 0) total = length;
                progress.highValue = total > 0 ? total : 1;
                progress.SetEnabled(active && MusicPreview.CanSeek);
                if (!active) scrubbing = false;
                if (!scrubbing) progress.SetValueWithoutNotify(position);
                elapsed.text = TimeLabel(scrubbing ? progress.value : position);
                duration.text = total > 0 ? TimeLabel(total) : "—";
            }, stopOnFailure: true);
        }

        private void Run(Action action, bool stopOnFailure = false)
        {
            try { action(); }
            catch (Exception error)
            {
                if (stopOnFailure) MusicPreview.Stop(owner);
                status.text = PluginLog.FailureMessage("audio.detail-player", error);
            }
        }

        private sealed class Option
        {
            internal Entry Entry { get; }
            internal string Label { get; }
            internal bool LoopOnly { get; }
            internal bool Transition { get; }

            internal Option(Entry entry, string label, bool loopOnly = false, bool transition = false)
            {
                Entry = entry;
                Label = label;
                LoopOnly = loopOnly;
                Transition = transition;
            }
        }

        private static List<Option> Options(Entry entry, bool allowTransition)
        {
            if (entry.IsSound && !entry.IsEffectClip && entry.Effects.Count > 1)
            {
                var variants = new List<Option>();
                foreach (var effect in entry.Effects)
                    if (effect.Clip.File != null)
                        variants.Add(new Option(entry.ForEffect(effect), effect.Label + " · " + ClipTime(effect.Clip, precise: true)));
                if (variants.Count > 0) return variants;
            }
            var result = new List<Option> { new(entry, "完整试听") };
            if (!entry.IsSound && entry.Intro?.File != null && entry.Loop?.File != null)
                result.Add(new Option(entry, "只听循环段", loopOnly: true));
            if (!entry.IsSound && allowTransition)
                result.Add(new Option(entry, "试听换曲", transition: true));
            return result;
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.AddToClassList("audio-player-row");
            return row;
        }

        internal static string ClipTime(MusicEventCatalog.Clip clip, bool precise = false)
        {
            if (clip.SampleRate <= 0 || clip.Frames <= 0) return "时长未收录";
            var seconds = clip.Frames / (double)clip.SampleRate;
            return precise && seconds < 60
                ? seconds.ToString("0.##", CultureInfo.InvariantCulture) + " 秒"
                : TimeLabel((float)seconds);
        }

        private static string TimeLabel(float seconds)
        {
            var rounded = Mathf.FloorToInt(seconds);
            return $"{rounded / 60}:{rounded % 60:00}";
        }
    }
}
