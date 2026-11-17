using System;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Arknights.MapEditor.Tests.Core
{
    public sealed class MapRulesTests
    {
        private const string Edge = "{\"pos\":{\"row\":1,\"col\":2},\"direction\":\"UP\",\"blockMask\":\"NONE\"}";
        [Test]
        public void AddIsImmutableAndRetainsBothDuplicateAndReciprocalEdges()
        {
            var original = LevelDocument.Create(4, 3);
            var edited = original.AddMapRule("blockEdges", Edge).AddMapRule("blockEdges", Edge)
                .AddMapRule("blockEdges", "{\"pos\":{\"row\":2,\"col\":2},\"direction\":\"DOWN\",\"blockMask\":1}");
            Assert.That(original.GetMapRules("blockEdges"), Is.Null);
            Assert.That(((JArray)edited.GetMapRules("blockEdges")).Count, Is.EqualTo(3));
            Assert.That(edited.GetMapRules("blockEdges")[0]["blockMask"].Value<string>(), Is.EqualTo("NONE"));
        }

        [Test]
        public void NullMissingAndEmptyAreDistinctAndRoundTrip()
        {
            var original = LevelDocument.Create(3, 3);
            var nul = original.SetMapRules("blockEdges", "null");
            var empty = nul.SetMapRules("blockEdges", "[]");
            Assert.That(nul.GetMapRules("blockEdges").Type, Is.EqualTo(JTokenType.Null));
            Assert.That(((JArray)empty.GetMapRules("blockEdges")).Count, Is.Zero);
            Assert.That(empty.RemoveMapRules("blockEdges").GetMapRules("blockEdges"), Is.Null);
            Assert.That(LevelDocument.Parse(nul.ToJson()).GetMapRules("blockEdges").Type, Is.EqualTo(JTokenType.Null));
            Assert.That(original.SetMapRules("tilesDisallowToLocate", "null").RemoveMapRules("tilesDisallowToLocate").GetMapRules("tilesDisallowToLocate"), Is.Null);
        }

        [Test]
        public void PatchRetainsUnknownFieldsAndNumericRepresentation()
        {
            var original = LevelDocument.Create(4, 3).AddMapRule("blockEdges",
                "{\"pos\":{\"row\":1,\"col\":2,\"future\":true},\"direction\":0,\"blockMask\":3,\"extension\":{\"x\":1.234567890123456789}}");
            var edited = original.PatchMapRule("blockEdges", 0, JObject.Parse("{\"pos\":{\"col\":3}}"));
            var item = edited.GetMapRules("blockEdges")[0];
            Assert.That(item["pos"]["future"].Value<bool>(), Is.True);
            Assert.That(item["direction"].Type, Is.EqualTo(JTokenType.Integer));
            StringAssert.Contains("1.234567890123456789", edited.ToJson());
            Assert.That(original.GetMapRules("blockEdges")[0]["pos"]["col"].Value<int>(), Is.EqualTo(2));
        }

        [Test]
        public void UntouchedUnknownImportedValuesSurvivePatchAndDeletion()
        {
            var original = LevelDocument.Create(4, 3).WithMapDataField("blockEdges",
                "[{\"pos\":{\"row\":1,\"col\":2},\"direction\":99,\"blockMask\":\"FUTURE\"},null]");
            var edited = original.PatchMapRule("blockEdges", 0, JObject.Parse("{\"pos\":{\"col\":1}}"));
            Assert.That(edited.GetMapRules("blockEdges")[0]["direction"].Value<int>(), Is.EqualTo(99));
            Assert.That(edited.DeleteMapRule("blockEdges", 0).GetMapRules("blockEdges")[0].Type, Is.EqualTo(JTokenType.Null));
            Assert.Throws<LevelDocumentException>(() => edited.PatchMapRule("blockEdges", 0, JObject.Parse("{\"direction\":100}")));
        }

        [TestCase("{\"pos\":{\"row\":-1,\"col\":0},\"direction\":0,\"blockMask\":1}")]
        [TestCase("{\"pos\":{\"row\":0,\"col\":4},\"direction\":0,\"blockMask\":1}")]
        [TestCase("{\"pos\":{\"row\":0.5,\"col\":0},\"direction\":0,\"blockMask\":1}")]
        [TestCase("{\"pos\":{\"row\":0,\"col\":0},\"direction\":4,\"blockMask\":1}")]
        [TestCase("{\"pos\":{\"row\":0,\"col\":0},\"direction\":0,\"blockMask\":-1}")]
        [TestCase("{\"pos\":{\"row\":0,\"col\":0},\"direction\":\"up\",\"blockMask\":1}")]
        [TestCase("null")]
        [TestCase("{}")]
        public void InvalidNewEdgesAreRejected(string json) => Assert.Throws<LevelDocumentException>(() => LevelDocument.Create(4, 3).AddMapRule("blockEdges", json));

        [Test]
        public void DisallowedCoordinatesStaySeparateFromSharedTileData()
        {
            var original = LevelDocument.Create(4, 3);
            var edited = original.AddMapRule("tilesDisallowToLocate", "{\"row\":1,\"col\":2,\"future\":true}");
            Assert.That(JToken.DeepEquals(original.GetSection("mapData"), edited.GetSection("mapData")), Is.True);
            Assert.That(edited.GetMapRules("tilesDisallowToLocate")[0]["future"].Value<bool>(), Is.True);
            Assert.Throws<LevelDocumentException>(() => edited.AddMapRule("tilesDisallowToLocate", "{\"row\":3,\"col\":0}"));
        }

        [TestCase("blockEdges")]
        [TestCase("tilesDisallowToLocate")]
        public void CropRequiresExplicitRuleRemoval(string field)
        {
            var edited = LevelDocument.Create(4, 3).AddMapRule(field, field == "blockEdges" ? Edge : "{\"row\":1,\"col\":2}");
            Assert.Throws<LevelDocumentException>(() => edited.Resize(2, 3));
            Assert.That(edited.DeleteMapRule(field, 0).Resize(2, 3).Width, Is.EqualTo(2));
            Assert.That(JToken.DeepEquals(edited.Resize(5, 4).GetMapRules(field), edited.GetMapRules(field)), Is.True);
        }

        [Test]
        public void GetRulesIsDetachedAndInvalidIndexDoesNotMutate()
        {
            var doc = LevelDocument.Create(4, 3).AddMapRule("blockEdges", Edge);
            ((JArray)doc.GetMapRules("blockEdges")).Clear();
            Assert.That(((JArray)doc.GetMapRules("blockEdges")).Count, Is.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.DeleteMapRule("blockEdges", 1));
            Assert.Throws<ArgumentException>(() => doc.GetMapRules("map"));
        }

        [Test]
        public void DocumentUndoRedoRestoresExactRuleState()
        {
            var session = ScriptableObject.CreateInstance<DocumentSession>();
            try
            {
                session.UseLocalHistory();
                session.Load(LevelDocument.Create(4, 3).ToJson(), "", "");
                var before = session.Json;
                session.Edit(session.Document.AddMapRule("blockEdges", Edge), "添加阻挡边");
                var after = session.Json;
                session.StepHistory(false); Assert.That(session.Json, Is.EqualTo(before));
                session.StepHistory(true); Assert.That(session.Json, Is.EqualTo(after));
            }
            finally { UnityEngine.Object.DestroyImmediate(session); }
        }
    }
}
