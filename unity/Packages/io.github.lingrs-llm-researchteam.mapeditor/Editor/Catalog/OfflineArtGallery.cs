#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Catalog
{
    internal enum OfflineArtGalleryLayer { Texture, Material, Model, Effect }

    internal sealed class OfflineArtSample
    {
        internal OfflineArtEntry Entry { get; }
        internal OfflineArtEntry? ModelMaterial { get; }
        internal OfflineArtGalleryLayer Layer { get; }
        internal int Column { get; }

        internal OfflineArtSample(OfflineArtEntry entry, OfflineArtEntry? modelMaterial, OfflineArtGalleryLayer layer, int column)
        {
            Entry = entry;
            ModelMaterial = modelMaterial;
            Layer = layer;
            Column = column;
        }
    }

    internal static class OfflineArtGallery
    {
        internal const string Title = "离线美术 · 分层抽样";
        internal const int Columns = 3;
        internal const int Rows = 4;

        // Verified candidates with known preview roles. Prefer paired source materials for background meshes.
        private static readonly (string Asset, string Material)[] Models =
        {
            ("arts/maps/map_ageaegir/background_mesh/bg_age", "arts/maps/map_ageaegir/res/materials/mt_age_common"),
            ("arts/maps/map_chernobog_a/background_mesh/bg_qcity", "arts/maps/map_chernobog_a/res/materials/mt_qcity_bg"),
            ("arts/maps/map_lm_center/background_mesh/bg_lmc", "arts/maps/map_lm_center/res/materials/mt_lmc_n_bg"),
            ("arts/maps/map_lm_center/trap/trap_crsbow", "arts/maps/map_lm_center/trap/mt_crsbow")
        };

        internal static IReadOnlyList<OfflineArtSample> Select(OfflineArtCatalog catalog)
        {
            var models = Models.Where(pair => catalog.TryGet(pair.Asset, out _) && catalog.TryGet(pair.Material, out _))
                .ToDictionary(pair => pair.Asset, pair => { catalog.TryGet(pair.Material, out var material); return material; },
                    StringComparer.Ordinal);
            var rows = new[]
            {
                catalog.Entries.Where(entry => entry.Category == OfflineArtCategory.Surface &&
                    entry.Key.StartsWith("arts/maps/map_lm_center/res/textures/", StringComparison.Ordinal) &&
                    (entry.Key.EndsWith("_a", StringComparison.Ordinal) || entry.Key.EndsWith("_bg", StringComparison.Ordinal))),
                catalog.Entries.Where(entry => entry.Category == OfflineArtCategory.Surface && entry.Extension == ".mat" &&
                    entry.Key.StartsWith("arts/maps/map_lm_center/res/materials/", StringComparison.Ordinal) &&
                    (entry.Key.EndsWith("_common", StringComparison.Ordinal) || entry.Key.EndsWith("_bg", StringComparison.Ordinal))),
                catalog.Entries.Where(entry => models.ContainsKey(entry.Key)),
                catalog.Entries.Where(entry => entry.Category == OfflineArtCategory.Effect &&
                    (entry.Key.EndsWith("/map_038_sign_glow_02", StringComparison.Ordinal) ||
                    entry.Key.EndsWith("/map_sand2_0010", StringComparison.Ordinal) ||
                    entry.Key.EndsWith("/mapeff_waterternado_01", StringComparison.Ordinal)))
            };

            var samples = new List<OfflineArtSample>();
            for (var layer = 0; layer < rows.Length; layer++)
            {
                var candidates = rows[layer].ToArray();
                var indices = SampleIndices(candidates.Length, Columns);
                for (var column = 0; column < indices.Length; column++)
                {
                    var entry = candidates[indices[column]];
                    models.TryGetValue(entry.Key, out var material);
                    samples.Add(new OfflineArtSample(entry, material, (OfflineArtGalleryLayer)layer, column));
                }
            }
            return samples.AsReadOnly();
        }

        internal static int[] SampleIndices(int count, int maximum)
        {
            var take = Math.Min(count, maximum);
            if (take <= 0) return Array.Empty<int>();
            if (take == 1) return new[] { 0 };
            return Enumerable.Range(0, take).Select(index => (int)((long)index * (count - 1) / (take - 1))).ToArray();
        }

        internal static LevelDocument CreateMap() => LevelDocument.Create(Columns, Rows).PlaceTileConfigurations(
            new JObject
            {
                ["tileKey"] = "tile_road", ["heightType"] = "LOWLAND", ["buildableType"] = "MELEE",
                ["passableMask"] = "ALL", ["playerSideMask"] = "ALL", ["blackboard"] = null, ["effects"] = null
            },
            from row in Enumerable.Range(0, Rows) from col in Enumerable.Range(0, Columns) select (row, col), true);
    }
}
