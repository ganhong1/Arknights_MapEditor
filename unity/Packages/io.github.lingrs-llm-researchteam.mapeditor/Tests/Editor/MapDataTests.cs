using System;
using System.Linq;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Arknights.MapEditor.Tests.Core
{
    public sealed class MapDataTests
    {
        [Test]
        public void FieldEditPreservesUnknownValuesAndOtherLevelSections()
        {
            var original = LevelDocument.Create(2, 2)
                .WithSection("options", "{\"characterLimit\":3,\"future\":null}")
                .WithMapDataField("future", "{\"precise\":0.12345678901234567890123456789,\"ordered\":[3,1,2]}");
            var changed = original.WithMapDataField("tags", "[\"a\",\"a\",\"A\",null]");
            Assert.That(changed.GetSection("options").ToString(), Is.EqualTo(original.GetSection("options").ToString()));
            Assert.That(changed.GetSection("mapData")["future"].ToString(), Is.EqualTo(original.GetSection("mapData")["future"].ToString()));
            Assert.That(changed.ToJson(), Does.Contain("0.12345678901234567890123456789"));
            Assert.That(changed.GetSection("mapData")["tags"].ToString(Formatting.None), Is.EqualTo("[\"a\",\"a\",\"A\",null]"));
            Assert.That(original.GetSection("mapData")["tags"], Is.Null);
        }

        [Test]
        public void OptionalFieldMissingNullAndEmptyStayDistinct()
        {
            var original = LevelDocument.Create(1, 1);
            var explicitNull = original.WithMapDataField("effects", "null");
            var empty = explicitNull.WithMapDataField("effects", "[]");
            var absent = empty.RemoveMapDataField("effects");
            Assert.That(explicitNull.GetSection("mapData")["effects"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That(((JArray)empty.GetSection("mapData")["effects"]).Count, Is.Zero);
            Assert.That(absent.GetSection("mapData")["effects"], Is.Null);
            Assert.That(absent.ToJson(), Is.EqualTo(original.ToJson()));
            Assert.Throws<LevelDocumentException>(() => original.RemoveMapDataField("map"));
            Assert.Throws<LevelDocumentException>(() => original.RemoveMapDataField("tiles"));
        }

        [Test]
        public void CompleteMapDataReplacementAppliesGridAndConfigurationsAtomically()
        {
            var original = LevelDocument.Create(2, 1).WithSection("runes", "[{\"future\":7}]");
            var replacement = "{\"map\":[[1,0]],\"tiles\":[{\"tileKey\":\"a\"},{\"tileKey\":\"b\"}],\"tags\":null,\"future\":{\"empty\":\"\"}}";
            var changed = original.WithMapData(replacement);
            Assert.That((string)changed.GetCell(0, 0)["tileKey"], Is.EqualTo("b"));
            Assert.That((string)changed.GetCell(0, 1)["tileKey"], Is.EqualTo("a"));
            Assert.That(changed.GetSection("runes").ToString(), Is.EqualTo(original.GetSection("runes").ToString()));
            Assert.That(changed.GetSection("mapData")["future"]["empty"].Value<string>(), Is.Empty);
            Assert.That(original.GetCell(0, 0)["tileKey"].Value<string>(), Is.EqualTo("tile_forbidden"));
        }

        [Test]
        public void RawMapDataAndMapFieldCannotBypassResizeReferenceChecks()
        {
            var original = LevelDocument.Create(4, 3).WithSection("predefines",
                "{\"tokenInsts\":[{\"position\":{\"row\":2,\"col\":3}}]}");
            var smaller = LevelDocument.Create(2, 2).GetSection("mapData").ToString();
            var rawError = Assert.Throws<LevelDocumentException>(() => original.WithMapData(smaller));
            var fieldError = Assert.Throws<LevelDocumentException>(() => original.WithMapDataField("map", "[[0,0],[0,0]]"));
            Assert.That(rawError.Path, Does.Contain("predefines.tokenInsts"));
            Assert.That(fieldError.Path, Is.EqualTo(rawError.Path));
            Assert.That(original.Width, Is.EqualTo(4));
            Assert.That(original.Height, Is.EqualTo(3));
        }

        [Test]
        public void CropRequiresExplicitReferenceRepairBeforeApplyingMapChange()
        {
            var original = LevelDocument.Create(4, 3).WithSection("routes", "[{\"endPosition\":{\"row\":2,\"col\":3}}]");
            var repaired = original.WithSection("routes", "[{\"endPosition\":{\"row\":1,\"col\":1}}]");
            var cropped = repaired.WithMapDataField("map", "[[0,0],[0,0]]");
            Assert.That(cropped.Width, Is.EqualTo(2));
            Assert.That(cropped.Height, Is.EqualTo(2));
            Assert.That(cropped.GetSection("routes").ToString(), Is.EqualTo(repaired.GetSection("routes").ToString()));
        }

        [Test]
        public void CropAlsoChecksNewCoordinateShapedRecordsInReplacement()
        {
            var original = LevelDocument.Create(4, 3);
            var smaller = (JObject)LevelDocument.Create(2, 2).GetSection("mapData");
            smaller["future"] = new JObject { ["row"] = 2, ["col"] = 3 };
            var error = Assert.Throws<LevelDocumentException>(() => original.WithMapData(smaller.ToString()));
            Assert.That(error.Path, Does.Contain("mapData.future"));
        }

        [TestCase("[[0],[0,0]]")]
        [TestCase("[[1]]")]
        [TestCase("[[-1]]")]
        [TestCase("[[0.0]]")]
        [TestCase("[[\"0\"]]")]
        [TestCase("[]")]
        [TestCase("null")]
        public void GridEditRejectsMalformedOrUnboundIndices(string json)
        {
            var original = LevelDocument.Create(1, 1);
            var before = original.ToJson();
            Assert.Throws<LevelDocumentException>(() => original.WithMapDataField("map", json));
            Assert.That(original.ToJson(), Is.EqualTo(before));
        }

        [Test]
        public void NativeIndexBoundaryAllows32767AndRejects32768()
        {
            var original = LevelDocument.Create(1, 1);
            var configurations = new JArray(Enumerable.Range(0, short.MaxValue + 2).Select(_ => new JObject()));
            var map = new JObject { ["map"] = new JArray { new JArray((int)short.MaxValue) }, ["tiles"] = configurations };
            var accepted = original.WithMapData(map.ToString(Formatting.None));
            Assert.That((int)accepted.GetSection("mapData")["map"][0][0], Is.EqualTo(short.MaxValue));
            map["map"][0][0] = short.MaxValue + 1;
            var error = Assert.Throws<LevelDocumentException>(() => original.WithMapData(map.ToString(Formatting.None)));
            Assert.That(error.Path, Is.EqualTo("$.mapData.map[0][0]"));
        }

        [TestCase("tags", "[1]")]
        [TestCase("layerRects", "{}")]
        [TestCase("effects", "[true]")]
        [TestCase("blockEdges", "\"invalid\"")]
        public void ChangedKnownCollectionsRequireTheirDeclaredShape(string field, string json)
        {
            Assert.Throws<LevelDocumentException>(() => LevelDocument.Create(1, 1).WithMapDataField(field, json));
        }

        [Test]
        public void UnchangedLegacyInvalidOptionalFieldDoesNotBlockUnrelatedEdit()
        {
            var original = LevelDocument.Create(1, 1);
            var map = (JObject)original.GetSection("mapData");
            map["tags"] = "legacy-unknown-shape";
            original = original.WithSection("mapData", map.ToString());
            var changed = original.WithMapDataField("effects", "null");
            Assert.That((string)changed.GetSection("mapData")["tags"], Is.EqualTo("legacy-unknown-shape"));
        }

        [Test]
        public void FieldFragmentsUseStrictParserAndDoNotAcceptDuplicateOrInjectedMembers()
        {
            var original = LevelDocument.Create(1, 1);
            Assert.Throws<LevelDocumentException>(() => original.WithMapDataField("future", "{\"x\":1,\"x\":2}"));
            Assert.Throws<LevelDocumentException>(() => original.WithMapDataField("future", "1,\"injected\":2"));
        }

        [Test]
        public void SpatialWarningsNameUnverifiedRecordsWithoutRewritingThem()
        {
            var original = LevelDocument.Create(2, 2)
                .WithMapDataField("layerRects", "[\"unparsed rectangle\"]")
                .WithMapDataField("effects", "[{\"key\":\"future_effect\",\"offset\":{\"x\":9,\"y\":8,\"z\":7}}]")
                .WithMapDataField("blockEdges", "[{\"pos\":{\"row\":0,\"col\":0},\"direction\":99,\"blockMask\":1024}]");
            var before = original.ToJson();
            var warnings = original.GetMapDataSpatialWarnings();
            Assert.That(warnings.Any(message => message.Contains("layerRects")), Is.True);
            Assert.That(warnings.Any(message => message.Contains("effects")), Is.True);
            Assert.That(warnings.Any(message => message.Contains("blockEdges")), Is.True);
            Assert.That(original.ToJson(), Is.EqualTo(before));
            Assert.That(LevelDocument.Create(2, 2).GetMapDataSpatialWarnings(), Is.Empty);
        }
    }
}
