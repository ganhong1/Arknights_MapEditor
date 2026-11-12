#nullable enable
using System;
using System.Collections.Generic;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Arknights.MapEditor.Editor
{
    /// <summary>Native, atlas-backed projection. Cards are not instantiated; hidden entries remain annotations.</summary>
    public sealed class PrtsObjectView : IDisposable
    {
        public const string CatalogRoot = "Packages/io.github.lingrs-llm-researchteam.mapeditor/Editor/Catalog/";
        private const string OperatorAtlasFile = "preset-operator-atlas.png";
        private const string OperatorExtraAtlasFile = "preset-operator-extra-atlas.png";
        private enum IconAtlas { Original, Supplement, Operator, OperatorExtra }
        private static JObject? icons;
        private static JObject? supplements;
        private static JObject? operators;
        private static JObject? displayNames;
        private readonly Scene scene;
        private GameObject? root;
        private Mesh? mesh;
        private Material? originalMaterial;
        private Material? supplementMaterial;
        private Material? operatorMaterial;
        private Material? operatorExtraMaterial;
        public int VisibleCount { get; private set; }
        internal int MissingIconCount => MissingIconMarkers.Count;
        internal readonly List<Vector3> MissingIconMarkers = new List<Vector3>();
        public readonly List<Vector3> UnresolvedMarkers = new List<Vector3>();
        public PrtsObjectView(Scene scene) { this.scene = scene; }
        public static int IconCount { get { LoadCatalog(); return icons!.Count; } }
        public static string DisplayName(string id)
        {
            LoadCatalog();
            if (displayNames == null)
            {
                var names = AssetDatabase.LoadAssetAtPath<TextAsset>(CatalogRoot + "display-names.json");
                displayNames = names == null ? new JObject() : JObject.Parse(names.text);
            }
            var name = icons![id]?["name"]?.Value<string>() ?? supplements![id]?["name"]?.Value<string>() ??
                displayNames[id]?.Value<string>();
            if (!id.StartsWith("tile_", StringComparison.Ordinal)) return name ?? id;
            name = name?.Trim();
            return string.IsNullOrEmpty(name) || name == "说明表未列名" ? id : name!;
        }
        public static bool TryGetRect(string id, out Rect rect) => TryGetVisual(id, out rect, out _);

        internal static bool TryGetOperatorAvatar(string id, out Texture2D? texture, out Rect rect)
        {
            LoadCatalog();
            var entry = operators![id];
            texture = null;
            if (!TryReadRect(entry, out rect)) return false;
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(CatalogRoot +
                ((string?)entry?["atlas"] == "extra" ? OperatorExtraAtlasFile : OperatorAtlasFile));
            return texture != null;
        }

        internal static bool TryGetIcon(string id, out Texture2D? texture, out Rect rect)
        {
            texture = null;
            if (!TryGetVisual(id, out rect, out var atlas)) return false;
            var file = atlas switch
            {
                IconAtlas.Original => "prts-object-atlas.png",
                IconAtlas.Supplement => "prts-object-supplement-atlas.png",
                IconAtlas.OperatorExtra => OperatorExtraAtlasFile,
                _ => OperatorAtlasFile
            };
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(CatalogRoot + file);
            return texture != null;
        }

        public static string VisualSource(string id)
        {
            LoadCatalog();
            if (icons![id] != null) return "PRTSMap 图标";
            return supplements![id]?["label"]?.Value<string>() ??
                (operators![id] == null ? "未收录美术" : "2.7.81 原生干员头像");
        }

        private static bool TryGetVisual(string id, out Rect rect, out IconAtlas atlas)
        {
            LoadCatalog();
            var entry = icons![id];
            var supplement = supplements![id];
            var actor = operators![id];
            atlas = entry != null ? IconAtlas.Original : supplement != null ? IconAtlas.Supplement :
                (string?)actor?["atlas"] == "extra" ? IconAtlas.OperatorExtra : IconAtlas.Operator;
            return TryReadRect(entry ?? supplement ?? actor, out rect);
        }

        private static bool TryReadRect(JToken? entry, out Rect rect)
        {
            if (entry?["rect"] is JArray values && values.Count == 4)
            { rect = new Rect((float)values[0], (float)values[1], (float)values[2], (float)values[3]); return true; }
            rect = default; return false;
        }
        private static void LoadCatalog()
        {
            if (icons != null) return;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(CatalogRoot + "prts-object-icons.json");
            if (asset == null) throw new InvalidOperationException("PRTS object catalog unavailable.");
            var original = JObject.Parse(asset.text)["icons"] as JObject ?? throw new FormatException("Invalid PRTS catalog.");
            var supplement = AssetDatabase.LoadAssetAtPath<TextAsset>(CatalogRoot + "prts-object-supplement.json");
            supplements = supplement == null ? new JObject() :
                JObject.Parse(supplement.text)["icons"] as JObject ?? throw new FormatException("Invalid object supplement.");
            var operatorAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(CatalogRoot + "preset-operator-icons.json");
            operators = operatorAsset == null ? new JObject() :
                JObject.Parse(operatorAsset.text)["icons"] as JObject ?? throw new FormatException("Invalid operator avatar catalog.");
            icons = original;
        }
        public void Update(LevelDocument document)
        {
            Dispose();
            try { Build(document); }
            catch { Dispose(); throw; }
        }
        private void Build(LevelDocument document)
        {
            var vertices = new List<Vector3>(); var uv = new List<Vector2>();
            var local = new List<Vector2>();
            var originalIndices = new List<int>();
            var supplementIndices = new List<int>();
            var operatorIndices = new List<int>();
            var operatorExtraIndices = new List<int>();
            foreach (var scope in new[] { "predefines", "hardPredefines" })
            foreach (var kind in new[] { "characterInsts", "tokenInsts" })
            {
                if (!(document.GetSection(scope) is JObject data) || !(data[kind] is JArray entries)) continue;
                foreach (var entry in entries)
                {
                    if (!(entry is JObject item) || !(item["position"] is JObject position) ||
                        position["row"]?.Type != JTokenType.Integer || position["col"]?.Type != JTokenType.Integer) continue;
                    if (!int.TryParse(position["row"]!.ToString(), out var row) ||
                        !int.TryParse(position["col"]!.ToString(), out var col)) continue;
                    if (row < 0 || col < 0 || row >= document.Height || col >= document.Width) continue;
                    var center = new Vector3(col + .5f, SkeletonStyle.SurfaceHeight(document.GetCell(row, col)) + .025f, row + .5f);
                    var idToken = (item["inst"] as JObject)?["characterKey"];
                    var id = idToken?.Type == JTokenType.String ? idToken.Value<string>()! : "";
                    if (item["hidden"]?.Type == JTokenType.Boolean && item.Value<bool>("hidden"))
                    { UnresolvedMarkers.Add(center); continue; }
                    if (!TryGetVisual(id, out var rect, out var atlas))
                    { MissingIconMarkers.Add(center); UnresolvedMarkers.Add(center); continue; }
                    var start = vertices.Count;
                    // 40 CSS pixels on a 50 pixel cell pitch. Horizontal plane follows native camera projection.
                    vertices.Add(center + new Vector3(-.4f, 0, -.4f)); vertices.Add(center + new Vector3(-.4f, 0, .4f));
                    vertices.Add(center + new Vector3(.4f, 0, .4f)); vertices.Add(center + new Vector3(.4f, 0, -.4f));
                    uv.Add(new Vector2(rect.xMin, rect.yMin)); uv.Add(new Vector2(rect.xMin, rect.yMax));
                    uv.Add(new Vector2(rect.xMax, rect.yMax)); uv.Add(new Vector2(rect.xMax, rect.yMin));
                    local.Add(new Vector2(0,0)); local.Add(new Vector2(0,1)); local.Add(new Vector2(1,1)); local.Add(new Vector2(1,0));
                    var indices = atlas switch
                    {
                        IconAtlas.Original => originalIndices,
                        IconAtlas.Supplement => supplementIndices,
                        IconAtlas.OperatorExtra => operatorExtraIndices,
                        _ => operatorIndices
                    };
                    indices.AddRange(new[] { start, start+1, start+2, start, start+2, start+3 }); VisibleCount++;
                }
            }
            if (vertices.Count == 0) return;
            var shader = Shader.Find("Hidden/ArknightsMapEditor/PrtsObject");
            if (shader == null) throw new InvalidOperationException("PRTS native icon shader unavailable.");
            var materials = new List<Material>(4);
            mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, local);
            mesh.subMeshCount = (originalIndices.Count > 0 ? 1 : 0) + (supplementIndices.Count > 0 ? 1 : 0) +
                (operatorIndices.Count > 0 ? 1 : 0) + (operatorExtraIndices.Count > 0 ? 1 : 0);
            if (originalIndices.Count > 0)
            {
                originalMaterial = CreateMaterial(shader, "prts-object-atlas.png");
                mesh.SetTriangles(originalIndices, materials.Count);
                materials.Add(originalMaterial);
            }
            if (supplementIndices.Count > 0)
            {
                supplementMaterial = CreateMaterial(shader, "prts-object-supplement-atlas.png");
                mesh.SetTriangles(supplementIndices, materials.Count);
                materials.Add(supplementMaterial);
            }
            if (operatorIndices.Count > 0)
            {
                operatorMaterial = CreateMaterial(shader, OperatorAtlasFile);
                mesh.SetTriangles(operatorIndices, materials.Count);
                materials.Add(operatorMaterial);
            }
            if (operatorExtraIndices.Count > 0)
            {
                operatorExtraMaterial = CreateMaterial(shader, OperatorExtraAtlasFile);
                mesh.SetTriangles(operatorExtraIndices, materials.Count);
                materials.Add(operatorExtraMaterial);
            }
            mesh.RecalculateBounds();
            root = new GameObject("PRTSMap object icons") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<MeshFilter>().sharedMesh = mesh; root.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
        }
        private static Material CreateMaterial(Shader shader, string filename)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(CatalogRoot + filename);
            if (texture == null) throw new InvalidOperationException("Object atlas unavailable: " + filename);
            return new Material(shader) { hideFlags = HideFlags.HideAndDontSave, mainTexture = texture };
        }

        public void Dispose()
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            if (originalMaterial != null) UnityEngine.Object.DestroyImmediate(originalMaterial);
            if (supplementMaterial != null) UnityEngine.Object.DestroyImmediate(supplementMaterial);
            if (operatorMaterial != null) UnityEngine.Object.DestroyImmediate(operatorMaterial);
            if (operatorExtraMaterial != null) UnityEngine.Object.DestroyImmediate(operatorExtraMaterial);
            root = null; mesh = null; originalMaterial = null; supplementMaterial = null; operatorMaterial = null;
            operatorExtraMaterial = null;
            VisibleCount = 0; MissingIconMarkers.Clear(); UnresolvedMarkers.Clear();
        }
    }
}
