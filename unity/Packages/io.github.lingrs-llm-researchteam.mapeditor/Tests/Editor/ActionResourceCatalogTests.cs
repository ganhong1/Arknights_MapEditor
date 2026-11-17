#nullable enable
using System.Linq;
using Arknights.MapEditor.Catalog;
using NUnit.Framework;

namespace Arknights.MapEditor.Tests
{
    public sealed class ActionResourceCatalogTests
    {
        [Test]
        public void BundledCandidatesRetainResourceMatchingAndExactOperaContext()
        {
            var catalog = ActionResourceCatalog.LoadBundled();
            Assert.That(catalog.Find("STORY", "").Count, Is.EqualTo(625));
            Assert.That(catalog.Find("DIALOG", "").Count, Is.EqualTo(55));
            Assert.That(catalog.Find("TUTORIAL", "").Count, Is.Zero);
            var audio = catalog.Find("PLAY_BGM", "ignored");
            Assert.That(audio.Count, Is.EqualTo(24));
            Assert.That(audio.Count(item => item.Verified), Is.EqualTo(20));
            Assert.That(audio.Single(item => item.Key == "rl2boss2").Verified, Is.False);
            Assert.That(catalog.Find("PLAY_OPERA", "level_act46side_10").Any(item => item.Key == "init"), Is.True);
            Assert.That(catalog.Find("PLAY_OPERA", "rogue_4_boss4_formal").Any(item => item.Key == "init"), Is.True);
            Assert.That(catalog.Find("PLAY_OPERA", "missing").Count, Is.Zero);
            Assert.That(catalog.Find("BATTLE_EVENTS", "").Count, Is.EqualTo(4));
        }

        [Test]
        public void CandidateSourcesCarryOriginalBodyAndActionPath()
        {
            var catalog = ActionResourceCatalog.LoadBundled();
            var sample = catalog.Find("STORY", "").First(item => item.Key.EndsWith(".txt"));
            Assert.That(sample.Verified, Is.True);
            Assert.That(sample.Source, Does.Contain("LevelData "));
            Assert.That(sample.Source, Does.Contain("actions["));
            Assert.That(sample.Source, Does.Contain(".json"));
        }
    }
}
