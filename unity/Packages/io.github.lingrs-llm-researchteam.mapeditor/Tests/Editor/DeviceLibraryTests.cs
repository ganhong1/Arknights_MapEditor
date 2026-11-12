#nullable enable
using System;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Tests
{
    public sealed class DeviceLibraryTests
    {

        [Test]
        public void EveryBundledEntryMatchesItsCountsAndPlacementFreeConfigurationsKeepOriginalSources()
        {
            var catalog = DeviceSampleCatalog.LoadBundled();
            var total = 0;
            foreach (var entry in catalog.Entries.Where(x => x.HasSamples))
            {
                var details = catalog.Load(entry.Id);
                foreach (var role in DeviceSampleCatalog.Roles)
                {
                    var samples = details.Samples(role);
                    Assert.That(samples.Count, Is.EqualTo(entry.Configurations(role)), entry.Id + "/" + role);
                    foreach (var sample in samples)
                    {
                        total++;
                        var config = sample.CopyRecord();
                        if (role == "tokenInsts")
                        {
                            Assert.That(config.Property("position"), Is.Null);
                            Assert.That(config.Property("direction"), Is.Null);
                        }
                        Assert.That(sample.SourcePaths.Count, Is.GreaterThan(0));
                    }
                }
            }
            Assert.That(total, Is.GreaterThan(2552));
            Assert.That(total, Is.LessThan(14003));
        }

        [Test]
        public void ConfigPreviewHasNoOriginalRecordSelectorAndPreservesAllPlacementOrigins()
        {
            var catalog = DeviceSampleCatalog.LoadBundled();
            var details = catalog.Load("trap_001_crate");
            var sample = details.Samples("tokenInsts").First(x => x.Originals.Count > 1);
            Assert.That(sample.Originals.Any(x => x.CopyRecord().Property("position") != null), Is.True);
            Assert.That(sample.SourcePaths.Count, Is.EqualTo(sample.Originals.SelectMany(x => x.SourcePaths).Distinct().Count()));
            var overlay = new DeviceDetailOverlay(catalog, (_, __) => true);
            overlay.Open(catalog.Entries.Single(x => x.Id == "trap_001_crate"), new Button());
            Assert.That(overlay.Q("device-original-choice"), Is.Null);
            Assert.That(overlay.Q<Foldout>("device-known-configurations").text, Does.StartWith("全部既有已知配置"));
        }

        [Test]
        public void CatalogIncludesBothRolesHistoricalEntriesAndNonTrapRecords()
        {
            var catalog = DeviceSampleCatalog.LoadBundled();
            Assert.That(catalog.Entries.Count, Is.EqualTo(847));
            Assert.That(catalog.Entries.Count(x => x.HasSamples), Is.EqualTo(403));
            Assert.That(catalog.Entries.Single(x => x.Id == "token_10053_radian_tower3").HasSamples, Is.True);
            Assert.That(catalog.Entries.Single(x => x.Id == "trap_004_iceblock").HasSamples, Is.False);
            var crate = catalog.Load("trap_001_crate");
            Assert.That(crate.Samples("tokenInsts").Count, Is.GreaterThan(0));
            Assert.That(crate.Samples("tokenCards").Count, Is.EqualTo(catalog.Entries.Single(x => x.Id == "trap_001_crate").Configurations("tokenCards")));
            foreach (var role in DeviceSampleCatalog.Roles)
            {
                var choices = crate.Recommended(role);
                Assert.That(choices.Length, Is.InRange(1, 3));
                Assert.That(choices.Distinct().Count(), Is.EqualTo(choices.Length));
            }
        }

        [Test]
        public void CopyingDoesNotChangeLibraryOrDiscardOriginalPlacementAndAliases()
        {
            var details = DeviceSampleCatalog.LoadBundled().Load("trap_007_ballis");
            var source = details.Samples("tokenInsts").First().Originals.First();
            var first = source.CopyRecord();
            var snapshot = first.DeepClone();
            Assert.That(first["position"], Is.Not.Null);
            Assert.That(first.Property("alias"), Is.Not.Null);
            first["inst"]!["level"] = 999;
            Assert.That(JToken.DeepEquals(source.CopyRecord(), snapshot), Is.True);
            Assert.That(source.SourcePaths.Distinct().Count(), Is.EqualTo(source.SourcePaths.Count));
        }

        [Test]
        public void DetailTabsKeepCurrentAndTotalOriginsSeparateAndNeverApplyOnBrowse()
        {
            var choices = 0;
            var catalog = DeviceSampleCatalog.LoadBundled();
            var detail = new DeviceDetailOverlay(catalog, (_, __) => { choices++; return true; });
            detail.Open(catalog.Entries.Single(x => x.Id == "trap_001_crate"), new Button());
            Assert.That(detail.Q("device-associations"), Is.Null);
            var card = detail.Q("device-role-tokenCards");
            Assert.That(card.Q("device-known-configurations"), Is.Not.Null);
            Assert.That(card.Q("device-current-source-browser"), Is.Not.Null);
            Assert.That(card.Q("device-total-source-browser"), Is.Not.Null);
            Assert.That(card.Q<TextField>("device-current-json").isReadOnly, Is.True);
            Assert.That(choices, Is.Zero);
        }

        [Test]
        public void SearchReusesToolboxFuseAndFindsNameAndStableId()
        {
            var catalog = DeviceSampleCatalog.LoadBundled();
            var search = catalog.CreateSearch();
            Assert.That(search.Filter(catalog.Entries, "障碍物", out var error).First().Id, Is.EqualTo("trap_001_crate"));
            Assert.That(error, Is.Null);
            Assert.That(search.Filter(catalog.Entries, "TRAP_001_CRATE", out _).First().Id, Is.EqualTo("trap_001_crate"));
            Assert.That(search.Filter(catalog.Entries, "zhangaiwu", out _).First().Id, Is.EqualTo("trap_001_crate"));
        }
    }
}
