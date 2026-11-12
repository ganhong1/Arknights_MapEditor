using System;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Arknights.MapEditor.Tests.Core
{
    public sealed class MapCommandTests
    {
        [Test]
        public void BatchChangesAreAtomicAndRetainUneditedSharedTiles()
        {
            var original = LevelDocument.Create(3, 2);
            var tile = new JObject { ["tileKey"] = "road" };
            var changed = original.WithCells(new[] { new CellEdit(0, 0, tile), new CellEdit(1, 2, tile) });
            Assert.That(changed.GetCell(0, 0)["tileKey"].Value<string>(), Is.EqualTo("road"));
            Assert.That(changed.GetCell(1, 2)["tileKey"].Value<string>(), Is.EqualTo("road"));
            Assert.That(original.GetCell(0, 0)["tileKey"].Value<string>(), Is.EqualTo("tile_forbidden"));
            Assert.That(((JArray)changed.GetSection("mapData")["tiles"]).Count, Is.EqualTo(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => original.WithCells(new[] { new CellEdit(0, 0, tile), new CellEdit(2, 0, tile) }));
        }

        [Test]
        public void ResizePreservesBottomLeftCoordinatesAndUnknownMapFields()
        {
            var original = LevelDocument.Create(2, 2).WithCell(0, 1, new JObject { ["tileKey"] = "marker" });
            var map = original.GetSection("mapData");
            map["future"] = new JArray(3, 2, 1);
            original = original.WithSection("mapData", map.ToString());
            var enlarged = original.Resize(4, 3);
            Assert.That(enlarged.GetCell(0, 1)["tileKey"].Value<string>(), Is.EqualTo("marker"));
            Assert.That(enlarged.GetCell(2, 3)["tileKey"].Value<string>(), Is.EqualTo("tile_forbidden"));
            Assert.That(enlarged.GetSection("mapData")["future"].ToString(), Is.EqualTo(map["future"].ToString()));
            Assert.That(enlarged.Resize(2, 2).GetCell(0, 1)["tileKey"].Value<string>(), Is.EqualTo("marker"));
        }

        [Test]
        public void CroppingReferencedCoordinatesFailsInsteadOfClamping()
        {
            var original = LevelDocument.Create(4, 3).WithSection("routes", "[{\"startPosition\":{\"row\":0,\"col\":0},\"endPosition\":{\"row\":2,\"col\":3},\"checkpoints\":[]}]");
            var error = Assert.Throws<LevelDocumentException>(() => original.Resize(2, 2));
            Assert.That(error.Path, Does.Contain("endPosition"));
            Assert.That(original.Width, Is.EqualTo(4));
        }

        [Test]
        public void SectionEditDistinguishesNullMissingAndPreservesOtherSections()
        {
            var original = LevelDocument.Create(2, 2).WithSection("future", "{\"precise\":0.123456789012345678901234567890123}");
            var changed = original.WithSection("runes", "null");
            Assert.That(changed.GetSection("runes").Type, Is.EqualTo(JTokenType.Null));
            Assert.That(changed.RemoveSection("runes").GetSection("runes"), Is.Null);
            Assert.That(changed.ToJson(), Does.Contain("0.123456789012345678901234567890123"));
            Assert.Throws<LevelDocumentException>(() => original.WithSection("mapData", "null"));
            Assert.Throws<LevelDocumentException>(() => original.WithSection("future", "{\"x\":1,\"x\":2}"));
            Assert.Throws<LevelDocumentException>(() => original.WithSection("future", "1,\"injected\":2"));
        }

        [Test]
        public void BatchRejectsRawTokenInjectionAndKeepsUnknownTilePayloads()
        {
            var document = LevelDocument.Create(2, 2);
            Assert.Throws<LevelDocumentException>(() => document.WithCells(new[] { new CellEdit(0, 0, new JObject { ["x"] = new JRaw("1,\"injected\":2") }) }));
            var payload = new JObject { ["tileKey"] = "future_tile", ["future"] = new JObject { ["ordered"] = new JArray(3, 1, 2), ["empty"] = null } };
            var changed = document.WithCells(new[] { new CellEdit(1, 1, payload) });
            Assert.That(JToken.DeepEquals(changed.GetCell(1, 1), payload), Is.True);
        }

        [Test]
        public void CropChecksNestedPresetAndUnknownCoordinatesWithoutGuessingOffsets()
        {
            var document = LevelDocument.Create(4, 3).WithSection("predefines", "{\"tokenInsts\":[{\"position\":{\"row\":2,\"col\":3}}]}");
            Assert.Throws<LevelDocumentException>(() => document.Resize(2, 2));
            Assert.That(document.Resize(5, 4).GetSection("predefines").ToString(), Is.EqualTo(document.GetSection("predefines").ToString()));
            Assert.Throws<LevelDocumentException>(() => LevelDocument.Create(4, 3).WithSection("unknown", "{\"row\":\"ambiguous\",\"col\":3}").Resize(2, 2));
        }
    }
}
