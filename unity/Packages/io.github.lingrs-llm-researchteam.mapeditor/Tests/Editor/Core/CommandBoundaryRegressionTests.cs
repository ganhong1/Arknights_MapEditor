using System.Linq;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Arknights.MapEditor.Tests.Core
{
    public sealed class CommandBoundaryRegressionTests
    {
        [Test]
        public void KnownNonRouteActionDefaultIndexDoesNotPreventRouteChanges()
        {
            var original = LevelDocument.Create(2, 2)
                .WithSection("routes", "[{\"name\":\"a\"},{\"name\":\"b\"}]")
                .WithSection("waves", "[{\"fragments\":[{\"actions\":[{\"actionType\":\"EMPTY\",\"routeIndex\":0}]}]}]");
            var moved = original.MoveRoute("routes", 0, 1);
            Assert.That(moved.GetSection("routes")[0]["name"].Value<string>(), Is.EqualTo("b"));
            Assert.That(moved.GetSection("waves").ToString(), Is.EqualTo(original.GetSection("waves").ToString()));
            Assert.That(((JArray)original.DeleteRoute("routes", 0).GetSection("routes")).Count, Is.EqualTo(1));
            var extension = original.WithSection("waves", "[{\"fragments\":[{\"actions\":[{\"actionType\":\"EMPTY\",\"routeIndex\":0,\"future\":{\"routeIndex\":1}}]}]}]");
            Assert.Throws<LevelDocumentException>(() => extension.DeleteRoute("routes", 0));
        }

        [TestCase("9007199254740992.1", true)]
        [TestCase("1e-9999", true)]
        [TestCase("10e-1", false)]
        [TestCase("100.00", false)]
        [TestCase("1.50e1", false)]
        [TestCase("0e-9999", false)]
        [TestCase("0.000000000000000000001", true)]
        public void CountIntegralityUsesExactDecimalScale(string count, bool invalid)
        {
            var document = LevelDocument.Create(2, 2).WithSection("waves",
                "[{\"preDelay\":0,\"postDelay\":0,\"maxTimeWaitingForNextWave\":-1,\"fragments\":[{\"preDelay\":0,\"actions\":[{\"actionType\":\"EMPTY\",\"count\":" + count + ",\"preDelay\":0,\"interval\":0}]}]}]");
            Assert.That(document.ValidateReferences().Any(d => d.Path.EndsWith(".count") && d.Severity == "error"), Is.EqualTo(invalid));
        }

        [Test]
        public void CropRetainsAlreadyExternalCoordinatesWithoutAddingUnusedTiles()
        {
            var original = LevelDocument.Create(4, 4)
                .WithSection("future", "{\"position\":{\"row\":3,\"col\":-1}}");
            var cropped = original.Resize(2, 2);
            Assert.That(cropped.GetSection("future").ToString(), Is.EqualTo(original.GetSection("future").ToString()));
            Assert.That(cropped.GetSection("mapData")["tiles"].ToString(), Is.EqualTo(original.GetSection("mapData")["tiles"].ToString()));
            Assert.Throws<LevelDocumentException>(() => original.WithSection("future", "{\"row\":3,\"col\":1}").Resize(2, 2));
        }
    }
}
