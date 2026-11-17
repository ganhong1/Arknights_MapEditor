#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using Arknights.MapEditor.Catalog;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Arknights.MapEditor.Editor
{
    [InitializeOnLoad]
    internal static class MusicPreview
    {
        private const int Channels = 2;
        private const int MaxClipSeconds = 300;
        private const string RootPreference = "ArknightsMapEditor.MusicPreview.AudioRoot";
        private const string EffectsRootPreference = RootPreference + ".Effects";
        private const string ManifestFile = "manifest.json";
        private static readonly Type? AudioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        private static readonly MethodInfo? PlayClip = AudioUtil?.GetMethod("PlayPreviewClip",
            BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
        private static readonly MethodInfo? StopClips = AudioUtil?.GetMethod("StopAllPreviewClips", BindingFlags.Public | BindingFlags.Static);
        private static readonly MethodInfo? IsPlaying = AudioUtil?.GetMethod("IsPreviewClipPlaying", BindingFlags.Public | BindingFlags.Static);
        private static readonly MethodInfo? PauseClip = AudioUtil?.GetMethod("PausePreviewClip", BindingFlags.Public | BindingFlags.Static);
        private static readonly MethodInfo? ResumeClip = AudioUtil?.GetMethod("ResumePreviewClip", BindingFlags.Public | BindingFlags.Static);
        private static readonly MethodInfo? GetSample = AudioUtil?.GetMethod("GetPreviewClipSamplePosition", BindingFlags.Public | BindingFlags.Static);
        private static readonly MethodInfo? SetSample = AudioUtil?.GetMethod("SetPreviewClipSamplePosition", BindingFlags.Public | BindingFlags.Static);
        private static PreviewRequest? request;
        private static AudioClip? playing;
        private static object? playingOwner;
        private static PreviewPcm? playingAudio;
        private static bool paused;
        private static int pausedFrame;
        private static Action<string>? playbackStatus;
        private static double monitorAfter;

        static MusicPreview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) Stop();
            };
        }

        internal static string? AudioRoot(bool effects = false)
        {
            var local = EditorPrefs.GetString(effects ? EffectsRootPreference : RootPreference, "");
            var configured = Environment.GetEnvironmentVariable(effects ? "ARKNIGHTS_MAP_EDITOR_EFFECTS_ROOT" : "ARKNIGHTS_MAP_EDITOR_AUDIO_ROOT");
            var projects = Directory.GetParent(Application.dataPath)?.Parent;
            var localRoot = projects?.Name == "UnityProjects" && projects.Parent?.Name == ".local"
                ? Path.Combine(projects.Parent.FullName, "Resources", "Audio", effects ? "Effects-2.7.81" : "Music-2.7.81")
                : null;
            var workspace = projects?.FullName;
            var sibling = workspace == null ? null : effects
                ? Path.Combine(workspace, "audio-library-20261117", "ogg-effects-2.7.81")
                : Path.Combine(workspace, "music-editor-20261116", "ogg-2.7.81");
            foreach (var root in new[] { local, configured, localRoot, sibling })
                if (!string.IsNullOrWhiteSpace(root) && File.Exists(Path.Combine(root, ManifestFile))) return Path.GetFullPath(root);
            return null;
        }

        internal static bool SetAudioRoot(string root, bool effects = false)
        {
            if (!File.Exists(Path.Combine(root, ManifestFile))) return false;
            var clips = ReadManifest(root);
            var prefix = effects ? "audio/sound_beta_2/ambience/" : "audio/sound_beta_2/music/";
            if (!clips.Properties().Any(item => item.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                throw new FormatException(effects ? "此试听包没有音效。" : "此试听包没有音乐。");
            Stop();
            EditorPrefs.SetString(effects ? EffectsRootPreference : RootPreference, Path.GetFullPath(root));
            return true;
        }

        internal static void Start(object owner, MusicEventCatalog.Entry entry, bool loopOnly,
            MusicEventCatalog.Entry? previous, Action<string> status, bool singlePass = false, float volume = 1)
        {
            Stop();
            if (PlayClip == null || StopClips == null || IsPlaying == null)
                throw new NotSupportedException("当前 Unity 版本缺少编辑模式试听接口。");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请在编辑模式中试听。");
            var root = AudioRoot(entry.IsSound) ?? throw new InvalidOperationException("请先选择包含 manifest.json 的 Ogg Vorbis 试听包。");
            request = new PreviewRequest(owner, status, entry, root, ReadManifest(root), loopOnly, previous, singlePass, volume);
            status("正在读取试听音轨…");
            EditorApplication.update += Poll;
        }

        internal enum PlaybackPhase { Idle, Loading, Playing, Paused }

        internal static (PlaybackPhase Phase, float Seconds, float Duration) ReadPlayback(object owner)
        {
            if (ReferenceEquals(request?.Owner, owner)) return (PlaybackPhase.Loading, 0, 0);
            if (!ReferenceEquals(playingOwner, owner) || playing == null) return (PlaybackPhase.Idle, 0, 0);
            var frame = paused ? pausedFrame : GetSample?.Invoke(null, null) is int current ? current : 0;
            var position = Mathf.Clamp(frame, 0, playing.samples) / (float)playing.frequency;
            return (paused ? PlaybackPhase.Paused : PlaybackPhase.Playing, position, playing.length);
        }

        internal static bool CanPause => PauseClip != null && ResumeClip != null;
        internal static bool CanSeek => GetSample != null && SetSample != null;

        internal static void TogglePause(object owner)
        {
            if (!ReferenceEquals(playingOwner, owner) || playing == null || !CanPause) return;
            if (paused)
            {
                ResumeClip!.Invoke(null, null);
                paused = false;
                monitorAfter = EditorApplication.timeSinceStartup + 0.35;
            }
            else
            {
                pausedFrame = GetSample?.Invoke(null, null) is int frame ? frame : 0;
                PauseClip!.Invoke(null, null);
                paused = true;
            }
            playbackStatus?.Invoke(paused ? "已暂停" : "正在试听");
        }

        internal static void SeekTo(object owner, float seconds)
        {
            if (!ReferenceEquals(playingOwner, owner) || playing == null || !CanSeek) return;
            var frame = Mathf.Clamp(Mathf.RoundToInt(seconds * playing.frequency), 0, playing.samples - 1);
            SetSample!.Invoke(null, new object[] { playing, frame });
            if (paused) pausedFrame = frame;
            else monitorAfter = EditorApplication.timeSinceStartup + 0.35;
        }

        internal static void SetVolume(object owner, float volume)
        {
            if (ReferenceEquals(playingOwner, owner) && playingAudio != null) playingAudio.Volume = volume;
            if (request is { } pending && ReferenceEquals(pending.Owner, owner)) pending.Volume = volume;
        }

        internal static void Stop(object owner)
        {
            if (ReferenceEquals(request?.Owner, owner) || ReferenceEquals(playingOwner, owner)) Stop();
        }

        internal static void Stop()
        {
            EditorApplication.update -= Poll;
            if (request != null)
            {
                var pending = request;
                request = null;
                pending.Dispose();
                pending.Status("试听已停止");
            }
            if (playing != null)
            {
                try { StopClips?.Invoke(null, null); }
                catch (Exception error) { PluginLog.Failure("audio.preview.stop", error); }
                finally { ReleaseClip("试听已停止"); }
            }
        }

        private static void ReleaseClip(string message)
        {
            if (playing != null) UnityEngine.Object.DestroyImmediate(playing);
            playing = null;
            playingOwner = null;
            playingAudio = null;
            paused = false;
            pausedFrame = 0;
            var status = playbackStatus;
            playbackStatus = null;
            status?.Invoke(message);
        }

        private static void Poll()
        {
            if (request == null)
            {
                try
                {
                    if (playing != null && !paused && EditorApplication.timeSinceStartup >= monitorAfter &&
                        IsPlaying!.Invoke(null, null) is false)
                    {
                        EditorApplication.update -= Poll;
                        ReleaseClip("试听结束");
                    }
                }
                catch (Exception error)
                {
                    EditorApplication.update -= Poll;
                    ReleaseClip(PluginLog.FailureMessage("audio.preview", error));
                }
                return;
            }
            if (!request.IsDone) return;
            var ready = request;
            request = null;
            try
            {
                var (audio, rate) = ready.Read();
                playing = AudioClip.Create("Arknights music preview", audio.SampleCount / Channels, Channels,
                    rate, true, audio.Read, audio.Seek);
                playing.hideFlags = HideFlags.HideAndDontSave;
                playingOwner = ready.Owner;
                playingAudio = audio;
                playbackStatus = ready.Status;
                PlayClip!.Invoke(null, new object[] { playing, 0, audio.NativeLoop });
                monitorAfter = EditorApplication.timeSinceStartup + 0.35;
                ready.Status(audio.NativeLoop ? "正在试听循环段" : "正在试听");
            }
            catch (Exception error)
            {
                EditorApplication.update -= Poll;
                var message = PluginLog.FailureMessage("audio.preview", error);
                if (playing != null) ReleaseClip(message);
                else ready.Status(message);
            }
            finally { ready.Dispose(); }
        }

        private static JObject ReadManifest(string root)
        {
            var path = Path.Combine(root, ManifestFile);
            if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new FormatException("试听资源包清单过大。");
            var manifest = JObject.Parse(File.ReadAllText(path));
            if ((string?)manifest["format"] != "Ogg Vorbis" || (string?)manifest["resourceBaseline"] != MusicEventCatalog.GameVersion ||
                manifest["clips"] is not JObject clips || (int?)manifest["clipCount"] != clips.Count)
                throw new FormatException("试听资源包与 2.7.81 音乐索引不匹配。");
            return clips;
        }

        internal static IReadOnlyDictionary<string, string> LocalPreviewFiles(bool effects, IEnumerable<MusicEventCatalog.Clip> clips)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var root = AudioRoot(effects);
                if (root == null) return result;
                var manifest = ReadManifest(root);
                foreach (var clip in clips)
                {
                    if (clip.File == null) continue;
                    try { result[clip.Asset] = ResolvePackClip(root, manifest, clip); }
                    catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException)
                    {
                        PluginLog.Failure("audio.pack.path", error);
                    }
                }
            }
            catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException or
                FormatException or Newtonsoft.Json.JsonException)
            {
                PluginLog.Failure("audio.pack.manifest", error);
            }
            return result;
        }

        private static string ResolvePackClip(string root, JObject manifest, MusicEventCatalog.Clip clip)
        {
            var record = manifest[clip.Asset.ToLowerInvariant()] as JObject;
            if (record == null)
                throw new IOException("试听包缺少音轨 " + clip.Asset);
            var sourceHash = (string?)record["sourceWavSha256"] ?? (string?)record["sourceFlacSha256"];
            if (sourceHash != clip.Sha256 || (int?)record["sampleRate"] != clip.SampleRate ||
                (int?)record["channels"] != clip.Channels || clip.Channels is < 1 or > Channels)
                throw new IOException("试听音轨与曲目索引的校验信息不一致。");
            var file = (string?)record["file"] ?? "";
            var path = ResolveLocalClip(root, file);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != (long?)record["bytes"] || info.Length > 64L * 1024 * 1024)
                throw new IOException("试听音轨文件缺失或大小与清单不一致。");
            var expectedHash = (string?)record["sha256"];
            if (expectedHash != null)
            {
                using var stream = info.OpenRead();
                using var sha = SHA256.Create();
                var actualHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("试听音轨文件与清单的 SHA-256 不一致。");
            }
            return path;
        }

        private sealed class PreviewRequest : IDisposable
        {
            private readonly TrackLoad?[] tracks = new TrackLoad?[3];
            private readonly float crossfade;
            private readonly bool singlePass;
            internal object Owner { get; }
            internal Action<string> Status { get; }
            internal float Volume { get; set; }
            internal bool IsDone => tracks.All(track => track == null || track.IsDone);

            internal PreviewRequest(object owner, Action<string> status, MusicEventCatalog.Entry entry, string root,
                JObject manifest, bool loopOnly, MusicEventCatalog.Entry? previous, bool singlePass, float volume)
            {
                Owner = owner;
                Status = status;
                this.singlePass = singlePass;
                Volume = volume;
                crossfade = Math.Min(3f, Math.Max(0f, entry.Crossfade));
                try
                {
                    var lead = previous?.Loop?.File != null ? previous.Loop : previous?.Intro;
                    tracks[0] = TrackLoad.Open(root, manifest, lead, limitSeconds: 3);
                    if (!loopOnly) tracks[1] = TrackLoad.Open(root, manifest, entry.Intro);
                    tracks[2] = TrackLoad.Open(root, manifest, entry.Loop);
                    if (tracks[1] == null && tracks[2] == null)
                        throw new IOException("所选曲目没有可试听的音轨。");
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            internal (PreviewPcm Audio, int Rate) Read()
            {
                var rate = tracks[2]?.Rate ?? tracks[1]!.Rate;
                var lead = tracks[0]?.Read(rate) ?? Array.Empty<float>();
                var intro = tracks[1]?.Read(rate) ?? Array.Empty<float>();
                var loop = tracks[2]?.Read(rate) ?? Array.Empty<float>();
                return (new PreviewPcm(lead, intro, loop, (int)(crossfade * rate), singlePass) { Volume = Volume }, rate);
            }

            public void Dispose()
            {
                foreach (var track in tracks) track?.Dispose();
            }
        }

        private sealed class TrackLoad : IDisposable
        {
            private readonly UnityWebRequest request;
            private readonly int limitSeconds;
            private readonly int sourceChannels;
            internal int Rate { get; }
            internal bool IsDone => request.isDone;

            private TrackLoad(string path, int rate, int channels, int limitSeconds)
            {
                Rate = rate;
                sourceChannels = channels;
                this.limitSeconds = limitSeconds;
                request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.OGGVORBIS);
                try
                {
                    var handler = (DownloadHandlerAudioClip)request.downloadHandler;
                    handler.streamAudio = false;
                    handler.compressed = false;
                    request.timeout = 25;
                    request.SendWebRequest();
                }
                catch { request.Dispose(); throw; }
            }

            internal static TrackLoad? Open(string root, JObject manifest, MusicEventCatalog.Clip? clip, int limitSeconds = MaxClipSeconds)
            {
                if (clip?.File == null) return null;
                var path = ResolvePackClip(root, manifest, clip);
                return new TrackLoad(path, clip.SampleRate, clip.Channels, limitSeconds);
            }

            internal float[] Read(int outputRate)
            {
                if (request.result != UnityWebRequest.Result.Success)
                    throw new IOException("Unity 无法读取 Ogg 音轨。" + request.error);
                var clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null) throw new IOException("Unity 没有返回可试听的音频片段。");
                try
                {
                    if (clip.channels != sourceChannels || clip.frequency != Rate ||
                        clip.samples <= 0 || clip.samples > (long)Rate * MaxClipSeconds)
                        throw new IOException("试听音轨的采样信息与资源索引不一致。");
                    var frames = Math.Min(clip.samples, checked(Rate * limitSeconds));
                    var source = new float[checked(frames * sourceChannels)];
                    if (!clip.GetData(source, 0)) throw new IOException("Unity 无法读取试听音轨的 PCM 数据。");
                    if (sourceChannels == 1)
                    {
                        var stereo = new float[checked(frames * Channels)];
                        for (var frame = 0; frame < frames; frame++)
                            stereo[2 * frame] = stereo[2 * frame + 1] = source[frame];
                        source = stereo;
                    }
                    return Rate == outputRate ? source : Resample(source, Rate, outputRate);
                }
                finally { UnityEngine.Object.DestroyImmediate(clip); }
            }

            public void Dispose()
            {
                if (!request.isDone) request.Abort();
                request.Dispose();
            }
        }

        internal static string ResolveLocalClip(string root, string file)
        {
            if (!file.StartsWith("clips/", StringComparison.Ordinal) || !file.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                file.Contains("\\") || file.Contains(":") || file.Contains("\"") || Array.IndexOf(file.Split('/'), "..") >= 0)
                throw new IOException("试听索引中的相对路径无效。");
            var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var result = Path.GetFullPath(Path.Combine(prefix, file));
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!result.StartsWith(prefix, comparison)) throw new IOException("试听音轨路径超出所选资源目录。");
            return result;
        }

        private static float[] Resample(float[] source, int sourceRate, int outputRate)
        {
            var frames = source.Length / Channels;
            var destination = new float[checked((int)Math.Ceiling((double)frames * outputRate / sourceRate) * Channels)];
            var ratio = (double)sourceRate / outputRate;
            for (var target = 0; target < destination.Length / Channels; target++)
            {
                var at = target * ratio;
                var first = Math.Min(frames - 1, (int)at);
                var second = Math.Min(frames - 1, first + 1);
                for (var channel = 0; channel < Channels; channel++)
                    destination[target * Channels + channel] = Mathf.Lerp(source[first * Channels + channel],
                        source[second * Channels + channel], (float)(at - first));
            }
            return destination;
        }

        internal sealed class PreviewPcm
        {
            private readonly float[] lead;
            private readonly float[] intro;
            private readonly float[] loop;
            private readonly int overlap;
            private int position;
            private readonly bool singlePass;
            private float volume = 1;
            internal float Volume
            {
                get => Volatile.Read(ref volume);
                set => Volatile.Write(ref volume, Mathf.Clamp01(value));
            }
            internal int SampleCount { get; }
            internal bool NativeLoop => !singlePass && lead.Length == 0 && intro.Length == 0 && loop.Length > 0;

            internal PreviewPcm(float[] lead, float[] intro, float[] loop, int crossfadeFrames, bool singlePass = false)
            {
                this.lead = lead; this.intro = intro; this.loop = loop;
                this.singlePass = singlePass;
                if (lead.Length % Channels != 0 || intro.Length % Channels != 0 || loop.Length % Channels != 0)
                    throw new FormatException("试听音轨的采样通道未对齐。");
                var head = intro.Length == 0 ? loop.Length : intro.Length;
                overlap = Math.Min(Math.Min(lead.Length, head), Math.Max(0, crossfadeFrames) * Channels);
                SampleCount = checked(lead.Length - overlap + intro.Length + loop.Length * (singlePass || NativeLoop ? 1 : 8));
                if (SampleCount == 0) throw new IOException("没有可播放的试听音轨。");
            }

            internal void Read(float[] buffer)
            {
                var gain = Volume;
                for (var at = 0; at < buffer.Length; at++)
                {
                    var sample = position++;
                    var trackSample = sample - (lead.Length - overlap);
                    if (sample >= SampleCount) buffer[at] = 0f;
                    else if (trackSample < 0) buffer[at] = lead[sample];
                    else
                    {
                        var target = trackSample < intro.Length ? intro[trackSample] :
                            loop.Length == 0 ? 0 : loop[(trackSample - intro.Length) % loop.Length];
                        if (trackSample < overlap)
                        {
                            var mix = (float)(trackSample / Channels) / (overlap / Channels);
                            buffer[at] = lead[sample] * (1f - mix) + target * mix;
                        }
                        else buffer[at] = target;
                    }
                    buffer[at] *= gain;
                }
            }

            internal void Seek(int frame) => position = checked(Math.Max(0, frame) * Channels);
        }
    }
}
