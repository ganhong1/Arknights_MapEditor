#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Catalog
{
    internal static class GameStageLabel
    {
        internal static string Mode(string? mode, string code) => mode switch
        {
            "NORMAL" => "普通",
            "FOUR_STAR" => code.StartsWith("ISW", StringComparison.Ordinal) ? "紧急" : "突袭",
            "SIX_STAR" => "沙盘",
            "HARD" => "突袭",
            "TOUGH" => "磨难",
            "EASY" => "简单",
            _ => mode ?? ""
        };

        internal static string Title(params string?[] parts) => string.Join(" · ",
            parts.Where(part => !string.IsNullOrWhiteSpace(part)).Distinct(StringComparer.Ordinal));

        internal static string LevelId(string path) => Path.GetFileNameWithoutExtension(path);

        internal static IEnumerable<string> FromOrigins(JToken? sources, string stageField)
        {
            if (sources is not JArray entries) yield break;
            foreach (var source in entries.OfType<JObject>())
            {
                var stages = (source[stageField] as JArray)?.OfType<JArray>().ToArray() ?? Array.Empty<JArray>();
                if (stages.Length == 0)
                {
                    var id = LevelId((string?)source["path"] ?? "");
                    if (id.Length > 0) yield return id;
                }
                else
                {
                    foreach (var stage in stages)
                    {
                        var code = (string?)stage.ElementAtOrDefault(0) ?? "";
                        yield return Title(code, (string?)stage.ElementAtOrDefault(1),
                            Mode((string?)stage.ElementAtOrDefault(2), code));
                    }
                }
            }
        }
    }
}
