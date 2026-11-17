using Arknights.MapEditor.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Tests
{
    public sealed class SkeletonStyleTests
    {
        private static JObject Tile(string key) => new JObject { ["tileKey"] = key, ["heightType"] = "LOWLAND", ["buildableType"] = "MELEE" };
        [Test]
        public void DefinitionsAreAvailableAndAliasesUseSamePattern()
        {
            Assert.That(SkeletonStyle.DefinitionCount, Is.EqualTo(125));
            Assert.That(SkeletonStyle.Resolve(Tile("tile_start_cooperate")).Pattern,
                Is.EqualTo(SkeletonStyle.Resolve(Tile("tile_start")).Pattern));
            Assert.That(SkeletonStyle.Resolve(Tile("tile_end")).Pattern, Is.EqualTo(3));
            foreach (var pair in new[]
            {
                (Alias: "tile_act6fun_start", Target: "tile_start"),
                (Alias: "tile_act6fun_end", Target: "tile_end"),
                (Alias: "tile_water", Target: "tile_deepsea"),
            })
            {
                var alias = Tile(pair.Alias);
                var target = Tile(pair.Target);
                Assert.That(SkeletonStyle.Resolve(alias).Pattern, Is.EqualTo(SkeletonStyle.Resolve(target).Pattern), pair.Alias);
                Assert.That(SkeletonStyle.SurfaceHeight(alias), Is.EqualTo(SkeletonStyle.SurfaceHeight(target)), pair.Alias);
                Assert.That(alias.Value<string>("tileKey"), Is.EqualTo(pair.Alias));
            }
        }
        [Test]
        public void UnknownTileIsExplicitAndNeverRewritesDocument()
        {
            var tile = Tile("tile_future"); tile["future"] = 42;
            var before = tile.ToString();
            var style = SkeletonStyle.Resolve(tile);
            Assert.That(style.Known, Is.False); Assert.That(style.Pattern, Is.EqualTo(17));
            Assert.That(tile.ToString(), Is.EqualTo(before));
        }
        [Test]
        public void ActualHeightAndBuildabilityControlSurfaceColor()
        {
            var tile = Tile("tile_road"); var low = SkeletonStyle.Resolve(tile).TopColor;
            tile["heightType"] = "HIGHLAND";
            Assert.That(SkeletonStyle.Resolve(tile).TopColor, Is.Not.EqualTo(low));
            var high = SkeletonStyle.Resolve(tile).TopColor;
            tile["buildableType"] = "NONE";
            Assert.That(SkeletonStyle.Resolve(tile).TopColor, Is.Not.EqualTo(high));
            var supplemental = Tile("tile_sleep_wall");
            supplemental["heightType"] = "HIGHLAND";
            supplemental["buildableType"] = "RANGED";
            var before = supplemental.ToString();
            Assert.That(SkeletonStyle.Resolve(supplemental).Label, Is.Empty);
            Assert.That(SkeletonStyle.Resolve(supplemental).Symbol, Is.Positive);
            Assert.That(SkeletonStyle.SurfaceHeight(supplemental), Is.EqualTo(SkeletonStyle.BlockTop));
            Assert.That(SkeletonStyle.ShowsDeploymentBorder(supplemental), Is.True);
            Assert.That(supplemental.ToString(), Is.EqualTo(before));
        }
        [Test]
        public void SupplementalTilesUseSymbolsWithoutReplacingOriginalPrtsStyles()
        {
            var source = AssetDatabase.LoadAssetAtPath<TextAsset>(SkeletonStyle.DefinitionPath);
            var catalog = JObject.Parse(source.text);
            var marked = 0;
            foreach (var entry in ((JObject)catalog["tiles"]).Properties())
            {
                var style = SkeletonStyle.Resolve(Tile(entry.Name));
                if (entry.Value["symbol"] == null)
                {
                    Assert.That(style.Symbol, Is.Zero, entry.Name);
                    continue;
                }
                marked++;
                Assert.That(style.Symbol, Is.InRange(1, ((JArray)catalog["symbols"]).Count), entry.Name);
                Assert.That(style.Label, Is.Empty, entry.Name);
            }
            Assert.That(marked, Is.EqualTo(70));
            Assert.That(SkeletonStyle.Resolve(Tile("tile_sleep_wall")).Symbol,
                Is.Not.EqualTo(SkeletonStyle.Resolve(Tile("tile_replace_wall")).Symbol));
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(SkeletonStyle.SymbolAtlasPath);
            Assert.That(atlas, Is.Not.Null);
            Assert.That(atlas.width, Is.EqualTo(512));
            Assert.That(atlas.height, Is.EqualTo(512));
        }
        [Test]
        public void InvalidFieldShapesUseFallbackWithoutChangingOriginal()
        {
            var tile = new JObject { ["tileKey"] = new JObject(), ["heightType"] = new JArray(1), ["buildableType"] = 42 };
            var before = tile.ToString();
            Assert.That(SkeletonStyle.Resolve(tile).Known, Is.False);
            Assert.That(tile.ToString(), Is.EqualTo(before));
        }
        [Test]
        public void InactivePreviewCannotChangeSceneViewCamera()
        {
            var stage = ScriptableObject.CreateInstance<MapPreviewStage>();
            var sceneView = EditorWindow.GetWindow<SceneView>();
            stage.Configure(Arknights.MapEditor.Core.LevelDocument.Create(2, 2));
            var rotation = sceneView.rotation; var pivot = sceneView.pivot;
            try { stage.SetView(true); Assert.That(sceneView.rotation, Is.EqualTo(rotation)); Assert.That(sceneView.pivot, Is.EqualTo(pivot)); }
            finally { Object.DestroyImmediate(stage); }
        }
        [Test]
        public void SkeletonShaderImportsWithoutCompilerErrors()
        {
            var shader = Shader.Find("Hidden/ArknightsMapEditor/Skeleton");
            Assert.That(shader, Is.Not.Null);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
            Assert.That(shader.isSupported, Is.True);
        }
    }
}
