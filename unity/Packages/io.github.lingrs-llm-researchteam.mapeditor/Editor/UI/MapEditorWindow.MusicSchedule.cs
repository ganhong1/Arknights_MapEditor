#nullable enable
using System;
using System.Linq;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        private sealed partial class WavesPanel
        {
            private string? revealedMusic;
            private bool MatchesRevealedMusic(string key) => revealedMusic != null &&
                (revealedMusic == key || revealedMusic.StartsWith(key + "/", StringComparison.Ordinal));

            internal void DrawMusicAction(VisualElement parent, MusicActionAddress address, Action rebuild, Action<int> applied, Action draftChanged)
            {
                Refresh();
                var fragment = WaveSchedule.Read(waves, new[] { address.Wave, address.Segment }) as JObject;
                if (fragment == null) return;
                var actions = fragment["actions"] as JArray;
                var index = address.Pending ? actions?.Count ?? 0 : address.Index;
                var action = address.Pending ? ActionDataRules.NewAction("PLAY_BGM") :
                    actions != null && index < actions.Count ? actions[index] as JObject : null;
                if (action == null) return;
                DrawAction(parent, new[] { address.Wave, address.Segment, index }, address.Target, action, address.Pending,
                    true, rebuild, () => applied(index), draftChanged);
            }

            internal void RemapMusicReveal(Func<string, string?> remap) =>
                revealedMusic = revealedMusic == null ? null : remap(revealedMusic);

            internal void RevealMusicAction(MusicActionAddress address)
            {
                var path = address.Pending ? new[] { address.Wave, address.Segment } :
                    new[] { address.Wave, address.Segment, address.Index };
                revealedMusic = address.Target;
                Expand(path);
                if (address.Pending) owner.selectedWaveRecord = address.Target;
                Refresh(true);
                ScrollToMusicTarget(tree.Q<Foldout>("waves-node-" + address.Target.Replace('/', '-')));
            }
        }

        private sealed partial class BranchesPanel
        {
            private string? revealedMusicBranch;
            private string? revealedMusicTarget;

            internal void DrawMusicAction(VisualElement parent, MusicActionAddress address, Action rebuild, Action<int> applied, Action draftChanged)
            {
                Refresh();
                if (BranchInspectorDrafts.ReadRecord(document,
                    BranchInspectorDrafts.Key(address.Branch!, address.Segment))?["actions"] is not JArray actions) return;
                var index = address.Pending ? actions.Count : address.Index;
                var action = address.Pending ? ActionDataRules.NewAction("PLAY_BGM") :
                    index < actions.Count ? actions[index] as JObject : null;
                if (action == null) return;
                DrawActionFields(parent, address.Branch!, address.Segment, address.BranchIndex, action, actions.Count,
                    true, rebuild, () => applied(index), draftChanged);
            }

            internal void RemapMusicReveal(Func<string, string?> remap)
            {
                revealedMusicTarget = revealedMusicTarget == null ? null : remap(revealedMusicTarget);
                revealedMusicBranch = revealedMusicTarget != null &&
                    BranchInspectorDrafts.ReadTarget(revealedMusicTarget, out var branch, out _) ? branch : null;
            }

            internal void RevealMusicAction(MusicActionAddress address)
            {
                var branch = address.Branch!;
                revealedMusicBranch = branch;
                revealedMusicTarget = address.Target;
                Open(branch, address.Segment, address.BranchIndex);
                var parent = BranchInspectorDrafts.Key(branch, address.Segment);
                limits[branch] = Math.Max(address.Segment + 1, limits.TryGetValue(branch, out var phaseLimit) ? phaseLimit : 30);
                limits[parent] = Math.Max(address.Pending ? 0 : address.Index + 1,
                    limits.TryGetValue(parent, out var actionLimit) ? actionLimit : 30);
                Refresh(true);
                var target = tree.Query<Foldout>().ToList().FirstOrDefault(fold => fold.name == "branch-node:" + address.Target);
                ScrollToMusicTarget(target);
            }
        }
    }
}
