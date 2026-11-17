#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal static class LibraryPortrait
    {
        internal static Image Create(string cssClass, string name)
        {
            var image = new Image { name = name, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.AddToClassList(cssClass);
            return image;
        }

        internal static void Bind(Image image, Texture2D? texture, Rect uv, bool lightBackground = false)
        {
            image.image = texture;
            image.uv = uv;
            image.EnableInClassList("library-portrait-light", texture != null && lightBackground);
            image.style.visibility = texture != null ? Visibility.Visible : Visibility.Hidden;
        }

        internal static VisualElement Detail(Texture2D? texture, Rect uv, string source, string? asset, bool lightBackground = false)
        {
            var block = new VisualElement { name = "library-portrait-detail" };
            block.AddToClassList("library-portrait-detail");
            var image = Create("operator-detail-avatar", "library-detail-portrait");
            Bind(image, texture, uv, lightBackground);
            if (texture != null) block.Add(image);
            var labels = new VisualElement();
            labels.AddToClassList("library-portrait-labels");
            labels.Add(new Label(source) { name = "library-portrait-source" });
            if (!string.IsNullOrEmpty(asset))
            {
                var path = new Label(asset) { name = "library-portrait-asset", tooltip = asset };
                path.AddToClassList("catalog-id");
                labels.Add(path);
            }
            block.Add(labels);
            return block;
        }
    }
}
