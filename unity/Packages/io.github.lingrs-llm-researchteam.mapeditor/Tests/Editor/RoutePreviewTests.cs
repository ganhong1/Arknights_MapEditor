#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Arknights.MapEditor.Tests
{
    public sealed class RoutePreviewTests
    {
        private const string Route = "{\"motionMode\":\"WALK\",\"allowDiagonalMove\":true," +
            "\"startPosition\":{\"row\":0,\"col\":0},\"endPosition\":{\"row\":2,\"col\":2},\"checkpoints\":[]}";

        private static LevelDocument Roads()
        {
            var doc = LevelDocument.Create(3, 3);
            var tile = JObject.Parse("{\"tileKey\":\"tile_road\",\"heightType\":\"LOWLAND\"," +
                "\"buildableType\":\"MELEE\",\"passableMask\":\"ALL\",\"playerSideMask\":\"ALL\"}");
            return doc.WithCells(Enumerable.Range(0, 9).Select(index => new CellEdit(index / 3, index % 3, tile)));
        }

        [Test]
        public void LibraryViewportBuildsPrtsPathsForBothRouteCollectionsAndClearsOnSwitch()
        {
            var document = Roads().WithSection("routes", "[" + Route + "]")
                .WithSection("extraRoutes", "[" + Route.Replace("WALK", "FLY") + "]");
            var original = document.ToJson();
            using var viewport = new MapWorkspaceViewport();
            viewport.Render(document, new MapViewportState(), new Vector2(320, 200));
            var model = new PrtsRoutePreview(document, false);
            Assert.That(viewport.RoutePaths.Count, Is.EqualTo(model.Definitions.Count));
            for (var i = 0; i < model.Definitions.Count; i++)
            {
                var expected = model.GetPath(model.Definitions[i])!;
                var actual = viewport.RoutePaths[i];
                Assert.That(actual.Air, Is.EqualTo(expected.Air));
                Assert.That(actual.Legs.Select(leg => (leg.From, leg.To, leg.Fallback)),
                    Is.EqualTo(expected.Legs.Select(leg => (leg.From, leg.To, leg.Fallback))));
                Assert.That(actual.Markers.Select(marker => marker.Kind), Is.EqualTo(expected.Markers.Select(marker => marker.Kind)));
            }
            Assert.That(document.ToJson(), Is.EqualTo(original));
            viewport.Render(Roads(), new MapViewportState(), new Vector2(240, 200));
            Assert.That(viewport.RoutePaths, Is.Empty);
        }

        [Test]
        public void ProjectedRouteUsesPreviewRectangleAndCameraProjection()
        {
            var root = new GameObject("Route projection test");
            try
            {
                var camera = root.AddComponent<Camera>(); camera.enabled = false;
                camera.transform.SetPositionAndRotation(new Vector3(0, 10, 0), Quaternion.Euler(90, 0, 0));
                camera.orthographic = true; camera.orthographicSize = 5; camera.aspect = 2;
                var rect = new Rect(30, 40, 400, 200);
                Assert.That(Vector3.Distance(PrtsRouteOverlay.Project(camera, rect, Vector3.zero), new Vector3(230, 140, 0)), Is.LessThan(.001f));
                Assert.That(PrtsRouteOverlay.Project(camera, rect, Vector3.right).x, Is.GreaterThan(230));
                Assert.That(PrtsRouteOverlay.Project(camera, rect, Vector3.forward).y, Is.LessThan(140));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void DefaultAndRestoredPreviewUseThePrtsOverlay()
        {
            var doc = Roads().WithSection("routes", "[" + Route + "," + Route + "]");
            var fresh = new RoutePreviewPlayback();
            fresh.RestoreSettings(new RoutePreviewSettings(), doc);
            Assert.That(fresh.ControlsOverlay, Is.True);
            Assert.That(fresh.Visible, Is.True);
            Assert.That(fresh.UniquePathCount, Is.EqualTo(1));

            fresh.SetAnimate(false);
            fresh.SetMode(RoutePreviewMode.Order);
            fresh.Select(1);
            var restored = new RoutePreviewPlayback();
            restored.RestoreSettings(fresh.CaptureSettings(), doc);
            Assert.That(restored.Mode, Is.EqualTo(RoutePreviewMode.Order));
            Assert.That(restored.Selected, Is.EqualTo(1));
            Assert.That(restored.UniquePathCount, Is.EqualTo(2));

            restored.SetVisible(false, doc);
            var hidden = new RoutePreviewPlayback();
            hidden.RestoreSettings(restored.CaptureSettings(), doc);
            Assert.That(hidden.Visible, Is.False);
            Assert.That(hidden.ControlsOverlay, Is.True);
        }

        [Test]
        public void SortsSpawnActionsAndPreservesTheirReferencesAndCounts()
        {
            var doc = Roads().WithSection("routes", "[" + Route + "," + Route + "]")
                .WithSection("extraRoutes", "[" + Route + "]")
                .WithSection("waves", @"[
                  {""preDelay"": 3, ""postDelay"": 2, ""fragments"": [
                    {""preDelay"": 2, ""actions"": [
                      {""actionType"":""SPAWN"",""key"":""late"",""routeIndex"":1,""preDelay"":6,""count"":1},
                      {""actionType"":""SPAWN"",""key"":""early"",""routeIndex"":0,""preDelay"":1,""count"":3,""interval"":2},
                      {""actionType"":""DISPLAY_ENEMY_INFO"",""routeIndex"":0,""preDelay"":8},
                      {""actionType"":""SPAWN"",""key"":""hidden"",""routeIndex"":0,""preDelay"":0,""hiddenGroup"":""g""}]},
                    {""preDelay"": 1, ""actions"": [{""actionType"":0,""key"":""extra"",""routeIndex"":0,""useExtraRoute"":true}]}]},
                  {""preDelay"": 1, ""fragments"": [{""actions"": [{""actionType"":""SPAWN"",""key"":""next"",""routeIndex"":0}]}]}]")
                .WithSection("branches", @"{""on_hit"":{""phases"":[{""preDelay"":5,""actions"":[{""actionType"":""SPAWN"",""routeIndex"":0}]}]}}");
            var preview = new PrtsRoutePreview(doc, false);
            Assert.That(preview.Spawns.Select(entry => entry.Enemy), Is.EqualTo(new[] { "early", "late", "extra", "next" }));
            Assert.That(preview.Spawns.Select(entry => entry.Time), Is.EqualTo(new[] { 6d, 11d, 14d, 22d }));
            Assert.That(preview.Spawns.Select(entry => entry.First), Is.EqualTo(new[] { 1L, 4L, 5L, 6L }));
            Assert.That(preview.Spawns[2].Library, Is.EqualTo("routes"));
            Assert.That(preview.BranchSpawns.Single().Library, Is.EqualTo("extraRoutes"));
            Assert.That(preview.BranchSpawns.Single().Time, Is.EqualTo(5d));
            Assert.That(preview.BranchSpawns.Single().Group, Is.EqualTo("on_hit"));
            Assert.That(new PrtsRoutePreview(doc, true).Spawns[0].Enemy, Is.EqualTo("hidden"));
        }

        [Test]
        public void RespectsBlockedCornersAndVisibleCrates()
        {
            var doc = Roads().WithSection("predefines",
                @"{""tokenInsts"":[{""position"":{""row"":0,""col"":1},""hidden"":false,""inst"":{""characterKey"":""trap_001_crate""}}]}");
            var grid = new PrtsRouteGrid(doc);
            var path = grid.Find(new Vector2Int(0, 0), new Vector2Int(2, 2), true, false, false);
            Assert.That(path, Is.Not.Null);
            Assert.That(path![1], Is.EqualTo(new Vector2Int(0, 1)));
            Assert.That(grid.Find(new Vector2Int(0, 0), new Vector2Int(1, 0), true, false, false), Is.Null);
        }

        [Test]
        public void OrderAndTimeWindowsIncludeBoundsAndKeepTheFocusedRoute()
        {
            var entries = new[]
            {
                new PrtsRoutePreview.Entry("routes", 2, "a", 1, 1, 1, ""),
                new PrtsRoutePreview.Entry("routes", 1, "a", 20, 2, 2, ""),
                new PrtsRoutePreview.Entry("routes", 3, "b", 21, 4, 1, ""),
                new PrtsRoutePreview.Entry("routes", 0, "b", 31, 5, 1, "")
            };
            Assert.That(PrtsRoutePreview.Window(entries, 2, RoutePreviewMode.Single, 5, 5), Is.EqualTo(new[] { 2 }));
            Assert.That(PrtsRoutePreview.Window(entries, 2, RoutePreviewMode.Time, 1, 10), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(PrtsRoutePreview.Window(entries, 2, RoutePreviewMode.Order, 2, 0), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(PrtsRoutePreview.Duration(8, RoutePreviewSpeed.Normal), Is.EqualTo(.8).Within(.0001));
            Assert.That(PrtsRoutePreview.Duration(8, RoutePreviewSpeed.Constant), Is.EqualTo(1.6).Within(.0001));
        }

        [Test]
        public void PlaybackIsDocumentLocalAndDoesNotEditLevelData()
        {
            var doc = Roads().WithSection("routes", "[" + Route + "," + Route + "]");
            var json = doc.ToJson();
            var preview = new RoutePreviewPlayback();
            try
            {
                preview.SetVisible(true, doc);
                preview.SetMode(RoutePreviewMode.Order);
                preview.Select(1);
                preview.Scrub(.5f);
                Assert.That(preview.VisibleCount, Is.EqualTo(2));
                Assert.That(preview.UniquePathCount, Is.EqualTo(2));
                Assert.That(preview.Progress, Is.EqualTo(.5f).Within(.001f));
                Assert.That(preview.Playing, Is.False);
                Assert.That(new RoutePreviewPlayback().Visible, Is.False);
                Assert.That(doc.ToJson(), Is.EqualTo(json));
                preview.SetVisible(false, doc);
                Assert.That(preview.ControlsOverlay, Is.True);
                Assert.That(preview.Visible, Is.False);
            }
            finally { preview.Stop(); }
        }

        [Test]
        public void Version2781SampleCoversAllSpawnRowsAndFaustTransition()
        {
            var file = Environment.GetEnvironmentVariable("MAP_EDITOR_ROUTE_DEMO_510");
            if (string.IsNullOrEmpty(file)) Assert.Ignore("Set MAP_EDITOR_ROUTE_DEMO_510 to the pinned 2.7.81 LevelData.");
            var doc = LevelDocument.Parse(File.ReadAllText(file!));
            var preview = new PrtsRoutePreview(doc, false);
            Assert.That((doc.Width, doc.Height), Is.EqualTo((12, 8)));
            Assert.That(preview.Definitions.Count, Is.EqualTo(36));
            Assert.That(preview.Spawns.Count, Is.EqualTo(36));
            Assert.That(preview.Spawns.Sum(entry => entry.Count), Is.EqualTo(73));
            Assert.That(preview.BranchSpawns, Is.Empty);
            Assert.That(preview.Spawns.Count(entry => preview.GetPath(entry)?.HasFallback == false), Is.EqualTo(36));
            // PRTSMap 0.49.66 queue and path compression, with the hidden jump split into separate runs.
            var geometry = string.Join("\n", preview.Spawns.OrderBy(entry => entry.RouteIndex).Select(entry =>
                entry.RouteIndex + ":" + string.Join(";", preview.GetPath(entry)!.Legs.Select(leg =>
                    string.Join(",", new[] { leg.From.x, leg.From.z, leg.To.x, leg.To.z }.Select(coordinate =>
                        (coordinate - .5f).ToString("0.####", CultureInfo.InvariantCulture)))))));
            using var sha = SHA256.Create();
            var digest = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(geometry))).Replace("-", "");
            Assert.That(digest, Is.EqualTo("6EB44BD62DDFB3A9A7F79B3584A8BEFE9ABC0A366E841019945535443BD75C66"));
            var faust = preview.Spawns.FindIndex(entry => entry.Enemy == "enemy_1508_faust");
            var focus = preview.Spawns[faust];
            Assert.That((focus.Time, focus.First, focus.RouteIndex), Is.EqualTo((114d, 23L, 16)));
            Assert.That(PrtsRoutePreview.Window(preview.Spawns, faust, RoutePreviewMode.Order, 5, 0).Count, Is.EqualTo(6));
            Assert.That(PrtsRoutePreview.Window(preview.Spawns, faust, RoutePreviewMode.Time, 10, 20).Count, Is.EqualTo(4));
            var path = preview.GetPath(focus)!;
            var disappear = path.Markers.Single(marker => marker.Kind == RouteMarkerKind.Disappear);
            var appear = path.Markers.Single(marker => marker.Kind == RouteMarkerKind.Appear);
            Assert.That(disappear.Point.x, Is.EqualTo(9.5f));
            Assert.That(appear.Point.x, Is.EqualTo(7.5f));
            Assert.That(disappear.Offset, Is.EqualTo(appear.Offset));
            Assert.That(path.Legs.Any(leg => leg.From == disappear.Point && leg.To == appear.Point), Is.False);
            Assert.That(path.Markers.Any(marker => marker.Kind == RouteMarkerKind.Wait && marker.Label == "75s" &&
                marker.Point.x == 9.5f && marker.Point.z == 6.5f), Is.True);
        }
    }
}
