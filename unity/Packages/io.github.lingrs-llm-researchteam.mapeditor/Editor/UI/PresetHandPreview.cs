#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class PresetHandPreview : VisualElement
    {
        internal const float AuthoredHeight = 184;
        private const float PreferredWidth = 178;
        private const float MinimumWidth = 109;
        private const float Inset = 8;
        private const float ScrollbarHeight = 10;
        private readonly ScrollView strip = new(ScrollViewMode.Horizontal) { name = "map-hand-scroll" };
        private readonly List<DeploymentCard> cards = new();
        private readonly Func<string, string, int, bool>? select;
        private LevelDocument? document;
        private VisualElement? host;
        private string layer = "";
        private (string Role, int Index)? selected;
        private bool visible;
        private bool revealRight;
        private bool canScroll;

        internal PresetHandPreview(Func<string, string, int, bool>? select = null)
        {
            this.select = select;
            name = "map-preset-hand";
            pickingMode = PickingMode.Ignore;
            AddToClassList("preset-hand-preview");
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/UI/Workbench.uss");
            if (sheet != null) styleSheets.Add(sheet);
            strip.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            strip.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            strip.contentContainer.style.flexDirection = FlexDirection.Row;
            strip.RegisterCallback<WheelEvent>(ScrollWheel, TrickleDown.TrickleDown);
            Add(strip);
            style.display = DisplayStyle.None;
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                host = parent;
                host?.RegisterCallback<GeometryChangedEvent>(HostResized);
                Resize();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                host?.UnregisterCallback<GeometryChangedEvent>(HostResized);
                host = null;
            });
        }

        internal static (float CardWidth, float CardHeight, float StripWidth, bool Scroll) Measure(
            float viewportWidth, float viewportHeight, int count)
        {
            if (count == 0) return default;
            var available = Mathf.Max(1, viewportWidth - 2 * Inset);
            var scale = Mathf.Clamp(viewportHeight / 1080f, .30f, .65f);
            var width = Mathf.Clamp(available / count, MinimumWidth * scale, PreferredWidth * scale);
            return (width, AuthoredHeight * scale, Mathf.Min(available, width * count), width * count > available + .01f);
        }

        internal void Update(LevelDocument value, string presetLayer, bool show, (string Role, int Index)? selection = null)
        {
            if (show && (!ReferenceEquals(value, document) || presetLayer != layer))
            {
                revealRight = document != value || layer != presetLayer;
                document = value;
                layer = presetLayer;
                cards.Clear();
                strip.Clear();
                if (value.GetSection(layer) is JObject presets)
                {
                    AddCards("characterCards", presets["characterCards"] as JArray, true);
                    AddCards("tokenCards", presets["tokenCards"] as JArray, false);
                }
                selected = selection;
                RefreshSelection();
                visible = show && cards.Count > 0;
                style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                Resize();
                return;
            }
            var nextVisible = show && cards.Count > 0;
            if (visible != nextVisible)
            {
                visible = nextVisible;
                style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible) Resize();
            }
            if (selected == selection) return;
            selected = selection;
            RefreshSelection();
        }

        private void AddCards(string role, JArray? source, bool isOperator)
        {
            if (source == null) return;
            for (var index = 0; index < source.Count; index++)
            {
                var card = new DeploymentCard(source[index] as JObject, role, index, isOperator)
                    { name = $"map-hand-card-{cards.Count}" };
                card.clicked += () =>
                {
                    if (select?.Invoke(layer, card.Role, card.Index) != true) return;
                    selected = (card.Role, card.Index);
                    RefreshSelection();
                };
                cards.Add(card);
                strip.Add(card);
            }
        }

        private void RefreshSelection()
        {
            foreach (var card in cards) card.Selected(selected == (card.Role, card.Index));
        }

        private void HostResized(GeometryChangedEvent evt)
        {
            if (evt.target == host) Resize();
        }

        private void Resize()
        {
            if (!visible || host == null || host.contentRect.width <= 0 || host.contentRect.height <= 0) return;
            var layout = Measure(host.contentRect.width, host.contentRect.height, cards.Count);
            var rowHeight = layout.CardHeight * (1 + 20 / AuthoredHeight) + 3;
            foreach (var card in cards) card.Resize(layout.CardWidth, layout.CardHeight);
            style.width = layout.StripWidth;
            style.height = rowHeight + (layout.Scroll ? ScrollbarHeight : 0);
            strip.contentContainer.style.height = rowHeight;
            strip.horizontalScrollerVisibility = layout.Scroll ? ScrollerVisibility.AlwaysVisible : ScrollerVisibility.Hidden;
            if (layout.Scroll && (revealRight || !canScroll))
            {
                schedule.Execute(() =>
                {
                    if (canScroll) strip.scrollOffset = new Vector2(strip.horizontalScroller.highValue, 0);
                });
            }
            canScroll = layout.Scroll;
            revealRight = false;
        }

        private void ScrollWheel(WheelEvent evt)
        {
            if (!canScroll) return;
            var delta = Mathf.Abs(evt.delta.x) > Mathf.Abs(evt.delta.y) ? evt.delta.x : evt.delta.y;
            strip.scrollOffset = new Vector2(Mathf.Clamp(strip.scrollOffset.x + delta * 18, 0, strip.horizontalScroller.highValue), 0);
            evt.StopImmediatePropagation();
        }

        private sealed class DeploymentCard : Button
        {
            internal string Role { get; }
            internal int Index { get; }
            private readonly Image portrait;
            private readonly Image rarity;
            private readonly Image profession;
            private readonly Image? elite;
            private readonly Image? quantityBack;
            private readonly Label? quantity;
            private readonly Image focus;
            private bool selected;
            private float lift;

            internal DeploymentCard(JObject? record, string role, int index, bool isOperator)
            {
                Role = role;
                Index = index;
                AddToClassList("preset-hand-card");
                var face = new VisualElement { pickingMode = PickingMode.Ignore };
                face.AddToClassList("deployment-card-face");
                Add(face);
                var content = new PresetPreviewContent(record, isOperator, false);
                var inst = record?["inst"] as JObject;
                var skin = Text(record, "skinId");
                var template = Text(record, "tmplId");
                var entry = isOperator ? OperatorPresetCatalog.LoadBundled().Find(content.Id) : null;
                var professionKey = !isOperator ? "TOKEN" : entry?.Profession ?? "";
                if (entry != null && template.Length > 0)
                {
                    foreach (var form in ((JArray)OperatorPresetCatalog.LoadBundled().Details(entry)["forms"]!).OfType<JObject>())
                        if ((string?)form["id"] == template) { professionKey = (string?)form["profession"] ?? professionKey; break; }
                }
                var hidden = record?["hidden"]?.Type == JTokenType.Boolean && (bool)record["hidden"]!;
                tooltip = $"{content.Name} · {content.Id}\n{content.Detail}" +
                    (skin.Length == 0 ? "" : "\n" + skin) + (hidden ? "\n隐藏卡牌" : "");
                var shadow = DeploymentCardArt.Image("shadow", "deployment-card-shadow");
                shadow.AddToClassList("deployment-card-full");
                face.Add(shadow);
                portrait = new Image { name = "deployment-card-portrait", pickingMode = PickingMode.Ignore,
                    scaleMode = isOperator ? ScaleMode.ScaleAndCrop : ScaleMode.ScaleToFit };
                Texture2D? texture;
                Rect uv;
                var found = isOperator
                    ? DeploymentCardArt.TryGet(skin, out texture, out uv) ||
                      PrtsObjectView.TryGetOperatorAvatar(template, out texture, out uv) ||
                      PrtsObjectView.TryGetOperatorAvatar(content.Id, out texture, out uv)
                    : DeploymentCardArt.TryGet(content.Id, out texture, out uv) ||
                      PrtsObjectView.TryGetIcon(content.Id, out texture, out uv);
                portrait.image = texture;
                if (found) portrait.uv = uv;
                face.Add(portrait);
                rarity = DeploymentCardArt.Image(entry?.Rarity switch { 6 => "rarity_6", 5 => "rarity_5", _ => "rarity_1_4" },
                    "deployment-card-rarity");
                face.Add(rarity);
                profession = DeploymentCardArt.Image(professionKey, "deployment-card-profession", ScaleMode.ScaleToFit);
                face.Add(profession);
                var phase = OperatorPresetCatalog.ParseTier(inst?["phase"]?.ToString());
                if (isOperator && phase is >= 1 and <= 3)
                {
                    elite = DeploymentCardArt.Image("elite_" + phase, "deployment-card-elite", ScaleMode.ScaleToFit);
                    face.Add(elite);
                }
                if (!isOperator && record?["initialCnt"]?.Type == JTokenType.Integer)
                {
                    quantityBack = DeploymentCardArt.Image("quantity", "deployment-card-quantity-back");
                    face.Add(quantityBack);
                    quantity = new Label($"×{record["initialCnt"]}") { name = "deployment-card-quantity", pickingMode = PickingMode.Ignore };
                    quantity.AddToClassList("deployment-card-quantity");
                    face.Add(quantity);
                }
                var frame = DeploymentCardArt.Frame();
                frame.AddToClassList("deployment-card-full");
                face.Add(frame);
                focus = DeploymentCardArt.Image("selected", "deployment-card-selected");
                focus.AddToClassList("deployment-card-full");
                face.Add(focus);
                Selected(false);
            }

            internal void Resize(float width, float height)
            {
                var scale = height / AuthoredHeight;
                lift = 20 * scale;
                style.width = width;
                style.height = height;
                style.marginTop = lift + 3;
                Place(portrait, 0, 0, width, height - 7 * scale);
                Place(rarity, 0, height - 47 * scale, width, 47 * scale);
                Place(profession, width * .5f - 44 * scale, scale, 44 * scale, 45 * scale);
                if (elite != null) Place(elite, 0, height - 43 * scale, 50 * scale, 35 * scale);
                if (quantityBack != null) Place(quantityBack, 0, height - 54 * scale, width, 54 * scale);
                if (quantity != null)
                {
                    Place(quantity, 0, height - 41 * scale, width - 5 * scale, 37 * scale);
                    quantity.style.fontSize = Mathf.Max(11, 31 * scale);
                }
                style.translate = new Translate(0, selected ? -lift : 0, 0);
            }

            internal void Selected(bool active)
            {
                selected = active;
                focus.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
                // Move the button with the card so its clip and hit area follow the selection.
                style.translate = new Translate(0, active ? -lift : 0, 0);
            }

            private static string Text(JObject? record, string key) =>
                record?[key]?.Type == JTokenType.String ? (string)record[key]! : "";

            private static void Place(VisualElement element, float left, float top, float width, float height)
            {
                element.style.position = Position.Absolute;
                element.style.left = left;
                element.style.top = top;
                element.style.width = width;
                element.style.height = height;
            }
        }
    }
}
