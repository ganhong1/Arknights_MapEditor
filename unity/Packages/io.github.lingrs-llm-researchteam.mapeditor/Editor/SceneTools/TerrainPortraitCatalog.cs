#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Editor
{
    internal sealed class TerrainPortraitCatalog
    {
        private const int CellSize = 64;
        private const int Columns = 12;
        private const int AtlasSize = CellSize * Columns;
        private const string AtlasFile = "terrain-portraits.png";
        private const string IndexFile = "terrain-portraits.json";
        private const string NewAtlasFile = "terrain-portraits-2791.png";
        private const string NewIndexFile = "terrain-portraits-2791.json";
        private readonly Texture2D? atlas;
        private readonly TerrainPortraitCatalog? supplement;
        private readonly Dictionary<string, int> slots;

        private TerrainPortraitCatalog(Texture2D? atlas, Dictionary<string, int> slots, TerrainPortraitCatalog? supplement)
        {
            this.atlas = atlas;
            this.slots = slots;
            this.supplement = supplement;
        }

        internal static TerrainPortraitCatalog Load() =>
            LoadPage(AtlasFile, IndexFile, LoadPage(NewAtlasFile, NewIndexFile, null));

        private static TerrainPortraitCatalog LoadPage(string image, string indexFile, TerrainPortraitCatalog? supplement)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(PrtsObjectView.CatalogRoot + image);
            var index = AssetDatabase.LoadAssetAtPath<TextAsset>(PrtsObjectView.CatalogRoot + indexFile);
            var slots = index == null ? new Dictionary<string, int>() :
                JArray.Parse(index.text).Values<string>().OfType<string>()
                    .Select((id, slot) => (id, slot)).ToDictionary(item => item.id, item => item.slot, StringComparer.Ordinal);
            return new TerrainPortraitCatalog(atlas, slots, supplement);
        }

        internal (Texture2D? Image, Rect Uv) Find(string id)
        {
            if (atlas == null || !slots.TryGetValue(id, out var slot)) return supplement?.Find(id) ?? (null, default);
            var rect = Slot(slot);
            return (atlas, new Rect(rect.x / (float)AtlasSize, rect.y / (float)AtlasSize,
                CellSize / (float)AtlasSize, CellSize / (float)AtlasSize));
        }

        private static RectInt Slot(int index) => new(index % Columns * CellSize, index / Columns * CellSize, CellSize, CellSize);

        // Regenerate with Unity 2021.3 in graphics mode using -executeMethod
        // Arknights.MapEditor.Editor.TerrainPortraitCatalog.Rebuild.
        public static void Rebuild() => RebuildPage(EditorCatalog.LoadBaseline().Entries.Where(entry => entry.Category == "terrain"),
            TerrainSampleCatalog.LoadBundled(), AtlasFile, IndexFile);

        public static void Rebuild2791()
        {
            var samples = TerrainSampleCatalog.Load2791();
            RebuildPage(EditorCatalog.LoadBundled().Entries.Where(entry => samples.Contains(entry.Id)),
                samples, NewAtlasFile, NewIndexFile);
        }

        private static void RebuildPage(IEnumerable<CatalogEntry> source, TerrainSampleCatalog samples, string atlasFile, string indexFile)
        {
            var root = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(SkeletonStyle.DefinitionPath).resolvedPath;
            var folder = Path.Combine(root, "Editor", "Catalog");
            var entries = source.OrderBy(entry => entry.Id, StringComparer.Ordinal).ToArray();
            if (entries.Length > Columns * Columns) throw new InvalidOperationException("地块缩略图超出图集容量。");
            var blank = LevelDocument.Create(1, 1);
            var state = new MapViewportState { Zoom = .52f, VerticalOffset = .12f, Pitch = 64f };
            var atlas = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGB24, false);
            var active = RenderTexture.active;
            try
            {
                atlas.SetPixels32(new Color32[AtlasSize * AtlasSize]);
                using var view = new MapWorkspaceViewport();
                for (var i = 0; i < entries.Length; i++)
                {
                    var entry = entries[i];
                    var tile = samples.Default(entry.Id) ?? TerrainBlackboardCatalog.Template(entry.Id) ??
                        entry.GetTemplate() ?? new JObject { ["tileKey"] = entry.Id, ["heightType"] = "LOWLAND",
                            ["buildableType"] = "NONE", ["passableMask"] = "ALL", ["playerSideMask"] = "ALL" };
                    var document = blank.WithCell(0, 0, tile);
                    var screenSize = CellSize / EditorGUIUtility.pixelsPerPoint;
                    RenderTexture.active = view.Render(document, state, Vector2.one * screenSize);
                    var rect = Slot(i);
                    atlas.ReadPixels(new Rect(0, 0, CellSize, CellSize), rect.x, rect.y, false);
                    RenderTexture.active = active;
                }
                atlas.Apply();
                File.WriteAllBytes(Path.Combine(folder, atlasFile), atlas.EncodeToPNG());
                File.WriteAllText(Path.Combine(folder, indexFile), new JArray(entries.Select(entry => entry.Id)).ToString(Formatting.Indented));
            }
            finally
            {
                RenderTexture.active = active;
                UnityEngine.Object.DestroyImmediate(atlas);
            }
        }
    }
}
