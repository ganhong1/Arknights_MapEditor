#nullable enable
using System;
using Newtonsoft.Json;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Catalog
{
    internal sealed class EnemyPortraitCatalog
    {
        private static EnemyPortraitCatalog? bundled;
        private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
        private readonly Dictionary<int, Texture2D> atlases = new();
        private readonly int pageSize;

        internal sealed class Portrait
        {
            internal int Page { get; }
            internal Rect Uv { get; }
            internal string Kind { get; }
            internal string Asset { get; }
            internal string Bundle { get; }
            internal string Sha256 { get; }
            internal bool LightBackground { get; }

            internal Portrait(JObject data, JObject images, int pageSize, int cellSize)
            {
                Sha256 = (string)data["image"]!;
                var position = images[Sha256] as JObject ?? throw new FormatException("敌人头像缺少图集位置。");
                Page = (int)position["page"]!;
                var x = (int)position["x"]!;
                var y = (int)position["y"]!;
                if (Page < 0 || x < 0 || y < 0 || x > pageSize - cellSize || y > pageSize - cellSize)
                    throw new FormatException("敌人头像图集位置越界。");
                Uv = new Rect((float)x / pageSize, (float)y / pageSize, (float)cellSize / pageSize, (float)cellSize / pageSize);
                Kind = (string)data["kind"]!;
                Asset = (string)data["asset"]!;
                Bundle = (string)data["bundle"]!;
                LightBackground = (bool?)data["lightBackground"] == true;
            }

            internal string SourceLabel => Kind switch
            {
                "handbook" => "原版敌方图鉴头像",
                "activity" => "原版活动战斗图标",
                "boss" => "原版首领血条图标",
                "avatar" => "原版头像",
                "prefab" => "关联预制体的原版头像",
                "spine" => "原版 Spine 缩略图",
                "mesh" => "原版模型缩略图",
                "effect" => "原版特效代表帧",
                "effect-texture" => "原版特效贴图",
                "mechanism-effect" => "关联装置的原版特效代表帧",
                "summoner" => "召唤者的原版头像",
                _ => throw new FormatException("未知敌人头像来源：" + Kind)
            };
        }

        private sealed class Entry
        {
            internal Portrait Default { get; }
            internal Dictionary<string, Portrait> Prefabs { get; } = new(StringComparer.Ordinal);

            internal Entry(JObject data, JObject images, int pageSize, int cellSize)
            {
                Default = new Portrait((JObject)data["default"]!, images, pageSize, cellSize);
                foreach (var pair in ((JObject)data["prefabs"]!).Properties())
                    Prefabs.Add(pair.Name, new Portrait((JObject)pair.Value, images, pageSize, cellSize));
            }
        }

        private static readonly EnemyPortraitCatalog Empty = new EnemyPortraitCatalog();

        private EnemyPortraitCatalog() { }

        internal static EnemyPortraitCatalog LoadOptional(Func<EnemyPortraitCatalog>? load = null)
        {
            try { return (load ?? LoadBundled)(); }
            catch (Exception error) when (error is JsonException || error is FormatException ||
                error is InvalidOperationException || error is ArgumentException || error is InvalidCastException ||
                error is NullReferenceException || error is OverflowException)
            {
                Editor.PluginLog.Failure("catalog.enemy-portraits", error);
                return Empty;
            }
        }

        private EnemyPortraitCatalog(JObject root)
        {
            if ((int?)root["version"] != 1 || (string?)root["revision"] != EnemyCatalog.Revision ||
                root["images"] is not JObject images || root["entries"] is not JObject data)
                throw new FormatException("敌人头像索引版本不匹配。");
            pageSize = (int)root["pageSize"]!;
            var cellSize = (int)root["cellSize"]!;
            if (cellSize <= 0 || pageSize < cellSize) throw new FormatException("敌人头像尺寸无效。");
            foreach (var pair in data.Properties())
                entries.Add(pair.Name, new Entry((JObject)pair.Value, images, pageSize, cellSize));
        }

        internal static EnemyPortraitCatalog LoadBundled()
        {
            if (bundled != null) return bundled;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Editor.PrtsObjectView.CatalogRoot + "enemy-portraits.json");
            if (asset == null) throw new InvalidOperationException("缺少敌人头像索引。");
            return bundled = new EnemyPortraitCatalog(JObject.Parse(asset.text));
        }

        internal Portrait? Find(string id, string? prefabKey = null)
        {
            if (!entries.TryGetValue(id, out var entry)) return null;
            if (prefabKey == null || entry.Prefabs.Count == 0) return entry.Default;
            return entry.Prefabs.TryGetValue(prefabKey, out var portrait) ? portrait : null;
        }

        internal Texture2D? Texture(Portrait portrait)
        {
            if (atlases.TryGetValue(portrait.Page, out var atlas)) return atlas;
            atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(
                Editor.PrtsObjectView.CatalogRoot + $"enemy-portraits-{portrait.Page}.png");
            if (atlas == null || atlas.width != pageSize || atlas.height != pageSize) return null;
            atlases.Add(portrait.Page, atlas);
            return atlas;
        }
    }
}
