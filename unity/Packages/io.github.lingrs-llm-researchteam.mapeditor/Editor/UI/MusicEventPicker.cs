#nullable enable
using System;
using Arknights.MapEditor.Catalog;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class MusicEventPicker : VisualElement
    {
        private readonly Action<string> choose;
        private readonly Label status = new() { name = "music-event-status" };
        private readonly MusicEventCatalog? catalog;
        internal bool Opening { get; }
        internal bool Environment { get; }
        internal string Key { get; private set; }
        internal Func<MusicEventCatalog.Entry?>? PreviousMusic { get; }
        internal event Action? KeyChanged;

        internal MusicEventPicker(bool opening, string key, Action<string> choose,
            Func<MusicEventCatalog.Entry?>? previousMusic = null, Action<MusicEventPicker>? openLibrary = null, bool environment = false)
        {
            Opening = opening;
            Environment = environment;
            Key = key;
            this.choose = choose;
            PreviousMusic = previousMusic;
            name = "music-event-picker";
            status.style.whiteSpace = WhiteSpace.Normal;
            Add(status);
            if (openLibrary != null)
                Add(new Button(() => openLibrary(this)) { text = Environment ? "从资源库选音效" : "从资源库选曲", name = "music-open-library" });
            try { catalog = MusicEventCatalog.LoadBundled(); }
            catch (Exception error) { status.text = PluginLog.FailureMessage("audio.catalog", error); return; }
            ShowStatus();
        }

        internal void SetKey(string value)
        {
            if (string.Equals(Key, value, StringComparison.Ordinal)) return;
            Key = value;
            ShowStatus();
            KeyChanged?.Invoke();
        }

        internal void Choose(string value) => choose(value);

        private void ShowStatus()
        {
            if (catalog == null) return;
            if (Key.Length == 0)
            {
                status.text = Environment ? "未选择环境音效" : Opening ? "未选择开局曲" : "未选择换曲事件";
                return;
            }
            var group = Environment ? AudioLibraryCatalog.EnvironmentGroup :
                Opening ? AudioLibraryCatalog.OpeningGroup : AudioLibraryCatalog.TriggerGroup;
            MusicEventCatalog.Entry? entry;
            try
            {
                entry = (Environment ? null : catalog.Find(Opening, Key)) ??
                    AudioLibraryCatalog.LoadBundled().Find(group, Key);
            }
            catch (Exception error)
            {
                status.text = PluginLog.FailureMessage("audio.catalog", error);
                return;
            }
            status.text = entry == null
                ? (Environment ? "环境音事件未登记 · " : Opening ? "开局事件未登记 · " : "局中事件未登记 · ") + group + "." + Key
                : $"{entry.Label} · {entry.BindingStatus} · {entry.PreviewStatus}";
        }
    }
}
