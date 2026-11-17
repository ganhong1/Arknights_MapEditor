#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Arknights.MapEditor.Editor
{
    // Editor-only visual projection. Never writes LevelData or implies gameplay support.
    public sealed class SkeletonStyle
    {
        public Color TopColor { get; }
        public Color SideColor { get; }
        public int Pattern { get; }
        public string Label { get; }
        public int Symbol { get; }
        public bool Known { get; }
        private static JObject? definitions;
        private static Dictionary<string,int> symbols = new Dictionary<string,int>(StringComparer.Ordinal);
        public const string DefinitionPath = "Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/Catalog/skeleton-definitions.json";
        public const string SymbolAtlasPath = "Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/Catalog/skeleton-terrain-symbols.png";
        public static int DefinitionCount { get { EnsureDefinitions(); return ((JObject)definitions!["tiles"]!).Count; } }
        private SkeletonStyle(Color top, Color side, int pattern, string label, int symbol, bool known)
        { TopColor=top; SideColor=side; Pattern=pattern; Label=label; Symbol=symbol; Known=known; }
        private static void EnsureDefinitions()
        {
            if (definitions != null) return;
            var asset=AssetDatabase.LoadAssetAtPath<TextAsset>(DefinitionPath);
            if(asset==null) throw new InvalidOperationException("Skeleton visual definitions unavailable");
            var data=JObject.Parse(asset.text);
            if(data.Value<int>("version")!=1 || !(data["tiles"] is JObject)) throw new FormatException("Invalid skeleton definitions");
            if (!(data["symbols"] is JArray atlas) || atlas.Count > 64)
                throw new FormatException("Invalid terrain symbol atlas");
            var slots = new Dictionary<string,int>(StringComparer.Ordinal);
            foreach (var entry in atlas)
            {
                var name = ReadString(entry);
                if (name == null || string.IsNullOrWhiteSpace(name) || !slots.TryAdd(name, slots.Count + 1))
                    throw new FormatException("Invalid terrain symbol slot: " + name);
            }
            symbols = slots;
            definitions=data;
        }
        private static string? ReadString(JToken? token) => token?.Type == JTokenType.String ? token.Value<string>() : null;
        // Match the Timeline LevelData adapter: only unique numeric entries
        // on an otherwise valid board can contribute a display parameter.
        private static double? BlackboardNumber(JToken? board, string key)
        {
            if (!(board is JArray entries)) return null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            double? result = null;
            foreach (var entry in entries)
            {
                if (!(entry is JObject pair)) return null;
                var name = ReadString(pair["key"]);
                if (name == null || name.Length == 0 || !seen.Add(name)) return null;
                var raw = pair["value"];
                var number = raw?.Type == JTokenType.Integer || raw?.Type == JTokenType.Float
                    ? raw.Value<double>() : double.NaN;
                var label = ReadString(pair["valueStr"] ?? pair["value_str"]);
                var finite = !double.IsNaN(number) && !double.IsInfinity(number);
                if (!finite && string.IsNullOrEmpty(label)) return null;
                if (name == key && finite && string.IsNullOrEmpty(label)) result = number;
            }
            return result;
        }

        public static readonly Color DeployableFloor = new Color32(119, 119, 119, 255);
        public static readonly Color NonDeployableFloor = new Color32(53, 53, 53, 255);
        public const float BlockTop = .4f;
        public const float BoxTop = 1f;
        public const float FenceTop = .25f;
        public const float RecessTop = -1f;
        public const float TrenchTop = -.4f;

        private static JObject? Definition(string key)
        {
            EnsureDefinitions();
            return definitions!["tiles"]![key] as JObject;
        }

        private static bool IsHigh(JObject tile) =>
            ReadString(tile["heightType"]) == "HIGHLAND" ||
            (tile["heightType"]?.Type == JTokenType.Integer && tile["heightType"]?.ToString() == "1");

        public static float SurfaceHeight(JObject tile)
        {
            var key = ReadString(tile["tileKey"]) ?? "";
            switch (Definition(key)?.Value<string>("geometry_as") ?? key)
            {
                case "tile_start": case "tile_end": case "tile_flystart":
                case "tile_start_cooperate": case "tile_end_cooperate": case "tile_crate":
                    return BoxTop;
                case "tile_fence": case "tile_fence_bound": return FenceTop;
                case "tile_hole": case "tile_deepwater": return RecessTop;
                case "tile_trench": return TrenchTop;
                case "tile_passable_wall": case "tile_passable_wall_forbidden": return BlockTop;
                case "tile_forbidden": case "tile_empty": return 0f;
                default: return IsHigh(tile) ? BlockTop : 0f;
            }
        }

        public static float ContentHeight(JObject tile)
        {
            var key = ReadString(tile["tileKey"]) ?? "";
            var definition = Definition(key);
            var surface = SurfaceHeight(tile);
            if (surface == BoxTop) return BoxTop;
            if (surface == BlockTop && definition?["content_on_top"]?.Value<bool>() == true)
                return BlockTop;
            return 0f;
        }

        public static bool ShowsDeploymentBorder(JObject tile)
        {
            if (ReadString(tile["tileKey"]) == "tile_crate") return false;
            var token = tile["buildableType"];
            var name = ReadString(token);
            if (name == "MELEE" || name == "RANGED" || name == "ALL") return true;
            return token?.Type == JTokenType.Integer &&
                (token.ToString() == "1" || token.ToString() == "2" || token.ToString() == "3");
        }

        public static SkeletonStyle Resolve(JObject tile)
        {
            if(tile==null) throw new ArgumentNullException(nameof(tile));
            var key = ReadString(tile["tileKey"]) ?? "";
            var definition = Definition(key);
            var visual = definition?.Value<string>("render_as") ?? key;
            var inherited = visual == key ? null : Definition(visual);
            var effect = definition?.Value<string>("effect") ?? inherited?.Value<string>("effect") ?? "";
            var high = SurfaceHeight(tile) == BlockTop;
            var deploy = ShowsDeploymentBorder(tile);
            var top = high ? (Color)(deploy ? new Color32(194,194,194,255) : new Color32(16,16,16,255)) :
                (deploy ? DeployableFloor : NonDeployableFloor);
            if (!deploy && (key == "tile_floor" || key == "tile_stairs" || key == "tile_passable_wall_forbidden"))
                top = new Color(.32f,.32f,.32f);
            int pattern=0;
            if(definition==null) pattern=17;
            else switch(visual) {
                case "tile_forbidden": case "tile_empty":
                    if (!deploy) top = new Color32(19,19,19,255);
                    pattern=1; break;
                case "tile_start": case "tile_flystart": top=new Color(.17f,.085f,.10f); pattern=2; break;
                case "tile_end": top=new Color(.075f,.13f,.17f); pattern=3; break;
                case "tile_hole": pattern=4; break;
                case "tile_telin": case "tile_telout": pattern=11; break;
                case "tile_fence": pattern=12; break;
                case "tile_crate": top=new Color(.23f,.24f,.26f); pattern=13; break;
                case "tile_stairs": pattern=14; break;
                case "tile_aircraft": pattern=18; break;
                case "tile_grvtybtn": pattern=24; break;
            }
            switch(effect) {
                case "effect_grass": pattern=5; break;
                case "effect_healing": pattern=6; break;
                case "effect_buff": pattern=7; break;
                case "effect_infection": pattern=8; break;
                case "effect_volcano": pattern=28; break;
                case "effect_corrosion": pattern=22; break;
                case "effect_volspread": pattern=23; break;
                case "effect_deepwater": pattern=9; break;
                case "effect_shallowwater": pattern=29; break;
                case "effect_ice": pattern=10; break;
                case "effect_yinyang_road": case "effect_yinyang_wall":
                    var state = BlackboardNumber(tile["blackboard"], "dynamic");
                    pattern = state == null ? 0 : state == 0 ? 25 : 26;
                    break;
                case "effect_yinyang_switch": pattern=15; break;
                case "effect_poison": pattern=16; break;
                case "effect_smog": pattern=27; break;
                case "effect_reed": pattern=20; break;
                case "effect_mire": pattern=19; break;
                case "effect_ristar_road": pattern=21; break;
            }
            if (definition?["editor_visual"] is JObject editorVisual)
            {
                pattern = EditorPattern(editorVisual.Value<string>("motif"));
                if (ColorUtility.TryParseHtmlString(editorVisual.Value<string>("tint"), out var tint))
                    top = Color.Lerp(top, tint, deploy ? .54f : .43f);
            }
            var label = definition?.Value<string>("content") ?? inherited?.Value<string>("content") ?? "?";
            if (visual == "tile_grvtybtn")
            {
                var direction = BlackboardNumber(tile["blackboard"], "source_direction");
                label = direction == 0 ? "G↑" : direction == 2 ? "G↓" : "G↕";
            }
            var symbolName = ReadString(definition?["symbol"]);
            var symbol = 0;
            if (symbolName != null && !symbols.TryGetValue(symbolName, out symbol))
                throw new FormatException("Unknown terrain symbol: " + symbolName);
            return new SkeletonStyle(top, new Color(top.r*.48f,top.g*.48f,top.b*.48f),pattern,label,symbol,definition!=null);
        }

        private static int EditorPattern(string? motif)
        {
            switch (motif)
            {
                case "corners": return 33;
                case "waves": return 34;
                case "rings": return 35;
                case "grid": return 36;
                case "diagonal": return 37;
                case "crosshatch": return 38;
                case "link": return 39;
                case "chevrons": return 40;
                case "dots": return 41;
                case "cracks": return 42;
                case "stamp": return 43;
                default: throw new FormatException("Unknown editor terrain motif: " + motif);
            }
        }
    }
}
