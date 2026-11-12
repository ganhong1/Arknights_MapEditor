#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arknights.MapEditor.Editor.Tests
{
    public sealed class FinishedLevelLibraryTests
    {
        [Test]
        public void BundledArchivePreservesStageModesAndVersionVariants()
        {
            var catalog = FinishedLevelCatalog.Load(FinishedLevelCatalog.BundledPath());
            Assert.That(catalog.Groups.Count(group => !group.IsComponent), Is.EqualTo(3461));
            Assert.That(catalog.BodyIds.Count(), Is.EqualTo(8664));
            var group = catalog.Groups.Single(item => item.Variants.Any(variant => variant.Stage?.Id == "main_12-16"));
            var normal = group.Variants.Single(item => item.Stage?.Id == "main_12-16" && item.Source.Repository == "ArknightsGameData");
            var tough = group.Variants.Single(item => item.Stage?.Id == "tough_12-16" && item.Source.Repository == "ArknightsGameData");
            Assert.That(tough.Stage!.Mode, Is.EqualTo("磨难"));
            using var archive = catalog.OpenBodies();
            var first = LevelDocument.Parse(catalog.ReadBody(archive, normal.Source.Body));
            var second = LevelDocument.Parse(catalog.ReadBody(archive, tough.Source.Body));
            Assert.That(first.GetSection("options")!.Value<int>("initialCost"), Is.EqualTo(10));
            Assert.That(second.GetSection("options")!.Value<int>("initialCost"), Is.EqualTo(5));
            Assert.That(normal.Source.Revision, Is.Not.Empty);
            Assert.That(JObject.Parse(normal.Origin).Value<string>("path"), Is.EqualTo(normal.Source.Path));
        }

        [Test]
        public void GroupedStageSearchUsesToolboxNamesReadingsCodesAndPaths()
        {
            var catalog = FinishedLevelCatalog.Load(FinishedLevelCatalog.BundledPath());
            var groups = catalog.Groups.Where(group => !group.IsComponent).ToArray();
            var expected = groups.Single(group => group.Variants.Any(variant => variant.Stage?.Id == "main_05-10"));
            var search = TerrainSearchRuntime.CreateWorker<FinishedLevelCatalog.StageGroup>(TerrainSearchRuntime.FinishedLevel);
            foreach (var query in new[] { "长夜终尽", "changyezhongjin", "cyzj", "main_05-10", "level_main_05-10", "cyzj 突袭" })
            {
                var matches = search.Filter(groups, query, out var error).ToArray();
                Assert.That(error, Is.Null, query);
                Assert.That(matches, Does.Contain(expected), query);
            }
        }

        [Test]
        public void FinishedLevelCopyRetainsExistingDraftAndHasNoSourceWriter()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            try
            {
                workspace.AddDocument(0, "已有草稿", LevelDocument.Create(6, 5).ToJson(), refresh: false);
                var draft = workspace.FocusedEditor!;
                var document = draft.WorkspaceDocument;
                var catalog = FinishedLevelCatalog.Load(FinishedLevelCatalog.BundledPath());
                var item = catalog.Groups.SelectMany(group => group.Variants)
                    .First(variant => variant.Stage?.Id == "main_05-10");
                using var archive = catalog.OpenBodies();
                workspace.AddFinishedLevel(item.Title, catalog.ReadBody(archive, item.Source.Body), item.Origin, refresh: false);
                Assert.That(workspace.Documents, Does.Contain(draft));
                Assert.That(draft.WorkspaceDocument, Is.SameAs(document));
                Assert.That(workspace.FocusedEditor!.WorkspacePath, Is.Empty);
                Assert.That(workspace.FocusedEditor.WorkspaceDirty, Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(workspace); }
        }

        [Test]
        public void RenderEntireCorpusWhenReportPathIsSupplied()
        {
            var report = Environment.GetEnvironmentVariable("ARK_MAP_LEVEL_RENDER_REPORT");
            if (string.IsNullOrEmpty(report)) Assert.Ignore("Full render audit requires ARK_MAP_LEVEL_RENDER_REPORT.");
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null));
            var catalog = FinishedLevelCatalog.Load(FinishedLevelCatalog.BundledPath());
            using var archive = catalog.OpenBodies();
            using var output = new StreamWriter(new FileStream(report!, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            output.WriteLine(JsonConvert.SerializeObject(new
            {
                unity = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName,
                graphics = SystemInfo.graphicsDeviceType.ToString()
            }));
            output.Flush();
            var initialScenes = EditorSceneManager.previewSceneCount;
            var texture = new Texture2D(256, 144, TextureFormat.RGBA32, false);
            var counts = new Dictionary<string, int>();
            int processed = 0;
            try
            {
                using var view = new MapWorkspaceViewport();
                var state = new MapViewportState();
                foreach (var body in catalog.BodyIds.OrderBy(id => id, StringComparer.Ordinal))
                {
                    LevelDocument? doc = null;
                    var chunks = 0;
                    var foreground = 0;
                    var missing = 0;
                    var unknown = Array.Empty<string>();
                    var status = "rendered";
                    string? error = null;
                    string? raw = null;
                    try
                    {
                        raw = catalog.ReadBody(archive, body);
                        doc = LevelDocument.Parse(raw);
                        var frame = view.Render(doc, state, new Vector2(256, 144) / UnityEditor.EditorGUIUtility.pixelsPerPoint);
                        chunks = view.RenderedChunkCount;
                        missing = view.MissingObjectIconCount;
                        foreground = CountForeground(frame, texture);
                        unknown = UnknownTiles(doc);
                        status = chunks == 0 || foreground == 0 ? "empty-frame" :
                            missing > 0 || unknown.Length > 0 ? "rendered-with-fallback" : "rendered";
                    }
                    catch (LevelDocumentException failure)
                    {
                        status = raw != null && failure.Path == "$.mapData.map" &&
                            (JObject.Parse(raw)["mapData"] as JObject)?["map"]?.Type == JTokenType.Null
                            ? "no-map" : "parse-error";
                        error = failure.Message;
                    }
                    catch (Exception failure)
                    {
                        status = doc == null ? "read-error" : "render-error";
                        error = failure.Message;
                    }
                    output.WriteLine(JsonConvert.SerializeObject(new
                    {
                        body, status, width = doc?.Width, height = doc?.Height, chunks, foreground, missing, unknown, error
                    }));
                    counts[status] = counts.TryGetValue(status, out var count) ? count + 1 : 1;
                    if (++processed % 64 == 0) output.Flush();
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            output.WriteLine(JsonConvert.SerializeObject(new { processed, counts }));
            output.Flush();
            Assert.That(processed, Is.EqualTo(8664));
            Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(initialScenes));
            Assert.That(counts.ContainsKey("read-error"), Is.False);
            Assert.That(counts.ContainsKey("render-error"), Is.False);
            Assert.That(counts.ContainsKey("empty-frame"), Is.False);
        }

        private static string[] UnknownTiles(LevelDocument doc)
        {
            var mapData = (JObject)doc.GetSection("mapData")!;
            var tiles = (JArray)mapData["tiles"]!;
            return ((JArray)mapData["map"]!).Children<JArray>().SelectMany(row => row.Values<int>()).Distinct()
                .Select(index => (JObject)tiles[index]!).Where(tile => !SkeletonStyle.Resolve(tile).Known)
                .Select(tile => tile["tileKey"]?.Type == JTokenType.String ? tile.Value<string>("tileKey")! : "(缺少 tileKey)")
                .Distinct().ToArray();
        }

        private static int CountForeground(RenderTexture frame, Texture2D texture)
        {
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = frame;
                texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
                var pixels = texture.GetRawTextureData<Color32>();
                var background = pixels[0];
                var foreground = 0;
                for (var index = 0; index < pixels.Length; index++)
                {
                    var pixel = pixels[index];
                    if (Math.Abs(pixel.r - background.r) + Math.Abs(pixel.g - background.g) + Math.Abs(pixel.b - background.b) > 6)
                        foreground++;
                }
                return foreground;
            }
            finally { RenderTexture.active = previous; }
        }
    }
}
