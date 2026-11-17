#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Newtonsoft.Json.Linq;

namespace Arknights.MapEditor.Editor
{
    internal sealed class EnemyParameterSamples
    {
        private readonly EnemyCatalog catalog;
        private readonly EnemyCatalog.Entry? entry;
        private (JObject Value, string Source)[]? records;

        internal EnemyParameterSamples(EnemyCatalog catalog, EnemyCatalog.Entry? entry)
        {
            this.catalog = catalog;
            this.entry = entry;
        }

        internal IEnumerable<(JObject Value, string Source)> Skills() => Distinct(
            Records().SelectMany(item => (item.Value["skills"] as JArray ?? new JArray()).OfType<JObject>()
                .Where(skill => skill["prefabKey"]?.Type == JTokenType.String)
                .Select(skill => (skill, item.Source))));

        internal IEnumerable<(JObject Value, string Source)> Blackboard(string? skill) => Distinct(
            (skill == null ? Records().Select(item => (Board: item.Value["talentBlackboard"], item.Source)) :
                Skills().Where(item => (string?)item.Value["prefabKey"] == skill)
                    .Select(item => (Board: item.Value["blackboard"], item.Source)))
            .SelectMany(item => (item.Board as JArray ?? new JArray()).OfType<JObject>()
                .Where(value => value["key"]?.Type == JTokenType.String).Select(value => (value, item.Source))));

        private (JObject Value, string Source)[] Records()
        {
            if (records != null) return records;
            if (entry == null) return records = Array.Empty<(JObject, string)>();
            var details = catalog.Details(entry);
            IEnumerable<JObject> Read(string key) => (details[key] as JArray ?? new JArray()).OfType<JObject>();
            string Source(JObject row, string label)
            {
                var paths = (row["origins"] as JArray ?? new JArray()).OfType<JObject>()
                    .SelectMany(catalog.SourceLabels).Distinct().Take(2).ToArray();
                return paths.Length == 0 ? label : label + " · " + string.Join(" / ", paths);
            }
            return records = Read("databaseLevels").Where(row => row["enemyData"] is JObject)
                .Select(row => ((JObject)row["enemyData"]!, "2.7.81 数据库 Lv." + row["level"]))
                .Concat(Read("inline").Where(row => row["record"] is JObject)
                    .Select((row, index) => ((JObject)row["record"]!, Source(row, "原版内联 #" + (index + 1)))))
                .Concat(Read("references").Where(row => row["record"]?["overwrittenData"] is JObject)
                    .Select((row, index) => ((JObject)row["record"]!["overwrittenData"]!, Source(row, "原版覆盖 #" + (index + 1))))).ToArray();
        }

        private static IEnumerable<(JObject Value, string Source)> Distinct(IEnumerable<(JObject Value, string Source)> items)
        {
            var seen = new HashSet<JToken>(JToken.EqualityComparer);
            return items.Where(item => seen.Add(item.Value));
        }
    }
}
