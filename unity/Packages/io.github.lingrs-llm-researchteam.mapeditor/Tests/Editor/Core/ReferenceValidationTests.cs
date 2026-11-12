using System.Linq;
using Arknights.MapEditor.Core;
using NUnit.Framework;

namespace Arknights.MapEditor.Tests.Core
{
    public sealed class ReferenceValidationTests
    {
        [Test]
        public void InvalidRouteStructureAndCoordinatesProducePathDiagnostics()
        {
            var document = LevelDocument.Create(2, 2).WithSection("routes", "[null,{\"motionMode\":\"E_NUM\",\"startPosition\":{\"row\":\"0\",\"col\":0},\"endPosition\":null,\"checkpoints\":[{\"type\":\"FUTURE\",\"position\":{\"row\":0,\"col\":\"x\"}}]}]");
            var diagnostics = document.ValidateReferences();
            Assert.That(diagnostics.Any(d => d.Severity == "error" && d.Path == "routes[0]"), Is.True);
            Assert.That(diagnostics.Any(d => d.Severity == "error" && d.Path.EndsWith("startPosition.row")), Is.True);
            Assert.That(diagnostics.Any(d => d.Severity == "error" && d.Path.EndsWith("endPosition")), Is.True);
            Assert.That(diagnostics.Any(d => d.Severity == "error" && d.Path.EndsWith("motionMode")), Is.True);
            Assert.That(diagnostics.Any(d => d.Severity == "warning" && d.Path.Contains("checkpoints")), Is.True);
        }

        [Test]
        public void WaveAndActionNumbersAreValidatedWithoutNormalizingUnknownFields()
        {
            var document = LevelDocument.Create(2, 2).WithSection("waves", "[{\"preDelay\":-1,\"postDelay\":0,\"maxTimeWaitingForNextWave\":-2,\"future\":null,\"fragments\":[{\"preDelay\":0,\"actions\":[{\"actionType\":\"EMPTY\",\"count\":1.5,\"preDelay\":0,\"interval\":\"1\",\"future\":[3,2,1]}]}]}]");
            var before = document.ToJson();
            var diagnostics = document.ValidateReferences();
            Assert.That(diagnostics.Any(d => d.Path.EndsWith("maxTimeWaitingForNextWave") && d.Severity == "error"), Is.True);
            Assert.That(diagnostics.Any(d => d.Path.EndsWith("count") && d.Severity == "error"), Is.True);
            Assert.That(diagnostics.Any(d => d.Path.EndsWith("interval") && d.Severity == "error"), Is.True);
            Assert.That(document.ToJson(), Is.EqualTo(before));
        }

        [Test]
        public void ValidNumericEnumsFractionalCoordinatesAndWaveDefaultsAreAccepted()
        {
            var document = LevelDocument.Create(2, 2)
                .WithSection("routes", "[{\"motionMode\":0,\"startPosition\":{\"row\":0.5,\"col\":0},\"endPosition\":{\"row\":1,\"col\":1},\"checkpoints\":[{\"type\":1,\"time\":1}]}]")
                .WithSection("waves", "[{\"preDelay\":0,\"postDelay\":0,\"maxTimeWaitingForNextWave\":-1,\"fragments\":[{\"preDelay\":0,\"actions\":[{\"actionType\":1,\"routeIndex\":0,\"count\":1,\"preDelay\":0,\"interval\":0}]}]}]");
            Assert.That(document.ValidateReferences().Where(d => d.Severity == "error"), Is.Empty);
        }
    }
}
