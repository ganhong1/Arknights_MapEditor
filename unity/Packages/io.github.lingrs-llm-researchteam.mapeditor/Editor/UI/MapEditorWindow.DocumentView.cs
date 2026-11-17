#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        [SerializeField] private List<string> expandedDocumentPaths = new List<string>();
        private Action? refreshDocumentView;
        private Action? refreshDocumentNotice;
        private Action<bool>? setDocumentMode;

        private VisualElement BuildDocumentView()
        {
            var main = new VisualElement { name = "leveldata-document" };
            main.AddToClassList("leveldata-document");
            var tools = new VisualElement(); tools.AddToClassList("workbench-tabs");
            var treeButton = new Button { text = "字段树", name = "leveldata-tree-mode" };
            var sourceButton = new Button { text = "原始 JSON", name = "leveldata-json-mode" };
            tools.Add(treeButton); tools.Add(sourceButton); main.Add(tools);
            var notice = new Label(); notice.AddToClassList("leveldata-notice"); main.Add(notice);
            var tree = new ScrollView { name = "leveldata-tree" };
            tree.style.flexGrow = 1; main.Add(tree);
            var source = new VisualElement { name = "leveldata-source-panel" };
            source.style.flexGrow = 1; source.style.minHeight = 0;
            var scroll = new ScrollView(); scroll.style.flexGrow = 1;
            jsonField = new TextField { multiline = true, name = "leveldata-json" };
            jsonField.style.flexGrow = 1;
            jsonField.RegisterValueChangedCallback(change =>
            {
                draft = change.newValue;
                hasUnsavedChanges = WorkspaceDirty;
                refreshDocumentView?.Invoke();
                workspaceOwner?.DocumentChanged(this);
            });
            scroll.Add(jsonField); source.Add(scroll);
            AddButton(source, "校验并应用 JSON", () => { ApplyDraft(); Refresh(); });
            main.Add(source);
            void Mode(bool raw)
            {
                sourceExpanded = raw;
                tree.style.display = raw ? DisplayStyle.None : DisplayStyle.Flex;
                source.style.display = raw ? DisplayStyle.Flex : DisplayStyle.None;
                treeButton.EnableInClassList("selected-tab", !raw);
                sourceButton.EnableInClassList("selected-tab", raw);
            }
            treeButton.clicked += () => Mode(false);
            setDocumentMode = Mode;
            sourceButton.clicked += () => Mode(true);
            var rendered = "";
            refreshDocumentNotice = () => notice.text = draft != session!.Json ? "原始 JSON 有未应用修改；字段树显示已应用文档。" :
                    InspectorDirty ? "编辑抽屉有未应用草稿；字段树显示已应用文档。" : "完整 LevelData · 已应用文档（字段树只读）";
            refreshDocumentView = () =>
            {
                if (session == null) return;
                foreach (var refresh in refreshFieldSummaries) refresh();
                RefreshDocumentNotice();
                if (rendered == session.Json) return;
                var offset = tree.scrollOffset;
                tree.Clear();
                using var input = new System.IO.StringReader(session.Json);
                using var reader = new JsonTextReader(input) { DateParseHandling = DateParseHandling.None };
                var document = JObject.Load(reader);
                foreach (var property in document.Properties())
                    AddDocumentNode(tree, property.Name, session.Document.GetSection(property.Name)!, "/" + EscapePointer(property.Name));
                rendered = session.Json;
                tree.scrollOffset = offset;
            };
            Mode(sourceExpanded);
            return main;
        }

        private void RefreshDocumentNotice() => refreshDocumentNotice?.Invoke();

        private static string EscapePointer(string key) => key.Replace("~", "~0").Replace("/", "~1");

        private void AddDocumentNode(VisualElement parent, string key, JToken value, string path)
        {
            if (!(value is JObject) && !(value is JArray))
            {
                var row = new Label(key + " : " + value.ToString(Formatting.None)) { tooltip = path };
                row.AddToClassList("leveldata-value"); parent.Add(row); return;
            }
            var container = (JContainer)value;
            var fold = new Foldout { text = key + (value is JArray ? " []" : " {}") + " · " + container.Count,
                tooltip = path, name = "leveldata-node:" + path, value = expandedDocumentPaths.Contains(path) };
            // Nested Foldout defaults depend on Unity's depth classes. Give each
            // JSON container its own explicit indent, including lazily added nodes.
            fold.style.marginLeft = 0;
            fold.contentContainer.style.marginLeft = 18;
            fold.AddToClassList("leveldata-tree-node");
            var header = fold.Q<Toggle>();
            if (header != null) header.style.marginLeft = 0;
            parent.Add(fold);
            var loaded = false;
            void Load()
            {
                if (loaded) return;
                loaded = true;
                // Materialize only requested containers and page wide arrays/objects.
                var items = container.Children().ToArray();
                var next = 0;
                var more = new Button { text = "显示后续 50 项", name = "leveldata-more:" + path };
                void Append()
                {
                    more.RemoveFromHierarchy();
                    var stop = Math.Min(next + 50, items.Length);
                    for (; next < stop; next++)
                    {
                        var child = items[next];
                        var property = child as JProperty;
                        var label = property?.Name ?? "[" + next + "]";
                        AddDocumentNode(fold, label, property?.Value ?? child,
                            path + "/" + (property == null ? next.ToString() : EscapePointer(property.Name)));
                    }
                    if (next < items.Length) fold.Add(more);
                }
                more.clicked += Append;
                more.userData = (Action)Append;
                Append();
            }
            fold.RegisterValueChangedCallback(evt =>
            {
                if (evt.target != fold) return;
                if (evt.newValue) { if (!expandedDocumentPaths.Contains(path)) expandedDocumentPaths.Add(path); Load(); }
                else expandedDocumentPaths.Remove(path);
            });
            if (fold.value) Load();
        }
    }
}
