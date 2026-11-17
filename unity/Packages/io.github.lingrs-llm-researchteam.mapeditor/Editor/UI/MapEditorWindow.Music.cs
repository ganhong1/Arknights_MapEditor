#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        internal readonly struct MusicActionAddress : IEquatable<MusicActionAddress>
        {
            internal string? Branch { get; }
            internal int Wave { get; }
            internal int Segment { get; }
            internal int Index { get; }
            internal bool Pending => Index < 0;
            internal bool FromBranch => Branch != null;
            internal int BranchIndex => Pending ? int.MaxValue : Index;
            internal string Target => FromBranch
                ? BranchInspectorDrafts.Key(Branch!, Segment, BranchIndex)
                : WaveSchedule.Key(new[] { Wave, Segment }) + (Pending ? "/new" : "/" + Index);
            internal string Slot => FromBranch ? BranchInspectorDrafts.Slot(Target) : WaveInspectorDrafts.Slot(Target);
            internal string ParentLabel => FromBranch ? $"分支 {Branch} · 阶段 [{Segment}]" : $"波次 [{Wave}] · 片段 [{Segment}]";

            internal MusicActionAddress(string? branch, int wave, int segment, int index)
            { Branch = branch; Wave = wave; Segment = segment; Index = index; }

            internal MusicActionAddress At(int index) => new(Branch, Wave, Segment, index);
            public bool Equals(MusicActionAddress other) =>
                Branch == other.Branch && Wave == other.Wave && Segment == other.Segment && Index == other.Index;
            public override bool Equals(object? obj) => obj is MusicActionAddress other && Equals(other);
            public override int GetHashCode() => (Branch, Wave, Segment, Index).GetHashCode();
        }

        private MusicPanel? musicPanel;
        private MusicActionAddress? musicSelection;
        private string? OpeningMusicKey => WorkspaceDocument.GetSection("bgmEvent") is JValue { Type: JTokenType.String } value
            ? value.Value<string>() : null;

        private MusicEventCatalog.Entry? PreviousMusic(JArray? siblings, int actionIndex, JObject action)
        {
            var catalog = MusicEventCatalog.LoadBundled();
            var time = WaveSchedule.Time(action["preDelay"]) ?? 0;
            if (siblings != null)
            {
                var candidates = siblings.Select((value, index) => (Fields: value as JObject, Index: index))
                    .Where(item => item.Index != actionIndex && item.Fields != null &&
                        WaveSchedule.ActionType(item.Fields["actionType"]) == "PLAY_BGM")
                    .Select(item => (item.Fields, item.Index, Time: WaveSchedule.Time(item.Fields!["preDelay"]) ?? 0))
                    .Where(item => item.Time < time || item.Time == time && item.Index < actionIndex)
                    .OrderByDescending(item => item.Time).ThenByDescending(item => item.Index);
                foreach (var candidate in candidates)
                    if (candidate.Fields!["key"] is JValue { Type: JTokenType.String } key &&
                        catalog.Find(false, key.Value<string>()!) is { IsRegistered: true, HasPreview: true } entry)
                        return entry;
            }
            return OpeningMusicKey is string opening ? catalog.Find(true, opening) : null;
        }

        private void BuildMusicPanel(VisualElement parent)
        {
            musicPanel = new MusicPanel(this);
            parent.Add(musicPanel);
        }

        private void RefreshMusic()
        {
            if (workbenchTab == 11 && session != null) musicPanel?.Render();
        }

        private void NavigateWorkbench(int tab)
        {
            selectWorkbenchTab?.Invoke(tab);
        }

        private void OpenActionInMusic(MusicActionAddress address)
        {
            musicSelection = address;
            NavigateWorkbench(11);
            musicPanel?.RevealSelection();
        }

        private void OpenMusicActionInSchedule(MusicActionAddress address)
        {
            if (address.FromBranch)
            {
                NavigateWorkbench(7);
                branchesPanel?.RevealMusicAction(address);
            }
            else
            {
                NavigateWorkbench(2);
                wavesPanel?.RevealMusicAction(address);
            }
        }

        private void ClearMusicSelection(bool fromBranch)
        {
            RemapMusicSelection(fromBranch, _ => null);
            if (fromBranch) branchesPanel?.RemapMusicReveal(_ => null);
            else wavesPanel?.RemapMusicReveal(_ => null);
        }

        private void RemapMusicSelection(bool fromBranch, Func<string, string?> remap)
        {
            musicPanel?.RemapCreationTarget(fromBranch, remap);
            if (musicSelection is not { } selected || selected.FromBranch != fromBranch) return;
            musicSelection = RemapMusicAddress(selected, remap);
        }

        private static MusicActionAddress? RemapMusicAddress(MusicActionAddress selected, Func<string, string?> remap)
        {
            var fromBranch = selected.FromBranch;
            var mapped = remap(selected.Target);
            if (mapped == selected.Target) return selected;
            if (mapped == null) return null;
            if (fromBranch)
            {
                if (BranchInspectorDrafts.ReadTarget(mapped, out var branch, out var path) && path.Length == 2)
                    return new MusicActionAddress(branch, -1, path[0], path[1] == int.MaxValue ? -1 : path[1]);
                return null;
            }
            var parts = mapped.Split('/');
            if (parts.Length == 3 && int.TryParse(parts[0], out var wave) && int.TryParse(parts[1], out var fragment) &&
                (parts[2] == "new" || int.TryParse(parts[2], out _)))
                return new MusicActionAddress(null, wave, fragment, parts[2] == "new" ? -1 : int.Parse(parts[2]));
            return null;
        }

        private static void ScrollToMusicTarget(VisualElement? target)
        {
            if (target == null) return;
            target.schedule.Execute(() =>
            {
                if (target.panel == null) return;
                target.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(target);
            });
        }

        private sealed partial class MusicPanel : VisualElement
        {
            private readonly MapEditorWindow owner;
            private readonly VisualElement opening = new();
            private readonly VisualElement targets = new();
            private readonly VisualElement list = new() { name = "music-action-list" };
            private readonly VisualElement edit = new() { name = "music-action-edit" };
            private readonly Label message = Label("");
            private readonly DropdownField filter;
            private readonly List<(MusicActionAddress Address, JObject Fields)> rows = new();
            private MusicActionAddress? creationTarget;
            private int limit = 40;

            internal MusicPanel(MapEditorWindow owner)
            {
                this.owner = owner;
                name = "music-editor";
                opening.AddToClassList("wave-settings");
                Add(opening);
                var schedule = new VisualElement();
                schedule.AddToClassList("wave-settings");
                schedule.Add(Label("局中换曲"));
                schedule.Add(targets);
                filter = new DropdownField("显示", new List<string> { "全部", "波次", "分支" }, 0);
                filter.RegisterValueChangedCallback(_ => DrawRows());
                schedule.Add(filter);
                schedule.Add(list);
                schedule.Add(edit);
                Add(schedule);
                Add(message);
            }

            internal void RemapCreationTarget(bool fromBranch, Func<string, string?> remap)
            {
                if (creationTarget is { } target && target.FromBranch == fromBranch)
                    creationTarget = RemapMusicAddress(target, remap);
            }

            internal void Render()
            {
                DrawOpening();
                DrawTargets();
                DrawRows();
            }

            internal void RevealSelection()
            {
                DrawRows();
                ScrollToMusicTarget(edit);
            }

            private static IEnumerable<(MusicActionAddress Parent, JArray? Actions)> EnumerateTargets(LevelDocument document)
            {
                if (document.GetSection("waves") is JArray waves)
                    for (var wave = 0; wave < waves.Count; wave++)
                    {
                        if ((waves[wave] as JObject)?["fragments"] is not JArray fragments) continue;
                        for (var fragment = 0; fragment < fragments.Count; fragment++)
                        {
                            if (fragments[fragment] is not JObject record) continue;
                            var actions = record["actions"];
                            if (actions == null || actions.Type == JTokenType.Null || actions is JArray)
                                yield return (new MusicActionAddress(null, wave, fragment, -1), actions as JArray);
                        }
                    }
                if (document.GetSection("branches") is not JObject branches) yield break;
                foreach (var branch in branches.Properties())
                {
                    if ((branch.Value as JObject)?["phases"] is not JArray phases) continue;
                    for (var phase = 0; phase < phases.Count; phase++)
                        if ((phases[phase] as JObject)?["actions"] is JArray actions)
                            yield return (new MusicActionAddress(branch.Name, -1, phase, -1), actions);
                }
            }

            private void DrawTargets()
            {
                targets.Clear();
                var choices = EnumerateTargets(owner.WorkspaceDocument).Select(target => target.Parent).ToArray();
                if (choices.Length == 0)
                {
                    targets.Add(Label("新增换曲前请选择一个波次片段或分支阶段。"));
                    AddCommand(targets, "去新建片段", () => owner.NavigateWorkbench(2));
                    AddCommand(targets, "去新建阶段", () => owner.NavigateWorkbench(7));
                    return;
                }

                var labels = new List<string> { "选择片段或阶段" };
                labels.AddRange(choices.Select(choice => choice.ParentLabel));
                var selected = Array.FindIndex(choices, address => address.Equals(creationTarget)) + 1;
                var destination = new DropdownField("放入", labels, selected) { name = "music-target" };
                targets.Add(destination);
                var create = AddCommand(targets, "新增换曲", () =>
                {
                    var address = choices[destination.index - 1];
                    owner.RequireWorkspaceEditable();
                    owner.RequireAppliedDraft();
                    var slot = address.Slot;
                    if (owner.InspectorStore.HasChanges(slot))
                        owner.InspectorStore.RequireCurrent(slot, owner.session!.Json, address.Target);
                    else
                    {
                        owner.InspectorStore.SetDraft(slot, owner.session!.Json, address.Target, "",
                            ActionDataRules.NewAction("PLAY_BGM").ToString(Formatting.None));
                        owner.PersistInspectorDrafts();
                    }
                    owner.musicSelection = address;
                    RevealSelection();
                }, "music-new-action");
                create.SetEnabled(destination.index > 0);
                destination.RegisterValueChangedCallback(_ =>
                {
                    creationTarget = destination.index <= 0 ? null : choices[destination.index - 1];
                    create.SetEnabled(destination.index > 0);
                });
            }

            private JObject? ReadFields(MusicActionAddress address, JObject? applied)
            {
                var store = owner.InspectorStore;
                if (!store.HasChanges(address.Slot) || store.HasPendingRaw(address.Slot)) return applied;
                try { store.RequireCurrent(address.Slot, owner.session!.Json, address.Target); }
                catch (InvalidOperationException) { return applied; }
                if (!store.TryRead(address.Slot, out var raw)) return applied;
                try { return LevelDocument.ParseEnemyDefinition(raw); }
                catch (FormatException) { return applied; }
            }

            private void DrawRows()
            {
                RefreshRows();
                DrawSelected();
            }

            private void RefreshRows()
            {
                rows.Clear();
                list.Clear();
                foreach (var target in EnumerateTargets(owner.WorkspaceDocument))
                {
                    var excluded = filter.index == 1 && target.Parent.FromBranch || filter.index == 2 && !target.Parent.FromBranch;
                    if (excluded && !(owner.musicSelection is { } selected && selected.At(-1).Equals(target.Parent))) continue;
                    if (target.Actions != null)
                        for (var index = 0; index < target.Actions.Count; index++)
                        {
                            if (target.Actions[index] is not JObject applied) continue;
                            var address = target.Parent.At(index);
                            if (excluded && !address.Equals(owner.musicSelection)) continue;
                            var fields = ReadFields(address, applied)!;
                            if (WaveSchedule.ActionType(fields["actionType"]) == "PLAY_BGM" ||
                                WaveSchedule.ActionType(applied["actionType"]) == "PLAY_BGM")
                                rows.Add((address, fields));
                        }
                    if ((!excluded || target.Parent.Equals(owner.musicSelection)) &&
                        ReadFields(target.Parent, null) is { } pending &&
                        WaveSchedule.ActionType(pending["actionType"]) == "PLAY_BGM")
                        rows.Add((target.Parent, pending));
                }
                var selectedIndex = rows.FindIndex(row => row.Address.Equals(owner.musicSelection));
                if (selectedIndex >= 0) limit = Math.Max(limit, selectedIndex + 1);
                if (rows.Count == 0) list.Add(Label("当前没有换曲动作。"));
                for (var index = 0; index < Math.Min(limit, rows.Count); index++)
                {
                    var (address, fields) = rows[index];
                    var key = fields["key"]?.Type == JTokenType.String ? fields.Value<string>("key")! : "未选曲";
                    var delay = WaveSchedule.Time(fields["preDelay"]);
                    var entry = MusicEventCatalog.LoadBundled().Find(false, key);
                    var title = entry != null && entry.Label != key ? entry.Label + " · " + key : key;
                    var state = entry?.IsRegistered == true ? "已登记" : entry?.HasPreview == true ? "局中事件未登记 · 有参考音轨" : "局中事件未登记";
                    var item = AddCommand(list, $"{title} · {(delay.HasValue ? $"+{delay:g}s" : "延时未定")}\n{address.ParentLabel} · {(address.Pending ? "新增草稿" : $"动作 [{address.Index}]")} · {state}" +
                        (owner.InspectorStore.HasChanges(address.Slot) ? " ●" : ""), () =>
                        {
                            owner.musicSelection = address;
                            DrawRows();
                            ScrollToMusicTarget(edit);
                        }, "music-action-" + index);
                    item.style.whiteSpace = WhiteSpace.Normal;
                    item.style.unityTextAlign = UnityEngine.TextAnchor.MiddleLeft;
                    item.EnableInClassList("selected-tab", address.Equals(owner.musicSelection));
                }
                if (limit < rows.Count) AddCommand(list, "显示后续 40 项", () => { limit += 40; RefreshRows(); });
            }

            private void DrawSelected()
            {
                edit.Clear();
                if (owner.musicSelection is not { } address) return;
                if (!rows.Any(row => row.Address.Equals(address)) &&
                    !(address.Pending && owner.InspectorStore.HasChanges(address.Slot))) return;

                if (!EnumerateTargets(owner.WorkspaceDocument).Any(target => target.Parent.Equals(address.At(-1)))) return;
                edit.Add(Label(address.ParentLabel + (address.Pending ? " · 新增换曲" : $" · 动作 [{address.Index}]")));
                AddCommand(edit, address.FromBranch ? "定位到分支" : "定位到波次",
                    () => owner.OpenMusicActionInSchedule(address), "music-action-locate");
                void Applied(int index)
                {
                    owner.musicSelection = address.At(index);
                    RevealSelection();
                }
                if (address.FromBranch)
                    owner.branchesPanel?.DrawMusicAction(edit, address, DrawRows, Applied, RefreshRows);
                else
                    owner.wavesPanel?.DrawMusicAction(edit, address, DrawRows, Applied, RefreshRows);
            }

            private Button AddCommand(VisualElement parent, string text, Action action, string? controlName = null)
            {
                var button = new Button(() =>
                {
                    try { message.text = ""; action(); }
                    catch (Exception error) { message.text = PluginLog.FailureMessage("document.music", error); }
                }) { text = text };
                if (controlName != null) button.name = controlName;
                parent.Add(button);
                return button;
            }

            private static Label Label(string text)
            {
                var label = new Label(text);
                label.style.whiteSpace = WhiteSpace.Normal;
                return label;
            }
        }
    }
}
