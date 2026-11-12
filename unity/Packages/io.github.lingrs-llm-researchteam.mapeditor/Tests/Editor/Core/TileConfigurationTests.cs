using System;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Arknights.MapEditor.Tests
{
    public sealed class TileConfigurationTests
    {
        private static JObject Road() { var tile = LevelDocument.Create(1, 1).GetCell(0, 0); tile["tileKey"] = "tile_road"; return tile; }

        [Test]
        public void SharedEditChangesEveryBindingAndPreservesUnknownFields()
        {
            var doc = LevelDocument.Create(3, 1).AddTileConfiguration(Road());
            doc = doc.BindTileConfiguration(1, new[] { (0, 0), (0, 2) }, false);
            var tile = Road(); tile["future"] = new JObject { ["value"] = 7 };
            var changed = doc.EditTileConfiguration(1, tile);
            Assert.That(changed.GetCell(0, 0)["future"]["value"].Value<int>(), Is.EqualTo(7));
            Assert.That(changed.GetCell(0, 2)["future"]["value"].Value<int>(), Is.EqualTo(7));
            Assert.That(changed.GetCell(0, 1)["tileKey"].Value<string>(), Is.EqualTo("tile_forbidden"));
            Assert.That(doc.GetCell(0, 0)["future"], Is.Null);
        }

        [Test]
        public void PlacementIsIndependentAndCleanupRemapsAllIndices()
        {
            var doc = LevelDocument.Create(2, 1).PlaceTileConfigurations(Road(), new[] { (0, 0), (0, 1) }, true);
            var map = doc.GetSection("mapData");
            Assert.That(((JArray)map["tiles"]).Count, Is.EqualTo(2));
            Assert.That(map["map"][0][0].Value<int>(), Is.Not.EqualTo(map["map"][0][1].Value<int>()));
            var tile = Road(); tile["tileKey"] = "tile_wall";
            var changed = doc.EditTileConfiguration(0, tile);
            Assert.That(changed.GetCell(0, 1)["tileKey"].Value<string>(), Is.EqualTo("tile_road"));
        }

        [Test]
        public void KeepOriginalRetainsUnboundConfigurationAndDeleteRejectsLiveReferences()
        {
            var doc = LevelDocument.Create(1, 1).PlaceTileConfigurations(Road(), new[] { (0, 0) }, false);
            Assert.That(((JArray)doc.GetSection("mapData")["tiles"]).Count, Is.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() => doc.DeleteTileConfiguration(1));
            var next = doc.DeleteTileConfiguration(0);
            Assert.That(next.GetCell(0, 0)["tileKey"].Value<string>(), Is.EqualTo("tile_road"));
            Assert.That(next.GetSection("mapData")["map"][0][0].Value<int>(), Is.Zero);
        }

        [Test]
        public void InvalidMultiBindingIsAtomicAndDuplicateCoordinatesDoNotCreateExtraTiles()
        {
            var doc = LevelDocument.Create(2, 1);
            var original = doc.ToJson();
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.PlaceTileConfigurations(Road(), new[] { (0, 0), (1, 0) }, true));
            Assert.That(doc.ToJson(), Is.EqualTo(original));
            var next = doc.PlaceTileConfigurations(Road(), new[] { (0, 0), (0, 0) }, false);
            Assert.That(((JArray)next.GetSection("mapData")["tiles"]).Count, Is.EqualTo(2));
        }
    }
}
