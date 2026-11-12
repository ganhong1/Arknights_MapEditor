#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal static class DeploymentCardArt
    {
        private const string Catalog = PrtsObjectView.CatalogRoot;
        private static Dictionary<string, (Texture2D Texture, Rect Region)>? sprites;
        private static int frameBorder;

        private static void Load()
        {
            if (sprites != null) return;
            var index = AssetDatabase.LoadAssetAtPath<TextAsset>(Catalog + "deployment-card-art.json");
            var style = AssetDatabase.LoadAssetAtPath<Texture2D>(Catalog + "deployment-style.png");
            var portraits = AssetDatabase.LoadAssetAtPath<Texture2D>(Catalog + "deployment-portraits.png");
            var entries = new Dictionary<string, (Texture2D Texture, Rect Region)>(System.StringComparer.Ordinal);
            if (index != null && style != null && portraits != null)
            {
                var data = JObject.Parse(index.text);
                frameBorder = (int)data["frameBorder"]!;
                foreach (var item in ((JObject)data["entries"]!).Properties())
                {
                    var texture = (bool)item.Value["portrait"]! ? portraits : style;
                    var rect = (JArray)item.Value["rect"]!;
                    entries.Add(item.Name, (texture, new Rect((int)rect[0]!, (int)rect[1]!, (int)rect[2]!, (int)rect[3]!)));
                }
            }
            sprites = entries;
        }

        internal static bool TryGet(string key, out Texture2D? texture, out Rect uv)
        {
            Load();
            if (sprites!.TryGetValue(key, out var sprite))
            {
                texture = sprite.Texture;
                var r = sprite.Region;
                uv = new Rect(r.x / texture.width, r.y / texture.height, r.width / texture.width, r.height / texture.height);
                return true;
            }
            texture = null;
            uv = default;
            return false;
        }

        internal static Image Image(string key, string elementName, ScaleMode mode = ScaleMode.StretchToFill)
        {
            TryGet(key, out var texture, out var uv);
            return new Image { name = elementName, image = texture, uv = uv, scaleMode = mode, pickingMode = PickingMode.Ignore };
        }

        internal static VisualElement Frame()
        {
            var frame = new VisualElement { name = "deployment-card-frame", pickingMode = PickingMode.Ignore };
            frame.generateVisualContent += context =>
            {
                Load();
                if (sprites!.TryGetValue("frame", out var sprite))
                    DrawFrame(context, frame.contentRect, sprite.Texture, sprite.Region);
            };
            return frame;
        }

        private static void DrawFrame(MeshGenerationContext context, Rect bounds, Texture2D texture, Rect source)
        {
            if (bounds.width <= 0 || bounds.height <= 0) return;
            var border = Mathf.Min(frameBorder * bounds.height / PresetHandPreview.AuthoredHeight, bounds.width * .5f);
            var mesh = context.Allocate(16, 54, texture);
            var uv = mesh.uvRegion;
            for (var row = 0; row < 4; row++)
            for (var col = 0; col < 4; col++)
            {
                var u = Slice(source.x, source.width, frameBorder, col) / texture.width;
                var v = Slice(source.y, source.height, frameBorder, 3 - row) / texture.height;
                mesh.SetNextVertex(new Vertex
                {
                    position = new Vector3(Slice(bounds.x, bounds.width, border, col),
                        Slice(bounds.y, bounds.height, border, row), Vertex.nearZ),
                    tint = Color.white,
                    uv = new Vector2(uv.x + u * uv.width, uv.y + v * uv.height)
                });
            }
            for (var row = 0; row < 3; row++)
            for (var col = 0; col < 3; col++)
            {
                var top = row * 4 + col;
                mesh.SetNextIndex((ushort)top);
                mesh.SetNextIndex((ushort)(top + 1));
                mesh.SetNextIndex((ushort)(top + 5));
                mesh.SetNextIndex((ushort)top);
                mesh.SetNextIndex((ushort)(top + 5));
                mesh.SetNextIndex((ushort)(top + 4));
            }
        }

        private static float Slice(float start, float length, float border, int point) => point switch
        {
            0 => start,
            1 => start + border,
            2 => start + length - border,
            _ => start + length
        };
    }
}
