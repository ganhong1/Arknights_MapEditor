#nullable enable
using System.Linq;
using Arknights.MapEditor.Catalog;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Arknights.MapEditor.Tests.Catalog
{
    public sealed class EditorCatalogTests
    {
        [Test] public void FrozenCatalogSeparatesTemplatesFromVisualOnlyEntries()
        {
            var catalog=EditorCatalog.LoadBaseline();
            Assert.That(catalog.Entries.Count, Is.EqualTo(117));
            Assert.That(catalog.Entries.Single(x => x.VisualOnly).Id, Is.EqualTo("tile_crate"));
            var native = catalog.Entries.Where(x => !x.VisualOnly).ToArray();
            Assert.That(native.Select(x => x.Family ?? x.Id).Distinct().Count(), Is.EqualTo(91));
            Assert.That(native.Where(x => x.Family != null).Select(x => x.Family).Distinct().Count(), Is.EqualTo(18));
            Assert.That(catalog.Entries.Count(x=>x.HasTemplate),Is.EqualTo(23));
            Assert.That(catalog.Search("保护目标").Select(x => x.Id), Does.Contain("tile_end"));
            Assert.That(catalog.Find("unknown_future"),Is.Null);
        }
        [Test] public void DisplayNameTrimsChineseNamesAndUsesBareIdForMissingNames()
        {
            const string json = "{\"version\":1,\"entries\":[{\"id\":\"tile_road\",\"name\":\"　平地 \",\"description\":\"\",\"category\":\"terrain\",\"visualDefinitionAvailable\":false,\"template\":null}]}";
            Assert.That(EditorCatalog.LoadJson(json).Find("tile_road")!.DisplayName, Is.EqualTo("平地"));
            var bundled = EditorCatalog.LoadBundled();
            Assert.That(bundled.Find("tile_crate")!.DisplayName, Is.EqualTo("tile_crate"));
            Assert.That(bundled.Find("tile_pollution_roadf")!.DisplayName, Is.EqualTo("tile_pollution_roadf"));
        }

        [Test] public void TemplateAccessCannotMutateSharedCatalog()
        {
            var entry=EditorCatalog.LoadBundled().Find("tile_road")!;
            var template=entry.GetTemplate()!;template["tileKey"]="changed";
            Assert.That(entry.GetTemplate()!["tileKey"]!.Value<string>(),Is.EqualTo("tile_road"));
        }
        [Test] public void ImportedObjectsKeepCategoryAndUnknownIds()
        {
            var level=JObject.Parse("{\"predefines\":{\"characterInsts\":[{\"inst\":{\"characterKey\":\"unknown\"},\"hidden\":true}],\"tokenCards\":[{\"inst\":{\"characterKey\":\"card\"}}]},\"waves\":[{\"fragments\":[{\"actions\":[{\"actionType\":\"TRIGGER_PREDEFINED\",\"key\":\"mechanism\"}]}]}]}");
            var entries=EditorCatalog.DocumentObjects(level);
            Assert.That(entries.Select(x=>x.Category),Is.EqualTo(new[]{"hidden","card","native-action"}));
            Assert.That(entries[0].Id,Is.EqualTo("unknown"));
            Assert.That(entries.All(x=>!x.CanPlaceFromCatalog),Is.True);
        }
        [Test] public void RejectsTemplateIdentityMismatch()
        {
            const string json="{\"version\":1,\"entries\":[{\"id\":\"tile_a\",\"name\":\"\",\"description\":\"\",\"category\":\"terrain\",\"visualDefinitionAvailable\":false,\"template\":{\"tileKey\":\"tile_b\"}}]}";
            Assert.Throws<System.IO.InvalidDataException>(()=>EditorCatalog.LoadJson(json));
        }
        [Test] public void SearchCategoryDoesNotPromoteTerrainToDevice()
        {
            Assert.That(EditorCatalog.LoadBundled().Search("", "preplaced"),Is.Empty);
        }
    }
}
