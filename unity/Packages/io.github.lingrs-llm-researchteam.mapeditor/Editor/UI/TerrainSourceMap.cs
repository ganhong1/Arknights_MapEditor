#nullable enable
using System;
using Arknights.MapEditor.Catalog;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class TerrainSourceMap : VisualElement
    {
        private const string AssetPath =
            "Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/Catalog/terrain-source-maps.json";
        private static JObject? catalog;
        private readonly int width;
        private readonly int height;
        private readonly Color32[] colors;
        internal int HighlightCount { get; }

        internal TerrainSourceMap(string path, string tileKey, int? sampleNumber, string revision)
        {
            var data = catalog ??= LoadCatalog();
            if (data.Value<int>("version") != 1 || data.Value<string>("revision") != revision ||
                !(data["maps"]?[path] is JArray map) || !(data["tiles"] is JArray tiles))
                throw new FormatException("此出处暂无同版本的地图预览。");

            width = map[0]!.Value<int>();
            height = map[1]!.Value<int>();
            if (width <= 0 || height <= 0 || (long)width * height > 16383)
                throw new FormatException("地图预览尺寸无效。");
            var cells = Convert.FromBase64String(map[2]!.Value<string>()!);
            if (cells.Length != width * height * 2)
                throw new FormatException("地图预览格数不匹配。");

            var resolved = new Color32?[tiles.Count];
            colors = new Color32[width * height];
            var highlighted = new Color32(70, 221, 241, 255);
            for (var i = 0; i < colors.Length; i++)
            {
                var id = cells[i * 2] | (cells[i * 2 + 1] << 8);
                if (id >= tiles.Count || !(tiles[id] is JArray tile))
                    throw new FormatException("地图预览包含未知地块索引。");
                if (tile[0]!.Value<string>() == tileKey && (!sampleNumber.HasValue || tile[1]!.Value<int>() == sampleNumber.Value))
                {
                    colors[i] = highlighted;
                    HighlightCount++;
                }
                else
                {
                    resolved[id] ??= SkeletonStyle.Resolve(new JObject
                    {
                        ["tileKey"] = tile[0]!.DeepClone(),
                        ["heightType"] = tile[2]!.DeepClone(),
                        ["buildableType"] = tile[3]!.DeepClone()
                    }).TopColor;
                    colors[i] = resolved[id]!.Value;
                }
            }

            name = "terrain-source-map-preview";
            var title = new Label(TerrainSearchRuntime.StageName(path) ?? path);
            title.AddToClassList("tiledata-preview-important");
            Add(title);
            var highlightedScope = sampleNumber.HasValue ? "当前配置" : "当前地块变体";
            var summary = new Label($"{width} × {height} · 青色为{highlightedScope}的 {HighlightCount} 格");
            summary.AddToClassList("terrain-detail-text");
            Add(summary);
            var canvas = new VisualElement { name = "terrain-source-map-canvas" };
            canvas.style.height = 240;
            canvas.style.backgroundColor = (Color)new Color32(23, 25, 28, 255);
            canvas.generateVisualContent += context => Draw(context, canvas.contentRect);
            Add(canvas);
        }

        private static JObject LoadCatalog()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetPath);
            if (asset == null) throw new FormatException("找不到关卡地图预览资源。");
            return JObject.Parse(asset.text);
        }

        private void Draw(MeshGenerationContext context, Rect rect)
        {
            var size = Mathf.Min((rect.width - 12) / width, (rect.height - 12) / height);
            if (!(size > 0)) return;
            var origin = new Vector2((rect.width - width * size) / 2, (rect.height - height * size) / 2);
            var inset = Mathf.Min(1, size / 5);
            var mesh = context.Allocate(colors.Length * 4, colors.Length * 6);
            for (var i = 0; i < colors.Length; i++)
            {
                var x = origin.x + i % width * size;
                var y = origin.y + i / width * size;
                var vertex = (ushort)(i * 4);
                var tint = colors[i];
                mesh.SetNextVertex(new Vertex { position = new Vector3(x + inset, y + inset, Vertex.nearZ), tint = tint });
                mesh.SetNextVertex(new Vertex { position = new Vector3(x + size - inset, y + inset, Vertex.nearZ), tint = tint });
                mesh.SetNextVertex(new Vertex { position = new Vector3(x + size - inset, y + size - inset, Vertex.nearZ), tint = tint });
                mesh.SetNextVertex(new Vertex { position = new Vector3(x + inset, y + size - inset, Vertex.nearZ), tint = tint });
                mesh.SetNextIndex(vertex);
                mesh.SetNextIndex((ushort)(vertex + 1));
                mesh.SetNextIndex((ushort)(vertex + 2));
                mesh.SetNextIndex((ushort)(vertex + 2));
                mesh.SetNextIndex((ushort)(vertex + 3));
                mesh.SetNextIndex(vertex);
            }
        }
    }
}
