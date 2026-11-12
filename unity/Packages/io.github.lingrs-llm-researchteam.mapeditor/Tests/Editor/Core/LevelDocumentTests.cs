#nullable enable
using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Arknights.MapEditor.Core;

namespace Arknights.MapEditor.Tests.Core
{
    public sealed class LevelDocumentTests
    {
        private const string Map = "\"mapData\":{\"map\":[[0,1],[1,0]],\"tiles\":[{\"tileKey\":\"tile_road\"},{\"tileKey\":\"tile_wall\"}]}";

        [Test]
        public void NewDocumentIncludesConstructorOptionsAndEachMapOwnsItsValues()
        {
            var first = LevelDocument.Create(10, 6);
            var original = first.ToJson();
            var expected = JObject.Parse(@"{
                'characterLimit':99,'maxLifePoint':15,'initialCost':3,'maxCost':99,
                'costIncreaseTime':1.0,'moveMultiplier':0.5,'steeringEnabled':true,
                'reachableCheckIgnoreStartTile':false,'isTrainingLevel':false,
                'isHardTrainingLevel':false,'isPredefinedCardsSelectable':false,
                'displayRestTime':false,'maxPlayTime':-1.0,'functionDisableMask':0,
                'configBlackBoard':null}");
            Assert.That(JToken.DeepEquals(JObject.Parse(original)["options"], expected), Is.True);
            var changed = first.WithSection("options", "{\"initialCost\":20}");
            Assert.That(first.ToJson(), Is.EqualTo(original));
            Assert.That(JToken.DeepEquals(JObject.Parse(LevelDocument.Create(3, 4).ToJson())["options"], expected), Is.True);
            Assert.That(changed.GetSection("options")!["initialCost"]!.Value<int>(), Is.EqualTo(20));
        }

        [Test]
        public void ImportRetainsMissingNullAndPartialOptions()
        {
            Assert.That(LevelDocument.Parse("{" + Map + "}").GetSection("options"), Is.Null);
            Assert.That(LevelDocument.Parse("{" + Map + ",\"options\":null}").GetSection("options")!.Type, Is.EqualTo(JTokenType.Null));
            var partial = LevelDocument.Parse("{" + Map + ",\"options\":{\"initialCost\":7}}");
            Assert.That(((JObject)partial.GetSection("options")!).Count, Is.EqualTo(1));
        }

        [Test]
        public void RoundTripPreservesUnknownFieldsAndRepresentations()
        {
            var json = "{" + Map + ",\"future\":{\"n\":null,\"a\":[],\"enum\":17,\"large\":123456789012345678901234567890,\"precise\":0.12345678901234567890123456789012345},\"levelId\":\"α\"}";
            var output = LevelDocument.Parse(json).ToJson(false);
            Assert.That(output, Does.Contain("123456789012345678901234567890"));
            Assert.That(output, Does.Contain("0.12345678901234567890123456789012345"));
            var root = JObject.Parse(output);
            Assert.That(root["future"]!["n"]!.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(root["future"]!["a"]!.Type, Is.EqualTo(JTokenType.Array));
            Assert.That(root["options"], Is.Null);
            Assert.That(root["levelId"]!.Value<string>(), Is.EqualTo("α"));
        }

        [TestCase("{'a':1}")]
        [TestCase("{a:1}")]
        [TestCase("{\"a\":1,}")]
        [TestCase("{\"a\":[1,]}")]
        [TestCase("{\"a\":/*comment*/1}")]
        [TestCase("{\"a\":NaN}")]
        [TestCase("{\"a\":Infinity}")]
        [TestCase("{\"a\":undefined}")]
        [TestCase("{\"a\":01}")]
        [TestCase("{\"a\":+1}")]
        [TestCase("{\"a\":.1}")]
        [TestCase("{\"a\":1.}")]
        [TestCase("{\"a\":1} {}")]
        [TestCase("{\"a\":\"raw\nnewline\"}")]
        public void RejectsNonStrictJson(string json)
        {
            Assert.Throws<LevelDocumentException>(() => LevelDocument.Parse(json));
        }

        [Test]
        public void RejectsDuplicateKeysAtAnyDepth()
        {
            Assert.Throws<LevelDocumentException>(() => LevelDocument.Parse("{" + Map + ",\"future\":{\"x\":1,\"x\":2}}"));
        }

        [Test]
        public void CoordinatesAreBottomOriginAndReturnedCellsAreCopies()
        {
            var document = LevelDocument.Parse("{" + Map + "}");
            var cell = document.GetCell(0, 0);
            Assert.That(cell["tileKey"]!.Value<string>(), Is.EqualTo("tile_wall"));
            cell["tileKey"] = "mutated";
            Assert.That(document.GetCell(0, 0)["tileKey"]!.Value<string>(), Is.EqualTo("tile_wall"));
        }

        [Test]
        public void EditingSharedTileChangesOnlySelectedCellAndDoesNotMutateInputs()
        {
            var document = LevelDocument.Parse("{" + Map + ",\"future\":{\"keep\":42}}");
            var tile = new JObject { ["tileKey"] = "custom", ["blackboard"] = new JArray(1) };
            var changed = document.WithCell(0, 0, tile);
            tile["tileKey"] = "outside";
            Assert.That(changed.GetCell(0, 0)["tileKey"]!.Value<string>(), Is.EqualTo("custom"));
            Assert.That(changed.GetCell(1, 1)["tileKey"]!.Value<string>(), Is.EqualTo("tile_wall"));
            Assert.That(document.GetCell(0, 0)["tileKey"]!.Value<string>(), Is.EqualTo("tile_wall"));
            Assert.That(JObject.Parse(changed.ToJson())["future"]!["keep"]!.Value<int>(), Is.EqualTo(42));
        }

        [Test]
        public void EditingLevelIdPreservesOriginalAndUnknownContent()
        {
            var document = LevelDocument.Parse("{" + Map + ",\"future\":null}");
            var changed = document.WithLevelId("custom_01");
            Assert.That(document.LevelId, Is.Null);
            Assert.That(changed.LevelId, Is.EqualTo("custom_01"));
            Assert.That(JObject.Parse(changed.ToJson())["future"]!.Type, Is.EqualTo(JTokenType.Null));
        }

        [TestCase(1, 1)]
        [TestCase(7, 3)]
        [TestCase(256, 1)]
        public void CreateSupportsRectangularDimensions(int width, int height)
        {
            var document = LevelDocument.Create(width, height);
            Assert.That(document.Width, Is.EqualTo(width));
            Assert.That(document.Height, Is.EqualTo(height));
            Assert.That(document.GetCell(height - 1, width - 1)["tileKey"]!.Value<string>(), Is.EqualTo("tile_forbidden"));
            Assert.That(LevelDocument.Parse(document.ToJson()).Width, Is.EqualTo(width));
        }

        [TestCase(0, 3)]
        [TestCase(257, 1)]
        [TestCase(1, -1)]
        public void RejectsInvalidDimensions(int width, int height)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => LevelDocument.Create(width, height));
        }

        [TestCase("{\"mapData\":{\"map\":[[0],[0,0]],\"tiles\":[{}]}}")]
        [TestCase("{\"mapData\":{\"map\":[[2]],\"tiles\":[{}]}}")]
        [TestCase("{\"mapData\":{\"map\":[[-1]],\"tiles\":[{}]}}")]
        [TestCase("{\"mapData\":{\"map\":[[0.0]],\"tiles\":[{}]}}")]
        [TestCase("{\"mapData\":{\"map\":[[true]],\"tiles\":[{}]}}")]
        [TestCase("{\"mapData\":{\"map\":[[0]],\"tiles\":[null]}}")]
        [TestCase("[]")]
        [TestCase("{}")]
        public void RejectsInvalidMap(string json)
        {
            var error = Assert.Throws<LevelDocumentException>(() => LevelDocument.Parse(json));
            Assert.That(error!.Path, Is.Not.Empty);
        }

        [Test]
        public void RejectsExcessDepthAndSize()
        {
            Assert.Throws<LevelDocumentException>(() => LevelDocument.Parse(new string('[', 65) + "0" + new string(']', 65)));
            Assert.Throws<LevelDocumentException>(() => LevelDocument.Parse(new string(' ', LevelDocument.MaximumUtf8Bytes + 1)));
        }

        [Test]
        public void CoordinatesAndInvalidEditsFailWithoutChangingOriginal()
        {
            var document = LevelDocument.Create(2, 3);
            var before = document.ToJson();
            Assert.Throws<ArgumentOutOfRangeException>(() => document.GetCell(3, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => document.WithCell(-1, 0, new JObject()));
            Assert.Throws<LevelDocumentException>(() => document.WithCell(0, 0, new JObject { ["bad"] = double.NaN }));
            Assert.That(document.ToJson(), Is.EqualTo(before));
        }

        [Test]
        public void RawEditCannotBypassStrictJsonOrDuplicateKeyValidation()
        {
            var document = LevelDocument.Create(1, 1);
            Assert.Throws<LevelDocumentException>(() => document.WithCell(0, 0, new JObject { ["x"] = new JRaw("NaN") }));
            Assert.Throws<LevelDocumentException>(() => document.WithCell(0, 0, new JObject { ["x"] = new JRaw("{\"a\":1,\"a\":2}") }));
        }

        [TestCase("1e400")]
        [TestCase("-1e400")]
        public void RejectsOutOfFiniteRangeNumbers(string number)
        {
            Assert.Throws<LevelDocumentException>(() => LevelDocument.Parse("{" + Map + ",\"x\":" + number + "}"));
        }

        [Test]
        public void AcceptsEscapedStringsAndScientificNotationWithoutChangingTheirValue()
        {
            var document = LevelDocument.Parse("{" + Map + ",\"x\":-1.25E-3,\"s\":\"a\\\"b\\n\\u4e2d\"}");
            Assert.That(document.WithLevelId("x").ToJson(false), Does.Contain("-1.25E-3"));
            Assert.That(JObject.Parse(document.ToJson())["s"]!.Value<string>(), Is.EqualTo("a\"b\n中"));
        }

        [TestCase("\\ud800")]
        [TestCase("\\udc00")]
        [TestCase("\\ud800\\u0041")]
        [TestCase("\\ud800\\ud800")]
        [TestCase("\\udc00\\ud800")]
        [TestCase("\\ud800x\\udc00")]
        public void RejectsMalformedUnicodeDecodedFromEscape(string value)
        {
            Assert.Throws<LevelDocumentException>(() => LevelDocument.Parse("{" + Map + ",\"s\":\"" + value + "\"}"));
            Assert.Throws<LevelDocumentException>(() => LevelDocument.Parse("{" + Map + ",\"" + value + "\":0}"));
        }

        [Test]
        public void RejectsMixedRawAndEscapedSurrogates()
        {
            // Construct at runtime: invalid UTF-16 must not pass through attribute metadata encoding.
            foreach (var value in new[] { "\\ud800" + (char)0xdc00, (char)0xd800 + "\\udc00" })
                Assert.Throws<LevelDocumentException>(() => LevelDocument.Parse("{" + Map + ",\"s\":\"" + value + "\"}"));
        }

        [TestCase("\\ud83d\\ude00")]
        [TestCase("\\uD83D\\uDE00")]
        [TestCase("\ud83d\ude00")]
        public void PreservesValidSupplementaryUnicodeInKeysAndValues(string value)
        {
            var document = LevelDocument.Parse("{" + Map + ",\"" + value + "\":\"" + value + "\"}");
            var roundTrip = JObject.Parse(LevelDocument.Parse(document.ToJson()).ToJson());
            Assert.That(roundTrip["\ud83d\ude00"]!.Value<string>(), Is.EqualTo("\ud83d\ude00"));
        }
    }
}
