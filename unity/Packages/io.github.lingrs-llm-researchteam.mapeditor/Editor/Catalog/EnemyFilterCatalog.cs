#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Catalog
{
    internal sealed class EnemyFilterCatalog
    {
        internal sealed class Record
        {
            internal string Name { get; }
            internal string Number { get; }
            internal int? Order { get; }
            internal bool Public { get; }
            internal string Abilities { get; }
            internal string[] SearchText { get; }
            internal IReadOnlyList<Profile> Profiles { get; }

            internal Record(JObject data)
            {
                Name = (string)data["name"]!;
                Number = (string)data["number"]!;
                Order = (int?)data["order"];
                Public = (bool)data["public"]!;
                Abilities = (string)data["abilities"]!;
                SearchText = data["search"]!.Values<string>().OfType<string>().ToArray();
                Profiles = data["profiles"]!.OfType<JObject>().Select(value => new Profile(value)).ToArray();
            }
        }

        internal sealed class Profile
        {
            private readonly Dictionary<string, string[]> labels;

            internal Profile(JObject data) => labels = data.Properties().ToDictionary(
                property => property.Name, property => property.Value.Values<string>().OfType<string>().ToArray(), StringComparer.Ordinal);

            internal IReadOnlyList<string> Values(string group) =>
                labels.TryGetValue(group, out var values) ? values : Array.Empty<string>();
        }

        private static EnemyFilterCatalog? bundled;
        private readonly Dictionary<string, Record> entries;

        private EnemyFilterCatalog(JObject data)
        {
            if ((int?)data["version"] != 1 || (string?)data["revision"] != EnemyCatalog.Revision ||
                data["entries"] is not JObject source)
                throw new FormatException("敌人筛选数据版本不匹配。");
            entries = source.Properties().ToDictionary(
                pair => pair.Name, pair => new Record((JObject)pair.Value), StringComparer.Ordinal);
        }

        internal Record Get(string enemyId) => entries.TryGetValue(enemyId, out var value)
            ? value : throw new FormatException("缺少敌人筛选数据：" + enemyId);

        internal static EnemyFilterCatalog LoadBundled()
        {
            if (bundled != null) return bundled;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Editor.PrtsObjectView.CatalogRoot + "enemy-filters.json");
            if (asset == null) throw new InvalidOperationException("缺少敌人筛选索引。");
            return bundled = new EnemyFilterCatalog(JObject.Parse(asset.text));
        }
    }
}
