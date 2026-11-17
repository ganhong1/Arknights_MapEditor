#nullable enable
using System;
using System.Collections.Generic;
using Arknights.MapEditor.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Arknights.MapEditor.Editor
{
    /// <summary>Derived schematic chunks only. No document, scene or asset ownership outside this instance.</summary>
    public sealed class ChunkedMapView : IDisposable
    {
        public const int ChunkSize = 16;
        private const float Inset = .02f;
        private readonly Scene scene;
        private readonly Dictionary<Vector2Int, GameObject> chunks = new Dictionary<Vector2Int, GameObject>();
        private static readonly int[] TransparentFaceOrder = { 2, 3, 4, 0, 1 };
        private static readonly float[] SurfacePlanes =
            { 0f, SkeletonStyle.BlockTop, SkeletonStyle.BoxTop, SkeletonStyle.FenceTop, SkeletonStyle.RecessTop, SkeletonStyle.TrenchTop };
        private Material? material;
        private Material? translucentMaterial;
        private GameObject? backdrop;
        private Material? backdropMaterial;
        private int width, height;
        public int ChunkCount => chunks.Count;
        internal void SetBackdropVisible(bool visible)
        {
            if (backdrop != null && backdrop.activeSelf != visible) backdrop.SetActive(visible);
        }
        public ChunkedMapView(Scene scene) { this.scene = scene; }
        public Mesh MeshAt(int row, int col) => chunks[new Vector2Int(col, row)].GetComponent<MeshFilter>().sharedMesh;

        public void Update(LevelDocument document, IEnumerable<Vector2Int>? changedCells)
        {
            if (material == null)
            {
                var shader = Shader.Find("Hidden/ArknightsMapEditor/Skeleton");
                if (shader == null) throw new InvalidOperationException("Map skeleton preview shader unavailable.");
                var terrainSymbols = AssetDatabase.LoadAssetAtPath<Texture2D>(SkeletonStyle.SymbolAtlasPath);
                if (terrainSymbols == null) throw new InvalidOperationException("Terrain symbol atlas unavailable.");
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                material.SetTexture("_TerrainSymbols", terrainSymbols);
                material.SetInt("_Cull", (int)CullMode.Off);
                material.SetInt("_ZWrite", 1);
                translucentMaterial = new Material(material) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 3000 };
                translucentMaterial.SetInt("_ZWrite", 0);
            }
            if (backdrop == null)
            {
                backdrop = new GameObject("PRTSMap backdrop") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(backdrop, scene);
                var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                mesh.vertices = new[] { new Vector3(-2048,-1.1f,-2048), new Vector3(-2048,-1.1f,2048),
                    new Vector3(2048,-1.1f,2048), new Vector3(2048,-1.1f,-2048) };
                var background = new Color(.094f,.094f,.094f);
                mesh.colors = new[] { background, background, background, background };
                mesh.uv2 = new[] { new Vector2(30,1), new Vector2(30,1), new Vector2(30,1), new Vector2(30,1) };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                backdrop.AddComponent<MeshFilter>().sharedMesh = mesh;
                backdropMaterial = new Material(material) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 1999 };
                backdrop.AddComponent<MeshRenderer>().sharedMaterial = backdropMaterial;
            }
            var dirty = new HashSet<Vector2Int>();
            if (width != document.Width || height != document.Height || changedCells == null)
            {
                ClearChunks(); width = document.Width; height = document.Height;
                for (var r = 0; r < height; r += ChunkSize)
                for (var c = 0; c < width; c += ChunkSize) dirty.Add(new Vector2Int(c / ChunkSize, r / ChunkSize));
            }
            else foreach (var cell in changedCells)
            {
                if (cell.x < 0 || cell.y < 0 || cell.x >= width || cell.y >= height) throw new ArgumentOutOfRangeException(nameof(changedCells));
                dirty.Add(new Vector2Int(cell.x / ChunkSize, cell.y / ChunkSize));
            }
            foreach (var key in dirty)
            {
                var root = new GameObject("Map chunk " + key) { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    SceneManager.MoveGameObjectToScene(root, scene);
                    var mesh = Build(document, key);
                    root.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = root.AddComponent<MeshRenderer>();
                    if (mesh.subMeshCount == 2)
                        renderer.sharedMaterials = new[] { material!, translucentMaterial! };
                    else renderer.sharedMaterial = material;
                }
                catch { Release(root); throw; }
                if (chunks.TryGetValue(key, out var old)) Release(old);
                chunks[key] = root;
            }
        }

        public static bool TryPick(LevelDocument document, Ray ray, out Vector2Int cell)
        {
            cell = default;
            var best = float.PositiveInfinity;
            foreach (var y in SurfacePlanes)
            {
                if (!new Plane(Vector3.up, new Vector3(0, y, 0)).Raycast(ray, out var distance) || distance >= best) continue;
                var point = ray.GetPoint(distance);
                var col = Mathf.FloorToInt(point.x); var row = Mathf.FloorToInt(point.z);
                if (col < 0 || row < 0 || col >= document.Width || row >= document.Height) continue;
                if (!WithinCell(point.x, col) || !WithinCell(point.z, row)) continue;
                if (Height(document, row, col) != y) continue;
                best = distance; cell = new Vector2Int(col, row);
            }
            // Each inset cell has four independent side planes. Intersect only the
            // O(width + height) plane candidates, not every cell and no Colliders.
            if (Mathf.Abs(ray.direction.x) > 1e-7f)
                for (var col = 0; col < document.Width; col++)
                    for (var edge = 0; edge < 2; edge++)
                    {
                        var x = col + (edge == 0 ? Inset : 1 - Inset);
                        var distance = (x - ray.origin.x) / ray.direction.x;
                        if (distance < 0 || distance >= best) continue;
                        var point = ray.GetPoint(distance); var row = Mathf.FloorToInt(point.z);
                        if (row < 0 || row >= document.Height || !WithinCell(point.z, row) || !OnSide(point.y, Height(document, row, col))) continue;
                        best = distance; cell = new Vector2Int(col, row);
                    }
            if (Mathf.Abs(ray.direction.z) > 1e-7f)
                for (var row = 0; row < document.Height; row++)
                    for (var edge = 0; edge < 2; edge++)
                    {
                        var z = row + (edge == 0 ? Inset : 1 - Inset);
                        var distance = (z - ray.origin.z) / ray.direction.z;
                        if (distance < 0 || distance >= best) continue;
                        var point = ray.GetPoint(distance); var col = Mathf.FloorToInt(point.x);
                        if (col < 0 || col >= document.Width || !WithinCell(point.x, col) || !OnSide(point.y, Height(document, row, col))) continue;
                        best = distance; cell = new Vector2Int(col, row);
                    }
            return !float.IsPositiveInfinity(best);
        }

        private static bool OnSide(float y, float surface) =>
            surface != 0f && y >= Mathf.Min(0f, surface) && y <= Mathf.Max(0f, surface);

        private static bool WithinCell(float coordinate, int index) => coordinate >= index + Inset && coordinate <= index + 1 - Inset;

        private static float Height(LevelDocument document, int row, int col) =>
            SkeletonStyle.SurfaceHeight(document.GetCell(row, col));

        private static Mesh Build(LevelDocument document, Vector2Int chunk)
        {
            var startCol = chunk.x * ChunkSize; var startRow = chunk.y * ChunkSize;
            var cols = Math.Min(ChunkSize, document.Width - startCol); var rows = Math.Min(ChunkSize, document.Height - startRow);
            var vertices = new Vector3[rows * cols * 20]; var colors = new Color[vertices.Length]; var indices = new int[rows * cols * 30];
            var uv = new Vector2[vertices.Length]; var uv2 = new Vector2[vertices.Length];
            var symbols = new Vector2[vertices.Length];
            for (var r = 0; r < rows; r++) for (var c = 0; c < cols; c++)
            {
                var row = startRow + r; var col = startCol + c; var tile = document.GetCell(row, col);
                var y = SkeletonStyle.SurfaceHeight(tile);
                var style = SkeletonStyle.Resolve(tile);
                var x0 = col + Inset; var x1 = col + 1 - Inset; var z0 = row + Inset; var z1 = row + 1 - Inset;
                var face = (r * cols + c) * 5;
                Quad(face, new Vector3(x0,y,z0), new Vector3(x0,y,z1), new Vector3(x1,y,z1), new Vector3(x1,y,z0), style.TopColor, new Vector2(style.Pattern, SkeletonStyle.ShowsDeploymentBorder(tile) ? 2 : 1), vertices,colors,uv,uv2,indices);
                for (var corner = 0; corner < 4; corner++)
                    symbols[face * 4 + corner] = new Vector2(style.Symbol, 0);
                Quad(face+1, new Vector3(x0,0f,z0), new Vector3(x0,y,z0), new Vector3(x1,y,z0), new Vector3(x1,0f,z0), style.SideColor, new Vector2(style.Pattern,0), vertices,colors,uv,uv2,indices);
                Quad(face+2, new Vector3(x1,0f,z1), new Vector3(x1,y,z1), new Vector3(x0,y,z1), new Vector3(x0,0f,z1), style.SideColor, new Vector2(style.Pattern,0), vertices,colors,uv,uv2,indices);
                Quad(face+3, new Vector3(x0,0f,z1), new Vector3(x0,y,z1), new Vector3(x0,y,z0), new Vector3(x0,0f,z0), style.SideColor, new Vector2(style.Pattern,0), vertices,colors,uv,uv2,indices);
                Quad(face+4, new Vector3(x1,0f,z0), new Vector3(x1,y,z0), new Vector3(x1,y,z1), new Vector3(x1,0f,z1), style.SideColor, new Vector2(style.Pattern,0), vertices,colors,uv,uv2,indices);
            }
            // Ground plates render below raised boxes and above recessed faces.
            var groundFaces = new List<int>();
            var raisedCount = 0;
            for (var cell = 0; cell < rows * cols; cell++)
                if (vertices[cell * 20].y == SkeletonStyle.BoxTop)
                {
                    groundFaces.Add(cell * 20);
                    raisedCount++;
                }
            for (var cell = 0; cell < rows * cols; cell++)
                if (vertices[cell * 20].y == SkeletonStyle.RecessTop) groundFaces.Add(cell * 20);
            if (groundFaces.Count > 0)
            {
                var start = vertices.Length;
                var count = indices.Length;
                Array.Resize(ref vertices, start + groundFaces.Count * 4);
                Array.Resize(ref colors, vertices.Length);
                Array.Resize(ref uv, vertices.Length);
                Array.Resize(ref uv2, vertices.Length);
                Array.Resize(ref symbols, vertices.Length);
                Array.Resize(ref indices, count + groundFaces.Count * 6);
                Array.Copy(indices, 0, indices, raisedCount * 6, count);
                for (var i = 0; i < groundFaces.Count; i++)
                {
                    var face = groundFaces[i];
                    var vertex = start + i * 4;
                    for (var corner = 0; corner < 4; corner++)
                    {
                        var position = vertices[face + corner];
                        vertices[vertex + corner] = new Vector3(position.x, 0f, position.z);
                        colors[vertex + corner] = uv2[face].y > 1.5f ? SkeletonStyle.DeployableFloor : SkeletonStyle.NonDeployableFloor;
                        uv[vertex + corner] = uv[face + corner];
                        var pattern = i < raisedCount ? 0f : uv2[face].x == 9f ? 32f : 31f;
                        uv2[vertex + corner] = new Vector2(pattern, uv2[face].y);
                    }
                    var triangle = i * 6 + (i < raisedCount ? 0 : count);
                    indices[triangle] = vertex; indices[triangle + 1] = vertex + 1; indices[triangle + 2] = vertex + 2;
                    indices[triangle + 3] = vertex; indices[triangle + 4] = vertex + 2; indices[triangle + 5] = vertex + 3;
                }
            }
            var mesh = new Mesh { name = "Map chunk mesh", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices; mesh.colors = colors; mesh.uv = uv; mesh.uv2 = uv2;
            mesh.uv3 = symbols;
            var opaque = new List<int>(indices.Length);
            var transparent = new List<int>();
            for (var i = 0; i < indices.Length; i += 6)
            {
                var pattern = uv2[indices[i]].x;
                if (pattern == 2 || pattern == 3 || pattern == 31 || pattern == 32) continue;
                for (var j = 0; j < 6; j++) opaque.Add(indices[i + j]);
            }
            // The default PRTS camera looks from the south. Composite far
            // rows first, and the near face of each transparent box last.
            for (var row = rows - 1; row >= 0; row--)
            for (var col = 0; col < cols; col++)
            {
                var first = (row * cols + col) * 20;
                var pattern = uv2[first].x;
                if (pattern != 2 && pattern != 3) continue;
                var triangle = raisedCount * 6 + (first / 4) * 6;
                foreach (var face in TransparentFaceOrder)
                    for (var j = 0; j < 6; j++) transparent.Add(indices[triangle + face * 6 + j]);
            }
            for (var i = raisedCount; i < groundFaces.Count; i++)
                for (var j = 0; j < 6; j++) transparent.Add(indices[rows * cols * 30 + i * 6 + j]);
            mesh.subMeshCount = transparent.Count == 0 ? 1 : 2;
            mesh.SetTriangles(opaque, 0);
            if (transparent.Count > 0) mesh.SetTriangles(transparent, 1);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        private static void Quad(int face, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, Vector2 pattern,
            Vector3[] vertices, Color[] colors, Vector2[] uv, Vector2[] uv2, int[] indices)
        {
            var start = face * 4; var triangle = face * 6;
            vertices[start] = a; vertices[start+1] = b; vertices[start+2] = c; vertices[start+3] = d;
            uv[start] = new Vector2(0,0); uv[start+1] = new Vector2(0,1); uv[start+2] = new Vector2(1,1); uv[start+3] = new Vector2(1,0);
            for (var i = 0; i < 4; i++) { colors[start+i] = color; uv2[start+i] = pattern; }
            indices[triangle] = start; indices[triangle+1] = start+1; indices[triangle+2] = start+2;
            indices[triangle+3] = start; indices[triangle+4] = start+2; indices[triangle+5] = start+3;
        }

        private static void Release(GameObject root)
        {
            if (root == null) return;
            var filter = root.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) UnityEngine.Object.DestroyImmediate(filter.sharedMesh);
            UnityEngine.Object.DestroyImmediate(root);
        }
        private void ClearChunks() { foreach (var chunk in chunks.Values) Release(chunk); chunks.Clear(); }
        public void Dispose() { ClearChunks(); if (backdrop != null) Release(backdrop); backdrop = null; if (backdropMaterial != null) UnityEngine.Object.DestroyImmediate(backdropMaterial); backdropMaterial = null; if (translucentMaterial != null) UnityEngine.Object.DestroyImmediate(translucentMaterial); translucentMaterial = null; if (material != null) UnityEngine.Object.DestroyImmediate(material); material = null; width = height = 0; }
    }
}
