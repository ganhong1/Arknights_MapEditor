using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Arknights.MapEditor.Tests.Core
{
    public sealed class WebDifferentialFixtureTests
    {
        // Synthetic fixture, not game data. Expected was obtained from the repository's
        // map-model.js: grid = cells(input); grid[0][1] = editedTile; exportMap(input, grid).
        private const string Input = "{\"levelId\":\"fixture\",\"mapId\":\"synthetic\",\"mapData\":{\"map\":[[0,0],[0,0]],\"tiles\":[{\"tileKey\":\"tile_road\",\"blackboard\":null}],\"future\":{\"ordered\":[3,1,2]}},\"routes\":[],\"extraRoutes\":[],\"waves\":[],\"enemyDbRefs\":[],\"future\":{\"empty\":null,\"enum\":73}}";
        private const string Expected = "{\"levelId\":\"fixture\",\"mapId\":\"synthetic\",\"mapData\":{\"map\":[[0,0],[0,1]],\"tiles\":[{\"tileKey\":\"tile_road\",\"blackboard\":null},{\"tileKey\":\"tile_wall\",\"blackboard\":[{\"key\":\"future\",\"value\":17}]}],\"future\":{\"ordered\":[3,1,2]}},\"routes\":[],\"extraRoutes\":[],\"waves\":[],\"enemyDbRefs\":[],\"future\":{\"empty\":null,\"enum\":73}}";

        [Test]
        public void LocalCellEditMatchesWebExportAndPreservesUnrelatedSections()
        {
            var source = LevelDocument.Parse(Input);
            var editedTile = JObject.Parse("{\"tileKey\":\"tile_wall\",\"blackboard\":[{\"key\":\"future\",\"value\":17}]}");
            var result = source.WithCell(0, 1, editedTile);
            Assert.That(JToken.DeepEquals(JObject.Parse(result.ToJson()), JObject.Parse(Expected)), Is.True);
            Assert.That(JToken.DeepEquals(JObject.Parse(source.ToJson()), JObject.Parse(Input)), Is.True);
        }

        [Test]
        public void CellEditDoesNotImplicitlyPerformWebLegacyMigration()
        {
            // Web exportMap explicitly strips routeCalculatorTest, editorId,
            // visitEveryNodeStably and editorEnemyId; for a legacy envelope it also
            // normalizes empty optionalRunes and supplies checkpoint defaults.
            // Core edits intentionally preserve these fields: migration must be explicit.
            var document = LevelDocument.Parse(Input)
                .WithSection("routeCalculatorTest", "true")
                .WithSection("optionalRunes", "[]")
                .WithSection("routes", "[{\"editorId\":\"legacy-route\",\"visitEveryNodeStably\":true,\"checkpoints\":[{\"type\":\"MOVE\"}]}]")
                .WithSection("waves", "[{\"fragments\":[{\"actions\":[{\"editorEnemyId\":\"legacy-enemy\"}]}]}]");
            var result = document.WithCell(0, 1, JObject.Parse("{\"tileKey\":\"tile_wall\"}"));
            foreach (var section in new[] { "routeCalculatorTest", "optionalRunes", "routes", "waves" })
                Assert.That(JToken.DeepEquals(result.GetSection(section), document.GetSection(section)), Is.True, section);
        }
    }
}
