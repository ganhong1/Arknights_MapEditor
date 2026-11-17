#nullable enable
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Arknights.MapEditor.Tests
{
    public sealed class EnemyCatalogResolutionTests
    {
        [Test]
        public void EveryPinnedDatabaseLevelRoundTripsThroughIndependentDefinition()
        {
            var catalog = EnemyCatalog.LoadBundled();
            var count = 0;
            foreach (var entry in catalog.Entries)
            {
                if (!entry.HasDatabase) continue;
                var levels = (JArray)catalog.Details(entry)["databaseLevels"]!;
                foreach (var level in entry.Levels)
                {
                    var original = EnemyDefinitionResolver.ResolveDatabaseLevel(entry.Id, levels, level);
                    var independent = EnemyDefinitionResolver.MaterializeIndependent(original, entry.Id + "_copy");
                    var restored = EnemyDefinitionResolver.ResolveReference(independent, null);
                    original.Remove("alias"); restored.Remove("alias");
                    Assert.That(JToken.DeepEquals(original, restored), Is.True, entry.Id + " / " + level);
                    count++;
                }
            }
            Assert.That(count, Is.EqualTo(2456));
        }
    }
}
