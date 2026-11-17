#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Tests
{
    public sealed class RouteEditingTests
    {
        [UnityTest]
        public IEnumerator RebuildingAfterDragReorderKeepsOrderExpandedAndShowsTypeSpecificFields()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                var document = LevelDocument.Create(6, 5);
                window.LoadWorkspaceDocument(workspace, "route", document.ToJson(), "", "");
                window.ShowUtility();
                yield return null;
                window.CreateGUI();
                var inspector = window.rootVisualElement;
                window.BeginRouteDraft("routes", -1, "FLY");
                window.PlaceRouteCell(Vector2Int.zero);
                window.PlaceRouteCell(new Vector2Int(5, 4));
                window.InsertRouteNode(0, new Vector2Int(1, 2));
                window.InsertRouteNode(1, new Vector2Int(3, 2));

                var order = inspector.Q<Foldout>("scene-route-checkpoint-order");
                order.value = true;
                var reorder = order.Q<ListView>();
                const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                // Unity's animated drag controller uses ListViewController.Move to commit a drop.
                var controller = typeof(ListView).GetProperties(members).Single(property =>
                    property.Name == "viewController" && property.PropertyType.Name == "ListViewController");
                controller.PropertyType.GetMethod("Move", members)!.Invoke(controller.GetValue(reorder), new object[] { 1, 0 });
                yield return null;
                yield return null;

                Assert.That(inspector.Q<Foldout>("scene-route-checkpoint-order").value, Is.True);
                Assert.That(window.ActiveRouteDraft!.TryNodeCell(0, out var first), Is.True);
                Assert.That(first, Is.EqualTo(new Vector2Int(3, 2)));
                var type = inspector.Q<PopupField<string>>("scene-route-checkpoint-type");
                Assert.That(type.choices.Count, Is.EqualTo(11));
                Assert.That(type.value, Does.EndWith("MOVE"));
                var settings = inspector.Q("scene-route-checkpoint-settings");
                Assert.That(settings.Query<IntegerField>().ToList().Select(field => field.label), Does.Contain("行 row"));

                type.value = type.choices.Single(choice => choice.EndsWith("WAIT_CURRENT_WAVE_TIME", StringComparison.Ordinal));
                settings = inspector.Q("scene-route-checkpoint-settings");
                var time = settings.Query<DoubleField>().ToList().Single(field => field.label == "当前波次时间（秒）");
                time.value = 15;
                Assert.That(window.ActiveRouteDraft.Node(0)!["type"]!.Value<string>(), Is.EqualTo("WAIT_CURRENT_WAVE_TIME"));
                Assert.That(window.ActiveRouteDraft.Node(0)!["time"]!.Value<double>(), Is.EqualTo(15));
                Assert.That(inspector.Q<Foldout>("scene-route-checkpoint-order").value, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                UnityEngine.Object.DestroyImmediate(workspace);
            }
        }

        [Test]
        public void TypeChangesPreserveNativeEncodingAndUnusedFields()
        {
            var draft = new RouteEditDraft("routes", -1, RouteEditDraft.Create("FLY"));
            draft.Place(new Vector2Int(2, 1));
            draft.Place(new Vector2Int(5, 4));
            draft.Route["checkpoints"] = JArray.Parse(
                "[{\"type\":0,\"time\":12,\"position\":{\"row\":2,\"col\":3},\"future\":true},{\"type\":\"DISAPPEAR\"}]");

            draft.SetCheckpointType(0, RouteCheckpointType.WAIT_CURRENT_WAVE_TIME);
            var first = draft.Node(0)!;
            Assert.That(first["type"]!.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(first["type"]!.Value<int>(), Is.EqualTo(4));
            Assert.That(first["time"]!.Value<int>(), Is.EqualTo(12));
            Assert.That(first["position"]!["col"]!.Value<int>(), Is.EqualTo(3));
            Assert.That(first["future"]!.Value<bool>(), Is.True);
            Assert.That(draft.TryNodeCell(0, out _), Is.False);

            draft.SetCheckpointType(1, RouteCheckpointType.APPEAR_AT_POS);
            Assert.That(draft.Node(1)!["type"]!.Value<string>(), Is.EqualTo("APPEAR_AT_POS"));
            Assert.That(draft.TryNodeCell(1, out var appeared), Is.True);
            Assert.That(appeared, Is.EqualTo(new Vector2Int(2, 1)));
            Assert.That(RouteEditDraft.ReadCheckpointType(new JValue("INVALID")), Is.Null);
        }

        [Test]
        public void BlankMapPlacementPromotesCandidateAndAppliesAsOneUndo()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            var window = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                var blank = LevelDocument.Create(6, 5);
                window.LoadWorkspaceDocument(workspace, "blank", blank.ToJson(), "", "");
                window.BeginRouteDraft("routes", -1, "FLY");
                window.PlaceRouteCell(new Vector2Int(0, 0));
                Assert.That(window.FinishRouteDrawing(), Is.False);
                window.PlaceRouteCell(new Vector2Int(2, 0));
                window.PlaceRouteCell(new Vector2Int(5, 4));
                window.RemoveLastRoutePoint();
                Assert.That(window.ActiveRouteDraft!.Route["endPosition"]!["col"]!.Value<int>(), Is.EqualTo(2));
                window.PlaceRouteCell(new Vector2Int(5, 4));
                Assert.That(window.FinishRouteDrawing(), Is.True);
                window.MoveRouteNode(0, new Vector2Int(2, 1));
                window.InsertRouteNode(1, new Vector2Int(3, 2));
                Assert.That(window.WorkspaceDocument.ToJson(), Is.EqualTo(blank.ToJson()));
                window.ApplyRouteDraft();
                var saved = (JArray)window.WorkspaceDocument.GetSection("routes")!;
                Assert.That(saved.Count, Is.EqualTo(1));
                Assert.That(saved[0]["checkpoints"]!.Select(point => point["position"]!["row"]!.Value<int>()), Is.EqualTo(new[] { 1, 2 }));
                var roundTrip = LevelDocument.Parse(window.WorkspaceDocument.ToJson());
                Assert.That(JToken.DeepEquals(roundTrip.GetSection("waves"), blank.GetSection("waves")), Is.True);
                window.UndoFromShortcut();
                Assert.That(window.WorkspaceDocument.ToJson(), Is.EqualTo(blank.ToJson()));
                typeof(MapEditorWindow).GetMethod("StepEditorHistory", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(window, new object[] { true });
                Assert.That(window.WorkspaceDocument.ToJson(), Is.EqualTo(roundTrip.ToJson()));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                UnityEngine.Object.DestroyImmediate(workspace);
            }
        }

        [TestCase("main_05-10", "9ade453a5ba2c8325ec5c1dc03f6f3a4590cf9681861be6949d2ce4135c07f26", "routes", 39)]
        [TestCase("act12mini_06", "2f8d8f156bc412bef9c3067608599d104902004daef1b48623781afbf3006802", "extraRoutes", 12)]
        public void OriginalRoutesReusePrtsGeometryAndKeepScheduleOnEdit(string stage, string body, string library, int count)
        {
            var catalog = FinishedLevelCatalog.Load(FinishedLevelCatalog.BundledPath());
            var source = catalog.Groups.SelectMany(group => group.Variants).First(v => v.Stage?.Id == stage && v.Source.Body == body);
            using var archive = catalog.OpenBodies();
            var document = LevelDocument.Parse(catalog.ReadBody(archive, source.Source.Body));
            var preview = new PrtsRoutePreview(document, true);
            var routes = (JArray)document.GetSection(library)!;
            Assert.That(routes.Count, Is.EqualTo(count));
            var entry = preview.Definitions.First(item => item.Library == library && preview.GetPath(item)?.Legs.Count > 0);
            var route = (JObject)routes[entry.RouteIndex];
            var draft = new RouteEditDraft(library, entry.RouteIndex, route);
            Assert.That(preview.BuildPath(route, true)!.Legs.Select(leg => (leg.From, leg.To, leg.Fallback)),
                Is.EqualTo(preview.GetPath(entry)!.Legs.Select(leg => (leg.From, leg.To, leg.Fallback))));
            var leg = preview.BuildPath(route, true)!.Legs.First();
            Assert.That(leg.InsertBefore, Is.InRange(0, draft.Checkpoints?.Count ?? 0));
            Assert.That(Vector3.Dot((leg.To - leg.From).normalized, leg.To - leg.From), Is.Positive);
            draft.Insert(leg.InsertBefore, "MOVE", new Vector2Int(1, 1));
            var result = draft.Apply(document);
            Assert.That(JToken.DeepEquals(result.GetSection("waves"), document.GetSection("waves")), Is.True);
            Assert.That(JToken.DeepEquals(result.GetSection("branches"), document.GetSection("branches")), Is.True);
            Assert.That(((JArray)result.GetSection(library)!).Count, Is.EqualTo(count));
            if (library == "extraRoutes")
                Assert.That(preview.BranchSpawns.Select(spawn => spawn.RouteIndex).Distinct().Count(), Is.EqualTo(12));
        }

        [Test]
        public void DraftGeometryMarksWaitAndDisappearanceAndTracksInsertionIndices()
        {
            var draft = new RouteEditDraft("routes", -1, RouteEditDraft.Create("FLY"));
            draft.Place(Vector2Int.zero);
            draft.Place(new Vector2Int(5, 0));
            draft.Insert(0, "MOVE", new Vector2Int(1, 0));
            draft.Insert(1, "WAIT_FOR_SECONDS", null);
            draft.Insert(2, "DISAPPEAR", null);
            draft.Insert(3, "APPEAR_AT_POS", new Vector2Int(4, 0));
            var path = new PrtsRoutePreview(LevelDocument.Create(6, 3), false).BuildPath(draft.Route, true)!;
            Assert.That(path.Legs.Select(leg => leg.InsertBefore), Is.EqualTo(new[] { 0, 4 }));
            Assert.That(path.Legs.Select(leg => leg.To.x - leg.From.x), Is.All.GreaterThan(0));
            Assert.That(path.Markers.Select(marker => marker.Kind), Does.Contain(RouteMarkerKind.Wait));
            Assert.That(path.Markers.Select(marker => marker.Kind), Does.Contain(RouteMarkerKind.Disappear));
            Assert.That(path.Markers.Select(marker => marker.Kind), Does.Contain(RouteMarkerKind.Appear));
            Assert.That(draft.Handles().Select(node => node.Index), Is.EqualTo(new[] { -1, 0, 3, -2 }));
        }

        [Test]
        public void TwoDocumentDraftsStaySeparateAndRestoreAfterInspectorReload()
        {
            var workspace = ScriptableObject.CreateInstance<MapWorkspaceWindow>();
            var left = ScriptableObject.CreateInstance<MapEditorWindow>();
            var right = ScriptableObject.CreateInstance<MapEditorWindow>();
            try
            {
                var leftMap = LevelDocument.Create(6, 5);
                left.LoadWorkspaceDocument(workspace, "left", leftMap.ToJson(), "", "");
                right.LoadWorkspaceDocument(workspace, "right", LevelDocument.Create(7, 8).ToJson(), "", "");
                left.BeginRouteDraft("routes", -1, "WALK");
                left.PlaceRouteCell(new Vector2Int(1, 2));
                right.BeginRouteDraft("extraRoutes", -1, "FLY");
                right.PlaceRouteCell(new Vector2Int(3, 4));
                var restored = left.CopyToWorkspace(workspace);
                try
                {
                    Assert.That(restored.ActiveRouteDraft!.Library, Is.EqualTo("routes"));
                    Assert.That(restored.ActiveRouteDraft.TryNodeCell(RouteEditDraft.Start, out var point), Is.True);
                    Assert.That(point, Is.EqualTo(new Vector2Int(1, 2)));
                    Assert.That(restored.WorkspaceDocument.ToJson(), Is.EqualTo(leftMap.ToJson()));
                }
                finally { UnityEngine.Object.DestroyImmediate(restored); }
                Assert.That(right.ActiveRouteDraft!.TryNodeCell(RouteEditDraft.Start, out var other), Is.True);
                Assert.That(other, Is.EqualTo(new Vector2Int(3, 4)));
                right.SetWorkspaceEditLock(true);
                Assert.Throws<EditorOperationRejectedException>(() => right.PlaceRouteCell(Vector2Int.zero));
                Assert.That(left.ActiveRouteDraft!.Complete, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(left);
                UnityEngine.Object.DestroyImmediate(right);
                UnityEngine.Object.DestroyImmediate(workspace);
            }
        }
    }
}
