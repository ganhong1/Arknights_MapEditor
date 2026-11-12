#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Core;
using Arknights.MapEditor.Catalog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    public sealed partial class MapEditorWindow
    {
        [UnityEngine.SerializeField] private bool tileDataCardView;
        [UnityEngine.SerializeField] private bool automaticTileData = true;
        [UnityEngine.SerializeField] private int selectedTileData;
        private Action? openTileDataPreview;
        internal Func<int, int>? TileReplacementPrompt { get; set; }
        private int ConfirmTileReplacement(int count)
            => !automaticTileData ? 2 : TileReplacementPrompt?.Invoke(count) ?? EditorUtility.DisplayDialogComplex("确认置换与清理",
                $"将置换 {count} 格的 TileData 绑定。确定后仅清理本次失去全部绑定的旧配置；仍被其他格引用的配置不会删除。",
                "确定", "取消", "替换但保留原 TileData");

        private void PlaceManagedTiles(IReadOnlyList<UnityEngine.Vector2Int> cells)
        {
            if (!automaticTileData) throw new InvalidOperationException("自动绑定已关闭，请在 TileData 面板显式绑定配置与坐标。");
            RequireAppliedDraft();
            if (InspectorStore.HasChanges("tileData")) throw new InvalidOperationException("请先应用或保留并重新读取 TileData 草稿，再放置地块。");
            var positions = cells.Select(cell => (Row: cell.y, Col: cell.x)).Distinct().ToArray();
            if (positions.Length == 0) return;
            var next = session!.Document.PlaceTileConfigurations(Brush(), positions, false);
            var choice = ConfirmTileReplacement(positions.Length);
            if (choice == 1) return;
            if (choice == 0) next = session.Document.PlaceTileConfigurations(Brush(), positions, true);
            CommitEdit(next, "放置并绑定 TileData", null);
        }

        private void BuildTileDataPanel(VisualElement parent)
        {
            if (InspectorStore.TryReadTarget("tileData", out var target) && target.StartsWith("tile:", StringComparison.Ordinal) &&
                int.TryParse(target.Substring(5), out var index)) selectedTileData = index;
            var preview = new TileDataPreview(this, () => session!.Document, SelectedTerrain, tileDataCardView,
                showCards => tileDataCardView = showCards);
            openTileDataPreview = preview.Open;
            parent.Add(preview);
        }

        private sealed class TileDataPreview : VisualElement
        {
            private const int CardsPerPage = 24;
            private readonly MapEditorWindow owner;
            private const string DraftSlot = "tileData";
            private bool draftBound;
            private readonly TextField multiple = new TextField("多格坐标（行,列；行,列）") { name = "tiledata-bind-many" };
            private readonly Func<LevelDocument> readDocument;
            private readonly Action<bool> rememberView;
            private readonly VisualElement cardPanel = new VisualElement { name = "tiledata-preview-card-panel" };
            private readonly ScrollView cardScroll = new ScrollView();
            private readonly VisualElement cards = new VisualElement { name = "tiledata-preview-cards" };
            private readonly Label cardPageLabel = new Label();
            private readonly Button previousCards;
            private readonly Button nextCards;
            private readonly Button listViewButton;
            private readonly Button cardViewButton;
            private int cardPage;
            private bool showingCards;
            private readonly Func<JObject> readBrush;
            private readonly List<int> entries = new List<int>();
            private readonly ListView list;
            private readonly TextField json = new TextField("配置 JSON") { multiline = true, name = "tiledata-preview-json" };
            private readonly IntegerField row = new IntegerField("行（自底向上）") { name = "tiledata-preview-row" };
            private readonly IntegerField column = new IntegerField("列") { name = "tiledata-preview-column" };
            private readonly Toggle automatic = new Toggle("自动绑定、置换与清理") { name = "tiledata-preview-automatic", value = true };
            private readonly Label summary = new Label();
            private readonly Label coordinates = new Label { name = "tiledata-preview-coordinates" };
            private readonly List<string> boundCells = new List<string>();
            private readonly Foldout allCoordinates = new Foldout { text = "查看全部绑定坐标", value = false };
            private readonly ListView coordinateList;
            private readonly Dictionary<string, TextField> readOnlyFields = new Dictionary<string, TextField>();
            private readonly Label snapshot = new Label();
            private readonly TerrainBlackboardFields blackboardFields;
            private readonly Dictionary<string, PopupField<string>> attributes = new Dictionary<string, PopupField<string>>();
            private readonly Label feedback = new Label();
            private readonly VisualElement details = new VisualElement();
            private readonly Button paint;
            private LevelDocument? source;
            private JArray tiles = new JArray();
            private JArray grid = new JArray();
            private int selected = -1;

            public TileDataPreview(MapEditorWindow owner, Func<LevelDocument> readDocument, Func<JObject> readBrush,
                bool showCards, Action<bool> rememberView)
            {
                this.owner = owner;
                this.readDocument = readDocument;
                this.readBrush = readBrush;
                this.rememberView = rememberView;
                name = "tiledata-preview";
                var heading = new Label("TileData 配置管理");
                heading.AddToClassList("brush-title");
                Add(heading);
                Add(new HelpBox("配置管理 · 应用、增删和绑定写入当前地图，可撤销。编辑共享配置会同步影响全部绑定格；需要独立修改请先复制配置。", HelpBoxMessageType.Info));
                snapshot.AddToClassList("tiledata-preview-message");
                Add(snapshot);
                var toolbar = Toolbar(this);
                AddAction(toolbar, "重置为地图当前配置", Load);
                AddAction(toolbar, "复制当前地形为新配置", () => AddEntry(readBrush())).tooltip = "新配置初始未绑定格子";
                Add(automatic);
                automatic.SetValueWithoutNotify(owner.automaticTileData);
                automatic.AddToClassList("catalog-filter");

                var viewToolbar = Toolbar(this);
                listViewButton = new Button(() => ShowView(false)) { text = "列表", name = "tiledata-preview-show-list" };
                cardViewButton = new Button(() => ShowView(true)) { text = "块状", name = "tiledata-preview-show-cards" };
                viewToolbar.Add(listViewButton);
                viewToolbar.Add(cardViewButton);

                list = new ListView(entries, 28, () => new Label(), (element, index) =>
                {
                    var id = entries[index];
                    ((Label)element).text = $"#{id}  {TileKey(id)}";
                }) { name = "tiledata-preview-list", selectionType = SelectionType.Single };
                list.style.height = 140;
                list.onSelectionChange += items =>
                {
                    if (items.FirstOrDefault() is int id && id != selected) Select(id);
                };
                Add(list);

                cards.AddToClassList("tiledata-preview-cards");
                cardScroll.AddToClassList("tiledata-preview-card-scroll");
                cardScroll.Add(cards);
                cardPanel.Add(cardScroll);
                var pageToolbar = Toolbar(cardPanel);
                previousCards = new Button(() => ChangeCardPage(-1)) { text = "上一页" };
                nextCards = new Button(() => ChangeCardPage(1)) { text = "下一页" };
                cardPageLabel.AddToClassList("tiledata-preview-message");
                pageToolbar.Add(previousCards);
                pageToolbar.Add(cardPageLabel);
                pageToolbar.Add(nextCards);
                Add(cardPanel);
                ShowView(showCards);

                summary.AddToClassList("tiledata-preview-message");
                summary.AddToClassList("tiledata-preview-important");
                details.Add(summary);
                coordinates.AddToClassList("tiledata-preview-important");
                details.Add(coordinates);
                coordinateList = new ListView(boundCells, 22, () =>
                {
                    var label = new Label();
                    label.AddToClassList("tiledata-preview-important");
                    return label;
                }, (element, index) => ((Label)element).text = boundCells[index]);
                coordinateList.style.height = 110;
                allCoordinates.Add(coordinateList);
                details.Add(allCoordinates);

                AddReadOnlyField("tileKey", "地块键");
                AddAttribute("heightType", "高度", "LOWLAND", "HIGHLAND");
                AddAttribute("buildableType", "部署", "NONE", "MELEE", "RANGED", "ALL");
                AddAttribute("passableMask", "通行", "NONE", "WALK_ONLY", "FLY_ONLY", "ALL");
                AddAttribute("playerSideMask", "玩家侧", "ALL", "SIDE_A", "SIDE_B", "NONE");
                AddReadOnlyField("advancedBuildableMask", "高级部署限制 · 运行字段");
                AddReadOnlyField("blackboard", "黑板", true);
                blackboardFields = new TerrainBlackboardFields(WriteBlackboardValue);
                details.Add(blackboardFields);
                AddReadOnlyField("effects", "特效", true);
                json.style.minHeight = 125;
                var raw = new Foldout { text = "高级 · 编辑完整配置 JSON", value = false };
                raw.Add(json);
                details.Add(raw);
                json.RegisterValueChangedCallback(_ => { SaveDraft(); RefreshAttributes(); });
                var edits = Toolbar(details);
                AddAction(edits, "应用配置", Apply);
                AddAction(edits, "复制为独立配置", () => AddEntry((JObject)tiles[selected]!.DeepClone()));
                AddAction(edits, "删除未绑定配置", Delete);
                Add(details);

                Add(new Label("按坐标绑定"));
                Add(row);
                Add(column);
                Add(multiple);
                var bindings = Toolbar(this);
                paint = AddAction(bindings, "将当前地形绑定到坐标", () => Bind(readBrush(), null));
                AddAction(bindings, "绑定所选配置", () => Bind(null, selected));
                AddAction(bindings, "绑定到多个格", BindMultiple);
                AddAction(bindings, "置空", () => Bind(LevelDocument.Create(1, 1).GetCell(0, 0), null, true));
                paint.SetEnabled(automatic.value);
                automatic.RegisterValueChangedCallback(change => { owner.automaticTileData = change.newValue; paint.SetEnabled(change.newValue); });
                feedback.AddToClassList("tiledata-preview-message");
                Add(feedback);
            }

            public void Open()
            {
                if (source == null) Load();
                else if (source.ToJson() != readDocument().ToJson() && !owner.InspectorStore.HasChanges(DraftSlot)) Load();
                else RefreshSnapshotLabel();
            }

            private string TileKey(int id)
            {
                var key = tiles[id]?["tileKey"];
                return key?.Type == JTokenType.String ? key.Value<string>()! : "未指定地块";
            }

            private void ShowView(bool showCards)
            {
                showingCards = showCards;
                rememberView(showCards);
                list.style.display = showCards ? DisplayStyle.None : DisplayStyle.Flex;
                cardPanel.style.display = showCards ? DisplayStyle.Flex : DisplayStyle.None;
                listViewButton.EnableInClassList("selected-tab", !showCards);
                cardViewButton.EnableInClassList("selected-tab", showCards);
                if (!showCards) return;
                var index = entries.IndexOf(selected);
                if (index >= 0) cardPage = index / CardsPerPage;
                RebuildCards();
            }

            private void ChangeCardPage(int delta)
            {
                cardPage += delta;
                cardScroll.scrollOffset = UnityEngine.Vector2.zero;
                RebuildCards();
            }

            private void RebuildCards()
            {
                var pageCount = Math.Max(1, (entries.Count + CardsPerPage - 1) / CardsPerPage);
                cardPage = Math.Max(0, Math.Min(cardPage, pageCount - 1));
                cards.Clear();
                foreach (var id in entries.Skip(cardPage * CardsPerPage).Take(CardsPerPage))
                {
                    var card = new Button(() =>
                    {
                        if (id != selected) Select(id);
                    })
                    {
                        text = $"#{id}\n{TileKey(id)}",
                        name = "tiledata-preview-card-" + id
                    };
                    card.AddToClassList("tiledata-preview-card");
                    card.EnableInClassList("selected-tab", selected == id);
                    cards.Add(card);
                }
                cardPageLabel.text = $"{cardPage + 1} / {pageCount} 页 · {entries.Count} 条";
                previousCards.SetEnabled(cardPage > 0);
                nextCards.SetEnabled(cardPage + 1 < pageCount);
            }

            private void AddReadOnlyField(string key, string title, bool multiline = false)
            {
                var field = new TextField { name = "tiledata-field-" + key, isReadOnly = true, multiline = multiline };
                if (multiline) field.AddToClassList("tiledata-preview-data");
                AddFieldRow(key, title, field);
                readOnlyFields.Add(key, field);
            }

            private void AddFieldRow(string key, string title, VisualElement input)
            {
                var section = new VisualElement { name = "tiledata-row-" + key };
                section.Add(new Label($"{key} · {title}"));
                section.Add(input);
                details.Add(section);
            }

            private void AddAttribute(string key, string title, params string[] values)
            {
                var choices = new List<string>(values) { "（保留原值）" };
                var field = new PopupField<string>(choices, choices.Count - 1)
                {
                    name = "tiledata-field-" + key
                };
                attributes.Add(key, field);
                AddFieldRow(key, title, field);
                field.RegisterValueChangedCallback(change =>
                {
                    if (!values.Contains(change.newValue)) { RefreshAttributes(); return; }
                    try
                    {
                        var tile = ParseDraft();
                        tile[key] = change.newValue;
                        json.SetValueWithoutNotify(tile.ToString(Formatting.Indented));
                        SaveDraft();
                    }
                    catch (Exception error) { feedback.text = error.Message; }
                    RefreshAttributes();
                });
            }

            private void RefreshAttributes()
            {
                JObject? tile;
                try { tile = JObject.Parse(json.value); }
                catch (JsonReaderException) { tile = null; }
                blackboardFields.Show(tile);
                foreach (var pair in readOnlyFields)
                {
                    var token = tile?[pair.Key];
                    var value = tile == null ? "（JSON 无法读取）" :
                        token == null ? "（未设置）" :
                        token.Type == JTokenType.String ? token.Value<string>()! : token.ToString(Formatting.Indented);
                    pair.Value.SetValueWithoutNotify(value);
                }
                foreach (var pair in attributes)
                {
                    var token = tile?[pair.Key];
                    var value = token?.Type == JTokenType.String ? token.Value<string>() : null;
                    var choices = pair.Value.choices;
                    var original = token == null ? "（未设置）" : token.ToString(Formatting.None);
                    if (value == null || !choices.Contains(value) || value == choices[choices.Count - 1])
                    {
                        choices[choices.Count - 1] = original;
                        value = original;
                    }
                    pair.Value.SetValueWithoutNotify(value);
                    pair.Value.SetEnabled(tile != null);
                }
            }

            private void WriteBlackboardValue(string key, string field, JToken value)
            {
                try
                {
                    var tile = ParseDraft();
                    if (!TerrainBlackboardCatalog.SetValue(tile, key, field, value))
                        throw new FormatException("黑板格式异常或键重复，请在高级 JSON 中核对原值。");
                    json.SetValueWithoutNotify(tile.ToString(Formatting.Indented));
                    SaveDraft();
                    feedback.text = "";
                }
                catch (Exception error) { feedback.text = error.Message; }
                RefreshAttributes();
            }

            private void RefreshSnapshotLabel()
            {
                if (source == null) return;
                snapshot.text = $"当前配置 · {source.Width} × {source.Height}";
                if (!ReferenceEquals(source, readDocument())) snapshot.text += " · 地图已有变化，可重新读取";
            }

            private static VisualElement Toolbar(VisualElement parent)
            {
                var toolbar = new VisualElement();
                toolbar.AddToClassList("tiledata-preview-toolbar");
                parent.Add(toolbar);
                return toolbar;
            }

            private Button AddAction(VisualElement parent, string title, Action action)
            {
                var button = new Button(() =>
                {
                    feedback.text = "";
                    try { action(); }
                    catch (Exception error) { feedback.text = error.Message; }
                }) { text = title };
                parent.Add(button);
                return button;
            }

            private void Load()
            {
                var document = readDocument();
                if (source != null && owner.InspectorStore.HasChanges(DraftSlot) &&
                    !owner.Dialogs.Discard("重新读取 TileData", "将丢弃本配置尚未应用的修改，是否继续？")) return;
                if (source != null) owner.InspectorStore.Remove(DraftSlot);
                var map = (JObject)document.GetSection("mapData")!;
                source = document;
                tiles = (JArray)map["tiles"]!;
                grid = (JArray)map["map"]!;
                row.SetValueWithoutNotify(0);
                column.SetValueWithoutNotify(0);
                RefreshEntries(owner.selectedTileData);
                RefreshSnapshotLabel();
                feedback.text = "已读取当前地图。";
            }

            private void RefreshEntries(int id)
            {
                entries.Clear();
                for (var i = 0; i < tiles.Count; i++)
                    if (tiles[i] is JObject) entries.Add(i);
                list.Rebuild();
                if (!entries.Contains(id)) id = entries.Count == 0 ? -1 : entries[0];
                if (showingCards && id >= 0) cardPage = entries.IndexOf(id) / CardsPerPage;
                Select(id);
                if (showingCards) RebuildCards();
            }

            private void Select(int id)
            {
                if (draftBound && selected != id && owner.InspectorStore.HasChanges(DraftSlot))
                {
                    if (!owner.Dialogs.Discard("切换 TileData", "当前配置有未应用修改，是否丢弃后切换？"))
                    { list.SetSelectionWithoutNotify(new[] { entries.IndexOf(selected) }); return; }
                    owner.InspectorStore.Remove(DraftSlot);
                }
                else if (draftBound && selected != id) owner.InspectorStore.Remove(DraftSlot);
                selected = id;
                owner.selectedTileData = id;
                details.SetEnabled(id >= 0);
                json.SetValueWithoutNotify(id < 0 ? "" : tiles[id]!.ToString(Formatting.Indented));
                if (id >= 0)
                {
                    owner.BindLocalDraft(DraftSlot, json.value, "tile:" + id, raw => { json.SetValueWithoutNotify(raw); RefreshAttributes(); });
                    draftBound = true;
                }
                RefreshAttributes();
                if (id < 0) list.ClearSelection();
                else list.SetSelectionWithoutNotify(new[] { entries.IndexOf(id) });
                if (showingCards)
                    foreach (var card in cards.Children())
                        card.EnableInClassList("selected-tab", card.name == "tiledata-preview-card-" + id);
                boundCells.Clear();
                if (id >= 0) boundCells.AddRange(References(id));
                summary.text = id < 0 ? "没有配置" : $"配置 #{id} · {boundCells.Count} 个绑定格";
                coordinates.text = boundCells.Count == 0 ? "绑定格坐标 · 无" :
                    "绑定格坐标 · " + string.Join("、", boundCells.Take(12)) + (boundCells.Count > 12 ? " …" : "");
                allCoordinates.style.display = boundCells.Count > 12 ? DisplayStyle.Flex : DisplayStyle.None;
                coordinateList.Rebuild();
            }

            private List<string> References(int id)
            {
                var cells = new List<string>();
                for (var y = 0; y < grid.Count; y++)
                {
                    var columns = (JArray)grid[grid.Count - 1 - y]!;
                    for (var x = 0; x < columns.Count; x++)
                        if (columns[x]!.Value<int>() == id) cells.Add($"({y}, {x})");
                }
                return cells;
            }

            private void SaveDraft()
            {
                if (draftBound) owner.CaptureLocalDraft(DraftSlot, json.value);
            }

            private void RequireCurrent(bool allowDraft = false)
            {
                owner.RequireWorkspaceEditable();
                owner.RequireAppliedDraft();
                if (source == null || source.ToJson() != readDocument().ToJson())
                    throw new InvalidOperationException("地图已改变，请先保留草稿并重新读取当前配置。");
                if (!allowDraft && owner.InspectorStore.HasChanges(DraftSlot))
                    throw new InvalidOperationException("请先应用配置修改，再执行增删或绑定。");
            }

            private void Commit(LevelDocument next, int index, string label)
            {
                owner.CommitEdit(next, label, null);
                owner.InspectorStore.Remove(DraftSlot);
                draftBound = false;
                var map = (JObject)next.GetSection("mapData")!;
                source = next; tiles = (JArray)map["tiles"]!; grid = (JArray)map["map"]!;
                RefreshEntries(index);
                owner.PersistInspectorDrafts();
                RefreshSnapshotLabel();
                feedback.text = "已写入当前地图，可撤销。";
            }

            private void AddEntry(JObject tile)
            {
                RequireCurrent();
                Commit(source!.AddTileConfiguration(tile), tiles.Count, "新增 TileData");
            }

            private JObject ParseDraft()
                => LevelDocument.Create(1, 1).WithSection("tile", json.value).GetSection("tile") as JObject
                    ?? throw new FormatException("TileData 必须是 JSON 对象。");

            private void Apply()
            {
                RequireCurrent(true);
                owner.RequireLocalDraft(DraftSlot, "tile:" + selected);
                var tile = ParseDraft();
                var references = References(selected).Count;
                if (references > 1 && !EditorUtility.DisplayDialog("应用共享配置",
                    $"此配置绑定了 {references} 格。应用后所有绑定格同步变化。", "应用", "取消")) return;
                Commit(source!.EditTileConfiguration(selected, tile), selected, "编辑共享 TileData");
            }

            private void Delete()
            {
                RequireCurrent();
                if (References(selected).Count > 0)
                    throw new InvalidOperationException("这条配置仍有绑定格，请先绑定替代配置。");
                if (!EditorUtility.DisplayDialog("删除配置", $"删除未绑定配置 #{selected}？", "删除", "取消")) return;
                Commit(source!.DeleteTileConfiguration(selected), 0, "删除 TileData");
            }

            private void BindMultiple()
            {
                var cells = new List<(int Row, int Col)>();
                foreach (var part in multiple.value.Split(new[] { ';', '；', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var pair = part.Trim().Trim('(', ')', '（', '）').Split(new[] { ',', '，' });
                    if (pair.Length != 2 || !int.TryParse(pair[0], out var y) || !int.TryParse(pair[1], out var x))
                        throw new FormatException("坐标格式：行,列；行,列（行从底部开始）。");
                    cells.Add((y, x));
                }
                if (cells.Count == 0) throw new FormatException("请填写至少一个坐标。");
                BindCells(null, selected, cells);
            }

            private void Bind(JObject? tile, int? target, bool erase = false)
                => BindCells(tile, target, new[] { (row.value, column.value) });

            private void BindCells(JObject? tile, int? target, IEnumerable<(int Row, int Col)> coordinates)
            {
                RequireCurrent();
                var cells = coordinates.Distinct().ToArray();
                // Build and validate before displaying confirmation; failure never changes the document.
                var next = target.HasValue ? source!.BindTileConfiguration(target.Value, cells, false)
                    : source!.PlaceTileConfigurations(tile!, cells, false);
                var choice = owner.ConfirmTileReplacement(cells.Length);
                if (choice == 1) return;
                var clean = automatic.value && choice == 0;
                if (clean) next = target.HasValue ? source!.BindTileConfiguration(target.Value, cells, true)
                    : source!.PlaceTileConfigurations(tile!, cells, true);
                var map = (JObject)next.GetSection("mapData")!;
                var first = cells[0];
                var selectedIndex = map["map"]![next.Height - 1 - first.Row]![first.Col]!.Value<int>();
                Commit(next, selectedIndex, "绑定 TileData");
            }
        }
    }
}
