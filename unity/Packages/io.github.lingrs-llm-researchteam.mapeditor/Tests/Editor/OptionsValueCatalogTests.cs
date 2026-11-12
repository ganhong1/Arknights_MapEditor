#nullable enable
using System.Linq;
using System;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Arknights.MapEditor.Tests
{
    public sealed class OptionsValueCatalogTests
    {
        [Test] public void StageSourcesKeepReplacementsAndSharedDocumentsUnderTheirStageIds()
        {
            var files = OptionsValueCatalog.LoadBundled().Sources.Where(source =>
                source.Kind == "ArknightsGameData" && (source.Path.EndsWith("/level_main_09-01.json") ||
                    source.Path.EndsWith("/level_rogue4_1-1.json") || source.Path.EndsWith("/level_rogue4_1-1_r1.json") ||
                    source.Path.EndsWith("/level_rogue4_1-1_r2.json"))).ToArray();
            var groups = OptionsStageSources.LoadBundled().GroupFiles(files);
            var rogue = groups.Single(group => group.Id == "ro4_n_1_1");
            Assert.That(rogue.Files.Select(file => file.Path).Distinct().Count(), Is.EqualTo(3));
            Assert.That(groups.Single(group => group.Id == "easy_09-01").Files.Single(),
                Is.SameAs(groups.Single(group => group.Id == "main_09-01").Files.Single()));
        }

        private static JObject Fixture()
        {
            var record = new JObject { ["key"] = "same", ["value"] = 1, ["valueStr"] = "", ["extension"] = true };
            JObject Snapshot(int? cost) => new JObject { ["state"] = "object", ["value"] = cost.HasValue ?
                new JObject { ["maxCost"] = cost.Value, ["configBlackBoard"] = new JArray { record.DeepClone() } } : new JObject() };
            JObject Source(int index, string kind, string path) => new JObject
            {
                ["path"] = path, ["source"] = kind, ["sourceRevision"] = kind == "ArknightsGameData" ? "revision" : null,
                ["sha256"] = new string((char)('a' + index), 64), ["documentId"] = new string((char)('a' + index), 64),
                ["levelId"] = "", ["mapId"] = "", ["options"] = index
            };
            JObject Sample(JToken value, int source, int primary) => new JObject
            { ["value"] = value, ["sources"] = new JArray(source), ["count"] = 1, ["primaryCount"] = primary };
            return new JObject
            {
                ["version"] = 1, ["revision"] = "revision", ["corpus"] = new JObject(),
                ["levels"] = new JArray { Source(0, "ArknightsGameData", "same.json"), Source(1, "PRTSMap-cache", "same.json"), Source(2, "ArknightsGameData", "missing.json") },
                ["options"] = new JArray { Snapshot(99), Snapshot(500), Snapshot(null) },
                ["fields"] = new JObject { ["maxCost"] = new JObject
                { ["samples"] = new JArray { Sample(new JValue(99), 0, 1), Sample(new JValue(500), 1, 0) }, ["recommended"] = new JArray(0), ["missingSources"] = new JArray(2) } },
                ["blackboard"] = new JObject { ["same"] = new JObject
                { ["samples"] = new JArray { new JObject { ["value"] = record, ["sources"] = new JArray(0, 1), ["count"] = 2, ["primaryCount"] = 1,
                    ["references"] = new JArray { new JArray(0, 0), new JArray(1, 0) } } }, ["recommended"] = new JArray(0) } }
            };
        }

        [Test] public void SamePathAcrossSourcesRemainsDistinctWithExactContextAndMissingRecords()
        {
            var catalog = OptionsValueCatalog.Parse(Fixture().ToString());
            Assert.That(catalog.Sources.Count, Is.EqualTo(3));
            Assert.That(catalog.Sources[0].Path, Is.EqualTo(catalog.Sources[1].Path));
            Assert.That(catalog.Sources[1].Revision, Is.Null);
            Assert.That(catalog.Field("maxCost")!.MissingSources[0].Index, Is.EqualTo(2));
            Assert.That(catalog.Field("maxCost")!.Samples[1].Sources[0].CopyOptions()["maxCost"]!.Value<int>(), Is.EqualTo(500));
            Assert.That(catalog.Field("maxCost")!.Recommended[0].PrimaryCount, Is.EqualTo(1));
        }

        [Test] public void BlackboardReferencesKeepCompleteRecordsAndCopiesCannotMutateCatalog()
        {
            var catalog = OptionsValueCatalog.Parse(Fixture().ToString());
            var sample = catalog.Blackboard("same")!.Samples[0];
            var copy = (JObject)sample.CopyValue(); copy["valueStr"] = "changed";
            Assert.That(sample.CopyValue()["valueStr"]!.Value<string>(), Is.Empty);
            Assert.That(sample.CopyValue()["extension"]!.Value<bool>(), Is.True);
            Assert.That(sample.Pointers(catalog.Sources[1]), Is.EqualTo("/options/configBlackBoard/0"));
            var options = (JObject)catalog.Sources[0].CopyOptions(); options.RemoveAll();
            Assert.That(catalog.Sources[0].CopyOptions()["maxCost"], Is.Not.Null);
        }

        [TestCase("source")]
        [TestCase("count")]
        [TestCase("primary")]
        [TestCase("reference")]
        [TestCase("recommendation")]
        public void CorruptRelationshipsAreRejected(string fault)
        {
            var root = Fixture();
            if (fault == "source") root["fields"]!["maxCost"]!["samples"]![0]!["sources"] = new JArray(100);
            if (fault == "count") root["fields"]!["maxCost"]!["samples"]![0]!["count"] = 2;
            if (fault == "primary") root["fields"]!["maxCost"]!["samples"]![1]!["primaryCount"] = 1;
            if (fault == "reference") root["blackboard"]!["same"]!["samples"]![0]!["references"] = new JArray { new JArray(0, 3) };
            if (fault == "recommendation") root["fields"]!["maxCost"]!["recommended"] = new JArray(1);
            Assert.Throws<FormatException>(() => OptionsValueCatalog.Parse(root.ToString()));
        }

        [Test] public void DuplicateObjectKeysAreRejected()
        {
            Assert.Throws<LevelDocumentException>(() => OptionsValueCatalog.Parse("{\"version\":1,\"version\":1}"));
        }

        [Test] public void FractionalSamplesPreserveOriginalLexemesWithoutDoubleRounding()
        {
            const string precise = "0.12345678901234567890123456789012345678900";
            var root = Fixture();
            root["options"]![0]!["value"]!["maxCost"] = new JRaw(precise);
            root["fields"]!["maxCost"]!["samples"]![0]!["value"] = new JRaw(precise);
            var sample = OptionsValueCatalog.Parse(root.ToString()).Field("maxCost")!.Samples[0];
            Assert.That(sample.CopyValue().Type, Is.EqualTo(JTokenType.Raw));
            Assert.That(sample.CopyValue().ToString(Formatting.None), Is.EqualTo(precise));
            Assert.That(sample.Sources[0].CopyOptions()["maxCost"]!.ToString(Formatting.None), Is.EqualTo(precise));
            Assert.That(sample.Matches(new JRaw("0.123456789012345678901234567890123456789")), Is.True);
            Assert.That(sample.Matches(new JRaw("0.123456789012345678901234567890123456788")), Is.False);
        }

        [TestCase("1.00", "1e0")]
        [TestCase("0.00100", "10e-4")]
        [TestCase("-120.000", "-1.2e2")]
        [TestCase("-0.000", "0e100")]
        public void FractionalEqualityNormalizesDecimalNotationExactly(string left, string right)
        {
            Assert.That(OptionsValueCatalog.ExactValuesEqual(new JRaw(left), new JRaw(right)), Is.True);
        }

        [Test] public void ObservedNumberTypesRemainDistinctWhileFloatAndRawCanMatch()
        {
            Assert.That(OptionsValueCatalog.ExactValuesEqual(new JRaw("1.00"), new JValue(1.0)), Is.True);
            Assert.That(OptionsValueCatalog.ExactValuesEqual(new JRaw("1.00"), new JValue(1)), Is.False);
            Assert.That(OptionsValueCatalog.ExactValuesEqual(new JRaw("1.0000000000000000000000000001"), new JValue(1.0)), Is.False);
            Assert.That(OptionsValueCatalog.ExactValuesEqual(new JRaw("true"), new JValue(true)), Is.False);
        }

        [Test] public void SourceValidationAcceptsEquivalentFractionalSpellingsButRejectsChangedPreciseValue()
        {
            var root = Fixture();
            root["options"]![0]!["value"]!["maxCost"] = new JRaw("1.00");
            root["fields"]!["maxCost"]!["samples"]![0]!["value"] = new JRaw("1e0");
            Assert.DoesNotThrow(() => OptionsValueCatalog.Parse(root.ToString()));
            root["fields"]!["maxCost"]!["samples"]![0]!["value"] = new JRaw("1.0000000000000000000000000001");
            Assert.Throws<FormatException>(() => OptionsValueCatalog.Parse(root.ToString()));
        }
    }
}
