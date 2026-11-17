#nullable enable
using System;
using System.Collections.Generic;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        private RoutePreviewPlayback? routePreview;
        [SerializeField] private RoutePreviewSettings routePreviewSettings = new();
        [SerializeField] private bool routePlaybackSelected;

        internal RoutePreviewPlayback RoutePreview
        {
            get
            {
                if (routePreview != null) return routePreview;
                var playback = new RoutePreviewPlayback();
                routePreview = playback;
                playback.RestoreSettings(routePreviewSettings, WorkspaceDocument);
                playback.Changed += () => routePreviewSettings = playback.CaptureSettings();
                return playback;
            }
        }

        internal bool DrawRoutePreview()
        {
            if (session == null) return false;
            if (RoutePreview.Visible) RoutePreview.Draw();
            return true;
        }

        private void BuildRoutesPanel(VisualElement parent)
        {
            var edit = RouteSection("路线编辑", !routePlaybackSelected, "route-edit-section");
            BuildRouteEditing(edit);
            parent.Add(edit);
            var preview = RouteSection("路线演示", routePlaybackSelected, "route-preview-section");
            preview.Add(new RoutePreviewPanel(RoutePreview, () => WorkspaceDocument));
            parent.Add(preview);
            edit.RegisterValueChangedCallback(change =>
            {
                if (change.target != edit || !change.newValue) return;
                routePlaybackSelected = false;
                preview.SetValueWithoutNotify(false);
                SceneView.RepaintAll();
            });
            preview.RegisterValueChangedCallback(change =>
            {
                if (change.target != preview || !change.newValue) return;
                routePlaybackSelected = true;
                edit.SetValueWithoutNotify(false);
                ExitRouteScene();
            });
            var fold = RouteSection("旧版坐标与 JSON 编辑", false, "route-editor");
            var library = new PopupField<string>("路线库", new List<string>{"routes", "extraRoutes"}, 0);
            var index = new IntegerField("路线索引"); var target = new IntegerField("重排目标索引");
            var startRow = new FloatField("起点 row"); var startCol = new FloatField("起点 col");
            var endRow = new FloatField("终点 row"); var endCol = new FloatField("终点 col");
            var checkpoints = new TextField("检查点 JSON") { multiline = true, value = "[]" };
            var routeJson = new TextField("完整路线 JSON") { multiline = true };
            fold.Add(library); fold.Add(index); fold.Add(target); fold.Add(startRow); fold.Add(startCol); fold.Add(endRow); fold.Add(endCol); fold.Add(checkpoints); fold.Add(routeJson);
            Func<string> routeTarget = () => new JArray(library.value, index.value).ToString(Newtonsoft.Json.Formatting.None);
            BindLocalTextField("route", routeJson, routeTarget);
            Func<string> pointsRaw = () => new JObject { ["startRow"] = startRow.value, ["startCol"] = startCol.value, ["endRow"] = endRow.value, ["endCol"] = endCol.value, ["checkpoints"] = checkpoints.value }.ToString(Newtonsoft.Json.Formatting.None);
            // Populate the initial coordinate editor from the actual selected route.
            // Opening the inspector must not turn untouched controls into zero coordinates.
            var initialRoutes = session!.Document.GetSection(library.value) as JArray;
            if (initialRoutes != null && index.value >= 0 && index.value < initialRoutes.Count && initialRoutes[index.value] is JObject initialRoute)
            {
                startRow.SetValueWithoutNotify((float?)initialRoute["startPosition"]?["row"] ?? 0);
                startCol.SetValueWithoutNotify((float?)initialRoute["startPosition"]?["col"] ?? 0);
                endRow.SetValueWithoutNotify((float?)initialRoute["endPosition"]?["row"] ?? 0);
                endCol.SetValueWithoutNotify((float?)initialRoute["endPosition"]?["col"] ?? 0);
                checkpoints.SetValueWithoutNotify(initialRoute["checkpoints"]?.ToString() ?? "[]");
            }
            BindLocalDraft("routePoints", pointsRaw(), routeTarget(), raw =>
            {
                if (raw.Length == 0) { checkpoints.SetValueWithoutNotify(""); return; }
                var saved = JObject.Parse(raw);
                startRow.SetValueWithoutNotify((float)saved["startRow"]!); startCol.SetValueWithoutNotify((float)saved["startCol"]!);
                endRow.SetValueWithoutNotify((float)saved["endRow"]!); endCol.SetValueWithoutNotify((float)saved["endCol"]!);
                checkpoints.SetValueWithoutNotify((string)saved["checkpoints"]!);
            });
            startRow.RegisterValueChangedCallback(_ => CaptureLocalDraft("routePoints", pointsRaw()));
            startCol.RegisterValueChangedCallback(_ => CaptureLocalDraft("routePoints", pointsRaw()));
            endRow.RegisterValueChangedCallback(_ => CaptureLocalDraft("routePoints", pointsRaw()));
            endCol.RegisterValueChangedCallback(_ => CaptureLocalDraft("routePoints", pointsRaw()));
            checkpoints.RegisterValueChangedCallback(_ => CaptureLocalDraft("routePoints", pointsRaw()));
            Action load = () =>
            {
                var routes = session!.Document.GetSection(library.value) as JArray ?? throw new FormatException("路线库不是数组。");
                if (index.value < 0 || index.value >= routes.Count) throw new ArgumentOutOfRangeException("路线索引");
                var route = routes[index.value] as JObject ?? throw new FormatException("路线不是对象。");
                ReloadLocalDraft("route", routeTarget(), route.ToString());
                var points = new JObject { ["startRow"] = (float?)route["startPosition"]?["row"] ?? 0, ["startCol"] = (float?)route["startPosition"]?["col"] ?? 0,
                    ["endRow"] = (float?)route["endPosition"]?["row"] ?? 0, ["endCol"] = (float?)route["endPosition"]?["col"] ?? 0,
                    ["checkpoints"] = route["checkpoints"]?.ToString() ?? "[]" };
                ReloadLocalDraft("routePoints", routeTarget(), points.ToString(Newtonsoft.Json.Formatting.None));
            };
            AddButton(fold, "读取路线", load);
            AddButton(fold, "添加路线", () =>
            {
                var routes = session!.Document.GetSection(library.value) as JArray ?? new JArray();
                routes.Add(new JObject { ["motionMode"] = "WALK", ["allowDiagonalMove"] = true,
                    ["startPosition"] = new JObject { ["row"] = 0, ["col"] = 0 },
                    ["endPosition"] = new JObject { ["row"] = session.Document.Height - 1, ["col"] = session.Document.Width - 1 },
                    ["spawnOffset"] = new JObject { ["x"] = 0, ["y"] = 0 }, ["spawnRandomRange"] = new JObject { ["x"] = 0, ["y"] = 0 }, ["checkpoints"] = new JArray() });
                CommitEdit(session.Document.WithSection(library.value, routes.ToString()), "添加路线", null); index.value = routes.Count - 1; load();
            });
            AddButton(fold, "应用起终点与检查点", () =>
            {
                RequireLocalDraft("routePoints", routeTarget()); var routes = (JArray)session!.Document.GetSection(library.value)!;
                if (index.value < 0 || index.value >= routes.Count) throw new ArgumentOutOfRangeException("路线索引");
                var route = (JObject)routes[index.value];
                foreach (var name in new[]{"startPosition", "endPosition"}) if (!(route[name] is JObject)) throw new FormatException("路线缺少" + name);
                route["startPosition"]!["row"] = startRow.value; route["startPosition"]!["col"] = startCol.value;
                route["endPosition"]!["row"] = endRow.value; route["endPosition"]!["col"] = endCol.value;
                var parsed = LevelDocument.Create(1, 1).WithSection("fragment", checkpoints.value).GetSection("fragment");
                if (!(parsed is JArray)) throw new FormatException("检查点必须是数组。"); route["checkpoints"] = parsed;
                CommitEdit(session.Document.WithSection(library.value, routes.ToString()), "编辑路线", null); AppliedLocalDraft("routePoints");
            });
            AddButton(fold, "应用完整路线 JSON", () =>
            {
                RequireLocalDraft("route", routeTarget()); var routes = (JArray)session!.Document.GetSection(library.value)!;
                var route = LevelDocument.Create(1, 1).WithSection("fragment", routeJson.value).GetSection("fragment");
                if (!(route is JObject)) throw new FormatException("路线必须是对象。"); routes[index.value] = route;
                CommitEdit(session.Document.WithSection(library.value, routes.ToString()), "编辑路线 JSON", null); AppliedLocalDraft("route");
            });
            AddButton(fold, "移动路线并重写引用", () => { CommitEdit(session!.Document.MoveRoute(library.value,index.value,target.value), "重排路线", null); index.value = target.value; load(); });
            parent.Add(fold);
        }

        private static Foldout RouteSection(string title, bool expanded = false, string? name = null)
        {
            var section = new Foldout { text = title, value = expanded };
            if (name != null) section.name = name;
            section.AddToClassList("route-section");
            return section;
        }

        internal static VisualElement AddRouteCard(VisualElement parent, string title)
        {
            var card = new VisualElement();
            card.AddToClassList("route-card");
            var heading = new Label(title);
            heading.AddToClassList("route-card-title");
            card.Add(heading);
            parent.Add(card);
            return card;
        }

    }
}
