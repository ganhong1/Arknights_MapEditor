#nullable enable
using System;
using System.Linq;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Catalog
{
    internal sealed class OfflineArtReferenceMap
    {
        private static FinishedLevelCatalog? levels;
        internal LevelDocument Level { get; }
        internal string Title { get; }
        internal bool DirectCameraReference { get; }

        private OfflineArtReferenceMap(LevelDocument level, string title, bool direct)
        {
            Level = level;
            Title = title;
            DirectCameraReference = direct;
        }

        internal static OfflineArtReferenceMap Read(OfflineArtEntry entry)
        {
            levels ??= FinishedLevelCatalog.Load(FinishedLevelCatalog.BundledPath());
            var key = entry.Key;
            var preferred = key.EndsWith("/rogue_4_boss4", StringComparison.Ordinal) ? "ro4_b_8" :
                key.IndexOf("act49side_camera", StringComparison.Ordinal) >= 0 ? "act49side_01" :
                key.IndexOf("mainline_17_camera", StringComparison.Ordinal) >= 0 ? "main_17-01" : "main_05-10";

            const string chapterEight = "/level_main_08-";
            var index = key.IndexOf(chapterEight, StringComparison.Ordinal);
            if (index >= 0 && key.Length >= index + chapterEight.Length + 2)
                preferred = "main_08-" + key.Substring(index + chapterEight.Length, 2);

            var variants = levels.Groups.Where(group => !group.IsComponent).SelectMany(group => group.Variants);
            var chosen = variants.FirstOrDefault(candidate => candidate.Stage?.Id == preferred) ??
                variants.First(candidate => candidate.Stage?.Id == "main_05-10");
            using var archive = levels.OpenBodies();
            var json = levels.ReadBody(archive, chosen.Source.Body);
            var config = JObject.Parse(json);
            var camera = (string?)config["cameraPlugin"];
            var direct = !string.IsNullOrEmpty(camera) && key.EndsWith("/" + camera, StringComparison.Ordinal);
            return new OfflineArtReferenceMap(LevelDocument.Parse(json), chosen.Title, direct);
        }
    }
}
