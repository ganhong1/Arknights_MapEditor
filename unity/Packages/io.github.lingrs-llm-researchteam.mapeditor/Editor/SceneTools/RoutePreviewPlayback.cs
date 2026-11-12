#nullable enable
using System;
using System.Collections.Generic;
using Arknights.MapEditor.Core;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Editor
{
    internal enum RoutePreviewSource { Waves, Definitions, Branches }

    [Serializable]
    internal sealed class RoutePreviewSettings
    {
        public bool Visible = true;
        public bool Animate = true;
        public bool IncludeHidden;
        public RoutePreviewMode Mode;
        public RoutePreviewSpeed Speed;
        public RoutePreviewSource Source;
        public int Selected = -1;
        public float TimeBefore = 10, TimeAfter, OrderBefore = 5, OrderAfter;
    }

    internal sealed class RoutePreviewPlayback
    {
        private PrtsRoutePreview? model;
        private LevelDocument? document;
        private double started, elapsed, duration;
        private bool ticking;
        private readonly List<(PrtsRoutePreview.Path Path, bool Focused, double Age)> paths = new();
        private readonly HashSet<(string Library, int Index)> visibleRoutes = new();
        private readonly Dictionary<PrtsRoutePreview.Path, double> ages = new();
        internal event Action? Changed;
        internal event Action? FrameChanged;
        internal bool ControlsOverlay { get; private set; }
        internal bool Visible { get; private set; }
        internal bool IncludeHidden { get; private set; }
        internal bool Animate { get; private set; } = true;
        internal bool Playing => ticking;
        internal RoutePreviewMode Mode { get; private set; }
        internal RoutePreviewSpeed Speed { get; private set; }
        internal RoutePreviewSource Source { get; private set; }
        internal int Selected { get; private set; } = -1;
        internal int VisibleCount { get; private set; }
        internal int UniquePathCount => paths.Count;
        internal bool HasFallback { get; private set; }
        internal float Before => Mode == RoutePreviewMode.Time ? timeBefore : orderBefore;
        internal float After => Mode == RoutePreviewMode.Time ? timeAfter : orderAfter;
        private float timeBefore = 10, timeAfter, orderBefore = 5, orderAfter;
        internal float Progress => duration <= 0 ? 1 : Mathf.Clamp01((float)(elapsed / duration));
        internal IReadOnlyList<PrtsRoutePreview.Entry> Entries => Source switch
        {
            RoutePreviewSource.Definitions => model?.Definitions ?? Empty,
            RoutePreviewSource.Branches => model?.BranchSpawns ?? Empty,
            _ => model?.Spawns ?? Empty
        };
        private static readonly List<PrtsRoutePreview.Entry> Empty = new();

        internal RoutePreviewSettings CaptureSettings() => new()
        {
            Visible = Visible, Animate = Animate, IncludeHidden = IncludeHidden,
            Mode = Mode, Speed = Speed, Source = Source, Selected = Selected,
            TimeBefore = timeBefore, TimeAfter = timeAfter, OrderBefore = orderBefore, OrderAfter = orderAfter
        };

        internal void RestoreSettings(RoutePreviewSettings settings, LevelDocument current)
        {
            Stop();
            ControlsOverlay = true;
            Visible = settings.Visible;
            Animate = settings.Animate;
            IncludeHidden = settings.IncludeHidden;
            Mode = settings.Mode;
            Speed = settings.Speed;
            Source = settings.Source;
            Selected = settings.Selected;
            timeBefore = settings.TimeBefore;
            timeAfter = settings.TimeAfter;
            orderBefore = settings.OrderBefore;
            orderAfter = settings.OrderAfter;
            document = null;
            model = null;
            if (Visible) Refresh(current);
            else Rebuild(false);
        }

        internal void SetVisible(bool visible, LevelDocument current)
        {
            if (visible == Visible) return;
            ControlsOverlay = true;
            Visible = visible;
            if (visible)
            {
                Refresh(current);
                if (Animate) Replay();
                else Publish();
                return;
            }
            Stop();
            Publish();
        }

        internal void Refresh(LevelDocument current)
        {
            if (!Visible || ReferenceEquals(document, current)) return;
            var entry = Selected >= 0 && Selected < Entries.Count ? Entries[Selected] : null;
            document = current;
            model = new PrtsRoutePreview(current, IncludeHidden);
            if (Source == RoutePreviewSource.Waves && Entries.Count == 0) Source = RoutePreviewSource.Definitions;
            if (Source == RoutePreviewSource.Definitions && Mode == RoutePreviewMode.Time) Mode = RoutePreviewMode.Single;
            if (entry != null)
                for (var i = 0; i < Entries.Count; ++i)
                {
                    var candidate = Entries[i];
                    if (candidate.Library != entry.Library || candidate.RouteIndex != entry.RouteIndex ||
                        candidate.Enemy != entry.Enemy || candidate.Time != entry.Time || candidate.Group != entry.Group) continue;
                    Selected = i;
                    break;
                }
            Rebuild(false);
        }

        internal void SetSource(RoutePreviewSource source)
        {
            if (Source == source) return;
            Source = source;
            Selected = 0;
            if (source == RoutePreviewSource.Definitions && Mode == RoutePreviewMode.Time) Mode = RoutePreviewMode.Single;
            Rebuild(true);
        }

        internal void Select(int index)
        {
            var next = Entries.Count == 0 ? -1 : Mathf.Clamp(index, 0, Entries.Count - 1);
            if (next == Selected) return;
            Selected = next;
            Rebuild(true);
        }

        internal void SetMode(RoutePreviewMode mode)
        {
            if (mode == Mode || (mode == RoutePreviewMode.Time && Source == RoutePreviewSource.Definitions)) return;
            Mode = mode;
            Rebuild(true);
        }

        internal void SetBounds(float before, float after)
        {
            if (Mode == RoutePreviewMode.Time)
            {
                timeBefore = Mathf.Clamp(before, 0, 3600);
                timeAfter = Mathf.Clamp(after, 0, 3600);
            }
            else
            {
                orderBefore = Mathf.Round(Mathf.Clamp(before, 0, 100));
                orderAfter = Mathf.Round(Mathf.Clamp(after, 0, 100));
            }
            Rebuild(true);
        }

        internal void SetSpeed(RoutePreviewSpeed speed)
        {
            if (Speed == speed) return;
            Speed = speed;
            Rebuild(false);
            if (Animate) Replay();
        }

        internal void SetAnimate(bool animate)
        {
            Animate = animate;
            Rebuild(false);
            if (animate) Replay();
        }

        internal void SetIncludeHidden(bool include, LevelDocument current)
        {
            if (IncludeHidden == include) return;
            IncludeHidden = include;
            document = null;
            Refresh(current);
        }

        internal void Replay()
        {
            ResetAges();
            elapsed = 0;
            if (duration > 0) Play();
            else Publish();
        }

        internal void TogglePlay()
        {
            if (ticking) { Stop(); Publish(); }
            else if (elapsed >= duration) Replay();
            else Play();
        }

        internal void Scrub(float fraction)
        {
            Stop();
            ResetAges();
            elapsed = Mathf.Clamp01(fraction) * duration;
            Publish();
        }

        internal void Stop()
        {
            if (!ticking) return;
            ticking = false;
            EditorApplication.update -= Tick;
        }

        private void Rebuild(bool restart)
        {
            Stop();
            ages.Clear();
            if (restart)
                foreach (var (path, _, age) in paths) ages[path] = age + elapsed;
            paths.Clear();
            visibleRoutes.Clear();
            HasFallback = false;
            duration = 0;
            var entries = Entries;
            Selected = entries.Count == 0 ? -1 : Mathf.Clamp(Selected, 0, entries.Count - 1);
            var visible = PrtsRoutePreview.Window(entries, Selected, Mode, Before, After);
            VisibleCount = visible.Count;
            if (model != null && Selected >= 0)
            {
                var focus = entries[Selected];
                visibleRoutes.Add((focus.Library, focus.RouteIndex));
                foreach (var index in visible)
                {
                    var entry = entries[index];
                    if (visibleRoutes.Add((entry.Library, entry.RouteIndex))) AddPath(entry, false);
                }
                AddPath(focus, true);
            }
            ages.Clear();
            elapsed = restart && Animate ? 0 : duration;
            if (restart && Animate && Visible && duration > 0) Play();
            else Publish();
        }

        private void AddPath(PrtsRoutePreview.Entry entry, bool focused)
        {
            var path = model!.GetPath(entry);
            if (path == null) return;
            ages.TryGetValue(path, out var age);
            paths.Add((path, focused, age));
            HasFallback |= path.HasFallback;
            duration = Math.Max(duration, PathDuration(path) - age);
        }

        private double PathDuration(PrtsRoutePreview.Path path) => .2 + PrtsRoutePreview.Duration(path.Length, Speed);

        private void ResetAges()
        {
            duration = 0;
            for (var i = 0; i < paths.Count; ++i)
            {
                var (path, focused, _) = paths[i];
                paths[i] = (path, focused, 0);
                duration = Math.Max(duration, PathDuration(path));
            }
        }

        private void Play()
        {
            if (!Visible || !Animate || duration <= 0) return;
            started = EditorApplication.timeSinceStartup - elapsed;
            if (!ticking) EditorApplication.update += Tick;
            ticking = true;
            Publish();
        }

        private void Tick()
        {
            elapsed = Math.Min(duration, EditorApplication.timeSinceStartup - started);
            if (elapsed >= duration)
            {
                Stop();
                Publish();
                return;
            }
            SceneView.RepaintAll();
            FrameChanged?.Invoke();
        }

        private void Publish()
        {
            SceneView.RepaintAll();
            Changed?.Invoke();
        }

        internal void Draw()
        {
            foreach (var (path, focused, age) in paths)
            {
                var clock = elapsed + age;
                var routeTime = PrtsRoutePreview.Duration(path.Length, Speed);
                var shown = !Animate || clock >= .2 + routeTime ? path.Length :
                    path.Length * Mathf.Clamp01((float)((clock - .2) / routeTime));
                if (Animate && clock < .2) continue;
                PrtsRouteOverlay.Draw(path, focused, shown);
            }
        }
    }
}
