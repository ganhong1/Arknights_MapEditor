#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Catalog
{
    internal enum OfflineArtCategory { Scene, Surface, Model, Effect, Lighting, Camera }

    internal sealed class OfflineArtEntry
    {
        internal string Key { get; }
        internal string UnityPath { get; }
        internal string Bundle { get; }
        internal string SearchTerms { get; }
        internal string Extension => Path.GetExtension(UnityPath).ToLowerInvariant();
        internal OfflineArtCategory Category { get; }
        internal bool RequiresMetadata => Category == OfflineArtCategory.Camera ||
            (Category == OfflineArtCategory.Lighting && Extension == ".asset");

        internal string SourceType => Category switch
        {
            OfflineArtCategory.Camera => "镜头控制器",
            OfflineArtCategory.Scene => "Unity 场景",
            OfflineArtCategory.Model => "模型或预制体",
            OfflineArtCategory.Effect => "特效预制体",
            OfflineArtCategory.Lighting => RequiresMetadata ? "环境配置" : "反射与天空贴图",
            _ => Extension switch
            {
                ".mat" => "材质",
                ".anim" or ".controller" => "动画",
                _ => "贴图"
            }
        };

        internal OfflineArtEntry(string key, string unityPath, string bundle, string kind, string searchTerms = "")
        {
            Key = key;
            UnityPath = unityPath;
            Bundle = bundle;
            SearchTerms = searchTerms;
            Category = kind switch
            {
                "scene" => OfflineArtCategory.Scene,
                "effect" => OfflineArtCategory.Effect,
                "camera" => OfflineArtCategory.Camera,
                "map-art" when Extension == ".fbx" || Extension == ".prefab" || Extension == ".obj" => OfflineArtCategory.Model,
                "map-art" when key.IndexOf("lightmap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    Extension == ".exr" || Extension == ".cubemap" || Extension == ".asset" => OfflineArtCategory.Lighting,
                "map-art" => OfflineArtCategory.Surface,
                _ => throw new InvalidDataException("未知美术资源类型 " + kind)
            };
        }
    }

    internal sealed class OfflineArtCatalog
    {
        internal const string Baseline = "PC 2.7.81";
        internal const string Terrain2791 = "PC 2.7.91";
        internal string Root { get; }
        internal string GameBaseline { get; }
        internal string ResourceVersion { get; }
        internal IReadOnlyList<OfflineArtEntry> Entries { get; }
        private readonly Dictionary<string, string[]> bundles;
        private readonly Dictionary<string, OfflineArtEntry> byKey;

        private OfflineArtCatalog(string root, string gameBaseline, string version, OfflineArtEntry[] entries,
            Dictionary<string, string[]> bundles, Dictionary<string, OfflineArtEntry> byKey)
        {
            Root = root;
            GameBaseline = gameBaseline;
            ResourceVersion = version;
            Entries = Array.AsReadOnly(entries);
            this.bundles = bundles;
            this.byKey = byKey;
        }

        internal static OfflineArtCatalog Open(string root)
        {
            root = Path.GetFullPath(root);
            var file = new FileInfo(Path.Combine(root, "catalog.json"));
            if (!file.Exists) throw new FileNotFoundException("美术包目录缺少 catalog.json。", file.FullName);
            if (file.Length > 32 * 1024 * 1024) throw new InvalidDataException("美术包目录文件过大。");
            if (!Directory.Exists(Path.Combine(root, "Bundles"))) throw new DirectoryNotFoundException("美术包缺少 Bundles 目录。");
            return Parse(File.ReadAllText(file.FullName), root);
        }

        internal static OfflineArtCatalog Parse(string json, string root)
        {
            var loadSettings = new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error };
            using var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 8, DateParseHandling = DateParseHandling.None };
            var data = JObject.Load(reader, loadSettings);
            var gameBaseline = Required(data, "gameBaseline");
            if (reader.Read() || !JToken.DeepEquals(data["formatVersion"], new JValue(1)) ||
                (gameBaseline != Baseline && gameBaseline != Terrain2791) ||
                Required(data, "unityVersion") != "2021.3.39f1" || Required(data, "platform") != "Windows" ||
                Required(data, "compression") != "LZ4")
                throw new InvalidDataException("美术包需要 PC 2.7.81 或 2.7.91、Windows、Unity 2021.3.39f1、LZ4 目录格式 1。");

            var bundles = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in Rows(data, "bundles"))
            {
                var path = ValidateBundlePath(Required(row, "path"));
                var dependencies = row["dependencies"] as JArray ?? throw new InvalidDataException("缺少依赖列表 " + path);
                if (!bundles.TryAdd(path, dependencies.Select(value =>
                    ValidateBundlePath(value.Type == JTokenType.String ? (string)value! : "")).ToArray()))
                    throw new InvalidDataException("重复资源包 " + path);
            }
            foreach (var dependency in bundles.Values.SelectMany(value => value))
                if (!bundles.ContainsKey(dependency)) throw new InvalidDataException("目录缺少依赖 " + dependency);

            var entries = new Dictionary<string, OfflineArtEntry>(StringComparer.Ordinal);
            foreach (var row in Rows(data, "assets"))
            {
                var entry = new OfflineArtEntry(Required(row, "key"), Required(row, "unityPath"),
                    ValidateBundlePath(Required(row, "bundle")), Required(row, "kind"), row.Value<string>("searchTerms") ?? "");
                if (!bundles.ContainsKey(entry.Bundle)) throw new InvalidDataException("目录缺少资源包 " + entry.Bundle);
                if (!entries.TryAdd(entry.Key, entry)) throw new InvalidDataException("重复美术资源 " + entry.Key);
            }
            return new OfflineArtCatalog(root, gameBaseline, Required(data, "resourceVersion"),
                entries.Values.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray(), bundles, entries);
        }

        internal bool TryGet(string key, out OfflineArtEntry entry) => byKey.TryGetValue(key, out entry!);

        internal string[] LoadOrder(IEnumerable<OfflineArtEntry> entries)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var entry in entries)
            {
                foreach (var path in bundles[entry.Bundle])
                    if (!string.Equals(path, entry.Bundle, StringComparison.OrdinalIgnoreCase) && seen.Add(path))
                        order.Add(path);
                if (seen.Add(entry.Bundle)) order.Add(entry.Bundle);
            }
            return order.ToArray();
        }

        internal string BundleFile(string path) => Path.Combine(Root, "Bundles", ValidateBundlePath(path));

        internal static string ValidateBundlePath(string path)
        {
            if (path.Length == 0 || path.IndexOfAny(new[] { '\\', ':', '\0', '*', '?', '"', '<', '>', '|' }) >= 0 ||
                path.Split('/').Any(segment => segment.Length == 0 || segment == "." || segment == ".." ||
                    segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal)))
                throw new InvalidDataException("美术包路径无效 " + path);
            return path;
        }

        private static string Required(JObject row, string key) =>
            row[key]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)row[key])
                ? (string)row[key]! : throw new InvalidDataException("缺少美术目录字段 " + key);

        private static IEnumerable<JObject> Rows(JObject data, string key) =>
            (data[key] as JArray ?? throw new InvalidDataException("缺少美术目录列表 " + key))
                .Select(item => item as JObject ?? throw new InvalidDataException("无效的美术目录记录 " + key));
    }
}
