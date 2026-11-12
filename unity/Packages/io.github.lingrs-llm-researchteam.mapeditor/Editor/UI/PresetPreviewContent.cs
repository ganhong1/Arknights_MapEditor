#nullable enable
using System.Linq;
using Arknights.MapEditor.Catalog;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class PresetPreviewContent
    {
        internal string Id { get; }
        internal string Name { get; }
        internal string Detail { get; }
        private readonly bool isOperator;

        internal PresetPreviewContent(JToken? value, bool isOperator, bool onMap)
        {
            this.isOperator = isOperator;
            var record = value as JObject;
            var inst = record?["inst"] as JObject;
            Id = inst?["characterKey"]?.Type == JTokenType.String ? (string)inst["characterKey"]! : "";
            var catalog = isOperator ? OperatorPresetCatalog.LoadBundled() : null;
            Name = Id.Length == 0 ? "未识别预设" : catalog?.Find(Id)?.Name ?? PrtsObjectView.DisplayName(Id);
            Detail = onMap ? Position(record) : catalog != null ? Training(record, inst, catalog) : Quantity(record);
        }

        internal void AddTo(VisualElement parent, string title)
        {
            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.AddToClassList("preset-preview-content");
            Texture2D? texture;
            Rect uv;
            var found = isOperator
                ? PrtsObjectView.TryGetOperatorAvatar(Id, out texture, out uv)
                : PrtsObjectView.TryGetIcon(Id, out texture, out uv);
            if (found)
            {
                var icon = new Image { image = texture, uv = uv, scaleMode = ScaleMode.ScaleToFit,
                    name = "preset-preview-icon", pickingMode = PickingMode.Ignore };
                icon.AddToClassList("preset-preview-icon");
                line.Add(icon);
            }
            var labels = new VisualElement { pickingMode = PickingMode.Ignore };
            labels.AddToClassList("preset-preview-labels");
            var heading = new Label(title) { pickingMode = PickingMode.Ignore };
            heading.AddToClassList("catalog-name");
            labels.Add(heading);
            var detail = new Label(Detail) { name = "preset-preview-detail", pickingMode = PickingMode.Ignore };
            detail.AddToClassList("preset-preview-detail");
            labels.Add(detail);
            line.Add(labels);
            parent.tooltip = $"{title} · {Id}\n{Detail}";
            parent.Add(line);
        }

        private static string Position(JObject? record)
        {
            var cell = record?["position"] as JObject;
            var coordinates = cell?["row"] != null && cell["col"] != null ? $"({cell["row"]}, {cell["col"]})" : "坐标未设";
            var direction = record?["direction"]?.ToString() switch
            {
                "UP" or "0" => "上",
                "RIGHT" or "1" => "右",
                "DOWN" or "2" => "下",
                "LEFT" or "3" => "左",
                _ => "未设"
            };
            return $"{coordinates} · {(direction == "未设" ? "朝向未设" : "朝" + direction)}";
        }

        private static string Training(JObject? record, JObject? inst, OperatorPresetCatalog catalog)
        {
            var phase = OperatorPresetCatalog.ParseTier(inst?["phase"]?.ToString());
            var stage = phase < 0 ? "精英未设" : $"精英 {phase}";
            var level = inst?["level"]?.Type == JTokenType.Integer ? $"Lv.{inst["level"]}" : "等级未设";
            var potential = int.TryParse(inst?["potentialRank"]?.ToString(), out var rank) ? $"潜 {(long)rank + 1}" : "潜能未设";
            var skillLevel = record?["mainSkillLvl"];
            var mastery = int.TryParse(skillLevel?.ToString(), out var effective) ? OperatorLabels.Mastery(effective) : null;
            var skill = int.TryParse(record?["skillIndex"]?.ToString(), out var index) && index >= 0 &&
                skillLevel?.Type == JTokenType.Integer ? $" · 技能{mastery ?? $" {skillLevel}"}" : "";
            var modules = record?["uniEquipIds"] is JArray equipped
                ? string.Join("、", equipped.OfType<JObject>().Where(module => module["key"]?.Type == JTokenType.String)
                    .Select(module => catalog.ModuleLabel((string)module["key"]!) +
                        (module["level"]?.Type == JTokenType.Integer ? $" {module["level"]}级" : "")))
                : "";
            return $"{stage} · {level} · {potential}{skill}" + (modules.Length > 0 ? $" · {modules}" : "");
        }

        private static string Quantity(JObject? record) =>
            record?["initialCnt"]?.Type == JTokenType.Integer ? $"初始数量 ×{record["initialCnt"]}" : "初始数量未设";
    }

}
