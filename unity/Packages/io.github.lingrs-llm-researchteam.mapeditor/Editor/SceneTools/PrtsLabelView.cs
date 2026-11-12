#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Arknights.MapEditor.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Arknights.MapEditor.Editor
{
    /// <summary>Glyph meshes lie on the map plane, matching CSS perspective rather than screen-facing GUI labels.</summary>
    public sealed class PrtsLabelView : IDisposable
    {
        private readonly Scene scene;
        private readonly Font font;
        private GameObject? root;
        private Mesh? mesh;
        private Material? material;
        private LevelDocument? document;
        private bool coordinates, flat, dirty;
        private const int FontSize = 26;
        private const float PixelScale = .01f; // 26 raster px represent the verified 13 CSS px / 50 px pitch.
        public PrtsLabelView(Scene scene)
        {
            this.scene = scene;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Arial" }, FontSize);
            font.hideFlags = HideFlags.HideAndDontSave;
            Font.textureRebuilt += FontRebuilt;
        }
        private void FontRebuilt(Font changed) { if (changed == font) dirty = true; }
        public void Update(LevelDocument value, bool showCoordinates, bool orthographic)
        {
            if (document == value && coordinates == showCoordinates && flat == orthographic && !dirty) return;
            document = value; coordinates = showCoordinates; flat = orthographic;
            Build();
        }
        private void Build()
        {
            if (document == null) return;
            var labels = new List<(string Text, Vector3 Center, Color Color, float Scale)>();
            var characters = new StringBuilder();
            for (var row = 0; row < document.Height; row++)
            for (var col = 0; col < document.Width; col++)
            {
                var tile = document.GetCell(row, col);
                var label = SkeletonStyle.Resolve(tile).Label;
                var height = flat ? .02f : SkeletonStyle.ContentHeight(tile) + .02f;
                var center = new Vector3(col + .5f, height, row + .5f);
                if (!string.IsNullOrEmpty(label))
                {
                    labels.Add((label, center, new Color(1,1,1,label == "hole" && !flat ? .1f : .8f), 1f));
                    characters.Append(label);
                }
                if (coordinates)
                {
                    var text = col + "," + row;
                    labels.Add((text, center + (flat ? new Vector3(.216f,0,-.24f) : Vector3.zero), new Color(1,1,1,flat ? .5f : .2f), flat ? .6f : 1f));
                    characters.Append(text);
                }
            }
            foreach (var scope in new[] { "predefines", "hardPredefines" })
            foreach (var kind in new[] { "tokenInsts", "characterInsts" })
            {
                if (!(document.GetSection(scope) is JObject data) || !(data[kind] is JArray entries)) continue;
                foreach (var entry in entries)
                {
                    if (!(entry is JObject item) || !(item["position"] is JObject position)) continue;
                    var id = (item["inst"] as JObject)?["characterKey"]?.ToString() ?? "";
                    if (PrtsObjectView.TryGetRect(id, out _) ||
                        !int.TryParse(position["row"]?.ToString(), out var row) || !int.TryParse(position["col"]?.ToString(), out var col) ||
                        row < 0 || col < 0 || row >= document.Height || col >= document.Width) continue;
                    var center = new Vector3(col+.5f, SkeletonStyle.SurfaceHeight(document.GetCell(row,col))+.03f, row+.5f);
                    labels.Add(("?", center, new Color(1,.8f,.25f,1), 1.25f)); characters.Append('?');
                }
            }
            if (characters.Length == 0)
            {
                mesh?.Clear();
                dirty = false;
                return;
            }
            font.RequestCharactersInTexture(characters.ToString(), FontSize, FontStyle.Normal);
            if (font.material == null) { dirty = true; return; }
            var vertices = new List<Vector3>(); var uv = new List<Vector2>();
            var colors = new List<Color>(); var triangles = new List<int>();
            foreach (var label in labels)
            {
                var width = 0f;
                foreach (var character in label.Text) if (font.GetCharacterInfo(character, out var info, FontSize)) width += info.advance;
                var offset = -width / 2;
                foreach (var character in label.Text)
                {
                    if (!font.GetCharacterInfo(character, out var info, FontSize)) continue;
                    var n = vertices.Count; var scale = PixelScale * label.Scale;
                    // Baseline relative to CSS centered line box, projected on X/Z.
                    var baseline = -FontSize * .35f;
                    vertices.Add(label.Center + new Vector3(offset+info.minX,0,baseline+info.minY)*scale);
                    vertices.Add(label.Center + new Vector3(offset+info.minX,0,baseline+info.maxY)*scale);
                    vertices.Add(label.Center + new Vector3(offset+info.maxX,0,baseline+info.maxY)*scale);
                    vertices.Add(label.Center + new Vector3(offset+info.maxX,0,baseline+info.minY)*scale);
                    uv.Add(info.uvBottomLeft); uv.Add(info.uvTopLeft); uv.Add(info.uvTopRight); uv.Add(info.uvBottomRight);
                    for (var i=0;i<4;i++) colors.Add(label.Color);
                    triangles.AddRange(new[] {n,n+1,n+2,n,n+2,n+3}); offset += info.advance;
                }
            }
            if (root == null)
            {
                var shader = Shader.Find("Hidden/ArknightsMapEditor/PrtsLabel");
                if (shader == null) throw new InvalidOperationException("Native PRTS label shader unavailable.");
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                root = new GameObject("PRTSMap planar labels") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root,scene);
                root.AddComponent<MeshFilter>().sharedMesh = mesh; root.AddComponent<MeshRenderer>().sharedMaterial = material;
            }
            material!.mainTexture = font.material.mainTexture;
            mesh!.Clear(); mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetColors(colors); mesh.SetTriangles(triangles,0); mesh.RecalculateBounds();
            dirty = false;
        }
        public void Dispose()
        {
            Font.textureRebuilt -= FontRebuilt;
            if(root!=null) UnityEngine.Object.DestroyImmediate(root);
            if(mesh!=null) UnityEngine.Object.DestroyImmediate(mesh);
            if(material!=null) UnityEngine.Object.DestroyImmediate(material);
            if(font!=null) UnityEngine.Object.DestroyImmediate(font);
            root=null;mesh=null;material=null;
        }
    }
}
