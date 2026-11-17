#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class OptionsBlackboardSamples : Foldout
    {
        private readonly Action<JToken> append;
        private readonly TextField search = new TextField("搜索黑板键");
        private readonly Label status = OptionsSampleBrowser.Text("");
        private readonly List<string> keys = new List<string>();
        private readonly ListView list;
        private readonly VisualElement detail = new VisualElement();
        private OptionsValueCatalog? catalog;
        private CatalogSearch<string>? finder;
        private OptionsSampleBrowser? browser;
        private string? selected;
        private bool editingEnabled = true;

        internal OptionsBlackboardSamples(Action<JToken> append)
        {
            this.append = append;
            text = "按键查看原版黑板条目 · 选用后追加"; name = "options-blackboard-key-samples"; value = false;
            Add(OptionsSampleBrowser.Text("从原版关卡选取一条黑板记录，追加到当前列表末尾。"));
            Add(search); Add(status);
            list = new ListView(keys, 26, () => new Label(), (element, index) => ((Label)element).text = keys[index])
            { name = "options-blackboard-known-keys", selectionType = SelectionType.Single };
            list.style.height = 130; Add(list); Add(detail);
            list.onSelectionChange += items => { if (items.FirstOrDefault() is string key) Select(key); };
            search.RegisterValueChangedCallback(_ => RefreshKeys());
            this.RegisterValueChangedCallback(e => { if (e.target == this && e.newValue) RefreshKeys(); });
        }

        internal void SetEditingEnabled(bool enabled) { editingEnabled = enabled; browser?.SetEditingEnabled(enabled); }

        private void RefreshKeys()
        {
            try
            {
                catalog ??= OptionsValueCatalog.LoadBundled();
                IEnumerable<string> filtered = catalog.BlackboardKeys;
                string? error = null;
                if (!string.IsNullOrWhiteSpace(search.value))
                {
                    finder ??= CatalogSearchRuntime.CreateWorker<string>(key => new JObject
                    { ["name"] = key, ["targetFile"] = key, ["description"] = OptionsReferenceNotes.SearchText(key) });
                    filtered = finder.Filter(catalog.BlackboardKeys, search.value, out error);
                }
                keys.Clear(); keys.AddRange(filtered); list.Rebuild();
                list.SetSelectionWithoutNotify(selected == null || !keys.Contains(selected) ? Array.Empty<int>() : new[] { keys.IndexOf(selected) });
                status.text = error ?? keys.Count + "/" + catalog.BlackboardKeys.Count + " 个原版关卡黑板键";
                if (selected == null && keys.Count > 0) Select(keys[0]);
            }
            catch (Exception error) { status.text = "黑板取值读取失败：" + error.Message; }
        }

        private void Select(string key)
        {
            if (key == selected) return;
            selected = key; detail.Clear();
            var note = OptionsReferenceNotes.ForKey(key);
            detail.Add(OptionsSampleBrowser.Text(string.IsNullOrEmpty(note) ? "来源：原版关卡 LevelData。" : note));
            browser = new OptionsSampleBrowser(key, candidate => append(candidate), "追加完整条目", true);
            browser.SetEditingEnabled(editingEnabled); detail.Add(browser);
        }
    }
}
