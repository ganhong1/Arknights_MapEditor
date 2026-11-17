#nullable enable
using System;
using System.Linq;
using Arknights.MapEditor.Catalog;
using Arknights.MapEditor.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Arknights.MapEditor.Editor
{
    internal sealed class OfflineArtPreviewScene : IDisposable
    {
        internal const float Spacing = 4f;
        internal const float Rise = 0.24f;
        private readonly PreviewRenderUtility preview = new PreviewRenderUtility();
        private readonly Material surface;
        private readonly System.Collections.Generic.List<Material> materials = new System.Collections.Generic.List<Material>();

        internal Camera Camera => preview.camera;
        internal int RenderedAssets { get; private set; }

        internal OfflineArtPreviewScene()
        {
            try { surface = NewMaterial("Unlit/Color", new Color(0.20f, 0.24f, 0.29f)); }
            catch { Dispose(); throw; }
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(0.063f, 0.078f, 0.10f);
            preview.camera.orthographic = true;
            preview.camera.farClipPlane = 100f;
            preview.ambientColor = new Color(0.55f, 0.55f, 0.55f);
            preview.lights[0].intensity = 1.4f;
            preview.lights[0].transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            preview.lights[1].intensity = 0.8f;
            preview.lights[1].transform.rotation = Quaternion.Euler(25f, 145f, 0f);
        }

        internal static Vector3 Position(int row, int column) => new Vector3(
            (column - (OfflineArtGallery.Columns - 1) * 0.5f) * Spacing,
            row * Rise, (row - (OfflineArtGallery.Rows - 1) * 0.5f) * Spacing);

        internal void ConfigureCamera(float aspect, bool gallery, bool overhead = false)
        {
            var focus = gallery ? new Vector3(0, 0.65f, 0) : new Vector3(0, 0.4f, 0);
            var direction = overhead ? Vector3.up : new Vector3(0.48f, 1f, -1f).normalized;
            Camera.transform.position = focus + direction * 27f;
            Camera.transform.rotation = Quaternion.LookRotation(-direction, overhead ? Vector3.forward : Vector3.up);
            Camera.orthographicSize = gallery ? Mathf.Max(8.2f, 8.1f / Mathf.Max(aspect, 0.25f)) : Mathf.Max(2.65f, 2.65f / Mathf.Max(aspect, 0.25f));
        }

        internal void AddTile(Vector3 position, string name)
        {
            var tile = Primitive(PrimitiveType.Cube, name, position + Vector3.down * 0.13f, new Vector3(3.25f, 0.27f, 3.25f));
            tile.GetComponent<Renderer>().sharedMaterial = surface;
        }

        internal bool AddAsset(Object asset, Material? modelMaterial, Vector3 position, string name, bool particleFrame)
        {
            switch (asset)
            {
                case Texture texture:
                    var quad = Primitive(PrimitiveType.Quad, name, position + Vector3.up * 0.026f, Vector3.one * 2.9f);
                    quad.transform.rotation = Quaternion.Euler(90, 0, 0);
                    var unlit = NewMaterial("Unlit/Texture", Color.white);
                    unlit.mainTexture = texture;
                    quad.GetComponent<Renderer>().sharedMaterial = unlit;
                    break;
                case Material material:
                    var swatch = Primitive(PrimitiveType.Cube, name, position + Vector3.up * 0.145f, new Vector3(2.85f, 0.28f, 2.85f));
                    swatch.GetComponent<Renderer>().sharedMaterial = material;
                    break;
                case GameObject prefab:
                    if (!AddModel(prefab, modelMaterial, position, name, particleFrame)) return false;
                    break;
                case Mesh mesh:
                    var shape = new GameObject(name);
                    shape.AddComponent<MeshFilter>().sharedMesh = mesh;
                    shape.AddComponent<MeshRenderer>().sharedMaterial = modelMaterial ?? surface;
                    preview.AddSingleGO(shape);
                    Fit(shape, mesh.bounds, position);
                    break;
                default:
                    return false;
            }

            RenderedAssets++;
            return true;
        }

        private bool AddModel(GameObject prefab, Material? material, Vector3 position, string name, bool particleFrame)
        {
            var root = new GameObject(name);
            preview.AddSingleGO(root);
            var instance = Object.Instantiate(prefab, root.transform, false);
            instance.SetActive(true);
            foreach (var audio in root.GetComponentsInChildren<AudioSource>(true)) audio.enabled = false;
            var renderers = root.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy).ToArray();
            if (material != null)
                foreach (var renderer in renderers)
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray();

            var systems = particleFrame ? root.GetComponentsInChildren<ParticleSystem>(false) : Array.Empty<ParticleSystem>();
            if (particleFrame)
            {
                for (var index = 0; index < systems.Length; index++)
                {
                    systems[index].useAutoRandomSeed = false;
                    systems[index].randomSeed = (uint)(103 + index);
                }
                SimulateFrame(systems);
                renderers = renderers.Where(renderer => !(renderer is ParticleSystemRenderer) ||
                    renderer.GetComponent<ParticleSystem>().particleCount > 0).ToArray();
            }

            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                Fit(root, bounds, position);
                if (particleFrame) SimulateFrame(systems);
            }
            if (renderers.Length > 0) return true;
            Object.DestroyImmediate(root);
            return false;
        }

        private static void SimulateFrame(ParticleSystem[] systems)
        {
            foreach (var system in systems.Where(system => system.transform.parent == null ||
                system.transform.parent.GetComponentInParent<ParticleSystem>() == null))
                system.Simulate(1.2f, true, true, true);
        }
        private static void Fit(GameObject root, Bounds bounds, Vector3 position)
        {
            var size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (size < 0.001f || float.IsNaN(size) || float.IsInfinity(size)) return;
            var scale = 2.95f / size;
            root.transform.localScale = Vector3.one * scale;
            root.transform.position = position - new Vector3(bounds.center.x, bounds.min.y - 0.04f / scale, bounds.center.z) * scale;
        }

        private GameObject Primitive(PrimitiveType type, string name, Vector3 position, Vector3 scale)
        {
            var instance = GameObject.CreatePrimitive(type);
            instance.name = name;
            preview.AddSingleGO(instance);
            Object.DestroyImmediate(instance.GetComponent<Collider>());
            instance.transform.position = position;
            instance.transform.localScale = scale;
            return instance;
        }

        internal Material NewMaterial(string shaderName, Color color)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("Unity 缺少预览着色器 " + shaderName);
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (material.HasProperty("_Color")) material.color = color;
            materials.Add(material);
            return material;
        }

        internal void BuildReferenceMap(LevelDocument document, Material material)
        {
            void Draw(string name, Vector3 position, Vector3 scale, Color color)
            {
                var piece = Primitive(PrimitiveType.Cube, name, position, scale);
                var renderer = piece.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                var tint = new MaterialPropertyBlock();
                tint.SetColor("_Color", color);
                renderer.SetPropertyBlock(tint);
            }

            Draw("Map bed", new Vector3(0, -.45f, 0),
                new Vector3(document.Width + .6f, .42f, document.Height + .6f), new Color(.14f, .21f, .26f));

            for (var row = 0; row < document.Height; row++)
            for (var column = 0; column < document.Width; column++)
            {
                var cell = document.GetCell(row, column);
                var key = (string?)cell["tileKey"] ?? "";
                var height = cell["heightType"]?.ToString();
                var high = height is "HIGHLAND" or "1";
                var elevation = high ? .55f : 0f;
                var position = new Vector3(column - (document.Width - 1) * .5f, elevation - .16f, row - (document.Height - 1) * .5f);
                var color = key switch
                {
                    "tile_start" or "tile_flystart" => new Color(.96f, .3f, .18f),
                    "tile_end" => new Color(.22f, .62f, .98f),
                    "tile_hole" or "tile_forbidden" => new Color(.19f, .29f, .34f),
                    _ => high ? new Color(.68f, .79f, .76f) : new Color(.37f, .51f, .55f)
                };
                Draw(key, position, new Vector3(.94f, .32f, .94f), color);
                if (key is "tile_start" or "tile_end")
                    Draw("Gate marker", position + Vector3.up * .52f, new Vector3(.56f, .78f, .56f), color);
            }
        }

        internal void AddReferenceSphere(Material material)
        {
            var sphere = Primitive(PrimitiveType.Sphere, "Environment reflection", new Vector3(0, 2.1f, 0), Vector3.one * 3.2f);
            sphere.GetComponent<Renderer>().sharedMaterial = material;
        }

        internal Texture Render(Rect rectangle, bool gallery, bool overhead)
        {
            ConfigureCamera(rectangle.width / rectangle.height, gallery, overhead);
            return RenderConfigured(rectangle);
        }

        internal Texture RenderConfigured(Rect rectangle)
        {
            preview.BeginPreview(rectangle, GUIStyle.none);
            Texture output;
            try { preview.Render(); }
            finally { output = preview.EndPreview(); }
            return output;
        }

        public void Dispose()
        {
            preview.Cleanup();
            foreach (var material in materials) Object.DestroyImmediate(material);
            materials.Clear();
        }
    }
}
