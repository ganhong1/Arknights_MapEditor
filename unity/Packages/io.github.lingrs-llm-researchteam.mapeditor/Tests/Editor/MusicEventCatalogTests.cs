#nullable enable
using System.Linq;
using Arknights.MapEditor.Catalog;
using NUnit.Framework;

namespace Arknights.MapEditor.Editor.Tests
{
    public sealed class MusicEventCatalogTests
    {
        [TestCase("rglk1csghost", "傀儡的狂喜")]
        [TestCase("rglk1cswrtr", "行文之韵")]
        [TestCase("rl2boss2", "旅者//征服者")]
        [TestCase("rl2boss3", "Endospore")]
        public void MissingTriggerCanAuditionItsReferenceWithoutBecomingSelectable(string key, string title)
        {
            var catalog = MusicEventCatalog.LoadBundled();
            var opening = catalog.Find(true, key)!;
            var trigger = catalog.Find(false, key)!;
            Assert.That(opening.IsRegistered, Is.True);
            Assert.That(trigger.IsRegistered, Is.False);
            Assert.That(trigger.EventName, Is.EqualTo("battle.ON_CUSTOM_TRIGGER." + key));
            Assert.That(trigger.ReferenceEvent, Is.EqualTo(opening.EventName));
            Assert.That(trigger.Label, Is.EqualTo(title));
            Assert.That(trigger.HasFullPreview, Is.True);
            Assert.That(trigger.Intro!.Asset, Is.EqualTo(opening.Intro!.Asset));
            Assert.That(trigger.Loop!.Asset, Is.EqualTo(opening.Loop!.Asset));
            Assert.That(trigger.SuggestedKey, Is.Null);
            Assert.That(catalog.Choices(false).Any(entry => entry.Key == key), Is.False);
        }

        [Test]
        public void HeidiOpeningKeyOffersAnExplicitRegisteredReplacement()
        {
            var catalog = MusicEventCatalog.LoadBundled();
            var missing = catalog.Find(true, "bat_shatterpoint_02")!;
            Assert.That(missing.SuggestedKey, Is.EqualTo("shatterpoint_02"));
            var replacement = catalog.Find(true, missing.SuggestedKey!)!;
            Assert.That(missing.IsRegistered, Is.False);
            Assert.That(missing.HasFullPreview, Is.True);
            Assert.That(missing.ReferenceEvent, Is.EqualTo(replacement.EventName));
            Assert.That(replacement.IsRegistered, Is.True);
            Assert.That(replacement.Intro!.Asset, Does.EndWith("/m_bat_shatterpoint_02_intro"));
            Assert.That(replacement.Loop!.Asset, Does.EndWith("/m_bat_shatterpoint_02_loop"));
            Assert.That(catalog.Choices(true).Any(entry => entry.Key == missing.Key), Is.False);
        }

        [Test]
        public void NativeAliasAndDirectTriggerUseTheirRegisteredMusicBanks()
        {
            var catalog = MusicEventCatalog.LoadBundled();
            var alias = catalog.Find(false, "shield")!;
            var direct = catalog.Find(false, "talulhahard")!;
            Assert.That(alias.EventName, Is.EqualTo("battle.ON_CUSTOM_TRIGGER.shield"));
            Assert.That(alias.BankName, Is.EqualTo("battle.ON_GAME_READY.shield"));
            Assert.That(alias.IsRegistered && alias.HasFullPreview, Is.True);
            Assert.That(direct.BankName, Is.EqualTo(direct.EventName));
            Assert.That(direct.Delay, Is.EqualTo(12f));
            Assert.That(catalog.Choices(false), Does.Contain(alias));
            Assert.That(catalog.Choices(false), Does.Contain(direct));
            Assert.That(catalog.Find(false, "__unknown_editor_test_music__"), Is.Null);
        }
    }
}
