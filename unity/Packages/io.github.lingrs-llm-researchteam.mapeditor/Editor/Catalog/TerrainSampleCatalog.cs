#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Catalog
{
    internal sealed class TerrainSampleCatalog
    {
        private const string AssetPath =
            "Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/Catalog/terrain-samples.json";
        private static TextAsset? loadedAsset;
        private static TerrainSampleCatalog? cached;
        private static TextAsset? newerAsset;
        private static TerrainSampleCatalog? newer;
        internal const string ExpansionRevision = "26-11-13-06-37-23-89286612";
        private readonly string[] levelPaths;
        private readonly JObject tiles;
        internal string Revision { get; }

        private TerrainSampleCatalog(TextAsset asset)
        {
            using var input = new StringReader(asset.text);
            using var reader = new JsonTextReader(input) { DateParseHandling = DateParseHandling.None };
            var root = JObject.Load(reader, new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            });
            if (root.Value<int>("version") != 1 || !(root["levels"] is JArray levels) ||
                !(root["tiles"] is JObject entries))
                throw new FormatException("Invalid terrain sample catalog.");
            Revision = root.Value<string>("revision") ?? throw new FormatException("Missing terrain sample revision.");
            levelPaths = levels.Values<string>().Select(path => path!).ToArray();
            tiles = entries;
        }

        internal static TerrainSampleCatalog LoadBundled()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetPath);
            if (asset == null) throw new System.IO.FileNotFoundException("Missing terrain sample catalog.", AssetPath);
            if (cached != null && loadedAsset == asset) return cached;
            var result = new TerrainSampleCatalog(asset);
            loadedAsset = asset;
            cached = result;
            return result;
        }

        internal static TerrainSampleCatalog Load2791()
        {
            const string path = Editor.PrtsObjectView.CatalogRoot + "terrain-samples-2791.json";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null) throw new FileNotFoundException("Missing 2.7.91 terrain samples.", path);
            if (newer != null && newerAsset == asset) return newer;
            var result = new TerrainSampleCatalog(asset);
            if (result.Revision != ExpansionRevision) throw new FormatException("Invalid 2.7.91 terrain sample revision.");
            newerAsset = asset;
            return newer = result;
        }

        internal bool Contains(string id) => tiles[id] != null;

        internal bool HasRecommendation(string id) => tiles[id]?["recommended"] is JArray items && items.Count > 0;

        internal JObject? Default(string id)
        {
            var group = tiles[id];
            if (!(group?["recommended"] is JArray indices) || indices.Count == 0) return null;
            return (JObject)group["samples"]![indices[0]!.Value<int>()]!["tileData"]!.DeepClone();
        }

        internal IReadOnlyList<Sample> Samples(string id) => tiles[id]?["samples"] is JArray rows
            ? rows.OfType<JObject>().Select((row, index) => new Sample(row, index + 1, levelPaths)).ToArray()
            : Array.Empty<Sample>();

        internal int[] Recommended(string id) => tiles[id]?["recommended"] is JArray indices
            ? indices.Values<int>().ToArray() : Array.Empty<int>();

        internal sealed class Sample
        {
            private readonly JObject row;
            private readonly string[] levels;
            internal int Number { get; }
            internal int SourceCount => ((JArray)row["sources"]!).Count;
            internal int ExampleIndex => row.Value<int>("exampleIndex");
            internal string ExamplePath => levels[row["sources"]![0]!.Value<int>()];
            internal string Compact => row["tileData"]!.ToString(Formatting.None);
            internal JObject CopyTile() => (JObject)row["tileData"]!.DeepClone();
            internal IEnumerable<string> SourcePaths => row["sources"]!.Values<int>().Select(index => levels[index]);

            internal Sample(JObject row, int number, string[] levels)
            {
                this.row = row;
                this.levels = levels;
                Number = number;
            }
        }
    }
}
