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

        private void LocateDocumentField(string field)
        {
            var root = workspaceInspector ?? rootVisualElement;
            setDocumentMode?.Invoke(false);
            var path = "/" + EscapePointer(field);
            var tree = root.Q<ScrollView>("leveldata-tree");
            var node = tree?.Q("leveldata-node:" + path);
            if (node == null)
                node = tree?.Query<Label>().Where(label => label.tooltip == path).First();
            if (node == null)
            {
                if (status != null) status.text = "当前文档未提供字段：" + field;
                return;
            }
            if (node is Foldout fold) fold.value = true;
            tree!.schedule.Execute(() => tree.ScrollTo(node));
        }
    }
}
