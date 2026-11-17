#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        private readonly List<Action> refreshFieldSummaries = new List<Action>();

        private void BuildFieldPlaceholder(VisualElement parent, string title, string description, params string[] fields)
        {
            parent.Add(new Label(title));
            parent.Add(new HelpBox(description + " 专用编辑器待实现；此页面只读，不创建或修改字段。", HelpBoxMessageType.Info));
            foreach (var field in fields)
            {
                var summary = new Label { name = "field-summary-" + field };
                summary.style.whiteSpace = WhiteSpace.Normal;
                parent.Add(summary);
                void RefreshSummary()
                {
                    var value = session!.Document.GetSection(field);
                    var state = value == null ? "未提供" : value.Type == JTokenType.Null ? "null" :
                        value is JArray array ? "数组 · " + array.Count + " 项" :
                        value is JObject obj ? "对象 · " + obj.Count + " 个字段" : "值 · " + value.ToString();
                    summary.text = "LevelData." + field + " — " + state;
                }
                refreshFieldSummaries.Add(RefreshSummary);
                RefreshSummary();
                var locate = new Button(() => LocateDocumentField(field))
                    { text = "定位到字段树", name = "locate-field-" + field };
                parent.Add(locate);
            }
        }

        private void LocateDocumentField(string field) => LocateDocumentPath("/" + EscapePointer(field));

        private void LocateDocumentPath(string path)
        {
            var root = workspaceInspector ?? rootVisualElement;
            setDocumentMode?.Invoke(false);
            var tree = root.Q<ScrollView>("leveldata-tree");
            VisualElement? node = null;
            VisualElement? scope = tree;
            var current = "";
            foreach (var part in path.Substring(1).Split('/'))
            {
                var parent = current;
                current += "/" + part;
                node = scope?.Q("leveldata-node:" + current);
                if (node == null) node = scope?.Query<Label>().Where(label => label.tooltip == current).First();
                if (node == null && int.TryParse(part, out var index) && index >= 50 &&
                    scope?.Q<Button>("leveldata-more:" + parent)?.userData is Action append)
                {
                    for (var page = 1; page <= index / 50 && scope.Q<Button>("leveldata-more:" + parent) != null; page++)
                        append();
                    node = scope.Q("leveldata-node:" + current) ??
                        (VisualElement?)scope.Query<Label>().Where(label => label.tooltip == current).First();
                }
                if (node == null) break;
                if (node is Foldout fold) fold.value = true;
                scope = node;
            }
            if (node == null)
            {
                if (status != null) status.text = "字段树未显示：" + path;
                return;
            }
            tree!.schedule.Execute(() => tree.ScrollTo(node));
        }
    }
}
