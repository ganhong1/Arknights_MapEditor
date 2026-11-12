#nullable enable
using System;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Arknights.MapEditor.Tests.Core
{
    public sealed class DevicePlacementTests
    {
        private const string Config = "{\"inst\":{\"characterKey\":\"trap_001_crate\"},\"hidden\":true,\"alias\":null,\"future\":[2,1],\"position\":{\"row\":999,\"col\":999},\"direction\":\"UP\"}";

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void OperatorPlacementCanCreateJustOneRecord(bool onMap, bool inHand)
        {
            var document = LevelDocument.Create(3, 2).WithSection("predefines", "null");
            const string config = "{\"inst\":{\"characterKey\":\"char_220_grani\"},\"alias\":\"reserve\",\"direction\":\"UP\",\"position\":{\"row\":0,\"col\":0}}";
            var layer = document.CreateOperatorPresets("predefines", config, "RIGHT", (1, 2), onMap, inHand)
                .GetSection("predefines")!;
            var role = onMap ? "characterInsts" : "characterCards";
            var record = layer[role]![0]!;
            Assert.That(layer[onMap ? "characterCards" : "characterInsts"], Is.Null);
            Assert.That((string?)record["alias"], Is.EqualTo("reserve"));
            Assert.That((string?)record["direction"], Is.EqualTo(onMap ? "RIGHT" : null));
            Assert.That((int?)record["position"]?["col"], Is.EqualTo(onMap ? 2 : (int?)null));
        }

        [Test]
        public void PlacementUsesTargetCellsAndDirectionWithoutChangingOtherDataOrOriginal()
        {
            var original = LevelDocument.Create(4, 3).WithSection("predefines", "{\"tokenCards\":[{\"future\":9}],\"future\":42}");
            var before = original.ToJson();
            var changed = original.PlaceDevices("predefines", Config, "LEFT", new[] { (1, 2), (2, 3), (1, 2) });
            var rows = (JArray)changed.GetSection("predefines")!["tokenInsts"]!;
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(rows[0]["position"]!["row"]!.Value<int>(), Is.EqualTo(1));
            Assert.That(rows[0]["position"]!["col"]!.Value<int>(), Is.EqualTo(2));
            Assert.That(rows[1]["direction"]!.Value<string>(), Is.EqualTo("LEFT"));
            Assert.That(rows[1]["hidden"]!.Value<bool>(), Is.True);
            Assert.That(rows[1]["future"]!.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("[2,1]"));
            Assert.That(changed.GetSection("predefines")!["future"]!.Value<int>(), Is.EqualTo(42));
            Assert.That(changed.GetSection("predefines")!["tokenCards"]![0]["future"]!.Value<int>(), Is.EqualTo(9));
            Assert.That(original.ToJson(), Is.EqualTo(before));
        }

        [Test]
        public void InvalidPlacementIsAtomicAndDoesNotAcceptSentinelDirection()
        {
            var doc = LevelDocument.Create(4, 3);
            var before = doc.ToJson();
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.PlaceDevices("predefines", Config, "UP", new[] { (0, 0), (3, 0) }));
            Assert.Throws<ArgumentException>(() => doc.PlaceDevices("predefines", Config, "INVALID", new[] { (0, 0) }));
            Assert.Throws<ArgumentException>(() => doc.PlaceDevices("waves", Config, "UP", new[] { (0, 0) }));
            Assert.That(doc.ToJson(), Is.EqualTo(before));
        }

        [Test]
        public void NamedConfigurationsCannotSilentlyDuplicateAliases()
        {
            var doc = LevelDocument.Create(4, 3);
            var named = JObject.Parse(Config); named["alias"] = "device-a";
            Assert.Throws<InvalidOperationException>(() => doc.PlaceDevices("predefines", named.ToString(), "UP", new[] { (0, 0), (0, 1) }));
            var placed = doc.PlaceDevices("predefines", named.ToString(), "UP", new[] { (0, 0) });
            Assert.Throws<InvalidOperationException>(() => placed.PlaceDevices("predefines", named.ToString(), "UP", new[] { (0, 1) }));
        }
    }
}
