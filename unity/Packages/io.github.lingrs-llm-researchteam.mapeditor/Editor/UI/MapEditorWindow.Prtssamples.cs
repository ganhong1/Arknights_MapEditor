#nullable enable
using System.IO;
using Arknights.MapEditor.Core;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        [MenuItem("Tools/Arknights/PRTSMap/Complete Catalog Overview")]
        public static void OpenCompletePrtsGallery() => OpenPrtsSample("CompletePrtsGallery", "目录总览 · 120 地块项 + 1056 物件");

        [MenuItem("Tools/Arknights/PRTSMap/54 Tile Gallery")]
        public static void OpenTileGallery() => OpenPrtsSample("SkeletonGallery", "PRTSMap · 54 地块");

        [MenuItem("Tools/Arknights/PRTSMap/685 Object Gallery")]
        public static void OpenObjectGallery() => OpenPrtsSample("ObjectGallery", "PRTSMap · 685 物件");

        private static void OpenPrtsSample(string folder, string title)
        {
            var document = LevelDocument.Parse(File.ReadAllText(
                "Packages/io.github.lingrs-llm-researchteam.mapeditor/Samples~/" + folder + "/level.json"));
            // Independent session: never replace another editor's unsaved document.
            var window = CreateInstance<MapEditorWindow>();
            window.titleContent = new GUIContent(title);
            window.CommitEdit(document, "打开 PRTSMap 样式样本", null);
            window.Show();
            window.OpenPreview();
        }
    }
}
