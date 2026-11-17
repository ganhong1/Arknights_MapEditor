#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Catalog
{
    internal sealed class OfflineArtMetadata
    {
        private readonly JObject records;

        private OfflineArtMetadata(JObject records) => this.records = records;

        internal static OfflineArtMetadata Open(OfflineArtCatalog catalog)
        {
            var path = Path.Combine(catalog.Root, "preview-metadata.json");
            var file = new FileInfo(path);
            if (!file.Exists)
                throw new FileNotFoundException("此美术包缺少预览参数索引 preview-metadata.json。", path);
            if (file.Length > 8 * 1024 * 1024)
                throw new InvalidDataException("美术预览参数索引过大。");
            using var hash = SHA256.Create();
            using var manifest = File.OpenRead(Path.Combine(catalog.Root, "catalog.json"));
            var expected = BitConverter.ToString(hash.ComputeHash(manifest)).Replace("-", "").ToLowerInvariant();
            using var reader = new JsonTextReader(File.OpenText(path)) { MaxDepth = 48, DateParseHandling = DateParseHandling.None };
            var root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (reader.Read() || !JToken.DeepEquals(root["formatVersion"], new JValue(1)) ||
                !JToken.DeepEquals(root["catalogSha256"], new JValue(expected)) ||
                !(root["records"] is JObject records))
                throw new InvalidDataException("美术预览参数索引与该包的目录版本不匹配。");
            return new OfflineArtMetadata(records);
        }

        internal bool TryGet(OfflineArtEntry entry, out OfflineArtRecord record)
        {
            if (records[entry.Key] is JObject item && item["fields"] is JObject fields &&
                item["type"]?.Type == JTokenType.String && item["objectId"]?.Type == JTokenType.String)
            {
                record = new OfflineArtRecord((string)item["type"]!, (string)item["objectId"]!, fields,
                    item["settings"] as JArray, item["constants"] as JObject);
                return true;
            }
            record = null!;
            return false;
        }
    }

    internal sealed class OfflineArtRecord
    {
        internal string Type { get; }
        internal string ObjectId { get; }
        internal JObject Fields { get; }
        internal JArray? Settings { get; }
        internal JObject? Constants { get; }

        internal OfflineArtRecord(string type, string objectId, JObject fields, JArray? settings, JObject? constants)
        {
            Type = type;
            ObjectId = objectId;
            Fields = fields;
            Settings = settings;
            Constants = constants;
        }

        internal static float Number(JToken? value, float fallback = 0f)
        {
            if (value?.Type != JTokenType.Float && value?.Type != JTokenType.Integer) return fallback;
            var number = value.Value<float>();
            return float.IsNaN(number) || float.IsInfinity(number) ? fallback : number;
        }

        internal static bool Enabled(JToken? value) =>
            value?.Type == JTokenType.Boolean ? value.Value<bool>() : Number(value) != 0;
    }
}
